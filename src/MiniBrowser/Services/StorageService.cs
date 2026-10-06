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
            // Папка есть не всегда (голая "browser.db" — файл в текущей папке),
            // а врать оператором ! нельзя: иначе молча деградируем в IsAvailable == false.
            if (Path.GetDirectoryName(dbPath) is { } dir) Directory.CreateDirectory(dir);

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
                -- Раскладка групп и вкладок. Идентификаторы — Guid строкой, чтобы
                -- восстановление не зависело от порядка строк в файле сессии.
                CREATE TABLE IF NOT EXISTS tab_groups (
                    id TEXT PRIMARY KEY,
                    name TEXT NOT NULL,
                    color_index INTEGER NOT NULL DEFAULT 0,
                    position INTEGER NOT NULL,
                    collapsed INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS session_tabs (
                    id TEXT PRIMARY KEY,
                    group_id TEXT NOT NULL,
                    position INTEGER NOT NULL,
                    url TEXT NOT NULL,
                    title TEXT NOT NULL DEFAULT '',
                    is_active INTEGER NOT NULL DEFAULT 0
                );
                -- Хранилище паролей: секрет зашифрован DPAPI (см. PasswordStore),
                -- дедуп по паре хост+логин — повторный импорт не создаёт дублей.
                CREATE TABLE IF NOT EXISTS passwords (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    host TEXT NOT NULL,
                    username TEXT NOT NULL,
                    secret BLOB NOT NULL,
                    added_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                    UNIQUE(host, username)
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

    /// <summary>
    /// Добавить закладку. Возвращает false, если url пуст или закладка уже
    /// была (INSERT OR IGNORE — дедуп по url при импорте из других браузеров).
    /// </summary>
    public bool AddBookmark(string url, string title)
    {
        if (_connection is null || string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO bookmarks (url, title) VALUES ($u, $t);";
            cmd.Parameters.AddWithValue("$u", url);
            cmd.Parameters.AddWithValue("$t", Trim(title, 200));
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>Добавить пароль (секрет — уже зашифрованный DPAPI байт). False — дубль пары host+логин.</summary>
    public bool AddPassword(string host, string username, byte[] secret)
    {
        if (_connection is null || string.IsNullOrWhiteSpace(host)) return false;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO passwords (host, username, secret) VALUES ($h, $u, $s);";
            cmd.Parameters.AddWithValue("$h", host);
            cmd.Parameters.AddWithValue("$u", username);
            cmd.Parameters.AddWithValue("$s", secret);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
    }

    /// <summary>Все пароли без секрета: список в UI не должен тянуть шифраты.</summary>
    public List<StoredPassword> GetPasswords()
    {
        var result = new List<StoredPassword>();
        if (_connection is null) return result;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT id, host, username FROM passwords ORDER BY host, username;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(new StoredPassword(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        }
        catch { }
        return result;
    }

    /// <summary>Зашифрованный секрет записи или null, если записи нет.</summary>
    public byte[]? GetPasswordSecret(int id)
    {
        if (_connection is null) return null;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "SELECT secret FROM passwords WHERE id = $i;";
            cmd.Parameters.AddWithValue("$i", id);
            return cmd.ExecuteScalar() as byte[];
        }
        catch { return null; }
    }

    /// <summary>Удалить пароль. False — записи не было.</summary>
    public bool DeletePassword(int id)
    {
        if (_connection is null) return false;
        try
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = "DELETE FROM passwords WHERE id = $i;";
            cmd.Parameters.AddWithValue("$i", id);
            return cmd.ExecuteNonQuery() > 0;
        }
        catch { return false; }
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

    /// <summary>
    /// Записать раскладку сессии целиком. Сессия маленькая и меняется целиком
    /// при каждом изменении, поэтому диффы не делаем: обе таблицы чистятся и
    /// заполняются заново в одной транзакции — полузаписанной сессии не бывает.
    /// </summary>
    public void SaveSession(SessionSnapshot snapshot)
    {
        if (_connection is null) return;
        try
        {
            using var transaction = _connection.BeginTransaction();

            Run(transaction, "DELETE FROM tab_groups;");
            Run(transaction, "DELETE FROM session_tabs;");

            foreach (var group in snapshot.Groups)
            {
                Run(transaction,
                    "INSERT INTO tab_groups (id, name, color_index, position, collapsed) " +
                    "VALUES ($id, $name, $color, $pos, $collapsed);",
                    ("$id", group.Id), ("$name", group.Name),
                    ("$color", group.ColorIndex), ("$pos", group.Position),
                    ("$collapsed", group.Collapsed ? 1 : 0));
            }

            foreach (var tab in snapshot.Tabs)
            {
                Run(transaction,
                    "INSERT INTO session_tabs (id, group_id, position, url, title, is_active) " +
                    "VALUES ($id, $gid, $pos, $url, $title, $active);",
                    ("$id", tab.Id), ("$gid", tab.GroupId), ("$pos", tab.Position),
                    ("$url", tab.Url), ("$title", tab.Title), ("$active", tab.IsActive ? 1 : 0));
            }

            transaction.Commit();
        }
        catch { /* Сессия не обязана переживать сбой записи: смиссия не восстановится */ }
    }

    /// <summary>Прочитать снимок сессии или null, если хранилища/сессии нет.</summary>
    public SessionSnapshot? LoadSession()
    {
        if (_connection is null) return null;
        try
        {
            var groups = new List<SessionGroupRow>();
            using (var cmd = _connection.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT id, name, color_index, position, collapsed FROM tab_groups ORDER BY position;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    groups.Add(new SessionGroupRow(
                        reader.GetString(0), reader.GetString(1), (int)reader.GetInt64(2),
                        reader.GetInt64(4) != 0, (int)reader.GetInt64(3)));
            }

            // Без групп хранилище считается пустым: чинки группы без вкладок нет,
            // а старт с них означал бы потерю всего, что было.
            if (groups.Count == 0) return null;

            var tabs = new List<SessionTabRow>();
            using (var cmd = _connection.CreateCommand())
            {
                // Порядок вкладок восстанавливаем по порядку их группы, а уже внутри группы —
// по сохранённой позиции. Так чтение не зависит от того, сквозную позицию
// присвоил писатель или позицию внутри своей группы.
cmd.CommandText =
                    "SELECT id, group_id, url, title, is_active FROM session_tabs " +
                    "ORDER BY (SELECT g.position FROM tab_groups g WHERE g.id = session_tabs.group_id), " +
                    "position, rowid;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    tabs.Add(new SessionTabRow(
                        reader.GetString(0), reader.GetString(1), reader.GetString(2),
                        reader.GetString(3), reader.GetInt64(4) != 0, tabs.Count));
            }

            return new SessionSnapshot(groups, tabs);
        }
        catch { return null; }
    }

    /// <summary>Выполнить одну команду внутри транзакции сессии.</summary>
    private void Run(SqliteTransaction transaction, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = transaction.Connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value);
        cmd.ExecuteNonQuery();
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
