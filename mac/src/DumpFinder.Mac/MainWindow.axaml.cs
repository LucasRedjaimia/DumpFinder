using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace DumpFinder.Mac;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm;

    /// <summary>Set by --screenshot &lt;folder&gt;: scan, save every tab as a PNG, then quit.</summary>
    public static string? ScreenshotFolder { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel(async text =>
        {
            if (Clipboard is { } cb) await cb.SetTextAsync(text);
        });
        DataContext = _vm;
        Opened += async (_, _) =>
        {
            await _vm.ScanAsync();
            if (ScreenshotFolder is { } folder) await CaptureTabsAsync(folder);
        };
    }

    async Task CaptureTabsAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        for (int i = 0; i < Tabs.ItemCount; i++)
        {
            Tabs.SelectedIndex = i;
            // Let layout and rendering settle on the new tab.
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(300);
            var size = new PixelSize((int)Bounds.Width * 2, (int)Bounds.Height * 2);
            using var bmp = new RenderTargetBitmap(size, new Vector(192, 192));
            bmp.Render(this);
            var name = (Tabs.Items[i] as TabItem)?.Header?.ToString()?.ToLowerInvariant() ?? i.ToString();
            bmp.Save(Path.Combine(folder, $"{i + 1}-{name}.png"));
        }
        Close();
    }
}
