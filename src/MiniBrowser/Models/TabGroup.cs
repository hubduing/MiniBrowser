using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace MiniBrowser.Models;

/// <summary>
/// Именованная группа вкладок: цвет, сворачивание и состав. Контейнером владеет
/// <see cref="Services.TabGroups"/>, но модель ничего не знает о порядке групп —
/// она отвечает только за состав своей коллекции.
/// </summary>
public sealed class TabGroup : INotifyPropertyChanged
{
    private string _name = "Группа";
    private int _colorIndex;
    private bool _isCollapsed;

    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Имя группы, показывается на заголовке колонки.</summary>
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(nameof(Name)); } }
    }

    /// <summary>Индекс цвета в палитре групп (0–7). Нормализуется при загрузке сессии.</summary>
    public int ColorIndex
    {
        get => _colorIndex;
        set { if (_colorIndex != value) { _colorIndex = value; OnPropertyChanged(nameof(ColorIndex)); } }
    }

    /// <summary>true — вкладки группы скрыты, видна только узкая полоса.</summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set { if (_isCollapsed != value) { _isCollapsed = value; OnPropertyChanged(nameof(IsCollapsed)); } }
    }

    /// <summary>
    /// Вкладки группы. Коллекцию нельзя заменять — на неё подписана разметка,
    /// а состав меняется только перестановками внутри неё.
    /// </summary>
    public ObservableCollection<Tab> Tabs { get; } = new();

    /// <summary>Сколько вкладок в группе — для бейджа на заголовке.</summary>
    public int Count => Tabs.Count;

    public TabGroup() => Tabs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(Count));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}