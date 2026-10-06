namespace MiniBrowser.Models;

/// <summary>
/// Снимок раскладки групп и вкладок ровно в том виде, в каком он лежит на диске.
/// Существует отдельно от живых <see cref="TabGroup"/>, чтобы хранилище не зависело
/// от UI-моделей, а сессия могла быть записана и прочитана без окна браузера.
/// </summary>
public sealed record SessionGroupRow(string Id, string Name, int ColorIndex, bool Collapsed, int Position);

/// <summary>Вкладка снимка. Поле IsActive у всех вкладок, кроме одной, равно false.</summary>
public sealed record SessionTabRow(
    string Id, string GroupId, string Url, string Title, bool IsActive, int Position);

/// <summary>Снимок целиком: группы и вкладки, каждая со своим порядковым номером.</summary>
public sealed record SessionSnapshot(
    IReadOnlyList<SessionGroupRow> Groups, IReadOnlyList<SessionTabRow> Tabs);