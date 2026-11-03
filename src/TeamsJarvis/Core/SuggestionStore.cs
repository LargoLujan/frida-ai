using Microsoft.Data.Sqlite;

namespace TeamsJarvis.Core;

/// <summary>Persistencia SQLite: chats monitorizados, estado por chat y sugerencias.</summary>
public sealed class SuggestionStore
{
    private readonly string _connectionString = $"Data Source={AppPaths.DbPath}";

    /// <summary>Se dispara al guardar una nueva sugerencia (para refrescar la UI).</summary>
    public event Action<Suggestion>? SuggestionAdded;

    public SuggestionStore()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS monitored_chats (
                chat_id      TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                enabled      INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS chat_state (
                chat_id        TEXT PRIMARY KEY,
                last_processed TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS suggestions (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                chat_id         TEXT NOT NULL,
                chat_name       TEXT NOT NULL,
                created_at      TEXT NOT NULL,
                reply_to        TEXT NOT NULL DEFAULT '',
                trigger_summary TEXT NOT NULL,
                suggested_reply TEXT NOT NULL,
                confidence      REAL NOT NULL,
                sources         TEXT NOT NULL DEFAULT ''
            );
            CREATE TABLE IF NOT EXISTS chat_memory (
                chat_id    TEXT PRIMARY KEY,
                updated_at TEXT NOT NULL,
                summary    TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS usage_log (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                created_at    TEXT NOT NULL,
                chat_id       TEXT NOT NULL,
                provider      TEXT NOT NULL,
                model         TEXT NOT NULL,
                input_tokens  INTEGER NOT NULL,
                output_tokens INTEGER NOT NULL,
                cache_read    INTEGER NOT NULL,
                cache_write   INTEGER NOT NULL,
                cost_usd      REAL NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ---- Chats monitorizados -------------------------------------------------

    public List<MonitoredChat> GetMonitoredChats(bool enabledOnly = false)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT chat_id, display_name, enabled FROM monitored_chats"
                          + (enabledOnly ? " WHERE enabled = 1" : "");
        using var reader = cmd.ExecuteReader();
        var result = new List<MonitoredChat>();
        while (reader.Read())
            result.Add(new MonitoredChat(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) == 1));
        return result;
    }

    public void UpsertMonitoredChat(MonitoredChat chat)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO monitored_chats (chat_id, display_name, enabled)
            VALUES ($id, $name, $enabled)
            ON CONFLICT(chat_id) DO UPDATE SET display_name = $name, enabled = $enabled;
            """;
        cmd.Parameters.AddWithValue("$id", chat.ChatId);
        cmd.Parameters.AddWithValue("$name", chat.DisplayName);
        cmd.Parameters.AddWithValue("$enabled", chat.Enabled ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    // ---- Estado por chat (último mensaje procesado) --------------------------

    public DateTimeOffset? GetLastProcessed(string chatId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT last_processed FROM chat_state WHERE chat_id = $id";
        cmd.Parameters.AddWithValue("$id", chatId);
        var value = cmd.ExecuteScalar() as string;
        return value is null ? null : DateTimeOffset.Parse(value);
    }

    public void SetLastProcessed(string chatId, DateTimeOffset timestamp)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_state (chat_id, last_processed) VALUES ($id, $ts)
            ON CONFLICT(chat_id) DO UPDATE SET last_processed = $ts;
            """;
        cmd.Parameters.AddWithValue("$id", chatId);
        cmd.Parameters.AddWithValue("$ts", timestamp.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    // ---- Memoria por conversación (cifrada con DPAPI) ------------------------

    public string GetChatMemory(string chatId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT summary FROM chat_memory WHERE chat_id = $id";
        cmd.Parameters.AddWithValue("$id", chatId);
        var value = cmd.ExecuteScalar() as string;
        return value is null ? "" : CryptoHelper.Unprotect(value);
    }

    public void SetChatMemory(string chatId, string summary)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO chat_memory (chat_id, updated_at, summary) VALUES ($id, $ts, $summary)
            ON CONFLICT(chat_id) DO UPDATE SET updated_at = $ts, summary = $summary;
            """;
        cmd.Parameters.AddWithValue("$id", chatId);
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$summary", CryptoHelper.Protect(summary));
        cmd.ExecuteNonQuery();
    }

    // ---- Registro de uso / coste ----------------------------------------------

    public void LogUsage(string chatId, string provider, string model, TokenUsage usage, double costUsd)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO usage_log (created_at, chat_id, provider, model, input_tokens, output_tokens, cache_read, cache_write, cost_usd)
            VALUES ($ts, $chat, $provider, $model, $in, $out, $cr, $cw, $cost);
            """;
        cmd.Parameters.AddWithValue("$ts", DateTimeOffset.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$chat", chatId);
        cmd.Parameters.AddWithValue("$provider", provider);
        cmd.Parameters.AddWithValue("$model", model);
        cmd.Parameters.AddWithValue("$in", usage.InputTokens);
        cmd.Parameters.AddWithValue("$out", usage.OutputTokens);
        cmd.Parameters.AddWithValue("$cr", usage.CacheReadTokens);
        cmd.Parameters.AddWithValue("$cw", usage.CacheWriteTokens);
        cmd.Parameters.AddWithValue("$cost", costUsd);
        cmd.ExecuteNonQuery();
    }

    /// <summary>Resumen de coste/tokens/llamadas desde un instante dado (o todo si es null).</summary>
    public UsageSummary GetUsageSince(DateTimeOffset? since)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT COUNT(*), COALESCE(SUM(cost_usd),0), COALESCE(SUM(input_tokens),0),
                   COALESCE(SUM(output_tokens),0), COALESCE(SUM(cache_read),0), COALESCE(SUM(cache_write),0)
            FROM usage_log {(since is null ? "" : "WHERE created_at >= $since")};
            """;
        if (since is not null) cmd.Parameters.AddWithValue("$since", since.Value.ToString("O"));
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new UsageSummary(reader.GetInt32(0), reader.GetDouble(1), reader.GetInt64(2),
            reader.GetInt64(3), reader.GetInt64(4), reader.GetInt64(5));
    }

    /// <summary>Coste por modelo desde un instante dado.</summary>
    public List<(string Model, int Calls, double CostUsd)> GetCostByModelSince(DateTimeOffset? since)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT model, COUNT(*), COALESCE(SUM(cost_usd),0) FROM usage_log
            {(since is null ? "" : "WHERE created_at >= $since")}
            GROUP BY model ORDER BY 3 DESC;
            """;
        if (since is not null) cmd.Parameters.AddWithValue("$since", since.Value.ToString("O"));
        using var reader = cmd.ExecuteReader();
        var result = new List<(string, int, double)>();
        while (reader.Read())
            result.Add((reader.GetString(0), reader.GetInt32(1), reader.GetDouble(2)));
        return result;
    }

    // ---- Sugerencias (contenido cifrado con DPAPI) ----------------------------

    public Suggestion AddSuggestion(string chatId, string chatName, string replyTo,
        string triggerSummary, string suggestedReply, double confidence, string sources)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO suggestions (chat_id, chat_name, created_at, reply_to, trigger_summary, suggested_reply, confidence, sources)
            VALUES ($chatId, $chatName, $createdAt, $replyTo, $trigger, $reply, $confidence, $sources);
            SELECT last_insert_rowid();
            """;
        var createdAt = DateTimeOffset.Now;
        cmd.Parameters.AddWithValue("$chatId", chatId);
        cmd.Parameters.AddWithValue("$chatName", chatName);
        cmd.Parameters.AddWithValue("$createdAt", createdAt.ToString("O"));
        cmd.Parameters.AddWithValue("$replyTo", replyTo);
        cmd.Parameters.AddWithValue("$trigger", CryptoHelper.Protect(triggerSummary));
        cmd.Parameters.AddWithValue("$reply", CryptoHelper.Protect(suggestedReply));
        cmd.Parameters.AddWithValue("$confidence", confidence);
        cmd.Parameters.AddWithValue("$sources", CryptoHelper.Protect(sources));
        var id = (long)cmd.ExecuteScalar()!;

        var suggestion = new Suggestion(id, chatId, chatName, createdAt, replyTo,
            triggerSummary, suggestedReply, confidence, sources);
        SuggestionAdded?.Invoke(suggestion);
        return suggestion;
    }

    /// <summary>Una sugerencia por id (descifra solo esa fila).</summary>
    public Suggestion? GetSuggestionById(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, chat_id, chat_name, created_at, reply_to, trigger_summary, suggested_reply, confidence, sources
            FROM suggestions WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadSuggestion(reader) : null;
    }

    /// <summary>
    /// Actividad agregada por chat para la lista lateral: número de sugerencias en la
    /// ventana reciente y timestamp de la última. No descifra ningún contenido.
    /// </summary>
    public Dictionary<string, (int RecentCount, DateTimeOffset? Latest)> GetChatActivity(TimeSpan recentWindow)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT chat_id,
                   SUM(CASE WHEN created_at > $threshold THEN 1 ELSE 0 END) AS recent,
                   MAX(created_at) AS latest
            FROM suggestions GROUP BY chat_id;
            """;
        cmd.Parameters.AddWithValue("$threshold", DateTimeOffset.Now.Subtract(recentWindow).ToString("O"));
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, (int, DateTimeOffset?)>();
        while (reader.Read())
        {
            var latest = reader.IsDBNull(2) ? (DateTimeOffset?)null : DateTimeOffset.Parse(reader.GetString(2));
            result[reader.GetString(0)] = (reader.GetInt32(1), latest);
        }
        return result;
    }

    /// <summary>Sugerencias creadas desde un instante dado (descifra su contenido).</summary>
    public List<Suggestion> GetSuggestionsSince(DateTimeOffset since, int limit = 200)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, chat_id, chat_name, created_at, reply_to, trigger_summary, suggested_reply, confidence, sources
            FROM suggestions WHERE created_at >= $since ORDER BY id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$since", since.ToString("O"));
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = cmd.ExecuteReader();
        var result = new List<Suggestion>();
        while (reader.Read()) result.Add(ReadSuggestion(reader));
        return result;
    }

    public List<Suggestion> GetSuggestions(int limit = 100, string? chatId = null)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            SELECT id, chat_id, chat_name, created_at, reply_to, trigger_summary, suggested_reply, confidence, sources
            FROM suggestions {(chatId is null ? "" : "WHERE chat_id = $chatId")}
            ORDER BY id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit", limit);
        if (chatId is not null) cmd.Parameters.AddWithValue("$chatId", chatId);
        using var reader = cmd.ExecuteReader();
        var result = new List<Suggestion>();
        while (reader.Read())
            result.Add(ReadSuggestion(reader));
        return result;
    }

    private static Suggestion ReadSuggestion(Microsoft.Data.Sqlite.SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3)), reader.GetString(4),
        CryptoHelper.Unprotect(reader.GetString(5)),
        CryptoHelper.Unprotect(reader.GetString(6)),
        reader.GetDouble(7),
        CryptoHelper.Unprotect(reader.GetString(8)));
}
