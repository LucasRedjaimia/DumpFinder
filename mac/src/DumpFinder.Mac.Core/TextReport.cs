using System.Text;

namespace DumpFinder.Mac.Core;

/// <summary>Plain-text report (CLI output and "Copy report").</summary>
public static class TextReport
{
    public static string Build(ScanResult r)
    {
        var sb = new StringBuilder();
        var hw = r.Hardware;
        sb.AppendLine($"Dump Finder for Mac — scan of {r.ScannedAt:yyyy-MM-dd HH:mm}");
        sb.AppendLine($"{hw.MachineName} ({hw.ModelId}) · {hw.Chip} · {hw.Memory} · {hw.OsVersion}");
        if (r.Boot.BootTime is { } bt) sb.AppendLine($"Started {bt:yyyy-MM-dd HH:mm} · boot reason: {Or(r.Boot.BootReason)} · last shutdown: {Or(r.Boot.ShutdownReason)}");
        sb.AppendLine();

        sb.AppendLine("== Findings");
        foreach (var f in r.Findings)
        {
            sb.AppendLine($"[{Label(f.Severity)}] {f.Category} · {f.Title}");
            foreach (var l in f.Detail.Split('\n')) sb.AppendLine("    " + l);
            if (f.Advice.Length > 0) sb.AppendLine("    → " + f.Advice);
        }

        sb.AppendLine();
        sb.AppendLine($"== Kernel panics ({r.Panics.Count} in 90 days)");
        foreach (var p in r.Panics)
        {
            sb.AppendLine($"{p.Time:yyyy-MM-dd HH:mm}  {p.Message}");
            if (p.BacktraceExtensions.Count > 0) sb.AppendLine("    extensions: " + string.Join(", ", p.BacktraceExtensions));
        }

        sb.AppendLine();
        var byKind = r.Reports.GroupBy(x => x.Kind).ToDictionary(g => g.Key, g => g.Count());
        sb.AppendLine($"== Reports (90 days): {byKind.GetValueOrDefault(ReportKind.Crash)} crashes, {byKind.GetValueOrDefault(ReportKind.MemoryPressure)} low-memory events, {byKind.GetValueOrDefault(ReportKind.Other)} other");
        foreach (var g in r.Reports.Where(x => x.Kind == ReportKind.Crash).GroupBy(x => x.Process).OrderByDescending(g => g.Count()).Take(15))
            sb.AppendLine($"{g.Count(),4} × {g.Key}{(g.First().FirstParty ? " (Apple)" : "")}");

        sb.AppendLine();
        sb.AppendLine($"== Third-party extensions: {r.KernelExtensions.Count} kernel, {r.SystemExtensions.Count} system");
        foreach (var k in r.KernelExtensions) sb.AppendLine($"kext {k.BundleId} {k.Version} {(k.Loaded ? "loaded" : "not loaded")}");
        foreach (var s in r.SystemExtensions) sb.AppendLine($"sysext {s.BundleId} {s.Version} [{s.State}]");

        sb.AppendLine();
        sb.AppendLine("== Hardware");
        foreach (var d in hw.Drives) sb.AppendLine($"Drive {d.Name} ({(d.Internal ? "internal" : "external")}): SMART {Or(d.SmartStatus)}");
        if (hw.BootVolumeSize is long size && hw.BootVolumeFree is long free) sb.AppendLine($"Startup disk: {free / 1e9:0.#} GB free of {size / 1e9:0.#} GB");
        if (hw.HasBattery) sb.AppendLine($"Battery: {hw.BatteryMaxCapacityPct} % capacity, {hw.BatteryCycles} cycles, {Or(hw.BatteryCondition)}");
        sb.AppendLine($"SIP: {OnOff(hw.SipEnabled)} · Gatekeeper: {OnOff(hw.GatekeeperEnabled)}");

        if (r.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("== Not read");
            foreach (var e in r.Errors) sb.AppendLine(e);
        }
        return sb.ToString();
    }

    public static string Label(Severity s) => s switch
    {
        Severity.Critical => "CRITICAL",
        Severity.Warning => "WARNING",
        Severity.Info => "info",
        _ => "ok",
    };

    static string Or(string s) => s.Length > 0 ? s : "—";
    static string OnOff(bool? b) => b is null ? "unknown" : b.Value ? "on" : "off";
}
