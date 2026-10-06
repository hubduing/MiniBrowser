using System.Windows;
using MiniBrowser.Models;

namespace MiniBrowser.Services;

/// <summary>Куда именно попал курсор при перетаскивании.</summary>
public enum DropKind
{
    /// <summary>Мимо полосы — перетаскивание отменяется, вкладка остаётся на месте.</summary>
    None,
    /// <summary>Над колонкой: вкладка уходит в эту группу целиком.</summary>
    Group,
    /// <summary>Между строками вкладок: вкладка встаёт на указанную позицию.</summary>
    BetweenTabs,
}

/// <summary>Решение резолвера: цель и позиция вставки.</summary>
public sealed record DropTarget(DropKind Kind, TabGroup? Group, int Index)
{
    public static readonly DropTarget None = new(DropKind.None, null, -1);
}

/// <summary>Прямоугольник одной строки вкладки внутри колонки.</summary>
public sealed record DropTabRow(Rect Bounds, Tab Tab);

/// <summary>Геометрия колонки группы на момент перетаскивания.</summary>
public sealed record DropColumn(
    TabGroup Group, Rect Bounds, bool IsCollapsed, IReadOnlyList<DropTabRow> Tabs);

/// <summary>
/// Решает, во что превратится перетаскивание вкладки. Класс чистый: геометрию
/// ему отдаёт полоса вкладок, а решение принимает без единого обращения к UI —
/// так его и покрывают тесты.
/// </summary>
public static class TabDropResolver
{
    /// <summary>Высота заголовка колонки: над ней попадание означает «в группу».</summary>
    public const double HeaderHeight = 28.0;

    public static DropTarget Resolve(Point pointer, IReadOnlyList<DropColumn> columns)
    {
        DropColumn? hit = null;
        foreach (var column in columns)
        {
            if (!column.Bounds.Contains(pointer)) continue;
            hit = column;
            break;
        }

        if (hit is null) return DropTarget.None;

        // Свёрнутая колонка и её заголовок — одна зона: вкладка просто падает в группу.
        if (hit.IsCollapsed || hit.Tabs.Count == 0 || pointer.Y < hit.Bounds.Top + HeaderHeight)
            return new DropTarget(DropKind.Group, hit.Group, hit.Tabs.Count);

        // Ищем первую строку, в верхней половине которой курсор ещё не был:
        // это ровно позиция вставки перед ней.
        for (var i = 0; i < hit.Tabs.Count; i++)
        {
            var row = hit.Tabs[i];
            if (pointer.Y < row.Bounds.Top + row.Bounds.Height / 2)
                return new DropTarget(DropKind.BetweenTabs, hit.Group, i);
        }

        return new DropTarget(DropKind.BetweenTabs, hit.Group, hit.Tabs.Count);
    }
}