using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

public partial class MainWindow : Window, IPlatform
{
    readonly MainViewModel _vm;

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
            settings = new AppSettings { Language = ScreenshotLang ?? settings.Language, Theme = ScreenshotTheme switch { "dark" => 1, "light" => 2, _ => settings.Theme } };
            if (ScreenshotLang is not null) Loc.SetLanguage(ScreenshotLang);
            App.ApplyTheme(settings.Theme);
        }
        _vm = new MainViewModel(settings, this);
        if (ScreenshotFolder is not null && ScreenshotCollapsed) _vm.SidebarCollapsed = true;
        DataContext = _vm;
        Opened += async (_, _) =>
        {
            await _vm.ScanAsync();
            if (ScreenshotFolder is { } folder) await CaptureAsync(folder);
        };
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
        bool url = pathOrUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        if (!url && !File.Exists(pathOrUrl) && !Directory.Exists(pathOrUrl)) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("/usr/bin/open") { ArgumentList = { pathOrUrl }, UseShellExecute = false }); }
        catch (Exception e) { _vm.ShowToast("danger", e.Message); }
    }

    async Task CaptureAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        // Section, crash sub-tab, file name.
        var shots = new (int Page, int CrashTab, string Name)[]
        {
            (0, 0, "1-home"), (1, 0, "2-crashes"), (1, 1, "2b-common-crashes"), (2, 0, "3-hardware"), (3, 0, "4-fixes"), (6, 0, "7-settings"),
        };
        foreach (var (page, crashTab, name) in shots)
        {
            _vm.Page = page;
            _vm.CrashTab = crashTab;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(350);
            await SaveCapture(Path.Combine(folder, name + ".png"));
        }
        _vm.CrashTab = 0;
        Close();
    }

    async Task SaveCapture(string path)
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        using var bmp = new RenderTargetBitmap(new PixelSize((int)Bounds.Width * 2, (int)Bounds.Height * 2), new Vector(192, 192));
        bmp.Render(this);
        bmp.Save(path);
    }
}
