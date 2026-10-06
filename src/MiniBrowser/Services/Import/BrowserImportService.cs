namespace MiniBrowser.Services.Import;

/// <summary>Итог импорта одного браузера: счётчики и причины пропусков.</summary>
public sealed record ImportResult(
    int BookmarksAdded,
    int BookmarksSkipped,
    int PasswordsAdded,
    int PasswordsSkipped,
    IReadOnlyList<string> Notes);

/// <summary>
/// Оркестрация импорта: выбранный читатель → дедуп → наши хранилища.
/// Работает в фоновом потоке (диалог сам зовёт через Task.Run).
/// </summary>
public sealed class BrowserImportService
{
    private readonly StorageService _storage;
    private readonly PasswordStore _passwords;

    public BrowserImportService(StorageService storage, PasswordStore passwords)
    {
        _storage = storage;
        _passwords = passwords;
    }

    public ImportResult Import(BrowserProfile profile, bool withBookmarks, bool withLogins)
    {
        IProfileReader reader = profile.Kind == BrowserKind.Firefox
            ? new FirefoxReader(profile)
            : new ChromiumReader(profile);

        var notes = new List<string>();
        var bookmarksAdded = 0;
        var bookmarksSkipped = 0;
        var passwordsAdded = 0;
        var passwordsSkipped = 0;

        if (withBookmarks)
        {
            if (!_storage.IsAvailable)
            {
                notes.Add("база данных недоступна — закладки не сохранены");
            }
            else
            {
                var result = reader.ReadBookmarks();
                foreach (var bookmark in result.Items)
                {
                    // AddBookmark вернул false — такой url уже есть, считаем пропуском.
                    if (_storage.AddBookmark(bookmark.Url, bookmark.Title)) bookmarksAdded++;
                    else bookmarksSkipped++;
                }
                bookmarksSkipped += result.Skipped;
                notes.AddRange(result.Notes);
            }
        }

        if (withLogins)
        {
            if (!_passwords.IsAvailable)
            {
                notes.Add("база данных недоступна — пароли не сохранены");
            }
            else
            {
                var result = reader.ReadLogins();
                foreach (var login in result.Items)
                {
                    // Дедуп по паре host+логин: существующий пароль не затираем.
                    if (_passwords.Add(login.Host, login.Username, login.Password)) passwordsAdded++;
                    else passwordsSkipped++;
                }
                passwordsSkipped += result.Skipped;
                notes.AddRange(result.Notes);
            }
        }

        return new ImportResult(bookmarksAdded, bookmarksSkipped, passwordsAdded, passwordsSkipped, notes);
    }
}
