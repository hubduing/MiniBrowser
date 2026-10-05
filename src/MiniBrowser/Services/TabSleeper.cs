using System.Windows.Threading;

namespace MiniBrowser.Services;

/// <summary>
/// Политика усыпления неактивных вкладок: вкладка без фокуса дольше
/// <see cref="SleepDelay"/> выгружает WebView2, освобождая память.
/// </summary>
public sealed class TabSleeper
{
    public static readonly TimeSpan SleepDelay = TimeSpan.FromMinutes(5);

    private readonly TabManager _owner;
    private readonly DispatcherTimer _timer;

    public TabSleeper(TabManager owner)
    {
        _owner = owner;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += OnTick;
    }

    public void Start() => _timer.Start();

    public void Stop() => _timer.Stop();

    private void OnTick(object? sender, EventArgs e)
    {
        foreach (var tab in _owner.Tabs.ToArray())
        {
            if (tab.IsActive || tab.IsAsleep || tab.DeactivatedAt is null) continue;
            if (DateTime.UtcNow - tab.DeactivatedAt > SleepDelay)
                _owner.Sleep(tab);
        }
    }
}
