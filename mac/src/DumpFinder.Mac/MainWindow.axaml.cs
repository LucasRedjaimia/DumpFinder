using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

public partial class MainWindow : Window, IPlatform
{
    readonly MainViewModel _vm;
    readonly SentinelHost? _sentinel;

    /// <summary>
    /// --screenshot &lt;folder&gt; [--theme light|dark] [--lang xx] [--collapsed]: scans, saves every section as a PNG,
    /// then quits. Same switches as the Windows capture harness; never changes the saved settings.
    /// </summary>
    public static string? ScreenshotFolder { get; set; }
    public static string? ScreenshotTheme { get; set; }
    public static string? ScreenshotLang { get; set; }
    public static bool ScreenshotCollapsed { get; set; }

    public MainWindow() : this(new AppSettings()) { }

    public MainWindow(AppSettings settings)
    {
        InitializeComponent();
        if (ScreenshotFolder is not null)
        {
            // Capture settings live in a copy: nothing is written back.
            settings = new AppSettings
            {
                Language = ScreenshotLang ?? settings.Language, Theme = ScreenshotTheme switch { "dark" => 1, "light" => 2, _ => settings.Theme },
                HasCompletedTour = true, SeenTips = "tab1,tab2,tab5",
            };
            MainViewModel.Offline = true;
            // Captures never write to the user's data: they work on a temporary copy (scans, journal, stress runs).
            var capture = Path.Combine(Path.GetTempPath(), "dumpfinder-capture-" + Environment.ProcessId);
            CopyTree(AppStore.Root, capture);
            AppStore.Root = capture;
            if (ScreenshotLang is not null) Loc.SetLanguage(ScreenshotLang);
            App.ApplyTheme(settings.Theme);
        }
        _vm = new MainViewModel(settings, this);
        if (ScreenshotFolder is not null && ScreenshotCollapsed) _vm.SidebarCollapsed = true;
        DataContext = _vm;
        if (ScreenshotFolder is null)
        {
            _sentinel = new SentinelHost(this, _vm);
            _vm.SentinelToggled += _sentinel.SetEnabled;
            _sentinel.SetEnabled(settings.SentinelEnabled);
            LoginItem.RefreshPathIfEnabled(McpServer.ExePath);
        }
        // Red button: with the Sentinel on, the window hides and the menu bar icon stays (Windows: tray).
        Closing += (_, e) => { if (_sentinel?.HideInsteadOfClose() == true) e.Cancel = true; };
        Closed += (_, _) =>
        {
            _sentinel?.Dispose();
            if (Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        };
        // Introduction: Right / Enter = next, Left = back (Windows Onboarding keys); Escape is a key binding.
        AddHandler(KeyDownEvent, (_, e) =>
        {
            if (!_vm.OnboardingVisible) return;
            if (e.Key is Avalonia.Input.Key.Right or Avalonia.Input.Key.Enter) { _vm.OnboardingNextCommand.Execute(null); e.Handled = true; }
            else if (e.Key == Avalonia.Input.Key.Left) { _vm.OnboardingBackCommand.Execute(null); e.Handled = true; }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            _ = _vm.StartupUpdateCheckAsync();
            await _vm.ScanAsync();
            if (ScreenshotFolder is { } folder) await CaptureAsync(folder);
        };
    }

    /// <summary>
    /// Title bar drag (the buttons handle their own clicks, so they never get here): move the window, or zoom it on a
    /// double-click, like the native macOS title bar.
    /// </summary>
    void OnTitleBarPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    /// <summary>⌘Q or the menu bar Quit: closing the window then really quits.</summary>
    public void AllowExit()
    {
        if (_sentinel is not null) _sentinel.ForceExit = true;
    }

    /// <summary>Login start (--tray): no window, menu bar icon, background check for crashes since last time.</summary>
    public void StartInBackground()
    {
        if (_sentinel is null || !_sentinel.IsEnabled) return;
        MacNative.SetDockIconVisible(false);
        _ = _sentinel.StartInBackgroundAsync();
    }

    // ---------- IPlatform ----------
    public async Task CopyTextAsync(string text)
    {
        if (Clipboard is { } cb) await cb.SetTextAsync(text);
    }

    public async Task<string?> SaveTextAsync(string suggestedName, string extension, string content)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = suggestedName + "." + extension,
            DefaultExtension = extension,
            ShowOverwritePrompt = true,
            SuggestedStartLocation = await StorageProvider.TryGetWellKnownFolderAsync(WellKnownFolder.Documents),
        });
        var path = file?.TryGetLocalPath();
        if (path is null) return null;
        await File.WriteAllTextAsync(path, content);
        return path;
    }

    public async Task<string?> PickFileAsync(string[] patterns)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Crash reports") { Patterns = patterns } },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(DiagnosticReports.SystemDir),
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    /// <summary>Opens a file, a folder (Finder) or an https link (default browser) with /usr/bin/open.</summary>
    public void Open(string pathOrUrl)
    {
        bool url = pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                   || pathOrUrl.StartsWith("x-apple.systempreferences:", StringComparison.Ordinal);
        if (!url && !File.Exists(pathOrUrl) && !Directory.Exists(pathOrUrl)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }); }
        catch (Exception e) { _vm.ShowToast("danger", e.Message); }
    }

    async Task CaptureAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        // Section, sub-tab (Crashes / Hardware / Tools), overlay (0 none, 1-3 introduction step, 4 update card), file name.
        var shots = new (int Page, int Sub, int Overlay, string Name)[]
        {
            (0, 0, 0, "1-home"), (1, 0, 0, "2-crashes"), (1, 1, 0, "2b-common-crashes"),
            (2, 0, 0, "3-hardware"), (2, 1, 0, "3b-conflicts"), (2, 2, 0, "3c-extensions"), (2, 3, 0, "3d-stability"), (2, 3, 7, "3e-stability-live"), (2, 3, 8, "3f-stability-result"), (3, 0, 0, "4-fixes"),
            (4, 0, 0, "5-optimize"), (4, 1, 0, "5b-startup"), (4, 2, 0, "5c-cleanup"), (4, 3, 9, "5d-storage"), (4, 4, 0, "5e-myths"),
            (1, 0, 5, "2c-ai-check-up"), (5, 0, 0, "6-tools"), (5, 1, 0, "6b-report"), (5, 2, 0, "6c-history"), (6, 0, 0, "7-settings"), (6, 0, 6, "7b-settings-ai"),
            (0, 0, 1, "8-intro-1"), (0, 0, 2, "8-intro-2"), (0, 0, 3, "8-intro-3"), (0, 0, 4, "9-update"),
        };
        foreach (var (page, sub, overlay, name) in shots)
        {
            _vm.Page = page;
            if (page == 1) _vm.CrashTab = sub;
            if (page == 2) _vm.HardwareTab = sub;
            if (page == 5) _vm.ToolsTab = sub;
            if (page == 4) _vm.OptimizeTab = sub;
            if (overlay == 9)
            {
                // Storage: a real (read-only) measure of the apps and the home folder, then the three views.
                await _vm.SpaceScanCommand.ExecuteAsync();
                await SaveCapture(Path.Combine(folder, "5d-storage-apps.png"));
                _vm.SpaceMode = 1;
                await Task.Delay(300);
                await SaveCapture(Path.Combine(folder, "5d-storage-folders.png"));
                _vm.SpaceMode = 2;
            }
            if (overlay == 7)
            {
                // A real one-minute graphics stage, captured after a few seconds, then stopped: the live view and the results.
                _vm.StressPlanIndex = (int)StressPlanKind.Gpu;
                _vm.StressMinutes = 1;
                _vm.StartStressCommand.Execute(null);
                await Task.Delay(7000);
            }
            if (overlay == 8)
            {
                _vm.StopStressCommand.Execute(null);
                for (int i = 0; i < 40 && _vm.IsStressRunning; i++) await Task.Delay(250);
            }
            if (page == 4 && sub == 2) await Task.Delay(4000);   // sizes of the cleanup items
            if (overlay is >= 1 and <= 3) { _vm.StartOnboarding(); _vm.OnboardingStep = overlay - 1; }
            if (overlay == 4) { _vm.OnboardingEndCommand.Execute(null); _vm.PreviewUpdate(SampleUpdate()); }
            if (overlay == 5) _vm.SystemReviewCommand.Execute(null);
            else if (page == 1) _vm.CloseSystemReviewCommand.Execute(null);
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            if (overlay == 6 && this.GetVisualDescendants().OfType<Pages.SettingsPage>().FirstOrDefault()?.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault() is { } sv)
                sv.ScrollToEnd();
            await Task.Delay(350);
            await SaveCapture(Path.Combine(folder, name + ".png"));
        }
        Close();
    }

    static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        if (!Directory.Exists(from)) return;
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(f, dest, overwrite: true);
        }
    }

    /// <summary>A made-up release to render the update card (capture only, never offered).</summary>
    static UpdateInfo SampleUpdate() => new(new Version(9, 9, 0), "mac-v9.9.0", "Dump Finder for Mac 9.9.0",
        "## What's new\n- Sample release notes rendered as on Windows\n  - nested bullet\n- **Bold** and `code` are dropped, [links](https://example.com) keep their text\n\n## Fixes\n- Another line",
        UpdateChecker.RepoUrl + "/releases", UpdateChecker.RepoUrl + "/releases", DateTime.Now);

    async Task SaveCapture(string path)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        using var bmp = new RenderTargetBitmap(new PixelSize((int)Bounds.Width * 2, (int)Bounds.Height * 2), new Vector(192, 192));
        bmp.Render(this);
        bmp.Save(path);
    }
}
