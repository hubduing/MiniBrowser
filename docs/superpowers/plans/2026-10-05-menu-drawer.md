# Menu Drawer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Заменить выпадающее меню на боковую панель (drawer) с разделами «Все закладки», «Вся история» и «Настройки», где списки полные, с поиском и удалением, а настройки (масштаб, размер окна, шрифт, поисковая система, домашняя страница, статусная строка) сохраняются между запусками.

**Architecture:** Drawer — обычная `Grid` в дереве `MainWindow` поверх `ContentHost`, а не `Popup` (WebView2 ломает air-space у `Popup`). Панель — `MenuDrawerView` с `MenuDrawerViewModel`; поиск реализован через штатный `ICollectionView.Filter`. Настройки — POCO `AppSettings` в JSON через `SettingsService`; масштаб доходит до движков через делегат `Func<double> zoomProvider`, чтобы `BrowserTabView` не зависел от хранилища настроек.

**Tech Stack:** .NET 8, WPF, WebView2 (`Microsoft.Web.WebView2` 1.0.2592.51), `Microsoft.Data.Sqlite` 8.0.10, `System.Text.Json`, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-05-menu-drawer-design.md`

## Global Constraints

- Тема тёмная и не переключается. Все новые стили используют существующие кисти из `App.xaml`: `B.Window`, `B.Toolbar`, `B.TabStrip`, `B.Content`, `B.Status`, `B.Border`, `B.Hover`, `B.Pressed`, `B.TabIdle`, `B.Address`, `T.Primary`, `T.Muted`, `T.Accent`. Новых кистей не вводить.
- Русский текст интерфейса. Комментарии в коде — по-русски, в стиле существующих (объясняют «почему», не «что»).
- Сбой хранилища или настроек не должен ронять браузер: ломается функциональность, а не приложение. Это уже принятая в проекте линия — следовать ей.
- Пути данных: `%LOCALAPPDATA%\MiniBrowser\browser.db` и `%LOCALAPPDATA%\MiniBrowser\settings.json`. Обе папки создаются при необходимости.
- `browser.db` не меняет структуру таблиц `history` и `bookmarks`.
- История физически подрезается до 1000 записей каждые 50 записей — существующее поведение сохраняется.
- Диапазоны: `ZoomPercent` 50–200, `DefaultFontSize` 13–20.
- Итоговый множитель масштаба: `ZoomPercent / 100 * DefaultFontSize / 16`.

## Review Focus

Пять классов ввода, которые спека подразумевает, но тестами не закрывает:

1. **`settings.json` отсутствует, пуст или содержит мусор** — приложение стартует с настройками по умолчанию, а не падает.
2. **Значение настройки вне диапазона или `NaN`/`Infinity`** (`ZoomPercent = 500`, `WindowWidth = -100`) — приводится к допустимому, окно остаётся видимым и кликабельным.
3. **БД недоступна** (`StorageService.IsAvailable == false`) — панель открывается, списки пустые, поиск не бросает исключений, разделы настроек работают.
4. **Поисковый запрос содержит спецсимволы SQL** (`%`, `_`, `'`, `100%`) — ищется буквальная подстрока, исключения нет. Это главный риск `LIKE`-запросов.
5. **Удаление последней записи при открытой панели и работа с масштабом после сна вкладки** — список пустеет без артефактов, удалённая закладка исчезает сразу, масштаб восстанавливается при пробуждении усыпленной вкладки.

---

### Task 1: Тестовый проект и `SettingsService`

**Files:**
- Create: `tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
- Create: `tests/MiniBrowser.Tests/SettingsServiceTests.cs`
- Create: `src/MiniBrowser/Models/AppSettings.cs`
- Create: `src/MiniBrowser/Services/SettingsService.cs`
- Modify: `MiniBrowser.sln`

**Interfaces:**
- Consumes: ничего (первая задача).
- Produces:
  - `Models/AppSettings.cs` — класс со свойствами и значениями по умолчанию из спеки.
  - `Services/SettingsService.cs`:
    - `public Settings Current { get; }` — текущие настройки, никогда не `null`.
    - `public SettingsService(string? filePath = null)` — путь по умолчанию `%LOCALAPPDATA%\MiniBrowser\settings.json`.
    - `public void Save()` — сериализует `Current` в файл.
    - `public static void ResetToDefaults(AppSettings settings)` — сбрасывает переданный объект к значениям по умолчанию.
    - `public double EffectiveZoom { get; }` — `ZoomPercent / 100 * DefaultFontSize / 16`.
- Global constraints: диапазоны `ZoomPercent` 50–200, `DefaultFontSize` 13–20; клампинг применяется и при загрузке, и перед `Save()`.

- [ ] **Step 1: Создать тестовый проект**

`tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MiniBrowser\MiniBrowser.csproj" />
  </ItemGroup>
</Project>
```
`UseWPF` обязателен: проект `MiniBrowser` — `WinExe` с WPF, и без него тестовый проект не поднимет типы разметки.

- [ ] **Step 2: Добавить проект в решение**

Run: `dotnet sln MiniBrowser.sln add tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: сообщение о добавлении, в `.sln` появляется второй `Project(...)` блок и четыре строки конфигурации с новым GUID.

- [ ] **Step 3: Написать падающие тесты**

`tests/MiniBrowser.Tests/SettingsServiceTests.cs`. Каждый тест работает на своём временном пути в `Path.GetTempPath()` и удаляет файл в финале:

```csharp
using MiniBrowser.Models;
using MiniBrowser.Services;

public class SettingsServiceTests : IDisposable
{
    private readonly string _path = Path.Combine(
        Path.GetTempPath(), "mb-tests", Guid.NewGuid().ToString("N"), "settings.json");

    public void Dispose()
    {
        var dir = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var service = new SettingsService(_path);
        Assert.Equal(100, service.Current.ZoomPercent);
        Assert.Equal("https://www.google.com/", service.Current.HomeUrl);
    }

    [Fact]
    public void Load_CorruptJson_FallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ this is not json");
        var service = new SettingsService(_path);
        Assert.Equal(100, service.Current.ZoomPercent);
    }

    [Fact]
    public void Load_EmptyFile_FallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "");
        var service = new SettingsService(_path);
        Assert.Equal(1200, service.Current.WindowWidth);
    }

    [Fact]
    public void SaveAndLoad_RoundTrips_AllValues()
    {
        var first = new SettingsService(_path);
        first.Current.ZoomPercent = 175;
        first.Current.WindowWidth = 1600;
        first.Current.WindowMaximized = true;
        first.Current.ShowStatusBar = false;
        first.Current.DefaultFontSize = 20;
        first.Current.HomeUrl = "https://example.org/";
        first.Save();

        var second = new SettingsService(_path);
        Assert.Equal(175, second.Current.ZoomPercent);
        Assert.Equal(1600, second.Current.WindowWidth);
        Assert.True(second.Current.WindowMaximized);
        Assert.False(second.Current.ShowStatusBar);
        Assert.Equal(20, second.Current.DefaultFontSize);
        Assert.Equal("https://example.org/", second.Current.HomeUrl);
    }

    [Theory]
    [InlineData(500, 200)]
    [InlineData(0, 50)]
    [InlineData(100, 100)]
    public void Load_OutOfRangeZoom_ClampsToBounds(double written, double expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, $"{{ \"ZoomPercent\": {written} }}");
        var service = new SettingsService(_path);
        Assert.Equal(expected, service.Current.ZoomPercent);
    }

    [Theory]
    [InlineData(-100, 640)]
    [InlineData(10000, 10000)]
    public void Load_OutOfRangeWindowSize_StaysClickable(double written, double expected)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, $"{{ \"WindowWidth\": {written} }}");
        var service = new SettingsService(_path);
        Assert.Equal(expected, service.Current.WindowWidth);
    }

    [Fact]
    public void Load_NotANumberZoom_FallsBackToDefault()
    {
        // System.Text.Json кинет исключение на нечисловом поле — это тот же путь,
        // что битый файл, и результат должен быть тем же.
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "{ \"ZoomPercent\": \"не число\" }");
        var service = new SettingsService(_path);
        Assert.Equal(100, service.Current.ZoomPercent);
    }

    [Fact]
    public void EffectiveZoom_CombinesZoomAndFontSize()
    {
        var service = new SettingsService(_path);
        service.Current.ZoomPercent = 200;
        service.Current.DefaultFontSize = 20;
        Assert.Equal(2.5, service.EffectiveZoom, precision: 5);
    }

    [Fact]
    public void EffectiveZoom_DefaultsToOne()
    {
        Assert.Equal(1.0, new SettingsService(_path).EffectiveZoom, precision: 5);
    }

    [Fact]
    public void ResetToDefaults_RestoresEveryValue()
    {
        var settings = new AppSettings { ZoomPercent = 50, WindowWidth = 1024, HomeUrl = "https://x/" };
        SettingsService.ResetToDefaults(settings);
        Assert.Equal(100, settings.ZoomPercent);
        Assert.Equal(1200, settings.WindowWidth);
        Assert.Equal("https://www.google.com/", settings.HomeUrl);
    }
}
```

- [ ] **Step 4: Запустить тесты и убедиться, что падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: ошибка компиляции — `SettingsService` и `AppSettings` не определены.

- [ ] **Step 5: Создать `AppSettings`**

`src/MiniBrowser/Models/AppSettings.cs` — POCO со свойствами и значениями по умолчанию ровно из спеки: `ZoomPercent` = 100 (50..200), `WindowWidth` = 1200, `WindowHeight` = 800, `WindowMaximized` = false, `HomeUrl` = `"https://www.google.com/"`, `SearchUrl` = `"https://www.google.com/search?q={0}"`, `ShowStatusBar` = true, `DefaultFontSize` = 16 (13..20). Все свойства `{ get; set; }`, класс `sealed`.

- [ ] **Step 6: Создать `SettingsService`**

`src/MiniBrowser/Services/SettingsService.cs`:
- Конструктор `SettingsService(string? filePath = null)` сохраняет путь (по умолчанию `%LOCALAPPDATA%\MiniBrowser\settings.json`) и вызывает `Load()`.
- `Load()` под `try/catch`: `Directory.CreateDirectory`, `File.ReadAllText`, `JsonSerializer.Deserialize<AppSettings>`. Любое исключение — включая `JsonException` на нечисловом поле — даёт новый `AppSettings()`. После успешного разбора вызвать `Validate(_current)`.
- `Validate(AppSettings)`: `ZoomPercent` клампится в 50..200, `DefaultFontSize` в 13..20, `WindowWidth` в 640..10000, `WindowHeight` в 400..10000. Значения `double.NaN` и `double.IsInfinity` заменяются на соответствующее значение по умолчанию до клампинга, иначе `Math.Clamp` вернёт `NaN`. Пустые `HomeUrl` и `SearchUrl` заменяются на дефолтные.
- `Save()` под `try/catch`: перед записью вызвать `Validate(_current)`, затем `Directory.CreateDirectory` и `File.WriteAllText(path, JsonSerializer.Serialize(_current, _jsonOptions))`. `JsonSerializerOptions` с `WriteIndented = true` и `PropertyNameCaseInsensitive = true`.
- `public static void ResetToDefaults(AppSettings settings)` — присваивает каждому свойству значение по умолчанию.
- `public double EffectiveZoom` — `Current.ZoomPercent / 100 * Current.DefaultFontSize / 16`.

Неизвестные свойства в JSON и отсутствующие поля не должны ломать разбор — это следствие `PropertyNameCaseInsensitive` и значений по умолчанию у свойств.

- [ ] **Step 7: Запустить тесты и убедиться, что проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: все 11 тестов PASS.

- [ ] **Step 8: Закоммитить**

```bash
git add tests/MiniBrowser.Tests src/MiniBrowser/Models/AppSettings.cs src/MiniBrowser/Services/SettingsService.cs MiniBrowser.sln
git commit -m "Add settings service with JSON persistence and clamping"
```

---

### Task 2: `StorageService` — полные списки, поиск, удаление

**Files:**
- Create: `tests/MiniBrowser.Tests/StorageServiceTests.cs`
- Modify: `src/MiniBrowser/Models/HistoryEntry.cs`
- Modify: `src/MiniBrowser/Services/StorageService.cs`

**Interfaces:**
- Consumes: ничего из других задач.
- Produces:
  - `HistoryEntry` получает инициализируемые свойства `public int VisitCount { get; init; } = 1;` и `public string LastVisit { get; init; } = "";`. Существующий позиционный конструктор `HistoryEntry(string Url, string Title, string VisitedAt)` не меняется.
  - `StorageService`:
    - Конструктор `public StorageService(string? databasePath = null)` — путь по умолчанию `%LOCALAPPDATA%\MiniBrowser\browser.db`.
    - `public List<Bookmark> GetAllBookmarks()` — все закладки, `ORDER BY added_at DESC`.
    - `public List<HistoryEntry> GetHistory(int limit = 200)` — история с дедупом по URL: счётчик визитов и дата последнего.
    - `public List<HistoryEntry> SearchHistory(string query, int limit = 200)` — дедуп по URL плюс фильтр по `url` и `title`.
    - `public void DeleteBookmark(string url)`
    - `public void ClearHistory()`
- Global constraints: `SearchHistory` экранирует спецсимволы `LIKE`, поэтому запрос `"100%"` ищется буквально. Пустой или null `query` в `SearchHistory` равносилен отсутствию фильтра.

- [ ] **Step 1: Написать падающие тесты**

`tests/MiniBrowser.Tests/StorageServiceTests.cs`. Каждый тест — своя временная БД, конструктор принимает путь:

```csharp
using MiniBrowser.Services;

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
    public void ClearHistory_RemovesEverything_ButKeepsBookmarks()
    {
        _storage.AddHistory("https://a.example/", "A");
        _storage.AddBookmark("https://a.example/", "A");
        _storage.ClearHistory();
        Assert.Empty(_storage.GetHistory());
        Assert.Single(_storage.GetAllBookmarks());
    }

    [Fact]
    public void GetRecentHistory_FillsVisitCountAndLastVisit()
    {
        _storage.AddHistory("https://a.example/", "A");
        var entry = Assert.Single(_storage.GetRecentHistory());
        Assert.Equal(1, entry.VisitCount);
        Assert.NotEmpty(entry.LastVisit);
    }

    [Fact]
    public void UnavailableDatabase_MethodsReturnEmptyWithoutThrowing()
    {
        // Путь внутри существующего файла: SQLite не сможет открыть БД,
        // и StorageService обязан деградировать, а не упасть.
        var blocker = Path.Combine(Path.GetTempPath(), "mb-blocker.db");
        File.WriteAllText(blocker, "not a database");

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

        File.Delete(blocker);
    }
}
```

Этот тест закрывает третий пункт Review Focus: панель над недоступной БД должна открываться с пустыми списками.

`Thread.Sleep(1100)` в тесте сортировки — осознанная цена: `visited_at` пишется с точностью до секунды, иначе две записи в одну секунду неразличимы.

- [ ] **Step 2: Запустить тесты и убедиться, что падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj --filter "FullyQualifiedName~StorageServiceTests"`
Expected: ошибка компиляции — конструктор `StorageService` с аргументом и новые методы не существуют.

- [ ] **Step 3: Расширить `HistoryEntry`**

`src/MiniBrowser/Models/HistoryEntry.cs` — добавить в тело record инициализируемые свойства с дефолтами, не трогая позиционный конструктор:
```csharp
/// <summary>Запись истории посещений.</summary>
/// <param name="VisitedAt">Момент посещения строки (SQLite localtime, до секунды).</param>
public sealed record HistoryEntry(string Url, string Title, string VisitedAt)
{
    /// <summary>Сколько раз URL был посещён. 1 — одна строка в истории.</summary>
    public int VisitCount { get; init; } = 1;

    /// <summary>Дата последнего посещения — для показа в панели.</summary>
    public string LastVisit { get; init; } = "";
}
```

- [ ] **Step 4: Расширить конструктор `StorageService`**

`src/MiniBrowser/Services/StorageService.cs` — заменить `public StorageService()` на `public StorageService(string? databasePath = null)`. Внутри вычислить `databasePath ?? Path.Combine(localAppData, "MiniBrowser", "browser.db")` и использовать его вместо прежнего `Path.Combine(dir, "browser.db")`. Создание папки остаётся. Тесты передают путь в несуществующей папке — её создание уже покрыто существующим `Directory.CreateDirectory`.

- [ ] **Step 5: Добавить `GetAllBookmarks`, `DeleteBookmark`, `ClearHistory`**

В `StorageService`, перед `GetBookmarks(int limit = 12)`:
- `GetAllBookmarks()` — тот же запрос, что у `GetBookmarks`, но без `LIMIT`: `SELECT url, title FROM bookmarks ORDER BY added_at DESC;`. Читать результат существующим `while (reader.Read())` с обёрткой в `try/catch`.
- `DeleteBookmark(string url)` — `DELETE FROM bookmarks WHERE url = $u;` с параметром, под `try/catch`.
- `ClearHistory()` — `DELETE FROM history;` под `try/catch`.

Существующий `GetBookmarks(int limit = 12)` не трогать: после задачи 8 старое меню будет удалено, но метод останется как публичный API.

- [ ] **Step 6: Добавить `GetHistory` и `SearchHistory` с дедупом по URL**

Оба метода строят общий базовый запрос и отличаются только наличием условия `WHERE`. Текст запроса для дедупа:
```sql
SELECT url,
       MAX(visited_at)      AS last_visit,
       COUNT(*)             AS visit_count,
       (SELECT h2.title FROM history h2
         WHERE h2.url = history.url
         ORDER BY h2.id DESC LIMIT 1) AS title
FROM history
WHERE (url LIKE $q ESCAPE '\' OR title LIKE $q ESCAPE '\')
GROUP BY url
ORDER BY last_visit DESC
LIMIT $limit;
```
Ключевые решения, которые шаг фиксирует:
- `GROUP BY url` даёт дедуп, `COUNT(*)` — счётчик визитов, `MAX(visited_at)` — дату последнего.
- Заголовок берётся подзапросом из самой свежей строки URL: `MAX(title)` был бы неверным, потому что алфавитный максимум не связан с последним посещением.
- `ESCAPE '\'` вместе с экранированием в C#: `%`, `_` и `\` заменяются на `\%`, `\_`, `\\` через `Replace`, иначе пользовательский запрос `"100%"` превратился бы в LIKE-маску. Это единственный способ сделать экранирование переносимым — SQLite не поддерживает `ESCAPE` без управляющего символа, а `'\'` им и является.
- `LIMIT $limit` передаётся параметром, а не склейкой — существующий `Clamp(int)` даёт 1..100, здесь нужен диапазон до 500.
- `reader.GetInt32(2)` для счётчика, `reader.GetString(1)` для даты.

Собирать результат так, чтобы оба новых метода возвращали `new HistoryEntry(url, title, lastVisit) { VisitCount = count, LastVisit = lastVisit }`.

- [ ] **Step 7: Обновить `GetRecentHistory` под новую модель**

Существующий `GetRecentHistory` остаётся для старого меню и теста `GetRecentHistory_FillsVisitCountAndLastVisit`. В нём `LastVisit` должен получать значение `visited_at` прочитанной строки, иначе тест упадёт на `Assert.NotEmpty`. Дефолт `VisitCount = 1` в модели закрывает вторую половину.

- [ ] **Step 8: Запустить тесты и убедиться, что проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: все тесты обоих классов PASS.

- [ ] **Step 9: Закоммитить**

```bash
git add tests/MiniBrowser.Tests/StorageServiceTests.cs src/MiniBrowser/Models/HistoryEntry.cs src/MiniBrowser/Services/StorageService.cs
git commit -m "Extend StorageService with full lists, search and deletion"
```

---

### Task 3: Поисковая система из настроек

**Files:**
- Modify: `src/MiniBrowser/Services/NavigationService.cs`
- Create: `tests/MiniBrowser.Tests/NavigationServiceTests.cs`

**Interfaces:**
- Consumes: `Models.AppSettings` из задачи 1.
- Produces:
  - `NavigationService.SearchEngine(string name)` → строка шаблона `{0}`. Принимает `"Google"`, `"Bing"`, `"DuckDuckGo"`, `"Яндекс"`; неизвестное имя даёт Google.
  - `NavigationService.EngineNames` → `string[]` из этих четырёх имён в порядке отображения в ComboBox.
  - Существующая `public static string? BuildUrl(string input)` меняет сигнатуру на `BuildUrl(string input, string searchTemplate)`. Значение `searchTemplate` — строка из `AppSettings.SearchUrl`, может содержать `{0}`.
- Global constraints: константа `SearchUrlTemplate` остаётся и используется как дефолт в `EngineNames`-зависимом коде и в `AppSettings` по умолчанию.

- [ ] **Step 1: Написать падающие тесты**

`tests/MiniBrowser.Tests/NavigationServiceTests.cs`:
```csharp
using MiniBrowser.Services;

public class NavigationServiceTests
{
    [Fact]
    public void BuildUrl_EmptyInput_ReturnsNull()
    {
        Assert.Null(NavigationService.BuildUrl("   ", "{0}"));
    }

    [Fact]
    public void BuildUrl_ExplicitScheme_KeepsInput()
    {
        Assert.Equal("http://a.example/x",
            NavigationService.BuildUrl("http://a.example/x", "{0}"));
    }

    [Fact]
    public void BuildUrl_LooksLikeDomain_PrefersHttps()
    {
        Assert.Equal("https://example.com",
            NavigationService.BuildUrl("example.com", "{0}"));
    }

    [Theory]
    [InlineData("Google", "https://www.google.com/search?q=")]
    [InlineData("Bing", "https://www.bing.com/search?q=")]
    [InlineData("DuckDuckGo", "https://duckduckgo.com/?q=")]
    [InlineData("Яндекс", "https://yandex.ru/search/?text=")]
    public void BuildUrl_Query_UsesGivenEngine(string engine, string expectedPrefix)
    {
        Assert.StartsWith(expectedPrefix, NavigationService.BuildUrl("коты", NavigationService.SearchEngine(engine)));
    }

    [Fact]
    public void SearchEngine_UnknownName_FallsBackToGoogle()
    {
        Assert.Equal(NavigationService.SearchEngine("Google"), NavigationService.SearchEngine("Что-то"));
    }

    [Fact]
    public void EngineNames_ContainsFourEnginesInUiOrder()
    {
        Assert.Equal(new[] { "Google", "Bing", "DuckDuckGo", "Яндекс" },
            NavigationService.EngineNames);
    }

    [Fact]
    public void BuildUrl_QueryWithoutPlaceholder_StillEscapesInput()
    {
        // Пользователь мог дописать в поле настроек шаблон без {0} — не падаем,
        // а подставляем запрос в конец.
        var result = NavigationService.BuildUrl("a b", "https://s.example/?");
        Assert.Equal("https://s.example/?a%20b", result);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj --filter "FullyQualifiedName~NavigationServiceTests"`
Expected: ошибка компиляции — `BuildUrl` с двумя аргументами не существует.

- [ ] **Step 3: Переписать `NavigationService`**

`src/MiniBrowser/Services/NavigationService.cs`:
- Оставить `SearchUrlTemplate = "https://www.google.com/search?q={0}"`.
- `EngineNames` — `new[] { "Google", "Bing", "DuckDuckGo", "Яндекс" }`.
- `SearchEngine(string name)` — словарь `name` → шаблон, ключи из `EngineNames`; при отсутствии ключа вернуть `SearchUrlTemplate`.
- `BuildUrl(string input, string searchTemplate)` — новая сигнатура. Логика определения URL/запроса остаётся как была (схема, `LooksLikeUrl`, иначе поиск).
- Формирование запроса: если `searchTemplate` содержит `{0}`, вернуть `string.Format(searchTemplate, Uri.EscapeDataString(input))`. Иначе вернуть `searchTemplate + Uri.EscapeDataString(input)` — покрывает тест `BuildUrl_QueryWithoutPlaceholder`.
- Пустой или null `searchTemplate` трактовать как `SearchUrlTemplate`, чтобы настройка с пустым полем не ломала адресную строку.

- [ ] **Step 4: Запустить и убедиться, что проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: все тесты PASS.

- [ ] **Step 5: Закоммитить**

```bash
git add src/MiniBrowser/Services/NavigationService.cs tests/MiniBrowser.Tests/NavigationServiceTests.cs
git commit -m "Make search engine configurable through settings"
```

---

### Task 4: Масштаб доходит до движков

**Files:**
- Modify: `src/MiniBrowser/Views/BrowserTabView.xaml.cs`
- Modify: `src/MiniBrowser/Services/TabManager.cs`

**Interfaces:**
- Consumes: `System.Func<double>` — источник текущего множителя масштаба.
- Produces:
  - `BrowserTabView(Tab tab, IBrowserActions actions, Func<double> zoomProvider)` — конструктор с делегатом. Старый двухаргументный конструктор удаляется.
  - `public void ApplyZoom(double zoom)` — применяет множитель к `CoreWebView2`, если движок создан; иначе запоминает для применения при создании.
  - `public void ZoomChanged()` — повторно читает `zoomProvider()` и применяет. Вызывается хостом, когда настройка изменилась.
- Global constraints: множитель — `SettingsService.EffectiveZoom`, то есть `ZoomPercent / 100 * DefaultFontSize / 16`. `BrowserTabView` не ссылается на `SettingsService`.

- [ ] **Step 1: Проверить текущее поведение компиляцией**

Run: `dotnet build MiniBrowser.sln`
Expected: сборка проходит до шага 3 — это подтверждает, что правка не нужна для компиляции и её правомерность проверяется шагом 4. Если сборка уже падает, остановиться и починить, иначе баг неистребим.

- [ ] **Step 2: Проверить отсутствие покрытия тестами — ручной замер**

Run: `dotnet run --project src/MiniBrowser -- https://example.com`
Expected: страница открывается в масштабе 100%. Это контрольная точка «до»; после шага 4 тот же запуск с изменённой настройкой должен дать другой масштаб.

- [ ] **Step 3: Добавить масштаб в `BrowserTabView`**

`src/MiniBrowser/Views/BrowserTabView.xaml.cs`:
- Поле `private readonly Func<double> _zoomProvider;` и `private double? _pendingZoom;`.
- Конструктор принимает `Func<double> zoomProvider` и сохраняет его; `zoomProvider` не должен быть `null` (MainWindow передаёт лямбду, всегда возвращаующую значение).
- `public void ApplyZoom(double zoom)`: если `_web?.CoreWebView2` не `null` — присвоить `ZoomFactor = zoom`. Иначе сохранить `_pendingZoom = zoom`, чтобы движок получил его при создании.
- `public void ZoomChanged() => ApplyZoom(_zoomProvider());`
- В `EnsureWebViewAsync`, сразу после успешного `EnsureCoreWebView2Async` и до `WireEvents()`, применить масштат: `ApplyZoom(_pendingZoom ?? _zoomProvider());` — новая вкладка обязана открыться в текущем масштабе, а не в 100%.
- В `Sleep()` после `_web = null` очистить `_pendingZoom`, чтобы уснувшая вкладка при пробуждении взяла свежий множитель, а не устаревший. Действующий `ZoomChanged` при этом сбрасывает `_pendingZoom` естественным путём, так как движка нет и значение записывается в поле.

- [ ] **Step 4: Прокинуть делегат через `TabManager`**

`src/MiniBrowser/Services/TabManager.cs`:
- Поле `private readonly Func<double> _zoomProvider;`.
- Конструктор `TabManager(Panel contentHost, StorageService storage, IBrowserActions actions, Func<double> zoomProvider)` — `MainWindow` передаёт `() => _settings.EffectiveZoom`.
- В `NewTab`: `new BrowserTabView(tab, _actions, _zoomProvider)`.
- Добавить `public void ApplyZoomToAllTabs()` — вызывает `ZoomChanged()` у всех значений `_views`. Это точка для вызова из `MainWindow` при изменении настройки масштаба.

- [ ] **Step 5: Обновить вызов конструктора в `MainWindow`**

`src/MiniBrowser/MainWindow.xaml.cs`: поле `_settings` объявить до конструктора (`private readonly SettingsService _settings = new();`), и передать делегат в `new TabManager(ContentHost, _storage, this, () => _settings.EffectiveZoom)`. Инициализатор поля выполняется раньше тела конструктора, поэтому порядок безопасен. Это единственное место, где `_settings` нужен уже сейчас; полное применение настроек — задача 8.

- [ ] **Step 6: Собрать и проверить**

Run: `dotnet build MiniBrowser.sln`
Expected: BUILD SUCCEEDED, без предупреждений о неиспользованных параметрах.

- [ ] **Step 7: Закоммитить**

```bash
git add src/MiniBrowser/Views/BrowserTabView.xaml.cs src/MiniBrowser/Services/TabManager.cs src/MiniBrowser/MainWindow.xaml.cs
git commit -m "Apply page zoom to every WebView2 engine via provider delegate"
```

---

### Task 5: MVVM-основа и `MenuDrawerViewModel`

**Files:**
- Create: `src/MiniBrowser/ViewModels/RelayCommand.cs`
- Create: `src/MiniBrowser/ViewModels/MenuItemBase.cs`
- Create: `src/MiniBrowser/ViewModels/MenuDrawerViewModel.cs`

**Interfaces:**
- Consumes: `StorageService` (задача 2) — методы `GetAllBookmarks()`, `GetHistory()`, `SearchHistory(string, int)`, `DeleteBookmark(string)`, `ClearHistory()`.
- Produces:
  - `ViewModels.RelayCommand` — `public RelayCommand(Action execute)`, `public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)`, `public void RaiseCanExecuteChanged()`.
  - `ViewModels.MenuItemBase : INotifyPropertyChanged` — абстрактный, `public string Title { get; set; }`, `public string Url { get; set; }`, `public string Detail { get; set; }`, `public bool CanDelete { get; set; }`, `public event PropertyChangedEventHandler? PropertyChanged`, `protected void OnPropertyChanged([CallerMemberName] string? name = null)`, `protected virtual void OnDeleted()`.
  - `ViewModels.BookmarkItem : MenuItemBase` — конструктор `(string url, string title)`, `Detail` = дата добавления (пустая строка допустима).
  - `ViewModels.HistoryItem : MenuItemBase` — конструктор `(string url, string title, int visitCount, string lastVisit)`, `Detail` = `"3 визита · 2026-10-05 14:22"`, `VisitCount` — `public int VisitCount { get; }`.
  - `ViewModels.MenuDrawerViewModel` — конструктор `(StorageService storage, SettingsService settings)`; свойства и команды:
    - `public ObservableCollection<BookmarkItem> Bookmarks { get; }`
    - `public ObservableCollection<HistoryItem> History { get; }`
    - `public string SearchText { get; set; }` — сеттер перезапрашивает источник и обновляет оба списка
    - `public ICommand OpenUrlCommand { get; }`
    - `public ICommand DeleteItemCommand { get; }` — `Parameter` типа `MenuItemBase`
    - `public ICommand ClearHistoryCommand { get; }`
    - `public ICommand ResetSettingsCommand { get; }`
    - `public ICommand ClearSearchCommand { get; }`
    - `public event Action<string>? NavigateRequested;`
    - `public event Action? HistoryChanged;`
    - `public event Action? SettingsChanged;`
    - `public ICommand SetWindowSizeCommand { get; }` — `Parameter` вида `"1024,640"`, разбирается через `Split(',')`, присваивает `Settings.Current.WindowWidth/Height`, затем `Save()` и `SettingsChanged`.
    - `public string[] SearchEngines => NavigationService.EngineNames;` — источник для ComboBox поисковой системы.
    - `public void RefreshBookmarks()` и `public void RefreshHistory()` — публичные, чтобы хост обновлял списки при возврате к вкладке
    - `public string BookmarksCountText { get; }` и `public string HistoryCountText { get; }` — формы `"12 из 84"`, при пустом поиске — просто `"84"`
    - `public bool HasBookmarks { get; }` и `public bool HasHistory { get; }` — для конвертера видимости
- Global constraints: `SettingsChanged` поднимается **после** изменения `SettingsService.Current`, и хост в этот момент ещё не применил их — порядок важен для задачи 8. Классы только на `System.Windows.Input.ICommand` и `System.Collections.ObjectModel`, без ссылок на UI-типы.

- [ ] **Step 1: Создать `RelayCommand`**

`src/MiniBrowser/ViewModels/RelayCommand.cs` — реализация `ICommand` с полями `execute` и `canExecute`, свойствами `CanExecute(object?)` и `CanExecuteChanged`. `RaiseCanExecuteChanged()` вызывает `CanExecuteChanged?.Invoke(this, EventArgs.Empty)`. В реализации `CanExecuteChanged` создаётся лениво, иначе при отсутствии подписчиков событие не подписывается.

- [ ] **Step 2: Создать `MenuItemBase` и наследников**

`src/MiniBrowser/ViewModels/MenuItemBase.cs` содержит все три типа из блока Produces. Свойства — обычные автосвойства, уведомление через `OnPropertyChanged`. `OnDeleted()` виртуальный и по умолчанию ничего не делает: `BookmarkItem` и `HistoryItem` его не переопределяют, коллекция управляется из VM.

`HistoryItem.Detail`: множественное число по-русски — `"1 визит"`, `"2 визита"`, `"5 визитов"`, через `n % 100` в 11..14 → `"визитов"`. Дата `LastVisit` приходит из SQLite в формате `"yyyy-MM-dd HH:mm:ss"`.

- [ ] **Step 3: Создать `MenuDrawerViewModel`**

`src/MiniBrowser/ViewModels/MenuDrawerViewModel.cs`:
- Поля `_storage`, `_settings`, `_bookmarkView`, `_historyView` (`ICollectionView` поверх коллекций, создаётся через `CollectionViewSource.GetDefaultView` в конструкторе).
- `SearchText` — сеттер: сохраняет значение, вызывает `Refresh()` у обоих `ICollectionView`, затем пересчитывает `BookmarksCountText`, `HistoryCountText`, `HasBookmarks`, `HasHistory` и поднимает `PropertyChanged` для них.
- Фильтры как `Predicate<object>`: строка сравнивается без учёта регистра и по `Title`, и по `Url`, при пустом `SearchText` всегда `true`. Это штатный `ICollectionView.Filter` — отдельный механизм поиска не нужен.
- `RefreshBookmarks()` — читает `_storage.GetAllBookmarks()`, при непустом `SearchText` переключается на выборку из всех закладок с тем же предикатом. Важно: `StorageService` не имеет поиска по закладкам, поэтому здесь фильтрация полностью в VM. Коллекцию перезаполнять с `Clear()` + `Add()`, а не заменять свойство — иначе `ICollectionView` теряет источник.
- `RefreshHistory()` — при пустом `SearchText` зовёт `GetHistory()`, иначе `SearchHistory(SearchText)`. Из-за дедупа в `StorageService` коллекция уже уникальна по URL, повторная фильтрация VM не нужна.
- `OpenUrlCommand` — если `Parameter` строка, поднимает `NavigateRequested(url)`.
- `DeleteItemCommand` — если `Parameter` типа `BookmarkItem`, зовёт `_storage.DeleteBookmark(item.Url)`, и удаляет элемент из `Bookmarks` без полной перезагрузки; если `HistoryItem` — не поддерживается (`CanExecute` возвращает `false`), потому что удаление отдельной записи истории в спеку не входит: в истории хранятся посещения, а не закладки пользователя.
- `ClearHistoryCommand` — `_storage.ClearHistory()`, затем `History.Clear()` и поднимает `HistoryChanged`.
- `ResetSettingsCommand` — `SettingsService.ResetToDefaults(_settings.Current)`, `_settings.Save()`, поднимает `SettingsChanged`.
- `ClearSearchCommand` — присваивает `SearchText = ""`.
- `SetWindowSizeCommand` — `Parameter` вида `"1024,640"`, разбирается через `Split(',')`, присваивает `Settings.Current.WindowWidth` и `WindowHeight`, затем `_settings.Save()` и `SettingsChanged`. Значения разбираются в фиксированном порядке: сначала ширина, потом высота.
- `SearchEngines` — `NavigationService.EngineNames`, источник для ComboBox поисковой системы.

Команда пресетов размера объявлена здесь, а не в разметке Task 6: разметка только привязывается к ней.

- [ ] **Step 4: Собрать и убедиться, что компилируется**

Run: `dotnet build MiniBrowser.sln`
Expected: BUILD SUCCEEDED.

- [ ] **Step 5: Закоммитить**

```bash
git add src/MiniBrowser/ViewModels
git commit -m "Add drawer view model with search, deletion and count text"
```

---

### Task 6: Разметка drawer и стили

**Files:**
- Create: `src/MiniBrowser/Converters/CountToVisibilityConverter.cs`
- Create: `src/MiniBrowser/Views/MenuDrawerView.xaml`
- Create: `src/MiniBrowser/Views/MenuDrawerView.xaml.cs`
- Modify: `src/MiniBrowser/App.xaml`

**Interfaces:**
- Consumes: `MenuDrawerViewModel` из задачи 5; все её команды и свойства.
- Produces:
  - `Views.MenuDrawerView` — `public partial class MenuDrawerView : UserControl`, конструктор без аргументов, `InitializeComponent()`.
  - `Views.MenuDrawerView` события: `public event Action? CloseRequested;` (затемнение, ✕, Esc) и `public event Action<string>? OpenUrlRequested;` — последнее дублирует `NavigateRequested` VM, чтобы разметка не знала о VM.
  - `public void ShowAt(BookmarkItem?)` — нет. Метод открытия один: `public void SetOpen(bool open)` — ставит `IsDrawerOpen` и запускает анимацию `TranslateTransform`.
  - `public bool IsDrawerOpen { get; private set; }`
  - `public void SelectTab(int index)` — 0 закладки, 1 история, 2 настройки. Вызывается хостом из `ShowBookmarks`/`ShowHistory`, поэтому объявляется здесь, а не в Task 8.
  - `Converters.CountToVisibilityConverter` — `IValueConverter`, `Convert` возвращает `Visibility.Visible` для ненулевого `int` и `Collapsed` для нуля; обратное преобразование бросает `NotSupportedException`.
  - Стили в `App.xaml`: `DrawerListBox` (TargetType `ListBox`), `DrawerTabControl` (TargetType `TabControl`), `DrawerTabItem` (TargetType `TabItem`).
- Global constraints: только тёмная тема, новых кистей не вводить. Ширина drawer 360px, затемнение `#80000000`.

- [ ] **Step 1: Создать конвертер**

`src/MiniBrowser/Converters/CountToVisibilityConverter.cs` — стандартная реализация `IValueConverter`. `ConvertBack` бросает `NotSupportedException`, потому что обратное преобразование не используется.

- [ ] **Step 2: Добавить стили в `App.xaml`**

Три стиля в `Application.Resources`, перед существующим стилем `TargetType="MenuItem"`:
- `DrawerListBox` — фон прозрачный, рамки нет, `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`, `VerticalScrollBarVisibility="Auto"`.
- `DrawerTabControl` — та же рамка, что у существующего `ContextMenu`, шаблон без заголовка, `TabStripPlacement="Top"`, содержимое в `ScrollViewer`.
- `DrawerTabItem` — фон `B.TabIdle`, при `IsSelected` фон `B.TabIdle` и нижняя граница `T.Accent` толщиной 2, `Foreground="T.Muted"`, при выборе `T.Primary`.

Стиль `ListBoxItem` для строк списка — шаблон с `CornerRadius="5"` и триггером `IsMouseOver` на `B.Hover`, как у `MenuItem`. Без него строки не подсвечиваются и список выглядит мёртвым.

- [ ] **Step 3: Создать `MenuDrawerView.xaml`**

`src/MiniBrowser/Views/MenuDrawerView.xaml`. Корневой элемент — `Grid` шириной 360px:
```
Grid (Width=360)
 ├─ Border (затемнение, разворачивается на всю ширину окна, обрабатывает клик)
 └─ Border x:Name="Panel" (выровнен по Left, Margin 0, собственный фон B.Toolbar,
         правый край B.Border, RenderTransform → TranslateTransform x:Name="Slide")
```
`Slide` в начальном состоянии `X = -360`. Анимация — две сюжетные анимации `DoubleAnimation` на `Slide.X` в `SetOpen`: 180ms, `CubicEase` с `EasingMode="EaseOut"`. Открытие — в 0, закрытие — в −360. Затемнение меняет `Opacity` 0 → 1 тем же временем.

Содержимое `Panel`:
```
Grid (3 строки)
 ├─ строка 0: кнопка ✕ (FlatToolButton, выравнена вправо, Click → CloseRequested)
 ├─ строка 1: TabControl (3 вкладки)
 └─ строка 2: пусто
```

Вкладка «Закладки»:
```
DockPanel
 ├─ Dock=Top: Grid из двух столбцов
 │    ├─ TextBox (Style="AddressBox", Text="{Binding SearchText, UpdateSourceTrigger=PropertyChanged}")
 │    └─ Button ✕ (Style="FlatToolButton", Command="{Binding ClearSearchCommand}")
 ├─ Dock=Bottom: TextBlock (Text="{Binding BookmarksCountText}", стили T.Muted, 11px)
 └─ ListBox (Style="DrawerListBox", ItemSource={Binding Bookmarks})
      ItemTemplate: Grid из трёх строк
        ├─ TextBlock Text="{Binding Title}" (T.Primary, 13, обрезка хвостом)
        ├─ TextBlock Text="{Binding Url}" (T.Muted, 11, обрезка хвостом)
        ├─ Dock=Right: Button ✕ (Style="FlatToolButton", Command="{Binding DeleteItemCommand}"
        │            CommandParameter="{Binding}", ToolTip="Удалить закладку")
```

Вкладка «История» — та же структура плюс внизу (`Dock=Bottom`, выше счётчика) кнопка «Очистить историю» с `Command="{Binding ClearHistoryCommand}"`. Для строк истории используется `HistoryItem` с `Detail` в третьей строке рядом с URL.

Вкладка «Настройки» — `ScrollViewer` с вертикальным стеком, поля привязаны к `Settings.Current`:
- Масштаб: `Slider` 50–200, `IsSnapToTickEnabled="True"`, `TickFrequency="5"`, `Value="{Binding Settings.Current.ZoomPercent, Mode=TwoWay}"` + `TextBlock` с процентом через `StringFormat`.
- Размер шрифта: `ComboBox` с тремя фиксированными `ComboBoxItem` (13 / 16 / 20), выбор привязан к `Settings.Current.DefaultFontSize`. Значения хранятся как текст; привязка к `double` требует конвертера — проще привязать `SelectedValue` к строковому представлению и читать число при изменении. Реализовать через `ComboBoxItem Tag` и код-бэк-хендлер, вызывающий `ViewModel` — это единственное место, где разметке нужен код.
- Пресеты размера окна: три кнопки 1024×640 / 1200×800 / 1600×1000, `Command="{Binding SetWindowSizeCommand}"`, `CommandParameter` — `"1024,640"`, `"1200,800"`, `"1600,1000"`.
- «Запомнить размер»: `CheckBox` к `Settings.Current.WindowMaximized` — точнее, к отдельному полю `RememberSize`. В спеке за это отвечает `WindowMaximized`; завести отдельное свойство нельзя без правки спеки, поэтому `CheckBox` привязывается к нему же, а подпись читается «Запомнить размер окна». Это осознанное упрощение: перед закрытием окна `Window_Closing` пишет `Width`/`Height` только когда флаг установлен.
- Домашняя страница: `TextBox` к `Settings.Current.HomeUrl`.
- Поисковая система: `ComboBox` с `ItemsSource` из `NavigationService.EngineNames` — это требует свойства `EngineNames` во VM; добавить `public string[] SearchEngines => NavigationService.EngineNames;` и биндинг выбранного имени к `Settings.Current.SearchUrl` через конвертер либо через второй `ComboBoxItem`-подход с `Tag` и code-behind, как у шрифта. Выбрать code-behendler-подход для единообразия с полем шрифта.
- Статусная строка: `CheckBox` к `Settings.Current.ShowStatusBar`.
- Кнопка «Сбросить настройки» с `Command="{Binding ResetSettingsCommand}"`.

- [ ] **Step 4: Создать `MenuDrawerView.xaml.cs`**

`src/MiniBrowser/Views/MenuDrawerView.xaml.cs`:
- Конструктор вызывает `InitializeComponent()`.
- `IsDrawerOpen { get; private set; }` и `public void SetOpen(bool open)` — переключает поле, запускает анимацию `Slide.X` и анимацию `Opacity` затемнения. При `open == true` и `Panel` ещё не загружен — сначала `UpdateLayout()`.
- `CloseRequested` и `OpenUrlRequested` объявляются как `event Action?` и `event Action<string>?`.
- Обработчики `Panel_Close_Click` поднимают `CloseRequested`.
- Обработчики двух `SelectionChanged` для полей «Размер шрифта» и «Поисковая система» читают `Tag` выбранного `ComboBoxItem`, приводят к числу / строке, пишут в `Settings.Current` и поднимают у `ViewModel` событие `SettingsChanged`. Для этого код-бэкхендлер обращается к `DataContext` как к `MenuDrawerViewModel`.
- Клик по затемнению обрабатывается `MouseLeftButtonDown` на `Border` и поднимает `CloseRequested`.
- Для запуска анимации задать `Slide` как `RenderTransform` через `TranslateTransform` в разметке и обращаться к нему как `(TranslateTransform)Panel.RenderTransform`.

- [ ] **Step 5: Собрать**

Run: `dotnet build MiniBrowser.sln`
Expected: BUILD SUCCEEDED. Ошибки компиляции XAML здесь ожидаемы на первом проходе из-за несовпадения имён — исправить по сообщениям компилятора.

- [ ] **Step 6: Закоммитить**

```bash
git add src/MiniBrowser/Converters src/MiniBrowser/Views/MenuDrawerView.xaml src/MiniBrowser/Views/MenuDrawerView.xaml.cs src/MiniBrowser/App.xaml src/MiniBrowser/ViewModels/MenuDrawerViewModel.cs
git commit -m "Add drawer view with bookmarks, history and settings tabs"
```

---

### Task 7: Хоткеи меню

**Files:**
- Modify: `src/MiniBrowser/Services/Hotkeys.cs`
- Create: `tests/MiniBrowser.Tests/HotkeysTests.cs`

**Interfaces:**
- Consumes: `IBrowserActions` из `Hotkeys.cs` — существующий интерфейс.
- Produces:
  - `IBrowserActions` получает три новых члена: `void ToggleMenu();`, `void ShowHistory();`, `void ShowBookmarks();`, и `bool IsMenuOpen { get; }`.
  - `Hotkeys.TryHandle(Key, ModifierKeys, IBrowserActions)` обрабатывает: `Ctrl+M` → `ToggleMenu`, `Ctrl+H` → `ShowHistory`, `Ctrl+Shift+B` → `ShowBookmarks`, `Esc` без модификаторов при `actions.IsMenuOpen` → `ToggleMenu` и возвращает `true`.
- Global constraints: `Esc` перехватывается **только** когда `IsMenuOpen == true`. Иначе он обязан дойти до существующей логики выхода из F11 и HTML5-полноэкранного видео.

- [ ] **Step 1: Написать падающие тесты**

`tests/MiniBrowser.Tests/HotkeysTests.cs`. Нужен подставной `IBrowserActions`, фиксирующий вызовы:
```csharp
using System.Windows.Input;
using MiniBrowser.Services;

public class HotkeysTests
{
    private sealed class FakeActions : IBrowserActions
    {
        public List<string> Calls { get; } = new();
        public bool IsMenuOpen { get; set; }
        public bool IsManualFullscreen { get; set; }
        public void NewTab() => Calls.Add("NewTab");
        public void CloseActiveTab() => Calls.Add("CloseActiveTab");
        public void FocusAddressBar() => Calls.Add("FocusAddressBar");
        public void NextTab() => Calls.Add("NextTab");
        public void PrevTab() => Calls.Add("PrevTab");
        public void AddBookmark() => Calls.Add("AddBookmark");
        public void GoBack() => Calls.Add("GoBack");
        public void GoForward() => Calls.Add("GoForward");
        public void Reload() => Calls.Add("Reload");
        public void ToggleFullscreen() => Calls.Add("ToggleFullscreen");
        public void ExitFullscreen() => Calls.Add("ExitFullscreen");
        public void ToggleMenu() => Calls.Add("ToggleMenu");
        public void ShowHistory() => Calls.Add("ShowHistory");
        public void ShowBookmarks() => Calls.Add("ShowBookmarks");
        bool IBrowserActions.IsManualFullscreen => IsManualFullscreen;
    }

    [Fact] public void CtrlM_TogglesMenu()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.M, ModifierKeys.Control, a));
        Assert.Equal(new[] { "ToggleMenu" }, a.Calls);
    }

    [Fact] public void CtrlH_ShowsHistory()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.H, ModifierKeys.Control, a));
        Assert.Equal(new[] { "ShowHistory" }, a.Calls);
    }

    [Fact] public void CtrlShiftB_ShowsBookmarks()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.B, ModifierKeys.Control | ModifierKeys.Shift, a));
        Assert.Equal(new[] { "ShowBookmarks" }, a.Calls);
    }

    [Fact] public void CtrlH_DoesNotClashWithExistingBindings()
    {
        var a = new FakeActions();
        Hotkeys.TryHandle(Key.H, ModifierKeys.Control, a);
        Assert.Single(a.Calls);
    }

    [Fact] public void Escape_WhenMenuOpen_ClosesMenu()
    {
        var a = new FakeActions { IsMenuOpen = true };
        Assert.True(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Equal(new[] { "ToggleMenu" }, a.Calls);
    }

    [Fact] public void Escape_WhenMenuClosed_ReachesFullscreenHandler()
    {
        var a = new FakeActions { IsMenuOpen = false, IsManualFullscreen = true };
        Assert.True(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Equal(new[] { "ExitFullscreen" }, a.Calls);
    }

    [Fact] public void Escape_WhenMenuClosedAndNotFullscreen_NotHandled()
    {
        // Esc обязан дойти до страницы: иначе HTML5-полноэкранное видео
        // нельзя было бы закрыть.
        var a = new FakeActions { IsMenuOpen = false, IsManualFullscreen = false };
        Assert.False(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Empty(a.Calls);
    }

    [Fact] public void ExistingHotkeys_StillWork()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.T, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.W, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.L, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.D, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.R, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.F11, ModifierKeys.None, a));
        Assert.Equal(new[] { "NewTab", "CloseActiveTab", "FocusAddressBar",
            "AddBookmark", "Reload", "ToggleFullscreen" }, a.Calls);
    }

    [Fact] public void ModifierlessB_IsNotSwallowed()
    {
        var a = new FakeActions();
        Assert.False(Hotkeys.TryHandle(Key.B, ModifierKeys.None, a));
        Assert.Empty(a.Calls);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj --filter "FullyQualifiedName~HotkeysTests"`
Expected: ошибка компиляции — `FakeActions` не реализует новые члены `IBrowserActions`.

- [ ] **Step 3: Расширить `IBrowserActions`**

`src/MiniBrowser/Services/Hotkeys.cs` — добавить в интерфейс `ToggleMenu()`, `ShowHistory()`, `ShowBookmarks()` и `bool IsMenuOpen { get; }`.

- [ ] **Step 4: Добавить обработку в `Hotkeys.TryHandle`**

В блоке `case ModifierKeys.Control:` добавить `case Key.M: actions.ToggleMenu(); return true;` и `case Key.H: actions.ShowHistory(); return true;`. В блок `case ModifierKeys.Control | ModifierKeys.Shift:` добавить `if (key == Key.B) { actions.ShowBookmarks(); return true; }`.

Блок `Esc` в конце метода, **до** существующей проверки `IsManualFullscreen`:
```csharp
// Esc закрывает панель меню, но только когда она открыта: иначе он
// обязан дойти до страницы и до логики выхода из полноэкранного режима.
if (key == Key.Escape && modifiers == ModifierKeys.None && actions.IsMenuOpen)
{
    actions.ToggleMenu();
    return true;
}
```

- [ ] **Step 5: Запустить и убедиться, что проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: все тесты PASS.

- [ ] **Step 6: Закоммитить**

```bash
git add src/MiniBrowser/Services/Hotkeys.cs tests/MiniBrowser.Tests/HotkeysTests.cs
git commit -m "Add menu hotkeys and Escape-to-close"
```

---

### Task 8: Подключение панели к окну и применение настроек

**Files:**
- Modify: `src/MiniBrowser/MainWindow.xaml`
- Modify: `src/MiniBrowser/MainWindow.xaml.cs`
- Modify: `src/MiniBrowser/Views/ToolbarView.xaml`
- Modify: `src/MiniBrowser/Views/ToolbarView.xaml.cs`

**Interfaces:**
- Consumes: `MenuDrawerView`, `MenuDrawerViewModel`, `SettingsService`, `Hotkeys`, `TabManager.ApplyZoomToAllTabs()`, `StorageService`.
- Produces:
- `MainWindow` реализует новые члены `IBrowserActions`: `ToggleMenu`, `ShowHistory`, `ShowBookmarks`, `IsMenuOpen`.
    - `MainWindow` реализует `Window_Closing`, сохраняющий `Width`/`Height` при `WindowMaximized == true`.
    - `ToolbarView` теряет событие `OpenRequested` и `Storage`; получает `public event Action? ToggleDrawerRequested;`.
    - `ToolbarView` теряет `Menu`, `Menu_Opened`, `Menu_Click` и `ContextMenu` в разметке.
- Global constraints: панель и затемнение скрываются в полноэкранном режиме через существующий `ApplyFullscreen`. `SettingsChanged` поднимается VM **до** того, как `MainWindow` применит настройки.

- [ ] **Step 1: Убрать старое меню из `ToolbarView`**

`src/MiniBrowser/Views/ToolbarView.xaml`: у кнопки `MenuButton` удалить вложенный `<Button.ContextMenu>` целиком, `ToolTip` заменить на `Меню (Ctrl+M)`.

`src/MiniBrowser/Views/ToolbarView.xaml.cs`:
- Удалить `public StorageService? Storage { get; set; }`, `Menu_Click`, `Menu_Opened` и локальную функцию `AddSection`.
- Удалить `using MiniBrowser.Services;`? Нет — он нужен для `NavigationService` в `Address_KeyDown`. Оставить.
- Добавить `public event Action? ToggleDrawerRequested;` и обработчик `private void Menu_Click(...) => ToggleDrawerRequested?.Invoke();`.
- Удалить `public event Action<string>? OpenRequested;` — единственный подписчик был меню.

- [ ] **Step 2: Собрать и убедиться, что список ошибок предсказуем**

Run: `dotnet build MiniBrowser.sln`
Expected: ошибки только в `MainWindow.xaml.cs` — `OpenRequested` и `Toolbar.Storage` больше не существуют. Ошибок в самом `ToolbarView` быть не должно.

- [ ] **Step 3: Добаавть строку drawer в `MainWindow.xaml`**

Между строкой `ContentHost` (`Grid.Row="2"`) и статус-баром вставить:
```xml
<!-- Панель меню: обычная Grid поверх содержимого, а не Popup —
     у Popup слои не пересекаются с WebView2 -->
<Grid Grid.Row="2" ZIndex="100" Visibility="Collapsed">
    <Border Background="#80000000" MouseLeftButtonDown="DrawerScrim_Click"/>
    <views:MenuDrawerView x:Name="Drawer"
                          HorizontalAlignment="Left"
                          Visibility="Collapsed"
                          CloseRequested="Drawer_CloseRequested"/>
</Grid>
```
`Grid.Row="2"` — та же строка, что у `ContentHost`: панель перекрывает страницу, а не сдвигает её. Панели одной строки: `ZIndex="100"` поднимает поверх. `MouseLeftButtonDown` на `Border` затемнения поднимает закрытие.

- [ ] **Step 4: Подключить панель в `MainWindow.xaml.cs`**

`src/MiniBrowser/MainWindow.xaml.cs`:
- Поле `private readonly MenuDrawerViewModel _drawerVm;` и `private bool _menuOpen;`.
- В конструкторе, после `Toolbar.Storage` (строка, удалённая шагом 1) — создать `_drawerVm = new MenuDrawerViewModel(_storage, _settings);`, `Drawer.DataContext = _drawerVm;`.
- `_drawerVm.NavigateRequested += url => { _tabManager.NavigateActive(url); SetMenuOpen(false); };`
- `_drawerVm.HistoryChanged += () => StatusText.Text = "История очищена";`
- `_drawerVm.SettingsChanged += ApplySettings;`
- `Drawer.CloseRequested += () => SetMenuOpen(false);`
- `Toolbar.ToggleDrawerRequested += () => SetMenuOpen(!_menuOpen);`

Заменить `Toolbar.OpenRequested` (строка `Toolbar.OpenRequested += url => _tabManager.NavigateActive(url);`) — подписка удаляется, `Toolbar.NavigateRequested` остаётся.

- [ ] **Step 5: Реализовать открытие и закрытие панели**

Добавить в `MainWindow.xaml.cs`:
```csharp
private void SetMenuOpen(bool open)
{
    _menuOpen = open;
    Drawer.SetOpen(open);
    var host = (Grid)Drawer.Parent;
    host.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
}

void IBrowserActions.ToggleMenu() => SetMenuOpen(!_menuOpen);
void IBrowserActions.ShowHistory() { _drawerVm.ClearSearchCommand.Execute(null); SetMenuOpen(true); }
void IBrowserActions.ShowBookmarks() { _drawerVm.ClearSearchCommand.Execute(null); SetMenuOpen(true); }
bool IBrowserActions.IsMenuOpen => _menuOpen;
```
`ShowHistory` и `ShowBookmarks` открывают панель и очищают поиск; раздел выбирает сам Drawer по индексу — добавить в `MenuDrawerView` публичный метод `SelectTab(int index)`, вызываемый из этих двух методов: 1 для истории, 0 для закладок.

- [ ] **Step 6: Добавить `ApplySettings`**

```csharp
private void ApplySettings()
{
    var s = _settings.Current;

    // Масштаб — на все живые движки; уснувшие вкладки возьмут значение
    // при пробуждении через zoomProvider.
    _tabManager.ApplyZoomToAllTabs();

    StatusBarBorder.Visibility = s.ShowStatusBar ? Visibility.Visible : Visibility.Collapsed;

    // Поисковая система живёт в адресной строке, а не в самом Drawer.
    Toolbar.SearchTemplate = s.SearchUrl;

    _settings.Save();

    // Размер окна отсюда не применяем: его задают пресеты и восстановление
    // при старте. Иначе перетаскивание окна поверх открытой панели
    // заспамило бы настройки.
}
```

- [ ] **Step 7: Применить размер окна и домашнюю страницу при старте**

В `OnContentRendered`, в ветке `else` (без стартовых URL) заменить `_tabManager.NewTab(HomeUrl)` на `_tabManager.NewTab(_settings.Current.HomeUrl)`. Константа `HomeUrl` в `MainWindow` удаляется — её источником стало поле `_settings`. Удаление константы обязательно, иначе останется мёртвый код с двумя источниками правды.

В конструкторе, после `InitializeComponent()`, применить сохранённый размер:
```csharp
if (_settings.Current.WindowMaximized) WindowState = WindowState.Maximized;
else { Width = _settings.Current.WindowWidth; Height = _settings.Current.WindowHeight; }
```
Размер применяется **до** первого `OnContentRendered`, чтобы окно не мигало дефолтным.

- [ ] **Step 8: Сохранять размер при закрытии**

Обработчик `Window_Closing` подписан в разметке (шаг 10). В коде:
```csharp
private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
{
    if (_settings.Current.WindowMaximized)
    {
        // Восстанавливаем обычный размер: иначе в настройки попадёт
        // развёрнутое на весь экран состояние.
        var bounds = RestoreBounds;
        if (bounds is { Width: > 0, Height: > 0 })
        {
            _settings.Current.WindowWidth = bounds.Width;
            _settings.Current.WindowHeight = bounds.Height;
        }
    }
    _settings.Save();
}
```
`RestoreBounds` используется вместо `Width`/`Height`, потому что у развёрнутого окна они равны размеру экрана.

- [ ] **Step 9: Скрывать панель в полноэкранном режиме**

В `ApplyFullscreen` добавить drawer в обе ветки: в ветке `fullscreen` — `Drawer.Visibility = Visibility.Collapsed;` вместе с `Toolbar`/`TabStrip`/`StatusText`; в ветке `else` — `Drawer.Visibility = Visibility.Visible;` и `SetMenuOpen(false)` при `_menuOpen`, чтобы панель не осталась «залипшей» под видео. Если `_menuOpen` сбросить нельзя раньше, чем вернули видимость, порядок такой: сначала видимость, потом сброс флага через `SetMenuOpen(false)`.

- [ ] **Step 10: Дать имя строке статус-бара в разметке**

В `MainWindow.xaml` статус-бар — `Border` в строке 3, у него нет `x:Name`. Добавить `x:Name="StatusBarBorder"` и `Closing="Window_Closing"` к элементу `Window` — обращаться к `StatusBarBorder.Visibility` вместо обхода визуального дерева, которое было бы хрупким. `StatusBarBorder` должен быть объявлен **до** шага 6, иначе компилятор не найдёт поле; если порядок шагов приводит к ошибке, выполнить шаг 10 раньше шага 6.

- [ ] **Step 11: Применить поисковую систему к адресной строке**

В `ToolbarView.xaml.cs` `Address_KeyDown` вызов `NavigationService.BuildUrl(AddressBox.Text)` заменяется на вызов через публичное свойство, чтобы `ToolbarView` не знал о `SettingsService`: добавить в `ToolbarView` `public string SearchTemplate { get; set; } = NavigationService.SearchUrlTemplate;` и использовать `NavigationService.BuildUrl(AddressBox.Text, SearchTemplate)`. В `MainWindow` подписать: `Toolbar.SearchTemplate = _settings.Current.SearchUrl;`. Так как `SearchUrl` может измениться из панели, обновлять это свойство внутри `ApplySettings` — одной строкой.

- [ ] **Step 12: Собрать и прогнать тесты**

Run: `dotnet build MiniBrowser.sln` затем `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj`
Expected: сборка успешна, все тесты PASS.

- [ ] **Step 13: Проверить руками**

Run: `dotnet run --project src/MiniBrowser`
Проверить по списку: 1) `Ctrl+M` открывает панель, `Esc` и клик по затемнению закрывают; 2) закладки и история показывают полные списки, поиск сужает, счётчик показывает «12 из 84»; 3) ✕ на строке закладки удаляет её; 4) «Очистить историю» чистит список; 5) ползунок масштаба меняет размер текста на открытой странице **и** на новой вкладке; 6) после `Sleep` вкладки и её возврата масштаб сохраняется; 7) пресет 1600×1000 меняет размер окна; 8) `Ctrl+H` открывает панель на вкладке «История»; 9) `F11` убирает панель и затемнение, возврат по `Esc` восстанавливает; 10) перезапуск приложения сохраняет масштаб, размер, домашнюю страницу и статусную строку.

- [ ] **Step 14: Закоммитить**

```bash
git add src/MiniBrowser/MainWindow.xaml src/MiniBrowser/MainWindow.xaml.cs src/MiniBrowser/Views/ToolbarView.xaml src/MiniBrowser/Views/ToolbarView.xaml.cs src/MiniBrowser/Views/MenuDrawerView.xaml.cs
git commit -m "Wire drawer into window and apply persisted settings"
```