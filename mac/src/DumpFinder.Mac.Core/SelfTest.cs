using System.Text;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Offline checks of the parsers and rules on synthetic inputs (no access to the machine needed).
/// Returns the number of failed checks, like the Windows --*-selftest commands.
/// </summary>
public static class SelfTest
{
    // Shape of a modern .panic file: one-line JSON header, then a JSON body holding the panic string.
    const string PanicFile =
        "{\"bug_type\":\"210\",\"timestamp\":\"2026-11-15 22:04:11.00 +0100\",\"os_version\":\"macOS 26.6.2 (25G83)\",\"incident_id\":\"X\"}\n" +
        "{\"build\":\"macOS 26.6.2 (25G83)\",\"panicString\":\"panic(cpu 3 caller 0xfffffe0012345678): Kernel data abort. at pc 0xfffffe0023456789, lr 0x1\\n" +
        "Debugger message: panic\\nPanicked task 0xfffffe1a2b3c4d5e: 182 pages, 5 threads: pid 0: kernel_task\\n" +
        "last started kext at 1234567: com.vendor.fsdriver\\t3.2.1 (addr 0xfffffe0011112222, size 65536)\\n" +
        "Kernel Extensions in backtrace:\\n         com.vendor.fsdriver(3.2.1)[AAAA-BBBB]@0xfffffe0030000000->0xfffffe0030010000\\n" +
        "            dependency: com.apple.iokit.IOStorageFamily(2.1)[CCCC]@0xfffffe0040000000->0xfffffe0040001000\\n" +
        "         com.apple.filesystems.apfs(2332.1)[DDDD]@0xfffffe0050000000->0xfffffe0050100000\\n\\nlast loaded kext ...\"}";

    const string WatchdogText =
        "panic(cpu 0 caller 0xfffffe0011223344): userspace watchdog timeout: no successful checkins from WindowServer (2 induced crashes) in 120 seconds\n" +
        "Panicked task 0xfffffe1111111111: 0 pages, 100 threads: pid 1: launchd\n";

    const string KmutilOut =
        "Index Refs Address            Size       Wired      Name (Version) UUID <Linked Against>\n" +
        "    1  232 0                  0          0          com.apple.kpi.bsd (25.6.0) 3F7964F9-C5A6-39D4-942D-02750C0980E0 <>\n" +
        "  190    0 0xfffffe000abcd000 0x8000     0x8000     com.paragon-software.filesystems.ntfs (16.1.3) 1111-2222 <8 6 5 3 1>\n";

    const string SysExtOut =
        "1 extension(s)\n--- com.apple.system_extension.network_extension\n" +
        "enabled\tactive\tteamID\tbundleID (version)\tname\t[state]\n" +
        "\t*\t*\tABCDE12345\tcom.example.vpn.extension (2.4.0/240)\tExample VPN\t[activated enabled]\n";

    // system_profiler -json extract: a display serial number is present, as on a real Mac.
    const string ProfilerJson = """
        {"SPDisplaysDataType":[{"_name":"Apple M4 Pro","sppci_model":"Apple M4 Pro","sppci_cores":"20","spdisplays_mtlgpufamilysupport":"spdisplays_metal4",
          "spdisplays_ndrvs":[{"_name":"Color LCD","_spdisplays_display-serial-number":"fd626d62","_spdisplays_resolution":"1728 x 1117 @ 120.00Hz","spdisplays_connection_type":"spdisplays_internal","spdisplays_main":"spdisplays_yes"},
                              {"_name":"S24E390","_spdisplays_display-serial-number":"5a5a4751","_spdisplays_resolution":"1920 x 1080 @ 60.00Hz"}]}],
         "SPMemoryDataType":[{"dimm_manufacturer":"Micron","dimm_type":"LPDDR5","SPMemoryDataType":"48 GB"}],
         "SPUSBDataType":[{"_name":"USB31Bus","_items":[{"_name":"USB2.0 Hub","_items":[{"_name":"USB Keyboard","manufacturer":"Acme"}]}]}],
         "SPThunderboltDataType":[{"_name":"thunderboltusb4_bus_0","device_name_key":"MacBook Pro","_items":[{"_name":"CalDigit TS4","vendor_name_key":"CalDigit, Inc."}]}]}
        """;

    // GitHub /releases list: a Windows release (latest), a draft and a pre-release Mac release, and the real one.
    const string ReleasesJson = """
        [{"tag_name":"v7.0.0","draft":false,"prerelease":false,"html_url":"https://github.com/LucasRedjaimia/DumpFinder/releases/tag/v7.0.0","assets":[{"name":"DumpFinder.exe","browser_download_url":"https://github.com/LucasRedjaimia/DumpFinder/releases/download/v7.0.0/DumpFinder.exe"}]},
         {"tag_name":"mac-v0.9.0","draft":true,"prerelease":false},
         {"tag_name":"mac-v0.3.0","draft":false,"prerelease":true},
         {"tag_name":"mac-v0.2.0","name":"Dump Finder for Mac 0.2.0","body":"- notes","draft":false,"prerelease":false,"published_at":"2026-11-20T10:00:00Z",
          "html_url":"https://github.com/LucasRedjaimia/DumpFinder/releases/tag/mac-v0.2.0",
          "assets":[{"name":"DumpFinder-mac-0.2.0.zip","browser_download_url":"https://github.com/LucasRedjaimia/DumpFinder/releases/download/mac-v0.2.0/DumpFinder-mac-0.2.0.zip"}]},
         {"tag_name":"mac-v0.1.0","draft":false,"prerelease":false}]
        """;

    public static int Run(TextWriter log)
    {
        int failed = 0;
        void Check(bool ok, string name)
        {
            log.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
            if (!ok) failed++;
        }

        var data = DetectionData.Embedded;
        Check(data.Version >= 1 && data.PanicPatterns.Length > 0, "embedded detection data loads");
        Check(DetectionData.Parse("{\"version\":1,\"supportedMacOSMajors\":[26],\"kextVendors\":[{\"prefix\":\"com.apple.x\",\"category\":\"c\"}]}") is null,
              "detection data rejecting an Apple prefix");
        Check(DetectionData.Parse("not json") is null, "detection data rejecting invalid JSON");

        // Panic parsing
        var (h, body) = DiagnosticReports.SplitIps(PanicFile);
        var p = DiagnosticReports.ParsePanic("x.panic", h, body, DateTime.Now);
        DiagnosticReports.Classify(p, data);
        Check(p.Message.StartsWith("panic(cpu 3", StringComparison.Ordinal), "panic message");
        Check(p.PanickedProcess == "kernel_task", $"panicked task (got '{p.PanickedProcess}')");
        Check(p.LastStartedExtension == "com.vendor.fsdriver", $"last started kext (got '{p.LastStartedExtension}')");
        Check(p.BacktraceExtensions.SequenceEqual(new[] { "com.vendor.fsdriver", "com.apple.filesystems.apfs" }),
              $"backtrace extensions skip dependencies (got {string.Join(",", p.BacktraceExtensions)})");
        Check(p.PatternId == "data_abort", $"pattern data_abort (got '{p.PatternId}')");
        Check(p.Time == new DateTimeOffset(2026, 11, 15, 22, 4, 11, TimeSpan.FromHours(1)), "header timestamp with +0100 offset");

        var w = DiagnosticReports.ParsePanic("y.panic", null, WatchdogText, DateTime.Now);
        DiagnosticReports.Classify(w, data);
        Check(w.PatternId == "watchdog" && w.PanickedProcess == "launchd", "plain-text watchdog panic");

        // Robustness: truncated / garbage files must not throw.
        bool threw = false;
        foreach (var junk in new[] { "", "{", "{\"bug_type\":\"210\"}\n{\"panicString\":", "\0\0\0", PanicFile[..(PanicFile.Length / 2)] })
        {
            try { var (hh, bb) = DiagnosticReports.SplitIps(junk); DiagnosticReports.ParsePanic("j", hh, bb, DateTime.Now); DiagnosticReports.ParseIps("j.ips", hh, bb, "309", DateTime.Now); }
            catch (Exception e) { threw = true; log.WriteLine("    " + e.GetType().Name + ": " + e.Message); }
        }
        Check(!threw, "truncated or corrupted reports do not throw");

        // .ips crash report
        var ips = "{\"app_name\":\"Foo\",\"timestamp\":\"2026-11-16 15:57:22.00 +0100\",\"bug_type\":\"309\",\"is_first_party\":0,\"name\":\"Foo\"}\n" +
                  "{\"procName\":\"Foo\",\"exception\":{\"type\":\"EXC_BAD_ACCESS\",\"signal\":\"SIGSEGV\"},\"termination\":{\"indicator\":\"Segmentation fault: 11\"}}";
        var (ih, ib) = DiagnosticReports.SplitIps(ips);
        var c = DiagnosticReports.ParseIps("Foo-2026-11-16-155722.ips", ih, ib, "309", DateTime.Now);
        Check(c.Kind == ReportKind.Crash && c.Process == "Foo" && c.ExceptionType == "EXC_BAD_ACCESS" && c.Signal == "SIGSEGV" && !c.FirstParty, "ips crash fields");
        var j = DiagnosticReports.ParseIps("JetsamEvent-2026-11-10-092629.ips", null, "", "298", DateTime.Now);
        Check(j.Kind == ReportKind.MemoryPressure && j.Process == "JetsamEvent", $"jetsam report name from file (got '{j.Process}')");

        // Extensions
        var kexts = SystemProbe.ParseKmutil(KmutilOut).ToList();
        Check(kexts.Count == 2 && kexts[1].BundleId == "com.paragon-software.filesystems.ntfs" && kexts[1].Version == "16.1.3", "kmutil parsing");
        Check(data.VendorCategory(kexts[1].BundleId) == "filesystem", "kext vendor category");
        var sys = SystemProbe.ParseSystemExtensions(SysExtOut).ToList();
        Check(sys.Count == 1 && sys[0].BundleId == "com.example.vpn.extension" && sys[0].TeamId == "ABCDE12345" && sys[0].State == "activated enabled",
              $"systemextensionsctl parsing (got {sys.Count})");

        // Boot / shutdown reason
        var boot = SystemProbe.InterpretBoot("pwrbtn", " rst crash,crash0_in wdog,reset_in_1 rst btn_rst,btn", null, data);
        Check(boot.Abnormal && boot.Interpretation.Count >= 2, "abnormal shutdown tokens");
        var normal = SystemProbe.InterpretBoot("pwrbtn", "", null, data);
        Check(!normal.Abnormal, "normal power-button start");
        Check(SystemProbe.ParseBootTime("{ sec = 1793357287, usec = 753465 } Fri Oct 30 11:48:07 2026")?.ToUnixTimeSeconds() == 1793357287, "kern.boottime parsing");

        // Live readings and system_profiler (serial numbers must never reach the model)
        Check(Math.Abs(SystemProbe.ParseSwapUsed("total = 2048.00M  used = 556.50M  free = 1491.50M  (encrypted)") - 556.5 * 1048576) < 1, "vm.swapusage parsing");
        Check(SystemProbe.ParseMemoryFree("The system has 51539607552 (3145728 pages).\nSystem-wide memory free percentage: 91%") == 91, "memory_pressure parsing");
        Check(!SystemProbe.ParseTherm("Note: No thermal warning level has been recorded\nNote: No performance warning level has been recorded").Warning
              && !SystemProbe.ParseTherm("CPU_Scheduler_Limit \t= 100\nCPU_Available_CPUs \t= 8\nCPU_Speed_Limit \t= 100").Warning
              && SystemProbe.ParseTherm("CPU_Scheduler_Limit \t= 100\nCPU_Available_CPUs \t= 8\nCPU_Speed_Limit \t= 70").Warning
              && SystemProbe.ParseTherm("Thermal warning level set to 2.").Warning, "pmset -g therm parsing");
        var prof = new HardwareInfo();
        SystemProbe.FillFromProfiler(prof, ProfilerJson);
        Check(prof.GpuName == "Apple M4 Pro" && prof.GpuCores == 20 && prof.Metal == "Metal 4" && prof.MemoryType == "LPDDR5"
              && prof.Displays.Count == 2 && prof.Displays[0].BuiltIn && prof.Displays[1].Resolution == "1920 x 1080 @ 60.00Hz"
              && prof.Peripherals.Select(x => x.Name).SequenceEqual(new[] { "USB Keyboard", "CalDigit TS4" }),
              $"system_profiler displays, memory, USB and Thunderbolt (got {prof.Displays.Count} screens, {prof.Peripherals.Count} devices)");

        // Update check: only mac-v* releases, newest published one, links of this repository only
        Check(UpdateChecker.ParseVersion("mac-v0.2.0") == new Version(0, 2, 0) && UpdateChecker.ParseVersion("v6.1.0-beta") == new Version(6, 1, 0)
              && UpdateChecker.ParseVersion("0.3") == new Version(0, 3, 0) && UpdateChecker.ParseVersion("latest") is null, "release tag parsing");
        var latest = UpdateChecker.PickLatest(ReleasesJson);
        Check(latest is not null && latest.Version == new Version(0, 2, 0) && latest.DownloadUrl.EndsWith("DumpFinder-mac-0.2.0.zip", StringComparison.Ordinal),
              $"newest mac-v release, Windows / draft / pre-release ignored (got {latest?.Tag})");
        Check(!UpdateChecker.IsOwnUrl("https://github.com/someone/DumpFinder/releases") && !UpdateChecker.IsOwnUrl(UpdateChecker.RepoUrl + ".evil.com/x")
              && UpdateChecker.IsOwnUrl(UpdateChecker.RepoUrl + "/releases/tag/mac-v0.2.0"), "only links of this repository");
        var notes = UpdateChecker.NoteLines("# New\n- one\n  - two\n\n**bold** [link](https://x) `code`\n\n");
        Check(notes.Count == 5 && notes[0].Heading && notes[1].Text == "•  one" && notes[2].Indent == 16 && notes[3].Spacer && notes[4].Text == "bold link code",
              "release notes rendering");

        // Rules
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(1));
        var scan = new ScanResult
        {
            Panics = { p },
            KernelExtensions = { new KernelExtension("com.vendor.fsdriver", "3.2.1", true, "") },
            Hardware = new HardwareInfo { HasBattery = true, BatteryMaxCapacityPct = 75, BatteryCondition = "Service Recommended", BatteryCycles = 900, BootVolumeFree = 20_000_000_000, BootVolumeSize = 500_000_000_000, OsMajor = 26 },
        };
        var findings = DiagnosticsEngine.Analyze(scan, data, now);
        Check(findings[0].Id == "panic" && findings[0].Severity == Severity.Critical && findings[0].Title.Contains("com.vendor.fsdriver"), "panic finding names the third-party kext first");
        Check(findings.Any(x => x.Id == "battery_worn"), "worn battery finding");
        Check(findings.Any(x => x.Id == "low_disk" && x.Severity == Severity.Critical), "4 % free → critical");
        var clean = DiagnosticsEngine.Analyze(new ScanResult { Hardware = new HardwareInfo { OsMajor = 26 } }, data, now);
        Check(clean.Count == 1 && clean[0].Id == "ok", "clean machine → a single OK finding");

        // Hardware cards and extensions (levels follow the findings)
        scan.Findings.AddRange(findings);
        var cards = HardwareChecks.Build(scan, data, now);
        Check(cards.Single(x => x.Id == "kexts").Level == HealthLevel.Critical && cards.Single(x => x.Id == "battery").Level == HealthLevel.Warning
              && cards.Single(x => x.Id == "drive").Level == HealthLevel.Critical && cards.Single(x => x.Id == "sip").Tab == HardwareChecks.TabConflicts,
              "hardware cards take the level of their finding");
        var cleanScan = new ScanResult { Hardware = new HardwareInfo { OsMajor = 26, BootVolumeFree = 200_000_000_000, BootVolumeSize = 500_000_000_000, SipEnabled = true, GatekeeperEnabled = true } };
        cleanScan.Findings.AddRange(DiagnosticsEngine.Analyze(cleanScan, data, now));
        Check(HardwareChecks.Build(cleanScan, data, now).All(x => x.Level < HealthLevel.Warning), "clean machine → no card to check");
        var ext = HardwareChecks.Extensions(scan);
        Check(ext.Count == 1 && ext[0].Crashes == 1 && ext[0].Kernel, "extension blamed by a panic backtrace");

        // Privacy and exports
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var redacted = Privacy.Redact($"{home}/Library/x /Users/someone/a https://example.com/path?q=secret");
        Check(!redacted.Contains(home) && redacted.Contains("~/Library/x") && redacted.Contains("/Users/<user>/a") && redacted.Contains("https://example.com ") == false
              && redacted.EndsWith("https://example.com", StringComparison.Ordinal), $"privacy redaction (got '{redacted}')");
        var sample = new ScanResult { Panics = { p }, Hardware = new HardwareInfo { MachineName = "MacBook Pro", OsVersion = "macOS 26.6", OsMajor = 26 } };
        sample.Findings.AddRange(DiagnosticsEngine.Analyze(sample, data, now));
        var summary = CrashTimeline.Build(sample, data, now.LocalDateTime);
        bool exportsOk = true;
        foreach (var lang in new[] { "en", "fr", "ru", "ja" })
        {
            Loc.SetLanguage(lang);
            try
            {
                var html = ReportBuilder.Html(sample, summary);
                exportsOk &= html.StartsWith("<!doctype html>", StringComparison.Ordinal) && html.Contains("com.vendor.fsdriver") && html.EndsWith("</html>", StringComparison.Ordinal);
                exportsOk &= System.Text.Json.JsonDocument.Parse(ReportBuilder.Json(sample, summary)).RootElement.GetProperty("crashes").GetArrayLength() == 1;
                exportsOk &= ReportBuilder.Markdown(sample, summary).Length > 0 && ReportBuilder.Text(sample, summary).Length > 0;
                exportsOk &= CrashDetails.ForumMarkdown(summary.Crashes[0], sample).Contains("com.vendor.fsdriver");
            }
            catch (Exception e) { exportsOk = false; log.WriteLine($"    {lang}: {e.GetType().Name}: {e.Message}"); }
        }
        Loc.SetLanguage("en");
        Check(exportsOk, "HTML / JSON / Markdown / text exports in 4 languages");
        // Translations: every language has every Mac key, with the same placeholders ({0}, {1:…}); the "one" plural
        // form of some languages may leave out {0}.
        var en = Loc.MacTable("en");
        var placeholder = new System.Text.RegularExpressions.Regex(@"\{\d+(?::[^}]*)?\}");
        foreach (var code in Loc.Codes.Where(c => c != "en"))
        {
            var t = Loc.MacTable(code);
            var missing = en.Keys.Where(k => !t.ContainsKey(k)).ToList();
            var badArgs = en.Where(kv => t.TryGetValue(kv.Key, out var v)
                && !placeholder.Matches(kv.Value).Select(m => m.Value).Where(p => p != "{0}").ToHashSet()
                    .IsSubsetOf(placeholder.Matches(v).Select(m => m.Value).ToHashSet())).Select(kv => kv.Key).ToList();
            Check(missing.Count == 0 && badArgs.Count == 0,
                  $"mac/Locales/{code}.json complete" + (missing.Count + badArgs.Count > 0 ? $" (missing {missing.Count}: {string.Join(", ", missing.Take(3))}; placeholders: {string.Join(", ", badArgs.Take(3))})" : ""));
        }
        Check(KnowledgeBase.Entries.All(k => k.Title != "mac.kb." + k.Id + ".title" && k.Solution != "mac.kb." + k.Id + ".fix"), "every known-issue article has English texts");

        log.WriteLine(failed == 0 ? "All checks passed." : $"{failed} check(s) failed.");
        return failed;
    }
}
