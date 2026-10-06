using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MiniBrowser.Services;
using MiniBrowser.ViewModels;

namespace MiniBrowser.Views;

/// <summary>
/// Выезжающая слева панель: закладки, история, настройки. Разметка знает только события
/// и метод SetOpen; за навигацию и хранение отвечает хост через VM и подписки.
/// </summary>
public partial class MenuDrawerView : UserControl
{
    // Закрытая панель стоит ровно за левым краем: стартовый X равен её ширине.
    private const double ClosedX = -360;
    private static readonly Duration SlideDuration = new(TimeSpan.FromMilliseconds(180));

    // Синхронизация ComboBox поднимает их же SelectionChanged: флаг гасит эхо.
    private bool _syncingCombos;
    private MenuDrawerViewModel? _trackedVm;

    /// <summary>Затемнение, ✕ и Esc: хост скрывает панель.</summary>
    public event Action? CloseRequested;

    /// <summary>
    /// Двойной клик по строке. Дублирует NavigateRequested VM: хост (Task 8) подписывается
    /// на событие вида, не трогая VM, и заодно закрывает панель. OpenUrlCommand VM в разметке
    /// не используется сознательно: клику нужен mouse-event с выделением строки, а тянуть пакет
    /// behaviors ради InvokeCommandAction — лишняя зависимость; команда остаётся программным
    /// путём VM и покрыта тестами.
    /// </summary>
    public event Action<string>? OpenUrlRequested;

    public bool IsDrawerOpen { get; private set; }

    public MenuDrawerView()
    {
        InitializeComponent();
        Loaded += (_, _) => SyncCombos();
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>Единственный метод открытия: ставит флаг и гонит слайд панели и opacity затемнения.</summary>
    public void SetOpen(bool open)
    {
        IsDrawerOpen = open;
        // Открытие до первой отрисовки: без принудительной раскладки анимации не с чего стартовать.
        if (open && !Panel.IsLoaded) UpdateLayout();
        if (open)
        {
            Visibility = Visibility.Visible;
            IsHitTestVisible = true;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var slide = new DoubleAnimation(open ? 0 : ClosedX, SlideDuration) { EasingFunction = ease };
        if (!open)
            // Скрываем из hit-test только по окончании: Collapsed сразу убил бы саму анимацию закрытия.
            // Флаг перепроверяем — панель могли успеть открыть заново за 180 мс.
            slide.Completed += (_, _) =>
            {
                if (!IsDrawerOpen)
                {
                    Visibility = Visibility.Collapsed;
                    IsHitTestVisible = false;
                }
            };
        Slide.BeginAnimation(TranslateTransform.XProperty, slide);
        var dim = new DoubleAnimation(open ? 1 : 0, SlideDuration) { EasingFunction = ease };
        Dim.BeginAnimation(OpacityProperty, dim);
        if (open) SyncCombos();
    }

    /// <summary>0 — закладки, 1 — история, 2 — настройки. Хост зовёт из ShowBookmarks/ShowHistory.</summary>
    public void SelectTab(int index) => Tabs.SelectedIndex = Math.Clamp(index, 0, 2);

    private void Panel_Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke();

    private void Dim_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => CloseRequested?.Invoke();

    private void Drawer_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke();
            e.Handled = true;
        }
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Двойной клик, а не одиночный: одиночный нужен для выделения строки и кнопки удаления.
        if (sender is ListBox list && list.SelectedItem is MenuItemBase item)
            OpenUrlRequested?.Invoke(item.Url);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // Сброс настроек меняет Current молча (AppSettings без уведомлений):
        // пересинхронизируем ComboBox вслед за событием VM, не забывая отписаться от старой.
        if (_trackedVm is not null) _trackedVm.SettingsChanged -= OnVmSettingsChanged;
        _trackedVm = e.NewValue as MenuDrawerViewModel;
        if (_trackedVm is not null) _trackedVm.SettingsChanged += OnVmSettingsChanged;
        SyncCombos();
    }

    private void OnVmSettingsChanged()
    {
        SyncCombos();
        SyncSettingsControls();
    }

    /// <summary>
    /// AppSettings — POCO без уведомлений: сброс меняет Current молча, и TwoWay-привязки
    /// не узнают. Освежаем простые контролы вручную; флаг гасит эхо точно как у комбо.
    /// </summary>
    private void SyncSettingsControls()
    {
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        _syncingCombos = true;
        try
        {
            ZoomSlider.Value = vm.Settings.Current.ZoomPercent;
            StatusBarBox.IsChecked = vm.Settings.Current.ShowStatusBar;
            RememberSizeBox.IsChecked = vm.Settings.Current.WindowMaximized;
            HomeUrlBox.Text = vm.Settings.Current.HomeUrl;
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    /// <summary>
    /// Ползунок пишет прямо в Current (привязка тоже пишет, значение то же):
    /// без уведомления хост не узнал бы о новом масштабе и не применил его.
    /// </summary>
    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncingCombos) return;
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        vm.Settings.Current.ZoomPercent = e.NewValue;
        vm.NotifySettingsChanged();
    }

    /// <summary>
    /// Click, а не Checked: программное состояние после сброса не должно уведомлять.
    /// Значение пишем явно — порядок привязки и события нам не важен.
    /// </summary>
    private void StatusBarBox_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        if (sender is CheckBox box)
        {
            vm.Settings.Current.ShowStatusBar = box.IsChecked == true;
            vm.NotifySettingsChanged();
        }
    }

    /// <summary>
    /// Тот же приём, что у StatusBarBox: чистая TwoWay-привязка молча меняла бы
    /// значение без сохранения — хост узнаёт о новом флаге только через событие.
    /// </summary>
    private void RememberSizeBox_Click(object sender, RoutedEventArgs e)
    {
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        if (sender is CheckBox box)
        {
            vm.Settings.Current.WindowMaximized = box.IsChecked == true;
            vm.NotifySettingsChanged();
        }
    }

    /// <summary>
    /// Начальный выбор обоих ComboBox выставляем кодом: привязка SelectedValue к double
    /// (шрифт) и перевод имени движка в URL-шаблон без конвертера невозможны.
    /// </summary>
    private void SyncCombos()
    {
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        _syncingCombos = true;
        try
        {
            FontSizeBox.SelectedItem = null;
            foreach (var raw in FontSizeBox.Items)
                // Сравнение с допуском: значение прошло JSON и ползунки, младшие биты double
                // могли поплыть, — точное равенство опознало бы «16» не всегда.
                if (raw is ComboBoxItem { Tag: string tag }
                    && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
                    && Math.Abs(size - vm.Settings.Current.DefaultFontSize) < 0.001)
                {
                    FontSizeBox.SelectedItem = raw;
                    break;
                }
            // Имя движка восстанавливаем обратным проходом по известным шаблонам;
            // ручная правка конфига даст чужой URL — тогда выбора нет, и это честно.
            EngineBox.SelectedItem = null;
            foreach (var name in vm.SearchEngines)
                if (NavigationService.SearchEngine(name) == vm.Settings.Current.SearchUrl)
                {
                    EngineBox.SelectedItem = name;
                    break;
                }
        }
        finally
        {
            _syncingCombos = false;
        }
    }

    private void FontSizeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos) return;
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        if (FontSizeBox.SelectedItem is ComboBoxItem { Tag: string tag }
            && double.TryParse(tag, NumberStyles.Float, CultureInfo.InvariantCulture, out var size))
        {
            vm.Settings.Current.DefaultFontSize = size;
            vm.NotifySettingsChanged();
        }
    }

    private void EngineBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCombos) return;
        var vm = DataContext as MenuDrawerViewModel;
        if (vm is null) return;
        if (EngineBox.SelectedItem is string name)
        {
            vm.Settings.Current.SearchUrl = NavigationService.SearchEngine(name);
            vm.NotifySettingsChanged();
        }
    }
}
