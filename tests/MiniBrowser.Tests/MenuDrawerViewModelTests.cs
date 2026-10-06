using System.IO;
using System.Windows.Data;
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
    [InlineData(12, "12 визитов")]
    [InlineData(13, "13 визитов")]
    [InlineData(14, "14 визитов")]
    [InlineData(21, "21 визит")]
    [InlineData(22, "22 визита")]
    [InlineData(25, "25 визитов")]
    [InlineData(101, "101 визит")]
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

        // Коллекция — всегда полные данные, отбор виден только через представление.
        _vm.SearchText = "новости";
        Assert.Equal(2, _vm.Bookmarks.Count);
        Assert.Equal(1, VisibleBookmarks());
        Assert.Equal("1 из 2", _vm.BookmarksCountText);
        Assert.True(_vm.IsSearching);
        Assert.True(_vm.HasBookmarks);
        Assert.False(_vm.BookmarksNoResults);

        // Поиск нечувствителен к регистру и смотрит в URL тоже.
        _vm.SearchText = "WEATHER";
        Assert.Equal(1, VisibleBookmarks());

        _vm.SearchText = string.Empty;
        Assert.Equal(2, VisibleBookmarks());
        Assert.Equal("2", _vm.BookmarksCountText);
        Assert.False(_vm.IsSearching);
    }

    [Fact]
    public void SearchText_FilterLivesOnView_RemovingItShowsAll()
    {
        _storage.AddBookmark("https://news.example/", "Новости");
        _storage.AddBookmark("https://weather.example/", "Погода");
        _vm.RefreshBookmarks();
        _vm.SearchText = "новости";

        var view = CollectionViewSource.GetDefaultView(_vm.Bookmarks);
        Assert.NotNull(view.Filter);
        Assert.Single(view.Cast<object>());

        // Без фильтра представление показывает всё: механизм поиска — именно Filter,
        // а не предфильтр коллекции (коллекция всё время хранит оба элемента).
        view.Filter = null;
        view.Refresh();
        Assert.Equal(2, view.Cast<object>().Count());
    }

    [Fact]
    public void SearchText_FilterLivesOnHistoryView()
    {
        _storage.AddHistory("https://news.example/", "Новости дня");
        _storage.AddHistory("https://other.example/", "Другое");
        _vm.RefreshHistory();
        _vm.SearchText = "новости";

        var view = CollectionViewSource.GetDefaultView(_vm.History);
        Assert.NotNull(view.Filter);
        Assert.Single(view.Cast<object>());
        Assert.Equal("1 из 2", _vm.HistoryCountText);
    }

    [Fact]
    public void SearchText_WhitespaceOnly_TreatedAsNoSearch()
    {
        _storage.AddBookmark("https://a.example/", "Альфа");
        _storage.AddBookmark("https://b.example/", "Бета");
        _vm.RefreshBookmarks();

        _vm.SearchText = "   ";
        Assert.False(_vm.IsSearching);
        Assert.Equal(2, VisibleBookmarks());
        Assert.Equal("2", _vm.BookmarksCountText);
        Assert.False(_vm.BookmarksNoResults);
    }

    [Fact]
    public void SearchText_NoMatches_SetsNoResultsButKeepsHas()
    {
        _storage.AddBookmark("https://a.example/", "Альфа");
        _vm.RefreshBookmarks();

        _vm.SearchText = "такого нет";
        Assert.True(_vm.IsSearching);
        Assert.True(_vm.HasBookmarks);
        Assert.True(_vm.BookmarksNoResults);
        Assert.Equal("0 из 1", _vm.BookmarksCountText);
    }

    [Fact]
    public void EmptyLists_HaveNoResultsFalse()
    {
        Assert.False(_vm.HasBookmarks);
        Assert.False(_vm.HasHistory);
        Assert.False(_vm.IsSearching);
        Assert.False(_vm.BookmarksNoResults);
        Assert.False(_vm.HistoryNoResults);
        Assert.Equal("0", _vm.BookmarksCountText);
        Assert.Equal("0", _vm.HistoryCountText);
    }

    [Fact]
    public void ClearSearchCommand_ResetsSearchText()
    {
        _storage.AddBookmark("https://a.example/", "Альфа");
        _storage.AddBookmark("https://b.example/", "Бета");
        _vm.RefreshBookmarks();
        _vm.SearchText = "альфа";
        Assert.Equal(1, VisibleBookmarks());
        _vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, _vm.SearchText);
        Assert.Equal(2, VisibleBookmarks());
        Assert.False(_vm.IsSearching);
    }

    [Fact]
    public void AdBlock_EmptyWhitelistByDefault()
    {
        Assert.Equal(0, _vm.DisabledHostsCount);
        Assert.False(_vm.HasDisabledHosts);
        Assert.Equal("Белый список пуст — реклама блокируется везде", _vm.AdBlockDisabledHostsText);
    }

    [Fact]
    public void AdBlock_ClearCommand_DisabledWhileWhitelistEmpty()
    {
        // Пустая кнопка сброса выглядит как неработающая — она обязана быть серой.
        Assert.False(_vm.ClearAdBlockHostsCommand.CanExecute(null));
    }

    [Fact]
    public void AdBlock_RefreshAdBlockHosts_UpdatesCountAndText()
    {
        _settings.Current.AdBlockDisabledHosts.Add("example.com");
        _vm.RefreshAdBlockHosts();

        Assert.Equal(1, _vm.DisabledHostsCount);
        Assert.True(_vm.HasDisabledHosts);
        Assert.Equal("Реклама не блокируется на 1 сайте", _vm.AdBlockDisabledHostsText);
        Assert.True(_vm.ClearAdBlockHostsCommand.CanExecute(null));
    }

    [Fact]
    public void AdBlock_ClearCommand_ClearsWhitelistAndNotifies()
    {
        _settings.Current.AdBlockDisabledHosts.Add("example.com");
        _settings.Current.AdBlockDisabledHosts.Add("site.org");
        _vm.RefreshAdBlockHosts();

        var notified = 0;
        _vm.AdBlockStateChanged += () => notified++;
        _vm.ClearAdBlockHostsCommand.Execute(null);

        Assert.Empty(_settings.Current.AdBlockDisabledHosts);
        Assert.Equal(1, notified);
        Assert.Equal("Белый список пуст — реклама блокируется везде", _vm.AdBlockDisabledHostsText);
    }

    [Fact]
    public void AdBlock_PluralTextMatchesCount()
    {
        _settings.Current.AdBlockDisabledHosts.Add("a.example");
        _settings.Current.AdBlockDisabledHosts.Add("b.example");
        _settings.Current.AdBlockDisabledHosts.Add("c.example");
        _vm.RefreshAdBlockHosts();

        Assert.Equal("Реклама не блокируется на 3 сайтах", _vm.AdBlockDisabledHostsText);
    }

    [Fact]
    public void ResetSettingsCommand_NotifiesAdBlockState()
    {
        _settings.Current.AdBlockEnabled = false;
        _settings.Current.AdBlockDisabledHosts.Add("example.com");
        var notified = 0;
        _vm.AdBlockStateChanged += () => notified++;

        _vm.ResetSettingsCommand.Execute(null);

        // Сброс вернул блокировку к дефолтам, и щит в тулбаре обязан узнать.
        Assert.True(_settings.Current.AdBlockEnabled);
        Assert.Empty(_settings.Current.AdBlockDisabledHosts);
        Assert.Equal(1, notified);
    }

    [Fact]
    public void RefreshHistory_DedupesAndCountsWithoutSearch()
    {
        _storage.AddHistory("https://news.example/", "Новости дня");
        _storage.AddHistory("https://news.example/", "Новости дня");
        _storage.AddHistory("https://other.example/", "Другое");
        _vm.RefreshHistory();
        Assert.Equal(2, _vm.History.Count);
        Assert.Equal("2", _vm.HistoryCountText);
        Assert.True(_vm.HasHistory);
    }

    [Fact]
    public void SearchText_FindsCyrillicHistoryCaseInsensitively()
    {
        // Тот же запрос, что у закладок: «новости» находит «Новости дня».
        // Через SQLite LIKE это не работало (регистр складывается только для ASCII),
        // поэтому история фильтруется VM-предикатом, как и закладки.
        _storage.AddHistory("https://news.example/", "Новости дня");
        _storage.AddHistory("https://other.example/", "Другое");
        _vm.RefreshHistory();

        _vm.SearchText = "новости";
        Assert.Equal(2, _vm.History.Count);
        Assert.Equal(1, VisibleHistory());
        Assert.Equal("1 из 2", _vm.HistoryCountText);

        // Обратное направление: запрос капсом находит строчный заголовок.
        _vm.SearchText = "НОВОСТИ";
        Assert.Equal(1, VisibleHistory());
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
    public void DeleteItemCommand_WhileSearching_UpdatesViewAndCounter()
    {
        _storage.AddBookmark("https://a.example/", "A");
        _storage.AddBookmark("https://b.example/", "B");
        _vm.RefreshBookmarks();
        _vm.SearchText = "example";
        Assert.Equal("2 из 2", _vm.BookmarksCountText);

        _vm.DeleteItemCommand.Execute(_vm.Bookmarks[0]);
        Assert.Equal(1, VisibleBookmarks());
        Assert.Equal("1 из 1", _vm.BookmarksCountText);
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
    public void ClearHistoryCommand_WhileSearching_ResetsSearch()
    {
        _storage.AddHistory("https://other.example/", "Other");
        _storage.AddBookmark("https://other.example/", "Other");
        _vm.RefreshBookmarks();
        _vm.RefreshHistory();
        _vm.SearchText = "other";
        Assert.Equal("1 из 1", _vm.HistoryCountText);

        var raised = 0;
        _vm.HistoryChanged += () => raised++;
        _vm.ClearHistoryCommand.Execute(null);

        Assert.Equal(string.Empty, _vm.SearchText);
        Assert.False(_vm.IsSearching);
        Assert.Empty(_vm.History);
        Assert.Equal("0", _vm.HistoryCountText);
        Assert.False(_vm.HistoryNoResults);
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

    [Theory]
    [InlineData("-100,-50")]
    [InlineData("0,640")]
    [InlineData("NaN,640")]
    [InlineData("1024,NaN")]
    [InlineData("Infinity,640")]
    [InlineData("1024,Infinity")]
    [InlineData("-Infinity,640")]
    [InlineData("")]
    [InlineData("1024,640,100")]
    [InlineData("1024")]
    public void SetWindowSizeCommand_InvalidValues_IgnoredWithoutEvent(string parameter)
    {
        var raised = 0;
        _vm.SettingsChanged += () => raised++;
        var width = _settings.Current.WindowWidth;
        var height = _settings.Current.WindowHeight;
        _vm.SetWindowSizeCommand.Execute(parameter);
        Assert.Equal(width, _settings.Current.WindowWidth);
        Assert.Equal(height, _settings.Current.WindowHeight);
        Assert.Equal(0, raised);
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

    [Fact]
    public void UnavailableDatabase_ViewModelOpensWithEmptyLists()
    {
        // Путь внутри существующего файла: SQLite не откроет БД,
        // StorageService деградирует в IsAvailable == false вместо исключения.
        var blocker = Path.Combine(Path.GetTempPath(), "mb-vm-blocker-" + Guid.NewGuid().ToString("N") + ".db");
        File.WriteAllText(blocker, "not a database");
        try
        {
            using var broken = new StorageService(Path.Combine(blocker, "nested.db"));
            Assert.False(broken.IsAvailable);

            // Панель обязана открыться с пустыми списками, а не упасть в конструкторе.
            var vm = new MenuDrawerViewModel(broken, _settings);
            Assert.Empty(vm.Bookmarks);
            Assert.Empty(vm.History);
            Assert.Equal("0", vm.BookmarksCountText);
            Assert.Equal("0", vm.HistoryCountText);

            // И команды не должны прокидывать исключения в UI-поток.
            vm.SearchText = "что угодно";
            vm.RefreshBookmarks();
            vm.RefreshHistory();
            vm.DeleteItemCommand.Execute(new BookmarkItem("https://a.example/", "A"));
            vm.ClearHistoryCommand.Execute(null);
            vm.ClearSearchCommand.Execute(null);
        }
        finally
        {
            if (File.Exists(blocker)) File.Delete(blocker);
        }
    }

    private int VisibleBookmarks() =>
        CollectionViewSource.GetDefaultView(_vm.Bookmarks).Cast<object>().Count();

    private int VisibleHistory() =>
        CollectionViewSource.GetDefaultView(_vm.History).Cast<object>().Count();
}
