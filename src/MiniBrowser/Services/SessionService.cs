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

    /// <summary>
    /// Прочитать сессию в живые группы. Снимок возвращается вместе с ними:
    /// решение «восстанавливать или открыть домашнюю страницу» принимает
    /// StartupPlan по снимку, а не по собранным группам — те уже пустые,
    /// если мусор оказался нечитаемым.
    /// </summary>
    public (SessionSnapshot? Snapshot, IReadOnlyList<TabGroup> Groups, Tab? ActiveTab) Load()
    {
        var snapshot = _storage.LoadSession();
        if (snapshot is null)
            return (null, Array.Empty<TabGroup>(), null);

        // Словарь, а не поиск по списку: вкладок может быть десятки, а поиск был бы
        // квадратичным. Ссылка на группу нужна, чтобы положить вкладку в её коллекцию.
        var byId = new Dictionary<Guid, TabGroup>();
        var groups = new List<TabGroup>();
        foreach (var row in snapshot.Groups)
        {
            if (!Guid.TryParse(row.Id, out var id)) continue;
            var group = new TabGroup
            {
                Name = string.IsNullOrWhiteSpace(row.Name) ? "Группа" : row.Name,
                // Цвет приходит из файла и мог быть испорчен — молча берём первый.
                ColorIndex = Math.Clamp(row.ColorIndex, 0, TabGroups.PaletteSize - 1),
                IsCollapsed = row.Collapsed,
            };
            groups.Add(group);
            byId[id] = group;
        }

        if (groups.Count == 0) return (snapshot, groups, null);

        Tab? active = null;
        foreach (var row in snapshot.Tabs)
        {
            if (!Guid.TryParse(row.Id, out var id)) continue;
            if (!Guid.TryParse(row.GroupId, out var groupId)) continue;
            // Вкладка без группы — сирота: её группа не сохранилась, восстановить её некуда.
            if (!byId.TryGetValue(groupId, out var group)) continue;

            var tab = new Tab(id) { Url = row.Url, Title = row.Title };
            group.Tabs.Add(tab);
            // Активной берём первую помеченную: при нескольких флагах порядок решает.
            if (row.IsActive && active is null) active = tab;
        }

        if (active is not null) active.IsActive = true;
        return (snapshot, groups, active);
    }
}