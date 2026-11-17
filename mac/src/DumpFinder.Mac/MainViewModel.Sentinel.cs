using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>Settings › Monitoring: Sentinel and "Open at login" (Windows ToggleSentinel / ToggleAutostart).</summary>
public sealed partial class MainViewModel
{
    /// <summary>The window turns the menu bar icon and the report watcher on or off.</summary>
    public event Action<bool>? SentinelToggled;

    public bool SentinelOn => _settings.SentinelEnabled;
    public string SentinelLabel => Loc.Plain(Loc.S(SentinelOn ? "health.sentinel_active" : "health.sentinel_off"));
    public bool LoginOn => !Offline && LoginItem.IsEnabled;
    public string LoginLabel => Loc.Plain(Loc.S(LoginOn ? "sentinel.on" : "sentinel.off"));

    public RelayCommand ToggleSentinelCommand => _toggleSentinel ??= new(() => { ToggleSentinel(); return Task.CompletedTask; });
    public RelayCommand ToggleLoginCommand => _toggleLogin ??= new(() => { ToggleLogin(); return Task.CompletedTask; });
    RelayCommand? _toggleSentinel, _toggleLogin;

    void ToggleSentinel()
    {
        _settings.SentinelEnabled = !_settings.SentinelEnabled;
        _settings.Save();
        SentinelToggled?.Invoke(SentinelOn);
        RaiseMonitoring();
        ShowToast(SentinelOn ? "success" : "accent", Loc.S(SentinelOn ? "health.sentinel_enabled_background_tray_monitor" : "health.sentinel_disabled"));
    }

    void ToggleLogin()
    {
        bool target = !LoginItem.IsEnabled;
        if (target && !SentinelOn) ToggleSentinel();   // opening at login only makes sense with the Sentinel on
        var exe = McpServer.ExePath;
        bool ok = LoginItem.Set(target, exe);
        RaiseMonitoring();
        ShowToast(ok ? "success" : "warn", !ok
            ? Loc.S(McpServer.IsTranslocated(exe) ? "mac.mcp.move_to_applications" : "sentinel.could_not_change_automatic_start")
            : Loc.S(target ? "sentinel.dump_finder_will_start_with_windows" : "sentinel.automatic_start_disabled"));
    }

    void RaiseMonitoring()
    {
        foreach (var n in new[] { nameof(SentinelOn), nameof(SentinelLabel), nameof(LoginOn), nameof(LoginLabel) }) Raise(n);
    }

    /// <summary>In-app navigation from a notification ("nav:hardware"…).</summary>
    public void Navigate(string target) => RunAction(target);

    /// <summary>Live panic alert: the newest kernel panic of the last scan, in Crashes.</summary>
    public void OpenNewestPanic()
    {
        var panic = _summary?.Crashes.FirstOrDefault(c => c.Panic is not null);
        if (panic is not null) OpenCrash(panic.Key);
        else Page = TabCrashes;
    }
}
