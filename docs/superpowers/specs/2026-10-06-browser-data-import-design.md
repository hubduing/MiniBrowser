# Импорт данных из других браузеров — дизайн

Дата: 2026-10-06
Статус: согласовано с пользователем (устно, 2026-10-06)

## Цель

По образцу импорта при первом запуске Firefox: перенести **закладки и пароли**
из уже установленного браузера (Mozilla Firefox, Google Chrome, Microsoft Edge)
в MiniBrowser. Запуск — кнопкой в настройках, источник выбирается в диалоге.
Пароли хранятся в приложении и просматриваются во вкладке меню «Пароли».

## Объём

**Входит:**
- Обнаружение установленных браузеров по стандартным путям профилей.
- Импорт закладок из Firefox (`places.sqlite`) и Chrome/Edge (JSON `Bookmarks`).
- Импорт паролей из Firefox (`logins.json` + `key4.db`, шифрование NSS) и
  Chrome/Edge (`Login Data`, AES-GCM/DPAPI; формат v20 — пропуск).
- Хранилище паролей в существующем SQLite `StorageService` (DPAPI-шифрование).
- Диалог «Импорт данных из других браузеров» (источник + чекбоксы + итог).
- Вкладка «Пароли» в меню-drawer: поиск, показ, копирование, удаление.

**Не входит (позже/отдельно):**
- Автозаполнение форм на сайтах.
- Импорт истории, сессий, расширений.
- Экспорт данных из MiniBrowser.
- Мастер-пароль хранилища (шифрование — DPAPI текущего пользователя).

## Компоненты

| Компонент | Файл | Назначение |
|---|---|---|
| `BrowserProfile` + `BrowserDetector` | `Services/Import/BrowserDetector.cs` | Поиск установленных браузеров: Firefox — `%APPDATA%\Mozilla\Firefox\profiles.ini` (профиль Default), Chrome — `%LOCALAPPDATA%\Google\Chrome\User Data\Default`, Edge — `%LOCALAPPDATA%\Microsoft\Edge\User Data\Default`. |
| `IProfileReader` + реализации | `Services/Import/FirefoxReader.cs`, `ChromiumReader.cs` | Чтение закладок и паролей из копий файлов профиля (всегда работаем с копиями во временной папке — открытый браузер не мешает). |
| `BrowserImportService` | `Services/Import/BrowserImportService.cs` | Оркестрация: читатель → дедуп → `StorageService`. Возвращает `ImportResult` (счётчики + причины пропуска). |
| Таблица `passwords` | `Services/StorageService.cs` | Хранение: `host`, `username`, `secret BLOB` (DPAPI), `UNIQUE(host, username)`. |
| `PasswordStore` | `Services/PasswordStore.cs` | Обёртка над таблицей: добавить/список/удалить, шифрование-расшифровка DPAPI (`ProtectedData`, CurrentUser). |
| Диалог импорта | `Views/ImportDialogWindow.xaml(.cs)` | Модальное окно: ComboBox источников, чекбоксы «Закладки»/«Пароли», кнопка «Начать импорт», строка итога. |
| Блок в настройках | `Views/MenuDrawerView.xaml` | Заголовок «Импорт данных» + кнопка «Импорт данных из других браузеров». |
| Вкладка «Пароли» | `Views/MenuDrawerView.xaml`, `ViewModels/MenuDrawerViewModel.cs` | Список паролей по образцу вкладки «Закладки». |

## Форматы источников

**Firefox — закладки.** Копия `places.sqlite` (+ WAL) во временный файл,
запрос `moz_bookmarks ⋈ moz_places` (тип=1, исключить `place:%`), title может
 быть NULL — тогда берём URL.

**Firefox — пароли.** `logins.json` (массив `logins`: `hostname`,
`encryptedUsername`, `encryptedPassword`, `encType`) + ключ из `key4.db`
(SQLite: `metadata.item1` = globalSalt, `nssPrivate` = ключ, PKCS#5 v2 из
ASN.1-дескриптора; эталон — открытые реализации firepwd, детальные байтовые
смещения уточняются при реализации). Мастер-пароль по умолчанию пуст; если
пользователь задал свой — записи пропускаются, причина попадает в итог.

**Chromium — закладки.** JSON `Bookmarks`: обход `roots` (bookmark_bar,
other, children), папки рекурсивно, только узлы `type=url`.

**Chromium — пароли.** Копия `Login Data` (SQLite, таблица `logins`) +
`Local State` (`os_crypt.encrypted_key` → DPAPI → AES-256-GCM ключ).
Префиксы `v10`/`v11` расшифровываются; `v20` (app-bound, Chrome 127+) —
**пропуск** с причиной «новое шифрование Chrome». Точные байтовые раскладки
v10/v11 уточняются при реализации по открытой документации Chromium.

## Хранилище паролей

```sql
CREATE TABLE IF NOT EXISTS passwords (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  host TEXT NOT NULL,
  username TEXT NOT NULL,
  secret BLOB NOT NULL,   -- DPAPI(CurrentUser), не открытым текстом
  added_at TEXT NOT NULL DEFAULT (datetime('now','localtime')),
  UNIQUE(host, username)
);
```

- Шифрование: `ProtectedData.Protect/Unprotect` (пакет
  `System.Security.Cryptography.ProtectedData`, Windows-only — проект
  `net8.0-windows`).
- Дедуп при импорте: `INSERT OR IGNORE` по `UNIQUE(host, username)` —
  существующая запись не перезаписывается (пароль пользователя не затираем).
- Недоступность БД — существующий паттерн `IsAvailable == false`, пароли
  просто не сохраняются, импорт закладок работает как раньше.

## UI

**Настройки** (вкладка «Настройки», перед «Сбросить настройки»):

> **Импорт данных**
> Скопировать закладки и пароли из другого браузера
> **[ Импорт данных из других браузеров ]**

**Диалог** (`ImportDialogWindow`, модальное, Owner = MainWindow):

1. «Из какого браузера» — ComboBox с найденными браузерами (дисплей: имя +
   путь к профилю). Не найдено → «Установленные браузеры не найдены»,
   кнопка импорта неактивна.
2. «Что импортировать» — CheckBox «Закладки» и «Пароли», оба по умолчанию
   включены.
3. **[ Начать импорт ]** — импорт в фоне (`Task.Run`), на время кнопка
   неактивна, текст меняется на «Импортируем…».
4. Итог в диалоге: «Закладок: N · Паролей: N · Пропущено: N» + список причин
   пропуска (мастер-пароль Firefox, шифрование v20, повреждённый файл).
   Кнопка после итога — «Готово» (закрыть).

Выбор источника не сохраняется в `AppSettings` — он одноразовый, каждый раз
можно выбрать другой браузер.

**Вкладка «Пароли»** (второй TabItem, после «Закладки»; индексы вкладок:
0 — Закладки, 1 — Пароли, 2 — История, 3 — Настройки; `SelectTab` и
`ShowHistory` обновляются соответственно):
- поиск по host/username (тот же механизм фильтрации, что у закладок);
- строка: host, username, пароль замаскирован (`••••••`), кнопки
  «показать/скрыть» (переключает на текст), «копировать пароль», ✕ удалить;
- пустое состояние: «Паролей пока нет. Импортируйте их из другого браузера
  в настройках.»;
- список читается через `PasswordStore`, расшифровка — только по явному
  запросу (кнопка «показать»), не при отрисовке.

## Поток данных

```
Настройки → [Импорт данных из других браузеров] → ImportDialogWindow
  → BrowserDetector (найти профили)
  → BrowserImportService.Import(browser, withBookmarks, withPasswords)
      → копии файлов во временной папке → IProfileReader
      → StorageService.AddBookmark (дедуп по url)
      → PasswordStore.Add (DPAPI, дедуп по host+username)
  → ImportResult → строка итога в диалоге
```

Импорт выполняется в `Task.Run`; все обращения к UI — через `Dispatcher`.
Временные копии удаляются в `finally`.

## Обработка ошибок

- Браузер не установлен / профиль не найден → источник не показывается.
- Файл отсутствует или повреждён → причина в `ImportResult`, не краш.
- Мастер-пароль Firefox ≠ пустому → причина «в Firefox задан мастер-пароль».
- Записи v20 Chrome → причина «новое шифрование Chrome (v20)».
- БД недоступна → закладки пропускаются с причиной (существующий паттерн).
- Частичный успех нормален: импортируется всё, что прочиталось.

## Проверка

- Новых тестов не пишется (правило пользователя).
- Сборка Release — 0 ошибок; существующие 243 теста проходят.
- Ручная проверка: запуск, импорт из установленного браузера, вкладка
  «Пароли», показ/копирование/удаление.
- Финал: commit → push → `dotnet publish` (свежий `publish\MiniBrowser.exe`).
