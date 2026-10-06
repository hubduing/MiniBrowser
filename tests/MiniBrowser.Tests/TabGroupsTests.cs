using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class TabGroupsTests
{
    [Fact]
    public void AddTab_WithoutGroups_CreatesFirstGroup()
    {
        var groups = new TabGroups();
        var tab = groups.AddTab();
        Assert.Single(groups.Groups);
        Assert.Equal("Группа 1", groups.Groups[0].Name);
        Assert.Equal(tab, groups.ActiveTab);
    }

    [Fact]
    public void AddTab_WithoutGroup_GoesToActiveGroup()
    {
        var groups = new TabGroups();
        var first = groups.AddTab();
        var second = groups.AddTab();
        var group = groups.ActiveGroup!;
        Assert.Equal(2, group.Count);
        Assert.Equal(new[] { first, second }, groups.Tabs.ToArray());
        Assert.Equal(group.Id, second.GroupId);
    }

    [Fact]
    public void AddTab_WithExplicitGroup_JoinsItsEnd()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var tab = groups.AddTab(b);
        Assert.Equal(0, a.Count);
        Assert.Single(b.Tabs);
        Assert.Equal(tab, groups.ActiveTab);
    }

    [Fact]
    public void CreateGroup_AutoColor_DoesNotRepeatExisting()
    {
        var groups = new TabGroups();
        Assert.Equal(0, groups.CreateGroup("А").ColorIndex);
        Assert.Equal(1, groups.CreateGroup("Б").ColorIndex);
        // Восемь групп — палитра кончилась, цикл начинается заново.
        for (var i = 0; i < 6; i++) groups.CreateGroup($"Г{i}");
        Assert.InRange(groups.CreateGroup("Хвост").ColorIndex, 0, 7);
    }

    [Fact]
    public void RemoveTab_KeepsGroup()
    {
        var groups = new TabGroups();
        var group = groups.CreateGroup("А");
        var tab = groups.AddTab(group);
        groups.RemoveTab(tab);
        Assert.Single(groups.Groups);
        Assert.Equal(0, group.Count);
        Assert.Empty(groups.Tabs);
    }

    [Fact]
    public void RemoveGroup_TabsLeaveTheManager()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var inA = groups.AddTab(a);
        groups.AddTab(b);
        Assert.True(groups.RemoveGroup(a));
        Assert.Single(groups.Groups);
        Assert.DoesNotContain(inA, groups.Tabs);
        Assert.False(groups.RemoveGroup(a));
    }

    [Fact]
    public void MoveTab_ReordersWithinGroup()
    {
        var groups = new TabGroups();
        var group = groups.CreateGroup("А");
        var first = groups.AddTab(group);
        var second = groups.AddTab(group);
        var third = groups.AddTab(group);

        Assert.True(groups.MoveTab(third, group, 0));

        Assert.Equal(new[] { third, first, second }, group.Tabs.ToArray());
        Assert.Equal(new[] { third, first, second }, groups.Tabs.ToArray());
    }

    [Fact]
    public void MoveTab_ToOtherGroup_KeepsSourceOrderOfOthers()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var t1 = groups.AddTab(a);
        var t2 = groups.AddTab(a);
        var t3 = groups.AddTab(a);

        groups.MoveTab(t2, b, 0);

        Assert.Equal(new[] { t1, t3 }, a.Tabs.ToArray());
        Assert.Equal(new[] { t2 }, b.Tabs.ToArray());
        Assert.Equal(b.Id, t2.GroupId);
        // Сквозной порядок для горячих клавиш идёт по группам.
        Assert.Equal(new[] { t1, t3, t2 }, groups.Tabs.ToArray());
    }

    [Fact]
    public void MoveTab_ToSamePositionOfSameGroup_IsNoOp()
    {
        var groups = new TabGroups();
        var group = groups.CreateGroup("А");
        var t1 = groups.AddTab(group);
        var t2 = groups.AddTab(group);
        var changed = 0;
        groups.Changed += () => changed++;

        Assert.False(groups.MoveTab(t1, group, 0));

        Assert.Equal(0, changed);
        Assert.Equal(new[] { t1, t2 }, group.Tabs.ToArray());
    }

    [Fact]
    public void MoveGroup_ReordersColumns()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var c = groups.CreateGroup("В");
        groups.AddTab(a);
        groups.AddTab(b);
        groups.AddTab(c);

        Assert.True(groups.MoveGroup(c, 0));

        Assert.Equal(new[] { c, a, b }, groups.Groups.ToArray());
        Assert.Equal(c.Tabs[0], groups.Tabs[0]);
    }

    [Fact]
    public void SetActive_KeepsFlatOrderUnchanged()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var t1 = groups.AddTab(a);
        groups.AddTab(b);
        var order = groups.Tabs.ToArray();

        // Цель в свёрнутой группе активируется: сворачивание её не мешает.
        b.IsCollapsed = true;
        Assert.True(groups.SetActive(t1));

        Assert.True(groups.ActiveTab!.IsActive);
        Assert.Equal(order, groups.Tabs.ToArray());
    }

    [Fact]
    public void SetActive_UnknownTab_ReturnsFalse()
    {
        var groups = new TabGroups();
        groups.AddTab();
        Assert.False(groups.SetActive(new Tab()));
        Assert.False(groups.SetActive(null));
    }

    [Fact]
    public void Restored_Layout_KeepsOrderAndActivatesFirst()
    {
        var a = new TabGroup { Name = "А", ColorIndex = 3, IsCollapsed = true };
        a.Tabs.Add(new Tab { Url = "https://a.example/" });
        var b = new TabGroup { Name = "Б" };
        b.Tabs.Add(new Tab { Url = "https://b.example/" });

        var groups = new TabGroups(new[] { a, b });

        Assert.Equal(new[] { a, b }, groups.Groups.ToArray());
        Assert.Equal(3, a.ColorIndex);
        Assert.True(a.IsCollapsed);
        Assert.Equal(2, groups.Tabs.Count);
        Assert.Equal("https://a.example/", groups.ActiveTab!.Url);
    }
}