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
        Check(data.VendorCategory(kexts[1].BundleId)?.StartsWith("File system", StringComparison.Ordinal) == true, "kext vendor category");
        var sys = SystemProbe.ParseSystemExtensions(SysExtOut).ToList();
        Check(sys.Count == 1 && sys[0].BundleId == "com.example.vpn.extension" && sys[0].TeamId == "ABCDE12345" && sys[0].State == "activated enabled",
              $"systemextensionsctl parsing (got {sys.Count})");

        // Boot / shutdown reason
        var boot = SystemProbe.InterpretBoot("pwrbtn", " rst crash,crash0_in wdog,reset_in_1 rst btn_rst,btn", null, data);
        Check(boot.Abnormal && boot.Interpretation.Count >= 2, "abnormal shutdown tokens");
        var normal = SystemProbe.InterpretBoot("pwrbtn", "", null, data);
        Check(!normal.Abnormal, "normal power-button start");
        Check(SystemProbe.ParseBootTime("{ sec = 1793357287, usec = 753465 } Fri Oct 30 11:48:07 2026")?.ToUnixTimeSeconds() == 1793357287, "kern.boottime parsing");

        // Rules
        var now = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.FromHours(1));
        var scan = new ScanResult
        {
            Panics = { p },
            KernelExtensions = { new KernelExtension("com.vendor.fsdriver", "3.2.1", true, "") },
            Hardware = new HardwareInfo { HasBattery = true, BatteryMaxCapacityPct = 75, BatteryCondition = "Service Recommended", BatteryCycles = 900, BootVolumeFree = 20_000_000_000, BootVolumeSize = 500_000_000_000, OsMajor = 26 },
        };
        var findings = DiagnosticsEngine.Analyze(scan, data, now);
        Check(findings[0].Severity == Severity.Critical && findings[0].Title.Contains("com.vendor.fsdriver"), "panic finding names the third-party kext first");
        Check(findings.Any(x => x.Title.StartsWith("Battery worn", StringComparison.Ordinal)), "worn battery finding");
        Check(findings.Any(x => x.Title.StartsWith("Low free space", StringComparison.Ordinal) && x.Severity == Severity.Critical), "4 % free → critical");
        var clean = DiagnosticsEngine.Analyze(new ScanResult { Hardware = new HardwareInfo { OsMajor = 26 } }, data, now);
        Check(clean.Count == 1 && clean[0].Severity == Severity.Ok, "clean machine → a single OK finding");

        log.WriteLine(failed == 0 ? "All checks passed." : $"{failed} check(s) failed.");
        return failed;
    }
}
