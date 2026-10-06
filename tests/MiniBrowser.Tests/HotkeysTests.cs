using System.Windows.Input;
using MiniBrowser.Services;
using Xunit;

public class HotkeysTests
{
    private sealed class FakeActions : IBrowserActions
    {
        public List<string> Calls { get; } = new();
        public bool IsMenuOpen { get; set; }
        public bool IsManualFullscreen { get; set; }
        public void NewTab() => Calls.Add("NewTab");
        public void CloseActiveTab() => Calls.Add("CloseActiveTab");
        public void FocusAddressBar() => Calls.Add("FocusAddressBar");
        public void NextTab() => Calls.Add("NextTab");
        public void PrevTab() => Calls.Add("PrevTab");
        public void AddBookmark() => Calls.Add("AddBookmark");
        public void GoBack() => Calls.Add("GoBack");
        public void GoForward() => Calls.Add("GoForward");
        public void Reload() => Calls.Add("Reload");
        public void ToggleFullscreen() => Calls.Add("ToggleFullscreen");
        public void ExitFullscreen() => Calls.Add("ExitFullscreen");
        public void ToggleMenu() => Calls.Add("ToggleMenu");
        public void ShowHistory() => Calls.Add("ShowHistory");
        public void ShowBookmarks() => Calls.Add("ShowBookmarks");
        public void ToggleAdBlock() => Calls.Add("ToggleAdBlock");
        public void ToggleTabStrip() => Calls.Add("ToggleTabStrip");
        bool IBrowserActions.IsManualFullscreen => IsManualFullscreen;
    }

    [Fact] public void CtrlShiftL_TogglesTabStrip()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.L, ModifierKeys.Control | ModifierKeys.Shift, a));
        Assert.Equal(new[] { "ToggleTabStrip" }, a.Calls);
    }

    [Fact] public void CtrlShiftA_TogglesAdBlock()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.A, ModifierKeys.Control | ModifierKeys.Shift, a));
        Assert.Equal(new[] { "ToggleAdBlock" }, a.Calls);
    }

    [Fact] public void PlainA_IsNotSwallowed()
    {
        // Ctrl+Shift+A не должен ловить обычную «A»: она нужна странице
        // (например, в поисковой строке Google).
        var a = new FakeActions();
        Assert.False(Hotkeys.TryHandle(Key.A, ModifierKeys.None, a));
        Assert.Empty(a.Calls);
    }

    [Fact] public void CtrlA_IsNotSwallowed()
    {
        var a = new FakeActions();
        Assert.False(Hotkeys.TryHandle(Key.A, ModifierKeys.Control, a));
        Assert.Empty(a.Calls);
    }

    [Fact] public void CtrlM_TogglesMenu()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.M, ModifierKeys.Control, a));
        Assert.Equal(new[] { "ToggleMenu" }, a.Calls);
    }

    [Fact] public void CtrlH_ShowsHistory()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.H, ModifierKeys.Control, a));
        Assert.Equal(new[] { "ShowHistory" }, a.Calls);
    }

    [Fact] public void CtrlShiftB_ShowsBookmarks()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.B, ModifierKeys.Control | ModifierKeys.Shift, a));
        Assert.Equal(new[] { "ShowBookmarks" }, a.Calls);
    }

    [Fact] public void CtrlH_DoesNotClashWithExistingBindings()
    {
        var a = new FakeActions();
        Hotkeys.TryHandle(Key.H, ModifierKeys.Control, a);
        // Именно ShowHistory, а не чужое действие: Assert.Single не поймал бы
        // подмену одного вызова другим.
        Assert.Equal(new[] { "ShowHistory" }, a.Calls);
    }

    [Fact] public void Escape_WhenMenuOpen_ClosesMenu()
    {
        var a = new FakeActions { IsMenuOpen = true };
        Assert.True(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Equal(new[] { "ToggleMenu" }, a.Calls);
    }

    [Fact] public void Escape_WhenMenuOpenBeatsFullscreenExit()
    {
        // Панель открыта и одновременно включён F11: Esc обязан закрыть панель,
        // а не выйти из полноэкранного режима — это ловит неверный порядок блоков.
        var a = new FakeActions { IsMenuOpen = true, IsManualFullscreen = true };
        Assert.True(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Equal(new[] { "ToggleMenu" }, a.Calls);
    }

    [Fact] public void Escape_WhenMenuClosed_ReachesFullscreenHandler()
    {
        var a = new FakeActions { IsMenuOpen = false, IsManualFullscreen = true };
        Assert.True(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Equal(new[] { "ExitFullscreen" }, a.Calls);
    }

    [Fact] public void Escape_WhenMenuClosedAndNotFullscreen_NotHandled()
    {
        // Esc обязан дойти до страницы: иначе HTML5-полноэкранное видео
        // нельзя было бы закрыть.
        var a = new FakeActions { IsMenuOpen = false, IsManualFullscreen = false };
        Assert.False(Hotkeys.TryHandle(Key.Escape, ModifierKeys.None, a));
        Assert.Empty(a.Calls);
    }

    [Fact] public void ExistingHotkeys_StillWork()
    {
        var a = new FakeActions();
        Assert.True(Hotkeys.TryHandle(Key.T, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.W, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.L, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.D, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.R, ModifierKeys.Control, a));
        Assert.True(Hotkeys.TryHandle(Key.F11, ModifierKeys.None, a));
        Assert.Equal(new[] { "NewTab", "CloseActiveTab", "FocusAddressBar",
            "AddBookmark", "Reload", "ToggleFullscreen" }, a.Calls);
    }

    [Fact] public void ModifierlessB_IsNotSwallowed()
    {
        var a = new FakeActions();
        Assert.False(Hotkeys.TryHandle(Key.B, ModifierKeys.None, a));
        Assert.Empty(a.Calls);
    }
}
