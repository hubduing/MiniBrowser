using MiniBrowser.Services;
using Xunit;

namespace MiniBrowser.Tests;

public class ReloadStopPolicyTests
{
    [Fact]
    public void Decide_WhileLoading_Stops()
    {
        Assert.Equal(ReloadStopAction.Stop, ReloadStopPolicy.Decide(isLoading: true));
    }

    [Fact]
    public void Decide_WhenSettled_Reloads()
    {
        Assert.Equal(ReloadStopAction.Reload, ReloadStopPolicy.Decide(isLoading: false));
    }

    [Fact]
    public void Glyph_MatchesAction()
    {
        Assert.Equal("✕", ReloadStopPolicy.Glyph(isLoading: true));
        Assert.Equal("⟳", ReloadStopPolicy.Glyph(isLoading: false));
    }

    [Fact]
    public void ToolTip_DescribesAction()
    {
        Assert.Equal("Остановить загрузку", ReloadStopPolicy.ToolTip(isLoading: true));
        Assert.Equal("Обновить (Ctrl+R)", ReloadStopPolicy.ToolTip(isLoading: false));
    }
}