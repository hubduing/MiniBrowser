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
    [InlineData("Google", "https://www.google.com/search?q=%D0%BA%D0%BE%D1%82%D1%8B")]
    [InlineData("Bing", "https://www.bing.com/search?q=%D0%BA%D0%BE%D1%82%D1%8B")]
    [InlineData("DuckDuckGo", "https://duckduckgo.com/?q=%D0%BA%D0%BE%D1%82%D1%8B")]
    [InlineData("Яндекс", "https://yandex.ru/search/?text=%D0%BA%D0%BE%D1%82%D1%8B")]
    public void BuildUrl_Query_UsesGivenEngine(string engine, string expectedUrl)
    {
        // Кириллический запрос — самый частый случай: проверяем не только префикс,
        // но и экранирование целиком.
        Assert.Equal(expectedUrl, NavigationService.BuildUrl("коты", NavigationService.SearchEngine(engine)));
    }

    [Fact]
    public void SearchEngine_UnknownName_FallsBackToGoogle()
    {
        Assert.Equal(NavigationService.SearchUrlTemplate, NavigationService.SearchEngine("Что-то"));
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

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void BuildUrl_EmptyOrNullTemplate_FallsBackToDefault(string? template)
    {
        // Пустое поле настроек не должно ломать адресную строку — ищем по Google.
        Assert.Equal("https://www.google.com/search?q=%D0%BA%D0%BE%D1%82%D1%8B",
            NavigationService.BuildUrl("коты", template!));
    }

    [Fact]
    public void BuildUrl_TemplateWithExtraBraces_DoesNotThrow()
    {
        // Лишняя скобка в ручной правке настроек — не повод ронять ввод:
        // меняем только {0}, остальное оставляем как есть.
        var result = NavigationService.BuildUrl("a b", "https://s.example/?q={0}&x={1}");
        Assert.Equal("https://s.example/?q=a%20b&x={1}", result);
    }
}
