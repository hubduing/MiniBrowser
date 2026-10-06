# Tab Groups Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Перевести полосу вкладок в вертикальные колонки именованных групп слева, научить вкладку перетаскиваться мышью между группами и внутри них и восстанавливать раскладку групп и вкладок при следующем запуске.

**Architecture:** Состав и порядок групп живут в чистом классе `TabGroups` (без WPF), `TabManager` владеет им поверх своей работы с `BrowserTabView`. Персистентность сессии — `SessionService` поверх `StorageService` (строки-«снимки» + транзакционная перезапись), выбор стартового состояния — чистый класс `StartupPlan`. Геометрия drop вынесена в `TabDropResolver`, чтобы перетаскивание тестировалось без окна; разметка (`GroupColumnView`, `TabStripView`, две колонки в `MainWindow`) собирает из этих кусков рабочий UI и проверяется только сборкой и ручным прогоном.

**Tech Stack:** .NET 8, WPF, WebView2 (`Microsoft.Web.WebView2` 1.0.2592.51), `Microsoft.Data.Sqlite` 8.0.10, `System.Text.Json`, xUnit 2.9.2.

**Spec:** `docs/superpowers/specs/2026-10-06-tab-groups-design.md`

## Global Constraints

- Тема тёмная и не переключается. Все стили используют существующие кисти из `App.xaml`: `B.Window`, `B.Toolbar`, `B.TabStrip`, `B.Content`, `B.Status`, `B.Border`, `B.Hover`, `B.Pressed`, `B.TabIdle`, `B.Address`, `T.Primary`, `T.Muted`, `T.Accent`. Новых кистей не вводить — 8 цветов групп объявляются как массив `SolidColorBrush` в `App.xaml` с ключами `GroupColor0`…`GroupColor7`.
- Русский текст интерфейса. Комментарии в коде — по-русски, в стиле существующих: объясняют «почему», не «что».
- Сбой хранилища или настроек не должен ронять браузер: ломается функциональность, а не приложение. Уже принятая в проекте линия — следовать ей.
- Путь БД прежний: `%LOCALAPPDATA%\MiniBrowser\browser.db`. Таблицы `history` и `bookmarks` не меняются.
- Вкладка принадлежит ровно одной группе; вкладок вне групп не бывает.
- Группа без вкладок сохраняется и переживает закрытие всех вкладок.
- Ширина полосы вкладок: `MinWidth="150"`, `MaxWidth="420"`, значение по умолчанию 240, тянется мышью, сохраняется в `AppSettings`.
- Свёрнутая группа сужается до 32 px; высота зоны заголовка — 28 px (эта константа используется и разметкой, и `TabDropResolver`).
- Восстановление сессии включено по умолчанию (`AppSettings.RestoreSession = true`). URL'ы из аргументов запуска открываются **после** восстановления, новыми вкладками в конце активной группы, и сессию не заменяют.
- Сессия пишется с дебаунсом 2 с и принудительно при закрытии окна. Ошибки записи глушатся.
- Коммит после каждой задачи, сообщения на английском в стиле существующих (`git log`: `Add menu hotkeys and Escape-to-close`).
- Незакоммиченные изменения вне файлов задачи в коммит не попадают: перед `git add` проверять `git status --short` и добавлять только перечисленные в задаче файлы.

## Review Focus

Пять классов ввода/состояний, которые спека подразумевает, но обычные сценарии не закрывают:

1. **Битая сессия в БД** — `color_index` вне 0–7, `is_active = 1` у нескольких вкладок, у вкладки несуществующий `group_id`, нечитаемый `id`. Ожидание: браузер стартует, мусорные строки пропущены, активна одна вкладка, браузер не падает. → Тест в Task 4.
2. **Пустая группа сохраняется и восстанавливается** — закрыли все вкладки группы, перезапустили: группа на месте, пустая. → Тест в Task 4.
3. **Указатель мыши в серой зоне** — курсор между колонками, ниже последней колонки, в полосе прокрутки. Ожидание: перетаскивание отменяется, вкладка остаётся на месте. → Тест в Task 5.
4. **Перетаскивание вкладки в ту же позицию той же группы** — ожидание: полный no-op, без событий и без перерисовки. → Тест в Task 5 и Task 2.
5. **Активная вкладка находится в свёрнутой группе**, и пользователь жмёт `Ctrl+Tab`. Ожидание: группа разворачивается, активируется целевая вкладка. → Тест в Task 2 (`SetActive` в свёрнутой группе не запрещён, порядок плоского списка не меняется) и ручная проверка в Task 8.

---

### Task 1: Модель `TabGroup` и идентичность вкладки

**Files:**
- Create: `src/MiniBrowser/Models/TabGroup.cs`
- Modify: `src/MiniBrowser/Models/Tab.cs`
- Create: `tests/MiniBrowser.Tests/TabGroupTests.cs`

**Interfaces:**
- Consumes: ничего (первая задача).
- Produces:
  - `Models/TabGroup.cs`:
    - `public Guid Id { get; }` — новый `Guid` на группу.
    - `public string Name { get; set; }` — уведомляет `PropertyChanged` только при реальном изменении.
    - `public int ColorIndex { get; set; }` — индекс палитры 0–7; значение вне диапазона **не** нормализуется здесь (нормализация в UI и `SessionService`).
    - `public bool IsCollapsed { get; set; }`.
    - `public ObservableCollection<Tab> Tabs { get; }` — заменять нельзя, UI подписан на эту коллекцию.
    - `public int Count => Tabs.Count;`
    - класс реализует `INotifyPropertyChanged` и уведомляет об `Count` при изменении `Tabs`.
  - `Models/Tab.cs`:
    - `public Guid GroupId { get; set; }` — идентификация принадлежности; контейнером владеет `TabGroup.Tabs`.
    - `public Tab()` — как сейчас (новый `Guid`).
    - `public Tab(Guid id)` — для восстановления сессии с прежним `Id`.

- [ ] **Step 1: Написать падающий тест**

`tests/MiniBrowser.Tests/TabGroupTests.cs`:

```csharp
using MiniBrowser.Models;
using Xunit;

public class TabGroupTests
{
    [Fact]
    public void Count_FollowsTabCollection()
    {
        var g = new TabGroup { Name = "Работа" };
        Assert.Equal(0, g.Count);
        g.Tabs.Add(new Tab());
        g.Tabs.Add(new Tab());
        Assert.Equal(2, g.Count);
    }

    [Fact]
    public void Rename_RaisesPropertyChangedOnce()
    {
        var g = new TabGroup { Name = "Работа" };
        var changed = new List<string?>();
        g.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        g.Name = "Работа";      // то же значение — события быть не должно
        g.Name = "Учёба";

        Assert.Equal(new[] { nameof(TabGroup.Name) }, changed);
        Assert.Equal("Учёба", g.Name);
    }

    [Fact]
    public void Collapse_RaisesPropertyChanged()
    {
        var g = new TabGroup();
        var changed = new List<string?>();
        g.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        g.IsCollapsed = true;

        Assert.Equal(new[] { nameof(TabGroup.IsCollapsed) }, changed);
    }

    [Fact]
    public void TabCollection_RaisesCountChanged()
    {
        var g = new TabGroup();
        var countNotifications = 0;
        g.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TabGroup.Count)) countNotifications++;
        };

        g.Tabs.Add(new Tab());
        g.Tabs.RemoveAt(0);

        // Add и Remove обязаны уведомить: иначе бейдж «N» на заголовке застыл бы.
        Assert.Equal(2, countNotifications);
    }

    [Fact]
    public void Tab_GetsStableIdFromConstructor()
    {
        var id = Guid.NewGuid();
        Assert.Equal(id, new Tab(id).Id);
        Assert.NotEqual(Guid.Empty, new Tab().Id);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что тесты падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TabGroupTests"`
Expected: FAIL с ошибкой компиляции `TabGroup` не найден.

- [ ] **Step 3: Реализовать `Models/TabGroup.cs`**

Класс с полями-свойствами и `INotifyPropertyChanged`. Свойства `Name`, `ColorIndex`, `IsCollapsed` — через сеттер с проверкой `if (field != value)`; `Tabs` — поле `readonly ObservableCollection<Tab>`, на которое подписан `CollectionChanged`, а обработчик шлёт `OnPropertyChanged(nameof(Count))` на любое событие коллекции (и `Add`, и `Remove`, и `Clear` — счётчик обязан обновляться всегда). Не добавлять `Move`-специфичной логики: смена позиции не меняет `Count`.

- [ ] **Step 4: Добавить `GroupId` и конструктор с `Id` в `Models/Tab.cs`**

Ровно три правки: поле-автосвойство `GroupId`, конструктор без параметров, делегирующий `this(Guid.NewGuid())`, и конструктор `public Tab(Guid id)` с присваиванием `Id = id`. Остальной класс не трогать — `BrowserTabView`, `TabSleeper` и заголовок окна зависят от его текущего поведения.

- [ ] **Step 5: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TabGroupTests"`
Expected: PASS, 5 тестов.

- [ ] **Step 6: Коммит**

```bash
git add src/MiniBrowser/Models/TabGroup.cs src/MiniBrowser/Models/Tab.cs tests/MiniBrowser.Tests/TabGroupTests.cs
git commit -m "Add TabGroup model and tab group identity"
```

---

### Task 2: `TabGroups` — состав и порядок без WPF

**Files:**
- Create: `src/MiniBrowser/Services/TabGroups.cs`
- Create: `tests/MiniBrowser.Tests/TabGroupsTests.cs`

**Interfaces:**
- Consumes: `TabGroup`, `Tab` из Task 1.
- Produces: `Services/TabGroups.cs` — единственный владелец порядка:
  - `public TabGroups()`, `public TabGroups(IEnumerable<TabGroup> restored)` — второй восстанавливает готовую раскладку (`Tabs` уже заполнены), активной становится первая вкладка первой непустой группы.
  - `public event Action? Changed;` — состав вкладок или их порядок изменились.
  - `public event Action? GroupsChanged;` — группы созданы, удалены, переставлены, переименованы или свёрнуты.
  - `public event Action<Tab>? ActiveChanged;`
  - `public IReadOnlyList<TabGroup> Groups { get; }`
  - `public IReadOnlyList<Tab> Tabs { get; }` — плоский кэш в порядке отображения: группы по порядку, внутри группы по порядку вкладок.
  - `public TabGroup? ActiveGroup { get; }`, `public Tab? ActiveTab { get; }`
  - `public TabGroup CreateGroup(string? name = null, int? colorIndex = null)` — имя по умолчанию `$"Группа {Groups.Count + 1}"`, цвет по умолчанию — первый незанятый индекс из 0–7, иначе `Groups.Count % 8`.
  - `public bool RemoveGroup(TabGroup group)` — `false`, если группы нет.
  - `public bool MoveGroup(TabGroup group, int newIndex)` — `newIndex` клампится в `[0, Groups.Count - 1]`.
  - `public TabGroup EnsureFirstGroup()` — создаёт «Группа 1», если групп нет.
  - `public TabGroup ActiveOrFirst()` — активная группа, иначе первая, иначе `EnsureFirstGroup()`.
  - `public Tab AddTab(TabGroup? group = null, Tab? tab = null)` — группа `null` → `ActiveOrFirst()`; `tab` `null` → `new Tab()`. Возвращает добавленную вкладку, проставляет ей `GroupId` и ставит активной.
  - `public bool RemoveTab(Tab tab)` — группа **не** удаляется.
  - `public bool MoveTab(Tab tab, TabGroup target, int index)` — `index` клампится в `[0, target.Count]` (индекс вставки); `false`, если цель совпадает с текущей группой и позицией.
  - `public bool SetActive(Tab? tab)` — `false`, если вкладки нет или она уже активна; свёрнутость группы **не** трогает.
  - `public TabGroup? GroupOf(Tab tab)`, `public int IndexOf(Tab tab)`.
  - `public void Clear()`.

- [ ] **Step 1: Написать падающий тест**

`tests/MiniBrowser.Tests/TabGroupsTests.cs` — конструктор `TabGroups`, `TabGroup`/`Tab` без UI. Обязательные кейсы (имена тестов использовать как есть):

```csharp
using MiniBrowser.Services;
using Xunit;

public class TabGroupsTests
{
    [Fact]
    public void AddTab_WithoutGroups_CreatesFirstGroup()
    {
        var groups = new TabGroups();
        var tab = groups.AddTab();
        Assert.Single(groups.Groups);
        Assert.Equal("Группа 1", groups.Groups[0].Name);
        Assert.Equal(tab, groups.ActiveTab);
    }

    [Fact]
    public void AddTab_WithoutGroup_GoesToActiveGroup()
    {
        var groups = new TabGroups();
        var first = groups.AddTab();
        var second = groups.AddTab();
        var group = groups.ActiveGroup!;
        Assert.Equal(2, group.Count);
        Assert.Equal(new[] { first, second }, groups.Tabs.ToArray());
        Assert.Equal(group.Id, second.GroupId);
    }

    [Fact]
    public void AddTab_WithExplicitGroup_JoinsItsEnd()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var tab = groups.AddTab(b);
        Assert.Equal(1, a.Count);
        Assert.Single(b.Tabs);
        Assert.Equal(tab, groups.ActiveTab);
    }

    [Fact]
    public void CreateGroup_AutoColor_DoesNotRepeatExisting()
    {
        var groups = new TabGroups();
        Assert.Equal(0, groups.CreateGroup("А").ColorIndex);
        Assert.Equal(1, groups.CreateGroup("Б").ColorIndex);
        // Восемь групп — палитра кончилась, цикл начинается заново.
        for (var i = 0; i < 6; i++) groups.CreateGroup($"Г{i}");
        Assert.InRange(groups.CreateGroup("Хвост").ColorIndex, 0, 7);
    }

    [Fact]
    public void RemoveTab_KeepsGroup()
    {
        var groups = new TabGroups();
        var group = groups.CreateGroup("А");
        var tab = groups.AddTab(group);
        groups.RemoveTab(tab);
        Assert.Single(groups.Groups);
        Assert.Equal(0, group.Count);
        Assert.Empty(groups.Tabs);
    }

    [Fact]
    public void RemoveGroup_TabsLeaveTheManager()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var inA = groups.AddTab(a);
        groups.AddTab(b);
        Assert.True(groups.RemoveGroup(a));
        Assert.Single(groups.Groups);
        Assert.DoesNotContain(inA, groups.Tabs);
        Assert.False(groups.RemoveGroup(a));
    }

    [Fact]
    public void MoveTab_ReordersWithinGroup()
    {
        var groups = new TabGroups();
        var group = groups.CreateGroup("А");
        var first = groups.AddTab(group);
        var second = groups.AddTab(group);
        var third = groups.AddTab(group);

        Assert.True(groups.MoveTab(third, group, 0));

        Assert.Equal(new[] { third, first, second }, group.Tabs.ToArray());
        Assert.Equal(new[] { third, first, second }, groups.Tabs.ToArray());
    }

    [Fact]
    public void MoveTab_ToOtherGroup_KeepsSourceOrderOfOthers()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var t1 = groups.AddTab(a);
        var t2 = groups.AddTab(a);
        var t3 = groups.AddTab(a);

        groups.MoveTab(t2, b, 0);

        Assert.Equal(new[] { t1, t3 }, a.Tabs.ToArray());
        Assert.Equal(new[] { t2 }, b.Tabs.ToArray());
        Assert.Equal(b.Id, t2.GroupId);
        // Сквозной порядок для горячих клавиш идёт по группам.
        Assert.Equal(new[] { t1, t3, t2 }, groups.Tabs.ToArray());
    }

    [Fact]
    public void MoveTab_ToSamePositionOfSameGroup_IsNoOp()
    {
        var groups = new TabGroups();
        var group = groups.CreateGroup("А");
        var t1 = groups.AddTab(group);
        var t2 = groups.AddTab(group);
        var changed = 0;
        groups.Changed += () => changed++;

        Assert.False(groups.MoveTab(t1, group, 0));

        Assert.Equal(0, changed);
        Assert.Equal(new[] { t1, t2 }, group.Tabs.ToArray());
    }

    [Fact]
    public void MoveGroup_ReordersColumns()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var c = groups.CreateGroup("В");
        groups.AddTab(a);
        groups.AddTab(b);
        groups.AddTab(c);

        Assert.True(groups.MoveGroup(c, 0));

        Assert.Equal(new[] { c, a, b }, groups.Groups.ToArray());
        Assert.Equal(c.Tabs[0], groups.Tabs[0]);
    }

    [Fact]
    public void SetActive_KeepsFlatOrderUnchanged()
    {
        var groups = new TabGroups();
        var a = groups.CreateGroup("А");
        var b = groups.CreateGroup("Б");
        var t1 = groups.AddTab(a);
        groups.AddTab(b);
        var order = groups.Tabs.ToArray();

        // Цель в свёрнутой группе активируется: сворачивание её не мешает.
        b.IsCollapsed = true;
        Assert.True(groups.SetActive(t1));

        Assert.True(groups.ActiveTab!.IsActive);
        Assert.Equal(order, groups.Tabs.ToArray());
    }

    [Fact]
    public void SetActive_UnknownTab_ReturnsFalse()
    {
        var groups = new TabGroups();
        groups.AddTab();
        Assert.False(groups.SetActive(new Tab()));
        Assert.False(groups.SetActive(null));
    }

    [Fact]
    public void Restored_Layout_KeepsOrderAndActivatesFirst()
    {
        var a = new TabGroup { Name = "А", ColorIndex = 3, IsCollapsed = true };
        a.Tabs.Add(new Tab { Url = "https://a.example/" });
        var b = new TabGroup { Name = "Б" };
        b.Tabs.Add(new Tab { Url = "https://b.example/" });

        var groups = new TabGroups(new[] { a, b });

        Assert.Equal(new[] { a, b }, groups.Groups.ToArray());
        Assert.Equal(3, a.ColorIndex);
        Assert.True(a.IsCollapsed);
        Assert.Equal(2, groups.Tabs.Count);
        Assert.Equal("https://a.example/", groups.ActiveTab!.Url);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что тесты падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TabGroupsTests"`
Expected: FAIL с ошибкой компиляции `TabGroups` не найден.

- [ ] **Step 3: Реализовать `Services/TabGroups.cs`**

`List<TabGroup> _groups`, кэш `List<Tab> _flat`, пересборка кэша в `Rebuild()` после любого изменения состава или порядка. Правила перестановки:

- `MoveTab` — найти текущую группу через `GroupOf`, снять вкладку из её `Tabs`, вставить в `target.Tabs` через `Insert` с клампленным `index`, проставить `GroupId = target.Id`, `Rebuild()`, `Changed`, `GroupsChanged` (состав групп изменился), `SetActive(tab)`. Если цель — та же группа и `index` совпадает с текущей позицией (с учётом того, что снятие сдвинуло индексы) — сразу вернуть `false`.
- `RemoveGroup` — снять все вкладки группы из `_flat`, удалить группу из `_groups`, `Rebuild()`, `Changed` + `GroupsChanged`; активной становится первая оставшаяся вкладка (`ActiveChanged`), если активная была удалена.
- `MoveGroup` — `Move` внутри `_groups` с клампленным индексом, `Rebuild()` не нужен (порядок вкладок не меняется), `GroupsChanged`.
- `SetActive` — снять `IsActive` с прежней активной, поставить новой, `ActiveChanged`.
- `AddTab` — вставка в конец группы, `Rebuild()`, `Changed`, затем `SetActive(tab)`.

Не делать `INotifyPropertyChanged` на самом `TabGroups`: подписчикам достаточно трёх событий, а коллекции групп отдаются как `IReadOnlyList` снимкой.

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TabGroupsTests"`
Expected: PASS, 14 тестов.

- [ ] **Step 5: Коммит**

```bash
git add src/MiniBrowser/Services/TabGroups.cs tests/MiniBrowser.Tests/TabGroupsTests.cs
git commit -m "Add tab groups model with ordering and moves"
```

---

### Task 3: Снимок сессии и его хранение в SQLite

**Files:**
- Create: `src/MiniBrowser/Models/SessionSnapshot.cs`
- Modify: `src/MiniBrowser/Services/StorageService.cs` (существующий блок `Exec(""" CREATE TABLE IF NOT EXISTS history ...` и конец класса)
- Modify: `tests/MiniBrowser.Tests/StorageServiceTests.cs`

**Interfaces:**
- Consumes: ничего из предыдущих задач (работает на своих строках).
- Produces:
  - `Models/SessionSnapshot.cs`:
    - `public sealed record SessionGroupRow(string Id, string Name, int ColorIndex, bool Collapsed, int Position);`
    - `public sealed record SessionTabRow(string Id, string GroupId, string Url, string Title, bool IsActive, int Position);`
    - `public sealed record SessionSnapshot(IReadOnlyList<SessionGroupRow> Groups, IReadOnlyList<SessionTabRow> Tabs);`
  - `StorageService`:
    - `public void SaveSession(SessionSnapshot snapshot)` — полная перезапись обеих таблиц в одной транзакции; недоступная БД и любая ошибка глушатся.
    - `public SessionSnapshot? LoadSession()` — `null`, если групп нет или БД недоступна; строки отсортированы по `position`.
    - Схема (добавить в существующий `Exec` с таблицами `history`/`bookmarks`):
      `tab_groups(id TEXT PRIMARY KEY, name TEXT NOT NULL, color_index INTEGER NOT NULL, position INTEGER NOT NULL, collapsed INTEGER NOT NULL)` и
      `session_tabs(id TEXT PRIMARY KEY, group_id TEXT NOT NULL, position INTEGER NOT NULL, url TEXT NOT NULL, title TEXT NOT NULL DEFAULT '', is_active INTEGER NOT NULL)`.

- [ ] **Step 1: Написать падающий тест**

Дописать в `tests/MiniBrowser.Tests/StorageServiceTests.cs` (конструктор класса уже создаёт `StorageService` на временном файле и удаляет папку в `Dispose`):

```csharp
    [Fact]
    public void LoadSession_NothingStored_ReturnsNull()
    {
        Assert.Null(_storage.LoadSession());
    }

    [Fact]
    public void SaveThenLoad_KeepsGroupsTabsAndOrder()
    {
        var snapshot = new SessionSnapshot(
            new[]
            {
                new SessionGroupRow("g1", "Работа", 3, true, 0),
                new SessionGroupRow("g2", "Учёба", 5, false, 1),
            },
            new[]
            {
                new SessionTabRow("t1", "g1", "https://a.example/", "А", true, 0),
                new SessionTabRow("t2", "g1", "https://b.example/", "Б", false, 1),
                new SessionTabRow("t3", "g2", "https://c.example/", "", false, 0),
            });

        _storage.SaveSession(snapshot);
        var loaded = _storage.LoadSession()!;

        Assert.Equal(2, loaded.Groups.Count);
        Assert.Equal("Работа", loaded.Groups[0].Name);
        Assert.Equal(3, loaded.Groups[0].ColorIndex);
        Assert.True(loaded.Groups[0].Collapsed);
        Assert.Equal(3, loaded.Tabs.Count);
        Assert.Equal(new[] { "t1", "t2", "t3" }, loaded.Tabs.Select(t => t.Id).ToArray());
        Assert.Equal("https://b.example/", loaded.Tabs[1].Url);
        Assert.True(loaded.Tabs[0].IsActive);
    }

    [Fact]
    public void SaveSession_SecondTime_ReplacesFirst()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("g1", "Старая", 0, false, 0) },
            new[] { new SessionTabRow("t1", "g1", "https://a.example/", "", false, 0) }));

        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("g2", "Новая", 1, false, 0) },
            new[] { new SessionTabRow("t2", "g2", "https://b.example/", "", false, 0) }));

        var loaded = _storage.LoadSession()!;
        Assert.Equal("Новая", Assert.Single(loaded.Groups).Name);
        Assert.Equal("t2", Assert.Single(loaded.Tabs).Id);
    }

    [Fact]
    public void SaveSession_EmptySnapshot_LeavesNothingToLoad()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("g1", "А", 0, false, 0) },
            Array.Empty<SessionTabRow>()));
        _storage.SaveSession(SessionService.EmptySnapshot());

        Assert.Null(_storage.LoadSession());
    }
```

- [ ] **Step 2: Запустить и убедиться, что тесты падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~StorageServiceTests"`
Expected: FAIL с ошибкой компиляции: `SessionSnapshot`, `SaveSession`, `LoadSession` не найдены.

- [ ] **Step 3: Добавить `Models/SessionSnapshot.cs`**

Три `record` — ровно с сигнатурами из блока Interfaces. `record` (не класс) ради сравнимости в тестах и без лишних полей.

- [ ] **Step 4: Дополнить `StorageService`**

Две таблицы — третьим и четвёртым оператором в существующий многострочный `Exec` внутри конструктора (таблицы `history` и `bookmarks` не трогать). Затем два метода перед `Dispose()`:

- `SaveSession` — `DELETE FROM tab_groups; DELETE FROM session_tabs;` затем вставки через `INSERT INTO ... VALUES ($p1, ...)` с параметрами, где `Collapsed`/`IsActive` пишутся как `1`/`0`. Параметры — только параметры, склейки строк не использовать: `name` и `title` приходят из интерфейса и могут содержать кавычки. Запись — в одной транзакции: `var tx = _connection.BeginTransaction(); ... tx.Commit();` с откатом в `catch`.
- `LoadSession` — два `SELECT` через `_connection.CreateCommand()` с читателем; `ORDER BY position`; `SessionSnapshot?` (`null`, если групп нет). Значения `collapsed`/`is_active` читать через `reader.GetInt64(0) != 0`.

- [ ] **Step 5: Добавить в `SessionService` статический `EmptySnapshot()`** (нужен тестом выше, сам класс появится в Task 4)

Временная заглушка не годится — вместо неё в этом шаге объявить в `Services/SessionService.cs` минимальный класс-заглушку **сразу с полным набором методов Task 4** нельзя: тест зовит только `SessionService.EmptySnapshot()`, поэтому в этом шаге создать `Services/SessionService.cs` с одним статическим методом:

```csharp
public static SessionSnapshot EmptySnapshot() =>
    new(Array.Empty<SessionGroupRow>(), Array.Empty<SessionTabRow>());
```

Task 4 расширит этот файл до полного сервиса.

- [ ] **Step 6: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~StorageServiceTests"`
Expected: PASS, все тесты класса зелёные (было 12 + 4 новых).

- [ ] **Step 7: Коммит**

```bash
git add src/MiniBrowser/Models/SessionSnapshot.cs src/MiniBrowser/Services/StorageService.cs src/MiniBrowser/Services/SessionService.cs tests/MiniBrowser.Tests/StorageServiceTests.cs
git commit -m "Store tab groups and tabs in the browser database"
```

---

### Task 4: `SessionService` — перевод снимка в живые группы

**Files:**
- Modify: `src/MiniBrowser/Services/SessionService.cs`
- Create: `tests/MiniBrowser.Tests/SessionServiceTests.cs`

**Interfaces:**
- Consumes: `StorageService.SaveSession`/`LoadSession`, `SessionSnapshot` (Task 3); `TabGroup`, `Tab(Guid)` (Task 1).
- Produces: `Services/SessionService.cs` — полный класс:
  - `public SessionService(StorageService storage)`
  - `public static SessionSnapshot EmptySnapshot()`
  - `public void Save(IReadOnlyList<TabGroup> groups, Tab? activeTab)` — строки строятся из групп; `activeTab` (по `Id`) помечается `IsActive = true`.
  - `public (SessionSnapshot? Snapshot, IReadOnlyList<TabGroup> Groups, Tab? ActiveTab) Load()` — `Snapshot` отдаёт то, что лежит на диске (`null`, если хранилище пусто), `Groups` — собранные `TabGroup` с заполненным `Tabs`, `ActiveTab` — найденная активная вкладка. Снимок нужен отдельно: `StartupPlan` решает по нему, а собранные группы могут оказаться пустыми даже при непустом хранилище.

- [ ] **Step 1: Написать падающий тест**

`tests/MiniBrowser.Tests/SessionServiceTests.cs` — тот же приём временной БД, что в `StorageServiceTests` (путь `%TEMP%/mb-tests/<guid>/browser.db`, `Dispose` удаляет папку). Обязательные кейсы:

```csharp
using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class SessionServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "mb-tests", Guid.NewGuid().ToString("N"), "browser.db");
    private readonly StorageService _storage;

    public SessionServiceTests() => _storage = new StorageService(_dir);

    public void Dispose()
    {
        _storage.Dispose();
        var dir = Path.GetDirectoryName(_dir)!;
        if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void Load_NothingStored_ReturnsEmpty()
    {
        var (_, groups, active) = new SessionService(_storage).Load();
        Assert.Empty(groups);
        Assert.Null(active);
    }

    [Fact]
    public void SaveThenLoad_KeepsLayoutAndActiveTab()
    {
        var service = new SessionService(_storage);

        var work = new TabGroup { Name = "Работа", ColorIndex = 4, IsCollapsed = true };
        var study = new TabGroup { Name = "Учёба", ColorIndex = 2 };
        var mail = new Tab(Guid.NewGuid());
        mail.Url = "https://mail.example/";
        var news = new Tab(Guid.NewGuid());
        var docs = new Tab(Guid.NewGuid());
        work.Tabs.Add(mail);
        work.Tabs.Add(news);
        study.Tabs.Add(docs);
        mail.IsActive = true;

        service.Save(new[] { work, study }, mail);
        var (_, groups, active) = service.Load();

        Assert.Equal(2, groups.Count);
        Assert.Equal("Работа", groups[0].Name);
        Assert.Equal(4, groups[0].ColorIndex);
        Assert.True(groups[0].IsCollapsed);
        Assert.Equal(new[] { "https://mail.example/", news.Url }, groups[0].Tabs.Select(t => t.Url).ToArray());
        Assert.Equal(mail.Id, groups[0].Tabs[0].Id);
        Assert.Equal(new[] { mail.Id, news.Id }, groups[0].Tabs.Select(t => t.Id).ToArray());
        Assert.NotNull(active);
        Assert.Equal(mail.Id, active!.Id);
    }

    [Fact]
    public void Load_EmptyGroup_RestoresIt()
    {
        var service = new SessionService(_storage);
        var group = new TabGroup { Name = "Заготовка", ColorIndex = 1 };

        service.Save(new[] { group }, null);
        var (_, groups, active) = service.Load();

        Assert.Equal("Заготовка", Assert.Single(groups).Name);
        Assert.Empty(groups[0].Tabs);
        Assert.Null(active);
    }

    [Fact]
    public void Save_GroupColorOutOfPalette_IsClampedOnLoad()
    {
        var service = new SessionService(_storage);
        var group = new TabGroup { Name = "А", ColorIndex = 42 };
        group.Tabs.Add(new Tab { Url = "https://a.example/" });

        service.Save(new[] { group }, null);
        var (_, groups, _) = service.Load();

        Assert.InRange(groups[0].ColorIndex, 0, 7);
    }

    [Fact]
    public void Load_TabsWithUnknownGroup_AreSkipped()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("g1", "А", 0, false, 0) },
            new[]
            {
                new SessionTabRow(Guid.NewGuid().ToString(), "g1", "https://a.example/", "А", false, 0),
                new SessionTabRow(Guid.NewGuid().ToString(), "нет-такой-группы", "https://b.example/", "Б", false, 1),
            }));

        var (_, groups, _) = new SessionService(_storage).Load();

        Assert.Single(groups);
        Assert.Equal("https://a.example/", Assert.Single(groups[0].Tabs).Url);
    }

    [Fact]
    public void Load_MultipleActiveRows_KeepsFirstByPosition()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("g1", "А", 0, false, 0) },
            new[]
            {
                new SessionTabRow(Guid.NewGuid().ToString(), "g1", "https://a.example/", "А", true, 0),
                new SessionTabRow(Guid.NewGuid().ToString(), "g1", "https://b.example/", "Б", true, 1),
            }));

        var (_, _, active) = new SessionService(_storage).Load();

        Assert.NotNull(active);
        Assert.Equal("https://a.example/", active!.Url);
    }

    [Fact]
    public void Load_UnparsableId_SkipsRow()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("g1", "А", 0, false, 0) },
            new[]
            {
                new SessionTabRow("не-guid", "g1", "https://a.example/", "А", false, 0),
                new SessionTabRow(Guid.NewGuid().ToString(), "g1", "https://b.example/", "Б", false, 1),
            }));

        var (_, groups, _) = new SessionService(_storage).Load();

        Assert.Equal("https://b.example/", Assert.Single(groups[0].Tabs).Url);
    }

    [Fact]
    public void Load_AllRowsUnusable_ReturnsEmpty()
    {
        _storage.SaveSession(new SessionSnapshot(
            new[] { new SessionGroupRow("не-guid", "А", 0, false, 0) },
            new[] { new SessionTabRow(Guid.NewGuid().ToString(), "не-guid", "https://a.example/", "", false, 0) }));

        var (_, groups, active) = new SessionService(_storage).Load();

        Assert.Empty(groups);
        Assert.Null(active);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что тесты падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~SessionServiceTests"`
Expected: FAIL: `Save`/`Load` не найдены в `SessionService`.

- [ ] **Step 3: Реализовать `Save` и `Load` в `Services/SessionService.cs`**

`Save`: перебрать группы с индексом `position`, для каждой — вкладки с индексом `position` (сквозным внутри группы), `is_active = tab.Id == activeTab?.Id`. Пустой список групп писать **не** надо — `StorageService.LoadSession()` вернёт `null`, и следующий запуск откроет домашнюю страницу.

`Load`: собрать `Dictionary<Guid, TabGroup>` из строк групп с `Guid.TryParse` (нераспознанные — пропустить, `ColorIndex` клампить в 0–7); затем вкладки в порядке `position`, `Guid.TryParse` строки, искать группу по `GroupId` в словаре — нет группы, строка пропускается. Активной считать первую вкладку с `IsActive = true`; если таких нет — `null`. Возвращать группы в порядке `position`.

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo`
Expected: PASS — весь набор зелёный (было 197 + новые).

- [ ] **Step 5: Коммит**

```bash
git add src/MiniBrowser/Services/SessionService.cs tests/MiniBrowser.Tests/SessionServiceTests.cs
git commit -m "Map stored session snapshot onto live tab groups"
```

---

### Task 5: `TabDropResolver` — геометрия перетаскивания

**Files:**
- Create: `src/MiniBrowser/Services/TabDropResolver.cs`
- Create: `tests/MiniBrowser.Tests/TabDropResolverTests.cs`

**Interfaces:**
- Consumes: `TabGroup`, `Tab` (Task 1), `System.Windows.Rect`/`Point` (WPF-примитивы, окно при этом не создаётся).
- Produces: `Services/TabDropResolver.cs`:
  - `public enum DropKind { None, Group, BetweenTabs }`
  - `public sealed record DropTarget(DropKind Kind, TabGroup? Group, int Index)` — `Index` осмыслен только при `BetweenTabs` (индекс вставки) и при `Group` (вставка в конец, равен `Group.Tabs.Count`).
  - `public sealed record DropColumn(TabGroup Group, Rect Bounds, bool IsCollapsed, IReadOnlyList<DropTabRow> Tabs)`
  - `public sealed record DropTabRow(Rect Bounds, Tab Tab)`
  - `public const double HeaderHeight = 28.0;`
  - `public static DropTarget Resolve(Point pointer, IReadOnlyList<DropColumn> columns)`
  - правила: нет колонок → `None`; указатель не внутри ни одной колонки (обе оси) → `None`; свёрнутая колонка или `pointer.Y < Bounds.Top + HeaderHeight` → `Group` с `Index = Tabs.Count`; иначе первый таб, у которого `pointer.Y < Bounds.Top + Bounds.Height / 2` → `BetweenTabs` с его индексом; ниже всех табов → `BetweenTabs` с `Tabs.Count`.

- [ ] **Step 1: Написать падающий тест**

`tests/MiniBrowser.Tests/TabDropResolverTests.cs`:

```csharp
using System.Windows;
using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class TabDropResolverTests
{
    private static DropColumn Column(TabGroup group, double x, int rows = 3, bool collapsed = false)
    {
        var tabs = new List<DropTabRow>();
        for (var i = 0; i < rows; i++)
        {
            var tab = new Tab();
            group.Tabs.Add(tab);
            tabs.Add(new DropTabRow(new Rect(x, 28 + i * 30, 200, 30), tab));
        }
        var height = collapsed ? 32 : 28 + rows * 30;
        return new DropColumn(group, new Rect(x, 0, 200, height), collapsed, tabs);
    }

    [Fact]
    public void NoColumns_IsNone()
    {
        Assert.Equal(DropKind.None, TabDropResolver.Resolve(new Point(10, 10), Array.Empty<DropColumn>()).Kind);
    }

    [Fact]
    public void PointerInGapBetweenColumns_IsNone()
    {
        var a = Column(new TabGroup { Name = "А" }, 0);
        var b = Column(new TabGroup { Name = "Б" }, 260);
        var result = TabDropResolver.Resolve(new Point(215, 40), new[] { a, b });
        Assert.Equal(DropKind.None, result.Kind);
        Assert.Null(result.Group);
    }

    [Fact]
    public void PointerOnHeader_DropsToGroupEnd()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        var result = TabDropResolver.Resolve(new Point(100, 10), new[] { column });
        Assert.Equal(DropKind.Group, result.Kind);
        Assert.Same(group, result.Group);
        Assert.Equal(3, result.Index);
    }

    [Fact]
    public void PointerOnCollapsedColumn_DropsToGroupEnd()
    {
        var group = new TabGroup { Name = "А", IsCollapsed = true };
        var column = Column(group, 0, collapsed: true);
        var result = TabDropResolver.Resolve(new Point(10, 15), new[] { column });
        Assert.Equal(DropKind.Group, result.Kind);
        Assert.Same(group, result.Group);
    }

    [Fact]
    public void PointerAboveFirstTabMiddle_InsertsAtZero()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        // Y = 43 — выше середины первого таба (28 + 15).
        var result = TabDropResolver.Resolve(new Point(100, 43), new[] { column });
        Assert.Equal(DropKind.BetweenTabs, result.Kind);
        Assert.Equal(0, result.Index);
    }

    [Fact]
    public void PointerBetweenTabs_InsertsAtThatIndex()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        // Y = 70 — середина второго таба (58 + 15), значит вставка перед ним.
        var result = TabDropResolver.Resolve(new Point(100, 70), new[] { column });
        Assert.Equal(DropKind.BetweenTabs, result.Kind);
        Assert.Equal(1, result.Index);
    }

    [Fact]
    public void PointerBelowLastTab_Appends()
    {
        var group = new TabGroup { Name = "А" };
        var column = Column(group, 0);
        var result = TabDropResolver.Resolve(new Point(100, 500), new[] { column });
        Assert.Equal(DropKind.BetweenTabs, result.Kind);
        Assert.Equal(3, result.Index);
    }

    [Fact]
    public void EmptyExpandedColumn_DropsToGroup()
    {
        var group = new TabGroup { Name = "Пусто" };
        var column = new DropColumn(group, new Rect(0, 0, 200, 40), false, Array.Empty<DropTabRow>());
        var result = TabDropResolver.Resolve(new Point(100, 35), new[] { column });
        Assert.Equal(DropKind.Group, result.Kind);
        Assert.Equal(0, result.Index);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что тесты падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TabDropResolverTests"`
Expected: FAIL с ошибкой компиляции `TabDropResolver` не найден.

- [ ] **Step 3: Реализовать `Services/TabDropResolver.cs`**

`Resolve` выполняет ровно три шага: найти колонку (первая, у которой `Bounds.Contains(pointer)` — при попадании в колонку, у которой `Tabs` пуст, `Kind = Group`); иначе `None`; в найденной — если `IsCollapsed` или `pointer.Y < Bounds.Top + HeaderHeight`, вернуть `Group` с `Index = column.Tabs.Count`, иначе линейно найти первый таб с `pointer.Y < row.Bounds.Top + row.Bounds.Height / 2` и вернуть `BetweenTabs` с его индексом, а если такого нет — `BetweenTabs` с `column.Tabs.Count`. Класс статический, без состояния.

- [ ] **Step 4: Запустить тесты и убедиться, что они проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~TabDropResolverTests"`
Expected: PASS, 8 тестов.

- [ ] **Step 5: Коммит**

```bash
git add src/MiniBrowser/Services/TabDropResolver.cs tests/MiniBrowser.Tests/TabDropResolverTests.cs
git commit -m "Resolve drag and drop targets from column geometry"
```

---

### Task 6: `TabManager` поверх `TabGroups`

**Files:**
- Modify: `src/MiniBrowser/Services/TabManager.cs`

**Interfaces:**
- Consumes: `TabGroups` (Task 2), `Tab`/`TabGroup` (Task 1).
- Produces: изменённая публичная поверхность `TabManager` (остальное — как было):
  - `public IReadOnlyList<TabGroup> Groups => _groups.Groups;`
  - `public TabGroup? ActiveGroup => _groups.ActiveGroup;`
  - `public IReadOnlyList<Tab> Tabs => _groups.Tabs;`
  - `public Tab NewTab(string? url = null, TabGroup? group = null)` — при `group = null` берёт `_groups.ActiveOrFirst()`, вкладку создаёт `_groups.AddTab(group)`.
  - `public TabGroup CreateGroup(string? name = null, int? colorIndex = null)`
  - `public void RenameGroup(TabGroup group, string name)`, `public void SetGroupColor(TabGroup group, int colorIndex)`
  - `public void ToggleGroupCollapsed(TabGroup group)`
  - `public void MoveTab(Tab tab, TabGroup target, int index)`
  - `public void MoveGroup(TabGroup group, int newIndex)`
  - `public void CloseGroup(TabGroup group)`
  - `public void RestoreGroups(IReadOnlyList<TabGroup> groups, Tab? activeTab)`
  - новые события: `public event Action? GroupsChanged;`, `public event Action? SessionDirty;`

- [ ] **Step 1: Переписать `TabManager`**

Один проход по файлу, правки локальные:

- поле `_groups = new TabGroups()`; старый `List<Tab> _tabs` удалить, `Tabs` отдавать из `_groups`;
- `NewTab`: `_groups.ActiveOrFirst()` → `new BrowserTabView(...)` → `_views[tab] = view; _contentHost.Children.Add(view); view.Visibility = Collapsed;` → `_groups.AddTab(group, tab)` → `ActivateTab(tab)` → навигация. Событие `TabsChanged` больше не шлётся из `NewTab` вручную: подписка `_groups.Changed += ...` заменит все прежние точки вызова, и `ActivateTab` тоже будет шлётиком.
- `CloseTab`: `_groups.RemoveTab(tab)` вместо `_tabs.RemoveAt(index)`; индекс для выбора следующей — считать через `_groups.IndexOf(tab)` **до** удаления; если активная вкладка закрыта — активировать `_groups.ActiveTab` либо ближайшую по старой позиции (`_groups.Tabs.ElementAtOrDefault(Math.Min(index, count - 1))`).
- `CloseGroup(group)`: снимок вкладок `group.Tabs.ToArray()`, для каждой `CloseTab`, затем `_groups.RemoveGroup(group)` и `GroupsChanged?.Invoke()`.
- `MoveTab`/`MoveGroup`/`RenameGroup`/`SetGroupColor`/`ToggleGroupCollapsed`/`CreateGroup` — делегирующие обёртки над `_groups` с инвалидацией кэша полосы (`TabsChanged`).
- `RestoreGroups(groups, activeTab)`: для каждого `Tab` во всех группах создать `BrowserTabView`, положить в `_views` и `_contentHost.Children`, `Visibility = Collapsed`; активировать `activeTab ?? первый непустой список[0]`, если он есть. Навигацию вызвать **только** для активной — остальные движки поднимутся лениво при активации.
- В конструкторе подписаться: `_groups.Changed += () => { TabsChanged?.Invoke(); SessionDirty?.Invoke(); };` и `_groups.GroupsChanged += () => { GroupsChanged?.Invoke(); SessionDirty?.Invoke(); };`, `_groups.ActiveChanged += t => OnGroupsActiveChanged(t);` — последний обязан звать `ActiveTabChanged` и обновлять `ActiveTab`, чтобы старые места (`OnActiveTabChanged` в окне, усыпление) не сломались.

- [ ] **Step 2: Собрать проект**

Run: `dotnet build src/MiniBrowser/MiniBrowser.csproj -c Release -v q --nologo`
Expected: ошибки только в `MainWindow.xaml.cs` (старые обращения к `Tabs`/`NewTab`) и в `TabStripView`; их правка — Task 7. Если ошибки в самом `TabManager` — исправить здесь.

- [ ] **Step 3: Коммит**

Коммит делать только когда проект собирается целиком, поэтому **Task 6 и Task 7 коммитятся вместе** (см. конец Task 7). Промежуточный коммит не делать.

---

### Task 7: Вертикальная полоса групп в разметке

**Files:**
- Create: `src/MiniBrowser/Views/GroupColumnView.xaml`
- Create: `src/MiniBrowser/Views/GroupColumnView.xaml.cs`
- Modify: `src/MiniBrowser/Views/TabStripView.xaml`
- Modify: `src/MiniBrowser/Views/TabStripView.xaml.cs`
- Modify: `src/MiniBrowser/MainWindow.xaml`
- Modify: `src/MiniBrowser/MainWindow.xaml.cs`
- Modify: `src/MiniBrowser/App.xaml`


**Interfaces:**
- Consumes: `TabManager.Groups`/`ActiveGroup`/`GroupsChanged`, `TabGroup`/`Tab`, константа `TabDropResolver.HeaderHeight`.
- Produces:
  - `GroupColumnView`:
    - `public static readonly DependencyProperty GroupProperty` (`TabGroup`), `public static readonly DependencyProperty IsActiveGroupProperty` (`bool`), `public static readonly DependencyProperty IsDropTargetProperty` (`bool`).
    - события: `public event Action<TabGroup>? TabActivated;`, `TabClosed`, `GroupCloseRequested`, `GroupRenameRequested`, `GroupColorRequested`, `GroupCollapseToggled`, `GroupMoveRequested`, `TabMoveRequested`.
    - `AllowDrop = true`, `DragOver`/`Drop` публичные обработчики, которые шлют `TabMoveRequested(tab, pointerInPanelCoordinates)` — координаты пересчитываются в `TabStripView`.
  - `TabStripView`:
    - `public IEnumerable<TabGroup>? Groups { set; }` — присваивает источник `ItemsControl` колонок; вызывающий обязан присваивать новую коллекцию при каждом изменении, как сейчас делал с `Items`.
    - `public void SetDropHint(TabGroup? group)` — подсветка колонки-цели.
    - `public Point ToPanelPoint(TabGroup group, Point pointInColumn)` — перевод координаты из колонки в координаты панели для `TabDropResolver`.
    - события прежние: `TabActivated`, `TabCloseRequested`, `NewTabRequested`, плюс `GroupCreateRequested`, `GroupCloseRequested`, `GroupRenameRequested`, `GroupColorRequested`, `GroupCollapseToggled`, `GroupMoveRequested`, `TabMoveRequested`.

- [ ] **Step 1: Добавить палитру групп и стили в `App.xaml`**

В блок кистей добавить массив из восьми `SolidColorBrush` с ключами `GroupColor0`…`GroupColor7` — цвета различимы на тёмном фоне и не совпадают с акцентом: `#FF7A5C8E` (фиолетовый), `#FF4C8D7B` (бирюзовый), `#FF8A6D3B` (охра), `#FF5B7FB0` (синий), `#FFA85B5B` (красный), `#FF6F8F3F` (зелёный), `#FF8C5B8E` (магента), `#FF4E7C96` (сталь). В `Application.Resources` после существующих стилей добавить стили: `GroupColumnSurface` (фон колонки `B.TabIdle`, `CornerRadius` 7), `GroupHeaderText`, `GroupTabRow`, `GroupBadge`, `SplitThumb` (`Width` 4, фон прозрачный, триггер на `IsMouseOver` → `T.Accent`).

- [ ] **Step 2: Создать `Views/GroupColumnView.xaml`**

`UserControl` с корневым `Border` (`Style="{StaticResource GroupColumnSurface}"`, `AllowDrop="True"`, `DragOver`/`Drop`/`DragLeave`), внутри `StackPanel`: заголовок (`Grid` высотой 28: кнопка сворачивания с `Content="▾"`, `TextBlock` имени с `MouseLeftButtonUp` для double-click, `TextBlock` бейджа с `Text="{Binding Count}"`, кнопка `✕`) и `ItemsControl` вкладок с вертикальным `StackPanel`, у которого `Visibility` переключается `DataTrigger` по `IsCollapsed`. Имя и бейдж в развёрнутом виде пишутся вертикально: тот же `Grid` внутри `RotateTransform` с `RenderTransformOrigin="0,0.5"`. Привязки — к `GroupProperty`; триггеры `IsDropTargetProperty` задают рамку `T.Accent`.

- [ ] **Step 3: Создать `Views/GroupColumnView.xaml.cs`**

Только свойства, события и три обработчика (`Tab_Click`, `Close_Click`, `Collapse_Click`, `DragOver`, `Drop`, `DragLeave`). Обработчики мыши отсекают попадания в кнопки тем же приёмом, что уже есть в `TabStripView.IsOverButton` — метод переносится сюда (статический, `internal`). `DragOver` шлёт событие подсветки и `e.Effects = DragDropEffects.Move` только когда источник — `Tab`, иначе `e.Effects = None`. `Drop` шлёт `TabMoveRequested` с `e.GetPosition(this)`.

- [ ] **Step 4: Переписать `Views/TabStripView.xaml`**

`UserControl` без рамки; внутри `DockPanel`: сверху кнопка «+ Группа» (`GroupCreateRequested`) и кнопка «+ Вкладка» (`NewTabRequested`); снизу `TextBlock` с подсказкой «Перетащите вкладку на группу», `Visibility` через `DataTrigger` на пустой `Groups` (для этого достаточно `x:Name` и привязки к коллекции с `TargetNullValue`/`TargetType`; если выйдет неудобно — оставить подсказку всегда видимой, но приглушённой). По центру `ScrollViewer` (`VerticalScrollBarVisibility="Auto"`) с `ItemsControl` колонок, у которого `ItemsPanel` — `WrapPanel`.

- [ ] **Step 5: Переписать `Views/TabStripView.xaml.cs`**

Оставить `Tab_Click`/`Close_Click`/создание колонок (через `DataTemplate` с `GroupColumnView` и привязкой `IsActiveGroup` к `IsActiveGroup` первого таба группы не привязывать — вместо этого сравнивать в code-behind и выставлять `SetDropHint`/`IsActiveGroup` при перерисовке). Добавить: `DragStart` на строке вкладки с порогом 5 px, `DragOver`/`Drop` на самой панели (для перетаскивания колонок), `ToPanelPoint`, `SetDropHint`, `SetActiveGroup`.

- [ ] **Step 6: Перевести `MainWindow.xaml` на две колонки**

Тулбар (`Grid.Row=1`) остаётся на всю ширину; строка 2 становится `Grid` с `ColumnDefinitions` `Auto` и `*`: в колонке 0 — `views:TabStripView x:Name="TabStrip"` с `MinWidth="150" MaxWidth="420"` и `Grid.ColumnSpan`-разделителем: `Thumb` из стиля `SplitThumb` шириной 4 px в той же колонке не помещается — положить разделитель как `Grid.Column=1` с `Width=4` **перед** `ContentHost` и `ContentHost` сдвинуть в колонку 2: то есть определить колонки `Auto`, `Auto`, `*`, где 0 — полоса, 1 — `Thumb`, 2 — контент. `ContentHost`, `DrawerPopup` и `StatusBarBorder` переезжают в колонку 2; `PlacementTarget` у Popup остаётся `ContentHost`.

- [ ] **Step 7: Перевести `MainWindow.xaml.cs`**

- `RefreshTabStrip` → `TabStrip.Groups = _tabManager.Groups.ToList();`
- подписать `_tabManager.GroupsChanged += RefreshTabStrip;`
- подписать новые события полосы: `GroupCreateRequested` → `_tabManager.CreateGroup()`; `GroupCloseRequested` → `CloseGroup`; `GroupRenameRequested` → начать переименование в колонке (метод колонки `BeginRename()`); `GroupColorRequested` → контекстное меню с палитрой (собирается в `TabStripView` как `ContextMenu` из 8 `MenuItem`); `GroupCollapseToggled` → `_tabManager.ToggleGroupCollapsed`; `GroupMoveRequested`/`TabMoveRequested` → без обработчика до Task 8 (подписки появятся там);
- `Thumb` перетаскивания: `DragDelta` → `TabStrip.Width = Math.Clamp(предыдущее + delta.HorizontalChange, 150, 420)`, результат писать в `_settings.Current.TabStripWidth` и `_settings.Save()` по `MouseUp` (не на каждом `DragDelta`, иначе файл настроек заспамится);
- в конструкторе задать стартовую ширину `TabStrip.Width = _settings.Current.TabStripWidth`.

- [ ] **Step 8: Добавить `TabStripWidth` в `Models/AppSettings.cs` и в `ResetToDefaults`/`Validate`**

`public double TabStripWidth { get; set; } = 240;` — в `Services/SettingsService.Validate` клампить в 150–420 через существующий `Normalize` (fallback 240), в `ResetToDefaults` присвоить из дефолтов.

- [ ] **Step 9: Собрать проект**

Run: `dotnet build src/MiniBrowser/MiniBrowser.csproj -c Release -v q --nologo`
Expected: `Сборка успешно завершена. Предупреждений: 0`.

- [ ] **Step 10: Прогнать тесты**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo`
Expected: PASS, весь набор зелёный.

- [ ] **Step 11: Коммит**

```bash
git add src/MiniBrowser/Views/GroupColumnView.xaml src/MiniBrowser/Views/GroupColumnView.xaml.cs src/MiniBrowser/Views/TabStripView.xaml src/MiniBrowser/Views/TabStripView.xaml.cs src/MiniBrowser/MainWindow.xaml src/MiniBrowser/MainWindow.xaml.cs src/MiniBrowser/App.xaml src/MiniBrowser/Models/AppSettings.cs src/MiniBrowser/Services/SettingsService.cs
git commit -m "Show tabs as vertical group columns with a resizable strip"
```

---

### Task 8: Перетаскивание вкладок и контекстные меню

**Files:**
- Modify: `src/MiniBrowser/Views/TabStripView.xaml.cs`
- Modify: `src/MiniBrowser/Views/GroupColumnView.xaml.cs`
- Modify: `src/MiniBrowser/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `TabDropResolver.Resolve` (Task 5), `TabManager.MoveTab`/`MoveGroup`/`CloseGroup`/`RenameGroup`/`SetGroupColor` (Task 6), события `TabStripView` (Task 7).
- Produces: `TabStripView.TabMoveRequested` (`Action<Tab, Point>` — вкладка и позиция в координатах панели), `TabStripView.GroupMoveRequested` (`Action<TabGroup, int>`); `GroupColumnView.TabMovedInGroup` (`Action<Tab, int>` — вставка на позицию внутри своей группы).

- [ ] **Step 1: Старт перетаскивания вкладки**

В `TabStripView`: `PreviewMouseLeftButtonDown` на строке вкладки запоминает точку и объект `Tab`; `PreviewMouseMove` при `LeftButton == Pressed` и расстоянии от точки > 5 px вызывает `DragDrop.DoDragDrop(rowElement, tab, DragDropEffects.Move)` и после возврата проверяет, что вкладка жива (`TabManager` зовёт `Groups.Tab`), — иначе ничего не делает. Клик без перемещения остаётся активацией вкладки.

- [ ] **Step 2: Сбор геометрии и вызов резолвера**

В `GroupColumnView` — публичный `IReadOnlyList<DropColumn> BuildDropGeometry(Point panelOffset)`: `Rect` колонки и строк берутся через `TransformToAncestor`/`TranslatePoint`, `IsCollapsed` — из `GroupProperty`. В `TabStripView.DragOver` при `e.Data.GetData(typeof(Tab)) is Tab tab`: собрать геометрию всех колонок, вызвать `TabDropResolver.Resolve(e.GetPosition(this), columns)`, подсветить колонку-цель через `SetDropHint` (для `BetweenTabs` — колонку, где появится линия вставки), `e.Effects = Move` при `Kind != None` иначе `None`. В `Drop` — тот же резолвер и `TabMoveRequested(tab, position)`; `DragLeave` и `Drop` вне панели обязаны гасить подсветку (`SetDropHint(null)`).

- [ ] **Step 3: Применить перемещение в `MainWindow`**

`TabStrip.TabMoveRequested += (tab, point) => { var columns = TabStrip.BuildColumnsGeometry(); var target = TabDropResolver.Resolve(point, columns); if (target.Kind == DropKind.None) return; if (target.Kind == DropKind.Group) _tabManager.MoveTab(tab, target.Group!, target.Group!.Tabs.Count); else _tabManager.MoveTab(tab, target.Group!, target.Index); };` — и `_tabManager.MoveTab` сам разошлёт `TabsChanged` и `SessionDirty`. `TabStripView.BuildColumnsGeometry()` — публичный метод, собирающий геометрию всех колонок относительно панели; `MainWindow` ничего не измеряет сам.

- [ ] **Step 4: Перетаскивание колонок групп**

`GroupColumnView` — `PreviewMouseLeftButtonDown` на заголовке группы с тем же порогом 5 px, `DoDragDrop(group, DragDropEffects.Move)`; `TabStripView` на `DragOver`/`Drop` определяет, между какими колонками курсор (по `X` центра каждой колонки), подсвечивает линию вставки и шлёт `GroupMoveRequested(group, index)`.

- [ ] **Step 5: Контекстные меню**

В `GroupColumnView` для вкладки: `ContextMenu` с «Переместить в группу» → подменю из групп (`ItemsSource` — список групп, с галочкой на текущей; выбор шлёт `TabMoveRequested`), «Создать группу из вкладки» (шлёт `TabCreateGroupRequested` — `MainWindow` создаёт группу и переносит в неё вкладку), «Убрать из группы» (шлёт `TabLeaveGroupRequested` — `MainWindow` переносит вкладку в соседнюю группу, а если групп больше нет — в `CreateGroup()`), «Закрыть вкладку». Для заголовка: «Переименовать» (`GroupRenameRequested`), «Сменить цвет» → 8 пунктов палитры (`GroupColorRequested` с индексом), «Свернуть»/«Развернуть» (`GroupCollapseToggled`), «Закрыть группу» (`GroupCloseRequested`). Меню собираются кодом (список групп динамический) и используют существующий стиль `ContextMenu`/`MenuItem` из `App.xaml`.

- [ ] **Step 6: Собрать и прогнать тесты**

Run: `dotnet build src/MiniBrowser/MiniBrowser.csproj -c Release -v q --nologo`, затем `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo`
Expected: сборка без предупреждений, тесты PASS.

- [ ] **Step 7: Ручная проверка (обязательно, окно)**

Запустить `src/MiniBrowser/bin/Release/net8.0-windows/win-x64/MiniBrowser.exe` и проверить: перетаскивание вкладки в другую группу и обратно; вставка между соседними вкладками; перетаскивание колонки группы; отмена перетаскивания за пределы панели; контекстные меню вкладки и заголовка; сворачивание и обратно; кнопка ✕ группы.

- [ ] **Step 8: Коммит**

```bash
git add src/MiniBrowser/Views/TabStripView.xaml.cs src/MiniBrowser/Views/GroupColumnView.xaml.cs src/MiniBrowser/MainWindow.xaml.cs
git commit -m "Drag tabs between groups and add tab and group menus"
```

---

### Task 9: Стартовое состояние, настройка и запись сессии

**Files:**
- Create: `src/MiniBrowser/Services/StartupPlan.cs`
- Modify: `src/MiniBrowser/Services/SessionService.cs` (`EmptySnapshot` уже есть — не ломать)
- Modify: `src/MiniBrowser/Services/Hotkeys.cs` (`IBrowserActions`)
- Modify: `src/MiniBrowser/Models/AppSettings.cs` (`RestoreSession`)
- Modify: `src/MiniBrowser/Services/SettingsService.cs` (`ResetToDefaults`)
- Modify: `src/MiniBrowser/Views/MenuDrawerView.xaml`, `src/MiniBrowser/Views/MenuDrawerView.xaml.cs`
- Modify: `src/MiniBrowser/MainWindow.xaml.cs`
- Create: `tests/MiniBrowser.Tests/StartupPlanTests.cs`
- Modify: `tests/MiniBrowser.Tests/HotkeysTests.cs`

**Interfaces:**
- Consumes: `SessionService.Load`/`Save` (Task 4), `TabManager.RestoreGroups` (Task 6), `StorageService` (Task 3).
- Produces:
  - `Services/StartupPlan.cs`:
    - `public sealed record StartupPlan(SessionSnapshot? Snapshot)` — `Snapshot` `null` означает «открыть домашнюю страницу».
    - `public static StartupPlan Resolve(bool restoreSessionEnabled, SessionSnapshot? stored)`.
  - `AppSettings.RestoreSession` (bool, по умолчанию `true`).
  - `IBrowserActions`: `void NewGroup();`, `void RenameActiveGroup();`.

- [ ] **Step 1: Написать падающий тест для `StartupPlan`**

`tests/MiniBrowser.Tests/StartupPlanTests.cs`:

```csharp
using MiniBrowser.Models;
using MiniBrowser.Services;
using Xunit;

public class StartupPlanTests
{
    private static SessionSnapshot Stored() => new(
        new[] { new SessionGroupRow("g1", "А", 0, false, 0) },
        new[] { new SessionTabRow("t1", "g1", "https://a.example/", "А", true, 0) });

    [Fact]
    public void RestoreEnabled_AndStored_ReturnsSnapshot()
    {
        Assert.NotNull(StartupPlan.Resolve(true, Stored()).Snapshot);
    }

    [Fact]
    public void RestoreEnabled_ButNothingStored_OpensHomeUrl()
    {
        Assert.Null(StartupPlan.Resolve(true, null).Snapshot);
    }

    [Fact]
    public void RestoreDisabled_IgnoresStored()
    {
        Assert.Null(StartupPlan.Resolve(false, Stored()).Snapshot);
    }

    [Fact]
    public void StoredWithoutTabs_RestoresEmptyGroup()
    {
        var snapshot = new SessionSnapshot(
            new[] { new SessionGroupRow("g1", "Пусто", 0, false, 0) },
            Array.Empty<SessionTabRow>());
        Assert.NotNull(StartupPlan.Resolve(true, snapshot).Snapshot);
    }
}
```

- [ ] **Step 2: Запустить и убедиться, что тесты падают**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~StartupPlanTests"`
Expected: FAIL с ошибкой компиляции `StartupPlan` не найден.

- [ ] **Step 3: Реализовать `Services/StartupPlan.cs`**

`Resolve` возвращает `new StartupPlan(restoreSessionEnabled ? stored : null)`. Пустой `stored` (`Groups.Count == 0`) считать отсутствующим: `stored is { Groups.Count: > 0 } ? stored : null`.

- [ ] **Step 4: Расширить `IBrowserActions` и `Hotkeys`**

Добавить в интерфейс `NewGroup()` и `RenameActiveGroup()`. В `TryHandle(Key, ModifierKeys, IBrowserActions)`: в ветку `Control | Shift` рядом с `Tab` и `B` добавить `case Key.N: actions.NewGroup(); return true;` и `case Key.G: actions.RenameActiveGroup(); return true;`. Перегрузка для `AcceleratorKeyPressed` правил не требует — она зовёт ту же.

- [ ] **Step 5: Дописать тесты хоткеев**

В `tests/MiniBrowser.Tests/HotkeysTests.cs` добавить кейсы по образцу существующих: `Ctrl+Shift+N` вызывает `NewGroup` ровно один раз; `Ctrl+Shift+G` вызывает `RenameActiveGroup`; `Ctrl+T` по-прежнему вызывает `NewTab` и **не** `NewGroup`.

- [ ] **Step 6: Запустить и убедиться, что тесты проходят**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo --filter "FullyQualifiedName~HotkeysTests|FullyQualifiedName~StartupPlanTests"`
Expected: PASS.

- [ ] **Step 7: Добавить `RestoreSession` в настройки и панель**

`Models/AppSettings.cs`: `public bool RestoreSession { get; set; } = true;` с комментарием «по умолчанию восстанавливаем раскладку групп». `Services/SettingsService.ResetToDefaults`: `settings.RestoreSession = defaults.RestoreSession;`. `Views/MenuDrawerView.xaml`: `CheckBox` с `Content="Восстанавливать вкладки и группы при запуске"`, `IsChecked="{Binding Settings.Current.RestoreSession, Mode=TwoWay}"`, `Click="RestoreSessionBox_Click"` — по образцу `StatusBarBox`. `Views/MenuDrawerView.xaml.cs` в обработчике звать `Save()` и `NotifySettingsChanged()`.

- [ ] **Step 8: Переписать стартовую логику `MainWindow.OnContentRendered`**

Чтение ровно одно: `SessionService.Load()` уже читает БД и возвращает и снимок, и собранные группы, поэтому решение принимается по его результату:

```csharp
var (stored, groups, activeTab) = _session.Load();
var plan = StartupPlan.Resolve(_settings.Current.RestoreSession, stored);
if (plan.Snapshot is not null)
{
    _tabManager.RestoreGroups(groups, activeTab);
}
else
{
    _tabManager.NewTab(_settings.Current.HomeUrl);
}
```

Затем URL'ы из аргументов — **после** восстановления, каждая новой вкладкой в конце активной группы:

```csharp
foreach (var raw in _startupUrls)
{
    var url = NavigationService.BuildUrl(raw, _settings.Current.SearchUrl) ?? raw;
    _tabManager.NewTab(url);
}
```

Прежний блок с `_tabManager.ActivateTab(_tabManager.Tabs[0])` в конце восстановления убрать: `RestoreGroups` сам активирует сохранённую вкладку, а лишняя активация первой вкладки перебивала бы её. Фильтр загрузки (`_ = LoadFilterListAsync()`) и его комментарий оставлять до этого кода как есть.

- [ ] **Step 9: Подключить запись сессии с дебаунсом**

В `MainWindow`: поле `_session = new SessionService(_storage)` и `DispatcherTimer _sessionTimer` (`Interval = TimeSpan.FromSeconds(2)`), `_tabManager.SessionDirty += () => _sessionTimer.Start();`, `Tick` → `Stop()` и `SaveSessionNow()`; `SaveSessionNow()` — `if (_sessionTimer.IsEnabled) _sessionTimer.Stop(); _session.Save(_tabManager.Groups, _tabManager.ActiveTab);` плюс тихое «Сессия не восстановлена» в `StatusText`, если план показал пустое хранилище при `RestoreSession = true`. `Window_Closing` вызывает `SaveSessionNow()` до `_settings.Save()`. Восстановленную раскладку тоже надо сохранить сразу, чтобы прерванный запуск не потерял её: `SaveSessionNow()` в конце `OnContentRendered`.

- [ ] **Step 10: Реализовать действия групп в `MainWindow`**

`IBrowserActions.NewGroup()` → `var group = _tabManager.CreateGroup(); _tabManager.NewTab(_settings.Current.HomeUrl, group); RefreshTabStrip();`. `IBrowserActions.RenameActiveGroup()` → `TabStrip.BeginRename(ActiveGroup)`.

- [ ] **Step 11: Собрать и прогнать тесты**

Run: `dotnet build src/MiniBrowser/MiniBrowser.csproj -c Release -v q --nologo`, затем `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo`
Expected: сборка без предупреждений, тесты PASS.

- [ ] **Step 12: Ручная проверка (обязательно, окно)**

Проверить цикл целиком: открыть несколько групп и вкладок → закрыть окно → запустить снова (ожидание: та же раскладка, те же имена, цвета и свёрнутость, активна та же вкладка); снять галочку восстановления в настройках → перезапустить (ожидание: одна вкладка `HomeUrl`, старая сессия перезаписана); вернуть галочку → перезапустить (ожидание: снова одна вкладка, а не старая раскладка).

- [ ] **Step 13: Коммит**

```bash
git add src/MiniBrowser/Services/StartupPlan.cs src/MiniBrowser/Services/SessionService.cs src/MiniBrowser/Services/Hotkeys.cs src/MiniBrowser/Models/AppSettings.cs src/MiniBrowser/Services/SettingsService.cs src/MiniBrowser/Views/MenuDrawerView.xaml src/MiniBrowser/Views/MenuDrawerView.xaml.cs src/MiniBrowser/MainWindow.xaml.cs tests/MiniBrowser.Tests/StartupPlanTests.cs tests/MiniBrowser.Tests/HotkeysTests.cs tests/MiniBrowser.Tests/SessionServiceTests.cs
git commit -m "Restore tab groups on startup and debounce session saving"
```

---

### Task 10: Финальная сборка, проверка и пуш

**Files:** нет изменений кода, только проверки.

**Interfaces:**
- Consumes: всё.
- Produces: зелёная сборка Release, зелёные тесты, ветка запушена в `origin`.

- [ ] **Step 1: Полная сборка решения**

Run: `dotnet build MiniBrowser.sln -c Release -v q --nologo`
Expected: `Сборка успешно завершена. Предупреждений: 0. Ошибок: 0`.

- [ ] **Step 2: Все тесты**

Run: `dotnet test tests/MiniBrowser.Tests/MiniBrowser.Tests.csproj -c Release --nologo`
Expected: `Пройдено` — не пройдено 0.

- [ ] **Step 3: Ручная проверка сквозного сценария**

Запустить собранный `MiniBrowser.exe` и пройти: создать три группы с разными именами и цветами, свернуть одну, перетащить вкладки между группами и внутри, переименовать через двойной клик и `Ctrl+Shift+G`, `Ctrl+Shift+N`, закрыть окно, запустить снова — раскладка совпадает; затем `Ctrl+Tab` через свёрнутую группу разворачивает её.

- [ ] **Step 4: Закоммитить и запушить**

```bash
git status --short
git add -u
git commit -m "Finalise tab groups build"   # только если есть изменения
git push origin main
```