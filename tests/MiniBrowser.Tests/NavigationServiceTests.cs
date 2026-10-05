using MiniBrowser.Services;
using Xunit;

public class NavigationServiceTests
{
    [Fact]
    public void BuildUrl_EmptyInput_ReturnsNull()
    {
        Assert.Null(NavigationService.BuildUrl("   ", "{0}"));
    }

    [Fact]
    public void BuildUrl_ExplicitScheme_KeepsInput()
    {
        Assert.Equal("http://a.example/x",
            NavigationService.BuildUrl("http://a.example/x", "{0}"));
    }

    [Fact]
    public void BuildUrl_LooksLikeDomain_PrefersHttps()
    {
        Assert.Equal("https://example.com",
            NavigationService.BuildUrl("example.com", "{0}"));
    }

    [Theory]
    [InlineData("Google", "https://www.google.com/search?q=")]
    [InlineData("Bing", "https://www.bing.com/search?q=")]
    [InlineData("DuckDuckGo", "https://duckduckgo.com/?q=")]
    [InlineData("Яндекс", "https://yandex.ru/search/?text=")]
    public void BuildUrl_Query_UsesGivenEngine(string engine, string expectedPrefix)
    {
        Assert.StartsWith(expectedPrefix, NavigationService.BuildUrl("коты", NavigationService.SearchEngine(engine)));
    }

    [Fact]
    public void SearchEngine_UnknownName_FallsBackToGoogle()
    {
        Assert.Equal(NavigationService.SearchEngine("Google"), NavigationService.SearchEngine("Что-то"));
    }

    [Fact]
    public void EngineNames_ContainsFourEnginesInUiOrder()
    {
        Assert.Equal(new[] { "Google", "Bing", "DuckDuckGo", "Яндекс" },
            NavigationService.EngineNames);
    }

    [Fact]
    public void BuildUrl_QueryWithoutPlaceholder_StillEscapesInput()
    {
        // Пользователь мог дописать в поле настроек шаблон без {0} — не падаем,
        // а подставляем запрос в конец.
        var result = NavigationService.BuildUrl("a b", "https://s.example/?");
        Assert.Equal("https://s.example/?a%20b", result);
    }
}
