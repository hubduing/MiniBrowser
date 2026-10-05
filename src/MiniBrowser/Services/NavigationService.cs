namespace MiniBrowser.Services;

/// <summary>
/// Преобразование ввода из адресной строки в URL.
/// URL по умолчанию — поиск в Google.
/// </summary>
public static class NavigationService
{
    public const string SearchUrlTemplate = "https://www.google.com/search?q={0}";

    public static string? BuildUrl(string input)
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

        // Иначе — это поисковый запрос (Google по умолчанию)
        return string.Format(SearchUrlTemplate, Uri.EscapeDataString(input));
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
