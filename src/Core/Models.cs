using System;
using System.Collections.Generic;

namespace WinCrashDoctor
{
    public enum AppLang
    {
        FR = 0,
        EN = 1,
        ES = 2,
        DE = 3,
        IT = 4,
        PT = 5
    }

    public enum AppThemeMode
    {
        SystemAuto = 0,
        Dark = 1,
        Light = 2
    }

    public enum CrashCategory
    {
        BSOD,
        GpuDriver,
        StorageNvme,
        HardwareWhea,
        MemoryExhaustion,
        BrowserGpu,
        AppCrash,
        UnexpectedReboot
    }

    public enum FixGroup
    {
        All,
        Gpu,
        BrowserApps,
        StoragePcie,
        SystemMaint
    }

    public enum FixStatus
    {
        Applied,
        Recommended,
        Optional,
        Info
    }

    public enum HealthLevel
    {
        Good,
        Warning,
        Critical,
        Info
    }

    public enum AiProviderType
    {
        Gemini = 0,
        Claude = 1,
        OllamaLocal = 2
    }

    public class AppSettings
    {
        public int Language { get; set; }
        public int Theme { get; set; }
        public bool TraySentinelEnabled { get; set; }
        public int AiProvider { get; set; }
        /// <summary>Plain key, in memory only (never serialized). Persisted encrypted in AiApiKeyProtected.</summary>
        [System.Web.Script.Serialization.ScriptIgnore]
        public string AiApiKey { get; set; }
        /// <summary>AI API key encrypted with DPAPI (current Windows user).</summary>
        public string AiApiKeyProtected { get; set; }
        /// <summary>Claude model id chosen in Settings (default claude-opus-5-5).</summary>
        public string ClaudeModel { get; set; }
        public string OllamaModel { get; set; }
        public bool HasCompletedTour { get; set; }
        public bool SidebarCollapsed { get; set; }
        public string SeenTips { get; set; }
        /// <summary>"board|BIOS version" the user confirmed is the latest available: no more old-BIOS warning for it.</summary>
        public string BiosLatestAck { get; set; }
        /// <summary>Opt-out of the launch-time GitHub release check (default: check).</summary>
        public bool UpdateCheckDisabled { get; set; }
        /// <summary>Release version the user chose to skip ("6.1.0"); a newer one is announced again.</summary>
        public string SkippedUpdateVersion { get; set; }
    }

    public class CrashEventItem
    {
        public DateTime Timestamp { get; set; }
        public CrashCategory Category { get; set; }
        public string Source { get; set; }
        public string CodeOrId { get; set; }
        public string SummaryFr { get; set; }
        public string SummaryEn { get; set; }
        public string DetailsFr { get; set; }
        public string DetailsEn { get; set; }

        public string DisplayTime { get { return Timestamp.ToString("dd/MM/yyyy HH:mm"); } }
    }

    public class RegistryFixItem
    {
        public string Id { get; set; }
        public FixGroup Group { get; set; }
        public string VendorTag { get; set; }
        public string HardwareTarget { get; set; } // "ALL", "AMD_GPU", "NVIDIA_GPU", "INTEL_ARC", "AMD_CPU", "INTEL_CPU"
        public string Title { get; set; }
        public string RegPathDisplay { get; set; }
        public string CurrentValue { get; set; }
        public string Explanation { get; set; }
        public string ApplyBtn { get; set; }
        public string RestoreBtn { get; set; }
        public FixStatus Status { get; set; }
        public bool RequiresAdmin { get; set; }
        public bool CanRestore { get; set; }
        public string RegExportContent { get; set; }

        public bool IsApplicableTo(HardwareSummary hw)
        {
            if (hw == null) return true;
            if (!HardwareTargets.Matches(HardwareTarget, hw)) return false;

            // Heuristic check if HardwareTarget wasn't explicitly populated
            if (Id != null && Id.StartsWith("amd_") && !hw.HasAmdGpu) return false;
            if (VendorTag != null && VendorTag.IndexOf("AMD RADEON", StringComparison.OrdinalIgnoreCase) >= 0 && !hw.HasAmdGpu) return false;
            if (VendorTag != null && VendorTag.IndexOf("INTEL ARC", StringComparison.OrdinalIgnoreCase) >= 0 && !hw.HasIntelArcGpu) return false;
            return true;
        }
    }

    public interface IHealthCardData
    {
        string Tag { get; }
        string Title { get; }
        string ValueSummary { get; }
        string Detail { get; }
        HealthLevel Level { get; }

        string InAppFixOp { get; set; }
        string InAppFixLabel { get; set; }
        bool InAppFixApplied { get; set; }
        string GuideTitle { get; set; }
        string GuideSteps { get; set; }
        string QuickActionLabel { get; set; }
        string QuickActionCommand { get; set; }
        string WebSearchQuery { get; set; }
    }

    public class HardwareCheckItem : IHealthCardData
    {
        public string CategoryTag { get; set; }
        public string Title { get; set; }
        public string ValueSummary { get; set; }
        public string Detail { get; set; }
        public HealthLevel Level { get; set; }

        public string InAppFixOp { get; set; }
        public string InAppFixLabel { get; set; }
        public bool InAppFixApplied { get; set; }
        public string GuideTitle { get; set; }
        public string GuideSteps { get; set; }
        public string QuickActionLabel { get; set; }
        public string QuickActionCommand { get; set; }
        public string WebSearchQuery { get; set; }

        string IHealthCardData.Tag { get { return CategoryTag; } }
    }

    public class ConflictCheckItem : IHealthCardData
    {
        public string Tag { get; set; }
        public string Title { get; set; }
        public string StatusText { get; set; }
        public string Explanation { get; set; }
        public HealthLevel Level { get; set; }

        public string InAppFixOp { get; set; }
        public string InAppFixLabel { get; set; }
        public bool InAppFixApplied { get; set; }
        public string GuideTitle { get; set; }
        public string GuideSteps { get; set; }
        public string QuickActionLabel { get; set; }
        public string QuickActionCommand { get; set; }
        public string WebSearchQuery { get; set; }

        string IHealthCardData.ValueSummary { get { return StatusText; } }
        string IHealthCardData.Detail { get { return Explanation; } }
    }

    public class MinidumpReportItem
    {
        public string FileName { get; set; }
        public string FullPath { get; set; }
        public DateTime CrashTime { get; set; }
        public string DumpType { get; set; } // "Kernel BSOD (PAGEDU64)" or "App / GPU (MDMP)"
        public string BugCheckOrException { get; set; }
        public string PrimaryCulpritModule { get; set; }
        public string ProcessName { get; set; }
        public string RelatedModules { get; set; }
        public string HumanExplanationFr { get; set; }
        public string HumanExplanationEn { get; set; }
        public long FileSizeBytes { get; set; }

        // ---- v6.1: structured kernel dump data (triage dumps)
        /// <summary>Raw stop code (0 when unknown / user-mode dump).</summary>
        public uint BugCheckCode { get; set; }
        public string BugCheckName { get; set; }
        /// <summary>The four stop parameters, hex-formatted.</summary>
        public List<string> BugCheckParameters { get; set; }
        public int OsBuild { get; set; }
        /// <summary>How the culprit was identified (parameter / stack / fallback), FR + EN.</summary>
        public string CulpritEvidenceFr { get; set; }
        public string CulpritEvidenceEn { get; set; }
        /// <summary>Driver modules found on the raw kernel stack, top first.</summary>
        public List<string> StackModules { get; set; }
        public int LoadedDriverCount { get; set; }
        /// <summary>True when the structured parser succeeded (vs. the string-scan fallback).</summary>
        public bool StructuredParse { get; set; }
        /// <summary>Readable copy of a protected kernel dump (C:\Windows\Minidump), made by the admin scan.</summary>
        public string LocalCopyPath { get; set; }
    }

    public class DiskSmartInfo
    {
        public string DeviceId { get; set; }
        public int TemperatureC { get; set; }
        public int WearPct { get; set; }
        public long UncorrectedErrors { get; set; }
        public long PowerOnHours { get; set; }
    }

    public class AdminDeepScanPayload
    {
        public List<MinidumpReportItem> KernelDumps { get; set; }
        public List<DiskSmartInfo> SmartCounters { get; set; }
    }

    public class KnowledgeEntry
    {
        public string Tag { get; set; }
        public string BadgeColorHex { get; set; }
        public string HardwareTarget { get; set; } // "ALL", "AMD_GPU", "NVIDIA_GPU", "INTEL_ARC", "AMD_CPU", "INTEL_CPU"
        public string Title { get; set; }
        public string Symptoms { get; set; }
        public string Cause { get; set; }
        public string Solution { get; set; }

        public bool IsApplicableTo(HardwareSummary hw)
        {
            return HardwareTargets.Matches(HardwareTarget, hw);
        }
    }

    /// <summary>Shared vendor filter for fixes and knowledge entries.</summary>
    public static class HardwareTargets
    {
        public static bool Matches(string target, HardwareSummary hw)
        {
            if (hw == null) return true;
            switch (target)
            {
                case "AMD_GPU": return hw.HasAmdGpu;
                case "NVIDIA_GPU": return hw.HasNvidiaGpu;
                case "INTEL_ARC": return hw.HasIntelArcGpu;
                case "AMD_CPU": return hw.HasAmdCpu;
                case "INTEL_CPU": return hw.HasIntelCpu;
                default: return true;
            }
        }
    }

    public class HardwareSummary
    {
        public string CpuName { get; set; }
        public string GpuName { get; set; }
        public string GpuDriver { get; set; }
        public bool HasAmdCpu { get; set; }
        public bool HasIntelCpu { get; set; }
        public bool HasAmdGpu { get; set; }
        public bool IsAmdRx7000 { get; set; }
        public bool HasNvidiaGpu { get; set; }
        public bool HasIntelArcGpu { get; set; }
        public bool IsIntel13Or14Gen { get; set; }
        public string BoardName { get; set; }
        public string OsVersion { get; set; }
        public DateTime LastBoot { get; set; }
        public bool IsAdmin { get; set; }

        public string GetCpuVendorDisplay()
        {
            if (HasAmdCpu) return "AMD";
            if (HasIntelCpu) return "Intel";
            return "CPU";
        }

        public string GetGpuVendorDisplay()
        {
            if (HasNvidiaGpu) return "NVIDIA";
            if (HasAmdGpu) return "AMD";
            if (HasIntelArcGpu) return "Intel Arc";
            return "GPU";
        }

        public string GetProfileBadge()
        {
            return string.Format("{0} + {1}", GetCpuVendorDisplay(), GetGpuVendorDisplay());
        }
    }

    public class ScanResult
    {
        public HardwareSummary Hardware { get; set; }
        public DateTime? FilterStartTime { get; set; }
        public List<CrashEventItem> Events { get; set; }
        public List<RegistryFixItem> Fixes { get; set; }
        public List<HardwareCheckItem> HardwareChecks { get; set; }
        public List<ConflictCheckItem> ConflictChecks { get; set; }
        public List<MinidumpReportItem> Minidumps { get; set; }
        public bool MinidumpFolderNeedsAdmin { get; set; }
        public int BsodAndRebootCount { get; set; }
        public int GpuTdrCount { get; set; }
        public int StorageNvmeCount { get; set; }
        public int WheaAndMemCount { get; set; }
        public int BrowserAndAppCount { get; set; }
        public string DiagnosisTitle { get; set; }
        public string DiagnosisBody { get; set; }
        public bool IsHealthy { get; set; }
        /// <summary>Windows Reliability Monitor index per day (may be empty).</summary>
        public List<ReliabilityPoint> Reliability { get; set; }
        /// <summary>Third-party driver inventory (may be empty).</summary>
        public List<DriverInfo> Drivers { get; set; }
    }

}
