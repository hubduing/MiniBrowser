using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MiniBrowser.Models;
using MiniBrowser.Services;

namespace MiniBrowser.Views;

/// <summary>
/// Колонка одной группы в вертикальной полосе вкладок: цветная метка, заголовок
/// со сворачиванием и переименованием, список вкладок. Все решения принимает
/// <see cref="TabManager"/>, здесь только геометрия и события наверх.
/// </summary>
public partial class GroupColumnView : UserControl
{
    /// <summary>Кисти палитры групп в порядке индекса ColorIndex.</summary>
    private static readonly Brush[] Palette =
    {
        (Brush)Application.Current.FindResource("GroupColor0"),
        (Brush)Application.Current.FindResource("GroupColor1"),
        (Brush)Application.Current.FindResource("GroupColor2"),
        (Brush)Application.Current.FindResource("GroupColor3"),
        (Brush)Application.Current.FindResource("GroupColor4"),
        (Brush)Application.Current.FindResource("GroupColor5"),
        (Brush)Application.Current.FindResource("GroupColor6"),
        (Brush)Application.Current.FindResource("GroupColor7"),
    };

    private TabGroup? _group;

    // Отложенный одиночный клик по имени: ждём, не станет ли он половиной
    // двойного клика — тогда вместо сворачивания откроется переименование.
    private DispatcherTimer? _pendingCollapse;

    // Откуда начали тащить вкладку: без этого обычный клик не отличить от начала
    // перетаскивания, а начинать drag на каждый чих нельзя.
    private Point _dragOrigin;
    private Tab? _dragTab;

    public static readonly DependencyProperty IsDropTargetProperty =
        DependencyProperty.Register(nameof(IsDropTarget), typeof(bool), typeof(GroupColumnView));

    /// <summary>true — колонка подсвечена как цель перетаскивания.</summary>
    public bool IsDropTarget
    {
        get => (bool)GetValue(IsDropTargetProperty);
        set => SetValue(IsDropTargetProperty, value);
    }

    public static readonly DependencyProperty IsActiveGroupProperty =
        DependencyProperty.Register(nameof(IsActiveGroup), typeof(bool), typeof(GroupColumnView));

    /// <summary>true — группа активна: новая вкладка попадёт именно в неё.</summary>
    public bool IsActiveGroup
    {
        get => (bool)GetValue(IsActiveGroupProperty);
        set => SetValue(IsActiveGroupProperty, value);
    }

    public event Action<Tab>? TabActivated;
    public event Action<Tab>? TabCloseRequested;
    public event Action<TabGroup>? GroupCloseRequested;
    public event Action<TabGroup>? GroupRenameRequested;
    public event Action<TabGroup>? GroupCollapseToggled;

    /// <summary>Имя группы поменялось — окно должно пометить сессию изменённой.</summary>
    public event Action<TabGroup>? GroupRenamed;
    public event Action<Tab, Point>? TabMoveRequested;
    public event Action<TabGroup, bool>? DropHintChanged;

    /// <summary>Правый клик по вкладке: полоса наполняет меню, колонка показывает его.</summary>
    public event Action<Tab, ContextMenu>? TabContextRequested;

    /// <summary>Правый клик по заголовку группы.</summary>
    public event Action<TabGroup, ContextMenu>? GroupContextRequested;

    public GroupColumnView() => InitializeComponent();

    public TabGroup? Group
    {
        get => _group;
        set
        {
            if (_group is not null) _group.PropertyChanged -= OnGroupChanged;
            _group = value;
            if (_group is not null) _group.PropertyChanged += OnGroupChanged;
            TabsItems.ItemsSource = value?.Tabs;
            Apply();
        }
    }

    /// <summary>
    /// Счётчик на заголовке обязан следовать за составом группы: вкладки
    /// добавляются и закрываются мимо колонки, события прилетают из модели.
    /// Сворачивание, имя и цвет меняются там же — колонка перерисовывается сама.
    /// </summary>
    private void OnGroupChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TabGroup.Count) or nameof(TabGroup.IsCollapsed)
            or nameof(TabGroup.Name) or nameof(TabGroup.ColorIndex) or null)
            Apply();
    }

    /// <summary>Показать колонку: цвет, имя, счётчик, свёрнутость.</summary>
    public void Apply()
    {
        if (_group is null)
        {
            ExpandedPanel.Visibility = Visibility.Collapsed;
            CollapsedPanel.Visibility = Visibility.Collapsed;
            return;
        }

        ColorMark.Background = Palette[Math.Clamp(_group.ColorIndex, 0, Palette.Length - 1)];
        NameText.Text = _group.Name;
        VerticalName.Text = _group.Name;
        Badge.Text = _group.Count.ToString();
        CollapsedBadge.Text = _group.Count.ToString();
        // Имя под курсором свёрнутой колонки — подсказка для длинных названий.
        CollapsedPanel.ToolTip = _group.Name;

        var collapsed = _group.IsCollapsed;
        ExpandedPanel.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        // Свёрнутая колонка показывает имя вертикально — панель видима,
        // а не Hidden: иначе узкая полоса остаётся пустой.
        CollapsedPanel.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        // Свёрнутая группа — узкая цветная полоса, вкладки в ней не видны.
        Width = collapsed ? 32 : 200;
        CollapseButton.Content = collapsed ? "▸" : "▾";
    }

    /// <summary>
    /// Геометрия колонки в координатах указанного предка — для TabDropResolver.
    /// Прямоугольники обязаны быть в одной системе с координатой курсора.
    /// </summary>
    public DropColumn BuildDropGeometry(FrameworkElement ancestor)
    {
        var tabs = new List<DropTabRow>();
        if (_group is not null)
        {
            foreach (var tab in _group.Tabs)
            {
                // Строка вкладки находится через генератор контейнеров: без
                // визуального дерева её прямоугольник недоступен.
                if (FindRowRect(tab) is not { } rect) continue;
                tabs.Add(new DropTabRow(rect, tab));
            }
        }

        var origin = TranslatePoint(new Point(0, 0), ancestor);
        // Только фактические размеры: Height у колонки Auto (NaN), и Math.Max
        // с ним дал бы NaN-прямоугольник, который не содержит ни одной точки.
        var bounds = new Rect(origin.X, origin.Y, ActualWidth, ActualHeight);
        return new DropColumn(_group!, bounds, _group!.IsCollapsed, tabs);
    }

    private Rect? FindRowRect(Tab tab)
    {
        // Контейнер строки достаётся из генератора: он и есть визуальный прямоугольник,
        // а в шаблоне строки несколько вложенных Border'ов.
        if (TabsItems.ItemContainerGenerator.ContainerFromItem(tab) is not FrameworkElement container)
            return null;
        var origin = container.TranslatePoint(new Point(0, 0), this);
        return new Rect(origin.X, origin.Y, container.ActualWidth, container.ActualHeight);
    }

    private void Collapse_Click(object sender, RoutedEventArgs e)
    {
        if (_group is not null) GroupCollapseToggled?.Invoke(_group);
    }

    private void Name_Click(object sender, MouseButtonEventArgs e)
    {
        if (_group is null) return;

        // Двойной клик — переименование: отложенный сворачиваемый одиночный
        // клик отменяется, иначе колонка свернётся под открытым полем имени.
        if (e.ClickCount >= 2)
        {
            CancelPendingCollapse();
            BeginRename();
            return;
        }

        // Одиночный клик по имени сворачивает группу. Действие откладывается
        // на 300 мс: вторая половина двойного клика прилетает быстрее.
        CancelPendingCollapse();
        _pendingCollapse = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        var group = _group;
        _pendingCollapse.Tick += (_, _) =>
        {
            CancelPendingCollapse();
            GroupCollapseToggled?.Invoke(group);
        };
        _pendingCollapse.Start();
    }

    private void CancelPendingCollapse()
    {
        if (_pendingCollapse is null) return;
        _pendingCollapse.Stop();
        _pendingCollapse = null;
    }

    private void Collapsed_Click(object sender, MouseButtonEventArgs e)
    {
        // Клик по свёрнутой колонке разворачивает её обратно.
        if (_group is not null) GroupCollapseToggled?.Invoke(_group);
    }

    /// <summary>Открыть поле ввода имени прямо в заголовке.</summary>
    public void BeginRename()
    {
        if (_group is null) return;
        CancelPendingCollapse();
        RenameBox.Text = _group.Name;
        RenameBox.Visibility = Visibility.Visible;
        NameText.Visibility = Visibility.Collapsed;
        RenameBox.Focus();
        RenameBox.SelectAll();
    }

    /// <summary>Имя поменялось — перерисовать колонку (вызовется из RefreshColumns).</summary>
    public void CommitRename()
    {
        var value = RenameBox.Text.Trim();
        RenameBox.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
        if (_group is null || value.Length == 0) return;
        var changed = _group.Name != value;
        _group.Name = value;
        Apply();
        // Новое имя должно пережить перезапуск — сообщаем наверх, что сессия
        // изменилась (дебаунс-запись запускается окном).
        if (changed) GroupRenamed?.Invoke(_group);
    }

    public void CancelRename()
    {
        RenameBox.Visibility = Visibility.Collapsed;
        NameText.Visibility = Visibility.Visible;
    }

    private void RenameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitRename(); e.Handled = true; }
        if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
    }

    private void RenameBox_LostFocus(object sender, RoutedEventArgs e) => CommitRename();

    private void Header_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Новое нажатие отменяет отложенное сворачивание: затяжное удержание
        // мыши — это начало перетаскивания или второй клик двойного, а не клик.
        CancelPendingCollapse();
        _dragGroupOrigin = e.GetPosition(this);
        _dragGroup = _group;
    }

    private Point _dragGroupOrigin;
    private TabGroup? _dragGroup;

    private void Header_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragGroup is null) return;

        var delta = e.GetPosition(this) - _dragGroupOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var group = _dragGroup;
        _dragGroup = null;
        DragDrop.DoDragDrop(this, group, DragDropEffects.Move);
    }

    private void CloseGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_group is not null) GroupCloseRequested?.Invoke(_group);
    }

    private void Tab_Click(object sender, MouseButtonEventArgs e)
    {
        if (IsOverButton(e.OriginalSource as DependencyObject)) return;
        if (sender is FrameworkElement { DataContext: Tab tab })
            TabActivated?.Invoke(tab);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Tab tab })
            TabCloseRequested?.Invoke(tab);
    }

    private void Tab_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "mb-drag.log"),
            $"down btn={e.ChangedButton} src={e.OriginalSource?.GetType().Name}\n");
        if (e.ChangedButton != MouseButton.Left) return;
        // Вкладка лежит в шаблоне строки, а событие пришло на ItemsControl:
        // поднимаемся по визуальному дереву до элемента с вкладкой в DataContext.
        _dragTab = TabAt(e.OriginalSource as DependencyObject);
        _dragOrigin = e.GetPosition(this);
    }

    private void Tab_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TabAt(e.OriginalSource as DependencyObject) is not { } tab) return;
        var menu = new ContextMenu();
        TabContextRequested?.Invoke(tab, menu);
        if (menu.Items.Count == 0) return;
        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>Ближайший предок с вкладкой в контексте данных.</summary>
    private static Tab? TabAt(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is FrameworkElement { DataContext: Tab tab }) return tab;
            node = VisualTreeHelper.GetParent(node);
        }
        return null;
    }

    private void Header_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_group is null) return;
        var menu = new ContextMenu();
        GroupContextRequested?.Invoke(_group, menu);
        if (menu.Items.Count == 0) return;
        menu.PlacementTarget = this;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void Tab_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "mb-drag.log"),
            $"move left={e.LeftButton} tab={_dragTab?.Id}\n");
        if (e.LeftButton != MouseButtonState.Pressed || _dragTab is null) return;

        var delta = e.GetPosition(this) - _dragOrigin;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        var tab = _dragTab;
        _dragTab = null;
        DragDrop.DoDragDrop(this, tab, DragDropEffects.Move);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        // Внутри колонки цель одна — сама группа: вставку по позиции решает
        // полоса, у которой геометрия всех колонок сразу.
        var isTab = e.Data.GetData(typeof(Tab)) is Tab;
        DropHintChanged?.Invoke(_group, isTab);
        e.Effects = isTab ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e) => DropHintChanged?.Invoke(_group, false);

    private void OnDrop(object sender, DragEventArgs e)
    {
        IsDropTarget = false;
        DropHintChanged?.Invoke(_group, false);
        if (e.Data.GetData(typeof(Tab)) is not Tab tab) return;

        TabMoveRequested?.Invoke(tab, e.GetPosition(this));
        e.Handled = true;
    }

    /// <summary>Клик по кнопке внутри строки вкладки не должен переключать вкладку.</summary>
    internal static bool IsOverButton(DependencyObject? node)
    {
        while (node is not null)
        {
            if (node is Button) return true;
            node = VisualTreeHelper.GetParent(node);
        }
        return false;
    }
}