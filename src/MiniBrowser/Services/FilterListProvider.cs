using System.IO;
using System.Net.Http;

namespace MiniBrowser.Services;

/// <summary>
/// Загрузка и хранение внешнего списка фильтров (EasyList).
///
/// Список большой (около 2 МБ), поэтому он не едет в приложении, а лежит
/// в кэше и обновляется раз в сутки. Пока файла нет, блокировку держит
/// встроенный список — она есть с первой секунды и просто усиливается.
/// </summary>
public sealed class FilterListProvider
{
    public const string DefaultUrl = "https://easylist.to/easylist/easylist.txt";

    private static readonly TimeSpan DefaultMaxAge = TimeSpan.FromDays(1);

    /// <summary>Загрузчик: URL и путь к файлу кэша. Подменяется в тестах.</summary>
    public Func<string, string, CancellationToken, Task<string>> Downloader { get; set; } = DownloadAsync;

    /// <summary>Возраст кэша, после которого файл считается устаревшим.</summary>
    public TimeSpan MaxAge { get; set; } = DefaultMaxAge;

    public FilterListProvider(string? cachePath = null)
    {
        CachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiniBrowser", "easylist.txt");
    }

    public string CachePath { get; }

    public async Task<EasyListRuleSet> LoadAsync(CancellationToken cancellationToken = default)
    {
        // Сначала пробуем сеть: если кэш протух, свежий список лучше.
        if (IsCacheStale())
        {
            var fresh = await TryDownloadAsync(cancellationToken);
            // Пустой ответ не считаем обновлением: иначе обрыв связи стёр бы
            // рабочий кэш и блокировка пропала бы до следующего обновления.
            if (!string.IsNullOrWhiteSpace(fresh))
            {
                TryWriteCache(fresh);
                return EasyListParser.Parse(fresh);
            }
        }

        return EasyListParser.Parse(ReadCache());
    }

    private bool IsCacheStale()
    {
        try
        {
            if (!File.Exists(CachePath)) return true;
            return DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) > MaxAge;
        }
        catch
        {
            return true;
        }
    }

    /// <summary>Сеть недоступна или ответила мусором — это не ошибка браузера.</summary>
    private async Task<string?> TryDownloadAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await Downloader(DefaultUrl, CachePath, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private void TryWriteCache(string content)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, content);
        }
        catch { /* кэш — украшение; без него работаем на встроенном списке */ }
    }

    private string? ReadCache()
    {
        try
        {
            return File.Exists(CachePath) ? File.ReadAllText(CachePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string> DownloadAsync(string url, string _, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // Некоторые зеркала EasyList отдают 403 без User-Agent.
        client.DefaultRequestHeaders.Add(
            "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) MiniBrowser/1.0");
        return await client.GetStringAsync(url, cancellationToken);
    }
}