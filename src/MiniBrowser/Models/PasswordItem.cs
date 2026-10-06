using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MiniBrowser.Models;

/// <summary>
/// Строка списка паролей. Секрет хранится пустым до первого запроса —
/// расшифровка только по явном «показать»/«копировать», не при отрисовке.
/// </summary>
public sealed class PasswordItem : INotifyPropertyChanged
{
    private bool _isRevealed;
    private string _secret = "";

    public PasswordItem(int id, string host, string username)
    {
        Id = id;
        Host = host;
        Username = username;
    }

    public int Id { get; }
    public string Host { get; }
    public string Username { get; }

    /// <summary>Расшифрованный секрет; "" пока не запрашивался.</summary>
    public string Secret
    {
        get => _secret;
        set
        {
            if (_secret == value) return;
            _secret = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplaySecret));
        }
    }

    public bool IsRevealed
    {
        get => _isRevealed;
        set
        {
            if (_isRevealed == value) return;
            _isRevealed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplaySecret));
        }
    }

    /// <summary>Что показывает строка: маска или открытый текст.</summary>
    public string DisplaySecret => IsRevealed
        ? (_secret.Length > 0 ? _secret : "—")
        : "••••••••";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
