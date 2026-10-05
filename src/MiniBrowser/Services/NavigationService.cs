namespace MiniBrowser.Services;

/// <summary>
/// Преобразование ввода из адресной строки в URL.
/// Шаблон поиска приходит из настроек, по умолчанию — Google.
/// </summary>
public static class NavigationService
{
    public const string SearchUrlTemplate = "https://www.google.com/search?q={0}";

    /// <summary>Имена движков в порядке отображения в ComboBox настроек.</summary>
    public static string[] EngineNames { get; } = new[] { "Google", "Bing", "DuckDuckGo", "Яндекс" };

    /// <summary>
    /// Шаблон поиска по имени движка. Неизвестное имя — это ручная правка
    /// конфига или устаревший список, поэтому молча откатываемся к Google.
    /// </summary>
    public static string SearchEngine(string name) => name switch
    {
        "Google" => SearchUrlTemplate,
        "Bing" => "https://www.bing.com/search?q={0}",
        "DuckDuckGo" => "https://duckduckgo.com/?q={0}",
        "Яндекс" => "https://yandex.ru/search/?text={0}",
        _ => SearchUrlTemplate,
    };

    public static string? BuildUrl(string input, string searchTemplate)
    {
        input = input.Trim();
        if (input.Length == 0) return null;

        // Уже полный URL с явной схемой
        if (input.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            input.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return input;

        // Похоже на домен (содержит точку, без пробелов) → переходим
        if (LooksLikeUrl(input))
            return "https://" + input;

        // Пустой шаблон из настроек не должен ломать адресную строку.
        if (string.IsNullOrEmpty(searchTemplate))
            searchTemplate = SearchUrlTemplate;

        // Шаблон без {0} — ручная правка настроек; запрос дописываем в конец.
        if (!searchTemplate.Contains("{0}", StringComparison.Ordinal))
            return searchTemplate + Uri.EscapeDataString(input);

        return string.Format(searchTemplate, Uri.EscapeDataString(input));
    }

    private static bool LooksLikeUrl(string s)
    {
        foreach (var c in s)
            if (char.IsWhiteSpace(c)) return false;

        if (s.Contains("://")) return false;      // непонятная схема — лучше поиском
        if (s.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return true;

        var dot = s.IndexOf('.');
        return dot > 0 && dot < s.Length - 1 && !s.EndsWith('.');
    }
}
