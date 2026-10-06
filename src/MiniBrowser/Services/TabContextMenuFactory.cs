using System.Windows.Controls;

namespace MiniBrowser.Services;

/// <summary>
/// Собирает контекстные меню вкладки и группы для полосы вкладок.
/// Вынесено из представления, чтобы структуру меню можно было проверить без
/// окна. Подменю здесь — это дочерние <see cref="MenuItem"/>, а не вложенный
/// <see cref="ContextMenu"/>: WPF запрещает делать меню чьим-либо дочерним
/// элементом и падает с InvalidOperationException.
/// </summary>
public static class TabContextMenuFactory
{
    /// <summary>
    /// Наполнить меню вкладки. <paramref name="groups"/> — группы в порядке
    /// колонок: имя, признак «вкладка уже здесь» и маркер (колонка), который
    /// вернётся в <paramref name="moveToGroup"/> при выборе пункта.
    /// </summary>
    public static void FillTabMenu<TGroup>(
        ContextMenu menu,
        IReadOnlyList<(string Name, bool IsCurrent, TGroup Target)> groups,
        Action<TGroup> moveToGroup,
        Action createGroupFromTab,
        Action leaveGroup,
        Action closeTab)
    {
        menu.Items.Clear();

        var move = new MenuItem { Header = "Переместить в группу" };
        foreach (var (name, isCurrent, target) in groups)
        {
            var destination = target;
            var item = new MenuItem
            {
                // Текущая группа помечена галочкой и недоступна: переносить
                // вкладку туда, где она уже лежит, нечего.
                Header = isCurrent ? "\u2713 " + name : name,
                IsEnabled = !isCurrent,
            };
            item.Click += (_, _) => moveToGroup(destination);
            move.Items.Add(item);
        }
        menu.Items.Add(move);

        var create = new MenuItem { Header = "Создать группу из вкладки" };
        create.Click += (_, _) => createGroupFromTab();
        menu.Items.Add(create);

        var leave = new MenuItem { Header = "Убрать из группы" };
        leave.Click += (_, _) => leaveGroup();
        menu.Items.Add(leave);

        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = "Закрыть вкладку" };
        close.Click += (_, _) => closeTab();
        menu.Items.Add(close);
    }

    /// <summary>
    /// Наполнить меню заголовка группы. <paramref name="colorIcon"/> даёт
    /// образец цвета для пункта палитры; null — пункт без образца.
    /// </summary>
    public static void FillGroupMenu(
        ContextMenu menu,
        bool isCollapsed,
        int paletteSize,
        Func<int, object?>? colorIcon,
        Action rename,
        Action<int> pickColor,
        Action toggleCollapse,
        Action closeGroup)
    {
        menu.Items.Clear();

        var renameItem = new MenuItem { Header = "Переименовать" };
        renameItem.Click += (_, _) => rename();
        menu.Items.Add(renameItem);

        var color = new MenuItem { Header = "Сменить цвет" };
        for (var i = 0; i < paletteSize; i++)
        {
            var index = i;
            var item = new MenuItem
            {
                Header = "Цвет " + (index + 1),
                Icon = colorIcon?.Invoke(index),
            };
            item.Click += (_, _) => pickColor(index);
            color.Items.Add(item);
        }
        menu.Items.Add(color);

        var collapse = new MenuItem { Header = isCollapsed ? "Развернуть" : "Свернуть" };
        collapse.Click += (_, _) => toggleCollapse();
        menu.Items.Add(collapse);

        menu.Items.Add(new Separator());

        var close = new MenuItem { Header = "Закрыть группу" };
        close.Click += (_, _) => closeGroup();
        menu.Items.Add(close);
    }
}