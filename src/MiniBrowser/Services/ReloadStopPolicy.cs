namespace MiniBrowser.Services;

/// <summary>Что должна сделать кнопка, совмещённая с «Обновить»/«Остановить».</summary>
public enum ReloadStopAction
{
    Reload,
    Stop,
}

/// <summary>
/// Кнопка «⟳/✕» одна: во время загрузки она останавливает, на settled-странице
/// перезагружает. Решение вынесено отдельно от WPF, чтобы его можно было
/// проверить тестом — сама загрузка живёт в WebView2 и юнит-тестам недоступна.
/// </summary>
public static class ReloadStopPolicy
{
    public static ReloadStopAction Decide(bool isLoading) =>
        isLoading ? ReloadStopAction.Stop : ReloadStopAction.Reload;

    public static string Glyph(bool isLoading) => isLoading ? "✕" : "⟳";

    public static string ToolTip(bool isLoading) =>
        isLoading ? "Остановить загрузку" : "Обновить (Ctrl+R)";
}