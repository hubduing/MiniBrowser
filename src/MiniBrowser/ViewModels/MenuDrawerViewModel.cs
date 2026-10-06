using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using MiniBrowser.Services;

namespace MiniBrowser.ViewModels;

/// <summary>
/// Состояние панели меню: полные списки закладок и истории, поиск, счётчики.
/// Сама панель только привязывается; хост слушает события и применяет изменения.
/// </summary>
public sealed class MenuDrawerViewModel : INotifyPropertyChanged
{
    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    // Список доменов, где пользователь отключил блокировку. Отдельный сервис
    // панели не нужен: правила фильтров её не касаются, нужен только счётчик
    // и умение очистить список. Заодно он чинит null из старого settings.json.
    private readonly AdBlockService _adBlock;
    private readonly ICollectionView _bookmarkView;
    private readonly ICollectionView _historyView;

    private string _searchText = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? NavigateRequested;
    public event Action? HistoryChanged;
    public event Action? SettingsChanged;

    /// <summary>Изменился мастер-выключатель или белый список — щит в тулбаре надо перерисовать.</summary>
    public event Action? AdBlockStateChanged;

    public ObservableCollection<BookmarkItem> Bookmarks { get; } = new();
    public ObservableCollection<HistoryItem> History { get; } = new();

    public MenuDrawerViewModel(StorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;
        _adBlock = new AdBlockService(_settings.Current);

        // Фильтрация — только здесь: коллекции всегда хранят полные данные,
        // а счётчики читают представление. Второй механизм (предфильтр коллекции)
        // сознательно убран, чтобы не создавать ложного впечатления дублирования.
        _bookmarkView = CollectionViewSource.GetDefaultView(Bookmarks);
        _bookmarkView.Filter = new Predicate<object>(BookmarkFilter);
        _historyView = CollectionViewSource.GetDefaultView(History);
        _historyView.Filter = new Predicate<object>(HistoryFilter);

        OpenUrlCommand = new RelayCommand(p =>
        {
            if (p is string url) NavigateRequested?.Invoke(url);
        });
        DeleteItemCommand = new RelayCommand(
            p =>
            {
                if (p is not BookmarkItem item) return;
                _storage.DeleteBookmark(item.Url);
                // Точечное удаление без полной перезагрузки: список большой, моргать им незачем.
                if (Bookmarks.Remove(item)) UpdateCounts();
            },
            // Удаление отдельной записи истории не поддерживается: в истории
            // хранятся посещения, а не закладки пользователя, — только ClearHistory.
            p => p is BookmarkItem);
        ClearHistoryCommand = new RelayCommand(() =>
        {
            _storage.ClearHistory();
            // Поиск сбрасываем: висеть над пустым множеством ему нечего,
            // а счётчик честно станет «0» вместо сбивающего с толку «0 из 0».
            SearchText = string.Empty;
            RefreshHistory();
            HistoryChanged?.Invoke();
        });
        ResetSettingsCommand = new RelayCommand(() =>
        {
            SettingsService.ResetToDefaults(_settings.Current);
            _settings.Save();
            // Сброс вернул блокировку к дефолтам — щит обязан это показать.
            AdBlockStateChanged?.Invoke();
            // Событие строго после изменения Current: хост применит их позже, в своей очереди.
            SettingsChanged?.Invoke();
        });
        ClearAdBlockHostsCommand = new RelayCommand(
            _ =>
            {
                // Без проверки команда отключается: пустая кнопка сброса
                // выглядит как неработающая, а список чаще всего пуст.
                _adBlock.ClearDisabledHosts();
                _settings.Save();
                OnPropertyChanged(nameof(AdBlockDisabledHostsText));
                AdBlockStateChanged?.Invoke();
            },
            _ => DisabledHostsCount > 0);
        ClearSearchCommand = new RelayCommand(() => SearchText = string.Empty);
        SetWindowSizeCommand = new RelayCommand(p => ApplyWindowSize(p as string));

        RefreshBookmarks();
        RefreshHistory();
    }

    /// <summary>Текст поиска: сеттер перезапрашивает источник и обновляет оба списка.</summary>
    public string SearchText
    {
        get => _searchText;
        set
        {
            value ??= string.Empty;
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            // История ищется предикатом в VM, а не в сервисе, но перезапрос всё равно
            // нужен: хост мог добавить посещения, пока панель была скрыта.
            // Внутри каждого Refresh — Refresh() представления и пересчёт счётчиков.
            RefreshBookmarks();
            RefreshHistory();
        }
    }

    public ICommand OpenUrlCommand { get; }
    public ICommand DeleteItemCommand { get; }
    public ICommand ClearHistoryCommand { get; }
    public ICommand ResetSettingsCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand SetWindowSizeCommand { get; }

    /// <summary>Сбросить список сайтов с отключённой блокировкой.</summary>
    public ICommand ClearAdBlockHostsCommand { get; }

    /// <summary>Сколько доменов пользователь исключил из блокировки.</summary>
    public int DisabledHostsCount => _adBlock.DisabledHostsCount;

    public bool HasDisabledHosts => DisabledHostsCount > 0;

    public string AdBlockDisabledHostsText => DisabledHostsCount > 0
        ? $"Реклама не блокируется на {DisabledHostsCount} " +
          (DisabledHostsCount == 1 ? "сайте" : "сайтах")
        : "Белый список пуст — реклама блокируется везде";

    /// <summary>Источник для ComboBox поисковой системы.</summary>
    public string[] SearchEngines => NavigationService.EngineNames;

    /// <summary>Настройки для привязки полей вкладки «Настройки» (Settings.Current.*).</summary>
    public SettingsService Settings => _settings;

    /// <summary>Разметка зовёт после записи в Settings.Current из code-behind (ComboBox через Tag).</summary>
    public void NotifySettingsChanged() => SettingsChanged?.Invoke();

    /// <summary>Разметка зовёт после смены глобального флажка блокировки.</summary>
    public void NotifyAdBlockStateChanged() => AdBlockStateChanged?.Invoke();

    /// <summary>
    /// Пересчитать счётчик белого списка. Зовётся хостом после переключения
    /// блокировки щитом или хоткеем: список меняется мимо панели, и без этого
    /// она показывала бы устаревшее число.
    /// </summary>
    public void RefreshAdBlockHosts()
    {
        OnPropertyChanged(nameof(DisabledHostsCount));
        OnPropertyChanged(nameof(HasDisabledHosts));
        OnPropertyChanged(nameof(AdBlockDisabledHostsText));
    }

    /// <summary>Поиск активен: эффективный запрос не пуст (пробелы не считаются).</summary>
    public bool IsSearching => _searchText.Trim().Length > 0;

    public string BookmarksCountText => IsSearching
        ? $"{VisibleCount(_bookmarkView)} из {Bookmarks.Count}"
        : $"{Bookmarks.Count}";

    public string HistoryCountText => IsSearching
        ? $"{VisibleCount(_historyView)} из {History.Count}"
        : $"{History.Count}";

    /// <summary>В списке вообще есть данные (без учёта поиска).</summary>
    public bool HasBookmarks => Bookmarks.Count > 0;

    /// <summary>В списке вообще есть данные (без учёта поиска).</summary>
    public bool HasHistory => History.Count > 0;

    /// <summary>Поиск активен, но закладок не найдено — показать «ничего не найдено», а не «пусто».</summary>
    public bool BookmarksNoResults => IsSearching && _bookmarkView.IsEmpty;

    /// <summary>Поиск активен, но истории не найдено — показать «ничего не найдено», а не «пусто».</summary>
    public bool HistoryNoResults => IsSearching && _historyView.IsEmpty;

    /// <summary>Перечитать закладки: хост зовёт при возврате к вкладке.</summary>
    public void RefreshBookmarks()
    {
        var all = _storage.GetAllBookmarks();
        // Заменять свойство нельзя — представление потеряет источник, только Clear + Add.
        // Коллекция всегда полная: отбором занимается Filter представления.
        Bookmarks.Clear();
        foreach (var b in all)
            Bookmarks.Add(new BookmarkItem(b.Url, b.Title));
        _bookmarkView.Refresh();
        UpdateCounts();
    }

    /// <summary>Перечитать историю: хост зовёт при возврате к вкладке.</summary>
    public void RefreshHistory()
    {
        // Всегда полная выборка, а не SearchHistory: SQLite LIKE складывает регистр
        // только для ASCII, и «НОВОСТИ» не нашли бы «новости». VM-предикат через
        // OrdinalIgnoreCase корректен для всей строки, а чинить LIKE без ICU/FTS5
        // нельзя — это отдельный follow-up по задаче 2. Панели хватает свежих 200:
        // это быстрый доступ, а не архивный поиск, зато запрос теперь один, а не два.
        var entries = _storage.GetHistory();
        History.Clear();
        foreach (var e in entries)
            History.Add(new HistoryItem(e.Url, e.Title, e.VisitCount, e.LastVisit));
        _historyView.Refresh();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(IsSearching));
        OnPropertyChanged(nameof(BookmarksCountText));
        OnPropertyChanged(nameof(HistoryCountText));
        OnPropertyChanged(nameof(HasBookmarks));
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(BookmarksNoResults));
        OnPropertyChanged(nameof(HistoryNoResults));
    }

    private bool BookmarkFilter(object item) => item is BookmarkItem b && Matches(b.Title, b.Url);

    private bool HistoryFilter(object item) => item is HistoryItem h && Matches(h.Title, h.Url);

    private bool Matches(string title, string url)
    {
        // Пробелы по краям поиском не считаются: запрос из одних пробелов —
        // это пустой поиск, а не буквальный поиск пробела.
        var query = _searchText.Trim();
        if (query.Length == 0) return true;
        return title.Contains(query, StringComparison.OrdinalIgnoreCase)
            || url.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private static int VisibleCount(ICollectionView view)
    {
        // Представление уже отфильтровано: перечисление даёт только видимые элементы.
        var count = 0;
        foreach (var _ in view) count++;
        return count;
    }

    private void ApplyWindowSize(string? parameter)
    {
        // Формат из разметки «1024,640»: сначала ширина, потом высота.
        var parts = (parameter ?? string.Empty).Split(',');
        if (parts.Length != 2) return;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) return;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) return;
        // Хост положит значения в размер окна: NaN, бесконечности и неположительные
        // значения его сломают, поэтому отсекаем здесь, а не надеемся на Save().
        if (!double.IsFinite(width) || !double.IsFinite(height)) return;
        if (width <= 0 || height <= 0) return;
        _settings.Current.WindowWidth = width;
        _settings.Current.WindowHeight = height;
        _settings.Save();
        // Событие строго после изменения Current: хост применит их позже, в своей очереди.
        SettingsChanged?.Invoke();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
