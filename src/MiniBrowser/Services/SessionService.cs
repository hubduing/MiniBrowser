using MiniBrowser.Models;

namespace MiniBrowser.Services;

/// <summary>
/// Чтение и запись раскладки сессии. Сам класс тонкий: работа со строками
/// принадлежит <see cref="StorageService"/>, а решение о том, какая вкладка
/// активна, принимается здесь — на момент сохранения это единственное место,
/// где известна настоящая активная вкладка.
/// </summary>
public sealed class SessionService
{
    private readonly StorageService _storage;

    public SessionService(StorageService storage) => _storage = storage;

    /// <summary>Пустая сессия — так StoreService выглядит для ещё не открытого браузера.</summary>
    public static SessionSnapshot EmptySnapshot() =>
        new(Array.Empty<SessionGroupRow>(), Array.Empty<SessionTabRow>());

    public void Save(IReadOnlyList<TabGroup> groups, Tab? activeTab)
    {
        var groupRows = new List<SessionGroupRow>();
        var tabRows = new List<SessionTabRow>();
        var groupPosition = 0;
        // Порядок вкладок сквозной: иначе при загрузке строки разных групп с
        // одинаковой позицией внутри своей группы перемешались бы между собой.
        var tabPosition = 0;

        foreach (var group in groups)
        {
            groupRows.Add(new SessionGroupRow(
                group.Id.ToString(), group.Name, group.ColorIndex, group.IsCollapsed, groupPosition++));

            foreach (var tab in group.Tabs)
                tabRows.Add(new SessionTabRow(
                    tab.Id.ToString(), group.Id.ToString(), tab.Url, tab.Title,
                    activeTab is not null && tab.Id == activeTab.Id, tabPosition++));
        }

        _storage.SaveSession(new SessionSnapshot(groupRows, tabRows));
    }
}