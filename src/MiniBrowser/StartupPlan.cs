using MiniBrowser.Models;
using MiniBrowser.Services;

namespace MiniBrowser;

/// <summary>
/// Решает, как запускать браузер: восстановить сессию, открыть домашнюю
/// страницу или URL из аргументов командной строки. Логика вынесена сюда,
/// чтобы MainWindow не знал деталей хранения сессии.
/// </summary>
public static class StartupPlan
{
    public sealed record Decision(
        StartupAction Action,
        IReadOnlyList<TabGroup>? RestoredGroups = null,
        Tab? ActiveTab = null,
        string[]? StartupUrls = null);

    public enum StartupAction
    {
        RestoreSession,
        HomePage,
        StartupUrls
    }

    public static Decision Decide(
        bool restoreSessionEnabled,
        SessionService sessionService,
        string[] startupUrls)
    {
        if (startupUrls.Length > 0)
            return new Decision(StartupAction.StartupUrls, StartupUrls: startupUrls);

        if (!restoreSessionEnabled)
            return new Decision(StartupAction.HomePage);

        var (snapshot, groups, activeTab) = sessionService.Load();
        if (snapshot is not null && groups.Count > 0)
            return new Decision(StartupAction.RestoreSession, groups, activeTab);

        return new Decision(StartupAction.HomePage);
    }
}