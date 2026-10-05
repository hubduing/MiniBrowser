using System.IO;
using MiniBrowser.Services;
using MiniBrowser.ViewModels;
using Xunit;

public class MenuDrawerViewModelTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mb-vm-tests", Guid.NewGuid().ToString("N"));
    private readonly StorageService _storage;
    private readonly SettingsService _settings;
    private readonly MenuDrawerViewModel _vm;

    public MenuDrawerViewModelTests()
    {
        _storage = new StorageService(Path.Combine(_dir, "browser.db"));
        _settings = new SettingsService(Path.Combine(_dir, "settings.json"));
        _vm = new MenuDrawerViewModel(_storage, _settings);
    }

    public void Dispose()
    {
        _storage.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void RelayCommand_WithoutPredicate_CanExecuteAlwaysTrue()
    {
        var calls = 0;
        var cmd = new RelayCommand(() => calls++);
        Assert.True(cmd.CanExecute(null));
        Assert.True(cmd.CanExecute("что угодно"));
        cmd.Execute(null);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void RelayCommand_Predicate_ControlsCanExecute()
    {
        var cmd = new RelayCommand(_ => { }, p => p is string);
        Assert.True(cmd.CanExecute("да"));
        Assert.False(cmd.CanExecute(42));
        Assert.False(cmd.CanExecute(null));
    }

    [Fact]
    public void RelayCommand_RaiseCanExecuteChanged_NotifiesSubscribers()
    {
        var cmd = new RelayCommand(() => { });
        var raised = 0;
        cmd.CanExecuteChanged += (_, _) => raised++;
        cmd.RaiseCanExecuteChanged();
        Assert.Equal(1, raised);
    }

    [Fact]
    public void RelayCommand_NullExecute_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new RelayCommand((Action)null!));
        Assert.Throws<ArgumentNullException>(() => new RelayCommand((Action<object?>)null!));
    }

    [Fact]
    public void BookmarkItem_HasEmptyDetailAndCanDelete()
    {
        var item = new BookmarkItem("https://example.com/", "Пример");
        Assert.Equal(string.Empty, item.Detail);
        Assert.True(item.CanDelete);
    }

    [Fact]
    public void HistoryItem_CannotDeleteAndKeepsVisitCount()
    {
        var item = new HistoryItem("https://example.com/", "Пример", 3, "2026-10-05 14:22");
        Assert.False(item.CanDelete);
        Assert.Equal(3, item.VisitCount);
    }

    [Theory]
    [InlineData(1, "1 визит")]
    [InlineData(2, "2 визита")]
    [InlineData(4, "4 визита")]
    [InlineData(5, "5 визитов")]
    [InlineData(11, "11 визитов")]
    [InlineData(14, "14 визитов")]
    [InlineData(21, "21 визит")]
    [InlineData(22, "22 визита")]
    [InlineData(25, "25 визитов")]
    [InlineData(111, "111 визитов")]
    [InlineData(112, "112 визитов")]
    [InlineData(121, "121 визит")]
    public void HistoryItem_PluralizesVisits(int count, string expected)
    {
        var item = new HistoryItem("https://example.com/", "Пример", count, "2026-10-05 14:22");
        Assert.Equal($"{expected} · 2026-10-05 14:22", item.Detail);
    }

    [Fact]
    public void RefreshBookmarks_LoadsAllAndCountsWithoutSearch()
    {
        _storage.AddBookmark("https://a.example/", "Альфа");
        _storage.AddBookmark("https://b.example/", "Бета");
        _vm.RefreshBookmarks();
        Assert.Equal(2, _vm.Bookmarks.Count);
        Assert.Equal("2", _vm.BookmarksCountText);
        Assert.True(_vm.HasBookmarks);
    }

    [Fact]
    public void SearchText_FiltersBookmarksByTitleAndUrl()
    {
        _storage.AddBookmark("https://news.example/", "Новости");
        _storage.AddBookmark("https://weather.example/", "Погода");
        _vm.RefreshBookmarks();

        _vm.SearchText = "новости";
        Assert.Single(_vm.Bookmarks);
        Assert.Equal("https://news.example/", _vm.Bookmarks[0].Url);
        Assert.Equal("1 из 2", _vm.BookmarksCountText);

        // Поиск нечувствителен к регистру и смотрит в URL тоже.
        _vm.SearchText = "WEATHER";
        Assert.Single(_vm.Bookmarks);

        _vm.SearchText = string.Empty;
        Assert.Equal(2, _vm.Bookmarks.Count);
        Assert.Equal("2", _vm.BookmarksCountText);
    }

    [Fact]
    public void ClearSearchCommand_ResetsSearchText()
    {
        _storage.AddBookmark("https://a.example/", "Альфа");
        _vm.RefreshBookmarks();
        _vm.SearchText = "альфа";
        Assert.Single(_vm.Bookmarks);
        _vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, _vm.SearchText);
        Assert.Single(_vm.Bookmarks);
    }

    [Fact]
    public void RefreshHistory_DedupesAndSearchesViaService()
    {
        _storage.AddHistory("https://news.example/", "Новости дня");
        _storage.AddHistory("https://news.example/", "Новости дня");
        _storage.AddHistory("https://other.example/", "Другое");
        _vm.RefreshHistory();
        Assert.Equal(2, _vm.History.Count);
        Assert.Equal("2", _vm.HistoryCountText);

        // LIKE в SQLite нечувствителен к регистру только для ASCII,
        // поэтому ищем по URL латиницей: кириллицу в другом регистре сервис не найдёт.
        _vm.SearchText = "other";
        Assert.Single(_vm.History);
        Assert.Equal("1 из 2", _vm.HistoryCountText);
        Assert.True(_vm.HasHistory);
    }

    [Fact]
    public void DeleteItemCommand_RemovesBookmarkWithoutFullReload()
    {
        _storage.AddBookmark("https://a.example/", "A");
        _storage.AddBookmark("https://b.example/", "B");
        _vm.RefreshBookmarks();
        var item = _vm.Bookmarks[0];

        Assert.True(_vm.DeleteItemCommand.CanExecute(item));
        _vm.DeleteItemCommand.Execute(item);
        Assert.Single(_vm.Bookmarks);
        Assert.DoesNotContain(_vm.Bookmarks, b => b.Url == item.Url);
        Assert.DoesNotContain(_storage.GetAllBookmarks(), b => b.Url == item.Url);
        Assert.Equal("1", _vm.BookmarksCountText);
    }

    [Fact]
    public void DeleteItemCommand_HistoryItem_NotSupported()
    {
        _storage.AddHistory("https://a.example/", "A");
        _vm.RefreshHistory();
        var item = Assert.Single(_vm.History);
        Assert.False(item.CanDelete);
        Assert.False(_vm.DeleteItemCommand.CanExecute(item));
        Assert.False(_vm.DeleteItemCommand.CanExecute(null));
    }

    [Fact]
    public void ClearHistoryCommand_ClearsAndRaisesEvent()
    {
        _storage.AddHistory("https://a.example/", "A");
        _vm.RefreshHistory();
        var raised = 0;
        _vm.HistoryChanged += () => raised++;
        _vm.ClearHistoryCommand.Execute(null);
        Assert.Empty(_vm.History);
        Assert.False(_vm.HasHistory);
        Assert.Equal("0", _vm.HistoryCountText);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void OpenUrlCommand_RaisesNavigateRequestedForString()
    {
        string? navigated = null;
        _vm.NavigateRequested += url => navigated = url;
        _vm.OpenUrlCommand.Execute("https://example.com/");
        Assert.Equal("https://example.com/", navigated);
        // Не строка — молча игнорируем, а не падаем.
        _vm.OpenUrlCommand.Execute(42);
        Assert.Equal("https://example.com/", navigated);
    }

    [Fact]
    public void SetWindowSizeCommand_ParsesWidthThenHeight()
    {
        var raised = 0;
        string? seenWidth = null;
        _vm.SettingsChanged += () =>
        {
            raised++;
            // Событие после изменения Current: хост уже видит новые значения.
            seenWidth = _settings.Current.WindowWidth.ToString();
        };
        _vm.SetWindowSizeCommand.Execute("1024,640");
        Assert.Equal(1024, _settings.Current.WindowWidth);
        Assert.Equal(640, _settings.Current.WindowHeight);
        Assert.Equal(1, raised);
        Assert.Equal("1024", seenWidth);

        // Мусор в параметре — игнорируем без события.
        _vm.SetWindowSizeCommand.Execute("не размер");
        Assert.Equal(1, raised);
    }

    [Fact]
    public void ResetSettingsCommand_RestoresDefaultsAndRaisesEvent()
    {
        _settings.Current.ZoomPercent = 150;
        var raised = 0;
        _vm.SettingsChanged += () => raised++;
        _vm.ResetSettingsCommand.Execute(null);
        Assert.Equal(100, _settings.Current.ZoomPercent);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void SearchEngines_MatchNavigationService()
    {
        Assert.Equal(NavigationService.EngineNames, _vm.SearchEngines);
    }
}
