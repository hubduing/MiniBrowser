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
    private readonly ICollectionView _bookmarkView;
    private readonly ICollectionView _historyView;

    private string _searchText = string.Empty;
    private int _totalBookmarks;
    private int _totalHistory;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? NavigateRequested;
    public event Action? HistoryChanged;
    public event Action? SettingsChanged;

    public ObservableCollection<BookmarkItem> Bookmarks { get; } = new();
    public ObservableCollection<HistoryItem> History { get; } = new();

    public MenuDrawerViewModel(StorageService storage, SettingsService settings)
    {
        _storage = storage;
        _settings = settings;

        // Представления по умолчанию: разметка биндится к коллекциям и автоматом
        // получает фильтрацию — отдавать ICollectionView наружу не нужно.
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
                if (Bookmarks.Remove(item))
                {
                    _totalBookmarks = Math.Max(0, _totalBookmarks - 1);
                    UpdateCounts();
                }
            },
            // Удаление отдельной записи истории не поддерживается: в истории
            // хранятся посещения, а не закладки пользователя, — только ClearHistory.
            p => p is BookmarkItem);
        ClearHistoryCommand = new RelayCommand(() =>
        {
            _storage.ClearHistory();
            History.Clear();
            _totalHistory = 0;
            UpdateCounts();
            HistoryChanged?.Invoke();
        });
        ResetSettingsCommand = new RelayCommand(() =>
        {
            SettingsService.ResetToDefaults(_settings.Current);
            _settings.Save();
            // Событие строго после изменения Current: хост применит их позже, в своей очереди.
            SettingsChanged?.Invoke();
        });
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
            // История ищется в сервисе, закладки — здесь, поэтому оба списка
            // перезапрашиваем; внутри — Refresh() представлений и пересчёт счётчиков.
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

    /// <summary>Источник для ComboBox поисковой системы.</summary>
    public string[] SearchEngines => NavigationService.EngineNames;

    public string BookmarksCountText => string.IsNullOrEmpty(_searchText)
        ? $"{_totalBookmarks}"
        : $"{Bookmarks.Count} из {_totalBookmarks}";

    public string HistoryCountText => string.IsNullOrEmpty(_searchText)
        ? $"{_totalHistory}"
        : $"{History.Count} из {_totalHistory}";

    public bool HasBookmarks => Bookmarks.Count > 0;
    public bool HasHistory => History.Count > 0;

    /// <summary>Перечитать закладки: хост зовёт при возврате к вкладке.</summary>
    public void RefreshBookmarks()
    {
        var all = _storage.GetAllBookmarks();
        _totalBookmarks = all.Count;
        // Заменять свойство нельзя — представление потеряет источник, только Clear + Add.
        Bookmarks.Clear();
        // Поиска по закладкам в сервисе нет, поэтому отбираем здесь тем же
        // предикатом, что стоит на представлении: коллекция уже хранит совпадения.
        foreach (var b in all)
        {
            if (Matches(b.Title, b.Url)) Bookmarks.Add(new BookmarkItem(b.Url, b.Title));
        }
        _bookmarkView.Refresh();
        UpdateCounts();
    }

    /// <summary>Перечитать историю: хост зовёт при возврате к вкладке.</summary>
    public void RefreshHistory()
    {
        var entries = string.IsNullOrEmpty(_searchText)
            ? _storage.GetHistory()
            : _storage.SearchHistory(_searchText);
        History.Clear();
        // Дедуп уже в сервисе: коллекция уникальна по URL, повторная фильтрация не нужна.
        foreach (var e in entries)
            History.Add(new HistoryItem(e.Url, e.Title, e.VisitCount, e.LastVisit));
        // Сколько всего без поиска — для текста «12 из 84».
        _totalHistory = string.IsNullOrEmpty(_searchText)
            ? History.Count
            : _storage.GetHistory().Count;
        _historyView.Refresh();
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        OnPropertyChanged(nameof(BookmarksCountText));
        OnPropertyChanged(nameof(HistoryCountText));
        OnPropertyChanged(nameof(HasBookmarks));
        OnPropertyChanged(nameof(HasHistory));
    }

    private bool BookmarkFilter(object item) => item is BookmarkItem b && Matches(b.Title, b.Url);

    private bool HistoryFilter(object item) => item is HistoryItem h && Matches(h.Title, h.Url);

    private bool Matches(string title, string url)
    {
        if (string.IsNullOrEmpty(_searchText)) return true;
        return title.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
            || url.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyWindowSize(string? parameter)
    {
        // Формат из разметки «1024,640»: сначала ширина, потом высота.
        var parts = (parameter ?? string.Empty).Split(',');
        if (parts.Length != 2) return;
        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) return;
        if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) return;
        _settings.Current.WindowWidth = width;
        _settings.Current.WindowHeight = height;
        _settings.Save();
        // Событие строго после изменения Current: хост применит их позже, в своей очереди.
        SettingsChanged?.Invoke();
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
