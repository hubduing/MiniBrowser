using System.Windows;
using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class TabDropResolverTests
{
    private static DropColumn Column(TabGroup group, double x, int rows = 3, bool collapsed = false)
    {
        var tabs = new List<DropTabRow>();
        for (var i = 0; i < rows; i++)
        {
            var tab = new Tab();
            group.Tabs.Add(tab);
            tabs.Add(new DropTabRow(new Rect(x, 28 + i * 30, 200, 30), tab));
        }
        // Колонка в разметке выше своего содержимого: под последней вкладкой
        // остаётся пустое место колонки, и попадание туда тоже значит «в конец».
        var height = collapsed ? 32 : 28 + rows * 30 + 80;
        return new DropColumn(group, new Rect(x, 0, 200, height), collapsed, tabs);
    }

    [Fact]
    public void NoColumns_IsNone()
    {
        Assert.Equal(DropKind.None,
            TabDropResolver.Resolve(new Point(10, 10), Array.Empty<DropColumn>()).Kind);
    }

    [Fact]
    public void PointerInGapBetweenColumns_IsNone()
    {
        var a = Column(new TabGroup { Name = "А" }, 0);
        var b = Column(new TabGroup { Name = "Б" }, 260);
        var result = TabDropResolver.Resolve(new Point(215, 40), new[] { a, b });
        Assert.Equal(DropKind.None, result.Kind);
        Assert.Null(result.Group);
    }

    [Fact]
    public void PointerOnHeader_DropsToGroupEnd()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        var result = TabDropResolver.Resolve(new Point(100, 10), new[] { column });
        Assert.Equal(DropKind.Group, result.Kind);
        Assert.Same(group, result.Group);
        Assert.Equal(3, result.Index);
    }

    [Fact]
    public void PointerOnCollapsedColumn_DropsToGroupEnd()
    {
        var group = new TabGroup { Name = "А", IsCollapsed = true };
        var column = Column(group, 0, collapsed: true);
        var result = TabDropResolver.Resolve(new Point(10, 15), new[] { column });
        Assert.Equal(DropKind.Group, result.Kind);
        Assert.Same(group, result.Group);
    }

    [Fact]
    public void PointerAboveFirstTabMiddle_InsertsAtZero()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        // Y = 40 — в верхней половине первого таба (28..43).
        var result = TabDropResolver.Resolve(new Point(100, 40), new[] { column });
        Assert.Equal(DropKind.BetweenTabs, result.Kind);
        Assert.Equal(0, result.Index);
    }

    [Fact]
    public void PointerBetweenTabs_InsertsAtThatIndex()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        // Y = 70 — середина второго таба (58 + 15), значит вставка перед ним.
        var result = TabDropResolver.Resolve(new Point(100, 70), new[] { column });
        Assert.Equal(DropKind.BetweenTabs, result.Kind);
        Assert.Equal(1, result.Index);
    }

    [Fact]
    public void PointerBelowLastTab_Appends()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        // Ниже последней вкладки, но внутри колонки — вставка в конец.
        var result = TabDropResolver.Resolve(new Point(100, 130), new[] { column });
        Assert.Equal(DropKind.BetweenTabs, result.Kind);
        Assert.Equal(3, result.Index);
    }

    [Fact]
    public void EmptyExpandedColumn_DropsToGroup()
    {
        var group = new TabGroup { Name = "Пусто" };
        var column = new DropColumn(group, new Rect(0, 0, 200, 40), false, Array.Empty<DropTabRow>());
        var result = TabDropResolver.Resolve(new Point(100, 35), new[] { column });
        Assert.Equal(DropKind.Group, result.Kind);
        Assert.Equal(0, result.Index);
    }
}