using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MiniBrowser.Models;
using MiniBrowser.Services;

namespace MiniBrowser.Views;

/// <summary>
/// Полоса групп вкладок слева: строки групп на всю ширину, перетаскивание
/// вкладок и групп, контекстные меню. Решения принимает не она, а
/// <see cref="TabManager"/> и <see cref="TabDropResolver"/>: здесь только
/// геометрия и события наверх.
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

    /// <summary>Имя группы изменено прямо в колонке — окно помечает сессию.</summary>
    public event Action<TabGroup>? GroupRenamed;

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
            column.GroupRenamed += g => GroupRenamed?.Invoke(g);
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

    /// <summary>Индекс группы, в которую надо вставить перетаскиваемую группу.</summary>
    private int GroupInsertIndexAt(Point pointer)
    {
        // Группы — строки, одна под другой: вставка перед первой строкой,
        // верхняя половина которой ещё ниже курсора.
        for (var i = 0; i < _columns.Count; i++)
        {
            var top = _columns[i].TranslatePoint(new Point(0, 0), this).Y;
            if (pointer.Y < top + _columns[i].ActualHeight / 2) return i;
        }
        return _columns.Count;
    }

    /// <summary>
    /// Меню вкладки. Наполняется кодом, потому что список групп динамический.
    /// Каждая группа — отдельный пункт, а не вложенное меню.
    /// </summary>
    private void OnTabContextRequested(Tab tab, ContextMenu menu)
    {
        var groups = new List<(string Name, bool IsCurrent, int Target)>();
        var columns = _columns;
        for (var i = 0; i < columns.Count; i++)
        {
            var group = columns[i].Group;
            if (group is null) continue;
            groups.Add((group.Name, group.Tabs.Contains(tab), i));
        }

        TabContextMenuFactory.FillTabMenu(
            menu,
            groups,
            target => MoveTabToColumn(tab, target),
            () => TabCreateGroupRequested?.Invoke(tab),
            () => TabLeaveGroupRequested?.Invoke(tab),
            () => TabCloseRequested?.Invoke(tab));
    }

    /// <summary>Перенести вкладку в группу колонки с указанным индексом.</summary>
    private void MoveTabToColumn(Tab tab, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= _columns.Count) return;
        if (_columns[columnIndex].Group is null) return;
        TabMoveRequested?.Invoke(tab, ToPanelPoint(_columns[columnIndex], new Point(_columns[columnIndex].ActualWidth / 2, 4)));
    }

    private void OnGroupContextRequested(TabGroup group, ContextMenu menu)
    {
        TabContextMenuFactory.FillGroupMenu(
            menu,
            group.IsCollapsed,
            TabGroups.PaletteSize,
            index => new Border
            {
                Background = (Brush)Application.Current.FindResource($"GroupColor{index}"),
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(3),
            },
            () => GroupRenameRequested?.Invoke(group),
            index => GroupColorRequested?.Invoke(group, index),
            () => GroupCollapseToggled?.Invoke(group),
            () => GroupCloseRequested?.Invoke(group));
    }
}