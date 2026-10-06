using System.Windows;
using System.Windows.Controls;
using MiniBrowser.Models;
using MiniBrowser.Views;

namespace MiniBrowser.Services;

/// <summary>
/// Управляет жизненным циклом вкладок: создание, закрытие, активация, усыпление.
/// Состав и порядок групп лежат в <see cref="TabGroups"/> — здесь только
/// представления (WebView2) и всё, что с ними связано.
/// </summary>
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
    private readonly Dictionary<Tab, BrowserTabView> _views = new();
    private readonly TabSleeper _sleeper;
    // Состав групп меняется разом при восстановлении сессии, поэтому поле
    // пересоздаётся, а подписки на него навешиваются заново.
    private TabGroups _groups = new();

    public event Action? TabsChanged;
    public event Action? GroupsChanged;

    /// <summary>Раскладка изменилась — окно вправе записать сессию на диск.</summary>
    public event Action? SessionDirty;

    public event Action<Tab>? ActiveTabChanged;
    public event Action<Tab, string>? Navigated;
    public event Action<Tab>? StateChanged;

    /// <summary>Активная вкладка: страница вошла/вышла из HTML5-полноэкранного режима.</summary>
    public event Action<bool>? ActiveViewFullscreenChanged;

    public IReadOnlyList<TabGroup> Groups => _groups.Groups;
    public TabGroup? ActiveGroup => _groups.ActiveGroup;
    public IReadOnlyList<Tab> Tabs => _groups.Tabs;
    public Tab? ActiveTab { get; private set; }

    public BrowserTabView? ActiveView =>
        ActiveTab is null ? null : _views.GetValueOrDefault(ActiveTab);

    /// <summary>Вкладка жива: та же, что лежит в группах. Нужна источнику перетаскивания.</summary>
    public bool Contains(Tab tab) => _groups.Tabs.Contains(tab);

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
        AttachGroups(_groups);
        _sleeper = new TabSleeper(this);
        _sleeper.Start();
    }

    public Tab NewTab(string? url = null, TabGroup? group = null)
    {
        var tab = new Tab();
        CreateView(tab);
        _groups.AddTab(group, tab);
        ActivateTab(tab);

        if (!string.IsNullOrWhiteSpace(url))
            Navigate(tab, url!);

        return tab;
    }

    /// <summary>Создать группу. Имя и цвет подставляются, если их не задали.</summary>
    public TabGroup CreateGroup(string? name = null, int? colorIndex = null) =>
        _groups.CreateGroup(name, colorIndex);

    public void RenameGroup(TabGroup group, string name)
    {
        group.Name = name;
        SessionDirty?.Invoke();
    }

    public void SetGroupColor(TabGroup group, int colorIndex)
    {
        group.ColorIndex = Math.Clamp(colorIndex, 0, TabGroups.PaletteSize - 1);
        SessionDirty?.Invoke();
    }

    public void ToggleGroupCollapsed(TabGroup group)
    {
        group.IsCollapsed = !group.IsCollapsed;
        SessionDirty?.Invoke();
    }

    /// <summary>
    /// Состояние групп изменилось мимо операций менеджера (переименование
    /// идёт прямо из колонки) — сообщить окну, что сессию пора записать.
    /// </summary>
    public void MarkSessionDirty() => SessionDirty?.Invoke();

    public void MoveTab(Tab tab, TabGroup target, int index) => _groups.MoveTab(tab, target, index);

    public void MoveGroup(TabGroup group, int newIndex) => _groups.MoveGroup(group, newIndex);

    public void ActivateTab(Tab tab)
    {
        if (!_views.ContainsKey(tab)) return;
        // Уже активная вкладка: состояние и так верное, второй раз событий не нужно.
        if (ActiveTab == tab) return;

        _groups.SetActive(tab);
        OnActiveTabSet(tab);
    }

    public void CloseTab(Tab tab)
    {
        var group = _groups.GroupOf(tab);
        if (group is null) return;

        var wasActive = ActiveTab == tab;
        // Следующую вкладку выбираем ДО удаления, иначе индексы уедут.
        Tab? next = null;
        if (wasActive)
        {
            var at = group.Tabs.IndexOf(tab);
            next = at > 0 ? group.Tabs[at - 1] : group.Tabs.ElementAtOrDefault(at + 1);
            next ??= _groups.Tabs.FirstOrDefault(t => t != tab);
            // Активируем заранее: тогда RemoveTab не поднимет лишнего события.
            if (next is not null) _groups.SetActive(next);
        }

        if (_views.Remove(tab, out var view))
        {
            _contentHost.Children.Remove(view);
            view.Shutdown();
        }

        // Группа остаётся: пустая группа — законная заготовка.
        _groups.RemoveTab(tab);

        if (wasActive && next is null)
            OnActiveTabSet(null!);
    }

    /// <summary>Закрыть все вкладки группы и саму группу.</summary>
    public void CloseGroup(TabGroup group)
    {
        // Снимок: коллекция меняется прямо во время закрытия.
        foreach (var tab in group.Tabs.ToArray())
            CloseTab(tab);

        _groups.RemoveGroup(group);
    }

    /// <summary>
    /// Поднять восстановленную раскладку. Движки WebView2 не создаются: вкладка
    /// списка без движка — норма, он поднимется при первой активации.
    /// </summary>
    public void RestoreGroups(IReadOnlyList<TabGroup> groups, Tab? activeTab)
    {
        AttachGroups(new TabGroups(groups));
        foreach (var group in groups)
        {
            foreach (var tab in group.Tabs)
                CreateView(tab);
        }

        var target = activeTab is not null && _views.ContainsKey(activeTab)
            ? activeTab
            : _groups.Tabs.FirstOrDefault();

        if (target is not null)
        {
            ActivateTab(target);
            // Страницу восстанавливаем только у активной: остальные вкладки
            // поднимут свой движок и адрес сами, когда пользователь на них придёт.
            if (!string.IsNullOrWhiteSpace(target.Url))
                _ = Navigate(target, target.Url);
        }

        TabsChanged?.Invoke();
        GroupsChanged?.Invoke();
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
        _groups.Clear();
        _contentHost.Children.Clear();
    }

    private void CreateView(Tab tab)
    {
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
        _contentHost.Children.Add(view);
        view.Visibility = Visibility.Collapsed;
    }

    /// <summary>Перехватить события нового набора групп.</summary>
    private void AttachGroups(TabGroups groups)
    {
        _groups = groups;
        _groups.Changed += () =>
        {
            TabsChanged?.Invoke();
            SessionDirty?.Invoke();
        };
        _groups.GroupsChanged += () =>
        {
            GroupsChanged?.Invoke();
            TabsChanged?.Invoke();
            SessionDirty?.Invoke();
        };
        _groups.ActiveChanged += OnActiveTabSet;
    }

    /// <summary>
    /// Единственная точка смены активной вкладки: и клик по вкладке, и
    /// внутренняя логика TabGroups приходят сюда.
    /// </summary>
    private void OnActiveTabSet(Tab? tab)
    {
        if (ReferenceEquals(ActiveTab, tab)) return;

        if (ActiveTab is { } previous)
        {
            previous.IsActive = false;
            previous.DeactivatedAt = DateTime.UtcNow;
            if (_views.TryGetValue(previous, out var prevView))
                prevView.Visibility = Visibility.Collapsed;
        }

        ActiveTab = tab;
        if (tab is not null)
        {
            tab.DeactivatedAt = null;
            if (_views.TryGetValue(tab, out var view))
            {
                view.Visibility = Visibility.Visible;
                // Вкладку вернули из сна — перезагружаем страницу
                if (tab.IsAsleep && !string.IsNullOrWhiteSpace(tab.Url))
                    _ = Navigate(tab, tab.Url);
            }
        }

        if (tab is not null) ActiveTabChanged?.Invoke(tab);

        // Панели скрыты, вкладки переключаются только хоткеями — сообщаем хосту
        // реальное состояние новой активной вкладки, иначе окно осталось бы
        // без рамки из-за полноэкранного видео на покинутой вкладке.
        ActiveViewFullscreenChanged?.Invoke(ActiveView?.IsPageFullscreen ?? false);
    }

    private void HandleNavigated(Tab tab, string url)
    {
        tab.DeactivatedAt = null;
        _storage.AddHistory(url, tab.Title);
        Navigated?.Invoke(tab, url);
    }
}