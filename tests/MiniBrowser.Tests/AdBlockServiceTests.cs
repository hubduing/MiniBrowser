using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class AdBlockServiceTests
{
    private static AppSettings NewSettings() => new();

    // ---- Сопоставление доменов ----

    [Theory]
    [InlineData("https://pagead2.googlesyndication.com/pagead/js/adsbygoogle.js")]
    [InlineData("https://securepubads.g.doubleclick.net/tag/js/gpt.js")]
    [InlineData("https://www.google-analytics.com/analytics.js")]
    [InlineData("https://mc.yandex.ru/metrika/watch.js")]
    [InlineData("https://ads.yandex.ru/ads/direct/banner.gif")]
    [InlineData("https://adfox.ru/ads.js")]
    public void IsBlocked_AdAndTrackerUrls_AreBlocked(string url)
    {
        var ad = new AdBlockService(NewSettings());
        Assert.True(ad.IsBlocked(new Uri(url)));
    }

    [Theory]
    [InlineData("https://example.com/")]
    [InlineData("https://google.com/search?q=test")]
    [InlineData("https://notdoubleclick.example/page")]
    [InlineData("https://dgooglesyndication.com.attacker.net/x.js")]
    public void IsBlocked_OrdinaryPages_AreNotBlocked(string url)
    {
        var ad = new AdBlockService(NewSettings());
        Assert.False(ad.IsBlocked(new Uri(url)));
    }

    [Fact]
    public void IsBlocked_SchemeWithoutHost_IsNotBlocked()
    {
        var ad = new AdBlockService(NewSettings());
        // about:, data:, blob: — у них нет host, сопоставление не должно
        // ни падать, ни «случайно» ничего не найти.
        Assert.False(ad.IsBlocked(new Uri("about:blank")));
        Assert.False(ad.IsBlocked(new Uri("data:text/html,<b>x</b>")));
    }

    [Fact]
    public void IsBlocked_SubdomainOfAdHost_IsBlocked()
    {
        var ad = new AdBlockService(NewSettings());
        Assert.True(ad.IsBlocked(new Uri("https://adservice.google.com/pagead/x")));
    }

    [Fact]
    public void IsBlocked_PageInWhitelist_LetsAdDomainsThrough()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        ad.ToggleHost("example.com");

        // Реклама со страницы example.com идёт с доменов Google Ads.
        // Пользователь отключил блокировку на сайте — она там больше не блокируется,
        // хотя сам домен doubleclick.net в списке фильтров остаётся.
        Assert.False(ad.IsBlocked(new Uri("https://doubleclick.net/x.js"), pageHost: "example.com"));
    }

    [Fact]
    public void IsBlocked_OtherPage_StillBlocksAdDomains()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        ad.ToggleHost("example.com");

        // Отключение на одном сайте не должно отключать блокировку на всех.
        Assert.True(ad.IsBlocked(new Uri("https://doubleclick.net/x.js"), pageHost: "other.org"));
    }

    [Fact]
    public void IsBlocked_AdHostWithoutPageHost_StillBlocked()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        ad.ToggleHost("example.com");

        // Сайт неизвестен (запрос до смены страницы) — ведём себя как для
        // обычного сайта: блокируем, а не пропускаем рекламу молча.
        Assert.True(ad.IsBlocked(new Uri("https://doubleclick.net/x.js")));
    }

    [Fact]
    public void IsBlocked_DisabledHost_LeavesOtherSubdomainsBlocked()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        ad.ToggleHost("site.example");

        Assert.False(ad.IsEnabledForHost("site.example"));
        Assert.True(ad.IsEnabledForHost("other.example"));
        Assert.True(ad.IsBlocked(new Uri("https://doubleclick.net/x.js"), pageHost: "other.example"));
    }

    // ---- Мастер-выключатель ----

    [Fact]
    public void MasterSwitch_Off_BlocksNothing()
    {
        var settings = NewSettings();
        settings.AdBlockEnabled = false;
        var ad = new AdBlockService(settings);

        Assert.False(ad.IsBlocked(new Uri("https://doubleclick.net/x.js")));
        Assert.False(ad.IsEnabledForHost("example.com"));
    }

    [Fact]
    public void MasterSwitch_OffThenOn_ResumesBlocking()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        settings.AdBlockEnabled = false;
        Assert.False(ad.IsBlocked(new Uri("https://doubleclick.net/x.js")));

        settings.AdBlockEnabled = true;
        Assert.True(ad.IsBlocked(new Uri("https://doubleclick.net/x.js")));
    }

    // ---- Переключение по хосту ----

    [Fact]
    public void ToggleHost_Twice_ReturnsToOriginal()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);

        Assert.False(ad.ToggleHost("example.com"));
        Assert.True(ad.ToggleHost("example.com"));
    }

    [Fact]
    public void ToggleHost_IsCaseAndWwwInsensitive()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);

        ad.ToggleHost("WWW.Example.COM");
        Assert.Contains("example.com", settings.AdBlockDisabledHosts);
        Assert.False(ad.IsEnabledForHost("example.com"));
    }

    [Fact]
    public void ToggleHost_BlankHost_DoesNothing()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);

        // Пустой host — это about:/data:, а не домен: переключать нечего.
        Assert.True(ad.ToggleHost(""));
        Assert.Empty(settings.AdBlockDisabledHosts);
    }

    [Fact]
    public void ClearDisabledHosts_ReenablesEverywhere()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        ad.ToggleHost("example.com");
        ad.ToggleHost("site.org");

        ad.ClearDisabledHosts();

        Assert.True(ad.IsEnabledForHost("example.com"));
        Assert.True(ad.IsBlocked(new Uri("https://doubleclick.net/x.js"), pageHost: "example.com"));
    }

    [Fact]
    public void DisabledHostsCount_MatchesList()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);

        Assert.Equal(0, ad.DisabledHostsCount);
        ad.ToggleHost("example.com");
        Assert.Equal(1, ad.DisabledHostsCount);
    }

    // ---- Косметика ----

    [Fact]
    public void CosmeticRules_AreNotEmpty()
    {
        Assert.NotEmpty(AdBlockService.CosmeticRules);
    }

    [Theory]
    [InlineData("iframe[src*='doubleclick.net']")]
    [InlineData("[data-ad-slot]")]
    [InlineData("[class~='ad-banner']")]
    public void CosmeticScript_EachSelectorIsPresentAndHidden(string selector)
    {
        var ad = new AdBlockService(NewSettings());
        var script = ad.BuildCosmeticScript("example.com");

        Assert.Contains(selector, script, StringComparison.Ordinal);
        // Список селекторов сам по себе невалиден как CSS: без блока с
        // display:none маскировка не спрятала бы ничего.
        Assert.Contains(" { display: none !important; }", script, StringComparison.Ordinal);
    }

    [Fact]
    public void CosmeticScript_HidesAllSelectorsInOneBlock()
    {
        var ad = new AdBlockService(NewSettings());
        var script = ad.BuildCosmeticScript("example.com");

        // CSS лежит в единственной строке в двойных кавычках — достаём её оттуда.
        var css = script.Split('"').ElementAtOrDefault(1) ?? string.Empty;

        // Список селекторов завершается общим блоком: так скрываются все сразу,
        // а не только последний элемент списка.
        Assert.EndsWith(" { display: none !important; }", css, StringComparison.Ordinal);
        Assert.StartsWith(AdBlockService.CosmeticRules[0], css, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildCosmeticScript_ForDisabledHost_ProducesEmptyCss()
    {
        var settings = NewSettings();
        var ad = new AdBlockService(settings);
        ad.ToggleHost("example.com");

        // Заблокированный CSS для отключённого сайта должен быть пустым,
        // иначе скрипт спрячет рекламу там, где пользователь её оставил.
        var script = ad.BuildCosmeticScript("example.com");
        Assert.DoesNotContain("doubleclick", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildCosmeticScript_ForEnabledHost_ContainsRules()
    {
        var ad = new AdBlockService(NewSettings());
        var script = ad.BuildCosmeticScript("example.com");
        // Скрипт создаёт <style> в рантайме, поэтому проверяем не текст тега,
        // а сами селекторы — они и есть полезная нагрузка.
        Assert.Contains("[data-ad-slot]", script, StringComparison.Ordinal);
        Assert.Contains("createElement('style')", script, StringComparison.Ordinal);
    }
}