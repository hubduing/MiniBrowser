# Browser Data Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Импорт закладок и паролей из установленных Firefox/Chrome/Edge по кнопке в настройках, с хранилищем паролей (SQLite + DPAPI) и вкладкой «Пароли» в меню.

**Architecture:** Слой `Services/Import/` (детектор профилей → читатели форматов → оркестратор) пишет в существующий `StorageService` (закладки) и новый `PasswordStore` (пароли, DPAPI). UI — модальное `ImportDialogWindow` + блок в настройках + вкладка «Пароли». Всё чтение — из копий файлов профиля во временной папке.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF, `Microsoft.Data.Sqlite`, `System.Security.Cryptography.ProtectedData` (DPAPI), `System.Formats.Asn1` (DER для key4.db), `AesGcm`/`TripleDES`/`Rfc2898DeriveBytes`.

**Spec:** `docs/superpowers/specs/2026-10-06-browser-data-import-design.md`

## Global Constraints

- **Новых тестов не писать** (правило пользователя); проверка — сборка + существующие 243 теста + ручной запуск.
- Комментарии в коде — на русском, в стиле проекта (`/// <summary>` на типы и публичные члены).
- Ни одного артефакта отладки в репозитории: без `Console.Write`, лог-файлов, скриншотов, временных скриптов (`git status` перед коммитом).
- Сборка Release — 0 ошибок (CS8604×3 и CS0067 — известные, не трогаем).
- После каждого независимого блока: `git add` конкретных файлов → commit → в конце плана push + `dotnet publish src/MiniBrowser/MiniBrowser.csproj -c Release -o publish`.
- Никаких изменений в существующем поведении закладок/истории/сессий, кроме добавления таблицы и сдвига индексов вкладок меню.

## Review Focus

(Тестов нет по правилу пользователя — каждый пункт закрывается конкретным шагом проверки в задаче, там же указано чем.)

1. **Мастер-пароль Firefox ≠ пустому** — импорт не падает, в итоге причина «в Firefox задан мастер-пароль», пароли 0. → проверка в Task 3 (ручной прогон на машине пользователя) и Task 5.
2. **Записи Chrome v20 (app-bound)** — пропускаются с причиной, остальные v10/v11 импортируются. → Task 4/5.
3. **Открытый браузер (заблокированный SQLite)** — чтение идёт из копий (`FileShare.ReadWrite`), импорт работает. → шаг копирования в Task 3/4.
4. **Дедуп** — повторный импорт не дублирует ни закладки (url), ни пароли (host+username), не затирает существующий пароль. → Task 5 (ручной двойной прогон) + `UNIQUE` в схеме.
5. **Сдвиг индексов вкладок меню** — «Пароли» вставлена второй, `ShowHistory` открывает историю, `ShowBookmarks` — закладки. → Task 7 (build + ручной клик).

---

### Task 1: Хранилище паролей (схема + PasswordStore)

**Files:**
- Modify: `src/MiniBrowser/MiniBrowser.csproj` (2 PackageReference)
- Modify: `src/MiniBrowser/Services/StorageService.cs` (таблица + 4 метода)
- Create: `src/MiniBrowser/Services/PasswordStore.cs`

**Interfaces:**
- Consumes: `StorageService.IsAvailable`, паттерн `Exec(...)` с параметрами-кортежами.
- Produces (для Task 5 и UI):
  ```csharp
  public sealed record StoredPassword(int Id, string Host, string Username);
  public sealed class PasswordStore
  {
      public PasswordStore(StorageService storage);
      public bool IsAvailable { get; }
      public bool Add(string host, string username, string password); // false = уже есть (INSERT OR IGNORE)
      public IReadOnlyList<StoredPassword> GetAll();                  // без секрета
      public string GetSecret(int id);                                // "" если нет/не расшифровалось
      public bool Delete(int id);
  }
  ```
  StorageService: `bool AddPassword(string host, string username, byte[] secret)`, `List<(int Id, string Host, string Username)> GetPasswords()`, `byte[]? GetPasswordSecret(int id)`, `bool DeletePassword(int id)`.

- [ ] **Step 1: csproj** — добавить `System.Security.Cryptography.ProtectedData` (8.0.0).
- [ ] **Step 2: таблица в `StorageService`** — в существующий `Exec("""...""")` конструктора:
  ```sql
  CREATE TABLE IF NOT EXISTS passwords (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    host TEXT NOT NULL,
    username TEXT NOT NULL,
    secret BLOB NOT NULL,
    added_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
    UNIQUE(host, username)
  );
  ```
- [ ] **Step 3: 4 метода StorageService** — по образцу `AddBookmark`/`GetAllBookmarks`: `try/catch`, при `_connection is null` возврат `false`/пустого списка; `INSERT OR IGNORE`; секрет — `byte[]` (параметр-массив).
- [ ] **Step 4: `PasswordStore`** — DPAPI: `ProtectedData.Protect(Encoding.UTF8.GetBytes(pw), optionalEntropy: null, DataProtectionScope.CurrentUser)` при добавлении, `Unprotect` в `GetSecret`; `OptionalEntropy` — константа `byte[]` (усиление трафика), одинаковая для protect/unprotect; обёртка над `StorageService`.
- [ ] **Step 5: Сборка** — `dotnet build MiniBrowser.sln -c Release` → 0 ошибок.
- [ ] **Step 6: Commit** — `git add src/MiniBrowser/MiniBrowser.csproj src/MiniBrowser/Services/StorageService.cs src/MiniBrowser/Services/PasswordStore.cs && git commit -m "Add password storage with DPAPI encryption"`

---

### Task 2: Детектор установленных браузеров

**Files:**
- Create: `src/MiniBrowser/Services/Import/BrowserProfile.cs`
- Create: `src/MiniBrowser/Services/Import/BrowserDetector.cs`

**Interfaces:**
- Produces (для Task 3/4/5 и диалога):
  ```csharp
  public enum BrowserKind { Firefox, Chrome, Edge }
  public sealed record BrowserProfile(BrowserKind Kind, string Name, string ProfileDir, string? LocalStateFile);
  public static class BrowserDetector
  {
      public static IReadOnlyList<BrowserProfile> Detect(); // порядок: Firefox, Chrome, Edge
  }
  ```
- Правила: Firefox — `%APPDATA%\Mozilla\Firefox\profiles.ini`, секция `[ProfileN]` с `Default=1` (иначе первая с `Path=`), `ProfileDir = <Firefox dir>\<Path>` (относительный путь); Chrome — `%LOCALAPPDATA%\Google\Chrome\User Data\Default` + `Local State`; Edge — `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default` + `Local State`. Профиль/файл отсутствует → браузер не включается в список. `profiles.ini` без `Default=1` → берётся секция `[Install...]` `Default=` если есть, иначе первый `[ProfileN]` с существующим каталогом.

- [ ] **Step 1: Реализация** — простой ручной парсер ini (секции, `Ключ=Значение`, `;`-комментарии), `Directory.Exists`-проверки; русский XML-комментарий на класс.
- [ ] **Step 2: Сборка** — Release, 0 ошибок.
- [ ] **Step 3: Commit** — `... -m "Detect installed browser profiles"`

---

### Task 3: Читатель Firefox (закладки + пароли NSS)

**Files:**
- Create: `src/MiniBrowser/Services/Import/IProfileReader.cs` (общие типы)
- Create: `src/MiniBrowser/Services/Import/NssCrypto.cs`
- Create: `src/MiniBrowser/Services/Import/FirefoxReader.cs`

**Interfaces:**
- Produces (общий контракт с ChromiumReader, Task 4):
  ```csharp
  public sealed record RawBookmark(string Url, string Title);
  public sealed record RawLogin(string Host, string Username, string Password);
  public sealed record ReadResult<T>(IReadOnlyList<T> Items, int Skipped, IReadOnlyList<string> Notes);
  public interface IProfileReader
  {
      ReadResult<RawBookmark> ReadBookmarks();
      ReadResult<RawLogin> ReadLogins();
  }
  ```
- `FirefoxReader(BrowserProfile profile)`, всё чтение — из копий во временной папке (`Path.GetTempPath()/MiniBrowserImport-<guid>/`, `finally` удаляет).

**Алгоритмы (зафиксировано исследованием):**
- Закладки: копия `places.sqlite` (+`-wal`/`-shm` если есть, копировать с `FileShare.ReadWrite`), запрос
  `SELECT b.title, p.url FROM moz_bookmarks b JOIN moz_places p ON b.fk = p.id WHERE b.type = 1 AND p.url NOT LIKE 'place:%'`; `title IS NULL` → берём url.
- Пароли: `logins.json` → массив `logins[]` (`hostname`, `encryptedUsername`, `encryptedPassword`, `encType`); base64-блобы начинаются с ASCII `v10`/`v11`.
- Ключ из `key4.db` (SQLite): `metadata` строка `id='password'` → `item1` = globalSalt, `item2` = ASN.1 blob проверки пароля; `nssPrivate` строка с `a102='f8000000000000000000000000000001'` → `a11` = ASN.1 blob ключа. Расшифровка по OID внутри DER (`System.Formats.Asn1`):
  - `1.2.840.113549.1.12.5.1.3` (legacy 3DES): `hp=SHA1(globalSalt‖masterPwd)`, `chp=SHA1(hp‖entrySalt)`, `k1=HMAC-SHA1(chp, entrySalt20‖entrySalt)`, `tk=HMAC-SHA1(chp, entrySalt20)`, `k2=HMAC-SHA1(chp, tk‖entrySalt)`, `key=k1‖k2` [0:24], `iv=k[24:32]`; `TripleDES-CBC` без padding.
  - `1.2.840.113549.1.5.13` (PBES2): `k=SHA1(globalSalt‖masterPwd)`, `key=Rfc2898DeriveBytes.Pbkdf2(k, entrySalt, iter, SHA256, 32)`, `iv = 0x04,0x0e ‖ 14 байт из ASN.1` (кверка NSS), `AES-CBC`.
  - Мастер-пароль = `""` (default); сначала проверить `item2` — не сошлось → `ReadResult` c `Skipped=0`, `Notes=["в Firefox задан мастер-пароль — пароли не импортированы"]`.
  - `a11` → тот же PBE → 32-байтовый мастер-ключ (или 24+8 для 3DES).
- Данные логина: `encType==2` (AES): `v10`-блоб → ключ = мастер-ключ из a11, `iv = блоб[3:19]`, `AES-CBC` остальное; `encType==1` (3DES): `v11`-блоб → legacy-KDF c `entrySalt = блоб[3:23]`, `iv/key` из k, шифртекст `блоб[23:]` (для v11 — блоб[23:]). Точные смещения сверить с эталоном `firepwd.py` (lclevy/firepwd, GPL — сверять логику, не копировать текст); расхождение → отладка по реальному профилю пользователя, артефакты не в репо.
- Не-UTF8/битые записи → `Skipped++`, без падения.

- [ ] **Step 1: Общие типы** — `IProfileReader.cs` (ReadResult/RawBookmark/RawLogin/интерфейс).
- [ ] **Step 2: `NssCrypto`** — мини-DER-чтение через `AsnReader` (SEQUENCE/OCTET STRING/INTEGER/OBJECT IDENTIFIER), две PBE-ветки, `DecryptMasterKey(key4DbPath, masterPwd)`, `DecryptLoginBlob(blob, masterKey, encType, ...)`.
- [ ] **Step 3: `FirefoxReader`** — копирование файлов, SQL, JSON, сборка `ReadResult`.
- [ ] **Step 4: Сборка** — Release, 0 ошибок.
- [ ] **Step 5: Commit** — `... -m "Read Firefox bookmarks and logins (NSS key4.db)"`

---

### Task 4: Читатель Chromium (Chrome/Edge)

**Files:**
- Create: `src/MiniBrowser/Services/Import/ChromiumReader.cs`

**Interfaces:**
- Consumes: `IProfileReader`, `ReadResult<T>` из Task 3, `BrowserProfile.LocalStateFile`.
- `ChromiumReader(BrowserProfile profile)` — закладки и пароли независимо (отсутствие одного файла → `Notes`, не исключение).

**Алгоритмы:**
- Закладки: JSON `ProfileDir/Bookmarks`, обход `roots.*.children` рекурсивно: узлы `type=="url"` → `RawBookmark(url, name)`, папки игнорируются (children обходятся).
- Пароли: копия `Login Data` → `SELECT origin_url, username_value, password_value FROM logins` (+ не-пустой `password_value`); ключ: `Local State` → JSON `os_crypt.encrypted_key` (base64) → срезать префикс `DPAPI` (5 байт) → `ProtectedData.Unprotect` → 32 байта AES-256-GCM.
- `password_value`: префикс `v10`/`v11` → `AesGcm(key, 16)`, nonce = `value[3..15]`, tag = последние 16, ciphertext между; успех → `RawLogin`. Без префикса → `ProtectedData.Unprotect(value)` (legacy pre-v80). Префикс `v20` → `Skipped++` с агрегированной Note «новое шифрование Chrome (v20) — N записей». Ошибка расшифровки → `Skipped++`.
- `AesGcm` в .NET 8: конструктор `new AesGcm(key, 16)` (безразмерный — obsolete).

- [ ] **Step 1: Реализация**.
- [ ] **Step 2: Сборка** — Release, 0 ошибок.
- [ ] **Step 3: Commit** — `... -m "Read Chrome/Edge bookmarks and logins"`

---

### Task 5: Оркестратор импорта

**Files:**
- Create: `src/MiniBrowser/Services/Import/BrowserImportService.cs`

**Interfaces:**
- Consumes: `BrowserProfile`, `IProfileReader` (реализации по `Kind`), `StorageService.AddBookmark/IsAvailable`, `PasswordStore.Add/IsAvailable`.
- Produces (для Task 6):
  ```csharp
  public sealed record ImportResult(int BookmarksAdded, int BookmarksSkipped,
      int PasswordsAdded, int PasswordsSkipped, IReadOnlyList<string> Notes);
  public sealed class BrowserImportService
  {
      public BrowserImportService(StorageService storage, PasswordStore passwords);
      public ImportResult Import(BrowserProfile profile, bool withBookmarks, bool withLogins);
  }
  ```
- Логика: выбор `FirefoxReader`/`ChromiumReader`; закладки — `StorageService.AddBookmark` возвращает `false` при дубле → `BookmarksSkipped++` (нужен счётчик: `AddBookmark` сейчас void — добавить возврат `bool`, обновив вызов в `MainWindow.AddBookmark`, где результат не используется); пароли — `PasswordStore.Add` `false` → `PasswordsAdded` не растёт (считать в `PasswordsSkipped`); `!IsAvailable` → Note «база данных недоступна»; объединить `Notes` читателя и свои.

- [ ] **Step 1: `AddBookmark` → `bool`** — возврат `false` при пустом url/ошибке/дубле (`INSERT OR IGNORE`), правка единственного потребителя `MainWindow.AddBookmark`.
- [ ] **Step 2: `BrowserImportService`**.
- [ ] **Step 3: Сборка** — Release, 0 ошибок.
- [ ] **Step 4: Commit** — `... -m "Orchestrate browser data import"`

---

### Task 6: Диалог импорта + кнопка в настройках

**Files:**
- Create: `src/MiniBrowser/Views/ImportDialogWindow.xaml` (+ `.cs`)
- Modify: `src/MiniBrowser/Views/MenuDrawerView.xaml` (блок «Импорт данных» перед «Сбросить настройки»)
- Modify: `src/MiniBrowser/Views/MenuDrawerView.xaml.cs` (событие)
- Modify: `src/MiniBrowser/MainWindow.xaml.cs` (подписка + открытие)

**Interfaces:**
- `ImportDialogWindow(StorageService storage, PasswordStore passwords, BrowserProfile[] profiles)` — модальное, `Owner = MainWindow`, `WindowStartupLocation=CenterOwner`.
- `MenuDrawerView.ImportDataRequested : event Action?` — клик по кнопке.
- MainWindow: подписка в конструкторе → `new ImportDialogWindow(_storage, _passwords? , BrowserDetector.Detect()).ShowDialog()`.

**UI диалога** (кисти приложения: `B.Content`, `T.Primary`, `T.Muted`, стиль `FlatToolButton`):
- Заголовок «Импорт данных из других браузеров».
- Label «Из какого браузера» + ComboBox: `profiles` (DisplayText — имя), пусто → TextBlock «Установленные браузеры не найдены», кнопка «Начать импорт» `IsEnabled=false`.
- CheckBox «Закладки» (Checked), «Пароли» (Checked).
- Кнопка «Начать импорт» → `Task.Run(() => service.Import(...))`, на время `IsEnabled=false` + текст «Импортируем…», результат через `Dispatcher.Invoke` в строку итога: `$"Закладок: {+add} · Пропущено: {skip} · Паролей: … · Пропущено: …"`, ниже `Notes` (каждая отдельной строкой, `T.Muted`).
- Кнопка «Готово» (появляется после итога) — `Close()`; Esc — тоже закрыть. Кнопка закрытия окна стандартная («✕» нет — у браузера свои паттерны; используется `Cancel`-кнопка).

- [ ] **Step 1: XAML диалога + code-behind**.
- [ ] **Step 2: Блок в настройках + событие + подписка MainWindow** (`_passwords` поле: создать `PasswordStore` рядом с `_storage` в конструкторе MainWindow).
- [ ] **Step 3: Сборка** — Release, 0 ошибок.
- [ ] **Step 4: Commit** — `... -m "Add browser data import dialog"`

---

### Task 7: Вкладка «Пароли» в меню

**Files:**
- Modify: `src/MiniBrowser/Views/MenuDrawerView.xaml` (новый TabItem после «Закладки»)
- Modify: `src/MiniBrowser/Views/MenuDrawerView.xaml.cs` (`SelectTab` clamp 0..3, комментарий индексов)
- Modify: `src/MiniBrowser/ViewModels/MenuDrawerViewModel.cs` (коллекция + команды)
- Modify: `src/MiniBrowser/MainWindow.xaml.cs` (`ShowHistory` → `SelectTab(2)`)
- Modify: `src/MiniBrowser/Models/` — новый `PasswordItem.cs` (INPC: Host, Username, IsRevealed, Secret)

**Interfaces:**
- Consumes: `PasswordStore.GetAll/GetSecret/Delete` (Task 1).
- VM: `ObservableCollection<PasswordItem> Passwords`, `RefreshPasswords()`, команда `DeletePasswordCommand(PasswordItem)`, строка поиска переиспользует существующий механизм фильтрации (`_passwordView` по образцу `_bookmarkView`), `PasswordsCountText`, `HasPasswords`.
- Секрет: `GetSecret(id)` вызывается лениво при первом раскрытии (`PasswordItem.Secret` заполняется из VM-хоста), показ/скрытие — ToggleButton в строке; копирование — `Clipboard.SetText`.

**XAML строка** (по образцу строки закладки): host (жирный), username (`T.Muted`), ToggleButton «👁/скрыть» рядом с `••••••`/текстом, кнопка «⧉» (копировать), ✕ (удалить). Пустое состояние: «Паролей пока нет. Импортируйте их из другого браузера в настройках.»

- [ ] **Step 1: `PasswordItem` + VM** (коллекция, фильтр, команды, счётчики).
- [ ] **Step 2: XAML TabItem + строки**; `SelectTab(0..3)`; `ShowHistory` → `SelectTab(2)`.
- [ ] **Step 3: Сборка** — Release, 0 ошибок.
- [ ] **Step 4: Commit** — `... -m "Add passwords tab to menu drawer"`

---

### Task 8: Финальная проверка, пуш, пересборка релиза

**Files:** без изменений (только git/publish).

- [ ] **Step 1: Сборка + тесты** — `dotnet build MiniBrowser.sln -c Release` (0 ошибок), `dotnet test MiniBrowser.sln -c Release --no-build` → 243/243.
- [ ] **Step 2: Ручной прогон** — запустить `publish`-сборку (после Step 4) нельзя раньше публикации, поэтому: `dotnet run`/запуск exe → Настройки → «Импорт данных из других браузеров» → выбрать браузер → импорт → проверить счётчики, вкладку «Пароли» (показ/копирование/удаление), повторный импорт (дубликатов нет), `ShowHistory`/`ShowBookmarks`. Если расшифровка падает — systematic-debugging, артефакты отладки вычистить.
- [ ] **Step 3: Чистота репо** — `git status --short` (кроме `MiniBrowser.exe.lnk` ничего лишнего), отсутствие `*.log`/`*.png`.
- [ ] **Step 4: Push + publish** — `git push`; `dotnet publish src/MiniBrowser/MiniBrowser.csproj -c Release -o publish`; сверить `LastWriteTime` `publish\MiniBrowser.exe`.
