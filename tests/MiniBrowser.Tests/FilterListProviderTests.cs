using System.IO;
using System.Net.Http;
using MiniBrowser.Services;
using Xunit;

public class FilterListProviderTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mb-flp-tests", Guid.NewGuid().ToString("N"));
    private readonly string _cachePath;

    public FilterListProviderTests() => _cachePath = Path.Combine(_dir, "easylist.txt");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    /// <summary>Загрузчик, который всегда падает: режим «сети нет».</summary>
    private static Func<string, string, CancellationToken, Task<string>> Offline =>
        (_, _, _) => throw new HttpRequestException("offline");

    private static Func<string, string, CancellationToken, Task<string>> Returning(string body) =>
        (_, _, _) => Task.FromResult(body);

    [Fact]
    public void CachePath_IsTheGivenFile()
    {
        Assert.Equal(_cachePath, new FilterListProvider(_cachePath).CachePath);
    }

    [Fact]
    public async Task LoadAsync_MissingCacheAndOffline_ReturnsEmpty()
    {
        // Сети нет и кэша нет: провайдер обязан вернуть пустой набор, а не
        // упасть — блокировка просто останется на встроенном списке.
        var provider = new FilterListProvider(_cachePath) { Downloader = Offline };

        Assert.Empty((await provider.LoadAsync()).Domains);
    }

    [Fact]
    public async Task LoadAsync_Offline_KeepsExistingCache()
    {
        WriteCache("||cached.example^");

        var provider = new FilterListProvider(_cachePath)
        {
            Downloader = Offline,
            MaxAge = TimeSpan.Zero,
        };

        Assert.Contains("cached.example", (await provider.LoadAsync()).Domains);
    }

    [Fact]
    public async Task LoadAsync_FreshCache_DoesNotDownload()
    {
        WriteCache("||fresh.example^");

        var called = false;
        var provider = new FilterListProvider(_cachePath)
        {
            Downloader = (_, _, _) =>
            {
                called = true;
                return Task.FromResult("||downloaded.example^");
            },
            MaxAge = TimeSpan.FromDays(1),
        };

        var set = await provider.LoadAsync();

        Assert.False(called);
        Assert.Contains("fresh.example", set.Domains);
    }

    [Fact]
    public async Task LoadAsync_StaleCache_DownloadsAndReplaces()
    {
        WriteCache("||stale.example^");
        // Кэш старше суток — файл надо обновить.
        File.SetLastWriteTimeUtc(_cachePath, DateTime.UtcNow.AddDays(-3));

        var provider = new FilterListProvider(_cachePath)
        {
            Downloader = Returning("||fresh.example^\n! comment\n@@||ok.example^"),
            MaxAge = TimeSpan.FromDays(1),
        };

        var set = await provider.LoadAsync();

        Assert.Contains("fresh.example", set.Domains);
        Assert.DoesNotContain("stale.example", set.Domains);
        Assert.Contains("ok.example", set.Exceptions);
        // Новое содержимое обязано лечь на диск для следующего запуска.
        Assert.Contains("fresh.example", File.ReadAllText(_cachePath));
    }

    [Fact]
    public async Task LoadAsync_SuccessfulDownload_IsReusedOffline()
    {
        var provider = new FilterListProvider(_cachePath)
        {
            Downloader = Returning("||first.example^"),
            MaxAge = TimeSpan.Zero,
        };

        await provider.LoadAsync();
        Assert.True(File.Exists(_cachePath));

        // Следующий запуск при отсутствии сети обязан взять кэш.
        var offline = new FilterListProvider(_cachePath)
        {
            Downloader = Offline,
            MaxAge = TimeSpan.FromDays(1),
        };

        Assert.Contains("first.example", (await offline.LoadAsync()).Domains);
    }

    [Fact]
    public async Task LoadAsync_EmptyDownload_KeepsCacheInsteadOfWipingIt()
    {
        // Пустой ответ — обрыв связи или заглушка. Стирать им рабочий кэш
        // нельзя: блокировка молча пропала бы до следующего обновления.
        WriteCache("||kept.example^");

        var provider = new FilterListProvider(_cachePath)
        {
            Downloader = Returning(string.Empty),
            MaxAge = TimeSpan.Zero,
        };

        Assert.Contains("kept.example", (await provider.LoadAsync()).Domains);
    }

    [Fact]
    public async Task LoadAsync_CorruptCacheOffline_ReturnsEmptyNotThrow()
    {
        WriteCache(new string('\0', 4096));

        var provider = new FilterListProvider(_cachePath)
        {
            Downloader = Offline,
            MaxAge = TimeSpan.FromDays(1),
        };

        Assert.Empty((await provider.LoadAsync()).Domains);
    }

    private void WriteCache(string content)
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(_cachePath, content);
    }
}