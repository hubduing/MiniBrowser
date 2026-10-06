using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace MiniBrowser.Services.Import;

/// <summary>
/// Чтение данных Chromium-браузеров (Chrome, Edge) из копий файлов профиля:
/// закладки — JSON «Bookmarks», пароли — «Login Data» + ключ из «Local State».
/// </summary>
public sealed class ChromiumReader : IProfileReader
{
    private const string NoBookmarks = "нет файла Bookmarks (закладки)";
    private const string NoLoginData = "нет файла Login Data (пароли)";
    private const string NoLocalState = "нет Local State — ключ шифрования не найден";
    private const string AppBoundPrefix = "новое шифрование браузера (v20)";

    private readonly BrowserProfile _profile;

    public ChromiumReader(BrowserProfile profile) => _profile = profile;

    public ReadResult<RawBookmark> ReadBookmarks()
    {
        try
        {
            var path = Path.Combine(_profile.ProfileDir, "Bookmarks");
            if (!File.Exists(path))
                return new(Array.Empty<RawBookmark>(), 0, new[] { NoBookmarks });

            using var doc = JsonDocument.Parse(File.ReadAllText(path, Encoding.UTF8));
            var items = new List<RawBookmark>();
            if (doc.RootElement.TryGetProperty("roots", out var roots)
                && roots.ValueKind == JsonValueKind.Object)
                foreach (var root in roots.EnumerateObject())
                    Walk(root.Value, items);
            return new(items, 0, Array.Empty<string>());
        }
        catch (Exception ex)
        {
            return new(Array.Empty<RawBookmark>(), 0, new[] { $"закладки не прочитаны: {ex.Message}" });
        }
    }

    public ReadResult<RawLogin> ReadLogins()
    {
        try
        {
            using var temp = new TempWorkspace();
            var dbPath = temp.CopyShared("Login Data", _profile.ProfileDir);
            if (dbPath.Length == 0)
                return new(Array.Empty<RawLogin>(), 0, new[] { NoLoginData });

            // Мастер-ключ из Local State: base64 → префикс DPAPI → расшифровка DPAPI.
            byte[]? masterKey = null;
            var notes = new List<string>();
            if (_profile.LocalStateFile is not null && File.Exists(_profile.LocalStateFile))
                masterKey = ReadMasterKey(_profile.LocalStateFile, notes);
            else
                notes.Add(NoLocalState);

            var items = new List<RawLogin>();
            var skipped = 0;
            var appBound = 0;
            using (var conn = OpenTemp(dbPath))
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT origin_url, username_value, password_value FROM logins;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    var origin = reader.IsDBNull(0) ? "" : reader.GetString(0);
                    var user = reader.IsDBNull(1) ? "" : reader.GetString(1);
                    var blob = reader.IsDBNull(2) ? Array.Empty<byte>() : (byte[])reader[2];
                    if (origin.Length == 0 || blob.Length == 0)
                    {
                        skipped++;
                        continue;
                    }
                    try
                    {
                        var password = DecryptPassword(blob, masterKey, out var appBoundEntry);
                        if (appBoundEntry)
                        {
                            appBound++;
                            continue;
                        }
                        items.Add(new RawLogin(origin, user, password));
                    }
                    catch
                    {
                        skipped++;
                    }
                }
            }
            if (appBound > 0)
                notes.Add($"{AppBoundPrefix}: {appBound} записей пропущено");
            return new(items, skipped, notes);
        }
        catch (Exception ex)
        {
            return new(Array.Empty<RawLogin>(), 0, new[] { $"пароли не прочитаны: {ex.Message}" });
        }
    }

    /// <summary>Обойти узел дерева закладок: url — в результат, папки — рекурсивно.</summary>
    private static void Walk(JsonElement node, List<RawBookmark> items)
    {
        if (node.ValueKind != JsonValueKind.Object) return;
        var type = node.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        if (type == "url")
        {
            var url = node.TryGetProperty("url", out var urlEl) ? urlEl.GetString() ?? "" : "";
            if (url.Length == 0) return;
            var name = node.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
            items.Add(new RawBookmark(url, name.Length == 0 ? url : name));
            return;
        }
        if (node.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
            foreach (var child in children.EnumerateArray())
                Walk(child, items);
    }

    /// <summary>32-байтовый AES-ключ из Local State или null (записи пойдут через старый DPAPI).</summary>
    private static byte[]? ReadMasterKey(string localStatePath, List<string> notes)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(localStatePath, Encoding.UTF8));
            if (!doc.RootElement.TryGetProperty("os_crypt", out var osCrypt)
                || !osCrypt.TryGetProperty("encrypted_key", out var keyEl)
                || keyEl.ValueKind != JsonValueKind.String)
            {
                notes.Add(NoLocalState);
                return null;
            }
            var protectedKey = Convert.FromBase64String(keyEl.GetString() ?? "");
            // Хранилище помечает ключ префиксом «DPAPI».
            if (protectedKey.Length > 5 && protectedKey[0] == 'D')
                protectedKey = protectedKey[5..];
            return ProtectedData.Unprotect(protectedKey, null, DataProtectionScope.CurrentUser);
        }
        catch
        {
            notes.Add(NoLocalState);
            return null;
        }
    }

    /// <summary>
    /// Расшифровать password_value: v10/v11 — AES-256-GCM мастер-ключом,
    /// без префикса — старый DPAPI, v20 (app-bound) — не поддерживается.
    /// </summary>
    private static string DecryptPassword(byte[] blob, byte[]? masterKey, out bool appBound)
    {
        appBound = false;
        if (blob.Length >= 3 && blob[0] == 'v' && blob[1] == '2' && blob[2] == '0')
        {
            appBound = true;
            return "";
        }
        if (blob.Length >= 3 && blob[0] == 'v' && blob[1] == '1' && (blob[2] == '0' || blob[2] == '1'))
        {
            if (masterKey is null)
                throw new InvalidDataException("нет мастер-ключа для AES-GCM");
            // Раскладка: «v10»(3) + nonce(12) + шифртекст + tag(16).
            var nonce = blob.AsSpan(3, 12).ToArray();
            var tag = blob.AsSpan(blob.Length - 16, 16).ToArray();
            var cipherText = blob.AsSpan(15, blob.Length - 15 - 16).ToArray();
            var plain = new byte[cipherText.Length];
            using var gcm = new AesGcm(masterKey, 16);
            gcm.Decrypt(nonce, cipherText, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        // До Chrome 80 значения шифровались DPAPI напрямую.
        var legacy = ProtectedData.Unprotect(blob, null, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(legacy);
    }

    private static SqliteConnection OpenTemp(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            // Копия наша: журнальный файл SQLite создаётся без проблем.
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        return connection;
    }
}
