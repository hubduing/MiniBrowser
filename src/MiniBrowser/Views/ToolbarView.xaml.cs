using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MiniBrowser.Services;

namespace MiniBrowser.Views;

/// <summary>Панель инструментов: навигация, адресная строка, закладки и история.</summary>
public partial class ToolbarView : UserControl
{
    public event Action<string>? NavigateRequested;
    public event Action<string>? OpenRequested;
    public event Action? BackRequested;
    public event Action? ForwardRequested;
    public event Action? RefreshRequested;
    public event Action? StopRequested;
    public event Action? BookmarkAddRequested;

    /// <summary>Хранилище для наполнения меню закладками/историей.</summary>
    public StorageService? Storage { get; set; }

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
        if (_editing) return;
        AddressBox.Text = url;
    }

    public void SetNavState(bool canGoBack, bool canGoForward)
    {
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
    }

    /// <summary>
    /// Программный фокус на адресную строку (Ctrl+L, новая вкладка).
    /// В отличие от клика мышью адрес не очищается, а выделяется: иначе при
    /// старте приложения поле осталось бы пустым, пока страница ещё грузится,
    /// и пользователь не видел бы, куда он попал.
    /// </summary>
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
            var url = NavigationService.BuildUrl(AddressBox.Text);
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
    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshRequested?.Invoke();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopRequested?.Invoke();
    private void Bookmark_Click(object sender, RoutedEventArgs e) => BookmarkAddRequested?.Invoke();

    private void Menu_Click(object sender, RoutedEventArgs e)
    {
        Menu.PlacementTarget = MenuButton;
        Menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        Menu.IsOpen = true;
    }

    private void Menu_Opened(object sender, RoutedEventArgs e)
    {
        Menu.Items.Clear();
        if (Storage is null) return;

        AddSection("Закладки", Storage.GetBookmarks()
            .Select(b => (b.Title, b.Url)));
        Menu.Items.Add(new Separator());
        AddSection("История", Storage.GetRecentHistory()
            .Select(h => (string.IsNullOrEmpty(h.Title) ? h.Url : h.Title, h.Url)));

        void AddSection(string header, IEnumerable<(string Title, string Url)> items)
        {
            var title = new MenuItem { Header = header, IsEnabled = false, FontWeight = FontWeights.SemiBold };
            Menu.Items.Add(title);

            var any = false;
            foreach (var (itemTitle, url) in items)
            {
                any = true;
                var item = new MenuItem
                {
                    Header = itemTitle,
                    Tag = url,
                    ToolTip = url,
                };
                item.Click += (_, _) => OpenRequested?.Invoke(url);
                Menu.Items.Add(item);
            }

            if (!any)
                Menu.Items.Add(new MenuItem { Header = "  пусто", IsEnabled = false });
        }
    }
}
