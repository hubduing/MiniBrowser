using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MiniBrowser.Services.Import;

/// <summary>
/// Чтение данных Firefox из копий файлов профиля (places.sqlite, logins.json,
/// key4.db): открытый Firefox не мешает — его файлы мы не трогаем.
/// </summary>
public sealed class FirefoxReader : IProfileReader
{
    private const string NoBookmarks = "нет файла places.sqlite";
    private const string NoLogins = "нет файла logins.json";
    private const string NoKey4 = "не найден key4.db — пароли не расшифровать";
    private const string MasterPassword = "в Firefox задан мастер-пароль — пароли не импортированы";
    private const string NoKey = "ключ шифрования Firefox не найден";

    private readonly BrowserProfile _profile;

    public FirefoxReader(BrowserProfile profile) => _profile = profile;

    public ReadResult<RawBookmark> ReadBookmarks()
    {
        try
        {
            using var temp = new TempWorkspace();
            var dbPath = temp.CopyShared("places.sqlite", _profile.ProfileDir);
            if (dbPath.Length == 0)
                return new(Array.Empty<RawBookmark>(), 0, new[] { NoBookmarks });
            // WAL и shm копируем вместе с БД — без них чтение увидит старые данные.
            temp.CopyShared("places.sqlite-wal", _profile.ProfileDir);
            temp.CopyShared("places.sqlite-shm", _profile.ProfileDir);

            var items = new List<RawBookmark>();
            using var conn = OpenTemp(dbPath);
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT b.title, p.url FROM moz_bookmarks b " +
                "JOIN moz_places p ON b.fk = p.id " +
                "WHERE b.type = 1 AND p.url NOT LIKE 'place:%' ORDER BY b.id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var url = reader.GetString(1);
                if (string.IsNullOrWhiteSpace(url)) continue;
                // Заголовок у закладки бывает NULL — тогда берём URL.
                var title = reader.IsDBNull(0) ? "" : reader.GetString(0);
                items.Add(new RawBookmark(url, string.IsNullOrWhiteSpace(title) ? url : title));
            }
            return new(items, 0, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new(Array.Empty<RawBookmark>(), 0, new[] { $"закладки Firefox не прочитаны: {ex.Message}" });
        }
    }

    public ReadResult<RawLogin> ReadLogins()
    {
        try
        {
            if (!File.Exists(Path.Combine(_profile.ProfileDir, "logins.json")))
                return new(Array.Empty<RawLogin>(), 0, new[] { NoLogins });
            if (!File.Exists(Path.Combine(_profile.ProfileDir, "key4.db")))
                return new(Array.Empty<RawLogin>(), 0, new[] { NoKey4 });

            using var temp = new TempWorkspace();

            // Мастер-ключ: проверка password-check, затем расшифровка a11 (соль и пароль пустые по умолчанию).
            var key4Path = temp.CopyShared("key4.db", _profile.ProfileDir);
            var material = ReadMasterKey(key4Path, out var keyError);
            if (material is null)
                return new(Array.Empty<RawLogin>(), 0, new[] { keyError! });

            var loginsPath = temp.CopyShared("logins.json", _profile.ProfileDir);
            using var doc = JsonDocument.Parse(File.ReadAllText(loginsPath, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("logins", out var logins) || logins.ValueKind != JsonValueKind.Array)
                return new(Array.Empty<RawLogin>(), 0, new[] { NoLogins });

            var items = new List<RawLogin>();
            var skipped = 0;
            foreach (var entry in logins.EnumerateArray())
            {
                if (!TryGetString(entry, "hostname", out var host)
                    || !TryGetString(entry, "encryptedUsername", out var encUser)
                    || !TryGetString(entry, "encryptedPassword", out var encPass)
                    || string.IsNullOrWhiteSpace(host))
                {
                    skipped++;
                    continue;
                }
                try
                {
                    var user = NssCrypto.DecryptLogin(DecodeBase64(encUser), material);
                    var pass = NssCrypto.DecryptLogin(DecodeBase64(encPass), material);
                    items.Add(new RawLogin(host, user, pass));
                }
                catch
                {
                    // Битый блоб или чужой алгоритм — пропускаем запись, не валим импорт.
                    skipped++;
                }
            }
            return new(items, skipped, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new(Array.Empty<RawLogin>(), 0, new[] { $"пароли Firefox не прочитаны: {ex.Message}" });
        }
    }

    /// <summary>Мастер-ключ из key4.db или причина отказа (мастер-пароль/нет ключа).</summary>
    private static byte[]? ReadMasterKey(string key4Path, out string? error)
    {
        error = null;
        using var conn = OpenTemp(key4Path);

        byte[]? globalSalt = null;
        byte[]? check = null;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT item1, item2 FROM metadata WHERE id = 'password';";
            using var reader = cmd.ExecuteReader();
            if (reader.Read())
            {
                globalSalt = (byte[])reader[0];
                check = (byte[])reader[1];
            }
        }
        if (globalSalt is null || check is null)
        {
            error = NoKey;
            return null;
        }

        var clear = NssCrypto.DecryptPbe(check, globalSalt, Encoding.UTF8.GetBytes(""));
        if (!NssCrypto.IsPasswordCheck(clear))
        {
            error = MasterPassword;
            return null;
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT a11 FROM nssPrivate WHERE a11 IS NOT NULL;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var a11 = (byte[])reader[0];
                var material = NssCrypto.DecryptPbe(a11, globalSalt, Encoding.UTF8.GetBytes(""));
                if (material.Length is 24 or 32 or 48)
                    return material;
            }
        }
        error = NoKey;
        return null;
    }

    private static SqliteConnection OpenTemp(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            // Это наша временная копия: на ней SQLite спокойно делает checkpoint WAL.
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        if (element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var prop)
            && prop.ValueKind == JsonValueKind.String)
        {
            value = prop.GetString() ?? "";
            return true;
        }
        value = "";
        return false;
    }

    private static byte[] DecodeBase64(string value)
    {
        try
        {
            return Convert.FromBase64String(value);
        }
        catch (FormatException)
        {
            // На случай префикса «v10»/«v11» прямо в JSON-строке: он ломает кратность base64.
            if (value.Length > 3 && (value.StartsWith("v10") || value.StartsWith("v11")))
                return Convert.FromBase64String(value[3..]);
            throw;
        }
    }

    /// <summary>Временная копия файлов профиля: чужие файлы открываются только на чтение с общим доступом.</summary>
    private sealed class TempWorkspace : IDisposable
    {
        public string Dir { get; } = Path.Combine(
            Path.GetTempPath(), "MiniBrowserImport-" + Guid.NewGuid().ToString("N"));

        public TempWorkspace() => Directory.CreateDirectory(Dir);

        /// <summary>Скопировать файл профиля; "" — файла нет.</summary>
        public string CopyShared(string fileName, string profileDir)
        {
            var source = Path.Combine(profileDir, fileName);
            if (!File.Exists(source)) return "";
            var target = Path.Combine(Dir, fileName);
            using var input = File.Open(source, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var output = File.Create(target);
            input.CopyTo(output);
            return target;
        }

        public void Dispose()
        {
            try { Directory.Delete(Dir, recursive: true); }
            catch { /* времёнка и так умрёт с системой */ }
        }
    }
}
