using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniBrowser.Services;

namespace MiniBrowser.Views;

/// <summary>Панель инструментов: навигация, адресная строка и кнопка меню.</summary>
public partial class ToolbarView : UserControl
{
    public event Action<string>? NavigateRequested;
    public event Action? BackRequested;
    public event Action? ForwardRequested;
    /// <summary>
    /// Кнопка «⟳/✕» без разбора: тулбар не знает, что сейчас грузится, —
    /// решение принимает хост, у которого есть состояние движка.
    /// </summary>
    public event Action? ReloadStopRequested;
    public event Action? BookmarkAddRequested;
    public event Action? CopyAddressRequested;

    /// <summary>Щит блокировки: хоста просим переключить блокировку на текущем сайте.</summary>
    public event Action? ToggleAdBlockRequested;

    /// <summary>Кнопка ☰ больше не держит ContextMenu: она просит хост открыть панель.</summary>
    public event Action? ToggleDrawerRequested;
    /// <summary>Кнопка переключения боковой панели вкладок.</summary>
    public event Action? ToggleTabStripRequested;

    /// <summary>
    /// Шаблон поиска приходит снаружи из настроек: тулбар не знает про SettingsService,
    /// он только подставляет готовую строку в запрос.
    /// </summary>
    public string SearchTemplate { get; set; } = NavigationService.SearchUrlTemplate;

    /// <summary>true — идёт ввод: поле очищено, обновления URL не должны его трогать.</summary>
    private bool _editing;

    /// <summary>Последний известный URL страницы — из него поле восстанавливается по Escape.</summary>
    private string _currentUrl = string.Empty;

    /// <summary>Следующий GotKeyboardFocus пришёл из FocusAddress, а не от клика мышью.</summary>
    private bool _suppressClear;

    public ToolbarView() => InitializeComponent();

    /// <summary>
    /// Подсказка «Поиск или адрес» живёт в шаблоне TextBox, поэтому каждый раз
    /// достаём её по имени: у TextBox нет дочерних элементов в визуальном дереве.
    /// </summary>
    private void UpdateHint()
    {
        var hint = AddressBox.Template.FindName("Hint", AddressBox) as FrameworkElement;
        if (hint is null) return;
        hint.Visibility = string.IsNullOrEmpty(AddressBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void Address_TextChanged(object sender, TextChangedEventArgs e) => UpdateHint();

    public void SetUrl(string url)
    {
        _currentUrl = url;
        CopyButton.IsEnabled = AddressCopy.CanCopy(url);
        if (_editing) return;
        AddressBox.Text = url;
    }

    public void SetNavState(bool canGoBack, bool canGoForward, bool isLoading)
    {
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
        ReloadStopButton.Content = ReloadStopPolicy.Glyph(isLoading);
        ReloadStopButton.ToolTip = ReloadStopPolicy.ToolTip(isLoading);
    }

    /// <summary>
    /// Программный фокус на адресную строку (Ctrl+L, новая вкладка).
    /// В отличие от клика мышью адрес не очищается, а выделяется: иначе при
    /// старте приложения поле осталось бы пустым, пока страница ещё грузится,
    /// и пользователь не видел бы, куда он попал.
    /// </summary>
    /// <summary>
    /// Состояние щита. Считает заблокированные запросы для подсказки: без него
    /// пользователь не может отличить «работает молча» от «сломано».
    /// </summary>
    public void SetAdBlockState(bool enabled, int blockedCount)
    {
        // Глобальный выключатель и пустая страница не различаем: и там, и там
        // блокировать нечего, и кнопка обязана это показывать одинаково.
        AdBlockButton.Style = (Style)FindResource(enabled ? "ActiveToolButton" : "FlatToolButton");
        AdBlockButton.ToolTip = enabled
            ? $"Реклама блокируется — заблокировано запросов: {blockedCount} (Ctrl+Shift+A)"
            : "Реклама на этом сайте не блокируется (Ctrl+Shift+A)";
    }

    public void FocusAddress()
    {
        _suppressClear = true;
        AddressBox.Focus();
        AddressBox.SelectAll();
        _suppressClear = false;
    }

    /// <summary>Клик мышью по адресной строке: очищаем поле целиком.</summary>
    private void BeginEditing()
    {
        if (_editing) return;
        _editing = true;
        AddressBox.Text = string.Empty;
        AddressBox.CaretIndex = 0;
    }

    /// <summary>Выход из режима ввода: поле снова показывает адрес текущей страницы.</summary>
    private void EndEditing()
    {
        _editing = false;
        AddressBox.Text = _currentUrl;
    }

    private void Address_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_suppressClear) return;
        BeginEditing();
    }

    /// <summary>
    /// Клик мышью очищает поле всегда. Одного GotKeyboardFocus мало: если поле
    /// уже в фокусе (например, после старта или Ctrl+L), фокус не меняется и
    /// событие не приходит — адрес остался бы на месте. Здесь же гасим
    /// стандартное поведение TextBox, который поставил бы курсор по клику.
    /// </summary>
    private void Address_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editing) return;

        _editing = true;
        AddressBox.Text = string.Empty;
        AddressBox.CaretIndex = 0;
        AddressBox.Focus();
        e.Handled = true;
    }

    /// <summary>
    /// Уход фокуса без Enter — это отмена ввода (клик мышью по странице, Tab).
    /// Пустое поле показывать бессмысленно, поэтому восстанавливаем адрес.
    /// </summary>
    private void Address_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_editing) EndEditing();
    }

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var url = NavigationService.BuildUrl(AddressBox.Text, SearchTemplate);
            // Пустая строка — не запрос: просто возвращаем адрес, ничего не открывая.
            if (url is null)
            {
                EndEditing();
                Keyboard.ClearFocus();
            }
            else
            {
                _editing = false;
                AddressBox.Text = url;
                NavigateRequested?.Invoke(url);
                // Фокус возвращается странице, иначе горячие клавиши остались бы в поле.
                Keyboard.ClearFocus();
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            EndEditing();
            Keyboard.ClearFocus();
            e.Handled = true;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => BackRequested?.Invoke();
    private void Forward_Click(object sender, RoutedEventArgs e) => ForwardRequested?.Invoke();
    private void ReloadStop_Click(object sender, RoutedEventArgs e) => ReloadStopRequested?.Invoke();
    private void Bookmark_Click(object sender, RoutedEventArgs e) => BookmarkAddRequested?.Invoke();
    private void Copy_Click(object sender, RoutedEventArgs e) => CopyAddressRequested?.Invoke();
    private void AdBlock_Click(object sender, RoutedEventArgs e) => ToggleAdBlockRequested?.Invoke();
    private void TabStripToggle_Click(object sender, RoutedEventArgs e) => ToggleTabStripRequested?.Invoke();

    /// <summary>Кнопка меню делегирует открытие панели хосту через событие.</summary>
    private void Menu_Click(object sender, RoutedEventArgs e) => ToggleDrawerRequested?.Invoke();
}
