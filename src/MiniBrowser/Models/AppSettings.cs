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
}
