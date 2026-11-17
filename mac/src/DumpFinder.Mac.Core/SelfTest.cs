using System.Text;
using System.Text.Json;

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
        var previousScanForFixes = McpServer.Scan;
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
        // AI assistant: prompt layout, focus question, redaction, Gemini stream chunks, key check
        var prompt = AiAssistant.BuildPrompt(sample, summary, AiAssistant.FocusQuestion(summary.Crashes[0]));
        Check(prompt.StartsWith(Loc.S("mac.ai.role"), StringComparison.Ordinal) && prompt.Contains("=== KERNEL PANICS ===") && prompt.Contains("com.vendor.fsdriver")
              && prompt.Contains("USER CONTEXT") && !prompt.Contains(home), "AI prompt: role, question, panics, redacted");
        Check(AiAssistant.GeminiChunkText("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"Hel\"},{\"text\":\"lo\"}]}}]}") == "Hello"
              && AiAssistant.GeminiChunkText("{\"usageMetadata\":{}}") == "" && AiAssistant.GeminiChunkText("not json") == "", "Gemini stream chunk parsing");
        Check(SecretStore.IsSafe("sk-ant-api03-AbC_123") && !SecretStore.IsSafe("a\"b") && !SecretStore.IsSafe("a b") && !SecretStore.IsSafe(""), "API key characters check");

        // MCP server: protocol layer on a synthetic scan (no access to the Mac)
        var previousScan = McpServer.Scan;
        McpServer.Scan = _ => (sample, summary);
        try
        {
            var init = McpServer.Handle("{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"protocolVersion\":\"2025-06-18\"}}") ?? "";
            Check(init.Contains("\"protocolVersion\":\"2025-06-18\"") && init.Contains("\"name\":\"dumpfinder\""), "MCP initialize");
            Check(McpServer.Handle("{\"jsonrpc\":\"2.0\",\"method\":\"notifications/initialized\"}") is null, "MCP notifications get no answer");
            using var tools = JsonDocument.Parse(McpServer.Handle("{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"tools/list\"}")!);
            Check(tools.RootElement.GetProperty("result").GetProperty("tools").GetArrayLength() == 7, "MCP tools/list: 7 tools");
            Check((McpServer.Handle("{\"jsonrpc\":\"2.0\",\"id\":3,\"method\":\"nope\"}") ?? "").Contains("-32601")
                  && (McpServer.Handle("{oops") ?? "").Contains("-32700"), "MCP unknown method and parse errors");
            string Call(string tool, string args)
            {
                using var d = JsonDocument.Parse(McpServer.Handle($"{{\"jsonrpc\":\"2.0\",\"id\":\"x\",\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\",\"arguments\":{args}}}}}")!);
                return d.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            }
            var list = Call("mac_get_crash_detail", "{}");
            var id = McpServer.CrashId(summary.Crashes[0]);
            Check(list.Contains("crash_id: " + id) && Call("mac_get_crash_detail", $"{{\"crash_id\":\"{id}\"}}").Contains("Plain explanation:"), "MCP crash list and detail");
            Check(Call("mac_scan_system", "{\"days\":0}").Contains("[KERNEL PANICS]") && Call("mac_export_report", "{\"format\":\"json\"}").TrimStart().StartsWith('{'),
                  "MCP scan and JSON report");
        }
        finally { McpServer.Scan = previousScan; }

        // MCP registration: other servers kept, a .bak written, a non-standard file left alone
        var tmpDir = Path.Combine(Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cfg = Path.Combine(tmpDir, "cfg.json");
            Directory.CreateDirectory(tmpDir);
            File.WriteAllText(cfg, "{\"theme\":\"dark\",\"mcpServers\":{\"other\":{\"command\":\"x\"}}}");
            McpServer.MergeServerIntoConfig(cfg, "/Applications/Dump Finder.app/Contents/MacOS/DumpFinder");
            using (var merged = JsonDocument.Parse(File.ReadAllText(cfg)))
                Check(merged.RootElement.GetProperty("theme").GetString() == "dark" && merged.RootElement.GetProperty("mcpServers").TryGetProperty("other", out _)
                      && merged.RootElement.GetProperty("mcpServers").GetProperty("dumpfinder").GetProperty("args")[0].GetString() == "--mcp" && File.Exists(cfg + ".bak"),
                      "MCP config merge keeps other servers");
            var bad = Path.Combine(tmpDir, "bad.json");
            File.WriteAllText(bad, "{ // comment\n \"mcpServers\": {} }");
            bool refused = false;
            try { McpServer.MergeServerIntoConfig(bad, "x"); } catch (InvalidDataException) { refused = true; }
            Check(refused && File.ReadAllText(bad).Contains("// comment"), "MCP config: non-standard JSON left untouched");
            Check(McpServer.IsTranslocated("/private/var/folders/x/T/AppTranslocation/ABC/d/Dump Finder.app/Contents/MacOS/DumpFinder")
                  && !McpServer.IsTranslocated("/Applications/Dump Finder.app/Contents/MacOS/DumpFinder"), "translocated app detected before registering");
        }
        finally { try { Directory.Delete(tmpDir, true); } catch (IOException) { } }

        // Sentinel: which new reports alert, the login item, crashes not seen yet
        Check(CrashWatcher.Classify("panic-full-2026-11-17-101112.panic") == LiveAlert.Panic && CrashWatcher.Classify("Kernel-2026-11-17.panic") == LiveAlert.Panic
              && CrashWatcher.Classify("panic-base-2026-11-17.ips") == LiveAlert.Panic && CrashWatcher.Classify("JetsamEvent-2026-11-17-092629.ips") == LiveAlert.Memory
              && CrashWatcher.Classify("Safari-2026-11-17-101112.ips") == LiveAlert.None && CrashWatcher.Classify("x.diag") == LiveAlert.None, "Sentinel alert classification");
        var agentsBefore = LoginItem.AgentsDir;
        var rootBefore = AppStore.Root;
        var sentinelDir = Path.Combine(Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
        try
        {
            LoginItem.AgentsDir = sentinelDir;
            const string exe1 = "/Applications/Dump Finder.app/Contents/MacOS/DumpFinder", exe2 = "/Users/x/Apps/Dump & Finder.app/Contents/MacOS/DumpFinder";
            bool on = LoginItem.Set(true, exe1) && LoginItem.IsEnabled;
            var plist = Path.Combine(sentinelDir, LoginItem.Label + ".plist");
            bool lint = !File.Exists("/usr/bin/plutil") || Proc.Run("/usr/bin/plutil", new[] { "-lint", plist }, 5000).ExitCode == 0;
            LoginItem.RefreshPathIfEnabled(exe2);
            bool moved = File.ReadAllText(plist).Contains("Dump &amp; Finder.app", StringComparison.Ordinal);
            bool lint2 = !File.Exists("/usr/bin/plutil") || Proc.Run("/usr/bin/plutil", new[] { "-lint", plist }, 5000).ExitCode == 0;
            bool off = LoginItem.Set(false, exe1) && !LoginItem.IsEnabled && !File.Exists(plist);
            bool refused = !LoginItem.Set(true, "/private/var/folders/x/AppTranslocation/y/Dump Finder.app/Contents/MacOS/DumpFinder");
            Check(on && lint && moved && lint2 && off && refused, "login item: written, valid plist, follows the app, removed, refused when translocated");

            AppStore.Root = sentinelDir;
            bool firstEmpty = History.PeekNew(new[] { "a" }).Count == 0;
            History.MarkSeen(new[] { "a" });
            var peek = History.PeekNew(new[] { "a", "b" });
            var peekAgain = History.PeekNew(new[] { "a", "b" });
            Check(firstEmpty && peek.SetEquals(new[] { "b" }) && peekAgain.SetEquals(new[] { "b" }), "unseen crashes peeked without being marked");
        }
        finally
        {
            LoginItem.AgentsDir = agentsBefore;
            AppStore.Root = rootBefore;
            try { Directory.Delete(sentinelDir, true); } catch (IOException) { }
        }

        // Fixes, Optimize and the journal, in a temporary home (admin commands recorded, not run)
        var fixHome = Path.Combine(Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
        var homeBefore = MacPaths.Home;
        var rootBefore2 = AppStore.Root;
        var adminCalls = new List<string>();
        try
        {
            MacPaths.Home = fixHome;
            AppStore.Root = Path.Combine(fixHome, "store");
            Admin.Runner = cmds => { adminCalls.AddRange(cmds); return ""; };
            StartupAgents.UseLaunchctl = false;
            var chrome = Path.Combine(MacPaths.AppSupport, "Google", "Chrome", "Local State");
            var discordFile = Path.Combine(MacPaths.AppSupport, "discord", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(chrome)!);
            Directory.CreateDirectory(Path.GetDirectoryName(discordFile)!);
            const string chromeJson = "{\"browser\":{\"x\":1},\"hardware_acceleration_mode\":{\"enabled\":true}}";
            File.WriteAllText(chrome, chromeJson);
            File.WriteAllText(discordFile, "{\"BACKGROUND_COLOR\":\"#202225\"}");

            var fixes = FixCatalog.Inspect(null, null);
            Check(fixes.Single(f => f.Id == "chrome_gpu").Status == FixStatus.Optional && fixes.Single(f => f.Id == "discord_hwaccel").Status == FixStatus.Optional
                  && !fixes.Any(f => f.Id == "edge_gpu") && fixes.Single(f => f.Id == "first_aid").IsTool, "fix catalog reads the installed apps");
            var res = FixCatalog.RunAndRecord(new[] { "chrome_gpu_apply", "discord_hwaccel_apply" }, op => op);
            bool chromeOff = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(chrome))!["hardware_acceleration_mode"]!["enabled"]!.GetValue<bool>() == false;
            bool discordOff = File.ReadAllText(discordFile).Contains("\"enableHardwareAcceleration\":false");
            var applied = FixCatalog.Inspect(null, null);
            Check(res.All(x => x.Ok) && chromeOff && discordOff && applied.Single(f => f.Id == "chrome_gpu").Status == FixStatus.Applied
                  && File.ReadAllText(chrome).Contains("\"browser\""), "browser and Discord fixes applied, other settings kept");
            var journal = Journal.Entries();
            Check(journal.Count == 2 && journal.All(e => e.Reversible && e.Changes.Count == 1), "each fix journaled with its backup");
            Check(Journal.Revert(journal.Single(e => e.Op == "chrome_gpu_apply")) == "" && File.ReadAllText(chrome) == chromeJson, "undo puts the exact file back");

            var nap = Pmset.Get("powernap", " sleep 1\n powernap 1\n");
            var napRun = FixCatalog.Run(new[] { "powernap_apply" });
            var napUndo = Journal.Revert(new JournalEntry { Changes = napRun[0].Changes });
            Check(nap == "1" && (napRun[0].Ok == (Pmset.Get("powernap") != "")) && (napRun[0].Changes.Count == 0 || (adminCalls.Contains("/usr/bin/pmset -a 'powernap' '0'")
                  && napUndo == "" && adminCalls.Any(c => c.StartsWith("/usr/bin/pmset -a 'powernap' '", StringComparison.Ordinal) && !c.EndsWith("'0'", StringComparison.Ordinal)))),
                  "Power Nap: one admin command, undo writes the previous value");
            adminCalls.Clear();
            Admin.Runner = _ => "cancelled";
            Check(FixCatalog.Run(new[] { "security_updates_apply" }) is [{ Ok: false, Message: "cancelled" }], "admin prompt cancelled → fix failed, nothing journaled");
            Admin.Runner = cmds => { adminCalls.AddRange(cmds); return ""; };
            Check(Admin.Sh("it's") == "'it'\\''s'" && Admin.AppleScriptEscape("a\"b\\c") == "a\\\"b\\\\c", "shell and AppleScript quoting");

            // Optimize: a preference on a private plist, cleanup to a private Trash, a LaunchAgent moved aside
            var plistDomain = Path.Combine(fixHome, "prefs");
            var tweak = new Tweak("ds_network", plistDomain, "DSDontWriteNetworkStores", "bool", "true", true);
            var tw = Optimizer.RunTweak(tweak, false);
            bool on = Optimizer.IsApplied(tweak);
            var twUndo = Journal.Revert(Journal.Entries().First(e => e.Op == "opt_ds_network_apply"));
            Check(tw.Ok && on && twUndo == "" && !Defaults.Read(plistDomain, "DSDontWriteNetworkStores").Exists, "preference applied then removed again by undo");

            var cache = Path.Combine(fixHome, "Library", "Caches", "com.example.app");
            Directory.CreateDirectory(cache);
            File.WriteAllBytes(Path.Combine(cache, "blob"), new byte[4096]);
            Directory.SetLastWriteTime(cache, DateTime.Now.AddDays(-3));
            var fresh = Path.Combine(fixHome, "Library", "Caches", "com.example.busy");
            Directory.CreateDirectory(fresh);
            var measured = Optimizer.MeasureCleanup().Single(c => c.Id == "caches");
            var cleaned = Optimizer.Clean(new[] { "caches" });
            bool inTrash = Directory.Exists(Path.Combine(fixHome, ".Trash", "com.example.app")) && !Directory.Exists(cache) && Directory.Exists(fresh);
            var cleanUndo = Journal.Revert(Journal.Entries().First(e => e.Op == "opt_clean_caches_apply"));
            Check(measured.Bytes == 4096 && measured.Count == 1 && cleaned[0].Ok && inTrash && cleanUndo == "" && File.Exists(Path.Combine(cache, "blob")),
                  "cleanup: recent items kept, the rest to the Trash, undo puts it back");

            var agentsDir = Path.Combine(fixHome, "Library", "LaunchAgents");
            Directory.CreateDirectory(agentsDir);
            File.WriteAllText(Path.Combine(agentsDir, "com.example.helper.plist"), LoginItem.Plist("/Applications/Example.app/Contents/MacOS/helper").Replace(LoginItem.Label, "com.example.helper"));
            var agent = Optimizer.Agents().Single();
            var dis = Optimizer.SetAgent(agent, enable: false);
            bool moved = !File.Exists(Path.Combine(agentsDir, "com.example.helper.plist")) && Optimizer.Agents().Single() is { Enabled: false };
            var agUndo = Journal.Revert(Journal.Entries().First(e => e.Op == "opt_startup_disable"));
            Check(agent.Label == "com.example.helper" && agent.Program.Contains("--tray") && dis.Ok && moved && agUndo == "" && Optimizer.Agents().Single().Enabled,
                  "startup agent disabled (moved aside, nothing deleted) and undone");

            // MCP: dry run, refusal on screen, approval
            McpServer.Scan = _ => (new ScanResult(), new StabilitySummary());
            string Fix(string args)
            {
                using var d = JsonDocument.Parse(McpServer.Handle($"{{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"tools/call\",\"params\":{{\"name\":\"mac_apply_fix\",\"arguments\":{args}}}}}")!);
                return d.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            }
            McpServer.Approve = _ => false;
            var runningBefore = McpServer.AppRunning;
            McpServer.AppRunning = _ => false;
            var dry = Fix("{\"fix_id\":\"chrome_gpu\"}");
            var declined = Fix("{\"fix_id\":\"chrome_gpu\",\"confirm\":true}");
            McpServer.Approve = _ => true;
            var approved = Fix("{\"fix_id\":\"chrome_gpu\",\"confirm\":true}");
            var tool = Fix("{\"fix_id\":\"first_aid\",\"confirm\":true}");
            McpServer.AppRunning = _ => true;
            var open = Fix("{\"fix_id\":\"discord_hwaccel\",\"restore\":false,\"confirm\":true}");
            McpServer.AppRunning = runningBefore;
            Check(dry.StartsWith("[DRY RUN]", StringComparison.Ordinal) && declined.StartsWith("[DECLINED]", StringComparison.Ordinal)
                  && approved.StartsWith("[SUCCESS]", StringComparison.Ordinal) && tool.StartsWith("[REFUSED]", StringComparison.Ordinal) && open.Contains("is open", StringComparison.Ordinal),
                  "MCP apply_fix: dry run, declined, approved, tool refused, refused while the app is open");
        }
        finally
        {
            MacPaths.Home = homeBefore;
            AppStore.Root = rootBefore2;
            Admin.Runner = null;
            StartupAgents.UseLaunchctl = true;
            McpServer.Approve = null;
            McpServer.Scan = previousScanForFixes;
            try { Directory.Delete(fixHome, true); } catch (IOException) { }
        }

        // Stability test: engines clean then with an injected fault, verdicts, the run, crash recovery
        {
            var second = TimeSpan.FromSeconds(1);
            var cpuOk = StressEngines.Cpu(second, CancellationToken.None, _ => { }, 2);
            var ramOk = StressEngines.Ram(256L << 20, second, CancellationToken.None, _ => { });
            var diskDir = Path.Combine(Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
            var diskOk = StressEngines.Disk(diskDir, 64L << 20, second, CancellationToken.None, _ => { });
            StressEngines.InjectFault = true;
            var cpuBad = StressEngines.Cpu(second, CancellationToken.None, _ => { }, 2);
            var ramBad = StressEngines.Ram(256L << 20, second, CancellationToken.None, _ => { });
            var diskBad = StressEngines.Disk(diskDir, 64L << 20, second, CancellationToken.None, _ => { });
            StressEngines.InjectFault = false;
            try { Directory.Delete(diskDir, true); } catch (IOException) { }
            Check(cpuOk.Errors == 0 && ramOk.Errors == 0 && diskOk.Errors == 0 && cpuOk.Rate > 0 && diskOk.Rate > 0,
                  $"stress engines: no error on a healthy run (cpu {cpuOk.Errors}, ram {ramOk.Errors}, disk {diskOk.Errors})");
            Check(cpuBad.Errors > 0 && ramBad.Errors > 0 && diskBad.Errors > 0, "stress engines: an injected fault is caught by each engine");
            Check(StressEngines.RamBudget(16L << 30, 50) == (16L << 30) * 50 / 100 * 6 / 10 && StressEngines.RamBudget(16L << 30, 100) == (16L << 30) * 6 / 10
                  && StressEngines.DiskBudget(10L << 30) == (10L << 30) / 20 && StressEngines.DiskBudget(1L << 40) == 2L << 30, "memory and disk budgets");

            Check(StressTest.Verdict(new StageOutcome(StressStage.Ram, true, 3, 1, "MB/s", second), false, false).Outcome == StressOutcome.Failed
                  && StressTest.Verdict(new StageOutcome(StressStage.Cpu, true, 0, 1, "ops/s", second), true, false).Outcome == StressOutcome.Warning
                  && StressTest.Verdict(new StageOutcome(StressStage.Gpu, false, 0, 0, "GFLOPS", TimeSpan.Zero, "helper"), false, false).Outcome == StressOutcome.Skipped
                  && StressTest.Verdict(new StageOutcome(StressStage.Disk, true, 0, 1, "MB/s", second, "stopped"), false, true).Outcome == StressOutcome.Stopped,
                  "stage verdicts: errors, throttling, missing helper, stop");
            Check(StressTest.Total(StressTest.Plan(StressPlanKind.Quick, 10)) == TimeSpan.FromMinutes(10) && StressTest.Plan(StressPlanKind.Disk, 500)[0].Duration == TimeSpan.FromMinutes(120),
                  "plans: quick is 10 min, a single stage is capped at 120 min");

            var stressRoot = Path.Combine(Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
            var rootBefore3 = AppStore.Root;
            try
            {
                AppStore.Root = stressRoot;
                StressTest.Engine = (p, ct, tick) => { tick(new StressTick(p.Stage, 1, 10, "MB/s", 0)); return new StageOutcome(p.Stage, true, p.Stage == StressStage.Ram ? 2 : 0, 10, "MB/s", second); };
                StressTest.ThermalProbe = () => false;
                int ticks = 0;
                var run = StressTest.Run(StressPlanKind.Quick, StressTest.Plan(StressPlanKind.Quick, 10), new HardwareInfo(), CancellationToken.None, (_, _) => ticks++);
                bool markerGone = !File.Exists(Path.Combine(stressRoot, "history", "stress_running.json"));
                Check(run.Completed && run.AnyFailure && run.Stages.Count == 4 && run.Stages[1].Outcome == StressOutcome.Failed && ticks == 4 && markerGone
                      && StressTest.Runs().Count == 1 && StressTest.RunTone(run) == "danger", "a run: stages in order, failure kept, saved, marker cleared");

                var marker = new StressMarker { Started = DateTime.Now.AddMinutes(-5), Plan = StressPlanKind.Quick, Stage = StressStage.Gpu, StageStarted = DateTime.Now.AddMinutes(-2) };
                AppStore.Write("history/stress_running.json", marker);
                var crashed = StressTest.RecoverInterrupted(DateTimeOffset.Now.AddMinutes(-1));
                AppStore.Write("history/stress_running.json", marker);
                var quit = StressTest.RecoverInterrupted(DateTimeOffset.Now.AddHours(-3));
                Check(crashed?.Stages.Last().Outcome == StressOutcome.Interrupted && quit?.Stages.Last().Outcome == StressOutcome.Stopped
                      && StressTest.RecoverInterrupted(null) is null, "a marker left behind: crash when the Mac booted after the stage started, else the app was quit");
            }
            finally
            {
                AppStore.Root = rootBefore3;
                StressTest.Engine = null;
                StressTest.ThermalProbe = null;
                try { Directory.Delete(stressRoot, true); } catch (IOException) { }
            }
        }

        // Storage: sizes of a small tree, largest first, protected items, Trash and Undo
        {
            var spaceHome = Path.Combine(Path.GetTempPath(), "dumpfinder-selftest-" + Guid.NewGuid().ToString("N"));
            var homeBefore2 = MacPaths.Home;
            var rootBefore4 = AppStore.Root;
            try
            {
                MacPaths.Home = spaceHome;
                AppStore.Root = Path.Combine(spaceHome, "store");
                Directory.CreateDirectory(Path.Combine(spaceHome, "Projects", "big"));
                Directory.CreateDirectory(Path.Combine(spaceHome, "Documents"));
                Directory.CreateDirectory(Path.Combine(spaceHome, ".ssh"));
                File.WriteAllBytes(Path.Combine(spaceHome, "Projects", "big", "video.mov"), new byte[3 << 20]);
                File.WriteAllBytes(Path.Combine(spaceHome, "Projects", "notes.txt"), new byte[1 << 20]);
                File.WriteAllBytes(Path.Combine(spaceHome, "Documents", "cv.pdf"), new byte[2 << 20]);
                File.WriteAllBytes(Path.Combine(spaceHome, ".ssh", "id_ed25519"), new byte[100]);
                var spaceScan = DiskSpaceScan.Scan(spaceHome, CancellationToken.None, bigFile: 1 << 20);
                var top = spaceScan.ChildrenOf(spaceHome);
                Check(spaceScan.SizeOf(Path.Combine(spaceHome, "Projects")) == 4 << 20 && top[0].Name == "Projects" && top[1].Name == "Documents"
                      && spaceScan.LargestFiles[0].Name == "video.mov", "storage: folder sizes summed, largest first, largest files");
                Check(DiskSpaceScan.IsProtected(Path.Combine(spaceHome, "Documents")) && DiskSpaceScan.IsProtected(Path.Combine(spaceHome, ".ssh"))
                      && DiskSpaceScan.IsProtected(Path.Combine(spaceHome, "Library", "Keychains")) && DiskSpaceScan.IsProtected(spaceHome)
                      && !DiskSpaceScan.IsProtected(Path.Combine(spaceHome, "Projects")) && !DiskSpaceScan.IsProtected(Path.Combine(spaceHome, "Documents", "cv.pdf"))
                      && !DiskSpaceScan.IsProtected(Path.Combine(spaceHome, "Library", "Caches", "x")) && DiskSpaceScan.IsProtected("/System/Applications/Safari.app"),
                      "storage: essentials protected, the rest can go to the Trash");
                var target = Path.Combine(spaceHome, "Projects", "big");
                var (ok, _, byFinder) = DiskSpaceScan.MoveToTrash(target, "big");
                bool trashed = ok && !byFinder && !Directory.Exists(target) && Directory.Exists(Path.Combine(spaceHome, ".Trash", "big"));
                spaceScan.Forget(target);
                bool parentShrank = spaceScan.SizeOf(Path.Combine(spaceHome, "Projects")) == 1 << 20 && spaceScan.LargestFiles.All(f => f.Name != "video.mov");
                var undo = Journal.Revert(Journal.Entries().First(e => e.Op == "space_trash"));
                Check(trashed && parentShrank && undo == "" && File.Exists(Path.Combine(target, "video.mov"))
                      && !DiskSpaceScan.MoveToTrash(Path.Combine(spaceHome, "Documents"), "Documents").Ok, "storage: to the Trash, journaled, undone; protected refused");
            }
            finally
            {
                MacPaths.Home = homeBefore2;
                AppStore.Root = rootBefore4;
                try { Directory.Delete(spaceHome, true); } catch (IOException) { }
            }
        }

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
