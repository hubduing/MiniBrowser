namespace MiniBrowser.Services;

/// <summary>
/// Разобранные сетевые правила списка фильтров: домены для блокировки и
/// домены-исключения. Косметические правила сюда не попадают — для них нужен
/// движок селекторов с :has-text(), которого здесь нет.
/// </summary>
public sealed class EasyListRuleSet
{
    public EasyListRuleSet(HashSet<string> domains, HashSet<string> exceptions)
    {
        Domains = domains;
        Exceptions = exceptions;
    }

    /// <summary>Домены, которые блокируются (с поддоменами).</summary>
    public HashSet<string> Domains { get; }

    /// <summary>Домены, для которых блокировка не применяется.</summary>
    public HashSet<string> Exceptions { get; }

    /// <summary>
    /// Совпадает ли host (с поддоменами) с блокируемым доменом. Проверяем
    /// метки по одной, с самой длинной: у «a.b.example» сначала «a.b.example»,
    /// потом «b.example», потом «example». Точное совпадение границы не
    /// подменяется подстрокой — «notexample» не ловится правилом «example».
    /// </summary>
    public bool Matches(string host)
    {
        if (host.Length == 0) return false;

        var candidate = host;
        while (candidate.Length > 0)
        {
            if (Exceptions.Contains(candidate)) return false;
            if (Domains.Contains(candidate)) return true;

            var dot = candidate.IndexOf('.');
            // Домен без точки («localhost») проверяем один раз и выходим.
            if (dot < 0) break;
            candidate = candidate[(dot + 1)..];
        }

        return false;
    }
}

/// <summary>
/// Разбор списка фильтров в формате Adblock Plus. Берём только сетевые
/// правила вида «||домен^» и исключения «@@||домен^».
/// </summary>
public static class EasyListParser
{
    public static EasyListRuleSet Parse(string? text)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var exceptions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(text)) return new EasyListRuleSet(domains, exceptions);

        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0) continue;

            var isException = line.StartsWith("@@", StringComparison.Ordinal);
            if (isException) line = line[2..];

            if (!line.StartsWith("||", StringComparison.Ordinal)) continue;

            var domain = ExtractDomain(line[2..]);
            // Список не может быть источником тишины: пустой результат означал бы
            // «блокировать всё», поэтому такие строки просто пропускаются.
            if (domain.Length == 0) continue;

            if (isException) exceptions.Add(domain);
            else domains.Add(domain);
        }

        return new EasyListRuleSet(domains, exceptions);
    }

    /// <summary>
    /// Расширения файлов: правило «||banner.gif$image» привязано к имени файла,
    /// а не к домену. Записав «banner.gif» в список доменов, мы заблокировали бы
    /// запросы к картинкам с таким именем на любом домене — это и неверно, и
    /// куда более разрушительно, чем кажется.
    /// </summary>
    private static readonly string[] FileExtensions =
    {
        ".gif", ".png", ".jpg", ".jpeg", ".webp", ".svg", ".ico", ".bmp",
        ".js", ".mjs", ".css", ".html", ".htm", ".xml", ".json", ".php", ".ashx",
        ".mp4", ".webm", ".mp3", ".woff", ".woff2",
    };

    /// <summary>
    /// Домен из тела правила: обрезаем по разделителям ABP (^, |, /, $) и
    /// отбрасываем маску символов (*), которая для сопоставления по домену
    /// ничего не значит. Возвращает пустую строку, если это не домен.
    /// </summary>
    private static string ExtractDomain(string body)
    {
        var end = body.Length;
        foreach (var sep in new[] { '^', '|', '/', '$' })
        {
            var index = body.IndexOf(sep, StringComparison.Ordinal);
            if (index >= 0 && index < end) end = index;
        }

        var domain = body[..end].Replace("*", string.Empty).Trim().ToLowerInvariant();
        if (!LooksLikeDomain(domain)) return string.Empty;

        foreach (var ext in FileExtensions)
            if (domain.EndsWith(ext, StringComparison.Ordinal)) return string.Empty;

        return domain;
    }

    /// <summary>Хост из меток вида «a.b.c», где метки — буквы, цифры и дефис.</summary>
    private static bool LooksLikeDomain(string value)
    {
        if (!value.Contains('.') || value.StartsWith('.') || value.EndsWith('.')) return false;

        foreach (var label in value.Split('.'))
        {
            if (label.Length == 0) return false;
            foreach (var c in label)
                if (!char.IsAsciiLetterOrDigit(c) && c != '-') return false;
        }

        return true;
    }
}