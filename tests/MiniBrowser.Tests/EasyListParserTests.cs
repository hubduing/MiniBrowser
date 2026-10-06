using MiniBrowser.Services;
using Xunit;

public class EasyListParserTests
{
    private static EasyListRuleSet Parse(string? text) => EasyListParser.Parse(text);

    // ---- Домены из сетевых правил ----

    [Fact]
    public void Parse_ExtractsDomainFromPlainRule()
    {
        Assert.Contains("doubleclick.net", Parse("||doubleclick.net^").Domains);
    }

    [Theory]
    [InlineData("||example.com^$third-party", "example.com")]
    [InlineData("||ads.example.org/path/to/file.js", "ads.example.org")]
    [InlineData("||tracker.net|", "tracker.net")]
    [InlineData("||no-separator.example", "no-separator.example")]
    public void Parse_CutsRuleAtSeparator(string rule, string expected)
    {
        Assert.Contains(expected, Parse(rule).Domains);
    }

    [Fact]
    public void Parse_LowercasesDomain()
    {
        // Регистр в списке встречается, а host из URL приходит уже в нижнем.
        Assert.Contains("doubleclick.net", Parse("||DoubleClick.NET^").Domains);
    }

    [Fact]
    public void Parse_IgnoresCosmeticRules()
    {
        // Косметика требует движка селекторов с :has-text(); мы её не разбираем.
        var set = Parse("example.com##.ad-banner\nexample.com#@#.sponsored\nexample.com#$#.title");
        Assert.Empty(set.Domains);
    }

    [Fact]
    public void Parse_IgnoresCommentsAndHeader()
    {
        var set = Parse("[Adblock Plus 2.0]\n! Title: EasyList\n! Version: 20261006\n");
        Assert.Empty(set.Domains);
        Assert.Empty(set.Exceptions);
    }

    [Fact]
    public void Parse_IgnoresRulesWithoutDomainAnchor()
    {
        // Правила без || привязаны к конкретному домену или к имени файла,
        // а не к сети: применять их всюду нельзя, в общий список они не идут.
        var set = Parse("||banner.gif$image\nexample.com/ads/*$script\n/ads/banner.js");
        Assert.Empty(set.Domains);
    }

    [Fact]
    public void Parse_IgnoresBlankAndMalformedLines()
    {
        var set = Parse("\n||\n||^\n||   \n||\n||ok.example^\n");
        Assert.Equal(new[] { "ok.example" }, set.Domains);
    }

    // ---- Исключения (@@) ----

    [Fact]
    public void Parse_CollectsExceptionDomain()
    {
        var set = Parse("@@||safe.example^$script");
        Assert.Contains("safe.example", set.Exceptions);
        Assert.DoesNotContain("safe.example", set.Domains);
    }

    [Fact]
    public void Parse_ExceptionAndBlockForSameDomain_BothRecorded()
    {
        // Нас интересует только домен: конкретное исключение по типу запроса
        // мы не разбираем, поэтому домен попадает в оба набора.
        var set = Parse("||mixed.example^\n@@||mixed.example^$script");
        Assert.Contains("mixed.example", set.Domains);
        Assert.Contains("mixed.example", set.Exceptions);
    }

    // ---- Масштаб и устойчивость ----

    [Fact]
    public void Parse_RealisticFile_ProducesLargeDomainSet()
    {
        // 3000 правил — кусок настоящего EasyList по объёму.
        var text = string.Join('\n',
            Enumerable.Range(0, 3000).Select(i => $"||host{i}.example^$third-party"));
        Assert.Equal(3000, Parse(text).Domains.Count);
    }

    [Fact]
    public void Parse_DuplicatedDomains_AreDeduplicated()
    {
        Assert.Single(Parse("||dup.example^\n||dup.example^$image\n||DUP.EXAMPLE^").Domains);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptySets()
    {
        var set = Parse(string.Empty);
        Assert.Empty(set.Domains);
        Assert.Empty(set.Exceptions);
    }

    [Fact]
    public void Parse_NullInput_DoesNotThrow()
    {
        Assert.Empty(Parse(null!).Domains);
    }

    [Fact]
    public void Matches_DomainAndSubdomainAreBlocked()
    {
        var set = Parse("||adnet.example^\n");
        Assert.True(set.Matches("adnet.example"));
        Assert.True(set.Matches("cdn.adnet.example"));
        Assert.False(set.Matches("notadnet.example"));
    }

    [Fact]
    public void Matches_ExceptionBeatsBlock()
    {
        var set = Parse("||adnet.example^\n@@||good.adnet.example^");
        Assert.True(set.Matches("adnet.example"));
        Assert.False(set.Matches("good.adnet.example"));
    }
}