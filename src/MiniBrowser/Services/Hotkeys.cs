using System.Windows.Input;

namespace MiniBrowser.Services;

/// <summary>Действия браузера, доступные из горячих клавиш.</summary>
public interface IBrowserActions
{
    void NewTab();
    void CloseActiveTab();
    void FocusAddressBar();
    void NextTab();
    void PrevTab();
    void AddBookmark();
    void GoBack();
    void GoForward();
    void Reload();
}

/// <summary>Горячие клавиши браузера.</summary>
public static class Hotkeys
{
    public static bool TryHandle(Key key, ModifierKeys modifiers, IBrowserActions actions)
    {
        switch (modifiers)
        {
            case ModifierKeys.Control:
                switch (key)
                {
                    case Key.T: actions.NewTab(); return true;
                    case Key.W: actions.CloseActiveTab(); return true;
                    case Key.L: actions.FocusAddressBar(); return true;
                    case Key.D: actions.AddBookmark(); return true;
                    case Key.R: actions.Reload(); return true;
                    case Key.Tab: actions.NextTab(); return true;
                }
                break;

            case ModifierKeys.Control | ModifierKeys.Shift:
                if (key == Key.Tab) { actions.PrevTab(); return true; }
                break;

            case ModifierKeys.Alt:
                if (key == Key.Left) { actions.GoBack(); return true; }
                if (key == Key.Right) { actions.GoForward(); return true; }
                break;
        }

        return false;
    }

    /// <summary>
    /// Перегрузка для CoreWebView2.AcceleratorKeyPressed: клавиши, нажатые,
    /// пока фокус внутри WebView2 (там свои HWND, WPF-обработка не срабатывает).
    /// </summary>
    public static bool TryHandle(int virtualKey, string? keyModifiers, IBrowserActions actions)
    {
        var modifiers = ModifierKeys.None;
        if (!string.IsNullOrEmpty(keyModifiers))
        {
            if (keyModifiers.Contains("CONTROL")) modifiers |= ModifierKeys.Control;
            if (keyModifiers.Contains("SHIFT")) modifiers |= ModifierKeys.Shift;
            if (keyModifiers.Contains("ALT")) modifiers |= ModifierKeys.Alt;
        }

        return TryHandle((Key)virtualKey, modifiers, actions);
    }
}

