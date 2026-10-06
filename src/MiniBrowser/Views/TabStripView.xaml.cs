using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniBrowser.Models;
using MiniBrowser.Services;

namespace MiniBrowser.Views;

/// <summary>
/// Вертикальная полоса групп вкладок слева: колонки, перетаскивание вкладок и
/// групп, контекстные меню. Решения принимает не она, а <see cref="TabManager"/>
/// и <see cref="TabDropResolver"/>: здесь только геометрия и события наверх.
/// </summary>
public partial class TabStripView : UserControl
{
    private readonly List<GroupColumnView> _columns = new();

    public event Action<Tab>? TabActivated;
    public event Action<Tab>? TabCloseRequested;
    public event Action? NewTabRequested;
    public event Action? GroupCreateRequested;
    public event Action<TabGroup>? GroupCloseRequested;
    public event Action<TabGroup>? GroupRenameRequested;
    public event Action<TabGroup, int>? GroupColorRequested;
    public event Action<TabGroup>? GroupCollapseToggled;
    public event Action<TabGroup, int>? GroupMoveRequested;

    /// <summary>Вкладка и позиция курсора в координатах панели.</summary>
    public event Action<Tab, Point>? TabMoveRequested;

    /// <summary>Вкладка ушла из своей группы (контекстное меню).</summary>
    public event Action<Tab>? TabLeaveGroupRequested;

    /// <summary>Из выделенной вкладки создаётся новая группа.</summary>
    public event Action<Tab>? TabCreateGroupRequested;

    /// <summary>Новое состояние списка групп (присваивать каждый раз новую коллекцию).</summary>
    public IEnumerable<TabGroup>? Groups
    {
        set => BuildColumns(value);
    }

    public TabStripView() => InitializeComponent();

    private void NewTab_Click(object sender, RoutedEventArgs e) => NewTabRequested?.Invoke();

    private void NewGroup_Click(object sender, RoutedEventArgs e) => GroupCreateRequested?.Invoke();

    private void BuildColumns(IEnumerable<TabGroup>? groups)
    {
        ColumnsItems.ItemsSource = null;
        ColumnsItems.Items.Clear();
        _columns.Clear();

        if (groups is null) return;

        foreach (var group in groups)
        {
            var column = new GroupColumnView { Group = group };
            column.TabActivated += t => TabActivated?.Invoke(t);
            column.TabCloseRequested += t => TabCloseRequested?.Invoke(t);
            column.GroupCloseRequested += g => GroupCloseRequested?.Invoke(g);
            column.GroupRenameRequested += g => GroupRenameRequested?.Invoke(g);
            column.GroupCollapseToggled += g => GroupCollapseToggled?.Invoke(g);
            column.TabMoveRequested += (t, p) => TabMoveRequested?.Invoke(t, ToPanelPoint(column, p));
            column.DropHintChanged += (g, on) => SetDropHint(g, on);
            column.TabContextRequested += OnTabContextRequested;
            column.GroupContextRequested += OnGroupContextRequested;

            ColumnsItems.Items.Add(column);
            _columns.Add(column);
        }
    }

    /// <summary>Перерисовать колонки без смены состава — после переименования и цвета.</summary>
    public void RefreshColumns()
    {
        foreach (var column in _columns)
            column.Apply();
    }

    /// <summary>Открыть переименование группы: двойной клик, F2 или пункт меню.</summary>
    public void BeginRename(TabGroup group)
    {
        foreach (var column in _columns)
        {
            if (!ReferenceEquals(column.Group, group)) continue;
            column.BeginRename();
            return;
        }
    }

    public void SetActiveGroup(TabGroup? group)
    {
        foreach (var column in _columns)
            column.IsActiveGroup = ReferenceEquals(column.Group, group);
    }

    private void SetDropHint(TabGroup? group, bool on)
    {
        foreach (var column in _columns)
            column.IsDropTarget = on && ReferenceEquals(column.Group, group);
    }

    /// <summary>Точка в координатах колонки → точка в координатах панели.</summary>
    public Point ToPanelPoint(GroupColumnView column, Point pointInColumn) =>
        column.TranslatePoint(pointInColumn, this);

    /// <summary>
    /// Геометрия всех колонок для TabDropResolver: координаты берём в системе
    /// панели, иначе прямоугольники не совпадут с позицией курсора.
    /// </summary>
    public IReadOnlyList<DropColumn> BuildColumnsGeometry()
    {
        var result = new List<DropColumn>();
        foreach (var column in _columns)
            result.Add(column.BuildDropGeometry(this));
        return result;
    }

    private void Strip_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(typeof(TabGroup)) is TabGroup) return;
        if (e.Data.GetData(typeof(Tab)) is not Tab tab) return;

        var pointer = e.GetPosition(this);
        var target = TabDropResolver.Resolve(pointer, BuildColumnsGeometry());
        SetDropHint(target.Group, target.Kind != DropKind.None);
        e.Effects = target.Kind == DropKind.None ? DragDropEffects.None : DragDropEffects.Move;
        e.Handled = true;
    }

    private void Strip_DragLeave(object sender, DragEventArgs e) => SetDropHint(null, false);

    private void Strip_Drop(object sender, DragEventArgs e)
    {
        SetDropHint(null, false);

        if (e.Data.GetData(typeof(TabGroup)) is TabGroup group)
        {
            GroupMoveRequested?.Invoke(group, GroupInsertIndexAt(e.GetPosition(this)));
            e.Handled = true;
            return;
        }

        if (e.Data.GetData(typeof(Tab)) is not Tab tab) return;
        TabMoveRequested?.Invoke(tab, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Индекс колонки, в которую надо вставить перетаскиваемую группу.</summary>
    private int GroupInsertIndexAt(Point pointer)
    {
        for (var i = 0; i < _columns.Count; i++)
        {
            var bounds = _columns[i].TranslatePoint(
                new Point(0, 0), StripScroll).X;
            var width = _columns[i].ActualWidth;
            if (pointer.X < bounds + width / 2) return i;
        }
        return _columns.Count;
    }

    /// <summary>Меню палитры групп: восемь пунктов-цветов.</summary>
    private ContextMenu BuildColorMenu()
    {
        var menu = new ContextMenu();
        for (var i = 0; i < TabGroups.PaletteSize; i++)
        {
            var index = i;
            var item = new MenuItem
            {
                Header = "Цвет " + (index + 1),
                Background = (Brush)Application.Current.FindResource($"GroupColor{index}"),
            };
            item.Click += (_, _) => GroupColorRequested?.Invoke(_pendingColorGroup!, index);
            menu.Items.Add(item);
        }
        return menu;
    }

    private TabGroup? _pendingColorGroup;

    /// <summary>Меню «Переместить в группу»: список групп с галочкой на текущей.</summary>
    private ContextMenu BuildGroupsMenu()
    {
        var menu = new ContextMenu();
        menu.Opened += (_, _) =>
        {
            menu.Items.Clear();
            foreach (var column in _columns)
            {
                var group = column.Group!;
                var item = new MenuItem { Header = group.Name };
                item.IsEnabled = !(column.Group is { } g && g.Tabs.Contains(_pendingMoveTab));
                item.Click += (_, _) => TabMoveRequested?.Invoke(_pendingMoveTab!, ToPanelPoint(column, new Point(column.ActualWidth / 2, 4)));
                menu.Items.Add(item);
            }
        };
        return menu;
    }

    private Tab? _pendingMoveTab;

    private void OnTabContextRequested(Tab tab, ContextMenu menu)
    {
        _pendingMoveTab = tab;
        menu.Items.Clear();

        var move = new MenuItem { Header = "Переместить в группу" };
        move.Items.Add(BuildGroupsMenu());
        menu.Items.Add(move);

        var create = new MenuItem { Header = "Создать группу из вкладки" };
        create.Click += (_, _) => TabCreateGroupRequested?.Invoke(tab);
        menu.Items.Add(create);

        var leave = new MenuItem { Header = "Убрать из группы" };
        leave.Click += (_, _) => TabLeaveGroupRequested?.Invoke(tab);
        menu.Items.Add(leave);

        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "Закрыть вкладку" };
        close.Click += (_, _) => TabCloseRequested?.Invoke(tab);
        menu.Items.Add(close);
    }

    private void OnGroupContextRequested(TabGroup group, ContextMenu menu)
    {
        _pendingColorGroup = group;
        menu.Items.Clear();

        var rename = new MenuItem { Header = "Переименовать" };
        rename.Click += (_, _) => GroupRenameRequested?.Invoke(group);
        menu.Items.Add(rename);

        var color = new MenuItem { Header = "Сменить цвет" };
        color.Items.Add(BuildColorMenu());
        menu.Items.Add(color);

        var collapse = new MenuItem { Header = group.IsCollapsed ? "Развернуть" : "Свернуть" };
        collapse.Click += (_, _) => GroupCollapseToggled?.Invoke(group);
        menu.Items.Add(collapse);

        menu.Items.Add(new Separator());
        var close = new MenuItem { Header = "Закрыть группу" };
        close.Click += (_, _) => GroupCloseRequested?.Invoke(group);
        menu.Items.Add(close);
    }
}