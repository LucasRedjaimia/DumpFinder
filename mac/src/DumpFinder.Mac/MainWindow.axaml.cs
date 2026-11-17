using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

public partial class MainWindow : Window
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
        _vm = new MainViewModel(settings, async text =>
        {
            if (Clipboard is { } cb) await cb.SetTextAsync(text);
        });
        if (ScreenshotFolder is not null && ScreenshotCollapsed) _vm.SidebarCollapsed = true;
        DataContext = _vm;
        Opened += async (_, _) =>
        {
            await _vm.ScanAsync();
            if (ScreenshotFolder is { } folder) await CaptureAsync(folder);
        };
    }

    async Task CaptureAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        string[] names = { "home", "crashes", "hardware", "fixes", "optimize", "tools", "settings" };
        foreach (int page in new[] { 0, 1, 2, 3, 6 })
        {
            _vm.Page = page;
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(350);
            using var bmp = new RenderTargetBitmap(new PixelSize((int)Bounds.Width * 2, (int)Bounds.Height * 2), new Vector(192, 192));
            bmp.Render(this);
            bmp.Save(Path.Combine(folder, $"{page + 1}-{names[page]}.png"));
        }
        Close();
    }
}
