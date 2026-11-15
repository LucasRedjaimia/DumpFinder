using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public static class SystemHealthScanner
    {
        /// <summary>Health-card op (handled in the UI, no admin, no registry): "this BIOS is already the latest".</summary>
        public const string BiosAckOp = "bios_ack:";

        /// <summary>Self-test only: pretend the BIOS has this release date.</summary>
        internal static DateTime? TestBiosDate;

        public static string BiosAckKey(string board, string biosVersion) { return (board ?? "") + "|" + (biosVersion ?? ""); }

        public static List<HardwareCheckItem> ScanHardwareHealth(HardwareSummary hw, List<DiskSmartInfo> adminSmart)
        {
            List<HardwareCheckItem> list = new List<HardwareCheckItem>();

            // 1. BIOS Version & Date
            try
            {
                string biosVer = "-";
                DateTime biosDate = DateTime.MinValue;
                using (var s = Wmi.Searcher("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        biosVer = Convert.ToString(mo["SMBIOSBIOSVersion"]).Trim();
                        string rd = Convert.ToString(mo["ReleaseDate"]);
                        if (!string.IsNullOrEmpty(rd))
                            biosDate = ManagementDateTimeConverter.ToDateTime(rd);
                        break;
                    }
                }

                if (TestBiosDate.HasValue) biosDate = TestBiosDate.Value;
                bool oldBios = (biosDate != DateTime.MinValue && (DateTime.Now - biosDate).TotalDays > 540);
                string dateStr = biosDate != DateTime.MinValue ? biosDate.ToString("d", Localizer.Culture) : "-";
                // The app cannot know whether a newer BIOS exists: the user can confirm this one is the latest.
                string biosAckKey = BiosAckKey(hw.BoardName, biosVer);
                bool biosAcked = oldBios && Localizer.CachedSettings != null && Localizer.CachedSettings.BiosLatestAck == biosAckKey;
                bool biosWarn = oldBios && !biosAcked;

                string biosGuideTitle = Localizer.S("system_health_scanner.guide_how_to_update_motherboard_bios");

                string biosGuideSteps = Localizer.F("system_health_scanner.1_note_your_exact_motherboard_model", hw.BoardName, biosVer);

                list.Add(new HardwareCheckItem
                {
                    CategoryTag = hw.HasAmdCpu ? "BIOS / AGESA (AMD)" : (hw.HasIntelCpu ? "BIOS / Microcode (Intel)" : "BIOS Carte Mère"),
                    Title = Localizer.S("system_health_scanner.motherboard_bios_version_date"),
                    ValueSummary = string.Format("BIOS {0} ({1})", biosVer, dateStr),
                    Detail = biosAcked
                        ? Localizer.F("system_health_scanner.you_confirmed_this_is_the_latest", hw.BoardName)
                        : oldBios
                        ? (hw.HasAmdCpu
                            ? Localizer.S("system_health_scanner.this_bios_is_over_18_months")
                            : Localizer.S("system_health_scanner.this_bios_is_over_18_months_2"))
                        : Localizer.F("system_health_scanner.up_to_date_bios_firmware_on", hw.BoardName),
                    Level = biosWarn ? HealthLevel.Warning : HealthLevel.Good,
                    InAppFixOp = biosWarn ? BiosAckOp + biosAckKey : null,
                    InAppFixLabel = biosWarn ? Localizer.S("system_health_scanner.it_s_already_the_latest_version") : null,
                    GuideTitle = biosGuideTitle,
                    GuideSteps = biosGuideSteps,
                    QuickActionLabel = Localizer.S("system_health_scanner.find_official_bios"),
                    QuickActionCommand = "web:search",
                    WebSearchQuery = hw.BoardName + " motherboard BIOS update download official"
                });
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 2. RAM Capacity, Speed & EXPO / XMP Check
            try
            {
                long totalBytes = 0;
                int sticks = 0;
                int cfgSpeed = 0;
                string partNum = "";
                string vendor = "";

                using (var s = Wmi.Searcher("SELECT Manufacturer, PartNumber, Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        sticks++;
                        totalBytes += Convert.ToInt64(mo["Capacity"]);
                        int cs = Convert.ToInt32(mo["ConfiguredClockSpeed"] ?? mo["Speed"] ?? 0);
                        if (cs > cfgSpeed) cfgSpeed = cs;
                        partNum = Convert.ToString(mo["PartNumber"]).Trim();
                        vendor = Convert.ToString(mo["Manufacturer"]).Trim();
                    }
                }

                double gb = Math.Round(totalBytes / (1024.0 * 1024.0 * 1024.0));
                int ratedSpeed = cfgSpeed;
                Match mRated = Regex.Match(partNum, @"(3200|3600|4000|5200|5600|6000|6400|6800|7200|7600|8000)");
                if (mRated.Success) int.TryParse(mRated.Groups[1].Value, out ratedSpeed);

                bool xmpDisabled = (cfgSpeed == 4800 && ratedSpeed > 4800) || (cfgSpeed == 2133 && ratedSpeed > 2133) || (cfgSpeed == 2400 && ratedSpeed > 2400);

                string ramCat = hw.HasAmdCpu ? "RAM • EXPO / DOCP" : (hw.HasIntelCpu ? "RAM • XMP" : "RAM • XMP / EXPO");
                string ramTitle = hw.HasAmdCpu
                    ? Localizer.S("system_health_scanner.system_memory_ram_expo_docp_profile")
                    : (hw.HasIntelCpu
                        ? Localizer.S("system_health_scanner.system_memory_ram_xmp_profile_intel")
                        : Localizer.S("system_health_scanner.system_memory_ram_xmp_expo_profile"));

                string ramGuideTitle = hw.HasAmdCpu
                    ? Localizer.S("system_health_scanner.guide_enable_amd_expo_docp_in")
                    : Localizer.S("system_health_scanner.guide_enable_intel_xmp_in_bios");

                string ramGuideSteps = Localizer.F("system_health_scanner.ram_guide_steps",
                    Localizer.S(hw.HasAmdCpu ? "system_health_scanner.ram_guide_step3_amd" : "system_health_scanner.ram_guide_step3_intel"),
                    ratedSpeed);

                list.Add(new HardwareCheckItem
                {
                    CategoryTag = ramCat,
                    Title = ramTitle,
                    ValueSummary = string.Format("{0} GB ({1}x) @ {2} MT/s  •  {3} {4}", gb, sticks, cfgSpeed, vendor, partNum).Trim(),
                    Detail = xmpDisabled
                        ? (hw.HasAmdCpu
                            ? Localizer.F("system_health_scanner.your_memory_sticks_are_running_at", partNum, cfgSpeed, ratedSpeed)
                            : Localizer.F("system_health_scanner.your_memory_sticks_are_running_at_2", partNum, cfgSpeed, ratedSpeed))
                        : Localizer.F("system_health_scanner.memory_frequency_is_optimal_mt_s", cfgSpeed),
                    Level = xmpDisabled ? HealthLevel.Warning : HealthLevel.Good,
                    GuideTitle = ramGuideTitle,
                    GuideSteps = ramGuideSteps,
                    QuickActionLabel = Localizer.S("system_health_scanner.run_ram_stability_test"),
                    QuickActionCommand = "nav:stress_ram"
                });
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 3. Resizable BAR & GPU Memory Window
            try
            {
                long bar1Mib = DetectGpuBar1Mib();
                bool rebarActive = (bar1Mib >= 512);
                string activeTag = Localizer.S("system_health_scanner.active");
                string rebarLabel = hw.HasAmdGpu ? "AMD SAM " : (hw.HasNvidiaGpu ? "NVIDIA ReBAR " : "Resizable BAR ");
                string barSummary = bar1Mib >= 1024
                    ? string.Format("{0}{1} ({2:F0} GB BAR1)", rebarLabel, activeTag, bar1Mib / 1024.0)
                    : (rebarActive ? (rebarLabel + activeTag) : "Standard (256 MB BAR)");

                string rebarCat = hw.HasAmdGpu ? "PCIe • AMD SAM" : (hw.HasNvidiaGpu ? "PCIe • NVIDIA ReBAR" : "PCIe • ReBAR");
                string rebarTitle = hw.HasAmdGpu
                    ? Localizer.S("system_health_scanner.amd_smart_access_memory_sam_resizable")
                    : (hw.HasNvidiaGpu
                        ? Localizer.S("system_health_scanner.nvidia_resizable_bar_direct_cpu_gpu")
                        : Localizer.S("system_health_scanner.resizable_bar_full_cpu_gpu_vram"));

                string rebarGuideTitle = Localizer.S("system_health_scanner.guide_enable_resizable_bar_in_bios");

                string rebarGuideSteps = Localizer.S("system_health_scanner.1_reboot_your_pc_and_press");

                list.Add(new HardwareCheckItem
                {
                    CategoryTag = rebarCat,
                    Title = rebarTitle,
                    ValueSummary = barSummary,
                    Detail = rebarActive
                        ? Localizer.S("system_health_scanner.your_cpu_can_directly_address_the")
                        : (hw.HasIntelArcGpu
                            ? Localizer.S("system_health_scanner.critical_on_intel_arc_resizable_bar")
                            : Localizer.S("system_health_scanner.resizable_bar_appears_capped_at_256")),
                    Level = rebarActive ? HealthLevel.Good : (hw.HasIntelArcGpu ? HealthLevel.Critical : HealthLevel.Info),
                    GuideTitle = rebarGuideTitle,
                    GuideSteps = rebarGuideSteps,
                    QuickActionLabel = Localizer.S("system_health_scanner.device_manager"),
                    QuickActionCommand = "tool:devmgmt"
                });
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 3b. GPU PCIe Link Width & Signal Integrity
            try
            {
                int genCur, genMax, widthCur, widthMax;
                if (DetectGpuPcieLink(out genCur, out genMax, out widthCur, out widthMax) && widthMax > 0)
                {
                    bool degradedWidth = (widthCur > 0 && widthCur < widthMax && widthMax >= 8);

                    string pcieGuideTitle = Localizer.S("system_health_scanner.guide_resolve_degraded_pcie_link_width");

                    string pcieGuideSteps = Localizer.S("system_health_scanner.1_verify_gpu_is_seated_in");

                    list.Add(new HardwareCheckItem
                    {
                        CategoryTag = Localizer.S("system_health_scanner.gpu_pcie_link"),
                        Title = Localizer.S("system_health_scanner.graphics_card_pcie_link_integrity"),
                        ValueSummary = Localizer.F("system_health_scanner.pcie_gen_gpu_max_gen_width", genCur, genMax, widthCur, widthMax),
                        Detail = degradedWidth
                            ? Localizer.F("system_health_scanner.warning_gpu_is_running_at_x", widthCur, widthMax)
                            : Localizer.F("system_health_scanner.full_x_pcie_lane_width_negotiated", widthCur, genCur),
                        Level = degradedWidth ? HealthLevel.Warning : HealthLevel.Good,
                        InAppFixOp = degradedWidth ? "pcie_aspm" : null,
                        InAppFixLabel = degradedWidth ? Localizer.S("system_health_scanner.disable_pcie_aspm") : null,
                        GuideTitle = pcieGuideTitle,
                        GuideSteps = pcieGuideSteps,
                        QuickActionLabel = Localizer.S("system_health_scanner.device_manager"),
                        QuickActionCommand = "tool:devmgmt"
                    });
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 4. NVMe / SSD Disk Health & SMART
            try
            {
                using (var s = Wmi.Searcher(@"\\.\root\Microsoft\Windows\Storage", "SELECT DeviceId, FriendlyName, BusType, HealthStatus, Size FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string devId = Convert.ToString(mo["DeviceId"]).Trim();
                        string model = Convert.ToString(mo["FriendlyName"]).Trim();
                        ushort health = Convert.ToUInt16(mo["HealthStatus"] ?? 0);
                        ushort bus = Convert.ToUInt16(mo["BusType"] ?? 0);
                        double sizeGb = Math.Round(Convert.ToDouble(mo["Size"] ?? 0) / (1000.0 * 1000.0 * 1000.0));

                        bool isNvme = (bus == 17);
                        // HMB advice only on an NVMe drive under Windows 11 24H2+, where the issue exists.
                        bool isDramLessHmb = isNvme && Detection.NvmeDrives.WindowsHasHmbIssue && Detection.NvmeDrives.IsDramLess(model);

                        DiskSmartInfo smart = (adminSmart != null) ? adminSmart.FirstOrDefault(x => x.DeviceId == devId) : null;
                        if (smart == null) smart = TryReadSingleSmartCounter(devId);

                        string smartStr = (smart != null && (smart.TemperatureC > 0 || smart.PowerOnHours > 0))
                            ? string.Format("  •  {0} °C  •  Usure/Wear: {1}%  •  Erreurs: {2}", smart.TemperatureC, smart.WearPct, smart.UncorrectedErrors)
                            : "";

                        string hStatus = (health == 0)
                            ? Localizer.S("system_health_scanner.healthy_smart_ok")
                            : Localizer.F("system_health_scanner.smart_warning", health);

                        bool isHmbApplied = false;
                        try
                        {
                            isHmbApplied = (DiagnosticsEngine.ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy") == 2);
                        }
                        catch (Exception ex) { Log.Warn(ex); }

                        string nvmeGuideTitle = Localizer.S("system_health_scanner.understanding_the_nvme_hmb_bug_in");

                        string nvmeGuideSteps = Localizer.S("system_health_scanner.1_dram_less_ssds_kingston_nv2");

                        list.Add(new HardwareCheckItem
                        {
                            CategoryTag = isNvme ? "SSD NVMe" : "STOCKAGE",
                            Title = model + " (" + sizeGb + " GB)",
                            ValueSummary = hStatus + smartStr,
                            Detail = isDramLessHmb
                                ? Localizer.S("system_health_scanner.this_nvme_model_uses_host_memory")
                                : Localizer.S("system_health_scanner.drive_hardware_status_verified"),
                            Level = (health == 0 && (smart == null || smart.UncorrectedErrors == 0)) ? HealthLevel.Good : HealthLevel.Warning,
                            InAppFixOp = (isDramLessHmb && !isHmbApplied) ? "nvme_hmb" : null,
                            InAppFixLabel = (isDramLessHmb && !isHmbApplied) ? Localizer.S("system_health_scanner.stabilize_nvme_hmb_64mb") : null,
                            InAppFixApplied = (isDramLessHmb && isHmbApplied),
                            GuideTitle = isDramLessHmb ? nvmeGuideTitle : null,
                            GuideSteps = isDramLessHmb ? nvmeGuideSteps : null,
                            QuickActionLabel = Localizer.S("system_health_scanner.disk_management"),
                            QuickActionCommand = "tool:diskmgmt"
                        });
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            return list;
        }

        public static List<DiskSmartInfo> ReadAllSmartCountersAdmin()
        {
            List<DiskSmartInfo> list = new List<DiskSmartInfo>();
            try
            {
                using (var s = Wmi.Searcher(@"\\.\root\Microsoft\Windows\Storage",
                    "SELECT DeviceId, Temperature, Wear, ReadErrorsUncorrected, WriteErrorsUncorrected, PowerOnHours FROM MSFT_StorageReliabilityCounter"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        long rErr = Convert.ToInt64(mo["ReadErrorsUncorrected"] ?? 0);
                        long wErr = Convert.ToInt64(mo["WriteErrorsUncorrected"] ?? 0);
                        list.Add(new DiskSmartInfo
                        {
                            DeviceId = Convert.ToString(mo["DeviceId"]).Trim(),
                            TemperatureC = Convert.ToInt32(mo["Temperature"] ?? 0),
                            WearPct = Convert.ToInt32(mo["Wear"] ?? 0),
                            UncorrectedErrors = rErr + wErr,
                            PowerOnHours = Convert.ToInt64(mo["PowerOnHours"] ?? 0)
                        });
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        private static DiskSmartInfo TryReadSingleSmartCounter(string devId)
        {
            try
            {
                var all = ReadAllSmartCountersAdmin();
                return all.FirstOrDefault(x => x.DeviceId == devId);
            }
            catch (Exception ex) { Log.Warn(ex); return null; }
        }

        private static long DetectGpuBar1Mib()
        {
            // 1. Try nvidia-smi -q -d MEMORY
            try
            {
                // Full path only, and a real timeout: nvidia-smi hangs when the driver is wedged (common after TDRs).
                string smi = SysTools.NvidiaSmiPath;
                if (smi != null)
                {
                    string output = SysTools.Run(smi, "-q -d MEMORY", 5000).Output;
                    Match m = Regex.Match(output, @"BAR1 Memory Usage[\s\S]{1,120}?Total\s*:\s*(\d+)\s*MiB", RegexOptions.IgnoreCase);
                    if (m.Success)
                        return long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 2. Fallback: Win32_DeviceMemoryAddress range > 512 MB
            try
            {
                using (var s = Wmi.Searcher("SELECT StartingAddress, EndingAddress FROM Win32_DeviceMemoryAddress"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        ulong start = Convert.ToUInt64(mo["StartingAddress"] ?? 0);
                        ulong end = Convert.ToUInt64(mo["EndingAddress"] ?? 0);
                        if (end > start && (end - start) > 536870912UL)
                            return (long)((end - start + 1UL) / (1024UL * 1024UL));
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return 256;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevsW(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiGetDevicePropertyW(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref DEVPROPKEY PropertyKey, out uint PropertyType, out uint PropertyBuffer, uint PropertyBufferSize, out uint RequiredSize, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        private static bool DetectGpuPcieLink(out int genCur, out int genMax, out int widthCur, out int widthMax)
        {
            genCur = genMax = widthCur = widthMax = 0;

            // 1. Universal Native Windows Kernel PCIe Link Query (works on AMD Radeon, Intel Arc & NVIDIA RTX without admin)
            try
            {
                Guid displayGuid = new Guid("4d36e968-e325-11ce-bfc1-08002be10318");
                IntPtr devSet = SetupDiGetClassDevsW(ref displayGuid, null, IntPtr.Zero, 0x02 /* DIGCF_PRESENT */);
                if (devSet != IntPtr.Zero && devSet != new IntPtr(-1))
                {
                    try
                    {
                        Guid pciFmt = new Guid("3ab22e31-8264-4b4e-9af5-a8d2d8e33e62");
                        SP_DEVINFO_DATA dev = new SP_DEVINFO_DATA { cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA)) };

                        uint bestMaxW = 0;
                        for (uint i = 0; SetupDiEnumDeviceInfo(devSet, i, ref dev); i++)
                        {
                            StringBuilder sbId = new StringBuilder(260);
                            uint reqId;
                            if (!SetupDiGetDeviceInstanceIdW(devSet, ref dev, sbId, 260, out reqId))
                                continue;
                            string instId = sbId.ToString();
                            if (!instId.StartsWith("PCI\\", StringComparison.OrdinalIgnoreCase))
                                continue;

                            uint t, reqSz, cSpd = 0, cW = 0, mSpd = 0, mW = 0;
                            DEVPROPKEY kCurSpd = new DEVPROPKEY { fmtid = pciFmt, pid = 9 };  // DEVPKEY_PciDevice_CurrentLinkSpeed
                            DEVPROPKEY kCurW   = new DEVPROPKEY { fmtid = pciFmt, pid = 10 }; // DEVPKEY_PciDevice_CurrentLinkWidth
                            DEVPROPKEY kMaxSpd = new DEVPROPKEY { fmtid = pciFmt, pid = 11 }; // DEVPKEY_PciDevice_MaxLinkSpeed
                            DEVPROPKEY kMaxW   = new DEVPROPKEY { fmtid = pciFmt, pid = 12 }; // DEVPKEY_PciDevice_MaxLinkWidth

                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kCurSpd, out t, out cSpd, 4, out reqSz, 0);
                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kCurW,   out t, out cW,   4, out reqSz, 0);
                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kMaxSpd, out t, out mSpd, 4, out reqSz, 0);
                            SetupDiGetDevicePropertyW(devSet, ref dev, ref kMaxW,   out t, out mW,   4, out reqSz, 0);

                            // Prefer discrete PCIe GPUs (x8 / x16) over CPU-internal iGPUs
                            if (mW >= 4 && mW >= bestMaxW)
                            {
                                bestMaxW = mW;
                                genCur = (int)(cSpd > 0 ? cSpd : mSpd);
                                genMax = (int)(mSpd > 0 ? mSpd : cSpd);
                                widthCur = (int)cW;
                                widthMax = (int)mW;
                            }
                        }

                        if (widthMax > 0)
                            return true;
                    }
                    finally
                    {
                        SetupDiDestroyDeviceInfoList(devSet);
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            // 2. Fallback: nvidia-smi
            try
            {
                string smi = SysTools.NvidiaSmiPath;
                if (smi != null)
                {
                    string output = SysTools.Run(smi, "--query-gpu=pcie.link.gen.current,pcie.link.gen.max,pcie.link.width.current,pcie.link.width.max --format=csv,noheader,nounits", 5000).Output;
                    string line = output.Split('\n')[0].Trim();
                    string[] parts = line.Split(',');
                    if (parts.Length >= 4)
                    {
                        int.TryParse(parts[0].Trim(), out genCur);
                        int.TryParse(parts[1].Trim(), out genMax);
                        int.TryParse(parts[2].Trim(), out widthCur);
                        int.TryParse(parts[3].Trim(), out widthMax);
                        return widthMax > 0;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return false;
        }

        // ==================== FEATURE 3: SOFTWARE CONFLICTS & DRIVER REMNANTS ====================
        public static List<ConflictCheckItem> ScanSoftwareConflicts(HardwareSummary hw)
        {
            List<ConflictCheckItem> list = new List<ConflictCheckItem>();

            // 1. Active Overlay Stack Check
            List<string> activeOverlays = new List<string>();
            if (IsProcActive("Discord")) activeOverlays.Add("Discord");
            if (IsProcActive("GameOverlayUI") || IsProcActive("steamwebhelper")) activeOverlays.Add("Steam Overlay");
            if (IsProcActive("NVIDIA Overlay") || IsProcActive("NVIDIA Share")) activeOverlays.Add("NVIDIA Overlay");
            if (IsProcActive("RadeonSoftware")) activeOverlays.Add("AMD Adrenalin Overlay");
            if (IsProcActive("RTSS")) activeOverlays.Add("RivaTuner (RTSS)");
            if (IsProcActive("Overwolf")) activeOverlays.Add("Overwolf");

            bool manyOverlays = activeOverlays.Count >= 3;
            bool isDiscordRunning = IsProcActive("Discord");

            string ovGuideTitle = Localizer.S("system_health_scanner.guide_disable_conflicting_overlays");

            string ovGuideSteps = Localizer.S("system_health_scanner.1_discord_user_settings_game_overlay");

            list.Add(new ConflictCheckItem
            {
                Tag = "OVERLAYS",
                Title = Localizer.S("system_health_scanner.simultaneous_graphics_overlays"),
                StatusText = activeOverlays.Count == 0
                    ? Localizer.S("system_health_scanner.no_active_overlays")
                    : string.Join(", ", activeOverlays),
                Explanation = manyOverlays
                    ? Localizer.S("system_health_scanner.3_or_more_overlays_are_running")
                    : Localizer.S("system_health_scanner.overlay_count_is_clean_no_directx"),
                Level = manyOverlays ? HealthLevel.Warning : HealthLevel.Good,
                InAppFixOp = (manyOverlays && isDiscordRunning) ? "discord_hwaccel" : null,
                InAppFixLabel = (manyOverlays && isDiscordRunning) ? Localizer.S("system_health_scanner.disable_discord_hwaccel") : null,
                GuideTitle = ovGuideTitle,
                GuideSteps = ovGuideSteps,
                QuickActionLabel = Localizer.S("system_health_scanner.app_settings"),
                QuickActionCommand = "tool:appsfeatures"
            });

            // 2. Cross-Vendor GPU Driver Service Remnants
            List<string> remnants = new List<string>();
            bool onlyRyzenIgpuHelper = false;
            try
            {
                bool isRyzenCpu = (hw.CpuName ?? "").IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0;
                ServiceController[] services = ServiceController.GetServices();
                foreach (var sc in services)
                {
                    if (sc.Status != ServiceControllerStatus.Running) continue;
                    string sn = sc.ServiceName ?? "";
                    if (!hw.HasAmdGpu && sn.Equals("AMDRSServ", StringComparison.OrdinalIgnoreCase))
                        remnants.Add("AMDRSServ (AMD Adrenalin)");
                    if (!hw.HasAmdGpu && sn.Equals("AMD External Events Utility", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isRyzenCpu) onlyRyzenIgpuHelper = true;
                        else remnants.Add("AMD External Events Utility");
                    }
                    if (!hw.HasNvidiaGpu && sn.IndexOf("NVDisplay.Container", StringComparison.OrdinalIgnoreCase) >= 0)
                        remnants.Add(sn + " (NVIDIA)");
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            string gpuGuideTitle = Localizer.S("system_health_scanner.guide_gpu_driver_cleanup_igpu_management");

            string gpuGuideSteps = onlyRyzenIgpuHelper
                ? Localizer.S("system_health_scanner.1_your_amd_ryzen_cpu_includes")
                : Localizer.S("system_health_scanner.1_download_the_latest_official_display");

            list.Add(new ConflictCheckItem
            {
                Tag = "PILOTES GPU",
                Title = Localizer.S("system_health_scanner.conflicting_gpu_driver_service_remnants"),
                StatusText = remnants.Count > 0
                    ? string.Join(", ", remnants)
                    : (onlyRyzenIgpuHelper
                        ? Localizer.S("system_health_scanner.ryzen_igpu_helper_active_amd_external")
                        : Localizer.S("system_health_scanner.no_conflicting_gpu_services_running")),
                Explanation = remnants.Count > 0
                    ? Localizer.S("system_health_scanner.a_background_driver_service_from_another")
                    : (onlyRyzenIgpuHelper
                        ? Localizer.S("system_health_scanner.installed_for_the_amd_ryzen_cpu")
                        : Localizer.S("system_health_scanner.only_your_active_gpu_vendor_s")),
                Level = remnants.Count > 0 ? HealthLevel.Warning : (onlyRyzenIgpuHelper ? HealthLevel.Info : HealthLevel.Good),
                InAppFixOp = onlyRyzenIgpuHelper ? "disable_ryzen_igpu" : null,
                InAppFixLabel = onlyRyzenIgpuHelper ? Localizer.S("system_health_scanner.disable_ryzen_igpu_service") : null,
                GuideTitle = gpuGuideTitle,
                GuideSteps = gpuGuideSteps,
                QuickActionLabel = onlyRyzenIgpuHelper
                    ? Localizer.S("system_health_scanner.device_manager")
                    : (remnants.Count > 0 ? Localizer.S("system_health_scanner.download_ddu") : null),
                QuickActionCommand = onlyRyzenIgpuHelper ? "tool:devmgmt" : null,
                WebSearchQuery = remnants.Count > 0 ? "https://www.wagnardsoft.com/display-driver-uninstaller-ddu-" : null
            });

            // 3. Risky Kernel Drivers & Motherboard Bloatware (ene.sys, WinRing0, Gigabyte Update, cFosSpeed)
            List<string> riskyFound = new List<string>();
            string driversDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
            if (File.Exists(Path.Combine(driversDir, "cfosspeed.sys"))) riskyFound.Add("cfosspeed.sys (cFosSpeed Network Shaper)");
            if (IsProcActive("GbtCloudMatrix") || IsServiceRunning("GigabyteUpdateService")) riskyFound.Add("Gigabyte Update / CloudMatrix");
            if (IsProcActive("PatriotViperRGB") || IsProcActive("AAChal")) riskyFound.Add("Legacy RGB Service (ene.sys)");

            string riskyGuideTitle = Localizer.S("system_health_scanner.guide_removing_risky_oem_components_driv");

            string riskyGuideSteps = Localizer.S("system_health_scanner.1_click_neutralize_in_1_click");

            list.Add(new ConflictCheckItem
            {
                Tag = "NOYAU & SERVICES",
                Title = Localizer.S("system_health_scanner.known_unstable_drivers_oem_bloat_service"),
                StatusText = riskyFound.Count == 0
                    ? Localizer.S("system_health_scanner.no_unstable_oem_services_active")
                    : string.Join(", ", riskyFound),
                Explanation = riskyFound.Count == 0
                    ? Localizer.S("system_health_scanner.services_notorious_for_memory_leaks_or")
                    : Localizer.S("system_health_scanner.third_party_components_known_to_cause"),
                Level = riskyFound.Count == 0 ? HealthLevel.Good : HealthLevel.Warning,
                InAppFixOp = riskyFound.Count > 0 ? "disable_risky_oem" : null,
                InAppFixLabel = riskyFound.Count > 0 ? Localizer.S("system_health_scanner.neutralize_bloat_in_1_click") : null,
                GuideTitle = riskyGuideTitle,
                GuideSteps = riskyGuideSteps,
                QuickActionLabel = Localizer.S("system_health_scanner.installed_apps"),
                QuickActionCommand = "tool:appsfeatures"
            });

            // 4. Anti-Cheat Ring-0 vs Kernel Shadow Stacks & Auto HDR (Win11 24H2+)
            List<string> antiCheats = new List<string>();
            if (File.Exists(Path.Combine(driversDir, "vgk.sys")) || IsServiceInstalled("vgk")) antiCheats.Add("Riot Vanguard (vgk.sys)");
            if (IsServiceInstalled("EasyAntiCheat") || IsServiceInstalled("EasyAntiCheat_EOS")) antiCheats.Add("Easy Anti-Cheat");
            if (IsServiceInstalled("BEService")) antiCheats.Add("BattlEye");

            bool shadowStackOn = ReadRegDwordSafe(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\KernelShadowStacks", "Enabled") == 1;
            bool autoHdrOn = CheckAutoHdrActive();
            bool acConflict = antiCheats.Count > 0 && (shadowStackOn || autoHdrOn);

            List<string> acCollisionNotes = new List<string>();
            if (shadowStackOn) acCollisionNotes.Add("Kernel Shadow Stacks ON");
            if (autoHdrOn) acCollisionNotes.Add("Auto HDR ON");

            string acGuideTitle = Localizer.S("system_health_scanner.guide_kernel_anti_cheat_vs_windows");

            string acGuideSteps = Localizer.S("system_health_scanner.1_on_windows_11_24h2_ring");

            list.Add(new ConflictCheckItem
            {
                Tag = "ANTI-CHEAT • RING-0",
                Title = Localizer.S("system_health_scanner.kernel_anti_cheats_vs_hardware_stack"),
                StatusText = antiCheats.Count == 0
                    ? Localizer.S("system_health_scanner.no_persistent_ring_0_anti_cheat")
                    : (string.Join(", ", antiCheats) + (acCollisionNotes.Count > 0 ? ("  •  " + string.Join(" + ", acCollisionNotes)) : "")),
                Explanation = acConflict
                    ? Localizer.S("system_health_scanner.on_windows_11_24h2_combining_ring")
                    : Localizer.S("system_health_scanner.no_known_collision_detected_between_ring"),
                Level = acConflict ? HealthLevel.Warning : HealthLevel.Good,
                InAppFixOp = autoHdrOn ? "disable_autohdr" : null,
                InAppFixLabel = autoHdrOn ? Localizer.S("system_health_scanner.disable_windows_auto_hdr") : null,
                GuideTitle = acGuideTitle,
                GuideSteps = acGuideSteps,
                QuickActionLabel = Localizer.S("system_health_scanner.device_security"),
                QuickActionCommand = "tool:device_security"
            });

            // 5. Realtek 2.5GbE EEE / Green Ethernet & MediaTek RZ616 Wi-Fi Power-Saving Check
            string nicDesc;
            bool nicHasPowerBug = InspectNicPowerSavingRisk(out nicDesc);
            if (!string.IsNullOrEmpty(nicDesc))
            {
                string nicGuideTitle = Localizer.S("system_health_scanner.guide_realtek_2_5gbe_wi_fi");

                string nicGuideSteps = Localizer.S("system_health_scanner.1_realtek_2_5gbe_rtl8125_8126");

                list.Add(new ConflictCheckItem
                {
                    Tag = "RÉSEAU • 2.5GbE / WIFI",
                    Title = Localizer.S("system_health_scanner.realtek_2_5gbe_mediatek_network_controll"),
                    StatusText = nicDesc,
                    Explanation = nicHasPowerBug
                        ? Localizer.S("system_health_scanner.power_saving_green_ethernet_eee_pcie")
                        : Localizer.S("system_health_scanner.unstable_power_saving_states_eee_green"),
                    Level = nicHasPowerBug ? HealthLevel.Warning : HealthLevel.Good,
                    InAppFixOp = nicHasPowerBug ? "nic_powersave_apply" : null,
                    InAppFixLabel = nicHasPowerBug ? Localizer.S("system_health_scanner.disable_network_power_saving_in_1") : null,
                    InAppFixApplied = !nicHasPowerBug,
                    GuideTitle = nicGuideTitle,
                    GuideSteps = nicGuideSteps,
                    QuickActionLabel = Localizer.S("system_health_scanner.network_connections_ncpa_cpl"),
                    QuickActionCommand = "tool:ncpa"
                });
            }

            list.AddRange(Detection.DetectionChecks.All(hw));

            return list;
        }

        public static bool InspectNicPowerSavingRisk(out string summary)
        {
            summary = "";
            List<string> names = new List<string>();
            bool risky = false;
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey ck = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", false))
                {
                    if (ck == null) return false;
                    foreach (string sub in ck.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        using (RegistryKey sk = ck.OpenSubKey(sub, false))
                        {
                            if (sk == null) continue;
                            string desc = Convert.ToString(sk.GetValue("DriverDesc") ?? "").Trim();
                            if (!Regex.IsMatch(desc, @"(Realtek.*2\.5GbE|Realtek.*Gaming|MediaTek.*Wi-Fi|RZ616|MT792|Realtek.*8852)", RegexOptions.IgnoreCase))
                                continue;

                            names.Add(desc);
                            string eee = Convert.ToString(sk.GetValue("*EEE") ?? "0");
                            string green = Convert.ToString(sk.GetValue("EnableGreenEthernet") ?? "0");
                            string ps = Convert.ToString(sk.GetValue("PowerSavingMode") ?? "0");
                            int pnp = Convert.ToInt32(sk.GetValue("PnPCapabilities") ?? 0);

                            if (eee != "0" || green != "0" || ps != "0" || (pnp & 0x18) != 0x18)
                                risky = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            if (names.Count > 0)
                summary = string.Join(" + ", names.Distinct());
            return risky;
        }

        private static bool IsServiceInstalled(string serviceName)
        {
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey sk = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + serviceName, false))
                {
                    return sk != null;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static int ReadRegDwordSafe(RegistryHive hive, string subKey, string valName)
        {
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey sk = bk.OpenSubKey(subKey, false))
                {
                    if (sk == null) return 0;
                    object v = sk.GetValue(valName);
                    return v != null ? Convert.ToInt32(v) : 0;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return 0; }
        }

        private static bool CheckAutoHdrActive()
        {
            try
            {
                using (RegistryKey uk = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences", false))
                {
                    if (uk == null) return false;
                    string def = Convert.ToString(uk.GetValue("DirectXUserGlobalSettings") ?? "");
                    return def.IndexOf("AutoHDREnable=1", StringComparison.OrdinalIgnoreCase) >= 0;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static bool IsProcActive(string name)
        {
            try { return Process.GetProcessesByName(name).Length > 0; }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static bool IsServiceRunning(string serviceName)
        {
            try
            {
                using (ServiceController sc = new ServiceController(serviceName))
                {
                    return sc.Status == ServiceControllerStatus.Running;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }
    }
}
