namespace MiniBrowser.Services;

/// <summary>Логика кнопки «скопировать адрес»: когда копирование доступно.</summary>
public static class AddressCopy
{
    public static bool CanCopy(string? url) => !string.IsNullOrWhiteSpace(url);
}
