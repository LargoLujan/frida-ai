using Microsoft.Data.Sqlite;

namespace TeamsJarvis.Core;

/// <summary>
/// Almacena los mensajes de las conversaciones (texto cifrado con DPAPI) y permite
/// búsquedas complejas tratando personas y chats como un grafo derivado por SQL:
/// una persona → los chats en los que participa → las personas con las que coincide.
/// El texto se cifra en reposo; la búsqueda por palabra clave descifra en memoria un
/// conjunto acotado de candidatos (suficiente para uso personal).
/// </summary>
public sealed class ConversationGraphStore
{
    private readonly string _connectionString = $"Data Source={AppPaths.DbPath}";

    public ConversationGraphStore()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS messages (
                id          TEXT PRIMARY KEY,
                chat_id     TEXT NOT NULL,
                chat_name   TEXT NOT NULL,
                author_id   TEXT NOT NULL,
                author_name TEXT NOT NULL,
                is_me       INTEGER NOT NULL,
                created_at  TEXT NOT NULL,
                text_enc    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_messages_author ON messages(author_id);
            CREATE INDEX IF NOT EXISTS ix_messages_chat ON messages(chat_id);
            CREATE INDEX IF NOT EXISTS ix_messages_created ON messages(created_at);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    /// <summary>Indexa (INSERT OR IGNORE) los mensajes de un chat. Dedup por id de mensaje.</summary>
    public int IndexMessages(string chatId, string chatName, IEnumerable<ChatMessageDto> messages)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR IGNORE INTO messages (id, chat_id, chat_name, author_id, author_name, is_me, created_at, text_enc)
            VALUES ($id, $chatId, $chatName, $authorId, $authorName, $isMe, $created, $text);
            """;
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
        var pChat = cmd.CreateParameter(); pChat.ParameterName = "$chatId"; cmd.Parameters.Add(pChat);
        var pChatName = cmd.CreateParameter(); pChatName.ParameterName = "$chatName"; cmd.Parameters.Add(pChatName);
        var pAid = cmd.CreateParameter(); pAid.ParameterName = "$authorId"; cmd.Parameters.Add(pAid);
        var pAname = cmd.CreateParameter(); pAname.ParameterName = "$authorName"; cmd.Parameters.Add(pAname);
        var pMe = cmd.CreateParameter(); pMe.ParameterName = "$isMe"; cmd.Parameters.Add(pMe);
        var pCreated = cmd.CreateParameter(); pCreated.ParameterName = "$created"; cmd.Parameters.Add(pCreated);
        var pText = cmd.CreateParameter(); pText.ParameterName = "$text"; cmd.Parameters.Add(pText);

        var count = 0;
        foreach (var m in messages)
        {
            if (string.IsNullOrWhiteSpace(m.Text)) continue;
            pId.Value = m.Id;
            pChat.Value = chatId;
            pChatName.Value = chatName;
            pAid.Value = string.IsNullOrEmpty(m.AuthorId) ? m.AuthorName : m.AuthorId;
            pAname.Value = m.AuthorName;
            pMe.Value = m.IsMe ? 1 : 0;
            pCreated.Value = m.CreatedAt.ToString("O");
            pText.Value = CryptoHelper.Protect(m.Text);
            count += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return count;
    }

    /// <summary>Personas (interlocutores, excluye al usuario) ordenadas por actividad.</summary>
    public List<GraphPerson> GetPeople()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT author_id, author_name, COUNT(*) AS msgs, COUNT(DISTINCT chat_id) AS chats
            FROM messages WHERE is_me = 0
            GROUP BY author_id ORDER BY msgs DESC;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<GraphPerson>();
        while (reader.Read())
            result.Add(new GraphPerson(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        return result;
    }

    /// <summary>Chats en los que participa una persona (aristas persona↔chat).</summary>
    public List<GraphChatLink> GetChatsForPerson(string personId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT chat_id, chat_name, COUNT(*), MAX(created_at)
            FROM messages WHERE author_id = $p
            GROUP BY chat_id ORDER BY 3 DESC;
            """;
        cmd.Parameters.AddWithValue("$p", personId);
        using var reader = cmd.ExecuteReader();
        var result = new List<GraphChatLink>();
        while (reader.Read())
            result.Add(new GraphChatLink(reader.GetString(0), reader.GetString(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
        return result;
    }

    /// <summary>Personas que coinciden en algún chat con la dada (aristas persona↔persona).</summary>
    public List<GraphPerson> GetCoParticipants(string personId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT m.author_id, m.author_name, COUNT(*) AS msgs, COUNT(DISTINCT m.chat_id) AS chats
            FROM messages m
            WHERE m.is_me = 0 AND m.author_id <> $p
              AND m.chat_id IN (SELECT DISTINCT chat_id FROM messages WHERE author_id = $p)
            GROUP BY m.author_id ORDER BY chats DESC, msgs DESC;
            """;
        cmd.Parameters.AddWithValue("$p", personId);
        using var reader = cmd.ExecuteReader();
        var result = new List<GraphPerson>();
        while (reader.Read())
            result.Add(new GraphPerson(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetInt32(3)));
        return result;
    }

    /// <summary>
    /// Busca mensajes por palabra(s) clave, opcionalmente filtrando por persona y/o chat.
    /// Acota los candidatos por SQL y descifra en memoria para el filtro de texto.
    /// </summary>
    public List<StoredMessage> Search(string? keyword, string? personId, string? chatId, int limit = 100)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        var where = new List<string>();
        if (!string.IsNullOrWhiteSpace(personId)) { where.Add("author_id = $p"); cmd.Parameters.AddWithValue("$p", personId); }
        if (!string.IsNullOrWhiteSpace(chatId)) { where.Add("chat_id = $c"); cmd.Parameters.AddWithValue("$c", chatId); }
        var whereSql = where.Count > 0 ? "WHERE " + string.Join(" AND ", where) : "";

        // Acotar el conjunto candidato para no descifrar toda la base.
        cmd.CommandText = $"""
            SELECT id, chat_id, chat_name, author_name, is_me, created_at, text_enc
            FROM messages {whereSql} ORDER BY created_at DESC LIMIT 3000;
            """;

        var keywords = (keyword ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var reader = cmd.ExecuteReader();
        var result = new List<StoredMessage>();
        while (reader.Read() && result.Count < limit)
        {
            var text = CryptoHelper.Unprotect(reader.GetString(6));
            if (keywords.Length > 0 && !keywords.All(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                continue;
            result.Add(new StoredMessage(
                reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetInt32(4) == 1, DateTimeOffset.Parse(reader.GetString(5)), text));
        }
        return result;
    }

    /// <summary>Mensajes recientes de un chat en orden cronológico (para mostrar la conversación).</summary>
    public List<StoredMessage> GetRecentForChat(string chatId, int limit = 50)
    {
        var list = Search(null, null, chatId, limit);
        list.Reverse(); // Search devuelve descendente; queremos ascendente
        return list;
    }

    /// <summary>Mensajes recientes (cualquier chat) para indexar en el almacén semántico.</summary>
    public List<StoredMessage> GetMessagesForIndexing(int limit = 3000) => Search(null, null, null, limit);

    /// <summary>Chats con mensajes almacenados (para reconstruir la memoria de cada uno).</summary>
    public List<(string ChatId, string ChatName, int MessageCount)> GetChats()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT chat_id, MAX(chat_name), COUNT(*) FROM messages
            GROUP BY chat_id ORDER BY 3 DESC;
            """;
        using var reader = cmd.ExecuteReader();
        var result = new List<(string, string, int)>();
        while (reader.Read())
            result.Add((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)));
        return result;
    }

    /// <summary>Número total de mensajes indexados (para diagnóstico/UX).</summary>
    public long MessageCount()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM messages;";
        return (long)cmd.ExecuteScalar()!;
    }
}
