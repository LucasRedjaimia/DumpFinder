using Avalonia;
using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Command-line modes, same spirit as the Windows --*-selftest switches.
        if (args.Length > 0 && args[0] == "--selftest")
            return SelfTest.Run(Console.Out);
        if (args.Length > 0 && args[0] == "--report")
        {
            var result = DiagnosticsEngine.ScanAsync().GetAwaiter().GetResult();
            Console.Write(TextReport.Build(result));
            return result.Findings.Count(f => f.Severity == Severity.Critical);
        }

        if (args.Length > 1 && args[0] == "--screenshot")
            MainWindow.ScreenshotFolder = Path.GetFullPath(args[1]);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
