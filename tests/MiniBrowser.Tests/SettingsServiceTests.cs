using System.IO;
using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class SettingsServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "mb-tests", Guid.NewGuid().ToString("N"), "settings.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var service = new SettingsService(_path);
        Assert.Equal(100, service.Current.ZoomPercent);
        Assert.Equal("https://www.google.com/", service.Current.HomeUrl);
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");
        var service = new SettingsService(_path);
        Assert.Equal(100, service.Current.ZoomPercent);
    }

    [Fact]
    public void Load_EmptyFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "");
        var service = new SettingsService(_path);
        Assert.Equal(1200, service.Current.WindowWidth);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AllValues()
    {
        var first = new SettingsService(_path);
        first.Current.ZoomPercent = 175;
        first.Current.WindowWidth = 1600;
        first.Current.WindowHeight = 900;
        first.Current.WindowMaximized = true;
        first.Current.ShowStatusBar = false;
        first.Current.DefaultFontSize = 20;
        first.Current.HomeUrl = "https://example.org/";
        first.Current.SearchUrl = "https://example.org/search?q={0}";
        first.Current.TabStripCollapsed = true;
        first.Save();

        var second = new SettingsService(_path);
        Assert.Equal(175, second.Current.ZoomPercent);
        Assert.Equal(1600, second.Current.WindowWidth);
        Assert.Equal(900, second.Current.WindowHeight);
        Assert.True(second.Current.WindowMaximized);
        Assert.False(second.Current.ShowStatusBar);
        Assert.Equal(20, second.Current.DefaultFontSize);
        Assert.Equal("https://example.org/", second.Current.HomeUrl);
        Assert.Equal("https://example.org/search?q={0}", second.Current.SearchUrl);
        Assert.True(second.Current.TabStripCollapsed);
    }

    [Theory]
    [InlineData(500, 200)]
    [InlineData(0, 50)]
    [InlineData(100, 100)]
    public void Load_OutOfRangeZoom_ClampsToBounds(double written, double expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, $"{{ \"ZoomPercent\": {written} }}");
        var service = new SettingsService(_path);
        Assert.Equal(expected, service.Current.ZoomPercent);
    }

    [Theory]
    [InlineData(-100, 640)]
    [InlineData(10000, 10000)]
    public void Load_OutOfRangeWindowSize_StaysClickable(double written, double expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, $"{{ \"WindowWidth\": {written} }}");
        var service = new SettingsService(_path);
        Assert.Equal(expected, service.Current.WindowWidth);
    }

    [Fact]
    public void Load_UnknownProperties_AreIgnoredAndMissingFieldsDefault()
    {
        // В файле может остаться мусор от будущих версий или ручных правок —
        // разбор не должен падать, а отсутствующие поля берут значения по умолчанию.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"ZoomPercent\": 150, \"SomeFutureOption\": true }");
        var service = new SettingsService(_path);
        Assert.Equal(150, service.Current.ZoomPercent);
        Assert.Equal(1200, service.Current.WindowWidth);
        Assert.Equal("https://www.google.com/", service.Current.HomeUrl);
    }

    [Fact]
    public void Load_NotANumberZoom_FallsBackToDefault()
    {
        // System.Text.Json кидает исключение на нечисловом поле — это тот же путь,
        // что битый файл, и результат должен быть тем же.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"ZoomPercent\": \"не число\" }");
        var service = new SettingsService(_path);
        Assert.Equal(100, service.Current.ZoomPercent);
    }

    [Fact]
    public void EffectiveZoom_CombinesZoomAndFontSize()
    {
        var service = new SettingsService(_path);
        service.Current.ZoomPercent = 200;
        service.Current.DefaultFontSize = 20;
        Assert.Equal(2.5, service.EffectiveZoom, precision: 5);
    }

    [Fact]
    public void EffectiveZoom_DefaultsToOne()
    {
        Assert.Equal(1.0, new SettingsService(_path).EffectiveZoom, precision: 5);
    }

    [Fact]
    public void ResetToDefaults_RestoresEveryValue()
    {
        var settings = new AppSettings
        {
            ZoomPercent = 50,
            WindowWidth = 1024,
            WindowHeight = 600,
            WindowMaximized = true,
            HomeUrl = "https://x/",
            SearchUrl = "https://y/{0}",
            ShowStatusBar = false,
            DefaultFontSize = 13,
            TabStripCollapsed = true,
            AdBlockEnabled = false,
            AdBlockDisabledHosts = new List<string> { "example.com" },
        };
        SettingsService.ResetToDefaults(settings);
        Assert.Equal(100, settings.ZoomPercent);
        Assert.Equal(1200, settings.WindowWidth);
        Assert.Equal(800, settings.WindowHeight);
        Assert.False(settings.WindowMaximized);
        Assert.Equal("https://www.google.com/", settings.HomeUrl);
        Assert.Equal("https://www.google.com/search?q={0}", settings.SearchUrl);
        Assert.True(settings.ShowStatusBar);
        Assert.Equal(16, settings.DefaultFontSize);
        Assert.False(settings.TabStripCollapsed);
        Assert.True(settings.AdBlockEnabled);
        Assert.Empty(settings.AdBlockDisabledHosts);
    }

    [Fact]
    public void ResetToDefaults_DoesNotShareListWithNewDefaults()
    {
        // Сброс обязан отвязать список от того дефолтного экземпляра:
        // иначе очистка белого списка обнулила бы и будущие дефолты.
        var settings = new AppSettings
        {
            AdBlockDisabledHosts = new List<string> { "example.com" },
        };
        SettingsService.ResetToDefaults(settings);
        Assert.Empty(settings.AdBlockDisabledHosts);

        // Дальше пользователь добавляет домен — и «чистые» дефолты
        // (создаваемые заново на каждый ResetToDefaults) не должны пострадать.
        settings.AdBlockDisabledHosts.Add("site.org");
        var second = new AppSettings();
        SettingsService.ResetToDefaults(second);
        Assert.Empty(second.AdBlockDisabledHosts);
    }

    [Fact]
    public void Load_OldFileWithoutAdBlockFields_UsesSafeDefaults()
    {
        // settings.json, записанный до появления блокировки: полей нет вовсе.
        // Список обязан быть живым, иначе первое переключение упало бы в null.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"ZoomPercent\": 100 }");
        var service = new SettingsService(_path);

        Assert.True(service.Current.AdBlockEnabled);
        Assert.NotNull(service.Current.AdBlockDisabledHosts);
        Assert.Empty(service.Current.AdBlockDisabledHosts);
    }

    [Fact]
    public void Load_MessyWhitelist_IsNormalizedAndDeduped()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"AdBlockDisabledHosts\": [\"WWW.Example.com\", \"example.com\", \"\", \"http://site.org/path\", \"site.org:8080\"] }");
        var service = new SettingsService(_path);

        Assert.Equal(new[] { "example.com", "site.org" }, service.Current.AdBlockDisabledHosts);
    }

    [Fact]
    public void Load_UnparsableWhitelistEntry_DoesNotBreakSettings()
    {
        // Кривой домен обязан отсеяться молча: иначе исключение из Validate
        // уронило бы Save(), и файл настроек перестал бы сохраняться вовсе.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"ZoomPercent\": 150, \"AdBlockDisabledHosts\": [\"http://\", \"good.example\"] }");
        var service = new SettingsService(_path);

        Assert.Equal(150, service.Current.ZoomPercent);
        Assert.Equal(new[] { "good.example" }, service.Current.AdBlockDisabledHosts);

        // И запись после починки обязана пройти.
        service.Current.WindowWidth = 1000;
        service.Save();
        Assert.Contains("good.example", File.ReadAllText(_path));
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AdBlockState()
    {
        var first = new SettingsService(_path);
        first.Current.AdBlockEnabled = false;
        first.Current.AdBlockDisabledHosts.Add("example.com");
        first.Save();

        var second = new SettingsService(_path);
        Assert.False(second.Current.AdBlockEnabled);
        Assert.Equal(new[] { "example.com" }, second.Current.AdBlockDisabledHosts);
    }
}
