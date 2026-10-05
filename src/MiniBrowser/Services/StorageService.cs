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

    public StorageService(string? databasePath = null)
    {
        try
        {
            // Путь по умолчанию — локальный профиль; тесты подсовывают временный файл.
            var dbPath = databasePath ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MiniBrowser",
                "browser.db");
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                // Без пула: сервис держит одно соединение на всю жизнь, а пул после Dispose
                // оставлял бы lock на файле — временные БД тестов тогда не удаляются.
                Pooling = false,
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

    public List<Bookmark> GetAllBookmarks()
    {
        var result = new List<Bookmark>();
        if (_connection is null) return result;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT url, title FROM bookmarks ORDER BY added_at DESC;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new Bookmark(reader.GetString(0), reader.GetString(1)));
        }
        catch { }
        return result;
    }

    public void DeleteBookmark(string url)
    {
        if (_connection is null || string.IsNullOrWhiteSpace(url)) return;
        try
        {
            Exec("DELETE FROM bookmarks WHERE url = $u;", ("$u", url));
        }
        catch { }
    }

    public void ClearHistory()
    {
        if (_connection is null) return;
        try
        {
            Exec("DELETE FROM history;");
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

    public List<HistoryEntry> GetHistory(int limit = 200) => QueryHistory(null, limit);

    public List<HistoryEntry> SearchHistory(string query, int limit = 200)
    {
        // Пустой запрос — то же, что без фильтра: пользователь просто листает всё.
        if (string.IsNullOrEmpty(query)) return QueryHistory(null, limit);
        return QueryHistory($"%{EscapeLike(query)}%", limit);
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
            {
                // LastVisit дублирует прочитанную строку — панели нужна дата даже в старом меню.
                var visitedAt = reader.GetString(2);
                result.Add(new HistoryEntry(reader.GetString(0), reader.GetString(1), visitedAt)
                {
                    LastVisit = visitedAt,
                });
            }
        }
        catch { }
        return result;
    }

    private List<HistoryEntry> QueryHistory(string? pattern, int limit)
    {
        var result = new List<HistoryEntry>();
        if (_connection is null) return result;
        try
        {
            using var cmd = _connection.CreateCommand();
            // Общий запрос для GetHistory и SearchHistory — разница лишь в наличии WHERE.
            // Дедуп по URL: COUNT(*) — счётчик визитов, MAX(visited_at) — дата последнего.
            // Заголовок — подзапросом из самой свежей строки URL: MAX(title) дал бы
            // алфавитный максимум, не связанный с последним посещением.
            // ESCAPE '\' вместе с экранированием в C#: запрос "100%" ищется буквально.
            cmd.CommandText =
                "SELECT url, " +
                "MAX(visited_at) AS last_visit, " +
                "COUNT(*) AS visit_count, " +
                "(SELECT h2.title FROM history h2 WHERE h2.url = history.url " +
                "ORDER BY h2.id DESC LIMIT 1) AS title " +
                "FROM history " +
                (pattern is null ? "" : "WHERE (url LIKE $q ESCAPE '\\' OR title LIKE $q ESCAPE '\\') ") +
                "GROUP BY url " +
                "ORDER BY last_visit DESC " +
                "LIMIT $limit;";
            if (pattern is not null)
                cmd.Parameters.AddWithValue("$q", pattern);
            // Лимит — параметром, а не склейкой; потолок 500: панели нужен запас сверх сотни.
            cmd.Parameters.AddWithValue("$limit", Clamp(limit, 500));
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var lastVisit = reader.GetString(1);
                result.Add(new HistoryEntry(reader.GetString(0), reader.GetString(3), lastVisit)
                {
                    VisitCount = reader.GetInt32(2),
                    LastVisit = lastVisit,
                });
            }
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

    private static int Clamp(int limit, int max) => Math.Clamp(limit, 1, max);

    // Экранирование спецсимволов LIKE: сначала бэкслэш, иначе он повредит уже вставленные префиксы.
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static string Trim(string value, int max) =>
        string.IsNullOrEmpty(value) ? "" : (value.Length <= max ? value : value[..max]);
}
