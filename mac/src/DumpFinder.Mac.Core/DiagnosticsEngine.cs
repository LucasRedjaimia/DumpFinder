namespace DumpFinder.Mac.Core;

/// <summary>Runs every probe (in parallel) and turns the readings into findings, in the app language (Loc).</summary>
public static class DiagnosticsEngine
{
    /// <param name="maxAgeDays">How far back reports are read: 90 days by default, more for "All history".</param>
    public static async Task<ScanResult> ScanAsync(DetectionData? data = null, int maxAgeDays = 90)
    {
        data ??= DetectionData.Embedded;
        var errors = new List<string>();
        // Each probe gets its own error list (they run concurrently), merged at the end.
        var eReports = new List<string>(); var eKext = new List<string>(); var eSys = new List<string>(); var eHw = new List<string>();

        var reportsT = Task.Run(() => DiagnosticReports.ReadAll(data, TimeSpan.FromDays(Math.Max(maxAgeDays, data.Thresholds.RecentDays)), eReports));
        var kextT = Task.Run(() => SystemProbe.ReadKernelExtensions(eKext));
        var sysExtT = Task.Run(() => SystemProbe.ReadSystemExtensions(eSys));
        var hwT = Task.Run(() => SystemProbe.ReadHardware(eHw));
        var bootT = Task.Run(() => SystemProbe.ReadBoot(data));
        await Task.WhenAll(reportsT, kextT, sysExtT, hwT, bootT);

        errors.AddRange(eReports.Concat(eKext).Concat(eSys).Concat(eHw));
        var result = new ScanResult
        {
            Hardware = hwT.Result,
            Boot = bootT.Result,
            Panics = reportsT.Result.Panics,
            Reports = reportsT.Result.Reports,
            KernelExtensions = kextT.Result,
            SystemExtensions = sysExtT.Result,
            Errors = errors,
        };
        result.Findings.AddRange(Analyze(result, data, DateTimeOffset.Now));
        return result;
    }

    /// <summary>Pure function of the readings: the self-test feeds it synthetic scans.</summary>
    public static List<Finding> Analyze(ScanResult r, DetectionData data, DateTimeOffset now)
    {
        var f = new List<Finding>();
        var th = data.Thresholds;
        var recent = now.AddDays(-th.RecentDays);

        // --- Kernel panics ---
        var recentPanics = r.Panics.Where(p => p.Time >= recent).ToList();
        foreach (var group in recentPanics.GroupBy(PanicKey))
        {
            var p = group.First();
            var thirdParty = p.BacktraceExtensions.Where(id => !IsApple(id)).ToList();
            string title = MacText.PanicTitle(p.PatternId, data);
            var detail = new List<string> { Loc.P("mac.check.panic_detail", group.Count(), p.Time?.LocalDateTime ?? now.LocalDateTime, th.RecentDays), p.Message };
            if (p.PanickedProcess.Length > 0) detail.Add(Loc.F("mac.check.panic_process", p.PanickedProcess));
            if (MacText.PanicExplanation(p.PatternId, data) is { Length: > 0 } ex) detail.Add(ex);
            string advice = MacText.PanicAdvice(p.PatternId, data) is { Length: > 0 } a ? a : Loc.S("mac.check.panic_advice_generic");
            if (thirdParty.Count > 0)
            {
                var list = string.Join(", ", thirdParty);
                title = Loc.F("mac.check.panic_with_kext", title, list);
                detail.Add(Loc.F("mac.check.panic_kexts", list));
                advice = Loc.F("mac.check.panic_advice_kext", list) + " " + advice;
            }
            f.Add(new Finding("panic", Severity.Critical, "crashes", title, string.Join("\n", detail), advice));
        }

        // --- Previous shutdown (Apple Silicon) ---
        if (r.Boot.Abnormal && recentPanics.Count == 0)
            f.Add(new Finding("restart", Severity.Warning, "crashes", Loc.S("mac.check.restart_title"),
                Loc.F("mac.check.restart_detail", r.Boot.BootTime?.LocalDateTime ?? now.LocalDateTime,
                      string.Join("; ", MacText.ShutdownCauses(r.Boot.ShutdownReason, data)), r.Boot.ShutdownReason),
                Loc.S("mac.check.restart_advice")));

        // --- App crashes and memory pressure ---
        foreach (var g in r.Reports.Where(x => x.Kind == ReportKind.Crash && x.Time >= recent)
                                   .GroupBy(x => x.Process).Where(g => g.Count() >= th.RepeatedCrashes)
                                   .OrderByDescending(g => g.Count()))
        {
            var last = g.First();
            var why = string.Join(" / ", new[] { last.ExceptionType, last.Signal, last.Termination }.Where(s => s.Length > 0));
            bool apple = g.All(x => x.FirstParty);
            f.Add(new Finding("app_crashes", apple ? Severity.Info : Severity.Warning, "crashes",
                Loc.P("mac.check.app_crashed", g.Count(), g.Key),
                Loc.F("mac.check.app_last", last.Time?.LocalDateTime ?? now.LocalDateTime, why.Length > 0 ? $" ({why})" : ""),
                Loc.S(apple ? "mac.check.app_apple_advice" : "mac.check.app_advice")));
        }
        var jetsam = r.Reports.Count(x => x.Kind == ReportKind.MemoryPressure && x.Time >= recent);
        if (jetsam > 0)
            f.Add(new Finding("jetsam", jetsam >= th.RepeatedCrashes ? Severity.Warning : Severity.Info, "memory",
                Loc.P("mac.check.jetsam_title", jetsam), Loc.P("mac.check.jetsam_detail", jetsam, th.RecentDays), Loc.S("mac.check.jetsam_advice")));

        // --- Kernel extensions ---
        foreach (var k in r.KernelExtensions.Where(k => k.Loaded))
        {
            bool inPanic = recentPanics.Any(p => p.BacktraceExtensions.Contains(k.BundleId));
            f.Add(new Finding("kext_loaded", inPanic ? Severity.Critical : Severity.Warning, "drivers",
                Loc.F("mac.check.kext_loaded_title", k.BundleId),
                Loc.F("mac.check.kext_loaded_detail", k.Version, VendorSuffix(k.BundleId, data)),
                Loc.S("mac.check.kext_loaded_advice")));
        }
        foreach (var k in r.KernelExtensions.Where(k => !k.Loaded))
            f.Add(new Finding("kext_leftover", Severity.Info, "drivers", Loc.F("mac.check.kext_leftover_title", k.BundleId),
                Loc.F("mac.check.kext_leftover_detail", k.Path, VendorSuffix(k.BundleId, data)), Loc.S("mac.check.kext_leftover_advice")));
        if (r.SystemExtensions.Count > 0)
            f.Add(new Finding("sysext", Severity.Info, "drivers", Loc.P("mac.check.sysext_title", r.SystemExtensions.Count),
                string.Join("\n", r.SystemExtensions.Select(s => $"{s.BundleId} ({s.Version}) — {s.State}")),
                Loc.S("mac.check.sysext_advice")));

        // --- Storage ---
        var hw = r.Hardware;
        foreach (var d in hw.Drives.Where(d => d.SmartStatus.Length > 0 && !d.SmartStatus.Equals("Verified", StringComparison.OrdinalIgnoreCase)))
            f.Add(new Finding("smart", Severity.Critical, "hardware", Loc.F("mac.check.smart_title", d.Name),
                Loc.F("mac.check.smart_detail", d.SmartStatus), Loc.S("mac.check.smart_advice")));
        if (hw.BootVolumeFree is long free && hw.BootVolumeSize is long size && size > 0)
        {
            double pct = 100.0 * free / size;
            var sev = pct < th.DiskFreeCriticalPct ? Severity.Critical : pct < th.DiskFreeWarnPct ? Severity.Warning : Severity.Ok;
            if (sev != Severity.Ok)
                f.Add(new Finding("low_disk", sev, "hardware", Loc.F("mac.check.disk_title", pct),
                    Loc.F("mac.check.disk_detail", free / 1e9, size / 1e9), Loc.S("mac.check.disk_advice")));
        }

        // --- Battery ---
        if (hw.HasBattery)
        {
            bool weak = hw.BatteryMaxCapacityPct < th.BatteryMaxCapacityPct
                        || (hw.BatteryCondition.Length > 0 && !hw.BatteryCondition.Equals("Good", StringComparison.OrdinalIgnoreCase)
                            && !hw.BatteryCondition.Equals("Normal", StringComparison.OrdinalIgnoreCase));
            if (weak)
                f.Add(new Finding("battery_worn", Severity.Warning, "hardware", Loc.S("mac.check.battery_title"),
                    Loc.F("mac.check.battery_detail", hw.BatteryMaxCapacityPct ?? 0, hw.BatteryCondition, hw.BatteryCycles ?? 0),
                    Loc.S("mac.check.battery_advice")));
            else if (hw.BatteryCycles >= th.BatteryCycles)
                f.Add(new Finding("battery_cycles", Severity.Info, "hardware", Loc.S("mac.check.cycles_title"),
                    Loc.F("mac.check.cycles_detail", hw.BatteryCycles ?? 0, th.BatteryCycles), Loc.S("mac.check.cycles_advice")));
        }

        // --- macOS and security ---
        if (hw.OsMajor > 0 && hw.OsMajor < data.SupportedMacOSMajors.Min())
            f.Add(new Finding("macos_unsupported", Severity.Warning, "system", Loc.F("mac.check.os_title", hw.OsVersion),
                Loc.S("mac.check.os_detail"), Loc.S("mac.check.os_advice")));
        if (hw.SipEnabled == false)
            f.Add(new Finding("sip_off", Severity.Warning, "system", Loc.S("mac.check.sip_title"), Loc.S("mac.check.sip_detail"), Loc.S("mac.check.sip_advice")));
        if (hw.GatekeeperEnabled == false)
            f.Add(new Finding("gatekeeper_off", Severity.Warning, "system", Loc.S("mac.check.gk_title"), Loc.S("mac.check.gk_detail"), Loc.S("mac.check.gk_advice")));
        if (r.Boot.BootTime is DateTimeOffset bt && (now - bt).TotalDays >= th.LongUptimeDays)
            f.Add(new Finding("uptime", Severity.Info, "system", Loc.P("mac.check.uptime_title", (int)(now - bt).TotalDays),
                Loc.S("mac.check.uptime_detail"), Loc.S("mac.check.uptime_advice")));

        if (!f.Any(x => x.Severity >= Severity.Warning))
            f.Insert(0, new Finding("ok", Severity.Ok, "summary", Loc.S("mac.check.ok_title"), Loc.F("mac.check.ok_detail", th.RecentDays), ""));

        return f.OrderByDescending(x => x.Severity).ToList();
    }

    static bool IsApple(string id) => id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase);

    static string VendorSuffix(string bundleId, DetectionData data) =>
        data.VendorCategory(bundleId) is { } cat ? " · " + MacText.VendorCategory(cat) : "";

    static string PanicKey(PanicReport p) =>
        p.PatternId + "|" + string.Join(",", p.BacktraceExtensions.Where(id => !IsApple(id)))
        + (p.PatternId.Length == 0 ? "|" + p.Message : "");
}

/// <summary>
/// Texts of the detection data in the app language: Locales keys first (mac.panic.*, mac.panic_explain.*,
/// mac.panic_advice.*, mac.shutdown.*, mac.vendor.*), the English text of mac-detection.json as the fallback.
/// </summary>
public static class MacText
{
    static string Or(string key, string? fallback)
    {
        var t = Loc.S(key);
        return t != key ? t : fallback ?? "";
    }

    public static string PanicTitle(string patternId, DetectionData data) =>
        patternId.Length == 0 ? Loc.S("mac.category.panic")
            : Or("mac.panic." + patternId, data.PanicPatterns.FirstOrDefault(p => p.Id == patternId)?.Title ?? Loc.S("mac.category.panic"));

    public static string PanicExplanation(string patternId, DetectionData data) =>
        patternId.Length == 0 ? "" : Or("mac.panic_explain." + patternId, data.PanicPatterns.FirstOrDefault(p => p.Id == patternId)?.Explanation);

    public static string PanicAdvice(string patternId, DetectionData data) =>
        patternId.Length == 0 ? "" : Or("mac.panic_advice." + patternId, data.PanicPatterns.FirstOrDefault(p => p.Id == patternId)?.Advice);

    public static IEnumerable<string> ShutdownCauses(string shutdownReason, DetectionData data)
    {
        var interpreted = SystemProbe.InterpretBoot("", shutdownReason, null, data);
        foreach (var t in data.ShutdownTokens)
            if (interpreted.Interpretation.Contains(t.Meaning))
                yield return Or("mac.shutdown." + t.Token, t.Meaning);
    }

    public static string VendorCategory(string id) => Or("mac.vendor." + id, id);
}
