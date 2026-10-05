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

    /// <summary>true — пользователь сейчас редактирует адрес, не перезаписывать.</summary>
    private bool _userEditing;

    public ToolbarView() => InitializeComponent();

    public void SetUrl(string url)
    {
        if (AddressBox.IsKeyboardFocused && _userEditing) return;
        AddressBox.Text = url;
    }

    public void SetNavState(bool canGoBack, bool canGoForward)
    {
        BackButton.IsEnabled = canGoBack;
        ForwardButton.IsEnabled = canGoForward;
    }

    public void FocusAddress()
    {
        AddressBox.Focus();
        AddressBox.SelectAll();
    }

    private void Address_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        _userEditing = false;
        AddressBox.SelectAll();
    }

    private void Address_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        _userEditing = false;

    private void Address_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            var url = NavigationService.BuildUrl(AddressBox.Text);
            if (url is not null)
                NavigateRequested?.Invoke(url);
            _userEditing = false;
            e.Handled = true;
            Keyboard.ClearFocus();
            return;
        }

        if (e.Key is Key.Tab or Key.Left or Key.Right or Key.Home or Key.End
            or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl)
            return;

        if (e.Key is Key.Up or Key.Down or Key.Escape) return;

        _userEditing = true;
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
