using System.IO;
using Microsoft.Data.Sqlite;
using MiniBrowser.Models;

namespace MiniBrowser.Services;

/// <summary>
/// Лёгкое хранилище закладок и истории на SQLite (один файл в %LOCALAPPDATA%\MiniBrowser\browser.db).
/// Недоступность БД не роняет браузер — просто отключает историю/закладки.
/// </summary>
public sealed class StorageService : IDisposable
{
    private readonly SqliteConnection? _connection;
    private int _writeCounter;

    public bool IsAvailable => _connection is not null;

    public StorageService()
    {
        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniBrowser");
            Directory.CreateDirectory(dir);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(dir, "browser.db"),
                Mode = SqliteOpenMode.ReadWriteCreate,
            }.ToString();

            _connection = new SqliteConnection(connectionString);
            _connection.Open();

            Exec("""
                CREATE TABLE IF NOT EXISTS history (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    url TEXT NOT NULL,
                    title TEXT NOT NULL DEFAULT '',
                    visited_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
                );
                CREATE TABLE IF NOT EXISTS bookmarks (
                    url TEXT PRIMARY KEY,
                    title TEXT NOT NULL DEFAULT '',
                    added_at TEXT NOT NULL DEFAULT (datetime('now', 'localtime'))
                );
                """);
        }
        catch
        {
            _connection = null;
        }
    }

    public void AddHistory(string url, string title)
    {
        if (_connection is null || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Exec("INSERT INTO history (url, title) VALUES ($u, $t);",
                ("$u", url), ("$t", Trim(title, 200)));

            // Раз в 50 записей подрезаем, чтобы БД не росла бесконечно
            if (++_writeCounter % 50 == 0)
                Exec("DELETE FROM history WHERE id NOT IN (SELECT id FROM history ORDER BY id DESC LIMIT 1000);");
        }
        catch { /* БД недоступна — игнорируем */ }
    }

    public void AddBookmark(string url, string title)
    {
        if (_connection is null || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Exec("INSERT OR REPLACE INTO bookmarks (url, title) VALUES ($u, $t);",
                ("$u", url), ("$t", Trim(title, 200)));
        }
        catch { }
    }

    public List<Bookmark> GetBookmarks(int limit = 12)
    {
        var result = new List<Bookmark>();
        if (_connection is null) return result;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT url, title FROM bookmarks ORDER BY added_at DESC LIMIT {Clamp(limit)};";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new Bookmark(reader.GetString(0), reader.GetString(1)));
        }
        catch { }
        return result;
    }

    public List<HistoryEntry> GetRecentHistory(int limit = 12)
    {
        var result = new List<HistoryEntry>();
        if (_connection is null) return result;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = $"SELECT url, title, visited_at FROM history ORDER BY id DESC LIMIT {Clamp(limit)};";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new HistoryEntry(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }
        catch { }
        return result;
    }

    public void Dispose() => _connection?.Dispose();

    private void Exec(string sql, params (string Name, string Value)[] args)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
    }

    private static int Clamp(int limit) => Math.Clamp(limit, 1, 100);

    private static string Trim(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
