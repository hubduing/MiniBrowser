namespace MiniBrowser.Models;

/// <summary>Запись истории посещений.</summary>
public sealed record HistoryEntry(string Url, string Title, string VisitedAt);
