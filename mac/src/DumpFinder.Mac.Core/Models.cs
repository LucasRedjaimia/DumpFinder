namespace DumpFinder.Mac.Core;

public enum Severity { Ok, Info, Warning, Critical }

/// <summary>Id: stable name of the rule ("panic", "low_disk"…). Category: crashes | memory | drivers | hardware | system | summary.</summary>
public sealed record Finding(string Id, Severity Severity, string Category, string Title, string Detail, string Advice);

/// <summary>A kernel panic report (.panic, or .ips with bug_type 210).</summary>
public sealed class PanicReport
{
    public required string File { get; init; }
    public DateTimeOffset? Time { get; init; }
    public string OsVersion { get; init; } = "";
    /// <summary>First line of the panic string, e.g. "panic(cpu 4 caller 0x…): userspace watchdog timeout…".</summary>
    public string Message { get; init; } = "";
    public string PanickedProcess { get; init; } = "";
    /// <summary>Bundle ids listed under "Kernel Extensions in backtrace".</summary>
    public List<string> BacktraceExtensions { get; init; } = new();
    public string LastStartedExtension { get; init; } = "";
    /// <summary>Id of the matched panic pattern in mac-detection.json, or "".</summary>
    public string PatternId { get; set; } = "";
    public string Explanation { get; set; } = "";
    public string Advice { get; set; } = "";
    /// <summary>The whole panic string (capped), shown under "Full report".</summary>
    public string PanicText { get; init; } = "";
    public long FileSize { get; init; }
}

public enum ReportKind { Crash, MemoryPressure, Panic, Other }

/// <summary>A user-space diagnostic report (.ips).</summary>
public sealed class CrashReport
{
    public required string File { get; init; }
    public ReportKind Kind { get; init; }
    public string BugType { get; init; } = "";
    public string Process { get; init; } = "";
    public DateTimeOffset? Time { get; init; }
    public bool FirstParty { get; init; }
    public string ExceptionType { get; init; } = "";
    public string Signal { get; init; } = "";
    public string Termination { get; init; } = "";
    public string AppVersion { get; init; } = "";
    public string BundleId { get; init; } = "";
    public string ProcPath { get; init; } = "";
    public string ParentProcess { get; init; } = "";
    public string OsVersion { get; init; } = "";
    public long FileSize { get; init; }
}

public sealed record KernelExtension(string BundleId, string Version, bool Loaded, string Path);

public sealed record SystemExtension(string BundleId, string Version, string TeamId, string State);

public sealed class BootInfo
{
    public DateTimeOffset? BootTime { get; init; }
    public string BootReason { get; init; } = "";
    public string ShutdownReason { get; init; } = "";
    /// <summary>Human readings of known tokens in ShutdownReason / BootReason.</summary>
    public List<string> Interpretation { get; init; } = new();
    public bool Abnormal { get; init; }
}

public sealed class HardwareInfo
{
    public string MachineName { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string Chip { get; set; } = "";
    public string Memory { get; set; } = "";
    public string OsVersion { get; set; } = "";
    public int OsMajor { get; set; }
    public string Kernel { get; set; } = "";
    public bool? SipEnabled { get; set; }
    public bool? GatekeeperEnabled { get; set; }

    public bool HasBattery { get; set; }
    public int? BatteryCycles { get; set; }
    public string BatteryCondition { get; set; } = "";
    public int? BatteryMaxCapacityPct { get; set; }

    public List<DriveInfo> Drives { get; } = new();
    public long? BootVolumeFree { get; set; }
    public long? BootVolumeSize { get; set; }

    // Processor, graphics, memory (Windows Hardware tab: CPU / GPU / RAM rows)
    public int PerformanceCores { get; set; }
    public int EfficiencyCores { get; set; }
    public string GpuName { get; set; } = "";
    public int GpuCores { get; set; }
    /// <summary>"Metal 4", "Metal 3"… from spdisplays_mtlgpufamilysupport.</summary>
    public string Metal { get; set; } = "";
    public string MemoryType { get; set; } = "";
    public string MemoryVendor { get; set; } = "";
    public long MemoryBytes { get; set; }
    public long SwapUsedBytes { get; set; }
    /// <summary>System-wide memory free percentage (memory_pressure), null when unknown.</summary>
    public int? MemoryFreePct { get; set; }
    /// <summary>pmset -g therm: a thermal or performance warning was recorded since boot.</summary>
    public bool ThermalWarning { get; set; }
    public string ThermalText { get; set; } = "";

    /// <summary>Screens and connected devices. Serial numbers are never read into the model.</summary>
    public List<DisplayInfo> Displays { get; } = new();
    public List<PeripheralInfo> Peripherals { get; } = new();
}

public sealed record DriveInfo(string Name, bool Internal, string SmartStatus);

/// <summary>A screen: name, resolution "1920 x 1080 @ 60.00Hz", built-in or not, main screen.</summary>
public sealed record DisplayInfo(string Name, string Resolution, bool BuiltIn, bool Main);

/// <summary>A USB or Thunderbolt device. Bus is "USB" or "Thunderbolt".</summary>
public sealed record PeripheralInfo(string Name, string Vendor, string Bus);

public sealed class ScanResult
{
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.Now;
    public HardwareInfo Hardware { get; init; } = new();
    public BootInfo Boot { get; init; } = new();
    public List<PanicReport> Panics { get; init; } = new();
    public List<CrashReport> Reports { get; init; } = new();
    public List<KernelExtension> KernelExtensions { get; init; } = new();
    public List<SystemExtension> SystemExtensions { get; init; } = new();
    public List<Finding> Findings { get; init; } = new();
    /// <summary>Steps that failed or timed out; the scan still returns what it could read.</summary>
    public List<string> Errors { get; init; } = new();
}
