namespace MiniBrowser.Models;

/// <summary>Настройки браузера с разумными значениями по умолчанию.</summary>
public sealed class AppSettings
{
    public double ZoomPercent { get; set; } = 100;
    public double WindowWidth { get; set; } = 1200;
    public double WindowHeight { get; set; } = 800;
    public bool WindowMaximized { get; set; } = false;
    public string HomeUrl { get; set; } = "https://www.google.com/";
    public string SearchUrl { get; set; } = "https://www.google.com/search?q={0}";
    public bool ShowStatusBar { get; set; } = true;
    public double DefaultFontSize { get; set; } = 16;

    /// <summary>Ширина вертикальной полосы вкладок слева.</summary>
    public double TabStripWidth { get; set; } = 240;

    /// <summary>Панель вкладок скрыта кнопкой «≡»; состояние переживает перезапуск.</summary>
    public bool TabStripCollapsed { get; set; } = false;

    /// <summary>
    /// Восстанавливать раскладку групп и вкладок при запуске. По умолчанию да:
    /// группы без этого терялись бы при каждом перезапуске браузера.
    /// </summary>
    public bool RestoreSession { get; set; } = true;

    /// <summary>Блокировка рекламы включена по умолчанию; выключается кнопкой или в настройках.</summary>
    public bool AdBlockEnabled { get; set; } = true;

    /// <summary>Домены, где пользователь отключил блокировку (нормализованные host).</summary>
    public List<string> AdBlockDisabledHosts { get; set; } = new();
}
