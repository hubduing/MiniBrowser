using System.IO;
using System.Text.Json;
using MiniBrowser.Models;

namespace MiniBrowser.Services;

/// <summary>
/// Настройки в %LOCALAPPDATA%\MiniBrowser\settings.json.
/// Битый файл не роняет браузер — просто откатываемся к значениям по умолчанию.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private AppSettings _current = new();

    public AppSettings Current => _current;

    public double EffectiveZoom => Current.ZoomPercent / 100 * Current.DefaultFontSize / 16;

    public SettingsService(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiniBrowser",
            "settings.json");
        Load();
    }

    public void Save()
    {
        try
        {
            // Пользователь мог выставить значения мимо ползунков — чиним до записи,
            // чтобы на диск никогда не попал мусор.
            Validate(_current);
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_current, JsonOptions));
        }
        catch { /* Настройки не должны ронять браузер */ }
    }

    public static void ResetToDefaults(AppSettings settings)
    {
        var defaults = new AppSettings();
        settings.ZoomPercent = defaults.ZoomPercent;
        settings.WindowWidth = defaults.WindowWidth;
        settings.WindowHeight = defaults.WindowHeight;
        settings.WindowMaximized = defaults.WindowMaximized;
        settings.HomeUrl = defaults.HomeUrl;
        settings.SearchUrl = defaults.SearchUrl;
        settings.ShowStatusBar = defaults.ShowStatusBar;
        settings.DefaultFontSize = defaults.DefaultFontSize;
        settings.TabStripWidth = defaults.TabStripWidth;
        settings.TabStripCollapsed = defaults.TabStripCollapsed;
        settings.RestoreSession = defaults.RestoreSession;
        settings.AdBlockEnabled = defaults.AdBlockEnabled;
        // Список не переиспользуем: сброс обязан отвязать его от дефолтного
        // экземпляра, иначе ClearDisabledHosts() обнулил бы и «настоящие» дефолты.
        settings.AdBlockDisabledHosts = new List<string>(defaults.AdBlockDisabledHosts);
    }

    private void Load()
    {
        try
        {
            // Папки может не быть при первом запуске — создаём заранее,
            // чтобы чтение не падало на ровном месте.
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = File.ReadAllText(_filePath);
            _current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            Validate(_current);
        }
        catch
        {
            // Битый файл, пустой файл, нечисловое поле — во всех случаях
            // JsonSerializer кидает исключение, и мы молча берём дефолты.
            _current = new AppSettings();
        }
    }

    private static void Validate(AppSettings settings)
    {
        // NaN проверяем до Math.Clamp: кламп вернул бы NaN как есть.
        settings.ZoomPercent = Normalize(settings.ZoomPercent, 100, 50, 200);
        settings.DefaultFontSize = Normalize(settings.DefaultFontSize, 16, 13, 20);
        settings.WindowWidth = Normalize(settings.WindowWidth, 1200, 640, 10000);
        settings.WindowHeight = Normalize(settings.WindowHeight, 800, 400, 10000);
        // Полоса вкладок уже указанной ширины: иначе окно нельзя было бы вернуть
        // на место, если значение пришло мусором.
        settings.TabStripWidth = Normalize(settings.TabStripWidth, 240, 150, 420);

        // Пустой URL ломает навигацию, поэтому возвращаем дефолт вместо пустоты.
        if (string.IsNullOrWhiteSpace(settings.HomeUrl))
            settings.HomeUrl = new AppSettings().HomeUrl;
        if (string.IsNullOrWhiteSpace(settings.SearchUrl))
            settings.SearchUrl = new AppSettings().SearchUrl;

        // Старый settings.json без этого поля даёт null, а обращается к нему
        // переключение блокировки — починка обязана быть здесь, не в AdBlockService.
        settings.AdBlockDisabledHosts ??= new List<string>();

        // Мусор в белом списке (пустые строки, URL целиком, нечитаемое) ломает
        // отображение счётчика и сравнение по хосту, поэтому приводим к виду host.
        settings.AdBlockDisabledHosts = settings.AdBlockDisabledHosts
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(NormalizeHost)
            .Where(h => h.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Домен без схемы, www и порта — ключ белого списка блокировки.</summary>
    private static string NormalizeHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return string.Empty;
        var value = host.Trim().ToLowerInvariant();
        if (value.Contains("://"))
        {
            // TryCreate, а не new Uri: мусор в настройках не должен бросать
            // исключение прямо из Validate — иначе Save() молча не сохранит файл.
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return string.Empty;
            value = uri.Host.ToLowerInvariant();
        }
        else
        {
            var colon = value.IndexOf(':');
            if (colon >= 0) value = value[..colon];
        }
        return value.StartsWith("www.", StringComparison.Ordinal) ? value[4..] : value;
    }

    private static double Normalize(double value, double fallback, double min, double max) =>
        double.IsNaN(value) || double.IsInfinity(value) ? fallback : Math.Clamp(value, min, max);
}
