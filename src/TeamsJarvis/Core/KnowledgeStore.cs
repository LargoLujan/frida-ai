using Microsoft.Data.Sqlite;

namespace TeamsJarvis.Core;

/// <summary>
/// Almacén vectorial local (SQLite) para RAG y búsqueda semántica. Guarda fragmentos de
/// texto (cifrados con DPAPI) junto con su embedding. La búsqueda calcula la similitud de
/// coseno en memoria: a escala personal (un vault + conversaciones) es de sobra rápido y
/// evita depender de extensiones vectoriales nativas.
/// </summary>
public sealed class KnowledgeStore
{
    private readonly string _connectionString = $"Data Source={AppPaths.DbPath}";

    public KnowledgeStore()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS knowledge_chunks (
                id          TEXT PRIMARY KEY,   -- kind|ref|chunkIndex
                kind        TEXT NOT NULL,      -- 'vault' | 'chat'
                source_ref  TEXT NOT NULL,      -- ruta de archivo o chat_id
                title       TEXT NOT NULL,
                chunk_index INTEGER NOT NULL,
                text_enc    TEXT NOT NULL,
                embedding   BLOB NOT NULL,
                model       TEXT NOT NULL,
                hash        TEXT NOT NULL,
                updated_at  TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_kc_kind ON knowledge_chunks(kind);
            CREATE INDEX IF NOT EXISTS ix_kc_ref ON knowledge_chunks(source_ref);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>¿Está ya indexado este id con este mismo hash de contenido? (para saltar reindexado)</summary>
    public bool IsIndexed(string id, string hash)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM knowledge_chunks WHERE id = $id AND hash = $h LIMIT 1;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$h", hash);
        return cmd.ExecuteScalar() is not null;
    }

    /// <summary>Id determinista de un fragmento (kind|ref|localId).</summary>
    public static string MakeId(string kind, string sourceRef, string localId) => $"{kind}|{sourceRef}|{localId}";

    public void Upsert(string id, string kind, string sourceRef, string title, int chunkIndex,
        string text, float[] embedding, string model)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO knowledge_chunks (id, kind, source_ref, title, chunk_index, text_enc, embedding, model, hash, updated_at)
            VALUES ($id, $kind, $ref, $title, $ci, $text, $emb, $model, $hash, $now)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title, text_enc = excluded.text_enc, embedding = excluded.embedding,
                model = excluded.model, hash = excluded.hash, updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$ref", sourceRef);
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$ci", chunkIndex);
        cmd.Parameters.AddWithValue("$text", CryptoHelper.Protect(text));
        cmd.Parameters.AddWithValue("$emb", TextChunker.ToBytes(embedding));
        cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$hash", TextChunker.HashContent(text));
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Elimina los fragmentos de una fuente cuyo chunk_index sea &gt;= el dado (limpia sobras al reindexar un archivo más corto).</summary>
    public void DeleteChunksFrom(string kind, string sourceRef, int fromChunkIndex)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM knowledge_chunks WHERE kind = $kind AND source_ref = $ref AND chunk_index >= $ci;";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$ref", sourceRef);
        cmd.Parameters.AddWithValue("$ci", fromChunkIndex);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Elimina todo lo indexado de una fuente (p. ej. un archivo borrado del vault).</summary>
    public void DeleteSource(string kind, string sourceRef)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM knowledge_chunks WHERE kind = $kind AND source_ref = $ref;";
        cmd.Parameters.AddWithValue("$kind", kind);
        cmd.Parameters.AddWithValue("$ref", sourceRef);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Rutas (source_ref) ya indexadas de un tipo, para detectar archivos borrados.</summary>
    public HashSet<string> GetIndexedSources(string kind)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT source_ref FROM knowledge_chunks WHERE kind = $kind;";
        cmd.Parameters.AddWithValue("$kind", kind);
        using var reader = cmd.ExecuteReader();
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (reader.Read()) set.Add(reader.GetString(0));
        return set;
    }

    public long ChunkCount(string? kind = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = kind is null
            ? "SELECT COUNT(*) FROM knowledge_chunks;"
            : "SELECT COUNT(*) FROM knowledge_chunks WHERE kind = $kind;";
        if (kind is not null) cmd.Parameters.AddWithValue("$kind", kind);
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Recupera los <paramref name="topK"/> fragmentos más parecidos al vector de consulta.
    /// Carga los embeddings del tipo pedido y calcula coseno en memoria.
    /// </summary>
    public List<KnowledgeHit> Search(float[] queryEmbedding, int topK, string? kind = null, double minScore = 0.0)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = kind is null
            ? "SELECT kind, source_ref, title, text_enc, embedding FROM knowledge_chunks;"
            : "SELECT kind, source_ref, title, text_enc, embedding FROM knowledge_chunks WHERE kind = $kind;";
        if (kind is not null) cmd.Parameters.AddWithValue("$kind", kind);

        var scored = new List<(double Score, string Kind, string Ref, string Title, string TextEnc)>();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var emb = TextChunker.FromBytes((byte[])reader["embedding"]);
                var score = TextChunker.Cosine(queryEmbedding, emb);
                if (score < minScore) continue;
                scored.Add((score, reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        return scored
            .OrderByDescending(x => x.Score)
            .Take(topK)
            .Select(x => new KnowledgeHit(x.Kind, x.Ref, x.Title, CryptoHelper.Unprotect(x.TextEnc), x.Score))
            .ToList();
    }
}
