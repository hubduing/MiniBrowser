using System.ComponentModel;

namespace MiniBrowser.Models;

/// <summary>Модель вкладки: состояние, не привязанное к UI.</summary>
public sealed class Tab : INotifyPropertyChanged
{
    private string _title = "Новая вкладка";
    private string _url = string.Empty;
    private bool _isActive;
    private bool _isAsleep;
    private bool _isLoading;

    public Guid Id { get; } = Guid.NewGuid();

    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(nameof(Title)); } }
    }

    /// <summary>Текущий URL. Сохраняется даже при «усыплении» вкладки.</summary>
    public string Url
    {
        get => _url;
        set { if (_url != value) { _url = value; OnPropertyChanged(nameof(Url)); } }
    }

    public bool IsActive
    {
        get => _isActive;
        set { if (_isActive != value) { _isActive = value; OnPropertyChanged(nameof(IsActive)); } }
    }

    /// <summary>true — WebView2 выгружен, память освобождена.</summary>
    public bool IsAsleep
    {
        get => _isAsleep;
        set { if (_isAsleep != value) { _isAsleep = value; OnPropertyChanged(nameof(IsAsleep)); } }
    }

    /// <summary>
    /// true — движок сейчас грузит страницу. По нему кнопка тулбара решает,
    /// показывать «Остановить» или «Обновить».
    /// </summary>
    public bool IsLoading
    {
        get => _isLoading;
        set { if (_isLoading != value) { _isLoading = value; OnPropertyChanged(nameof(IsLoading)); } }
    }

    /// <summary>Когда вкладка стала неактивной (для политики усыпления).</summary>
    public DateTime? DeactivatedAt { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
