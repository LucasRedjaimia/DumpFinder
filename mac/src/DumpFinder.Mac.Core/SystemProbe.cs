using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DumpFinder.Mac.Core;

/// <summary>Read-only probes of the running Mac: boot / shutdown reason, extensions, hardware.</summary>
public static partial class SystemProbe
{
    // ---------- Boot and previous shutdown ----------

    /// <summary>
    /// Apple Silicon exposes the last shutdown and boot causes through sysctl (kern.shutdownreason,
    /// kern.bootreason). Their tokens are not documented by Apple: the reading is a heuristic.
    /// </summary>
    public static BootInfo ReadBoot(DetectionData data)
    {
        var shutdown = Proc.Sysctl("kern.shutdownreason");
        var boot = Proc.Sysctl("kern.bootreason");
        return InterpretBoot(boot, shutdown, ParseBootTime(Proc.Sysctl("kern.boottime")), data);
    }

    public static BootInfo InterpretBoot(string boot, string shutdown, DateTimeOffset? bootTime, DetectionData data)
    {
        // Only the shutdown string tells how the previous session ended; the boot reason is shown as is.
        var tokens = TokenRx().Matches(shutdown).Select(m => m.Value.ToLowerInvariant()).ToHashSet();
        var readings = new List<string>();
        bool abnormal = false;
        foreach (var t in data.ShutdownTokens)
        {
            if (!tokens.Contains(t.Token.ToLowerInvariant())) continue;
            readings.Add(t.Meaning);
            abnormal |= t.Abnormal;
        }
        return new BootInfo { BootTime = bootTime, BootReason = boot, ShutdownReason = shutdown.Trim(), Interpretation = readings, Abnormal = abnormal };
    }

    /// <summary>"{ sec = 1793357287, usec = 753465 } Fri Oct 30 …" → time.</summary>
    public static DateTimeOffset? ParseBootTime(string s)
    {
        var m = BootTimeRx().Match(s);
        return m.Success && long.TryParse(m.Groups[1].Value, out var sec)
            ? DateTimeOffset.FromUnixTimeSeconds(sec).ToLocalTime()
            : null;
    }

    // ---------- Kernel extensions (kexts) and system extensions ----------

    public static List<KernelExtension> ReadKernelExtensions(List<string> errors)
    {
        var result = new List<KernelExtension>();
        var r = Proc.Run("/usr/bin/kmutil", new[] { "showloaded", "--list-only" }, 30000);
        if (r.TimedOut || r.ExitCode != 0) errors.Add("kmutil showloaded failed: loaded kernel extensions are unknown.");
        else result.AddRange(ParseKmutil(r.Stdout).Where(k => !IsApple(k.BundleId)));

        // Kexts installed on disk but not loaded: often leftovers of uninstalled software.
        foreach (var dir in new[] { "/Library/Extensions" })
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var kext in Directory.EnumerateDirectories(dir, "*.kext"))
            {
                var id = ReadBundleId(kext) ?? Path.GetFileNameWithoutExtension(kext);
                if (IsApple(id) || result.Any(k => k.BundleId == id) || InstalledByMacOS(kext)) continue;
                result.Add(new KernelExtension(id, "", false, kext));
            }
        }
        return result;
    }

    /// <summary>"   12    0 0xfff… 0x1000 0x1000 com.foo.bar (1.2.3) UUID &lt;…&gt;" → (com.foo.bar, 1.2.3).</summary>
    public static IEnumerable<KernelExtension> ParseKmutil(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            var m = KmutilLineRx().Match(line);
            if (m.Success) yield return new KernelExtension(m.Groups[1].Value, m.Groups[2].Value, true, "");
        }
    }

    public static List<SystemExtension> ReadSystemExtensions(List<string> errors)
    {
        var r = Proc.Run("/usr/bin/systemextensionsctl", new[] { "list" }, 15000);
        if (r.TimedOut || r.ExitCode != 0) { errors.Add("systemextensionsctl list failed."); return new(); }
        return ParseSystemExtensions(r.Stdout).ToList();
    }

    /// <summary>Lines like "*\t*\tTEAMID\tcom.foo.ext (1.0/1)\tName\t[activated enabled]".</summary>
    public static IEnumerable<SystemExtension> ParseSystemExtensions(string stdout)
    {
        foreach (var line in stdout.Split('\n'))
        {
            var m = SysExtLineRx().Match(line);
            if (m.Success)
                yield return new SystemExtension(m.Groups[2].Value, m.Groups[3].Value, m.Groups[1].Value, m.Groups[4].Value.Trim());
        }
    }

    static bool IsApple(string bundleId) => bundleId.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// macOS itself ships a few third-party kexts in /Library/Extensions (HighPoint, ATTO…): they belong
    /// to an Apple package (pkgid com.apple.*) and are not leftovers.
    /// </summary>
    static bool InstalledByMacOS(string path)
    {
        var r = Proc.Run("/usr/sbin/pkgutil", new[] { "--file-info", path }, 5000);
        return r.ExitCode == 0 && r.Stdout.Split('\n').Any(l => l.StartsWith("pkgid: com.apple.", StringComparison.Ordinal));
    }

    static string? ReadBundleId(string kextPath)
    {
        var plist = Path.Combine(kextPath, "Contents", "Info.plist");
        if (!File.Exists(plist)) return null;
        try
        {
            var m = PlistBundleIdRx().Match(File.ReadAllText(plist));
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    // ---------- Hardware, storage, battery, security ----------

    public static HardwareInfo ReadHardware(List<string> errors)
    {
        var hw = new HardwareInfo();
        // -detailLevel mini leaves out serial numbers and other identifiers.
        var r = Proc.Run("/usr/sbin/system_profiler",
            new[] { "-json", "-detailLevel", "mini", "SPHardwareDataType", "SPSoftwareDataType", "SPPowerDataType", "SPStorageDataType", "SPNVMeDataType" },
            60000);
        if (r.TimedOut || r.ExitCode != 0) errors.Add("system_profiler failed: hardware details are incomplete.");
        else
        {
            try { FillFromProfiler(hw, r.Stdout); }
            catch (JsonException e) { errors.Add("system_profiler output unreadable: " + e.Message); }
        }

        var sip = Proc.Run("/usr/bin/csrutil", new[] { "status" }, 5000).Stdout;
        if (sip.Contains("enabled", StringComparison.OrdinalIgnoreCase)) hw.SipEnabled = !sip.Contains("disabled", StringComparison.OrdinalIgnoreCase);
        else if (sip.Contains("disabled", StringComparison.OrdinalIgnoreCase)) hw.SipEnabled = false;

        var gk = Proc.Run("/usr/sbin/spctl", new[] { "--status" }, 5000).Stdout;
        if (gk.Contains("assessments enabled", StringComparison.OrdinalIgnoreCase)) hw.GatekeeperEnabled = true;
        else if (gk.Contains("assessments disabled", StringComparison.OrdinalIgnoreCase)) hw.GatekeeperEnabled = false;
        return hw;
    }

    public static void FillFromProfiler(HardwareInfo hw, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        foreach (var item in Items(root, "SPHardwareDataType"))
        {
            hw.MachineName = S(item, "machine_name");
            hw.ModelId = S(item, "machine_model");
            hw.Chip = S(item, "chip_type");
            hw.Memory = S(item, "physical_memory");
        }
        foreach (var item in Items(root, "SPSoftwareDataType"))
        {
            hw.OsVersion = S(item, "os_version");
            hw.Kernel = S(item, "kernel_version");
            var m = OsMajorRx().Match(hw.OsVersion);
            if (m.Success) hw.OsMajor = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        foreach (var item in Items(root, "SPPowerDataType"))
        {
            if (!item.TryGetProperty("sppower_battery_health_info", out var health)) continue;
            hw.HasBattery = true;
            if (health.TryGetProperty("sppower_battery_cycle_count", out var cc) && cc.ValueKind == JsonValueKind.Number) hw.BatteryCycles = cc.GetInt32();
            hw.BatteryCondition = S(health, "sppower_battery_health");
            var cap = DigitsRx().Match(S(health, "sppower_battery_health_maximum_capacity"));
            if (cap.Success) hw.BatteryMaxCapacityPct = int.Parse(cap.Value, CultureInfo.InvariantCulture);
        }
        foreach (var ctrl in Items(root, "SPNVMeDataType"))
            if (ctrl.TryGetProperty("_items", out var drives) && drives.ValueKind == JsonValueKind.Array)
                foreach (var d in drives.EnumerateArray())
                    hw.Drives.Add(new DriveInfo(S(d, "_name"), S(d, "detachable_drive") != "yes", S(d, "smart_status")));
        foreach (var vol in Items(root, "SPStorageDataType"))
        {
            var mount = S(vol, "mount_point");
            if (mount is "/System/Volumes/Data" || (mount == "/" && hw.BootVolumeSize is null))
            {
                if (vol.TryGetProperty("free_space_in_bytes", out var f) && f.ValueKind == JsonValueKind.Number) hw.BootVolumeFree = f.GetInt64();
                if (vol.TryGetProperty("size_in_bytes", out var s) && s.ValueKind == JsonValueKind.Number) hw.BootVolumeSize = s.GetInt64();
            }
            // External drives reporting a SMART status (internal ones come from SPNVMeDataType).
            if (vol.TryGetProperty("physical_drive", out var pd) && S(pd, "is_internal_disk") == "no" && S(pd, "smart_status") is { Length: > 0 } st
                && hw.Drives.All(x => x.Name != S(pd, "device_name")))
                hw.Drives.Add(new DriveInfo(S(pd, "device_name"), false, st));
        }
    }

    static IEnumerable<JsonElement> Items(JsonElement root, string type) =>
        root.TryGetProperty(type, out var arr) && arr.ValueKind == JsonValueKind.Array ? arr.EnumerateArray() : Enumerable.Empty<JsonElement>();

    static string S(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    [GeneratedRegex(@"[a-z0-9]+(?:_[a-z0-9]+)*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRx();

    [GeneratedRegex(@"^\{\s*sec\s*=\s*(\d+)")]
    private static partial Regex BootTimeRx();

    [GeneratedRegex(@"^\s*\d+\s+\d+\s+\S+\s+\S+\s+\S+\s+([A-Za-z0-9_.\-]+)\s+\(([^)]*)\)")]
    private static partial Regex KmutilLineRx();

    [GeneratedRegex(@"^\s*[*\s]*\t[*\s]*\t\s*([A-Z0-9]{10}|-)\s*\t\s*([A-Za-z0-9_.\-]+)\s+\(([^)]*)\)\t[^\t]*\t\s*\[([^\]]*)\]")]
    private static partial Regex SysExtLineRx();

    [GeneratedRegex(@"<key>CFBundleIdentifier</key>\s*<string>([^<]+)</string>")]
    private static partial Regex PlistBundleIdRx();

    [GeneratedRegex(@"macOS\s+(\d+)")]
    private static partial Regex OsMajorRx();

    [GeneratedRegex(@"\d+")]
    private static partial Regex DigitsRx();
}
