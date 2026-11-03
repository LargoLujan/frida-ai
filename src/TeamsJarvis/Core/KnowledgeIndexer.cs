using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TeamsJarvis.Core;

/// <summary>
/// Indexa el conocimiento local (vault de notas Markdown) en el almacén vectorial usando
/// embeddings locales, y recupera los fragmentos relevantes para una consulta (RAG).
/// Todo el trabajo de embeddings corre en tu CPU vía Ollama: coste 0.
/// </summary>
public sealed class KnowledgeIndexer
{
    private static readonly string[] Extensions = [".md", ".markdown", ".txt"];

    /// <summary>Separador interno para empaquetar metadatos de un mensaje en el título (autor · chat · fecha).</summary>
    public const char MetaSep = '\u001F';

    private readonly KnowledgeStore _store;
    private readonly EmbeddingService _embeddings;
    private readonly ConversationGraphStore _graph;
    private readonly JarvisOptions _options;
    private readonly ILogger<KnowledgeIndexer> _logger;

    public KnowledgeIndexer(KnowledgeStore store, EmbeddingService embeddings, ConversationGraphStore graph,
        IOptions<JarvisOptions> options, ILogger<KnowledgeIndexer> logger)
    {
        _store = store;
        _embeddings = embeddings;
        _graph = graph;
        _options = options.Value;
        _logger = logger;
    }

    private string VaultDir => string.IsNullOrWhiteSpace(_options.KnowledgeDir)
        ? AppPaths.DefaultKnowledgeDir
        : _options.KnowledgeDir;

    /// <summary>
    /// Reindexa el vault: trocea cada nota, embebe los fragmentos que cambiaron y elimina
    /// lo que ya no exista. Devuelve cuántos fragmentos se (re)generaron.
    /// </summary>
    public async Task<int> IndexVaultAsync(CancellationToken ct = default)
    {
        var dir = VaultDir;
        if (!Directory.Exists(dir)) return 0;

        var files = Directory.EnumerateFiles(dir, "*.*", SearchOption.AllDirectories)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var embedded = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var rel = Path.GetRelativePath(dir, file);
            seen.Add(rel);

            string text;
            try { text = await File.ReadAllTextAsync(file, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudo leer la nota {File}", rel); continue; }

            var chunks = TextChunker.Chunk(text);
            var title = Path.GetFileNameWithoutExtension(file);

            foreach (var chunk in chunks)
            {
                var id = KnowledgeStore.MakeId("vault", rel, chunk.Index.ToString());
                if (_store.IsIndexed(id, TextChunker.HashContent(chunk.Text))) continue;

                var vector = await _embeddings.EmbedAsync(chunk.Text, ct);
                if (vector is null) return embedded; // Ollama caído: no seguir insistiendo
                _store.Upsert(id, "vault", rel, title, chunk.Index, chunk.Text, vector, _embeddings.Model);
                embedded++;
            }

            // Limpiar fragmentos sobrantes si la nota se acortó.
            _store.DeleteChunksFrom("vault", rel, chunks.Count);
        }

        // Eliminar del índice las notas que ya no existen.
        foreach (var stale in _store.GetIndexedSources("vault").Where(s => !seen.Contains(s)))
            _store.DeleteSource("vault", stale);

        if (embedded > 0) _logger.LogInformation("Vault indexado: {N} fragmentos actualizados.", embedded);
        return embedded;
    }

    /// <summary>
    /// Indexa los mensajes almacenados de las conversaciones para búsqueda semántica.
    /// Incremental: salta los que ya están indexados con el mismo contenido. Empaqueta autor,
    /// chat y fecha en el título para poder mostrarlos en los resultados.
    /// </summary>
    public async Task<int> IndexConversationsAsync(int maxMessages = 3000, CancellationToken ct = default)
    {
        if (!_options.IndexConversations) return 0;
        var embedded = 0;
        foreach (var m in _graph.GetMessagesForIndexing(maxMessages))
        {
            ct.ThrowIfCancellationRequested();
            if (m.Text.Trim().Length < 15) continue; // saltar mensajes triviales

            var id = KnowledgeStore.MakeId("chat", m.ChatId, m.Id);
            if (_store.IsIndexed(id, TextChunker.HashContent(m.Text))) continue;

            var vector = await _embeddings.EmbedAsync(m.Text, ct);
            if (vector is null) return embedded; // embeddings no disponibles: parar

            var author = m.IsMe ? "Yo" : m.AuthorName;
            var title = string.Join(MetaSep, author, m.ChatName, m.CreatedAt.ToString("O"));
            _store.Upsert(id, "chat", m.ChatId, title, 0, m.Text, vector, _embeddings.Model);
            embedded++;
        }
        if (embedded > 0) _logger.LogInformation("Conversaciones indexadas: {N} mensajes.", embedded);
        return embedded;
    }

    /// <summary>
    /// Recupera los fragmentos más relevantes para una consulta. Devuelve lista vacía si el
    /// RAG está desactivado o si los embeddings no están disponibles (fallback sin coste).
    /// </summary>
    public async Task<List<KnowledgeHit>> RetrieveAsync(
        string query, int? topK = null, string? kind = null, CancellationToken ct = default)
    {
        if (!_options.RagEnabled || string.IsNullOrWhiteSpace(query)) return [];
        var q = await _embeddings.EmbedAsync(query, ct);
        if (q is null) return [];
        return _store.Search(q, topK ?? _options.RagTopK, kind, minScore: 0.25);
    }

    /// <summary>
    /// Búsqueda semántica explícita (desde la UI). A diferencia de RetrieveAsync, no depende de
    /// RagEnabled: el usuario puede buscar por significado aunque no use RAG en las sugerencias.
    /// Devuelve lista vacía si los embeddings no están disponibles.
    /// </summary>
    public async Task<List<KnowledgeHit>> SemanticSearchAsync(
        string query, int topK = 40, string? kind = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        var q = await _embeddings.EmbedAsync(query, ct);
        if (q is null) return [];
        return _store.Search(q, topK, kind, minScore: 0.2);
    }

    /// <summary>¿Están operativos los embeddings locales? (para que la UI sepa si ofrecer búsqueda semántica)</summary>
    public Task<bool> EmbeddingsAvailableAsync(CancellationToken ct = default) => _embeddings.IsAvailableAsync(ct);
}
