using Microsoft.Data.Sqlite;

namespace TeamsJarvis.Core;

/// <summary>
/// Notas de proyecto y recordatorios programables (SQLite). El cuerpo de las notas y el
/// texto de los recordatorios se cifran en reposo con DPAPI (solo los abre el usuario de Windows).
/// </summary>
public sealed class NotesStore
{
    private readonly string _connectionString = $"Data Source={AppPaths.DbPath}";

    /// <summary>Se dispara al crear/actualizar un recordatorio (para refrescar la UI / reprogramar avisos).</summary>
    public event Action? RemindersChanged;

    public NotesStore()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS notes (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                title      TEXT NOT NULL,
                project    TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL,
                body_enc   TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS reminders (
                id         INTEGER PRIMARY KEY AUTOINCREMENT,
                due_at     TEXT NOT NULL,
                project    TEXT NOT NULL DEFAULT '',
                done       INTEGER NOT NULL DEFAULT 0,
                notified   INTEGER NOT NULL DEFAULT 0,
                created_at TEXT NOT NULL,
                text_enc   TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_rem_due ON reminders(due_at);
            """;
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    // ---- Notas ---------------------------------------------------------------

    public long UpsertNote(long id, string title, string body, string project)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (id <= 0)
        {
            cmd.CommandText = """
                INSERT INTO notes (title, project, updated_at, body_enc)
                VALUES ($title, $project, $now, $body);
                SELECT last_insert_rowid();
                """;
        }
        else
        {
            cmd.CommandText = """
                UPDATE notes SET title = $title, project = $project, updated_at = $now, body_enc = $body
                WHERE id = $id;
                SELECT $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
        }
        cmd.Parameters.AddWithValue("$title", title);
        cmd.Parameters.AddWithValue("$project", project);
        cmd.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("O"));
        cmd.Parameters.AddWithValue("$body", CryptoHelper.Protect(body));
        return (long)cmd.ExecuteScalar()!;
    }

    public void DeleteNote(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM notes WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public List<ProjectNote> GetNotes()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, body_enc, project, updated_at FROM notes ORDER BY updated_at DESC;";
        using var reader = cmd.ExecuteReader();
        var result = new List<ProjectNote>();
        while (reader.Read())
            result.Add(new ProjectNote(reader.GetInt64(0), reader.GetString(1),
                CryptoHelper.Unprotect(reader.GetString(2)), reader.GetString(3),
                DateTimeOffset.Parse(reader.GetString(4))));
        return result;
    }

    // ---- Recordatorios -------------------------------------------------------

    public long UpsertReminder(long id, string text, DateTimeOffset dueAt, string project)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        if (id <= 0)
        {
            cmd.CommandText = """
                INSERT INTO reminders (due_at, project, done, notified, created_at, text_enc)
                VALUES ($due, $project, 0, 0, $now, $text);
                SELECT last_insert_rowid();
                """;
            cmd.Parameters.AddWithValue("$now", DateTimeOffset.Now.ToString("O"));
        }
        else
        {
            // Reprogramar reinicia el aviso (notified=0).
            cmd.CommandText = """
                UPDATE reminders SET due_at = $due, project = $project, text_enc = $text, notified = 0
                WHERE id = $id;
                SELECT $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
        }
        cmd.Parameters.AddWithValue("$due", dueAt.ToString("O"));
        cmd.Parameters.AddWithValue("$project", project);
        cmd.Parameters.AddWithValue("$text", CryptoHelper.Protect(text));
        var newId = (long)cmd.ExecuteScalar()!;
        RemindersChanged?.Invoke();
        return newId;
    }

    public void SetReminderDone(long id, bool done)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE reminders SET done = $d WHERE id = $id;";
        cmd.Parameters.AddWithValue("$d", done ? 1 : 0);
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        RemindersChanged?.Invoke();
    }

    public void DeleteReminder(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM reminders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
        RemindersChanged?.Invoke();
    }

    public List<Reminder> GetReminders(bool includeDone = true)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, text_enc, due_at, project, done, notified FROM reminders"
            + (includeDone ? "" : " WHERE done = 0") + " ORDER BY due_at ASC;";
        using var reader = cmd.ExecuteReader();
        var result = new List<Reminder>();
        while (reader.Read()) result.Add(ReadReminder(reader));
        return result;
    }

    public Reminder? GetReminderById(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, text_enc, due_at, project, done, notified FROM reminders WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadReminder(reader) : null;
    }

    /// <summary>Recordatorios vencidos, no avisados y no hechos (para lanzar el toast).</summary>
    public List<Reminder> GetDueUnnotified(DateTimeOffset now)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT id, text_enc, due_at, project, done, notified FROM reminders
            WHERE done = 0 AND notified = 0 AND due_at <= $now ORDER BY due_at ASC;
            """;
        cmd.Parameters.AddWithValue("$now", now.ToString("O"));
        using var reader = cmd.ExecuteReader();
        var result = new List<Reminder>();
        while (reader.Read()) result.Add(ReadReminder(reader));
        return result;
    }

    public void MarkNotified(long id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE reminders SET notified = 1 WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    private static Reminder ReadReminder(SqliteDataReader reader) => new(
        reader.GetInt64(0), CryptoHelper.Unprotect(reader.GetString(1)),
        DateTimeOffset.Parse(reader.GetString(2)), reader.GetString(3),
        reader.GetInt32(4) == 1, reader.GetInt32(5) == 1);
}
