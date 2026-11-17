using WinCrashDoctor;

namespace DumpFinder.Mac.Core;

/// <summary>One crash as shown in lists (Windows: CrashEntry).</summary>
public sealed class CrashEntry
{
    public required string Key { get; init; }
    public DateTime Time { get; init; }
    public CrashCategory Category { get; init; }
    public string Title { get; init; } = "";
    /// <summary>Technical line shown in monospace (code · culprit).</summary>
    public string TechLine { get; init; } = "";
    public bool IsNew { get; set; }
    public PanicReport? Panic { get; init; }
    public CrashReport? Report { get; init; }
}

/// <summary>Home data derived from a scan: the shared stability score, timeline and verdict.</summary>
public sealed class StabilitySummary
{
    public List<CrashEntry> Crashes { get; init; } = new();
    public int Score { get; init; }
    public StabilityLevel Level { get; init; }
    public List<StabilityDay> Days { get; init; } = new();
    public string VerdictTitle { get; init; } = "";
    public string VerdictBody { get; init; } = "";
    public int Crashes30 { get; init; }
    public int Severe30 { get; init; }
}

/// <summary>
/// Maps Mac reports onto the shared crash categories, so src/Core/Stability.cs scores a Mac exactly like a PC:
/// kernel panic = blue screen, watchdog / forced restart = unexpected reboot, Jetsam = memory exhaustion.
/// </summary>
public static class CrashTimeline
{
    static readonly string[] Browsers = { "Google Chrome", "Chrome", "Safari", "com.apple.WebKit", "Firefox", "Microsoft Edge", "Brave", "Arc", "Opera", "Vivaldi" };

    public static StabilitySummary Build(ScanResult r, DetectionData data, DateTime now)
    {
        var list = new List<CrashEntry>();

        foreach (var p in r.Panics)
        {
            if (p.Time is not { } t) continue;
            var thirdParty = p.BacktraceExtensions.FirstOrDefault(id => !id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase));
            var tech = string.Join(" · ", new[] { p.PatternId, p.PanickedProcess, thirdParty ?? "" }.Where(s => s.Length > 0));
            list.Add(new CrashEntry
            {
                Key = "panic:" + Path.GetFileName(p.File),
                Time = t.LocalDateTime,
                Category = CrashCategory.BSOD,
                Title = MacText.PanicTitle(p.PatternId, data),
                TechLine = tech.Length > 0 ? tech : "kernel panic",
                Panic = p,
            });
        }

        // Watchdog / forced restart with no panic report around the boot: an unexpected restart.
        if (r.Boot.Abnormal && r.Boot.BootTime is { } boot
            && !list.Any(c => c.Category == CrashCategory.BSOD && Math.Abs((c.Time - boot.LocalDateTime).TotalMinutes) < 30))
        {
            list.Add(new CrashEntry
            {
                Key = "boot:" + boot.ToUnixTimeSeconds(),
                Time = boot.LocalDateTime,
                Category = CrashCategory.UnexpectedReboot,
                Title = Loc.S("mac.crash.unexpected_restart"),
                TechLine = r.Boot.ShutdownReason.Length > 60 ? r.Boot.ShutdownReason[..60] + "…" : r.Boot.ShutdownReason,
            });
        }

        foreach (var c in r.Reports)
        {
            if (c.Time is not { } t) continue;
            CrashCategory cat;
            string title;
            if (c.Kind == ReportKind.MemoryPressure)
            {
                cat = CrashCategory.MemoryExhaustion;
                title = Loc.S("mac.crash.low_memory");
            }
            else if (c.Kind == ReportKind.Crash)
            {
                cat = Browsers.Any(b => c.Process.StartsWith(b, StringComparison.OrdinalIgnoreCase)) ? CrashCategory.BrowserGpu : CrashCategory.AppCrash;
                title = Loc.F("mac.crash.app_quit", c.Process);
            }
            else continue;
            list.Add(new CrashEntry
            {
                Key = "ips:" + Path.GetFileName(c.File),
                Time = t.LocalDateTime,
                Category = cat,
                Title = title,
                TechLine = string.Join(" · ", new[] { c.ExceptionType, c.Signal }.Where(s => s.Length > 0)),
                Report = c,
            });
        }

        list.Sort((a, b) => b.Time.CompareTo(a.Time));
        var events = list.Select(c => new CrashEventItem { Timestamp = c.Time, Category = c.Category }).ToList();
        int score = Stability.Score(events, now);
        var recent = list.Where(c => (now - c.Time).TotalDays <= Stability.WindowDays).ToList();
        var (title2, body) = Verdict(r, recent, data, now);

        return new StabilitySummary
        {
            Crashes = list,
            Score = score,
            Level = Stability.LevelOf(score),
            Days = Stability.Timeline(events, now),
            VerdictTitle = title2,
            VerdictBody = body,
            Crashes30 = recent.Count,
            Severe30 = recent.Count(c => Stability.IsSevere(c.Category)),
        };
    }

    /// <summary>Home verdict (Windows: DiagnosticsEngine.BuildSmartDiagnosis), most serious signal first.</summary>
    static (string Title, string Body) Verdict(ScanResult r, List<CrashEntry> recent, DetectionData data, DateTime now)
    {
        var panics = recent.Where(c => c.Category == CrashCategory.BSOD).ToList();
        if (panics.Count > 0)
        {
            var p = panics[0].Panic!;
            var kext = p.BacktraceExtensions.FirstOrDefault(id => !id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase));
            string body = kext is not null ? Loc.F("mac.verdict.panic_kext", kext)
                        : MacText.PanicExplanation(p.PatternId, data) is { Length: > 0 } ex ? ex
                        : Loc.S("mac.verdict.panic_body");
            return (Loc.P("mac.verdict.panic_title", panics.Count), body);
        }
        if (recent.Any(c => c.Category == CrashCategory.UnexpectedReboot))
            return (Loc.S("mac.verdict.restart_title"), Loc.S("mac.verdict.restart_body"));
        int jetsam = recent.Count(c => c.Category == CrashCategory.MemoryExhaustion);
        if (jetsam >= data.Thresholds.RepeatedCrashes)
            return (Loc.S("mac.verdict.memory_title"), Loc.S("mac.verdict.memory_body"));
        return (Loc.S("mac.verdict.stable_title"), Loc.S("mac.verdict.stable_body"));
    }

    public static string CategoryName(CrashCategory c) => Loc.S(c switch
    {
        CrashCategory.BSOD => "mac.category.panic",
        CrashCategory.UnexpectedReboot => "mac.category.restart",
        CrashCategory.MemoryExhaustion => "mac.category.memory",
        CrashCategory.BrowserGpu => "mac.category.browser",
        CrashCategory.GpuDriver => "mac.category.gpu",
        CrashCategory.StorageNvme => "mac.category.storage",
        CrashCategory.HardwareWhea => "mac.category.hardware",
        _ => "mac.category.app",
    });
}
