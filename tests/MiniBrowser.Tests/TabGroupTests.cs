using MiniBrowser.Models;
using Xunit;

public class TabGroupTests
{
    [Fact]
    public void Count_FollowsTabCollection()
    {
        var g = new TabGroup { Name = "Работа" };
        Assert.Equal(0, g.Count);
        g.Tabs.Add(new Tab());
        g.Tabs.Add(new Tab());
        Assert.Equal(2, g.Count);
    }

    [Fact]
    public void Rename_RaisesPropertyChangedOnce()
    {
        var g = new TabGroup { Name = "Работа" };
        var changed = new List<string?>();
        g.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        g.Name = "Работа";      // то же значение — события быть не должно
        g.Name = "Учёба";

        Assert.Equal(new[] { nameof(TabGroup.Name) }, changed);
        Assert.Equal("Учёба", g.Name);
    }

    [Fact]
    public void Collapse_RaisesPropertyChanged()
    {
        var g = new TabGroup();
        var changed = new List<string?>();
        g.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        g.IsCollapsed = true;

        Assert.Equal(new[] { nameof(TabGroup.IsCollapsed) }, changed);
    }

    [Fact]
    public void TabCollection_RaisesCountChanged()
    {
        var g = new TabGroup();
        var countNotifications = 0;
        g.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabGroup.Count)) countNotifications++;
        };

        g.Tabs.Add(new Tab());
        g.Tabs.RemoveAt(0);

        // Add и Remove обязаны уведомить: иначе бейдж «N» на заголовке застыл бы.
        Assert.Equal(2, countNotifications);
    }

    [Fact]
    public void Tab_GetsStableIdFromConstructor()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, new Tab(id).Id);
        Assert.NotEqual(Guid.Empty, new Tab().Id);
    }
}