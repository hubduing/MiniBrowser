using System.Windows;
using System.Windows.Controls;
using MiniBrowser.Models;
using MiniBrowser.Views;

namespace MiniBrowser.Services;

/// <summary>Управляет жизненным циклом вкладок: создание, закрытие, активация, усыпление.</summary>
public sealed class TabManager
{
    private readonly Panel _contentHost;
    private readonly StorageService _storage;
    private readonly IBrowserActions _actions;
    private readonly List<Tab> _tabs = new();
    private readonly Dictionary<Tab, BrowserTabView> _views = new();
    private readonly TabSleeper _sleeper;

    public event Action? TabsChanged;
    public event Action<Tab>? ActiveTabChanged;
    public event Action<Tab, string>? Navigated;
    public event Action<Tab>? StateChanged;

    /// <summary>Активная вкладка: страница вошла/вышла из HTML5-полноэкранного режима.</summary>
    public event Action<bool>? ActiveViewFullscreenChanged;

    public IReadOnlyList<Tab> Tabs => _tabs;
    public Tab? ActiveTab { get; private set; }

    public BrowserTabView? ActiveView =>
        ActiveTab is null ? null : _views.GetValueOrDefault(ActiveTab);

    public TabManager(Panel contentHost, StorageService storage, IBrowserActions actions)
    {
        _contentHost = contentHost;
        _storage = storage;
        _actions = actions;
        _sleeper = new TabSleeper(this);
        _sleeper.Start();
    }

    public Tab NewTab(string? url = null)
    {
        var tab = new Tab();
        var view = new BrowserTabView(tab, _actions);

        view.Navigated += HandleNavigated;
        view.StateChanged += t => StateChanged?.Invoke(t);
        view.NewWindowRequested += (_, newUrl) => NewTab(newUrl);
        view.FullscreenChanged += (_, isFullscreen) =>
        {
            // Реакция только на активную вкладку: полноэкранный фон в спящей
            // вкладке не должен дёргать окно.
            if (tab.IsActive) ActiveViewFullscreenChanged?.Invoke(isFullscreen);
        };

        _views[tab] = view;
        _tabs.Add(tab);
        _contentHost.Children.Add(view);
        view.Visibility = Visibility.Collapsed;

        TabsChanged?.Invoke();
        ActivateTab(tab);

        if (!string.IsNullOrWhiteSpace(url))
            Navigate(tab, url!);

        return tab;
    }

    public void ActivateTab(Tab tab)
    {
        if (ActiveTab == tab || !_views.ContainsKey(tab)) return;

        if (ActiveTab is { } previous)
        {
            previous.IsActive = false;
            previous.DeactivatedAt = DateTime.UtcNow;
            if (_views.TryGetValue(previous, out var prevView))
                prevView.Visibility = Visibility.Collapsed;
        }

        tab.IsActive = true;
        tab.DeactivatedAt = null;
        _views[tab].Visibility = Visibility.Visible;

        // Вкладку вернули из сна — перезагружаем страницу
        if (tab.IsAsleep && !string.IsNullOrWhiteSpace(tab.Url))
            _ = Navigate(tab, tab.Url);

        ActiveTab = tab;
        ActiveTabChanged?.Invoke(tab);

        // Панели скрыты, вкладки переключаются только хоткеями — сообщаем хосту
        // реальное состояние новой активной вкладки, иначе окно осталось бы
        // без рамки из-за полноэкранного видео на покинутой вкладке.
        ActiveViewFullscreenChanged?.Invoke(_views[tab].IsPageFullscreen);
    }

    public void CloseTab(Tab tab)
    {
        var index = _tabs.IndexOf(tab);
        if (index < 0) return;

        var wasActive = ActiveTab == tab;

        _tabs.RemoveAt(index);
        if (_views.Remove(tab, out var view))
        {
            _contentHost.Children.Remove(view);
            view.Shutdown();
        }

        if (!wasActive)
        {
            TabsChanged?.Invoke();
            return;
        }

        ActiveTab = null;
        if (_tabs.Count > 0)
        {
            var next = _tabs[Math.Min(index, _tabs.Count - 1)];
            ActivateTab(next);
        }

        TabsChanged?.Invoke();
    }

    public Task Navigate(Tab tab, string url)
    {
        tab.DeactivatedAt = null;
        return _views[tab].NavigateAsync(url);
    }

    public void NavigateActive(string url)
    {
        if (ActiveTab is not null) _ = Navigate(ActiveTab, url);
    }

    public void GoBackActive() => ActiveView?.GoBack();
    public void GoForwardActive() => ActiveView?.GoForward();
    public void ReloadActive() => ActiveView?.Reload();
    public void StopActive() => ActiveView?.Stop();

    /// <summary>Выгрузить WebView2 неактивной вкладки (освободить память).</summary>
    public void Sleep(Tab tab)
    {
        if (tab.IsActive || tab.IsAsleep) return;
        if (!_views.TryGetValue(tab, out var view) || !view.HasEngine) return;

        view.Sleep();
        tab.IsAsleep = true;
    }

    public void Shutdown()
    {
        _sleeper.Stop();
        foreach (var view in _views.Values)
            view.Shutdown();
        _views.Clear();
        _tabs.Clear();
        _contentHost.Children.Clear();
    }

    private void HandleNavigated(Tab tab, string url)
    {
        tab.DeactivatedAt = null;
        _storage.AddHistory(url, tab.Title);
        Navigated?.Invoke(tab, url);
    }
}
