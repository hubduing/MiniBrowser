# Меню-панель браузера: закладки, история, настройки

Дата: 2026-10-05
Статус: утверждён к реализации

## Цель

Собрать единое меню браузера вместо текущего выпадающего списка. Панель даёт
доступ ко всем закладкам, ко всей истории посещений и к базовым настройкам.

Успех считается достигнутым, если из одного меню можно открыть любую закладку
или любой ранее посещённый сайт, изменить настройки, и всё это переживает
перезапуск браузера.

## Границы

Входит: боковая панель (drawer) с тремя разделами; полные списки закладок и
истории с поиском; удаление записей; очистка истории; базовые настройки с
сохранением.

Не входит: папки закладок, переименование записей, импорт/экспорт, синхронизация,
светлая тема, расширенные настройки прокси/cookie/движка.

## Решения

| Решение | Выбор | Почему |
|---|---|---|
| Форма меню | Drawer поверх страницы | Много места для длинных списков, не перекрывает общение со страницей по ширине |
| Объём настроек | Расширенный набор (без переключателя темы) | Тема зашита в `App.xaml` на тёмные кисти; вынесение в ResourceDictionary — отдельная задача |
| Управление списками | Удаление и поиск | «Все закладки» без удаления — просто список, в котором ничего нельзя исправить |
| Архитектура | MVVM + `ICollectionView` | Новый крупный кусок UI с тремя независимыми разделами; вложение в MVVM вместо роста `ToolbarView` |
| История | Дедуп по URL, счётчик визитов и дата последнего | Список из 1000 посещений одного сайта нечитаем |
| Тесты | Новый xUnit-проект | Ядро (настройки и запросы БД) тестируется без WPF-окна |
| Связь вкладки с настройками | Делегат `Func<double>` | `BrowserTabView` не знает про файл настроек |

## Архитектура

```
MainWindow.xaml
 └─ Grid (4 строки)
     ├─ TabStripView
     ├─ ToolbarView         ☰ → ToggleDrawerRequested
     ├─ ContentHost         вкладки
     ├─ DrawerHost (Grid, ZIndex=100)      ← НОВОЕ, поверх содержимого
     │   ├─ Затемнение (0.5 чёрного), клик = закрыть
     │   └─ MenuDrawerView (360px, TranslateTransform -360 → 0)
     └─ StatusBar
```

Drawer — часть визуального дерева окна, а не `Popup`. `Popup` у WPF живёт в
отдельном окне, где анимация входа-выхода и затемнение поверх `WebView2`
работают непредсказуемо. Обычная `Grid` поверх содержимого даёт
предсказуемый результат, а `TranslateTransform` — анимацию без кода анимации.

`MenuDrawerView` — `UserControl`, `DataContext` = `MenuDrawerViewModel`. Внутри
`TabControl` из трёх вкладок:

| Вкладка | Содержимое |
|---|---|
| Закладки | поле поиска, `ListBox`, ✕ на элементе, счётчик «12 из 84» |
| История | то же, плюс «Очистить историю» внизу |
| Настройки | форма, 8 пунктов, помещается без прокрутки |

## Поток данных

`StorageService` — единственный источник правды для обоих списков. Существующие
`AddHistory`, `AddBookmark`, `GetRecentHistory` не меняются. Добавляются:

```csharp
List<Bookmark>    GetAllBookmarks();              // без обрезки до 12
List<HistoryEntry> GetHistory(int limit = 200);   // дедуп по URL
List<HistoryEntry> SearchHistory(string query, int limit = 200);
void               DeleteBookmark(string url);
void               ClearHistory();
```

`HistoryEntry` расширяется полями `VisitCount` и `LastVisit`. Существующий
`GetRecentHistory` продолжает работать и заполняет их: `VisitCount` — 1,
`LastVisit` — `visited_at` этой строки. Старое меню, если его ещё открыть,
покажет корректные данные без дополнительных правок.

`MenuDrawerViewModel`:

- `ObservableCollection<BookmarkItem> Bookmarks`
- `ObservableCollection<HistoryItem> History`
- команды `OpenUrl`, `DeleteBookmark`, `DeleteHistory`, `ClearHistory`, `ToggleDrawer`
- поиск через `ICollectionView.Filter`, обновление по событию `SearchText`

Фильтрация — штатным `ICollectionView`, а не ручным перебором: WPF уже умеет
пересчитывать видимость при изменении коллекции, свой фильтр дублировал бы
движок. На 1000 записей пересчёт мгновенный.

Открытие закладки: `OpenUrl` → событие `NavigateRequested(string)` →
`MainWindow` → `_tabManager.NavigateActive(url)`. Тот же путь, что у адресной
строки, и то же поведение, что у клика по закладке в старом меню. Панель
закрывается.

## Настройки

```csharp
public sealed class AppSettings
{
    public double ZoomPercent     { get; set; } = 100;   // 50..200
    public double WindowWidth      { get; set; } = 1200;
    public double WindowHeight     { get; set; } = 800;
    public bool   WindowMaximized  { get; set; }
    public string HomeUrl          { get; set; } = "https://www.google.com/";
    public string SearchUrl        { get; set; } = "https://www.google.com/search?q={0}";
    public bool   ShowStatusBar    { get; set; } = true;
    public double DefaultFontSize  { get; set; } = 16;   // px, множитель к масштабу
}
```

`SettingsService` — загрузка и запись `%LOCALAPPDATA%\MiniBrowser\settings.json`
через `System.Text.Json`.

- Повреждённый файл не роняет браузер: десериализация под `try/catch`, при
  ошибке возвращаются значения по умолчанию. Та же philosophy, что в
  `StorageService`.
- `Validate()` после загрузки: значения вне диапазона молча приводятся к
  допустимым. Иначе битый JSON даст невидимое окно.
- Запись на каждое изменение — файлы крошечные. `WindowWidth`, `WindowHeight`,
  `WindowMaximized` пишутся только в `Window_Closing`, иначе перетаскивание окна
  заспамит диск.

### Пункты в UI

| Пункт | Контрол | Применение |
|---|---|---|
| Масштаб страницы | Slider 50–200 + поле % | `CoreWebView2.ZoomFactor` |
| Размер окна | пресеты 1024×640 / 1200×800 / 1600×1000 | `Width`, `Height` |
| Запомнить размер | CheckBox | `Window_Closing` |
| Домашняя страница | TextBox | `HomeUrl` в `OnContentRendered` |
| Поисковая система | ComboBox: Google / Bing / DuckDuckGo / Яндекс | `NavigationService.BuildUrl` |
| Размер шрифта | ComboBox: мелкий / обычный / крупный → 13 / 16 / 20 px | множитель `ZoomFactor` |
| Статусная строка | CheckBox | `StatusBar.Visibility` |
| Данные | «Очистить историю», «Сбросить настройки» | `StorageService`, `SettingsService` |

### Связка масштаба и размера шрифта

`ZoomFactor` в WebView2 — множитель поверх системного масштаба. Чтобы «Масштаб
страницы» и «Размер шрифта» не перемножались в непредсказуемый итог,
`BrowserTabView` получает один итоговый множитель:

```
итог = ZoomPercent / 100 * DefaultFontSize / 16
```

Оба контрола показывают свой компонент, пользователь не видит произведения.

### Точки применения маштата

Их три, и пропуск любой означает «масштаб работает через раз»:

1. `BrowserTabView.ApplyZoom(double)` — на уже созданном движке, после
   `EnsureCoreWebView2Async`.
2. Новая вкладка — масштаб применяется при создании движка, иначе вкладка
   откроется в 100% до первого перехода.
3. Уснувшие вкладки — `Sleep()` уничтожает движок, поэтому при пробуждении
   масштаб применяется заново.

Связь с настройками идёт через делегат `Func<double> zoomProvider`, который
`TabManager` передаёт в `BrowserTabView`. Вкладка не знает про файл настроек.

## Краевые случаи

- **Полноэкранный режим.** `ApplyFullscreen` скрывает `Toolbar`, `TabStrip`,
  `StatusText`. Затемнение и drawer добавляются в тот же список, иначе поверх
  видео останется тёмная полоса.
- **Esc.** `CloseDrawer` добавляется в `IBrowserActions`. `Esc` перехватывается
  только когда панель открыта — иначе он перестанет работать для выхода из
  F11 и HTML5-полноэкранного видео.
- **Хоткеи.** `Ctrl+M` — меню, `Ctrl+H` — история, `Ctrl+Shift+B` — закладки.
  Свободны в текущем `Hotkeys`.
- **Домашняя страница.** Константа `HomeUrl` в `MainWindow` заменяется значением
  из настроек. Значение по умолчанию совпадает, поведение при первом запуске
  не меняется.
- **Поиск при большой истории.** `StorageService` подрезает историю до 1000
  записей каждые 50 записей — потолок сохраняется. `LIKE` по 1000 строк SQLite
  отрабатывает быстро.
- **Пустой ввод в поиске** снимает фильтр и показывает полный список, а не пустоту.
- **Недоступность БД.** Список пуст, поиск не падает, разделы показывают «пусто»,
  настройки открываются. Кнопки удаления отключены.
- **Заголовки** обрезаются до 200 символов, как сейчас в `Trim`.

## Тестирование

Проект `tests/MiniBrowser.Tests`, xUnit, `net8.0-windows`, ссылка на основной
проект. Тестируется только то, что не требует WPF-окна.

| Тест | Что проверяет |
|---|---|
| `SettingsService_SaveAndLoad_RoundTrips` | значения переживают цикл записи/чтения |
| `SettingsService_CorruptJson_FallsBackToDefaults` | битый файл даёт дефолты, а не исключение |
| `SettingsService_Validate_ClampsOutOfRange` | `ZoomPercent = 500` → 200 |
| `SettingsService_MissingFile_ReturnsDefaults` | первый запуск, файла нет |
| `StorageService_AddAndGetAllBookmarks` | полный список без обрезки до 12 |
| `StorageService_GetHistory_DedupesByUrl` | 3 визита на один URL → одна строка со счётчиком 3 |
| `StorageService_SearchHistory_FindsByTitleAndUrl` | поиск по обоим полям |
| `StorageService_DeleteBookmark_And_ClearHistory` | удаление точечное, очистка полная |

`StorageService` получает необязательный параметр пути к файлу БД, по умолчанию
`%LOCALAPPDATA%`. Это единственная правка его конструктора.

## Файлы

Новые:

- `Models/AppSettings.cs`
- `Services/SettingsService.cs`
- `ViewModels/RelayCommand.cs`
- `ViewModels/MenuDrawerViewModel.cs`
- `Views/MenuDrawerView.xaml` и `.xaml.cs`
- `Converters/CountToVisibilityConverter.cs`
- `tests/MiniBrowser.Tests/*`

Изменяемые:

- `Services/StorageService.cs` — новые методы, путь к БД в конструкторе
- `Services/NavigationService.cs` — поисковая система из настроек
- `Services/Hotkeys.cs` — `Ctrl+M`, `Ctrl+H`, `Ctrl+Shift+B`, Esc закрывает панель
- `Views/ToolbarView.xaml` / `.xaml.cs` — убрать `ContextMenu`, добавить `ToggleDrawerRequested`
- `MainWindow.xaml` — строка `DrawerHost`
- `MainWindow.xaml.cs` — создание VM, применение настроек, Esc/F11
- `App.xaml` — стили `ListBox` и `TabControl` под тёмную тему
- `Models/HistoryEntry.cs` — поля `VisitCount` и `LastVisit` для дедупа истории
- `MiniBrowser.sln` — добавление тестового проекта

## Что не трогаем

`TabSleeper` (кроме точки применения маштаба), `WebViewManager`, `App.xaml.cs`,
тёмную тему, модели `Tab` и `Bookmark`, структуру `browser.db`.