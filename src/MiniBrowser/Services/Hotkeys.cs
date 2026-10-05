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
    void ToggleFullscreen();
    void ExitFullscreen();
    void ToggleMenu();
    void ShowHistory();
    void ShowBookmarks();

    /// <summary>true — пользователь включил F11-режим (не HTML5-полноэкранный).</summary>
    bool IsManualFullscreen { get; }

    /// <summary>true — панель меню открыта (тогда Esc закрывает её).</summary>
    bool IsMenuOpen { get; }
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
                    case Key.M: actions.ToggleMenu(); return true;
                    case Key.H: actions.ShowHistory(); return true;
                    case Key.Tab: actions.NextTab(); return true;
                }
                break;

            case ModifierKeys.Control | ModifierKeys.Shift:
                if (key == Key.Tab) { actions.PrevTab(); return true; }
                if (key == Key.B) { actions.ShowBookmarks(); return true; }
                break;

            case ModifierKeys.Alt:
                if (key == Key.Left) { actions.GoBack(); return true; }
                if (key == Key.Right) { actions.GoForward(); return true; }
                break;
        }

        if (key == Key.F11)
        {
            actions.ToggleFullscreen();
            return true;
        }

        // Esc закрывает панель меню, но только когда она открыта: иначе он
        // обязан дойти до страницы и до логики выхода из полноэкранного режима.
        if (key == Key.Escape && modifiers == ModifierKeys.None && actions.IsMenuOpen)
        {
            actions.ToggleMenu();
            return true;
        }

        // Esc: выход из F11-полноэкранного режима. Из HTML5-полноэкранного
        // режима страницу выкидывает сам движок, поэтому Esc обязан дойти до
        // страницы — перехватываем его только в ручном режиме.
        if (key == Key.Escape && modifiers == ModifierKeys.None && actions.IsManualFullscreen)
        {
            actions.ExitFullscreen();
            return true;
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

