using System.Security.Cryptography;
using System.Text;

namespace MiniBrowser.Services;

/// <summary>Строка списка паролей — без секрета, он запрашивается отдельно.</summary>
public sealed record StoredPassword(int Id, string Host, string Username);

/// <summary>
/// Хранилище паролей. В БД лежит только DPAPI-шифрат (ключ текущего
/// пользователя Windows), открытый текст живёт в памяти на время
/// показа/копирования и в файл не попадает.
/// </summary>
public sealed class PasswordStore
{
    // Соль DPAPI: шифрат читается только нашим приложением, а не любым
    // инструментом, знающим стандартную обвязку CryptUnprotectData.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("MiniBrowser.Passwords.v1");

    private readonly StorageService _storage;

    public PasswordStore(StorageService storage) => _storage = storage;

    public bool IsAvailable => _storage.IsAvailable;

    /// <summary>Сохранить пароль. false — пустые данные или дубль пары host+логин.</summary>
    public bool Add(string host, string username, string password)
    {
        if (string.IsNullOrWhiteSpace(host) || password is null) return false;
        try
        {
            var secret = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(password), Entropy, DataProtectionScope.CurrentUser);
            return _storage.AddPassword(host, username, secret);
        }
        catch { return false; }
    }

    /// <summary>Все пароли без секрета (для списка).</summary>
    public IReadOnlyList<StoredPassword> GetAll() => _storage.GetPasswords();

    /// <summary>Расшифровать секрет по id; "" — нет записи или не расшифровалось.</summary>
    public string GetSecret(int id)
    {
        try
        {
            var secret = _storage.GetPasswordSecret(id);
            if (secret is null) return "";
            var bytes = ProtectedData.Unprotect(secret, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch { return ""; }
    }

    /// <summary>Удалить пароль. false — записи не было.</summary>
    public bool Delete(int id) => _storage.DeletePassword(id);
}
