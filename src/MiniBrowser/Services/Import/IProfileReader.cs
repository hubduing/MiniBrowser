namespace MiniBrowser.Services.Import;

/// <summary>Закладка из чужого браузера до записи в наше хранилище.</summary>
public sealed record RawBookmark(string Url, string Title);

/// <summary>Пароль из чужого браузера до шифрования в наше хранилище.</summary>
public sealed record RawLogin(string Host, string Username, string Password);

/// <summary>Итог чтения одного раздела: что прочиталось, что пропущено и почему.</summary>
public sealed record ReadResult<T>(IReadOnlyList<T> Items, int Skipped, IReadOnlyList<string> Notes);

/// <summary>Читатель профиля чужого браузера: закладки и пароли.</summary>
public interface IProfileReader
{
    ReadResult<RawBookmark> ReadBookmarks();
    ReadResult<RawLogin> ReadLogins();
}
