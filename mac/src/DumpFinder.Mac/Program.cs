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
            // --report [--lang xx] [--format text|html|json|md]
            string format = "text";
            for (int i = 1; i + 1 < args.Length; i++)
            {
                if (args[i] == "--lang") Loc.SetLanguage(args[++i]);
                else if (args[i] == "--format") format = args[++i];
            }
            var result = DiagnosticsEngine.ScanAsync().GetAwaiter().GetResult();
            var summary = CrashTimeline.Build(result, DetectionData.Embedded, DateTime.Now);
            Console.Write(format switch
            {
                "html" => ReportBuilder.Html(result, summary),
                "json" => ReportBuilder.Json(result, summary),
                "md" => ReportBuilder.Markdown(result, summary),
                _ => ReportBuilder.Text(result, summary),
            });
            return result.Findings.Count(f => f.Severity == Severity.Critical);
        }

        if (args.Length > 1 && args[0] == "--screenshot")
        {
            MainWindow.ScreenshotFolder = Path.GetFullPath(args[1]);
            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--theme" && i + 1 < args.Length) MainWindow.ScreenshotTheme = args[++i];
                else if (args[i] == "--lang" && i + 1 < args.Length) MainWindow.ScreenshotLang = args[++i];
                else if (args[i] == "--collapsed") MainWindow.ScreenshotCollapsed = true;
            }
        }

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
