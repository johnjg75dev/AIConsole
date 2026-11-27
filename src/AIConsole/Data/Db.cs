using System.IO;
using Microsoft.Data.Sqlite;

namespace AIConsole.Data;

/// <summary>Single shared SQLite connection; %APPDATA%\AIConsole\aiconsole.db.</summary>
public sealed class Db : IDisposable
{
    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AIConsole");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "aiconsole.db");

    private readonly SqliteConnection _conn;
    private readonly object _lock = new();

    public Db()
    {
        Directory.CreateDirectory(DataDirectory);
        _conn = new SqliteConnection($"Data Source={DatabasePath}");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA foreign_keys=ON;");
        Migrate();
    }

    private void Migrate()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS settings (
                key   TEXT PRIMARY KEY,
                value TEXT
            );
            CREATE TABLE IF NOT EXISTS endpoints (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                base_url    TEXT NOT NULL,
                api_key_enc TEXT,
                show_all    INTEGER NOT NULL DEFAULT 0,
                created_at  TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS models (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                endpoint_id TEXT NOT NULL REFERENCES endpoints(id) ON DELETE CASCADE,
                model_id    TEXT NOT NULL,
                enabled     INTEGER NOT NULL DEFAULT 1,
                last_seen   TEXT NOT NULL,
                UNIQUE(endpoint_id, model_id)
            );
            CREATE TABLE IF NOT EXISTS conversations (
                id         TEXT PRIMARY KEY,
                title      TEXT NOT NULL,
                model      TEXT,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS messages (
                id              INTEGER PRIMARY KEY AUTOINCREMENT,
                conversation_id TEXT NOT NULL REFERENCES conversations(id) ON DELETE CASCADE,
                role            TEXT NOT NULL,
                content         TEXT NOT NULL,
                model_id        TEXT,
                participant     TEXT,
                created_at      TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS message_attachments (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                message_id INTEGER NOT NULL REFERENCES messages(id) ON DELETE CASCADE,
                name TEXT NOT NULL,
                media_type TEXT NOT NULL,
                data BLOB NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_attachments_message ON message_attachments(message_id);
            CREATE INDEX IF NOT EXISTS idx_messages_conversation ON messages(conversation_id);
            """);
    }

    public void Exec(string sql)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    public object? Scalar(string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            Bind(cmd, args);
            return cmd.ExecuteScalar();
        }
    }

    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            Bind(cmd, args);
            using var r = cmd.ExecuteReader();
            var list = new List<T>();
            while (r.Read()) list.Add(map(r));
            return list;
        }
    }

    public int Run(string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = sql;
            Bind(cmd, args);
            return cmd.ExecuteNonQuery();
        }
    }

    private static void Bind(SqliteCommand cmd, (string, object?)[] args)
    {
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public void Dispose() => _conn.Dispose();
}
