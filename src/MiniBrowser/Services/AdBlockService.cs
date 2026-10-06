using MiniBrowser.Models;

namespace MiniBrowser.Services;

/// <summary>
/// Блокировка рекламы и трекеров. Решение «блокировать ли этот запрос» живёт
/// здесь одним местом: и WebView2-фильтр, и скрипт маскировки, и тулбар спрашивают
/// именно его, поэтому расходиться состояния не могут.
///
/// Список фильтров встроен в код — браузер работает офлайн и не тянет подписки.
/// Покрытие скромное, но для мини-браузера этого достаточно.
/// </summary>
public sealed class AdBlockService
{
    /// <summary>
    /// Домены рекламных сетей и счётчиков. Совпадение идёт по суффиксу host,
    /// поэтому домены CDN-поддоменов (googleads.g.doubleclick.net) ловятся
    /// вместе с основным именем.
    /// </summary>
    private static readonly string[] AdHosts =
    {
        // Google / двойной клик
        "doubleclick.net",
        "googlesyndication.com",
        "googleadservices.com",
        "adservice.google.com",
        "google-analytics.com",
        "googletagmanager.com",
        "googletagservices.com",
        "2mdn.net",
        // Яндекс
        "mc.yandex.ru",
        "metrika",
        "adservice.yandex.ru",
        "ads.yandex.ru",
        "adfox.ru",
        "adfox.info",
        "adfox.net",
        "tns-counter.ru",
        "top-fwz1.ru",
        "top100.rambler.ru",
        "mediator.yandex.net",
        // Прочие сети
        "adform.net",
        "smartadserver.com",
        "casalemedia.com",
        "openx.net",
        "criteo.com",
        "criteo.net",
        "taboola.com",
        "outbrain.com",
        "revcontent.com",
        "facebook.net",
        "connect.facebook.net",
        "ads.linkedin.com",
        "ads-twitter.com",
    };

    /// <summary>
    /// Селекторы типовых рекламных блоков. Скрываются элементы, чей класс или id
    /// однозначно указывает на рекламу; спорные («промежуточные») имена вроде
    /// просто .ad не трогаем — иначе под правило попадёт контент сайта.
    /// </summary>
    private static readonly string[] CosmeticSelectors =
    {
        "iframe[src*='doubleclick.net']",
        "iframe[src*='googlesyndication.com']",
        "iframe[src*='googletagservices.com']",
        "iframe[src*='googleadservices.com']",
        "iframe[src*='adfox']",
        "iframe[src*='tns-counter.ru']",
        "iframe[src*='vk.com/rtrg']",
        "[id^='google_ads_']",
        "[id^='div-gpt-ad']",
        "[id^='google-ad-']",
        "[class~='ad-banner']",
        "[class~='adbox']",
        "[class~='advert-banner']",
        "[class~='sponsored-post']",
        "[data-ad-client]",
        "[data-ad-slot]",
    };

    /// <summary>Правила для инлайновых картинок, если атрибут src попал в правило.</summary>
    public static IReadOnlyList<string> CosmeticRules => CosmeticSelectors;

    private readonly AppSettings _settings;

    public AdBlockService(AppSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        // Старый settings.json без нового поля даёт null — список обязан быть живым,
        // иначе первое же переключение упало бы на NullReferenceException.
        _settings.AdBlockDisabledHosts ??= new List<string>();
    }

    /// <summary>Сколько доменов пользователь отключил (для панели настроек).</summary>
    public int DisabledHostsCount => _settings.AdBlockDisabledHosts.Count;

    /// <summary>
    /// Заблокирован ли запрос.
    ///
    /// pageHost — домен страницы, с которой пришёл запрос. Он обязателен:
    /// белый список пользователя хранит домены сайтов, а блокируемые адреса
    /// лежат на других доменах (реклама с example.com грузится с doubleclick.net).
    /// Без pageHost отключение блокировки на сайте не сработало бы никогда.
    ///
    /// Схемы без host (about:, data:) проходят.
    /// </summary>
    public bool IsBlocked(Uri url, string? pageHost = null)
    {
        if (!url.IsAbsoluteUri || url.Host.Length == 0) return false;
        // Хост из URL может содержать порт — сопоставляем только имя.
        var host = NormalizeHost(url.Host);
        if (host.Length == 0) return false;

        if (!_settings.AdBlockEnabled) return false;

        // Сайт из белого списка отдаёт рекламу целиком, включая внешние домены.
        // Неизвестный pageHost (запрос до смены страницы) считаем обычным сайтом:
        // молча пропускать рекламу там, где пользователь её не разрешал, — хуже.
        var page = NormalizeHost(pageHost);
        if (page.Length > 0
            && _settings.AdBlockDisabledHosts.Contains(page, StringComparer.OrdinalIgnoreCase))
            return false;

        return IsBlockedHost(host);
    }

    /// <summary>
    /// Включена ли блокировка для сайта: мастер-выключатель и белый список.
    /// Пустой host (about:, data:) считаем разрешённым — блокировать там нечего.
    /// </summary>
    public bool IsEnabledForHost(string? host)
    {
        if (!_settings.AdBlockEnabled) return false;
        var normalized = NormalizeHost(host);
        if (normalized.Length == 0) return false;
        return !_settings.AdBlockDisabledHosts.Contains(normalized, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Переключить блокировку на домене и вернуть новое состояние блокировки
    /// (true — блокировка на этом сайте включена). Пустой host игнорируется:
    /// переключать нечего, и вызывающий код не должен считать это отказом.
    /// </summary>
    public bool ToggleHost(string? host)
    {
        var normalized = NormalizeHost(host);
        if (normalized.Length == 0) return true;

        var existing = _settings.AdBlockDisabledHosts
            .FirstOrDefault(h => string.Equals(h, normalized, StringComparison.OrdinalIgnoreCase));

        if (existing is null) _settings.AdBlockDisabledHosts.Add(normalized);
        else _settings.AdBlockDisabledHosts.Remove(existing);

        return IsEnabledForHost(normalized);
    }

    /// <summary>Очистить белый список: блокировка снова включена везде.</summary>
    public void ClearDisabledHosts() => _settings.AdBlockDisabledHosts.Clear();

    /// <summary>
    /// Скрипт, маскирующий рекламные элементы на документе. Список отключённых
    /// доменов не запекается сюда: скрипт сам спрашивает настройки страницы, но
    /// решения по хосту он не знает, поэтому для отключённого сайта скрипт
    /// намеренно пустой — иначе он спрятал бы рекламу там, где её оставили.
    /// </summary>
    public string BuildCosmeticScript(string? host)
    {
        if (!IsEnabledForHost(host)) return string.Empty;

        // Без блока с display:none список селекторов невалиден как CSS и не скроет
        // ничего. !important нужен, потому что у рекламы часто есть inline-стиль.
        var css = string.Join(",", CosmeticSelectors) + " { display: none !important; }";
        // Селекторы содержат одинарные кавычки, поэтому в JS они идут в строке
        // с двойными: иначе пришлось бы городить экранирование ради экранирования.
        var escaped = css.Replace("\\", "\\\\").Replace("\"", "\\\"");

        return "(function () {\n"
             + "  if (window.__mbAdBlockApplied) return;\n"
             + "  window.__mbAdBlockApplied = true;\n"
             + $"  var style = document.createElement('style');\n"
             + $"  style.textContent = \"{escaped}\";\n"
             + "  (document.head || document.documentElement).appendChild(style);\n"
             + "})();";
    }

    /// <summary>Нормализация домена: без схемы, без www, без порта, в нижнем регистре.</summary>
    private static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;
        var value = host.Trim().ToLowerInvariant();

        // Могут прийти как URL целиком, а не как host.
        if (value.Contains("://")) value = new Uri(value).Host.ToLowerInvariant();
        else
        {
            var colon = value.IndexOf(':');
            if (colon >= 0) value = value[..colon];
        }

        if (value.StartsWith("www.", StringComparison.Ordinal)) value = value[4..];
        return value;
    }

    /// <summary>Совпадение домена с фильтрами. Настройки здесь уже учтены вызывающим.</summary>
    private static bool IsBlockedHost(string host)
    {
        foreach (var rule in AdHosts)
        {
            if (host == rule) return true;
            // Суффикс с границей точки: «notdoubleclick.example» не должен
            // ловиться правилом «doubleclick.net».
            if (host.EndsWith("." + rule, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}