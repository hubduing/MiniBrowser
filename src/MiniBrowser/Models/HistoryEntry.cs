namespace MiniBrowser.Models;

/// <summary>Запись истории посещений.</summary>
/// <param name="VisitedAt">Момент посещения строки (SQLite localtime, до секунды).</param>
public sealed record HistoryEntry(string Url, string Title, string VisitedAt)
{
    /// <summary>Сколько раз URL был посещён. 1 — одна строка в истории.</summary>
    public int VisitCount { get; init; } = 1;

    /// <summary>Дата последнего посещения — для показа в панели.</summary>
    public string LastVisit { get; init; } = "";
}
