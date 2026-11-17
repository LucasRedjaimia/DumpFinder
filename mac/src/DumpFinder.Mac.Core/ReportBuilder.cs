using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using WinCrashDoctor;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Exports of a scan, same formats and look as the Windows ReportBuilder: self-contained HTML (print-friendly),
/// JSON, Markdown and plain text. Every export goes through Privacy.Redact.
/// </summary>
public static class ReportBuilder
{
    static string Version => Assembly.GetEntryAssembly()?.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
    static string H(string? s) => WebUtility.HtmlEncode(s ?? "");
    static IEnumerable<CrashEntry> Recent(StabilitySummary s) =>
        s.Crashes.Where(c => (DateTime.Now - c.Time).TotalDays <= Stability.WindowDays);

    static (string Color, string Text) Level(int score) => Stability.LevelOf(score) switch
    {
        StabilityLevel.Stable => ("#0E8A57", Loc.S("engine.report_builder.stable")),
        StabilityLevel.Fragile => ("#A86400", Loc.S("engine.report_builder.fragile")),
        _ => ("#D33A33", Loc.S("engine.report_builder.unstable")),
    };

    // ===================== HTML =====================
    public static string Html(ScanResult r, StabilitySummary s)
    {
        var hw = r.Hardware;
        var crashes = Recent(s).ToList();
        var (lvColor, lvText) = Level(s.Score);
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang=\"").Append(Loc.Current).Append("\"><head><meta charset=\"utf-8\">")
          .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
          .Append("<title>Dump Finder — ").Append(H(Loc.S("engine.report_builder.diagnostic_report"))).Append(" — ").Append(DateTime.Now.ToString("yyyy-MM-dd")).Append("</title>")
          // Same stylesheet as the Windows report (src/Core/ReportBuilder.cs), with the Mac system fonts.
          .Append("<style>:root{--ink:#14121F;--muted:#6F6B85;--line:#E5E2EF;--soft:#F6F5FB;--brand:#5B47F5;--bad:#D33A33;--warn:#A86400;--ok:#0E8A57}")
          .Append("*{box-sizing:border-box}body{margin:0;background:#fff;color:var(--ink);font:14px/1.55 -apple-system,BlinkMacSystemFont,'Segoe UI',system-ui,sans-serif}")
          .Append("main{max-width:980px;margin:0 auto;padding:40px 24px 64px}")
          .Append("header{display:flex;align-items:center;gap:14px;border-bottom:1px solid var(--line);padding-bottom:18px;margin-bottom:28px}")
          .Append("header h1{font-size:22px;margin:0;font-weight:650;letter-spacing:-.01em}header p{margin:2px 0 0;color:var(--muted)}")
          .Append("h2{font-size:17px;margin:32px 0 12px;font-weight:650}")
          .Append(".hero{display:flex;flex-wrap:wrap;gap:24px;align-items:center;background:var(--soft);border:1px solid var(--line);border-radius:14px;padding:20px 22px}")
          .Append(".score{font-size:44px;font-weight:650;line-height:1}.score small{display:block;font-size:12px;color:var(--muted);font-weight:500;margin-top:4px}")
          .Append(".pill{display:inline-block;font-size:12px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fff;border:1px solid var(--line)}")
          .Append("table{width:100%;border-collapse:collapse;font-size:13px}th{text-align:left;color:var(--muted);font-weight:600;border-bottom:1px solid var(--line);padding:8px 10px}")
          .Append("td{border-bottom:1px solid var(--line);padding:8px 10px;vertical-align:top;overflow-wrap:break-word}td.mono{overflow-wrap:anywhere}code,.mono{font-family:ui-monospace,Menlo,monospace;font-size:12px}")
          .Append(".card{border:1px solid var(--line);border-radius:12px;padding:14px 16px;margin-bottom:12px}.muted{color:var(--muted)}")
          .Append(".bad{color:var(--bad)}.warn{color:var(--warn)}.ok{color:var(--ok)}pre{background:var(--soft);border-radius:8px;padding:10px 12px;overflow:auto;font-size:12px;white-space:pre-wrap}")
          .Append("dl{display:grid;grid-template-columns:max-content minmax(0,1fr);gap:4px 16px;margin:8px 0}dt{color:var(--muted)}dd{margin:0;overflow-wrap:anywhere}")
          .Append("footer{margin-top:40px;color:var(--muted);font-size:12px}@media (max-width:640px){main{padding:24px 16px 48px}table{font-size:12px}th,td{padding:6px}}@media print{main{padding:0}.card,.hero{break-inside:avoid}}")
          .Append("</style></head><body><main>");

        sb.Append("<header><svg width=\"40\" height=\"40\" viewBox=\"0 0 512 512\" aria-hidden=\"true\"><defs>")
          .Append("<linearGradient id=\"b\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#1D1834\"/><stop offset=\"1\" stop-color=\"#0B0A13\"/></linearGradient>")
          .Append("<linearGradient id=\"i\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#A495FF\"/><stop offset=\"1\" stop-color=\"#6A55F7\"/></linearGradient>")
          .Append("<linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#FF5E62\"/><stop offset=\"1\" stop-color=\"#FFA24A\"/></linearGradient></defs>")
          .Append("<rect x=\"16\" y=\"16\" width=\"480\" height=\"480\" rx=\"124\" fill=\"url(#b)\"/><rect x=\"112\" y=\"132\" width=\"288\" height=\"64\" rx=\"22\" fill=\"url(#i)\"/>")
          .Append("<path d=\"M134 224H262L250 240L266 256L252 272L262 288H134a22 22 0 0 1-22-22V246a22 22 0 0 1 22-22Z\" fill=\"url(#i)\"/>")
          .Append("<path d=\"M292 214H414a22 22 0 0 1 22 22v20a22 22 0 0 1-22 22H292L282 262L296 246L280 230Z\" fill=\"url(#f)\"/>")
          .Append("<rect x=\"112\" y=\"316\" width=\"288\" height=\"64\" rx=\"22\" fill=\"url(#i)\" opacity=\".85\"/></svg>")
          .Append("<div><h1>").Append(H(Loc.S("engine.report_builder.diagnostic_report"))).Append("</h1><p>Dump Finder for Mac ").Append(Version).Append(" · ")
          .Append(H(DateTime.Now.ToString("f", Loc.Culture))).Append("</p></div></header>");

        // Summary
        sb.Append("<section class=\"hero\"><div class=\"score\" style=\"color:").Append(lvColor).Append("\">").Append(s.Score)
          .Append("<small>/ 100 ").Append(H(Loc.S("engine.report_builder.stability"))).Append("</small></div><div>")
          .Append("<span class=\"pill\" style=\"color:").Append(lvColor).Append("\">").Append(H(lvText)).Append("</span>")
          .Append("<h2 style=\"margin:10px 0 4px\">").Append(H(s.VerdictTitle)).Append("</h2><div class=\"muted\">").Append(H(s.VerdictBody)).Append("</div>")
          .Append("<div class=\"muted\" style=\"margin-top:8px\">").Append(H(Loc.F("mac.report.crash_counts", crashes.Count, crashes.Count(c => Stability.IsSevere(c.Category))))).Append("</div></div></section>");

        // System
        sb.Append("<h2>").Append(H(Loc.S("engine.report_builder.system"))).Append("</h2><dl>");
        Dl(sb, "Mac", $"{hw.MachineName} ({hw.ModelId})");
        Dl(sb, Loc.S("mac.report.chip"), $"{hw.Chip} · {hw.Memory}");
        Dl(sb, "macOS", hw.OsVersion);
        if (r.Boot.BootTime is { } bt) Dl(sb, Loc.S("engine.report_builder.last_boot"), bt.LocalDateTime.ToString("g", Loc.Culture));
        if (r.Boot.ShutdownReason.Length > 0) Dl(sb, Loc.S("mac.report.last_shutdown"), r.Boot.ShutdownReason);
        foreach (var d in hw.Drives) Dl(sb, d.Name, "SMART " + d.SmartStatus);
        if (hw.BootVolumeFree is long free && hw.BootVolumeSize is long size && size > 0)
            Dl(sb, Loc.S("mac.report.startup_disk"), Loc.F("mac.report.free_of", free / 1e9, size / 1e9, 100.0 * free / size));
        if (hw.HasBattery) Dl(sb, Loc.S("mac.report.battery"), Loc.F("mac.report.battery_value", hw.BatteryMaxCapacityPct ?? 0, hw.BatteryCycles ?? 0, hw.BatteryCondition));
        sb.Append("</dl>");

        // Crashes in the last 30 days
        sb.Append("<h2>").Append(H(Loc.S("engine.report_builder.crashes_in_the_last_30_days"))).Append("</h2>");
        if (crashes.Count == 0) sb.Append("<p class=\"ok\">").Append(H(Loc.S("engine.report_builder.no_crash_recorded"))).Append("</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(H(Loc.S("engine.report_builder.date"))).Append("</th><th>").Append(H(Loc.S("engine.report_builder.type")))
              .Append("</th><th>").Append(H(Loc.S("engine.report_builder.description"))).Append("</th><th>").Append(H(Loc.S("mac.report.details"))).Append("</th></tr>");
            foreach (var c in crashes.Take(80))
            {
                string cls = Stability.IsSevere(c.Category) ? "bad" : c.Category == CrashCategory.AppCrash ? "muted" : "warn";
                sb.Append("<tr><td class=\"mono\">").Append(H(c.Time.ToString("yyyy-MM-dd HH:mm"))).Append("</td><td class=\"").Append(cls).Append("\">")
                  .Append(H(CrashTimeline.CategoryName(c.Category))).Append("</td><td>").Append(H(c.Title)).Append("</td><td class=\"mono\">").Append(H(c.TechLine)).Append("</td></tr>");
            }
            sb.Append("</table>");
        }

        // Kernel panics, analysed
        var panics = s.Crashes.Where(c => c.Panic is not null).Take(10).ToList();
        if (panics.Count > 0)
        {
            sb.Append("<h2>").Append(H(Loc.S("mac.report.panics"))).Append("</h2>");
            foreach (var c in panics)
            {
                sb.Append("<div class=\"card\"><strong class=\"bad\">").Append(H(c.Title)).Append("</strong> <span class=\"muted\">· ")
                  .Append(H(c.Time.ToString("yyyy-MM-dd HH:mm"))).Append("</span><p>").Append(H(CrashDetails.Plain(c, DetectionData.Embedded))).Append("</p><dl>");
                foreach (var kv in CrashDetails.Analysis(c)) Dl(sb, kv.Key, kv.Value);
                sb.Append("</dl></div>");
            }
        }

        // Checks
        sb.Append("<h2>").Append(H(Loc.S("mac.home.checks"))).Append("</h2>");
        foreach (var f in r.Findings)
        {
            string cls = f.Severity switch { Severity.Critical => "bad", Severity.Warning => "warn", Severity.Ok => "ok", _ => "muted" };
            sb.Append("<div class=\"card\"><span class=\"pill ").Append(cls).Append("\">").Append(H(LevelName(f.Severity))).Append("</span> <strong>")
              .Append(H(f.Title)).Append("</strong><p style=\"white-space:pre-line\">").Append(H(f.Detail)).Append("</p>");
            if (f.Advice.Length > 0) sb.Append("<p class=\"muted\">→ ").Append(H(f.Advice)).Append("</p>");
            sb.Append("</div>");
        }

        // Third-party extensions
        sb.Append("<h2>").Append(H(Loc.S("mac.hardware.extensions"))).Append("</h2>");
        if (r.KernelExtensions.Count == 0 && r.SystemExtensions.Count == 0)
            sb.Append("<p class=\"ok\">").Append(H(Loc.S("mac.hardware.no_extension"))).Append("</p>");
        else
        {
            sb.Append("<table><tr><th>").Append(H(Loc.S("mac.report.extension"))).Append("</th><th>").Append(H(Loc.S("engine.report_builder.type"))).Append("</th><th>")
              .Append(H(Loc.S("mac.report.state"))).Append("</th></tr>");
            foreach (var k in r.KernelExtensions)
                sb.Append("<tr><td class=\"mono\">").Append(H(k.BundleId + " " + k.Version)).Append("</td><td>kext</td><td>")
                  .Append(H(k.Loaded ? Loc.S("mac.report.loaded") : Loc.S("mac.report.not_loaded"))).Append("</td></tr>");
            foreach (var x in r.SystemExtensions)
                sb.Append("<tr><td class=\"mono\">").Append(H(x.BundleId + " " + x.Version)).Append("</td><td>system extension</td><td>").Append(H(x.State)).Append("</td></tr>");
            sb.Append("</table>");
        }

        sb.Append("<footer>").Append(H(Loc.S("mac.report.footer"))).Append("</footer></main></body></html>");
        return Privacy.Redact(sb.ToString());
    }

    static void Dl(StringBuilder sb, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append("<dt>").Append(H(key)).Append("</dt><dd>").Append(H(value)).Append("</dd>");
    }

    public static string LevelName(Severity s) => Loc.S(s switch
    {
        Severity.Critical => "mac.level.critical",
        Severity.Warning => "mac.level.warning",
        Severity.Ok => "mac.level.ok",
        _ => "mac.level.info",
    });

    // ===================== JSON =====================
    public static string Json(ScanResult r, StabilitySummary s)
    {
        var hw = r.Hardware;
        var root = new JsonObject
        {
            ["app"] = "Dump Finder for Mac",
            ["version"] = Version,
            ["generated"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            ["score"] = s.Score,
            ["level"] = Stability.LevelOf(s.Score).ToString(),
            ["diagnosis"] = new JsonObject { ["title"] = s.VerdictTitle, ["body"] = s.VerdictBody },
            ["system"] = new JsonObject
            {
                ["model"] = hw.ModelId, ["name"] = hw.MachineName, ["chip"] = hw.Chip, ["memory"] = hw.Memory, ["macos"] = hw.OsVersion,
                ["boot_time"] = r.Boot.BootTime?.ToString("o", CultureInfo.InvariantCulture), ["boot_reason"] = r.Boot.BootReason,
                ["shutdown_reason"] = r.Boot.ShutdownReason, ["sip"] = hw.SipEnabled, ["gatekeeper"] = hw.GatekeeperEnabled,
                ["startup_disk_free_bytes"] = hw.BootVolumeFree, ["startup_disk_size_bytes"] = hw.BootVolumeSize,
                ["battery"] = hw.HasBattery ? new JsonObject { ["max_capacity_pct"] = hw.BatteryMaxCapacityPct, ["cycles"] = hw.BatteryCycles, ["condition"] = hw.BatteryCondition } : null,
                ["drives"] = new JsonArray(hw.Drives.Select(d => (JsonNode)new JsonObject { ["name"] = d.Name, ["internal"] = d.Internal, ["smart"] = d.SmartStatus }).ToArray()),
            },
            ["crashes"] = new JsonArray(s.Crashes.Take(300).Select(c => (JsonNode)new JsonObject
            {
                ["time"] = c.Time.ToString("o", CultureInfo.InvariantCulture), ["category"] = c.Category.ToString(), ["title"] = c.Title, ["details"] = c.TechLine,
                ["panic_message"] = c.Panic?.Message, ["panic_extensions"] = c.Panic is { } p ? new JsonArray(p.BacktraceExtensions.Select(x => (JsonNode)x!).ToArray()) : null,
                ["exception"] = c.Report?.ExceptionType, ["signal"] = c.Report?.Signal,
            }).ToArray()),
            ["checks"] = new JsonArray(r.Findings.Select(f => (JsonNode)new JsonObject
            {
                ["id"] = f.Id, ["severity"] = f.Severity.ToString(), ["category"] = f.Category, ["title"] = f.Title, ["detail"] = f.Detail, ["advice"] = f.Advice,
            }).ToArray()),
            ["kernel_extensions"] = new JsonArray(r.KernelExtensions.Select(k => (JsonNode)new JsonObject { ["bundle_id"] = k.BundleId, ["version"] = k.Version, ["loaded"] = k.Loaded }).ToArray()),
            ["system_extensions"] = new JsonArray(r.SystemExtensions.Select(x => (JsonNode)new JsonObject { ["bundle_id"] = x.BundleId, ["version"] = x.Version, ["state"] = x.State }).ToArray()),
        };
        return Privacy.Redact(root.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    }

    // ===================== Markdown (forum) =====================
    public static string Markdown(ScanResult r, StabilitySummary s)
    {
        var hw = r.Hardware;
        var crashes = Recent(s).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("## Dump Finder for Mac — " + Loc.S("engine.report_builder.diagnostic_report"));
        sb.AppendLine();
        sb.AppendLine($"**{s.Score} / 100** · {Level(s.Score).Text} · {s.VerdictTitle}");
        sb.AppendLine();
        sb.AppendLine(s.VerdictBody);
        sb.AppendLine();
        sb.AppendLine("**" + Loc.S("engine.report_builder.system") + "**");
        sb.AppendLine($"- {hw.MachineName} ({hw.ModelId}), {hw.Chip}, {hw.Memory}");
        sb.AppendLine($"- {hw.OsVersion}");
        if (r.Boot.ShutdownReason.Length > 0) sb.AppendLine($"- {Loc.S("mac.report.last_shutdown")}: `{r.Boot.ShutdownReason}`");
        sb.AppendLine();
        sb.AppendLine("**" + Loc.S("engine.report_builder.crashes_in_the_last_30_days") + "**");
        if (crashes.Count == 0) sb.AppendLine("- " + Loc.S("engine.report_builder.no_crash_recorded"));
        foreach (var c in crashes.Take(25))
            sb.AppendLine($"- {c.Time:yyyy-MM-dd HH:mm} · {CrashTimeline.CategoryName(c.Category)} · {c.Title}" + (c.TechLine.Length > 0 ? $" · `{c.TechLine}`" : ""));
        sb.AppendLine();
        sb.AppendLine("**" + Loc.S("mac.home.checks") + "**");
        foreach (var f in r.Findings.Where(f => f.Severity >= Severity.Warning).DefaultIfEmpty(r.Findings.FirstOrDefault()))
            if (f is not null) sb.AppendLine($"- [{LevelName(f.Severity)}] {f.Title}");
        sb.AppendLine();
        sb.AppendLine("_" + Loc.S("mac.report.footer") + "_");
        return Privacy.Redact(sb.ToString());
    }

    // ===================== Plain text =====================
    public static string Text(ScanResult r, StabilitySummary s)
    {
        var hw = r.Hardware;
        var sb = new StringBuilder();
        sb.AppendLine($"Dump Finder for Mac {Version} — {Loc.S("engine.report_builder.diagnostic_report")} — {r.ScannedAt.LocalDateTime.ToString("g", Loc.Culture)}");
        sb.AppendLine($"{hw.MachineName} ({hw.ModelId}) · {hw.Chip} · {hw.Memory} · {hw.OsVersion}");
        if (r.Boot.BootTime is { } bt) sb.AppendLine($"{Loc.S("engine.report_builder.last_boot")}: {bt.LocalDateTime.ToString("g", Loc.Culture)} · {r.Boot.BootReason} · {r.Boot.ShutdownReason}");
        sb.AppendLine();
        sb.AppendLine($"{s.Score} / 100 · {Level(s.Score).Text} · {s.VerdictTitle}");
        sb.AppendLine(s.VerdictBody);
        sb.AppendLine();
        sb.AppendLine("== " + Loc.S("mac.home.checks"));
        foreach (var f in r.Findings)
        {
            sb.AppendLine($"[{LevelName(f.Severity)}] {f.Title}");
            foreach (var l in f.Detail.Split('\n')) sb.AppendLine("    " + l);
            if (f.Advice.Length > 0) sb.AppendLine("    → " + f.Advice);
        }
        sb.AppendLine();
        sb.AppendLine("== " + Loc.S("engine.report_builder.crashes_in_the_last_30_days"));
        foreach (var c in Recent(s).Take(60))
            sb.AppendLine($"{c.Time:yyyy-MM-dd HH:mm}  {CrashTimeline.CategoryName(c.Category),-22} {c.Title}" + (c.TechLine.Length > 0 ? "  [" + c.TechLine + "]" : ""));
        sb.AppendLine();
        sb.AppendLine("== " + Loc.S("mac.hardware.extensions"));
        foreach (var k in r.KernelExtensions) sb.AppendLine($"kext {k.BundleId} {k.Version} ({(k.Loaded ? Loc.S("mac.report.loaded") : Loc.S("mac.report.not_loaded"))})");
        foreach (var x in r.SystemExtensions) sb.AppendLine($"system extension {x.BundleId} {x.Version} [{x.State}]");
        if (r.KernelExtensions.Count == 0 && r.SystemExtensions.Count == 0) sb.AppendLine(Loc.S("mac.hardware.no_extension"));
        if (r.Errors.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("== " + Loc.S("mac.hardware.not_read"));
            foreach (var e in r.Errors) sb.AppendLine(e);
        }
        return Privacy.Redact(sb.ToString());
    }
}
