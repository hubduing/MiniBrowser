using System.Threading;
using System.Windows;
using System.Windows.Controls;
using MiniBrowser.Services;
using Xunit;

namespace MiniBrowser.Tests;

/// <summary>
/// Структура контекстных меню полосы вкладок. Главный регресс: подменю
/// обязано быть дочерними MenuItem, а не вложенным ContextMenu — второе WPF
/// запрещает и роняет процесс.
/// </summary>
public class TabContextMenuFactoryTests
{
    /// <summary>WPF-контролы живут только на STA-потоке.</summary>
    private static void OnSta(Action action)
    {
        Exception? captured = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { captured = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (captured is not null) throw captured;
    }

    private static MenuItem Item(ItemCollection items, int index) => (MenuItem)items[index];

    [Fact]
    public void TabMenu_UsesMenuItemSubmenus_NotNestedContextMenus()
    {
        OnSta(() =>
        {
            var menu = new ContextMenu();
            TabContextMenuFactory.FillTabMenu(
                menu,
                new (string, bool, int)[]
                {
                    ("Работа", true, 0),
                    ("Новости", false, 1),
                    ("Прочее", false, 2),
                },
                _ => { }, () => { }, () => { }, () => { });

            Assert.Equal(5, menu.Items.Count);
            Assert.Equal("Переместить в группу", Item(menu.Items, 0).Header);
            Assert.IsType<Separator>(menu.Items[3]);
            Assert.Equal("Закрыть вкладку", Item(menu.Items, 4).Header);

            var move = Item(menu.Items, 0);
            Assert.Equal(3, move.Items.Count);
            foreach (var child in move.Items)
            {
                // Ни один пункт подменю не может быть ContextMenu: это и есть
                // причина падения «ContextMenu cannot have a logical parent».
                Assert.IsNotType<ContextMenu>(child);
                Assert.IsType<MenuItem>(child);
            }
        });
    }

    [Fact]
    public void TabMenu_MarksCurrentGroup_AndDisablesIt()
    {
        OnSta(() =>
        {
            var menu = new ContextMenu();
            TabContextMenuFactory.FillTabMenu(
                menu,
                new (string, bool, int)[] { ("Работа", false, 0), ("Новости", true, 1) },
                _ => { }, () => { }, () => { }, () => { });

            var move = Item(menu.Items, 0);
            var current = (MenuItem)move.Items[1];
            var other = (MenuItem)move.Items[0];

            Assert.StartsWith("\u2713", (string)current.Header);
            Assert.False(current.IsEnabled);
            Assert.Equal("Работа", other.Header);
            Assert.True(other.IsEnabled);
        });
    }

    [Fact]
    public void TabMenu_MoveItem_InvokesCallback_WithTargetGroup()
    {
        OnSta(() =>
        {
            var menu = new ContextMenu();
            var moved = new List<int>();
            TabContextMenuFactory.FillTabMenu(
                menu,
                new (string, bool, int)[] { ("Работа", true, 0), ("Новости", false, 1), ("Прочее", false, 2) },
                target => moved.Add(target), () => { }, () => { }, () => { });

            var move = Item(menu.Items, 0);
            ((MenuItem)move.Items[2]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(new[] { 2 }, moved);
        });
    }

    [Fact]
    public void TabMenu_ActionItems_InvokeTheirCallbacks()
    {
        OnSta(() =>
        {
            var menu = new ContextMenu();
            var log = new List<string>();
            TabContextMenuFactory.FillTabMenu(
                menu,
                Array.Empty<(string, bool, int)>(),
                _ => { },
                () => log.Add("create"),
                () => log.Add("leave"),
                () => log.Add("close"));

            Item(menu.Items, 1).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Item(menu.Items, 2).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Item(menu.Items, 4).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(new[] { "create", "leave", "close" }, log);
        });
    }

    [Fact]
    public void GroupMenu_ColorSubmenu_UsesMenuItems_WithColorIcons()
    {
        OnSta(() =>
        {
            var menu = new ContextMenu();
            TabContextMenuFactory.FillGroupMenu(
                menu, isCollapsed: false, paletteSize: 8,
                colorIcon: _ => new Border(),
                rename: () => { }, pickColor: _ => { }, toggleCollapse: () => { }, closeGroup: () => { });

            var color = Item(menu.Items, 1);
            Assert.Equal("Сменить цвет", color.Header);
            Assert.Equal(8, color.Items.Count);
            foreach (var child in color.Items)
            {
                Assert.IsNotType<ContextMenu>(child);
                Assert.IsType<MenuItem>(child);
                Assert.NotNull(((MenuItem)child).Icon);
            }
        });
    }

    [Fact]
    public void GroupMenu_PickColor_InvokesCallback_WithIndex()
    {
        OnSta(() =>
        {
            var menu = new ContextMenu();
            var picked = new List<int>();
            TabContextMenuFactory.FillGroupMenu(
                menu, isCollapsed: false, paletteSize: 8,
                colorIcon: _ => null,
                rename: () => { }, pickColor: index => picked.Add(index), toggleCollapse: () => { }, closeGroup: () => { });

            ((MenuItem)Item(menu.Items, 1).Items[5]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));

            Assert.Equal(new[] { 5 }, picked);
        });
    }

    [Fact]
    public void GroupMenu_CollapseLabel_ReflectsState()
    {
        OnSta(() =>
        {
            var collapsed = new ContextMenu();
            TabContextMenuFactory.FillGroupMenu(
                collapsed, isCollapsed: true, paletteSize: 8, colorIcon: _ => null,
                rename: () => { }, pickColor: _ => { }, toggleCollapse: () => { }, closeGroup: () => { });
            Assert.Equal("Развернуть", Item(collapsed.Items, 2).Header);

            var expanded = new ContextMenu();
            TabContextMenuFactory.FillGroupMenu(
                expanded, isCollapsed: false, paletteSize: 8, colorIcon: _ => null,
                rename: () => { }, pickColor: _ => { }, toggleCollapse: () => { }, closeGroup: () => { });
            Assert.Equal("Свернуть", Item(expanded.Items, 2).Header);
        });
    }
}