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
    // Источник текущего множителя масштаба — хост даёт `() => _settings.EffectiveZoom`.
    private readonly Func<double> _zoomProvider;
    // Провайдеры блокировки приходят от хоста: сам список фильтров и белый
    // список живут в AdBlockService, а вкладкам нужно только решение по запросу.
    private readonly Func<Uri, string?, bool>? _isBlocked;
    private readonly Func<string?, string>? _cosmeticScriptProvider;
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

    public TabManager(
        Panel contentHost,
        StorageService storage,
        IBrowserActions actions,
        Func<double> zoomProvider,
        Func<Uri, string?, bool>? isBlocked = null,
        Func<string?, string>? cosmeticScriptProvider = null)
    {
        _contentHost = contentHost;
        _storage = storage;
        _actions = actions;
        _zoomProvider = zoomProvider ?? throw new ArgumentNullException(nameof(zoomProvider));
        _isBlocked = isBlocked;
        _cosmeticScriptProvider = cosmeticScriptProvider;
        _sleeper = new TabSleeper(this);
        _sleeper.Start();
    }

    public Tab NewTab(string? url = null)
    {
        var tab = new Tab();
        var view = new BrowserTabView(tab, _actions, _zoomProvider, _isBlocked, _cosmeticScriptProvider);

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

    /// <summary>
    /// Действие совмещённой кнопки «⟳/✕»: во время загрузки останавливает,
    /// на settled-странице перезагружает.
    /// </summary>
    public void ReloadOrStopActive()
    {
        if (ActiveView is not { } view) return;
        if (ReloadStopPolicy.Decide(view.IsLoading) == ReloadStopAction.Stop)
            view.Stop();
        else
            view.Reload();
    }

    /// <summary>Разослать новый множитель масштаба всем вкладкам (смена настройки).</summary>
    public void ApplyZoomToAllTabs()
    {
        foreach (var view in _views.Values)
            view.ZoomChanged();
    }

    public void RefreshAdBlockOnAllTabs()
    {
        foreach (var view in _views.Values)
            view.RefreshCosmeticScript();
    }

    /// <summary>Перерегистрировать маскировку в одной вкладке и перезагрузить её.</summary>
    public void RefreshAdBlockForTab(Tab tab)
    {
        if (!_views.TryGetValue(tab, out var view)) return;
        view.RefreshCosmeticScript();
        view.Reload();
    }

    /// <summary>Сколько запросов заблокировано на активной вкладке (для щита в тулбаре).</summary>
    public int ActiveBlockedRequestCount => ActiveView?.BlockedRequestCount ?? 0;

    /// <summary>Есть ли вкладки с этим хостом — их индикаторы блокировки тоже меняются.</summary>
    public bool HasTabForHost(string host) =>
        _views.Keys.Any(t => string.Equals(HostOf(t.Url), host, StringComparison.OrdinalIgnoreCase));

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

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
