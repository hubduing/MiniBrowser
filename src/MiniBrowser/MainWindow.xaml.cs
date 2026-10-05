using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using MiniBrowser.Models;
using MiniBrowser.Services;
using MiniBrowser.Views;

namespace MiniBrowser;

public partial class MainWindow : Window, IBrowserActions
{
    private readonly StorageService _storage = new();
    private readonly SettingsService _settings = new();
    private readonly TabManager _tabManager;
    private readonly string[] _startupUrls;
    private Tab? _titleTab;
    private bool _started;
    // Полноэкранный режим имеет два независимых источника:
    //   _manualFullscreen — пользователь нажал F11;
    //   _pageFullscreen  — страница (видео) ушла в HTML5-fullscreen.
    // Окно без рамки нужно, если активен ЛИБО один, ЛИБО оба, иначе выход из
    // видео выкинет пользователя из F11-режима и наоборот.
    private bool _manualFullscreen;
    private bool _pageFullscreen;
    private WindowStyle _savedWindowStyle = WindowStyle.SingleBorderWindow;
    private ResizeMode _savedResizeMode = ResizeMode.CanResize;
    private WindowState _savedWindowState = WindowState.Normal;

    /// <summary>Текущее состояние окна — вычисляется в ApplyFullscreen, копить нельзя.</summary>
    private bool _isFullscreenApplied;

    public MainWindow(string[]? startupUrls = null)
    {
        InitializeComponent();
        _startupUrls = startupUrls ?? Array.Empty<string>();

        _tabManager = new TabManager(ContentHost, _storage, this, () => _settings.EffectiveZoom);
        _tabManager.TabsChanged += RefreshTabStrip;
        _tabManager.ActiveTabChanged += OnActiveTabChanged;
        _tabManager.StateChanged += _ => RefreshNavState();
        _tabManager.Navigated += OnNavigated;
        _tabManager.ActiveViewFullscreenChanged += OnPageFullscreenChanged;

        TabStrip.Items = _tabManager.Tabs.ToList();
        TabStrip.TabActivated += t => _tabManager.ActivateTab(t);
        TabStrip.TabCloseRequested += CloseTab;
        TabStrip.NewTabRequested += () => NewTab();

        Toolbar.Storage = _storage;
        Toolbar.SearchTemplate = _settings.Current.SearchUrl;
        Toolbar.NavigateRequested += url => _tabManager.NavigateActive(url);
        Toolbar.OpenRequested += url => _tabManager.NavigateActive(url);
        Toolbar.BackRequested += _tabManager.GoBackActive;
        Toolbar.ForwardRequested += _tabManager.GoForwardActive;
        Toolbar.RefreshRequested += _tabManager.ReloadActive;
        Toolbar.StopRequested += _tabManager.StopActive;
        Toolbar.BookmarkAddRequested += AddBookmark;

        // Горячие клавиши перехватываем хуком: сообщения клавиатуры приходят и на
        // дочерний HWND WebView2, WPF-события окна их не видят.
        // Хук срабатывает только когда активно окно НАШЕГО процесса.
        _hookProc = KeyboardProc;
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _hookProc, GetModuleHandle(null), 0);
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private const int WhKeyboardLl = 13;
    private const string HomeUrl = "https://www.google.com/";
    private static readonly IntPtr HookFailed = IntPtr.Zero;
    private LowLevelKeyboardProc? _hookProc;
    private IntPtr _keyboardHook = IntPtr.Zero;

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardHookData
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        const int WmKeyDown = 0x0100;
        const int WmSysKeyDown = 0x0104;

        if (nCode >= 0)
        {
            var message = wParam.ToInt32();
            if (message == WmKeyDown || message == WmSysKeyDown)
            {
                // Реагируем только когда наше окно в фокусе — чужие приложения не трогаем
                uint foregroundPid = 0;
                GetWindowThreadProcessId(GetForegroundWindow(), out foregroundPid);
                if (foregroundPid == (uint)Environment.ProcessId)
                {
                    var data = Marshal.PtrToStructure<KeyboardHookData>(lParam);
                    var key = KeyInterop.KeyFromVirtualKey((int)data.VkCode);

                    var modifiers = ModifierKeys.None;
                    if ((GetAsyncKeyState(0x10) & 0x8000) != 0) modifiers |= ModifierKeys.Shift;  // VK_SHIFT
                    if ((GetAsyncKeyState(0x11) & 0x8000) != 0) modifiers |= ModifierKeys.Control; // VK_CONTROL
                    if ((GetAsyncKeyState(0x12) & 0x8000) != 0) modifiers |= ModifierKeys.Alt;     // VK_MENU

                    if (Hotkeys.TryHandle(key, modifiers, this))
                        return (IntPtr)1; // клавиша перехвачена — не отдаём ни движку, ни системе
                }
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_started) return;
        _started = true;

        if (_startupUrls.Length > 0)
        {
            // Запуск с аргументами: по URL на вкладку (для тестов и «ярлыков»).
            // Вкладка активируется перед навигацией — движок создаётся лениво,
            // только когда вкладка видима.
            foreach (var raw in _startupUrls)
            {
                var url = NavigationService.BuildUrl(raw, _settings.Current.SearchUrl) ?? raw;
                var tab = _tabManager.NewTab();
                await _tabManager.Navigate(tab, url);
            }
            _tabManager.ActivateTab(_tabManager.Tabs[0]);
        }
        else
        {
            _tabManager.NewTab(HomeUrl);
        }
    }

    // ---- Вкладки ----

    private void NewTab()
    {
        _tabManager.NewTab(HomeUrl);
        Toolbar.FocusAddress();
    }

    private void CloseTab(Tab tab)
    {
        _tabManager.CloseTab(tab);
        if (_tabManager.Tabs.Count == 0) Close();
    }

    private void OnActiveTabChanged(Tab tab)
    {
        Toolbar.SetUrl(tab.Url);
        RefreshNavState();
        UpdateTitle(tab);

        if (string.IsNullOrEmpty(tab.Url))
            Toolbar.FocusAddress();
    }

    private void RefreshTabStrip() => TabStrip.Items = _tabManager.Tabs.ToList();

    private void RefreshNavState()
    {
        var view = _tabManager.ActiveView;
        Toolbar.SetNavState(view?.CanGoBack == true, view?.CanGoForward == true);
    }

    private void OnNavigated(Tab tab, string url)
    {
        if (tab == _tabManager.ActiveTab)
        {
            Toolbar.SetUrl(url);
            RefreshNavState();
        }
        StatusText.Text = url;
    }

    private void UpdateTitle(Tab tab)
    {
        if (_titleTab is not null) _titleTab.PropertyChanged -= OnTitleChanged;
        _titleTab = tab;
        tab.PropertyChanged += OnTitleChanged;
        Title = $"{tab.Title} — Mini Browser";
    }

    private void OnTitleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Tab.Title) or null && _titleTab is not null)
            Title = $"{_titleTab.Title} — Mini Browser";
    }

    // ---- Действия (IBrowserActions) ----

    void IBrowserActions.NewTab() => NewTab();

    void IBrowserActions.CloseActiveTab()
    {
        if (_tabManager.ActiveTab is { } tab) CloseTab(tab);
    }

    void IBrowserActions.FocusAddressBar() => Toolbar.FocusAddress();

    void IBrowserActions.NextTab() => SwitchTab(+1);

    void IBrowserActions.PrevTab() => SwitchTab(-1);

    void IBrowserActions.AddBookmark() => AddBookmark();

    void IBrowserActions.GoBack() => _tabManager.GoBackActive();
    void IBrowserActions.GoForward() => _tabManager.GoForwardActive();

    void IBrowserActions.ToggleFullscreen()
    {
        _manualFullscreen = !_manualFullscreen;
        ApplyFullscreen();
    }

    void IBrowserActions.ExitFullscreen()
    {
        _manualFullscreen = false;
        ApplyFullscreen();
    }

    bool IBrowserActions.IsManualFullscreen => _manualFullscreen;

    void IBrowserActions.Reload() => _tabManager.ReloadActive();

    private void SwitchTab(int delta)
    {
        var tabs = _tabManager.Tabs;
        if (_tabManager.ActiveTab is null || tabs.Count == 0) return;

        var current = 0;
        for (var i = 0; i < tabs.Count; i++)
        {
            if (tabs[i] == _tabManager.ActiveTab) { current = i; break; }
        }

        var index = (current + delta + tabs.Count) % tabs.Count;
        _tabManager.ActivateTab(tabs[index]);
    }

    private void AddBookmark()
    {
        if (_tabManager.ActiveTab is not { } tab || string.IsNullOrWhiteSpace(tab.Url)) return;
        _storage.AddBookmark(tab.Url, tab.Title);
        StatusText.Text = $"Закладка сохранена: {tab.Title}";
    }

    // ---- Системные обработчики ----

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int pv, int cb);

    /// <summary>Тёмная заголовочная строка окна (DWMWA_USE_IMMERSIVE_DARK_MODE).</summary>
    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var dark = 1;
        if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
            DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
    }

    /// <summary>Страница (видео) запросила или сняла HTML5-полноэкранный режим.</summary>
    private void OnPageFullscreenChanged(bool isFullScreen)
    {
        _pageFullscreen = isFullScreen;
        ApplyFullscreen();
    }

    /// <summary>
    /// Окно уходит в полноэкранный режим, если активен хотя бы один из источников.
    /// Состояние окна считается из флагов, а не накапливается — выход из одного
    /// режима не должен ломать другой.
    /// </summary>
    private void ApplyFullscreen()
    {
        var fullscreen = _manualFullscreen || _pageFullscreen;
        if (fullscreen == _isFullscreenApplied) return;
        _isFullscreenApplied = fullscreen;

        if (fullscreen)
        {
            // Запоминаем состояние окна только при реальном уходе в fullscreen,
            // иначе повторные переключения перезапишут эталон.
            _savedWindowStyle = WindowStyle;
            _savedResizeMode = ResizeMode;
            _savedWindowState = WindowState;

            Toolbar.Visibility = Visibility.Collapsed;
            TabStrip.Visibility = Visibility.Collapsed;
            StatusText.Visibility = Visibility.Collapsed;

            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            Toolbar.Visibility = Visibility.Visible;
            TabStrip.Visibility = Visibility.Visible;
            StatusText.Visibility = Visibility.Visible;

            WindowStyle = _savedWindowStyle;
            ResizeMode = _savedResizeMode;
            WindowState = _savedWindowState;
        }
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (Hotkeys.TryHandle(e.Key, Keyboard.Modifiers, this))
            e.Handled = true;
    }

    private void Window_Closed(object sender, EventArgs e)
    {
        if (_keyboardHook != HookFailed)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = HookFailed;
        }
        GC.KeepAlive(_hookProc);
        _tabManager.Shutdown();
        _storage.Dispose();
    }
}
