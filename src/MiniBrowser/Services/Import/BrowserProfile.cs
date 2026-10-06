namespace MiniBrowser.Services.Import;

/// <summary>Браузер-источник импорта.</summary>
public enum BrowserKind
{
    Firefox,
    Chrome,
    Edge,
}

/// <summary>
/// Найденный установленный профиль: где лежат файлы браузера.
/// LocalStateFile — только у Chromium (ключ шифрования паролей), у Firefox его нет.
/// </summary>
public sealed record BrowserProfile(BrowserKind Kind, string Name, string ProfileDir, string? LocalStateFile);
