namespace DumpFinder.Mac.Core;

/// <summary>Windows HealthLevel: badge "Healthy" / "Check" / "Critical" / "Info".</summary>
public enum HealthLevel { Good, Info, Warning, Critical }

/// <summary>
/// One card of Hardware › Hardware or Hardware › Conflicts (Windows IHealthCardData / BuildHealthInfoCard):
/// tag chip, title, badge, value line (mono), detail, then an optional guide drawer and quick action.
/// Action: "nav:drivers", "nav:crashes", an https link, an app bundle path or an x-apple.systempreferences: pane.
/// </summary>
public sealed record HealthCard(string Id, int Tab, HealthLevel Level, string Tag, string Title, string Value, string Detail,
                                string GuideTitle = "", string GuideSteps = "", string ActionLabel = "", string ActionTarget = "");

/// <summary>A third-party extension in Hardware › Extensions (Windows Drivers rows).</summary>
public sealed record ExtensionItem(string BundleId, string Version, bool Kernel, bool Loaded, string Detail, int Crashes);

/// <summary>
/// Builds the Hardware cards from a scan. Levels come from the findings of <see cref="DiagnosticsEngine.Analyze"/>,
/// so a threshold lives in one place; the cards add what is fine (Windows shows a "Healthy" card per component).
/// </summary>
public static class HardwareChecks
{
    public const int TabHardware = 0, TabConflicts = 1;

    public const string ActivityMonitor = "/System/Applications/Utilities/Activity Monitor.app";
    public const string DiskUtility = "/System/Applications/Utilities/Disk Utility.app";
    public const string SoftwareUpdatePane = "x-apple.systempreferences:com.apple.Software-Update-Settings.extension";
    public const string BatteryPane = "x-apple.systempreferences:com.apple.Battery-Settings.extension";
    public const string DisplaysPane = "x-apple.systempreferences:com.apple.Displays-Settings.extension";
    public const string LoginItemsPane = "x-apple.systempreferences:com.apple.LoginItems-Settings.extension";
    public const string PrivacyPane = "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension";

    public static List<HealthCard> Build(ScanResult r, DetectionData data, DateTimeOffset now)
    {
        var cards = new List<HealthCard>();
        var hw = r.Hardware;
        var th = data.Thresholds;
        Finding? F(string id) => r.Findings.Where(f => f.Id == id).OrderByDescending(f => f.Severity).FirstOrDefault();
        string whatToDo = Loc.S("mac.hw.what_to_do");

        // ---------- Hardware ----------
        // macOS & firmware (Windows: BIOS card)
        var os = F("macos_unsupported");
        cards.Add(new HealthCard("os", TabHardware, os is null ? HealthLevel.Good : HealthLevel.Warning, "macOS", Loc.S("mac.hw.os_title"),
            Join(hw.OsVersion, hw.Kernel), os?.Detail ?? Loc.S("mac.hw.os_ok"),
            os is null ? "" : whatToDo, os?.Advice ?? "", Loc.S("mac.hw.open_software_update"), SoftwareUpdatePane));

        // Chip: processor and graphics
        cards.Add(new HealthCard("chip", TabHardware, HealthLevel.Good, "SoC • CPU / GPU", Loc.S("mac.hw.chip_title"),
            Join(hw.Chip,
                 hw.PerformanceCores > 0 ? Loc.F("mac.hw.cores", hw.PerformanceCores, hw.EfficiencyCores) : "",
                 hw.GpuCores > 0 ? Loc.F("mac.hw.gpu_cores", hw.GpuCores) : "",
                 hw.Metal),
            Loc.S("mac.hw.chip_detail")));

        // Unified memory (Windows: RAM card)
        var jetsam = F("jetsam");
        bool lowMem = hw.MemoryFreePct is int freePct && freePct < 10;
        var memLevel = lowMem || jetsam?.Severity == Severity.Warning ? HealthLevel.Warning : jetsam is not null ? HealthLevel.Info : HealthLevel.Good;
        string memDetail = lowMem ? Loc.F("mac.hw.memory_warn", hw.MemoryFreePct ?? 0, hw.SwapUsedBytes / 1073741824.0) : Loc.S("mac.hw.memory_ok");
        if (jetsam is not null) memDetail += "\n" + jetsam.Detail;
        cards.Add(new HealthCard("memory", TabHardware, memLevel, "RAM", Loc.S("mac.hw.memory_title"),
            Join(hw.Memory.Length > 0 ? hw.Memory : hw.MemoryBytes > 0 ? $"{hw.MemoryBytes / 1073741824} GB" : "",
                 hw.MemoryType, hw.MemoryVendor,
                 Loc.F("mac.hw.swap", hw.SwapUsedBytes / 1073741824.0),
                 hw.MemoryFreePct is int p ? Loc.F("mac.hw.free_pct", p) : ""),
            memDetail, Loc.S("mac.hw.memory_guide_title"), Loc.S("mac.hw.memory_guide_steps"),
            Loc.S("mac.hw.open_activity_monitor"), ActivityMonitor));

        // One card per drive (Windows: one card per physical disk)
        var lowDisk = F("low_disk");
        string space = hw.BootVolumeFree is long free && hw.BootVolumeSize is long size && size > 0
            ? Loc.F("mac.hw.disk_space", free / 1e9, size / 1e9, 100.0 * free / size) : "";
        bool spaceShown = false;
        foreach (var d in hw.Drives)
        {
            var smart = r.Findings.FirstOrDefault(f => f.Id == "smart" && f.Title.Contains(d.Name, StringComparison.Ordinal));
            bool boot = d.Internal && !spaceShown;
            var disk = boot ? lowDisk : null;
            spaceShown |= boot;
            var level = smart is not null ? HealthLevel.Critical : disk?.Severity switch
            {
                Severity.Critical => HealthLevel.Critical, Severity.Warning => HealthLevel.Warning, _ => HealthLevel.Good,
            };
            var bad = smart ?? disk;
            cards.Add(new HealthCard("drive", TabHardware, level, d.Internal ? "SSD" : "SSD • USB / Thunderbolt", d.Name,
                Join("SMART: " + (d.SmartStatus.Length > 0 ? d.SmartStatus : "?"), boot ? space : ""),
                bad?.Detail ?? Loc.S("mac.hw.ssd_ok"), bad is null ? "" : whatToDo, bad?.Advice ?? "",
                Loc.S("mac.hw.open_disk_utility"), DiskUtility));
        }
        if (!spaceShown && space.Length > 0)
            cards.Add(new HealthCard("drive", TabHardware, lowDisk?.Severity == Severity.Critical ? HealthLevel.Critical : lowDisk is null ? HealthLevel.Good : HealthLevel.Warning,
                "SSD", Loc.S("mac.hw.startup_disk"), space, lowDisk?.Detail ?? Loc.S("mac.hw.ssd_ok"),
                lowDisk is null ? "" : whatToDo, lowDisk?.Advice ?? "", Loc.S("mac.hw.open_disk_utility"), DiskUtility));

        // Battery
        if (hw.HasBattery)
        {
            var bat = F("battery_worn") ?? F("battery_cycles");
            cards.Add(new HealthCard("battery", TabHardware,
                bat is null ? HealthLevel.Good : bat.Severity == Severity.Warning ? HealthLevel.Warning : HealthLevel.Info,
                Loc.S("mac.hw.tag_battery"), Loc.S("mac.hw.battery_title"),
                Join(hw.BatteryMaxCapacityPct is int cap ? Loc.F("mac.hw.battery_capacity", cap) : "",
                     hw.BatteryCycles is int cy ? Loc.P("mac.hw.battery_cycles", cy) : "", hw.BatteryCondition),
                bat?.Detail ?? Loc.S("mac.hw.battery_ok"), bat is null ? "" : whatToDo, bat?.Advice ?? "",
                Loc.S("mac.hw.open_battery"), BatteryPane));
        }

        // Temperature and throttling
        cards.Add(new HealthCard("thermal", TabHardware, hw.ThermalWarning ? HealthLevel.Warning : HealthLevel.Good,
            Loc.S("mac.hw.tag_thermal"), Loc.S("mac.hw.thermal_title"),
            hw.ThermalWarning ? hw.ThermalText : Loc.S("mac.hw.thermal_ok_value"),
            hw.ThermalWarning ? Loc.S("mac.hw.thermal_warn") : Loc.S("mac.hw.thermal_ok"),
            hw.ThermalWarning ? whatToDo : "", hw.ThermalWarning ? Loc.S("mac.hw.thermal_advice") : "",
            Loc.S("mac.hw.open_activity_monitor"), ActivityMonitor));

        // Screens
        if (hw.Displays.Count > 0)
            cards.Add(new HealthCard("displays", TabHardware, HealthLevel.Info, Loc.S("mac.hw.tag_display"), Loc.P("mac.hw.displays_title", hw.Displays.Count),
                string.Join("  •  ", hw.Displays.Select(d => d.Resolution.Length > 0 ? $"{d.Name} ({d.Resolution})" : d.Name)),
                Loc.S("mac.hw.displays_detail"), "", "", Loc.S("mac.hw.open_displays"), DisplaysPane));

        // USB and Thunderbolt devices
        cards.Add(new HealthCard("devices", TabHardware, HealthLevel.Info, "USB / THUNDERBOLT", Loc.P("mac.hw.devices_title", hw.Peripherals.Count),
            hw.Peripherals.Count == 0 ? Loc.S("mac.hw.no_device")
                : string.Join("  •  ", hw.Peripherals.Take(8).Select(p => p.Name)) + (hw.Peripherals.Count > 8 ? "  …" : ""),
            Loc.S("mac.hw.devices_detail")));

        // Readings that failed: said, never hidden
        if (r.Errors.Count > 0)
            cards.Add(new HealthCard("errors", TabHardware, HealthLevel.Info, "SCAN", Loc.S("mac.hardware.not_read"),
                Loc.P("mac.hw.errors_value", r.Errors.Count), string.Join("\n", r.Errors)));

        // ---------- Conflicts ----------
        var sip = F("sip_off");
        cards.Add(new HealthCard("sip", TabConflicts, sip is null ? (hw.SipEnabled is null ? HealthLevel.Info : HealthLevel.Good) : HealthLevel.Warning,
            Loc.S("mac.cf.tag_security"), Loc.S("mac.cf.sip_title"), OnOff(hw.SipEnabled),
            sip?.Detail ?? Loc.S("mac.cf.sip_ok"), sip is null ? "" : whatToDo, sip?.Advice ?? ""));

        var gk = F("gatekeeper_off");
        cards.Add(new HealthCard("gatekeeper", TabConflicts, gk is null ? (hw.GatekeeperEnabled is null ? HealthLevel.Info : HealthLevel.Good) : HealthLevel.Warning,
            Loc.S("mac.cf.tag_security"), "Gatekeeper", OnOff(hw.GatekeeperEnabled),
            gk?.Detail ?? Loc.S("mac.cf.gk_ok"), gk is null ? "" : whatToDo, gk?.Advice ?? "",
            Loc.S("mac.cf.open_privacy"), PrivacyPane));

        int loaded = r.KernelExtensions.Count(k => k.Loaded), onDisk = r.KernelExtensions.Count - loaded;
        var kextLevel = r.Findings.Any(f => f.Id == "kext_loaded" && f.Severity == Severity.Critical) ? HealthLevel.Critical
            : loaded > 0 ? HealthLevel.Warning : onDisk > 0 ? HealthLevel.Info : HealthLevel.Good;
        cards.Add(new HealthCard("kexts", TabConflicts, kextLevel, "KEXT", Loc.S("mac.cf.kext_title"),
            Loc.F("mac.cf.kext_value", loaded, onDisk),
            r.KernelExtensions.Count == 0 ? Loc.S("mac.cf.kext_none") : Loc.S("mac.cf.kext_some"),
            r.KernelExtensions.Count == 0 ? "" : whatToDo, r.KernelExtensions.Count == 0 ? "" : Loc.S("mac.check.kext_loaded_advice"),
            r.KernelExtensions.Count == 0 ? "" : Loc.S("mac.cf.see_extensions"), r.KernelExtensions.Count == 0 ? "" : "nav:drivers"));

        cards.Add(new HealthCard("sysext", TabConflicts, r.SystemExtensions.Count > 0 ? HealthLevel.Info : HealthLevel.Good,
            "SYSTEM EXTENSIONS", Loc.S("mac.cf.sysext_title"), Loc.P("mac.cf.sysext_value", r.SystemExtensions.Count),
            Loc.S("mac.cf.sysext_detail"), "", "", Loc.S("mac.cf.open_login_items"), LoginItemsPane));

        var apps = r.Findings.Where(f => f.Id == "app_crashes").ToList();
        var thirdParty = apps.Where(f => f.Severity >= Severity.Warning).ToList();
        cards.Add(new HealthCard("apps", TabConflicts, thirdParty.Count > 0 ? HealthLevel.Warning : apps.Count > 0 ? HealthLevel.Info : HealthLevel.Good,
            "APPS", Loc.S("mac.cf.apps_title"),
            apps.Count == 0 ? Loc.S("mac.cf.apps_none_value") : string.Join("  •  ", apps.Take(6).Select(f => f.Title)),
            apps.Count == 0 ? Loc.F("mac.cf.apps_ok", th.RepeatedCrashes, th.RecentDays) : Loc.S("mac.cf.apps_some"),
            "", "", apps.Count == 0 ? "" : Loc.S("mac.cf.see_crashes"), apps.Count == 0 ? "" : "nav:crashes"));

        var up = F("uptime");
        if (r.Boot.BootTime is DateTimeOffset bt)
            cards.Add(new HealthCard("uptime", TabConflicts, up is null ? HealthLevel.Good : HealthLevel.Info, "UPTIME", Loc.S("mac.cf.uptime_title"),
                Loc.P("mac.cf.uptime_value", Math.Max(0, (int)(now - bt).TotalDays)),
                up?.Detail ?? Loc.S("mac.cf.uptime_ok"), up is null ? "" : whatToDo, up?.Advice ?? ""));

        return cards;
    }

    /// <summary>Third-party kexts (loaded or left on disk) and system extensions, worst first (Windows RenderDrivers sort).</summary>
    public static List<ExtensionItem> Extensions(ScanResult r)
    {
        int Crashes(string id) => r.Panics.Count(p => p.BacktraceExtensions.Contains(id));
        var list = new List<ExtensionItem>();
        foreach (var k in r.KernelExtensions)
            list.Add(new ExtensionItem(k.BundleId, k.Version, true, k.Loaded, k.Loaded ? "" : k.Path, Crashes(k.BundleId)));
        foreach (var s in r.SystemExtensions)
            list.Add(new ExtensionItem(s.BundleId, s.Version, false, true, Join(s.TeamId == "-" ? "" : s.TeamId, s.State), Crashes(s.BundleId)));
        return list.OrderByDescending(e => e.Crashes).ThenBy(e => e.Loaded).ThenByDescending(e => e.Kernel).ThenBy(e => e.BundleId, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static string OnOff(bool? b) => Loc.S(b is null ? "mac.cf.unknown" : b.Value ? "mac.cf.enabled" : "mac.cf.disabled");

    static string Join(params string[] parts) => string.Join("  •  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
}
