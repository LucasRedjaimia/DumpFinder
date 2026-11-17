namespace DumpFinder.Mac.Core;

/// <summary>Runs every probe (in parallel) and turns the readings into findings.</summary>
public static class DiagnosticsEngine
{
    public static async Task<ScanResult> ScanAsync(DetectionData? data = null)
    {
        data ??= DetectionData.Embedded;
        var errors = new List<string>();
        // Each probe gets its own error list (they run concurrently), merged at the end.
        var eReports = new List<string>(); var eKext = new List<string>(); var eSys = new List<string>(); var eHw = new List<string>();

        var reportsT = Task.Run(() => DiagnosticReports.ReadAll(data, TimeSpan.FromDays(Math.Max(90, data.Thresholds.RecentDays)), eReports));
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
            var thirdParty = p.BacktraceExtensions.Where(id => !id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase)).ToList();
            var pattern = data.PanicPatterns.FirstOrDefault(x => x.Id == p.PatternId);
            string title = pattern?.Title ?? "Kernel panic";
            string count = group.Count() == 1 ? "once" : $"{group.Count()} times";
            string detail = $"The Mac restarted after a kernel panic {count} in the last {th.RecentDays} days (last: {p.Time:yyyy-MM-dd HH:mm}).\n{p.Message}";
            if (p.PanickedProcess.Length > 0) detail += $"\nProcess: {p.PanickedProcess}";
            if (p.Explanation.Length > 0) detail += "\n" + p.Explanation;
            string advice = p.Advice.Length > 0 ? p.Advice : "Update macOS and your apps. If it repeats, note what you were doing and contact Apple support.";
            if (thirdParty.Count > 0)
            {
                title += $" — involves {string.Join(", ", thirdParty)}";
                detail += $"\nNon-Apple extension(s) in the backtrace: {string.Join(", ", thirdParty)}.";
                advice = $"Update or uninstall the software that installed {string.Join(", ", thirdParty)}; it is the most likely cause. " + advice;
            }
            f.Add(new Finding(Severity.Critical, "Crashes", title, detail, advice));
        }

        // --- Previous shutdown (Apple Silicon) ---
        if (r.Boot.Abnormal && recentPanics.Count == 0)
            f.Add(new Finding(Severity.Warning, "Crashes", "The last restart was not a normal shutdown",
                $"Started {r.Boot.BootTime:yyyy-MM-dd HH:mm}. Recorded cause: {string.Join("; ", r.Boot.Interpretation)}.\nRaw value: {r.Boot.ShutdownReason}",
                "If the Mac froze and you held the power button, this is expected. If it restarted by itself, watch for repeats and check the accessories connected at the time. (These codes are not documented by Apple: the reading is approximate.)"));

        // --- App crashes and memory pressure ---
        foreach (var g in r.Reports.Where(x => x.Kind == ReportKind.Crash && x.Time >= recent)
                                   .GroupBy(x => x.Process).Where(g => g.Count() >= th.RepeatedCrashes)
                                   .OrderByDescending(g => g.Count()))
        {
            var last = g.First();
            var why = string.Join(" / ", new[] { last.ExceptionType, last.Signal, last.Termination }.Where(s => s.Length > 0));
            bool apple = g.All(x => x.FirstParty);
            f.Add(new Finding(apple ? Severity.Info : Severity.Warning, "Crashes",
                $"{g.Key} crashed {g.Count()} times",
                $"Last crash {last.Time:yyyy-MM-dd HH:mm}" + (why.Length > 0 ? $" ({why})." : "."),
                apple ? "This is an Apple background process: it is usually harmless unless you notice a problem. Updating macOS often fixes it."
                      : "Update the app. If it keeps crashing, reset its preferences or reinstall it."));
        }
        var jetsam = r.Reports.Count(x => x.Kind == ReportKind.MemoryPressure && x.Time >= recent);
        if (jetsam > 0)
            f.Add(new Finding(jetsam >= th.RepeatedCrashes ? Severity.Warning : Severity.Info, "Memory",
                $"macOS ran low on memory {jetsam} time(s)",
                $"macOS closed processes to free memory ({jetsam} \"Jetsam\" report(s) in {th.RecentDays} days).",
                "Look at Memory in Activity Monitor around those times; browsers with many tabs, VMs and creative apps use the most. Low free disk space makes it worse (less room for swap)."));

        // --- Kernel extensions ---
        foreach (var k in r.KernelExtensions.Where(k => k.Loaded))
        {
            var cat = data.VendorCategory(k.BundleId);
            bool inPanic = recentPanics.Any(p => p.BacktraceExtensions.Contains(k.BundleId));
            f.Add(new Finding(inPanic ? Severity.Critical : Severity.Warning, "Drivers",
                $"Third-party kernel extension loaded: {k.BundleId}",
                $"Version {k.Version}{(cat is null ? "" : $" · {cat}")}. Kernel extensions run inside the macOS kernel; on Apple Silicon they need reduced security in Startup Security Utility.",
                "Check that it is still needed and up to date. Most vendors now provide a system extension or a DriverKit driver instead."));
        }
        foreach (var k in r.KernelExtensions.Where(k => !k.Loaded))
        {
            var cat = data.VendorCategory(k.BundleId);
            f.Add(new Finding(Severity.Info, "Drivers", $"Kernel extension installed but not loaded: {k.BundleId}",
                $"{k.Path}{(cat is null ? "" : $" · {cat}")}. Often left behind by uninstalled software.",
                "If you no longer use this software, uninstall it with its vendor's uninstaller."));
        }
        if (r.SystemExtensions.Count > 0)
            f.Add(new Finding(Severity.Info, "Drivers", $"{r.SystemExtensions.Count} system extension(s) installed",
                string.Join("\n", r.SystemExtensions.Select(s => $"{s.BundleId} ({s.Version}) — {s.State}")),
                "Security, VPN and network-filter extensions can slow down or block the network. Disable the ones you do not use in System Settings › General › Login Items & Extensions."));

        // --- Storage ---
        var hw = r.Hardware;
        foreach (var d in hw.Drives.Where(d => d.SmartStatus.Length > 0 && !d.SmartStatus.Equals("Verified", StringComparison.OrdinalIgnoreCase)))
            f.Add(new Finding(Severity.Critical, "Hardware", $"Drive reports a problem: {d.Name}",
                $"SMART status: {d.SmartStatus}.", "Back up now. An internal drive in this state needs Apple support."));
        if (hw.BootVolumeFree is long free && hw.BootVolumeSize is long size && size > 0)
        {
            double pct = 100.0 * free / size;
            var sev = pct < th.DiskFreeCriticalPct ? Severity.Critical : pct < th.DiskFreeWarnPct ? Severity.Warning : Severity.Ok;
            if (sev != Severity.Ok)
                f.Add(new Finding(sev, "Hardware", $"Low free space on the startup disk ({pct:0.#} %)",
                    $"{Gb(free)} free out of {Gb(size)}. macOS needs room for swap, updates and caches.",
                    "Free up space: System Settings › General › Storage. Keep at least 10 % free."));
        }

        // --- Battery ---
        if (hw.HasBattery)
        {
            bool weak = hw.BatteryMaxCapacityPct < th.BatteryMaxCapacityPct
                        || (hw.BatteryCondition.Length > 0 && !hw.BatteryCondition.Equals("Good", StringComparison.OrdinalIgnoreCase)
                            && !hw.BatteryCondition.Equals("Normal", StringComparison.OrdinalIgnoreCase));
            if (weak)
                f.Add(new Finding(Severity.Warning, "Hardware", "Battery worn",
                    $"Maximum capacity {hw.BatteryMaxCapacityPct} %, condition \"{hw.BatteryCondition}\", {hw.BatteryCycles} cycles.",
                    "A worn battery can cause sudden shutdowns under load. Apple recommends service below 80 %."));
            else if (hw.BatteryCycles >= th.BatteryCycles)
                f.Add(new Finding(Severity.Info, "Hardware", "Battery past its rated cycle count",
                    $"{hw.BatteryCycles} cycles (rated {th.BatteryCycles}).", "Plan a battery service if battery life drops."));
        }

        // --- macOS and security ---
        if (hw.OsMajor > 0 && hw.OsMajor < data.SupportedMacOSMajors.Min())
            f.Add(new Finding(Severity.Warning, "System", $"{hw.OsVersion} no longer gets security updates",
                "Apple fixes security issues in the current macOS and the two previous ones.", "Update macOS if your Mac supports a newer version."));
        if (hw.SipEnabled == false)
            f.Add(new Finding(Severity.Warning, "System", "System Integrity Protection is off",
                "SIP protects system files from changes, including by kernel extensions.", "Turn it back on from recoveryOS (csrutil enable) unless you need it off."));
        if (hw.GatekeeperEnabled == false)
            f.Add(new Finding(Severity.Warning, "System", "Gatekeeper is off",
                "Apps are no longer checked for a valid signature at launch.", "Turn it back on: sudo spctl --master-enable."));
        if (r.Boot.BootTime is DateTimeOffset bt && (now - bt).TotalDays >= th.LongUptimeDays)
            f.Add(new Finding(Severity.Info, "System", $"No restart for {(int)(now - bt).TotalDays} days",
                "Some updates and memory fixes apply only after a restart.", "Restart the Mac when convenient."));

        if (!f.Any(x => x.Severity >= Severity.Warning))
            f.Insert(0, new Finding(Severity.Ok, "Summary", "No problem found",
                $"No kernel panic, repeated crash or hardware warning in the last {th.RecentDays} days.", ""));

        return f.OrderByDescending(x => x.Severity).ToList();
    }

    static string PanicKey(PanicReport p) =>
        p.PatternId + "|" + string.Join(",", p.BacktraceExtensions.Where(id => !id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase)))
        + (p.PatternId.Length == 0 ? "|" + p.Message : "");

    static string Gb(long bytes) => $"{bytes / 1e9:0.#} GB";
}
