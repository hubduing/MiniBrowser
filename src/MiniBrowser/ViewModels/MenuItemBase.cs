using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiniBrowser.ViewModels;

/// <summary>Строка списка панели: закладка или запись истории.</summary>
public abstract class MenuItemBase : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _url = string.Empty;
    private string _detail = string.Empty;
    private bool _canDelete;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    public string Url
    {
        get => _url;
        set { if (_url != value) { _url = value; OnPropertyChanged(); } }
    }

    public string Detail
    {
        get => _detail;
        set { if (_detail != value) { _detail = value; OnPropertyChanged(); } }
    }

    public bool CanDelete
    {
        get => _canDelete;
        set { if (_canDelete != value) { _canDelete = value; OnPropertyChanged(); } }
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    /// <summary>Хук удаления: коллекциями управляет VM, поэтому по умолчанию пусто.</summary>
    protected virtual void OnDeleted()
    {
    }
}

/// <summary>Закладка: удаляемая строка, даты добавления в модели нет.</summary>
public sealed class BookmarkItem : MenuItemBase
{
    public BookmarkItem(string url, string title)
    {
        Url = url;
        Title = title;
        // Bookmark хранит только url и title — показывать нечего, оставляем пусто.
        Detail = string.Empty;
        CanDelete = true;
    }
}

/// <summary>Запись истории: неудаляемая строка со счётчиком визитов и датой.</summary>
public sealed class HistoryItem : MenuItemBase
{
    public HistoryItem(string url, string title, int visitCount, string lastVisit)
    {
        Url = url;
        Title = title;
        VisitCount = visitCount;
        // Отдельного удаления записи нет: в истории лежат посещения, а не закладки пользователя.
        CanDelete = false;
        Detail = $"{visitCount} {VisitWord(visitCount)} · {lastVisit}";
    }

    public int VisitCount { get; }

    private static string VisitWord(int n)
    {
        // 11..14 — исключение: «11 визитов», а не «11 визита».
        if ((n % 100) is >= 11 and <= 14) return "визитов";
        return (n % 10) switch
        {
            1 => "визит",
            2 or 3 or 4 => "визита",
            _ => "визитов",
        };
    }
}
