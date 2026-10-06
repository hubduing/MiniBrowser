using MiniBrowser.Models;

namespace MiniBrowser.Services;

/// <summary>
/// Состав и порядок групп вкладок. Класс намеренно не знает про WPF: разметка
/// и <see cref="BrowserTabView"/> живут в TabManager, а здесь только правила
/// «вкладка лежит в такой-то группе и на такой-то позиции» — их и покрывают тесты.
/// </summary>
public sealed class TabGroups
{
    /// <summary>Размер палитры групп: столько цветов объявлено в App.xaml.</summary>
    public const int PaletteSize = 8;

    private readonly List<TabGroup> _groups;
    // Плоский список в порядке отображения — единственный источник для горячих
    // клавиш и усыпления. Пересобирается вместо точечных правок: вкладок мало,
    // а кэш всегда совпадает с тем, что видит пользователь.
    private List<Tab> _flat = new();

    public event Action? Changed;
    public event Action? GroupsChanged;
    public event Action<Tab>? ActiveChanged;

    public IReadOnlyList<TabGroup> Groups => _groups;

    public IReadOnlyList<Tab> Tabs => _flat;

    public TabGroup? ActiveGroup { get; private set; }

    public Tab? ActiveTab { get; private set; }

    public TabGroups() : this(Array.Empty<TabGroup>()) { }

    /// <summary>Восстановление готовой раскладки: группы приходят уже с вкладками.</summary>
    public TabGroups(IEnumerable<TabGroup> restored)
    {
        _groups = restored.ToList();
        foreach (var group in _groups)
            foreach (var tab in group.Tabs)
                tab.GroupId = group.Id;
        Rebuild();

        // Активной становится первая вкладка первой непустой группы: пустые группы
        // в начале раскладки для этого не годятся.
        var first = _groups.Select(g => g.Tabs.Count > 0 ? g.Tabs[0] : null)
            .FirstOrDefault(t => t is not null);
        if (first is not null) SetActive(first);
    }

    public TabGroup CreateGroup(string? name = null, int? colorIndex = null)
    {
        var group = new TabGroup
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Группа {_groups.Count + 1}" : name,
            ColorIndex = colorIndex ?? NextColorIndex(),
        };
        _groups.Add(group);
        GroupsChanged?.Invoke();
        Changed?.Invoke();
        return group;
    }

    public bool RemoveGroup(TabGroup group)
    {
        if (!_groups.Remove(group)) return false;

        // Активную вкладку этой группы снимаем, иначе она осталась бы активной
        // вхолостую — без представления и без группы.
        if (ActiveGroup == group) SetActiveQuiet(_groups.SelectMany(g => g.Tabs).FirstOrDefault());

        Rebuild();
        GroupsChanged?.Invoke();
        Changed?.Invoke();
        return true;
    }

    public bool MoveGroup(TabGroup group, int newIndex)
    {
        var index = _groups.IndexOf(group);
        if (index < 0) return false;

        _groups.RemoveAt(index);
        _groups.Insert(Math.Clamp(newIndex, 0, _groups.Count), group);
        // Порядок вкладок не меняется, но сквозной список строится по группам.
        Rebuild();
        GroupsChanged?.Invoke();
        return true;
    }

    public TabGroup EnsureFirstGroup() =>
        _groups.Count > 0 ? _groups[0] : CreateGroup();

    /// <summary>Куда класть новую вкладку: активная группа, иначе первая, иначе новая.</summary>
    public TabGroup ActiveOrFirst() =>
        ActiveGroup ?? _groups.FirstOrDefault() ?? EnsureFirstGroup();

    public Tab AddTab(TabGroup? group = null, Tab? tab = null)
    {
        var target = group ?? ActiveOrFirst();
        tab ??= new Tab();
        tab.GroupId = target.Id;
        target.Tabs.Add(tab);

        Rebuild();
        SetActive(tab);
        return tab;
    }

    public bool RemoveTab(Tab tab)
    {
        var group = GroupOf(tab);
        if (group is null) return false;

        group.Tabs.Remove(tab);
        if (ActiveTab == tab) SetActiveQuiet(group.Tabs.LastOrDefault() ?? _flat.FirstOrDefault());
        // Группа остаётся: пустая группа — законная заготовка, а не мусор.
        Rebuild();
        Changed?.Invoke();
        return true;
    }

    public bool MoveTab(Tab tab, TabGroup target, int index)
    {
        var source = GroupOf(tab);
        if (source is null) return false;

        // Вставка в текущее место — ничего не меняет, но события шлём только
        // когда порядок действительно поехал.
        var sameGroup = ReferenceEquals(source, target);
        var current = source.Tabs.IndexOf(tab);
        if (sameGroup && (current == index || current + 1 == index)) return false;

        source.Tabs.RemoveAt(current);
        target.Tabs.Insert(Math.Clamp(index, 0, target.Tabs.Count), tab);
        tab.GroupId = target.Id;

        Rebuild();
        Changed?.Invoke();
        GroupsChanged?.Invoke();
        SetActive(tab);
        return true;
    }

    public bool SetActive(Tab? tab)
    {
        if (tab is null) return false;
        if (ReferenceEquals(ActiveTab, tab)) return false;
        if (GroupOf(tab) is null) return false;

        SetActiveQuiet(tab);
        return true;
    }

    public TabGroup? GroupOf(Tab tab)
    {
        foreach (var group in _groups)
            if (group.Tabs.Contains(tab))
                return group;
        return null;
    }

    public int IndexOf(Tab tab)
    {
        var group = GroupOf(tab);
        return group is null ? -1 : group.Tabs.IndexOf(tab);
    }

    public void Clear()
    {
        _groups.Clear();
        _flat = new List<Tab>();
        ActiveGroup = null;
        ActiveTab = null;
        GroupsChanged?.Invoke();
        Changed?.Invoke();
    }

    /// <summary>Первый свободный цвет палитры, иначе по кругу.</summary>
    private int NextColorIndex()
    {
        for (var i = 0; i < PaletteSize; i++)
            if (!_groups.Any(g => g.ColorIndex == i))
                return i;
        return _groups.Count % PaletteSize;
    }

    private void SetActiveQuiet(Tab? tab)
    {
        if (ActiveTab is not null) ActiveTab.IsActive = false;
        ActiveTab = tab;
        ActiveGroup = tab is null ? null : GroupOf(tab);
        if (tab is not null) tab.IsActive = true;
        ActiveChanged?.Invoke(tab!);
    }

    private void Rebuild()
    {
        _flat = _groups.SelectMany(g => g.Tabs).ToList();
        if (ActiveGroup is not null && !_groups.Contains(ActiveGroup))
            ActiveGroup = _groups.FirstOrDefault();
    }
}