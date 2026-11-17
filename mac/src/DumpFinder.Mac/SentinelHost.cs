using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DumpFinder.Mac.Core;
using WinCrashDoctor;

namespace DumpFinder.Mac;

/// <summary>
/// The Sentinel (Windows TraySentinel + MainWindow.Sentinel.cs): a menu bar icon (Open · Rescan · Quit),
/// live alerts when macOS writes a kernel panic or a low-memory report, the window hiding instead of
/// quitting, and the login start (--tray) that tells about severe crashes that happened meanwhile.
/// A click on a notification brings the app to the front and shows what the alert was about.
/// </summary>
internal sealed class SentinelHost : IDisposable
{
    readonly MainWindow _window;
    readonly MainViewModel _vm;
    readonly CrashWatcher _watcher = new();
    TrayIcon? _tray;
    NativeMenuItem? _miOpen, _miScan, _miQuit;
    DateTime _lastAlert = DateTime.MinValue;
    bool _hintShown;
    /// <summary>What the next activation of the app (notification click) should show.</summary>
    Action? _pendingClick;

    public bool IsEnabled { get; private set; }
    /// <summary>Set by Quit (menu bar or ⌘Q): closing the window then really quits.</summary>
    public bool ForceExit { get; set; }

    public SentinelHost(MainWindow window, MainViewModel vm)
    {
        _window = window;
        _vm = vm;
        _watcher.Alert += (kind, path) => Dispatcher.UIThread.Post(() => OnLiveAlert(kind, path));
        Loc.LanguageChanged += RefreshMenu;
        if (Application.Current?.TryGetFeature(typeof(IActivatableLifetime)) is IActivatableLifetime act)
            act.Activated += (_, e) =>
            {
                // Notification click (the app becomes active) or Dock icon click: show the window and what the alert was about.
                if (_pendingClick is not null || !_window.IsVisible) ShowWindow();
                var click = _pendingClick;
                _pendingClick = null;
                click?.Invoke();
            };
    }

    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled) return;
        IsEnabled = enabled;
        if (enabled)
        {
            EnsureTray();
            _tray!.IsVisible = true;
            _watcher.Start();
        }
        else
        {
            _watcher.Stop();
            if (_tray is not null) _tray.IsVisible = false;
            MacNative.SetDockIconVisible(true);
        }
    }

    void EnsureTray()
    {
        if (_tray is not null) return;
        _miOpen = new NativeMenuItem();
        _miOpen.Click += (_, _) => ShowWindow();
        _miScan = new NativeMenuItem();
        _miScan.Click += (_, _) => { ShowWindow(); _ = _vm.ScanAsync(); };
        _miQuit = new NativeMenuItem();
        _miQuit.Click += (_, _) => Quit();
        var menu = new NativeMenu();
        menu.Add(_miOpen);
        menu.Add(_miScan);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(_miQuit);
        _tray = new TrayIcon { Icon = MenuBarIcon(), Menu = menu, IsVisible = false };
        MacOSProperties.SetIsTemplateIcon(_tray, true);
        if (Application.Current is { } app) TrayIcon.SetIcons(app, new TrayIcons { _tray });
        RefreshMenu();
    }

    void RefreshMenu()
    {
        if (_miOpen is null) return;
        _miOpen.Header = Loc.Plain(Loc.S("tray_sentinel.open_dump_finder"));
        _miScan!.Header = Loc.Plain(Loc.S("tray_sentinel.rescan_system"));
        _miQuit!.Header = Loc.Plain(Loc.S("tray_sentinel.quit"));
        if (_tray is not null) _tray.ToolTipText = "Dump Finder — " + Loc.S("tray_sentinel.sentinel_active");
    }

    /// <summary>The bars of the logo in black on transparent: macOS tints a template image for the menu bar.</summary>
    static WindowIcon MenuBarIcon()
    {
        var bars = Geometry.Parse("M134,132 H378 A22,22 0 0 1 400,154 V174 A22,22 0 0 1 378,196 H134 A22,22 0 0 1 112,174 V154 A22,22 0 0 1 134,132 Z " +
                                  "M134 224H262L250 240L266 256L252 272L262 288H134A22 22 0 0 1 112 266V246A22 22 0 0 1 134 224Z " +
                                  "M292 214H414A22 22 0 0 1 436 236V256A22 22 0 0 1 414 278H292L282 262L296 246L280 230Z " +
                                  "M134,316 H378 A22,22 0 0 1 400,338 V358 A22,22 0 0 1 378,380 H134 A22,22 0 0 1 112,358 V338 A22,22 0 0 1 134,316 Z");
        using var bmp = new RenderTargetBitmap(new PixelSize(44, 44), new Vector(144, 144));
        using (var dc = bmp.CreateDrawingContext())
        using (dc.PushTransform(Matrix.CreateTranslation(-100, -100) * Matrix.CreateScale(29.0 / 380, 29.0 / 380)))
            dc.DrawGeometry(Brushes.Black, null, bars);
        var ms = new MemoryStream();
        bmp.Save(ms);
        ms.Position = 0;
        return new WindowIcon(ms);
    }

    // ---------- Window ----------
    public void ShowWindow()
    {
        MacNative.SetDockIconVisible(true);
        if (!_window.IsVisible) _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    /// <summary>Windows OnWindowClosing: with the Sentinel on, the window hides and the menu bar icon stays.</summary>
    public bool HideInsteadOfClose()
    {
        if (!IsEnabled || ForceExit) return false;
        _window.Hide();
        MacNative.SetDockIconVisible(false);
        if (!_hintShown)
        {
            _hintShown = true;
            MacNative.Notify("Dump Finder", Loc.S("tray_sentinel.sentinel_active_in_the_background_0"));
        }
        return true;
    }

    public void Quit()
    {
        ForceExit = true;
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    // ---------- Alerts ----------
    void OnLiveAlert(LiveAlert kind, string path)
    {
        if (!IsEnabled || (DateTime.Now - _lastAlert).TotalSeconds < 6) return;
        _lastAlert = DateTime.Now;
        var (title, body) = kind == LiveAlert.Panic
            ? (Loc.S("mac.sentinel.panic_title"), Loc.S("mac.sentinel.panic_body"))
            : (Loc.S("mac.sentinel.memory_title"), Loc.S("mac.sentinel.memory_body"));
        // Panic: rescan, then open that crash. Memory shortage: the Hardware page (memory card).
        Notify(title, body, kind == LiveAlert.Panic
            ? async () => { await _vm.ScanAsync(); _vm.OpenNewestPanic(); }
            : () => _vm.Navigate("nav:hardware"));
    }

    public void Notify(string title, string body, Action onClick)
    {
        _pendingClick = onClick;
        MacNative.Notify(title, body);
    }

    /// <summary>
    /// Login start (Windows StartInTray): no window; after 40 s a scan, and a notification if severe crashes
    /// appeared since the last time the app looked. The window still shows them as new.
    /// </summary>
    public async Task StartInBackgroundAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(40));
        if (_window.IsVisible) return;   // opened meanwhile: the window shows the results itself
        var r = await DiagnosticsEngine.ScanAsync();
        var now = DateTime.Now;
        var s = CrashTimeline.Build(r, DetectionData.Embedded, now);
        var recent = s.Crashes.Where(c => (now - c.Time).TotalDays <= Stability.WindowDays).ToList();
        var fresh = History.PeekNew(recent.Select(c => c.Key));
        var severe = recent.Where(c => fresh.Contains(c.Key) && Stability.IsSevere(c.Category)).OrderByDescending(c => c.Time).ToList();
        if (severe.Count == 0 || !IsEnabled) return;
        var last = severe[0];
        string when = last.Time.Date == now.Date ? Loc.S("sentinel.today_at") + last.Time.ToString("HH:mm")
            : last.Time.Date == now.Date.AddDays(-1) ? Loc.S("sentinel.yesterday_at") + last.Time.ToString("HH:mm")
            : last.Time.ToString("g", Loc.Culture);
        var key = last.Key;
        Notify(Loc.F("sentinel.your_pc_crashed", when),
            last.Title + (severe.Count > 1 ? Loc.F("sentinel.more", severe.Count - 1) : "") + "\n" + Loc.S("sentinel.click_to_see_the_analysis"),
            async () => { await _vm.ScanAsync(); _vm.OpenCrash(key); });
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _tray?.Dispose();
    }
}
