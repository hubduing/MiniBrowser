using System.IO;
using MiniBrowser.Services;
using Xunit;

public class StorageServiceTests : IDisposable
{
    private readonly string _dbPath = Path.Combine(
        Path.GetTempPath(), "mb-tests", Guid.NewGuid().ToString("N"), "browser.db");
    private readonly StorageService _storage;

    public StorageServiceTests() => _storage = new StorageService(_dbPath);

    public void Dispose()
    {
        _storage.Dispose();
        var dir = Path.GetDirectoryName(_dbPath)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void AddBookmark_NothingStored_ReturnsEmpty()
    {
        Assert.Empty(_storage.GetAllBookmarks());
    }

    [Fact]
    public void AddBookmark_MoreThanTwelve_ReturnsAll()
    {
        for (var i = 0; i < 20; i++)
            _storage.AddBookmark($"https://site{i}.example/", $"Сайт {i}");

        var all = _storage.GetAllBookmarks();
        Assert.Equal(20, all.Count);
        Assert.Contains(all, b => b.Url == "https://site19.example/");
    }

    [Fact]
    public void AddBookmark_SameUrlTwice_KeepsOneRow()
    {
        _storage.AddBookmark("https://example.com/", "Первый");
        _storage.AddBookmark("https://example.com/", "Второй");
        Assert.Single(_storage.GetAllBookmarks());
    }

    [Fact]
    public void DeleteBookmark_RemovesOnlyThatUrl()
    {
        _storage.AddBookmark("https://a.example/", "A");
        _storage.AddBookmark("https://b.example/", "B");
        _storage.DeleteBookmark("https://a.example/");
        var all = _storage.GetAllBookmarks();
        Assert.Single(all);
        Assert.Equal("https://b.example/", all[0].Url);
    }

    [Fact]
    public void DeleteBookmark_UnknownUrl_DoesNothing()
    {
        _storage.AddBookmark("https://a.example/", "A");
        _storage.DeleteBookmark("https://нет-такой.example/");
        Assert.Single(_storage.GetAllBookmarks());
    }

    [Fact]
    public void GetHistory_RepeatedVisits_DedupesByUrlWithCount()
    {
        _storage.AddHistory("https://news.example/", "Новости");
        _storage.AddHistory("https://news.example/", "Новости");
        _storage.AddHistory("https://news.example/", "Новости");
        _storage.AddHistory("https://other.example/", "Другое");

        var history = _storage.GetHistory();
        Assert.Equal(2, history.Count);
        var news = history.Single(h => h.Url == "https://news.example/");
        Assert.Equal(3, news.VisitCount);
        Assert.NotEmpty(news.LastVisit);
    }

    [Fact]
    public void GetHistory_SameUrlRetitled_KeepsLatestTitle()
    {
        // Заголовок — из самой свежей строки URL, а не MAX(title):
        // «Ящик» алфавитно больше «Арбуза», но последнее посещение — с «Арбузом».
        _storage.AddHistory("https://news.example/", "Ящик");
        Thread.Sleep(1100); // visited_at с точностью до секунды
        _storage.AddHistory("https://news.example/", "Арбуз");

        var entry = Assert.Single(_storage.GetHistory());
        Assert.Equal(2, entry.VisitCount);
        Assert.Equal("Арбуз", entry.Title);
        // LastVisit — дата именно последнего посещения, а не первого.
        Assert.NotEmpty(entry.LastVisit);
        Assert.Equal(_storage.GetRecentHistory()[0].VisitedAt, entry.LastVisit);
    }

    [Fact]
    public void GetHistory_OrdersByMostRecentFirst()
    {
        _storage.AddHistory("https://first.example/", "Первый");
        Thread.Sleep(1100); // visited_at с точностью до секунды
        _storage.AddHistory("https://second.example/", "Второй");

        var history = _storage.GetHistory();
        Assert.Equal("https://second.example/", history[0].Url);
    }

    [Fact]
    public void SearchHistory_MatchesByTitle()
    {
        _storage.AddHistory("https://a.example/", "Отличные новости");
        _storage.AddHistory("https://b.example/", "Погода");
        var found = _storage.SearchHistory("новости");
        Assert.Single(found);
        Assert.Equal("https://a.example/", found[0].Url);
    }

    [Fact]
    public void SearchHistory_MatchesByUrl()
    {
        _storage.AddHistory("https://github.com/aspnet", "Репозитории");
        var found = _storage.SearchHistory("github");
        Assert.Single(found);
    }

    [Fact]
    public void SearchHistory_SqlWildcardsInQuery_MatchLiterally()
    {
        _storage.AddHistory("https://a.example/100%", "Скидка 100%");
        _storage.AddHistory("https://b.example/other", "Обычная страница");

        Assert.Single(_storage.SearchHistory("100%"));
        // Одиночный _ в LIKE — любой символ; экранирование обязано отсечь b.example
        Assert.Empty(_storage.SearchHistory("_example_"));
    }

    [Fact]
    public void SearchHistory_SingleQuoteInQuery_DoesNotThrow()
    {
        _storage.AddHistory("https://a.example/", "Тест");
        var found = _storage.SearchHistory("O'Brien");
        Assert.Empty(found);
    }

    [Fact]
    public void SearchHistory_EmptyQuery_ReturnsAllDeduped()
    {
        _storage.AddHistory("https://a.example/", "A");
        _storage.AddHistory("https://a.example/", "A");
        _storage.AddHistory("https://b.example/", "B");
        Assert.Equal(2, _storage.SearchHistory("").Count);
    }

    [Fact]
    public void SearchHistory_NullQuery_ReturnsAllDeduped()
    {
        _storage.AddHistory("https://a.example/", "A");
        _storage.AddHistory("https://a.example/", "A");
        _storage.AddHistory("https://b.example/", "B");
        Assert.Equal(2, _storage.SearchHistory(null!).Count);
    }

    [Fact]
    public void ClearHistory_RemovesEverything_ButKeepsBookmarks()
    {
        _storage.AddHistory("https://a.example/", "A");
        _storage.AddBookmark("https://a.example/", "A");
        _storage.ClearHistory();
        Assert.Empty(_storage.GetHistory());
        Assert.Single(_storage.GetAllBookmarks());
    }

    [Fact]
    public void GetRecentHistory_FillsLastVisitFromVisitedAt()
    {
        _storage.AddHistory("https://a.example/", "A");
        var entry = Assert.Single(_storage.GetRecentHistory());
        Assert.NotEmpty(entry.LastVisit);
    }

    [Fact]
    public void UnavailableDatabase_MethodsReturnEmptyWithoutThrowing()
    {
        // Путь внутри существующего файла: SQLite не сможет открыть БД,
        // и StorageService обязан деградировать, а не упасть.
        // Имя уникальное: два параллельных dotnet test иначе делят один blocker-файл.
        var blocker = Path.Combine(Path.GetTempPath(), "mb-blocker-" + Guid.NewGuid().ToString("N") + ".db");
        File.WriteAllText(blocker, "not a database");

        try
        {
            using var broken = new StorageService(Path.Combine(blocker, "nested.db"));
            Assert.False(broken.IsAvailable);
            Assert.Empty(broken.GetAllBookmarks());
            Assert.Empty(broken.GetHistory());
            Assert.Empty(broken.SearchHistory("что угодно"));
            Assert.Empty(broken.GetRecentHistory());

            // Записи не падают и не мешают чтению.
            broken.AddHistory("https://a.example/", "A");
            broken.AddBookmark("https://a.example/", "A");
            broken.DeleteBookmark("https://a.example/");
            broken.ClearHistory();
            Assert.Empty(broken.GetAllBookmarks());
        }
        finally
        {
            // Упавший тест не должен оставлять мусор в %TEMP%.
            if (File.Exists(blocker)) File.Delete(blocker);
        }
    }
}
