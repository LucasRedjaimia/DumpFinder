using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
using System.Reflection;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public static class DiagnosticsEngine
    {
        private const string GpuClassKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        private static HardwareSummary _cachedHw;

        /// <summary>System log: events kept per family (newest first). Blue screens (WER 1001) are never capped.</summary>
        private const int MaxEventsPerFamily = 500;
        /// <summary>Corrected WHEA errors (17 / 19 / 47) can be logged by the thousand: listing the newest is enough.</summary>
        private const int MaxCorrectedWheaEvents = 200;
        /// <summary>Chrome / Edge Crashpad dumps examined per scan (newest first), and bytes read from each.</summary>
        private const int MaxBrowserDumps = 200;
        private const int BrowserDumpReadBytes = 8 * 1024 * 1024;
        private static double _cachedShaderMb = -1;

        public static bool IsAdministrator()
        {
            return Elevation.IsAdministrator();
        }

        public static HardwareSummary GetHardwareSummary()
        {
            HardwareSummary hw = new HardwareSummary
            {
                CpuName = "Unknown CPU",
                GpuName = "Unknown GPU",
                GpuDriver = "-",
                BoardName = "-",
                OsVersion = Environment.OSVersion.VersionString,
                LastBoot = DateTime.Now.AddMilliseconds(-Environment.TickCount),
                IsAdmin = IsAdministrator()
            };

            if (_cachedHw != null)
            {
                hw.CpuName = _cachedHw.CpuName;
                hw.GpuName = _cachedHw.GpuName;
                hw.GpuDriver = _cachedHw.GpuDriver;
                hw.HasAmdCpu = _cachedHw.HasAmdCpu;
                hw.HasIntelCpu = _cachedHw.HasIntelCpu;
                hw.HasAmdGpu = _cachedHw.HasAmdGpu;
                hw.IsAmdRx7000 = _cachedHw.IsAmdRx7000; // was lost from the 2nd scan on (RDNA3 diagnosis title)
                hw.HasNvidiaGpu = _cachedHw.HasNvidiaGpu;
                hw.HasIntelArcGpu = _cachedHw.HasIntelArcGpu;
                hw.IsIntel13Or14Gen = _cachedHw.IsIntel13Or14Gen;
                hw.BoardName = _cachedHw.BoardName;
                hw.OsVersion = _cachedHw.OsVersion;
            }
            else
            {
                try
                {
                    using (var s = Wmi.Searcher("SELECT Name FROM Win32_Processor"))
                    {
                        foreach (ManagementObject mo in s.Get())
                        {
                            hw.CpuName = Convert.ToString(mo["Name"]).Trim();
                            hw.HasAmdCpu = (hw.CpuName.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                            hw.CpuName.IndexOf("Ryzen", StringComparison.OrdinalIgnoreCase) >= 0);
                            hw.HasIntelCpu = !hw.HasAmdCpu && (hw.CpuName.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                              Regex.IsMatch(hw.CpuName, @"\b(Core\s+i[3579]|Core\s+Ultra|Xeon|Celeron|Pentium)\b", RegexOptions.IgnoreCase));
                            if (hw.HasIntelCpu && Regex.IsMatch(hw.CpuName, @"i[579]-1[34]\d{3}", RegexOptions.IgnoreCase))
                                hw.IsIntel13Or14Gen = true;
                            break;
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }

                try
                {
                    // The GPU shown and checked is the most relevant discrete card, whatever order WMI lists them in:
                    // NVIDIA > AMD Radeon RX/Pro > Intel Arc A/B > integrated graphics. An integrated GPU (Ryzen
                    // "AMD Radeon(TM) Graphics", Core Ultra "Intel(R) Arc(TM) Graphics") no longer overwrites the
                    // discrete card's driver version, nor turns on AMD / Arc specific checks next to another card.
                    int bestRank = -1;
                    bool amdIntegrated = false, amdDiscrete = false;
                    using (var s = Wmi.Searcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                    {
                        foreach (ManagementObject mo in s.Get())
                        {
                            string name = Convert.ToString(mo["Name"]).Trim();
                            if (name.Length == 0 ||
                                name.IndexOf("spacedesk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Basic Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;

                            int rank = 0;
                            bool isAmd = name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                         name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (isAmd)
                            {
                                bool discrete = Regex.IsMatch(name, @"\b(RX|Pro\s+W|FirePro|Instinct)\b", RegexOptions.IgnoreCase);
                                if (discrete)
                                {
                                    amdDiscrete = true;
                                    rank = 3;
                                    if (Regex.IsMatch(name, @"RX\s*7[6789]\d{2}", RegexOptions.IgnoreCase))
                                        hw.IsAmdRx7000 = true;
                                }
                                else
                                {
                                    amdIntegrated = true;
                                    rank = 1;
                                }
                            }

                            if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hw.HasNvidiaGpu = true;
                                rank = 4;
                            }

                            if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // Discrete Arc cards carry a model number (A380, A770, B580…); the iGPU is just "Arc Graphics".
                                if (Regex.IsMatch(name, @"\bArc(\(TM\))?\s+(Pro\s+)?[AB]\d{2,3}", RegexOptions.IgnoreCase))
                                {
                                    hw.HasIntelArcGpu = true;
                                    rank = 2;
                                }
                                else rank = 1;
                            }

                            if (rank > bestRank)
                            {
                                bestRank = rank;
                                hw.GpuName = name;
                                hw.GpuDriver = Convert.ToString(mo["DriverVersion"]).Trim();
                            }
                        }
                    }
                    // A Ryzen iGPU only counts as "the AMD GPU" when no other graphics card is installed.
                    hw.HasAmdGpu = amdDiscrete || (amdIntegrated && !hw.HasNvidiaGpu && !hw.HasIntelArcGpu);
                }
                catch (Exception ex) { Log.Warn(ex); }

                try
                {
                    using (var s = Wmi.Searcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                    {
                        foreach (ManagementObject mo in s.Get())
                        {
                            hw.BoardName = (Convert.ToString(mo["Manufacturer"]) + " " + Convert.ToString(mo["Product"])).Trim();
                            break;
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }

                try
                {
                    using (var s = Wmi.Searcher("SELECT Caption, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem"))
                    {
                        foreach (ManagementObject mo in s.Get())
                        {
                            hw.OsVersion = Convert.ToString(mo["Caption"]).Trim() + " (Build " + Convert.ToString(mo["BuildNumber"]).Trim() + ")";
                            string lbt = Convert.ToString(mo["LastBootUpTime"]);
                            if (!string.IsNullOrEmpty(lbt))
                                hw.LastBoot = ManagementDateTimeConverter.ToDateTime(lbt);
                            break;
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }

                _cachedHw = hw;
            }

            try
            {
                using (var s = Wmi.Searcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        string lbt = Convert.ToString(mo["LastBootUpTime"]);
                        if (!string.IsNullOrEmpty(lbt))
                            hw.LastBoot = ManagementDateTimeConverter.ToDateTime(lbt);
                        break;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            return hw;
        }

        public static ScanResult RunScan(DateTime? startTimeFilter)
        {
            HardwareSummary hw = GetHardwareSummary();
            // Driver inventory is WMI-heavy (~3 s): run it alongside the event log scan.
            var driversTask = System.Threading.Tasks.Task.Run(() => DriverInventory.Scan());
            List<CrashEventItem> events = new List<CrashEventItem>();
            List<DateTime> bugCheckTimes = new List<DateTime>();

            ScanSystemEvents(startTimeFilter, events, bugCheckTimes);
            ScanApplicationEvents(startTimeFilter, events, hw);
            ScanBrowserCrashpad(startTimeFilter, events);

            events = events.OrderByDescending(e => e.Timestamp).ToList();

            int bsodCount = events.Count(e => e.Category == CrashCategory.BSOD || e.Category == CrashCategory.UnexpectedReboot);
            int gpuCount = events.Count(e => e.Category == CrashCategory.GpuDriver);
            int storageCount = events.Count(e => e.Category == CrashCategory.StorageNvme);
            int wheaMemCount = events.Count(e => e.Category == CrashCategory.HardwareWhea || e.Category == CrashCategory.MemoryExhaustion);
            int appCount = events.Count(e => e.Category == CrashCategory.BrowserGpu || e.Category == CrashCategory.AppCrash);

            List<RegistryFixItem> fixes = InspectAllFixes(true);

            bool minidumpNeedsAdmin;
            List<MinidumpReportItem> minidumps = MinidumpAnalyzer.ScanAllAccessibleDumps(out minidumpNeedsAdmin);
            List<DiskSmartInfo> autoSmart = hw.IsAdmin ? SystemHealthScanner.ReadAllSmartCountersAdmin() : null;
            List<HardwareCheckItem> hwChecks = SystemHealthScanner.ScanHardwareHealth(hw, autoSmart);
            List<ConflictCheckItem> conflictChecks = SystemHealthScanner.ScanSoftwareConflicts(hw);

            ScanResult res = new ScanResult
            {
                Hardware = hw,
                FilterStartTime = startTimeFilter,
                Events = events,
                Fixes = fixes,
                HardwareChecks = hwChecks,
                ConflictChecks = conflictChecks,
                Minidumps = minidumps,
                MinidumpFolderNeedsAdmin = minidumpNeedsAdmin,
                BsodAndRebootCount = bsodCount,
                GpuTdrCount = gpuCount,
                StorageNvmeCount = storageCount,
                WheaAndMemCount = wheaMemCount,
                BrowserAndAppCount = appCount
            };

            res.Reliability = Reliability.Daily(Stability.WindowDays);
            try { res.Drivers = driversTask.Result; }
            catch (Exception ex) { Log.Warn(ex, "drivers"); res.Drivers = new List<DriverInfo>(); }
            BuildSmartDiagnosis(res);
            return res;
        }

        public static void RefreshLocalizedHealthAndConflicts(ScanResult res, List<DiskSmartInfo> cachedAdminSmart)
        {
            if (res == null || res.Hardware == null) return;
            res.HardwareChecks = SystemHealthScanner.ScanHardwareHealth(res.Hardware, cachedAdminSmart);
            res.ConflictChecks = SystemHealthScanner.ScanSoftwareConflicts(res.Hardware);
        }

        public static bool RunAdminDeepScanToFile(string jsonPath)
        {
            try
            {
                var payload = new AdminDeepScanPayload
                {
                    KernelDumps = MinidumpAnalyzer.ScanKernelMinidumpsOnly(),
                    SmartCounters = SystemHealthScanner.ReadAllSmartCountersAdmin()
                };
                // Keep readable copies so later (non-elevated) scans and WinDbg can use them.
                MinidumpAnalyzer.CopyKernelDumpsLocally(payload.KernelDumps);
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                File.WriteAllText(jsonPath, ser.Serialize(payload), Encoding.UTF8);
                return true;
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static void ScanSystemEvents(DateTime? startTime, List<CrashEventItem> list, List<DateTime> bugCheckTimes)
        {
            string timeClause = "";
            if (startTime.HasValue)
            {
                string utcIso = startTime.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
                timeClause = " and TimeCreated[@SystemTime >= '" + utcIso + "']";
            }

            // Includes BSOD (1001), Power (41), GPU TDR (13, 14, 153, 4101), WHEA (17, 18, 19, 46, 47), NVMe/Disk (7, 11, 51, 55, 129), OOM (2004), NDIS (10400), TPM (1796)
            string xpath = "*[System[(EventID=1001 or EventID=41 or EventID=153 or EventID=14 or EventID=13 or EventID=4101 or EventID=17 or EventID=18 or EventID=19 or EventID=46 or EventID=47 or EventID=129 or EventID=11 or EventID=7 or EventID=51 or EventID=55 or EventID=2004 or EventID=10400 or EventID=1796)" + timeClause + "]]";

            List<EventRecordData> rawRecords = new List<EventRecordData>();
            // Newest first, with a cap per family: a PC logging thousands of corrected WHEA errors used to make the scan
            // format every one of them (FormatDescription is the slow part). Beyond the cap events are only counted.
            var perFamily = new Dictionary<string, int>(StringComparer.Ordinal);
            int capped = 0;
            Func<string, bool> keep = family =>
            {
                int n;
                perFamily.TryGetValue(family, out n);
                int cap = family == "whea-corrected" ? MaxCorrectedWheaEvents : MaxEventsPerFamily;
                if (n >= cap) { capped++; return false; }
                perFamily[family] = n + 1;
                return true;
            };
            try
            {
                EventLogQuery query = new EventLogQuery("System", PathType.LogName, xpath) { ReverseDirection = true };
                using (EventLogReader reader = new EventLogReader(query))
                {
                    EventRecord rec;
                    while ((rec = reader.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            string provider = rec.ProviderName ?? "";
                            int id = rec.Id;
                            DateTime ts = rec.TimeCreated.HasValue ? rec.TimeCreated.Value.ToLocalTime() : DateTime.MinValue;

                            if (provider.IndexOf("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) >= 0 && id == 1001)
                            {
                                // Blue screens are always kept (they also pair with Kernel-Power 41 below).
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                                bugCheckTimes.Add(ts);
                            }
                            else if (provider.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0 && id == 41)
                            {
                                if (keep("power41"))
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec), Named = NamedEventData(rec) });
                            }
                            else if (provider.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("amdwddmg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("amdkmdag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("amdkmdap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("igfx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("igdkmd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     (provider.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101))
                            {
                                if ((rec.Level == 1 || rec.Level == 2 || rec.Level == 3 || id == 153 || id == 14 || id == 4101) && keep("gpu"))
                                {
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                                }
                            }
                            else if (provider.IndexOf("WHEA-Logger", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                // 17 / 19 / 47 are corrected errors (often thousands): own, lower cap.
                                bool corrected = id == 17 || id == 19 || id == 47;
                                if (keep(corrected ? "whea-corrected" : "whea"))
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.IndexOf("stornvme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("storahci", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     (provider.Equals("disk", StringComparison.OrdinalIgnoreCase) && (id == 7 || id == 51 || id == 153)) ||
                                     (provider.IndexOf("Ntfs", StringComparison.OrdinalIgnoreCase) >= 0 && id == 55))
                            {
                                if (keep("storage"))
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.IndexOf("Resource-Exhaustion-Detector", StringComparison.OrdinalIgnoreCase) >= 0 && id == 2004)
                            {
                                if (keep("memory"))
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.Equals("NDIS", StringComparison.OrdinalIgnoreCase) && id == 10400)
                            {
                                if (keep("ndis"))
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if ((provider.IndexOf("TPM", StringComparison.OrdinalIgnoreCase) >= 0) && (id == 14 || id == 1796))
                            {
                                if (keep("tpm"))
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            if (capped > 0) Log.Info("System log: " + capped + " older event(s) beyond the per-family caps were counted but not listed");

            foreach (var r in rawRecords)
            {
                if (r.Provider.IndexOf("WER-SystemErrorReporting", StringComparison.OrdinalIgnoreCase) >= 0 && r.Id == 1001)
                {
                    string bugCode = ExtractBugCheckCode(r.Msg, r.Props);
                    string bugNameFr, bugNameEn, explainFr, explainEn;
                    DecodeBugCheck(bugCode, out bugNameFr, out bugNameEn, out explainFr, out explainEn);

                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.BSOD,
                        Source = "BugCheck / Minidump",
                        CodeOrId = bugCode,
                        SummaryFr = bugNameFr,
                        SummaryEn = bugNameEn,
                        DetailsFr = Localizer.Ref("engine.diagnostics_engine.bugcheck_details", explainEn, r.Msg),
                        DetailsEn = Localizer.Ref("engine.diagnostics_engine.bugcheck_details", explainEn, r.Msg)
                    });
                }
                else if (r.Provider.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0 && r.Id == 41)
                {
                    bool hasPairedBsod = bugCheckTimes.Any(bt => Math.Abs((bt - r.Time).TotalMinutes) <= 3.0);
                    // Kernel-Power 41 says *how* the previous session ended (named event data):
                    //  BugcheckCode ≠ 0          → a blue screen happened but no dump/WER report was written;
                    //  PowerButtonTimestamp ≠ 0  → the power button was held (forced off, usually after a freeze);
                    //  SleepInProgress ≠ 0       → power was lost while entering / leaving sleep;
                    //  all 0                     → power loss or a hardware reset (PSU, cable, overclock).
                    uint kpBugcheck = (uint)NamedNumber(r.Named, "BugcheckCode");
                    bool kpButton = NamedNumber(r.Named, "PowerButtonTimestamp") != 0;
                    bool kpSleep = NamedNumber(r.Named, "SleepInProgress") != 0;
                    if (!hasPairedBsod && kpBugcheck != 0)
                    {
                        string hex = string.Format("0x{0:X8}", kpBugcheck);
                        list.Add(new CrashEventItem
                        {
                            Timestamp = r.Time,
                            Category = CrashCategory.BSOD,
                            Source = "Kernel-Power",
                            CodeOrId = hex,
                            SummaryFr = Localizer.Ref("engine.diagnostics_engine.bsod_without_dump", BugCheckCatalog.NameOf(kpBugcheck), hex),
                            SummaryEn = Localizer.Ref("engine.diagnostics_engine.bsod_without_dump", BugCheckCatalog.NameOf(kpBugcheck), hex),
                            DetailsFr = Localizer.Ref("engine.diagnostics_engine.bsod_without_dump_details", r.Msg),
                            DetailsEn = Localizer.Ref("engine.diagnostics_engine.bsod_without_dump_details", r.Msg)
                        });
                    }
                    else if (!hasPairedBsod && kpButton)
                    {
                        list.Add(new CrashEventItem
                        {
                            Timestamp = r.Time,
                            Category = CrashCategory.UnexpectedReboot,
                            Source = "Kernel-Power",
                            CodeOrId = "Event 41",
                            SummaryFr = Localizer.Ref("engine.diagnostics_engine.forced_power_off"),
                            SummaryEn = Localizer.Ref("engine.diagnostics_engine.forced_power_off"),
                            DetailsFr = Localizer.Ref("engine.diagnostics_engine.forced_power_off_details", r.Msg),
                            DetailsEn = Localizer.Ref("engine.diagnostics_engine.forced_power_off_details", r.Msg)
                        });
                    }
                    else if (!hasPairedBsod && kpSleep)
                    {
                        list.Add(new CrashEventItem
                        {
                            Timestamp = r.Time,
                            Category = CrashCategory.UnexpectedReboot,
                            Source = "Kernel-Power",
                            CodeOrId = "Event 41",
                            SummaryFr = Localizer.Ref("engine.diagnostics_engine.power_lost_during_sleep"),
                            SummaryEn = Localizer.Ref("engine.diagnostics_engine.power_lost_during_sleep"),
                            DetailsFr = Localizer.Ref("engine.diagnostics_engine.power_lost_during_sleep_details", r.Msg),
                            DetailsEn = Localizer.Ref("engine.diagnostics_engine.power_lost_during_sleep_details", r.Msg)
                        });
                    }
                    else if (!hasPairedBsod)
                    {
                        list.Add(new CrashEventItem
                        {
                            Timestamp = r.Time,
                            Category = CrashCategory.UnexpectedReboot,
                            Source = "Kernel-Power",
                            CodeOrId = "Event 41",
                            SummaryFr = Localizer.Ref("engine.diagnostics_engine.sudden_reboot_without_minidump_psu_trip"),
                            SummaryEn = Localizer.Ref("engine.diagnostics_engine.sudden_reboot_without_minidump_psu_trip"),
                            DetailsFr = Localizer.Ref("engine.diagnostics_engine.the_pc_lost_power_or_reset", r.Msg),
                            DetailsEn = Localizer.Ref("engine.diagnostics_engine.the_pc_lost_power_or_reset", r.Msg)                        });
                    }
                }
                else if (r.Provider.IndexOf("WHEA-Logger", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string sumFr, sumEn, detFr, detEn;
                    if (r.Id == 18)
                    {
                        sumFr = Localizer.Ref("engine.diagnostics_engine.critical_cpu_hardware_fault_whea_event");
                        sumEn = sumFr;
                        detFr = Localizer.Ref("engine.diagnostics_engine.whea_event_18_means_a_cpu", r.Msg);
                        detEn = detFr;
                    }
                    else if (r.Id == 17)
                    {
                        sumFr = Localizer.Ref("engine.diagnostics_engine.pci_express_bus_error_whea_event");
                        sumEn = sumFr;
                        detFr = Localizer.Ref("engine.diagnostics_engine.whea_event_17_indicates_corrected_uncorr", r.Msg);
                        detEn = detFr;
                    }
                    else
                    {
                        sumFr = Localizer.Ref("engine.diagnostics_engine.hardware_error_reported_whea_logger_id", r.Id);
                        sumEn = sumFr;
                        detFr = Localizer.Ref("engine.diagnostics_engine.low_level_cpu_ram_pcie_hardware", r.Msg);
                        detEn = detFr;
                    }

                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.HardwareWhea,
                        Source = "WHEA-Logger",
                        CodeOrId = "WHEA " + r.Id,
                        SummaryFr = sumFr,
                        SummaryEn = sumEn,
                        DetailsFr = detFr,
                        DetailsEn = detEn
                    });
                }
                else if (r.Provider.IndexOf("stornvme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         r.Provider.IndexOf("storahci", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         r.Provider.Equals("disk", StringComparison.OrdinalIgnoreCase) ||
                         r.Provider.IndexOf("Ntfs", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bool is129 = (r.Id == 129);
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.StorageNvme,
                        Source = r.Provider,
                        CodeOrId = r.Provider + " " + r.Id,
                        SummaryFr = is129
                            ? "Freeze / Reset Contrôleur SSD NVMe (stornvme Event 129 — Bug HMB Windows 11 / ASPM)"
                            : ("Erreur Stockage SSD/Disque (" + r.Provider + " Event " + r.Id + ")"),
                        SummaryEn = is129
                            ? "NVMe SSD Controller Timeout Reset (stornvme Event 129 — Win11 HMB / ASPM Bug)"
                            : ("SSD/Disk Storage Error (" + r.Provider + " Event " + r.Id + ")"),
                        DetailsFr = Localizer.Ref("engine.diagnostics_engine.your_nvme_ssd_stalled_temporarily_freezi", r.Msg),
                        DetailsEn = Localizer.Ref("engine.diagnostics_engine.your_nvme_ssd_stalled_temporarily_freezi", r.Msg)                    });
                }
                else if (r.Provider.IndexOf("Resource-Exhaustion-Detector", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.MemoryExhaustion,
                        Source = "Virtual Memory OOM",
                        CodeOrId = "Event 2004",
                        SummaryFr = Localizer.Ref("engine.diagnostics_engine.windows_virtual_memory_exhaustion_disabl"),
                        SummaryEn = Localizer.Ref("engine.diagnostics_engine.windows_virtual_memory_exhaustion_disabl"),
                        DetailsFr = Localizer.Ref("engine.diagnostics_engine.windows_ran_out_of_committable_virtual", r.Msg),
                        DetailsEn = Localizer.Ref("engine.diagnostics_engine.windows_ran_out_of_committable_virtual", r.Msg)                    });
                }
                else if (r.Provider.Equals("NDIS", StringComparison.OrdinalIgnoreCase) && r.Id == 10400)
                {
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.HardwareWhea,
                        Source = "NDIS Network Reset",
                        CodeOrId = "NDIS 10400",
                        SummaryFr = Localizer.Ref("engine.diagnostics_engine.network_wi_fi_adapter_hardware_reset"),
                        SummaryEn = Localizer.Ref("engine.diagnostics_engine.network_wi_fi_adapter_hardware_reset"),
                        DetailsFr = Localizer.Ref("engine.diagnostics_engine.the_network_adapter_hung_and_was", r.Msg),
                        DetailsEn = Localizer.Ref("engine.diagnostics_engine.the_network_adapter_hung_and_was", r.Msg)                    });
                }
                else if (r.Provider.IndexOf("TPM", StringComparison.OrdinalIgnoreCase) >= 0 && (r.Id == 14 || r.Id == 1796))
                {
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.HardwareWhea,
                        Source = r.Provider,
                        CodeOrId = "TPM " + r.Id,
                        SummaryFr = Localizer.Ref("engine.diagnostics_engine.tpm_2_0_ftpm_security_module", r.Provider, r.Id),
                        SummaryEn = Localizer.Ref("engine.diagnostics_engine.tpm_2_0_ftpm_security_module", r.Provider, r.Id),
                        DetailsFr = Localizer.Ref("engine.diagnostics_engine.tpm_2_0_hardware_or_attestation", r.Msg),
                        DetailsEn = Localizer.Ref("engine.diagnostics_engine.tpm_2_0_hardware_or_attestation", r.Msg)                    });
                }
                else
                {
                    // GPU TDR: nvlddmkm, amdwddmg, amdkmdag, igdkmd, Display 4101
                    string propJoined = string.Join(" | ", r.Props.Where(p => !string.IsNullOrWhiteSpace(p)).Take(4));
                    bool isAmd = r.Provider.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 propJoined.IndexOf("amd", StringComparison.OrdinalIgnoreCase) >= 0;
                    string vendorLabel = isAmd ? "AMD Radeon" : "GPU";
                    if (r.Provider.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0 || propJoined.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0)
                        vendorLabel = "NVIDIA";
                    else if (r.Provider.IndexOf("igdkmd", StringComparison.OrdinalIgnoreCase) >= 0 || r.Provider.IndexOf("igfx", StringComparison.OrdinalIgnoreCase) >= 0)
                        vendorLabel = "Intel Arc / Graphics";

                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.GpuDriver,
                        Source = r.Provider + " (" + vendorLabel + ")",
                        CodeOrId = "Event " + r.Id,
                        SummaryFr = Localizer.Ref("engine.diagnostics_engine.driver_timeout_fault_id", vendorLabel, r.Provider, r.Id, propJoined),
                        SummaryEn = Localizer.Ref("engine.diagnostics_engine.driver_timeout_fault_id", vendorLabel, r.Provider, r.Id, propJoined),
                        DetailsFr = isAmd
                            ? ("Expiration du délai du pilote AMD Radeon (Driver Timeout). Sur Reddit r/AMDHelp, les 3 solutions majeures sont : bloquer l'écrasement du pilote par Windows Update, désactiver ULPS (EnableUlps=0) et désactiver MPO.\n\nDétails : " + (string.IsNullOrEmpty(r.Msg) ? propJoined : r.Msg))
                            : ("Le pilote graphique NVIDIA a cessé de répondre (TDR). Souvent causé par MPO + Skia Graphite en vidéo Chrome/YouTube, ou un undervolt GPU trop haut entre 750 et 850 mV.\n\nDétails : " + (string.IsNullOrEmpty(r.Msg) ? propJoined : r.Msg)),
                        DetailsEn = isAmd
                            ? ("AMD Radeon Driver Timeout. Top r/AMDHelp solutions: block Windows Update from overwriting Adrenalin, disable ULPS (EnableUlps=0), and disable Windows MPO.\n\nDetails: " + (string.IsNullOrEmpty(r.Msg) ? propJoined : r.Msg))
                            : ("NVIDIA GPU driver hit a timeout/fault (TDR). Commonly triggered by MPO + Chrome Skia Graphite video decoding, or an aggressive GPU undervolt curve at 750–850 mV.\n\nDetails: " + (string.IsNullOrEmpty(r.Msg) ? propJoined : r.Msg))
                    });
                }
            }
        }

        private static void ScanApplicationEvents(DateTime? startTime, List<CrashEventItem> list, HardwareSummary hw)
        {
            string timeClause = "";
            if (startTime.HasValue)
            {
                string utcIso = startTime.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
                timeClause = " and TimeCreated[@SystemTime >= '" + utcIso + "']";
            }

            // Provider filtered in the query so the cap below only counts real application crashes / hangs.
            string xpath = "*[System[Provider[@Name='Application Error' or @Name='Application Hang'] and (EventID=1000 or EventID=1002)" + timeClause + "]]";
            try
            {
                // Newest first: the 80-event cap must drop the oldest crashes, not yesterday's.
                EventLogQuery query = new EventLogQuery("Application", PathType.LogName, xpath) { ReverseDirection = true };
                using (EventLogReader reader = new EventLogReader(query))
                {
                    int count = 0;
                    EventRecord rec;
                    while (count < 80 && (rec = reader.ReadEvent()) != null)
                    {
                        using (rec)
                        {
                            string prov = rec.ProviderName ?? "";
                            if (prov.IndexOf("Application Error", StringComparison.OrdinalIgnoreCase) < 0 &&
                                prov.IndexOf("Application Hang", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            DateTime ts = rec.TimeCreated.HasValue ? rec.TimeCreated.Value.ToLocalTime() : DateTime.MinValue;
                            List<string> props = ExtractProps(rec);
                            string appName = props.Count > 0 ? props[0] : "Application";
                            string modName = props.Count > 3 ? props[3] : "-";
                            string excCode = props.Count > 6 ? props[6] : "";
                            string msg = SafeFormatDescription(rec);

                            bool isHang = rec.Id == 1002;
                            string extraHint = "";
                            if (excCode.IndexOf("c0000005", StringComparison.OrdinalIgnoreCase) >= 0)
                                extraHint = Localizer.Ref(hw.IsIntel13Or14Gen ? "engine.diagnostics_engine.hint_c0000005_intel_13_14" : "engine.diagnostics_engine.hint_c0000005");

                            // Deferred texts (rendered in the display language); the Windows message itself stays as Windows wrote it.
                            string summary = isHang
                                ? Localizer.Ref("engine.diagnostics_engine.app_freeze_title", appName)
                                : Localizer.Ref("engine.diagnostics_engine.app_crash_title", appName, modName);
                            string details = (string.IsNullOrEmpty(excCode) ? "" : Localizer.Ref("engine.diagnostics_engine.exception_code_line", excCode))
                                             + msg + extraHint;
                            list.Add(new CrashEventItem
                            {
                                Timestamp = ts,
                                Category = CrashCategory.AppCrash,
                                Source = appName,
                                CodeOrId = isHang ? "Hang 1002" : ("Crash " + (string.IsNullOrEmpty(excCode) ? "1000" : excCode)),
                                SummaryFr = summary,
                                SummaryEn = summary,
                                DetailsFr = details,
                                DetailsEn = details
                            });
                            count++;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private static void ScanBrowserCrashpad(DateTime? startTime, List<CrashEventItem> list)
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var browserDirs = new[]
            {
                new { Name = "Google Chrome", Path = Path.Combine(localAppData, @"Google\Chrome\User Data\Crashpad\reports") },
                new { Name = "Microsoft Edge", Path = Path.Combine(localAppData, @"Microsoft\Edge\User Data\Crashpad\reports") }
            };

            // Newest first and capped: a browser can keep hundreds of reports. Each dump is parsed once, then its
            // result comes from a small cache keyed by name + size + date (same key idea as the WinDbg cache).
            var files = new List<KeyValuePair<string, FileInfo>>();
            foreach (var b in browserDirs)
            {
                if (!Directory.Exists(b.Path)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(b.Path, "*.dmp"))
                    {
                        FileInfo fi = new FileInfo(file);
                        if (startTime.HasValue && fi.LastWriteTime < startTime.Value) continue;
                        files.Add(new KeyValuePair<string, FileInfo>(b.Name, fi));
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }

            Dictionary<string, string[]> cache = LoadCrashpadCache();
            var seen = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var bf in files.OrderByDescending(x => x.Value.LastWriteTimeUtc).Take(MaxBrowserDumps))
            {
                string browser = bf.Key;
                FileInfo fi = bf.Value;
                string file = fi.FullName;
                try
                {
                    string engine, decoder, url, errorDetail;
                    string cacheKey = fi.Name + "|" + fi.Length + "|" + fi.LastWriteTimeUtc.Ticks;
                    string[] hit;
                    if (cache.TryGetValue(cacheKey, out hit) && hit != null && hit.Length == 4)
                    {
                        engine = hit[0]; decoder = hit[1]; url = hit[2]; errorDetail = hit[3];
                    }
                    else
                    {
                        ParseChromiumDumpMetadata(file, out engine, out decoder, out url, out errorDetail);
                    }
                    seen[cacheKey] = new[] { engine, decoder, url, errorDetail };

                    string extra = "";
                    if (!string.IsNullOrEmpty(engine)) extra += engine;
                    if (!string.IsNullOrEmpty(decoder)) extra += (extra.Length > 0 ? " | " : "") + decoder;
                    if (!string.IsNullOrEmpty(url)) extra += (extra.Length > 0 ? " | " : "") + url;
                    if (string.IsNullOrEmpty(extra)) extra = fi.Name;

                    list.Add(new CrashEventItem
                    {
                        Timestamp = fi.LastWriteTime,
                        Category = CrashCategory.BrowserGpu,
                        Source = browser,
                        CodeOrId = string.IsNullOrEmpty(engine) ? "Crashpad .dmp" : engine,
                        SummaryFr = Localizer.Ref("engine.diagnostics_engine.internal_crash_dump", browser, extra),
                        SummaryEn = Localizer.Ref("engine.diagnostics_engine.internal_crash_dump", browser, extra),
                        DetailsFr = BrowserDumpDetails(file, engine, decoder, url, errorDetail),
                        DetailsEn = BrowserDumpDetails(file, engine, decoder, url, errorDetail)
                    });
                }
                catch (Exception ex) { Log.Warn(ex); }
            }
            SaveCrashpadCache(seen); // only the dumps still present: the cache never grows forever
        }

        private static string CrashpadCachePath
        {
            get { return Path.Combine(Localizer.AppDataDir, "analysis", "crashpad.json"); }
        }

        private static Dictionary<string, string[]> LoadCrashpadCache()
        {
            try
            {
                if (File.Exists(CrashpadCachePath))
                {
                    var d = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(CrashpadCachePath, Encoding.UTF8));
                    if (d != null) return new Dictionary<string, string[]>(d, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        }

        private static void SaveCrashpadCache(Dictionary<string, string[]> entries)
        {
            if (Localizer.ReadOnlySettings) return; // capture harness: read-only
            try { AtomicFile.WriteAllText(CrashpadCachePath, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(entries), Encoding.UTF8); }
            catch (Exception ex) { Log.Warn(ex); }
        }

        /// <summary>Browser dump details as deferred lines (only the lines that have a value).</summary>
        private static string BrowserDumpDetails(string file, string engine, string decoder, string url, string errorDetail)
        {
            return Localizer.Ref("engine.diagnostics_engine.browser_dump_file", file)
                 + (string.IsNullOrEmpty(engine) ? "" : Localizer.Ref("engine.diagnostics_engine.detected_gpu_engine", engine))
                 + (string.IsNullOrEmpty(decoder) ? "" : Localizer.Ref("engine.diagnostics_engine.active_video_decoder", decoder))
                 + (string.IsNullOrEmpty(url) ? "" : Localizer.Ref("engine.diagnostics_engine.active_url_at_crash", url))
                 + (string.IsNullOrEmpty(errorDetail) ? "" : Localizer.Ref("engine.diagnostics_engine.internal_error", errorDetail));
        }

        private static void ParseChromiumDumpMetadata(string filePath, out string engine, out string decoder, out string url, out string errorLine)
        {
            engine = ""; decoder = ""; url = ""; errorLine = "";
            try
            {
                // The annotations sit near the start of a Crashpad dump: the first megabytes are enough.
                byte[] bytes;
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    int len = (int)Math.Min(fs.Length, BrowserDumpReadBytes);
                    bytes = new byte[len];
                    int read = 0, n;
                    while (read < len && (n = fs.Read(bytes, read, len - read)) > 0) read += n;
                }
                string ascii = Encoding.ASCII.GetString(bytes);

                if (ascii.IndexOf("GraphiteDawnD3D11", StringComparison.OrdinalIgnoreCase) >= 0)
                    engine = "GraphiteDawnD3D11 (Skia Graphite)";
                else if (ascii.IndexOf("GpuWatchdog", StringComparison.OrdinalIgnoreCase) >= 0)
                    engine = "GpuWatchdog Timeout";
                else if (ascii.IndexOf("gpu-process", StringComparison.OrdinalIgnoreCase) >= 0)
                    engine = "gpu-process";

                Match mDec = Regex.Match(ascii, @"name=D3D11VideoDecoder:[a-zA-Z0-9:=\[\],]+");
                if (mDec.Success) decoder = mDec.Value;

                Match mUrl = Regex.Match(ascii, @"https?://[a-zA-Z0-9\.\-_/\?=&%]+");
                // Only the site is kept: the page path and query string can hold personal data (searches, tokens).
                if (mUrl.Success) url = Privacy.UrlOrigin(mUrl.Value);

                Match mErr = Regex.Match(ascii, @"\[[0-9:/.]+:ERROR:[^\r\n\0]+");
                if (mErr.Success) errorLine = mErr.Value;
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private static void DecodeBugCheck(string hexCode, out string titleFr, out string titleEn, out string descFr, out string descEn)
        {
            // Exact numeric match (the former suffix match confused e.g. 0x11A with 0x1A).
            uint code = 0;
            string h = (hexCode ?? "").Trim();
            if (h.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) h = h.Substring(2);
            uint.TryParse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code);
            uint baseCode = code & ~0x10000000u;
            string hex = string.Format("0x{0:X8}", code);
            BugCheckInfo bc = BugCheckCatalog.Get(code);
            string name = bc != null ? bc.Name : "BugCheck";

            string famFr, famEn;
            switch (baseCode)
            {
                case 0x116: case 0x117: case 0x119: case 0x10E: case 0x113: case 0xEA: case 0x1E4:
                    famFr = Localizer.Ref("engine.diagnostics_engine.gpu_blue_screen"); famEn = famFr; break;
                case 0x124: case 0x9C: case 0x101: case 0x12B: case 0x80:
                    famFr = Localizer.Ref("engine.diagnostics_engine.fatal_hardware_error"); famEn = famFr; break;
                case 0xEF: case 0x7A: case 0xF4: case 0x154: case 0x24: case 0x7B: case 0x133:
                    famFr = Localizer.Ref("engine.diagnostics_engine.system_storage_blue_screen"); famEn = famFr; break;
                case 0x9F: case 0xA0:
                    famFr = Localizer.Ref("engine.diagnostics_engine.driver_power_blue_screen"); famEn = famFr; break;
                case 0x1A: case 0x4E: case 0x50: case 0x0A: case 0x3B: case 0x19: case 0xC2: case 0xD1: case 0x139: case 0x13A:
                    famFr = Localizer.Ref("engine.diagnostics_engine.memory_driver_blue_screen"); famEn = famFr; break;
                default:
                    famFr = Localizer.Ref("engine.diagnostics_engine.windows_blue_screen"); famEn = famFr; break;
            }
            titleFr = Localizer.Ref("engine.diagnostics_engine.bugcheck_title", famFr, name, hex);
            titleEn = titleFr;

            // Only what the stop code establishes: no assumed symptoms, they differ from one PC to another.
            if (bc != null && bc.ExplanationFr != null)
            {
                descFr = bc.ExplanationFr;
                descEn = bc.ExplanationEn;
            }
            else
            {
                descFr = Localizer.Ref("engine.diagnostics_engine.system_rebooted_from_a_windows_kernel", name, hex);
                descEn = descFr;
            }
        }

        public static void BuildSmartDiagnosis(ScanResult res)
        {
            DateTime? startTime = res.FilterStartTime;
            string pDate = startTime.HasValue ? startTime.Value.ToString("g", Localizer.Culture) : "";
            string period = startTime.HasValue
                ? Localizer.F("diagnostics_engine.since", pDate)
                : Localizer.S("diagnostics_engine.across_all_history");

            bool hasGpu = res.GpuTdrCount > 0 || res.Events.Any(e => e.Category == CrashCategory.BSOD && (e.CodeOrId.IndexOf("00000116", StringComparison.OrdinalIgnoreCase) >= 0 || e.CodeOrId.IndexOf("00000119", StringComparison.OrdinalIgnoreCase) >= 0));
            bool hasAmdTimeout = res.Events.Any(e => e.Category == CrashCategory.GpuDriver && e.Source.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasGraphite = res.Events.Any(e => e.Category == CrashCategory.BrowserGpu && e.CodeOrId.IndexOf("Graphite", StringComparison.OrdinalIgnoreCase) >= 0);
            bool hasNvme = res.StorageNvmeCount > 0;
            bool hasWhea = res.Events.Any(e => e.Category == CrashCategory.HardwareWhea);

            if (res.BsodAndRebootCount == 0 && res.GpuTdrCount == 0 && res.StorageNvmeCount == 0 && res.WheaAndMemCount == 0)
            {
                res.IsHealthy = true;
                if (res.BrowserAndAppCount == 0)
                {
                    res.DiagnosisTitle = Localizer.F("diagnostics_engine.system_is_stable", period);
                    res.DiagnosisBody = Localizer.S("diagnostics_engine.no_blue_screens_gpu_driver_timeouts");
                }
                else
                {
                    res.DiagnosisTitle = Localizer.F("diagnostics_engine.kernel_hardware_stable", period);
                    res.DiagnosisBody = Localizer.S("diagnostics_engine.zero_hardware_faults_or_bsods_detected");
                }
            }
            else if (hasAmdTimeout)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = res.Hardware.IsAmdRx7000
                    ? Localizer.S("diagnostics_engine.rdna_3_issue_detected_radeon_rx")
                    : Localizer.S("diagnostics_engine.issue_detected_amd_radeon_driver_timeout");

                res.DiagnosisBody = Localizer.S("diagnostics_engine.1_in_fixes_disable_fast_startup");
            }
            else if (hasGpu)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.S("diagnostics_engine.issue_detected_graphics_driver_timeout_t");
                res.DiagnosisBody = Localizer.F("diagnostics_engine.gpu_crashes_body",
                    hasGraphite ? Localizer.S("diagnostics_engine.graphite_crashes_also_found") : "");
            }
            else if (hasNvme)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.S("diagnostics_engine.issue_detected_nvme_ssd_controller_reset");
                res.DiagnosisBody = Localizer.S("diagnostics_engine.your_nvme_ssd_logged_controller_timeouts");
            }
            else if (hasWhea)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.S("diagnostics_engine.issue_detected_whea_hardware_faults_cpu");
                res.DiagnosisBody = Localizer.S("diagnostics_engine.inspect_the_whea_events_below_whea");
            }
            else
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.F("diagnostics_engine.critical_events_detected", period);
                res.DiagnosisBody = Localizer.S("diagnostics_engine.click_any_event_in_the_list");
            }
        }

        public static List<RegistryFixItem> InspectAllFixes(bool refreshDiskSizes = false)
        {
            HardwareSummary hw = GetHardwareSummary();
            List<RegistryFixItem> list = new List<RegistryFixItem>();

            // ==================== GROUP 1: GPU ====================
            int? mpoVal = ReadRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode");
            bool mpoFixed = (mpoVal.HasValue && mpoVal.Value == 5);
            list.Add(new RegistryFixItem
            {
                Id = "mpo",
                Group = FixGroup.Gpu,
                VendorTag = "NVIDIA / AMD",
                Title = Localizer.S("diagnostics_engine.disable_windows_multiplane_overlay_mpo"),
                RegPathDisplay = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm  •  OverlayTestMode = 5",
                CurrentValue = mpoFixed
                    ? Localizer.S("diagnostics_engine.mpo_disabled_overlaytestmode_5")
                    : Localizer.S("diagnostics_engine.default_mpo_enabled"),
                Explanation = Localizer.S("diagnostics_engine.eliminates_the_primary_cause_of_black"),
                ApplyBtn = Localizer.S("diagnostics_engine.disable_mpo"),
                RestoreBtn = Localizer.S("diagnostics_engine.restore"),
                Status = mpoFixed ? FixStatus.Applied : FixStatus.Recommended,
                RequiresAdmin = true,
                CanRestore = mpoVal.HasValue,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\Dwm]\r\n\"OverlayTestMode\"=dword:00000005\r\n"
            });

            int? tdrVal = ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay");
            int? tdrDdiVal = ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay");
            bool tdrFixed = (tdrVal.HasValue && tdrVal.Value >= 8);
            list.Add(new RegistryFixItem
            {
                Id = "tdr",
                Group = FixGroup.Gpu,
                VendorTag = "NVIDIA / AMD",
                Title = Localizer.S("diagnostics_engine.extend_gpu_timeout_tolerance_tdrdelay_10"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers  •  TdrDelay = 10, TdrDdiDelay = 10",
                CurrentValue = tdrFixed
                    ? Localizer.F("diagnostics_engine.timeout_set_to_s", tdrVal.Value)
                    : Localizer.S("diagnostics_engine.default_2_seconds_only"),
                Explanation = Localizer.S("diagnostics_engine.prevents_fatal_black_screen_crashes_0x11"),
                ApplyBtn = Localizer.S("diagnostics_engine.set_to_10s"),
                RestoreBtn = Localizer.S("diagnostics_engine.reset_2s"),
                Status = tdrFixed ? FixStatus.Applied : FixStatus.Recommended,
                RequiresAdmin = true,
                CanRestore = tdrVal.HasValue || tdrDdiVal.HasValue,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers]\r\n\"TdrDelay\"=dword:0000000a\r\n\"TdrDdiDelay\"=dword:0000000a\r\n"
            });

            int ulpsFoundCount, ulpsEnabledCount;
            InspectAmdUlps(out ulpsFoundCount, out ulpsEnabledCount);
            bool ulpsDisabled = (ulpsFoundCount > 0 && ulpsEnabledCount == 0);
            list.Add(new RegistryFixItem
            {
                Id = "amd_ulps",
                Group = FixGroup.Gpu,
                VendorTag = "AMD RADEON",
                HardwareTarget = "AMD_GPU",
                Title = Localizer.S("diagnostics_engine.disable_amd_ultra_low_power_state"),
                RegPathDisplay = @"HKLM\...\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000x  •  EnableUlps = 0",
                CurrentValue = ulpsFoundCount == 0
                    ? Localizer.S("diagnostics_engine.no_amd_ulps_key_present")
                    : (ulpsDisabled
                        ? Localizer.S("diagnostics_engine.ulps_disabled_enableulps_0")
                        : Localizer.F("diagnostics_engine.ulps_enabled_adapter", ulpsEnabledCount)),
                Explanation = Localizer.S("diagnostics_engine.on_amd_radeon_gpus_e_g"),
                ApplyBtn = Localizer.S("diagnostics_engine.disable_ulps"),
                RestoreBtn = Localizer.S("risky_drivers.re_enable"),
                Status = ulpsFoundCount == 0 ? FixStatus.Info : (ulpsDisabled ? FixStatus.Applied : (hw.HasAmdGpu ? FixStatus.Recommended : FixStatus.Optional)),
                RequiresAdmin = true,
                CanRestore = ulpsDisabled,
                RegExportContent = ""
            });

            // AMD Crash Defender / Kernel Issue Detection (RDNA 2 / RDNA 3 grey-screen & stutter mitigation)
            bool hasAmdDriverKey;
            bool amdIssueDetOff = InspectAmdIssueDetection(out hasAmdDriverKey);
            if (hw.HasAmdGpu || hasAmdDriverKey)
            {
                list.Add(new RegistryFixItem
                {
                    Id = "amd_issue_detection",
                    Group = FixGroup.Gpu,
                    VendorTag = "AMD RADEON",
                    HardwareTarget = "AMD_GPU",
                    Title = Localizer.S("diagnostics_engine.disable_amd_issue_detection_crash_defend"),
                    RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Services\amdwddmg & amdkmdag  •  EnableIssueDetection = 0",
                    CurrentValue = amdIssueDetOff
                        ? Localizer.S("diagnostics_engine.issue_detection_disabled_enableissuedete")
                        : Localizer.S("diagnostics_engine.enabled_by_default"),
                    Explanation = Localizer.S("diagnostics_engine.on_radeon_rx_6000_7000_rx"),
                    ApplyBtn = Localizer.S("risky_drivers.disable"),
                    RestoreBtn = Localizer.S("diagnostics_engine.default"),
                    Status = amdIssueDetOff ? FixStatus.Applied : (hw.HasAmdGpu && !hw.HasNvidiaGpu ? FixStatus.Recommended : FixStatus.Optional),
                    RequiresAdmin = true,
                    CanRestore = amdIssueDetOff,
                    RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\amdwddmg]\r\n\"EnableIssueDetection\"=dword:00000000\r\n\r\n[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\amdkmdag]\r\n\"EnableIssueDetection\"=dword:00000000\r\n"
                });
            }

            int? wuExcl = ReadRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate");
            int? searchOrd = ReadRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig");
            bool wuLocked = (wuExcl.HasValue && wuExcl.Value == 1);
            list.Add(new RegistryFixItem
            {
                Id = "wu_driver_lock",
                Group = FixGroup.Gpu,
                VendorTag = "AMD / NVIDIA",
                Title = Localizer.S("diagnostics_engine.prevent_windows_update_from_overwriting"),
                RegPathDisplay = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate  •  ExcludeWUDriversInQualityUpdate = 1",
                CurrentValue = wuLocked
                    ? Localizer.S("diagnostics_engine.drivers_locked_against_windows_update")
                    : Localizer.S("diagnostics_engine.default_silent_driver_overwrite_allowed"),
                Explanation = Localizer.S("diagnostics_engine.stops_windows_update_from_silently_repla"),
                ApplyBtn = Localizer.S("diagnostics_engine.lock_drivers"),
                RestoreBtn = Localizer.S("diagnostics_engine.allow"),
                Status = wuLocked ? FixStatus.Applied : (hw.HasAmdGpu ? FixStatus.Recommended : FixStatus.Optional),
                RequiresAdmin = true,
                CanRestore = wuLocked || (searchOrd.HasValue && searchOrd.Value == 0),
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate]\r\n\"ExcludeWUDriversInQualityUpdate\"=dword:00000001\r\n"
            });

            if (refreshDiskSizes || _cachedShaderMb < 0)
                _cachedShaderMb = GetShaderCachesSizeMb();
            double shaderMb = _cachedShaderMb;
            list.Add(new RegistryFixItem
            {
                Id = "shader_cache",
                Group = FixGroup.Gpu,
                VendorTag = "DIRECTX / GPU",
                Title = Localizer.S("diagnostics_engine.purge_compiled_gpu_shader_caches_directx"),
                RegPathDisplay = @"%LOCALAPPDATA%\D3DSCache  •  NVIDIA\DXCache  •  AMD\DxCache",
                CurrentValue = string.Format("{0:F1} MB", shaderMb),
                Explanation = Localizer.S("diagnostics_engine.clears_stale_compiled_shaders_after_driv"),
                ApplyBtn = Localizer.S("diagnostics_engine.purge_cache"),
                RestoreBtn = "",
                Status = shaderMb < 5.0 ? FixStatus.Applied : FixStatus.Optional,
                RequiresAdmin = false,
                CanRestore = false,
                RegExportContent = ""
            });

            bool primaryAmd = hw.HasAmdGpu && !hw.HasNvidiaGpu;
            int? hagsVal = ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
            bool hagsOn = (!hagsVal.HasValue || hagsVal.Value == 2);
            list.Add(new RegistryFixItem
            {
                Id = "hags",
                Group = FixGroup.Gpu,
                VendorTag = primaryAmd ? "AMD RADEON / WDDM" : "WDDM / DLSS",
                Title = Localizer.S("diagnostics_engine.hardware_accelerated_gpu_scheduling_hags"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers  •  HwSchMode",
                CurrentValue = hagsOn
                    ? (primaryAmd
                        ? Localizer.S("diagnostics_engine.enabled_can_cause_grey_screens_timeouts")
                        : Localizer.S("diagnostics_engine.enabled_required_for_dlss_frame_gen"))
                    : Localizer.S("diagnostics_engine.disabled_hwschmode_1"),
                Explanation = primaryAmd
                    ? Localizer.S("diagnostics_engine.on_amd_radeon_rx_6000_7000")
                    : Localizer.S("diagnostics_engine.keep_hags_enabled_on_rtx_40"),
                ApplyBtn = hagsOn
                    ? Localizer.S("risky_drivers.disable")
                    : Localizer.S("diagnostics_engine.enable"),
                RestoreBtn = "",
                Status = primaryAmd ? (!hagsOn ? FixStatus.Applied : FixStatus.Recommended) : (hagsOn ? FixStatus.Applied : FixStatus.Optional),
                RequiresAdmin = true,
                CanRestore = false,
                RegExportContent = ""
            });

            // ==================== GROUP 2: BROWSERS & DISCORD ====================
            string chromeState = GetChromiumSkiaGraphiteStatus(@"Google\Chrome\User Data\Local State");
            bool chromeGraphiteOff = (chromeState == "disabled");
            list.Add(new RegistryFixItem
            {
                Id = "chrome_graphite",
                Group = FixGroup.BrowserApps,
                VendorTag = "CHROME",
                Title = Localizer.S("diagnostics_engine.disable_skia_graphite_in_google_chrome"),
                RegPathDisplay = @"%LOCALAPPDATA%\Google\Chrome\User Data\Local State  •  skia-graphite@5",
                CurrentValue = chromeState == "not_installed"
                    ? Localizer.S("diagnostics_engine.not_installed")
                    : (chromeGraphiteOff
                        ? Localizer.S("diagnostics_engine.skia_graphite_disabled")
                        : Localizer.S("diagnostics_engine.default_skia_graphite_enabled")),
                Explanation = Localizer.S("diagnostics_engine.prevents_graphitedawnd3d11_from_stalling"),
                ApplyBtn = Localizer.S("risky_drivers.disable"),
                RestoreBtn = Localizer.S("diagnostics_engine.restore"),
                Status = chromeGraphiteOff ? FixStatus.Applied : (chromeState == "not_installed" ? FixStatus.Info : FixStatus.Recommended),
                RequiresAdmin = false,
                CanRestore = chromeGraphiteOff,
                RegExportContent = ""
            });

            string edgeState = GetChromiumSkiaGraphiteStatus(@"Microsoft\Edge\User Data\Local State");
            bool edgeGraphiteOff = (edgeState == "disabled");
            list.Add(new RegistryFixItem
            {
                Id = "edge_graphite",
                Group = FixGroup.BrowserApps,
                VendorTag = "EDGE",
                Title = Localizer.S("diagnostics_engine.disable_skia_graphite_in_microsoft_edge"),
                RegPathDisplay = @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State  •  skia-graphite@2",
                CurrentValue = edgeGraphiteOff
                    ? Localizer.S("diagnostics_engine.skia_graphite_disabled")
                    : Localizer.S("diagnostics_engine.default_skia_graphite_enabled"),
                Explanation = Localizer.S("diagnostics_engine.protects_microsoft_edge_from_the_same"),
                ApplyBtn = Localizer.S("risky_drivers.disable"),
                RestoreBtn = Localizer.S("diagnostics_engine.restore"),
                Status = edgeGraphiteOff ? FixStatus.Applied : FixStatus.Optional,
                RequiresAdmin = false,
                CanRestore = edgeGraphiteOff,
                RegExportContent = ""
            });

            string discordStatus = GetDiscordHwAccelStatus();
            bool discordOff = (discordStatus == "disabled");
            list.Add(new RegistryFixItem
            {
                Id = "discord_hwaccel",
                Group = FixGroup.BrowserApps,
                VendorTag = "DISCORD",
                Title = Localizer.S("diagnostics_engine.disable_gpu_hardware_acceleration_in_dis"),
                RegPathDisplay = @"%APPDATA%\discord\settings.json  •  ENABLE_HARDWARE_ACCELERATION = false",
                CurrentValue = discordStatus == "not_installed"
                    ? Localizer.S("diagnostics_engine.discord_not_detected")
                    : (discordOff
                        ? Localizer.S("diagnostics_engine.gpu_acceleration_disabled")
                        : Localizer.S("diagnostics_engine.default_gpu_acceleration_enabled")),
                Explanation = Localizer.S("diagnostics_engine.prevents_overlay_and_p_state_conflicts"),
                ApplyBtn = Localizer.S("risky_drivers.disable"),
                RestoreBtn = Localizer.S("risky_drivers.re_enable"),
                Status = discordOff ? FixStatus.Applied : FixStatus.Optional,
                RequiresAdmin = false,
                CanRestore = discordOff,
                RegExportContent = ""
            });

            // ==================== GROUP 3: NVME, PCIE & USB ====================
            int? hmbVal = ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy");
            bool hmbFixed = (hmbVal.HasValue && (hmbVal.Value == 2 || hmbVal.Value == 0));
            list.Add(new RegistryFixItem
            {
                Id = "nvme_hmb",
                Group = FixGroup.StoragePcie,
                VendorTag = "SSD NVMe",
                Title = Localizer.S("diagnostics_engine.stabilize_nvme_ssd_host_memory_buffer"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device  •  HmbAllocationPolicy = 2",
                CurrentValue = hmbFixed
                    ? Localizer.F("diagnostics_engine.hmb_safely_capped_at_64_mb", hmbVal.Value)
                    : Localizer.S("diagnostics_engine.default_uncapped_hmb_allocation"),
                Explanation = Localizer.S("diagnostics_engine.fixes_total_system_freezes_stornvme_even"),
                ApplyBtn = Localizer.S("diagnostics_engine.apply_fix"),
                RestoreBtn = Localizer.S("diagnostics_engine.restore"),
                Status = hmbFixed ? FixStatus.Applied : FixStatus.Optional,
                RequiresAdmin = true,
                CanRestore = hmbFixed,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Services\\stornvme\\Parameters\\Device]\r\n\"HmbAllocationPolicy\"=dword:00000002\r\n\"IoTimeoutValue\"=dword:0000003c\r\n"
            });

            int aspmAc = GetPowerCfgAcIndex("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5");
            bool aspmOff = (aspmAc == 0);
            list.Add(new RegistryFixItem
            {
                Id = "pcie_aspm",
                Group = FixGroup.StoragePcie,
                VendorTag = "PCIe / ITX",
                Title = Localizer.S("diagnostics_engine.disable_pci_express_link_power_managemen"),
                RegPathDisplay = @"PowerCfg  •  SUB_PCIEXPRESS \ ASPM = 0",
                CurrentValue = aspmOff
                    ? Localizer.S("diagnostics_engine.aspm_disabled_on_ac_power")
                    : Localizer.F("diagnostics_engine.pcie_power_saving_active_level", aspmAc),
                Explanation = Localizer.S("diagnostics_engine.keeps_full_signaling_stability_on_gpu"),
                ApplyBtn = Localizer.S("diagnostics_engine.disable_aspm"),
                RestoreBtn = Localizer.S("diagnostics_engine.moderate"),
                Status = aspmOff ? FixStatus.Applied : FixStatus.Recommended,
                RequiresAdmin = false,
                CanRestore = aspmOff,
                RegExportContent = ""
            });

            int usbAc = GetPowerCfgAcIndex("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226");
            bool usbSuspendOff = (usbAc == 0);
            list.Add(new RegistryFixItem
            {
                Id = "usb_suspend",
                Group = FixGroup.StoragePcie,
                VendorTag = "USB",
                Title = Localizer.S("diagnostics_engine.disable_usb_selective_suspend_on_ac"),
                RegPathDisplay = @"PowerCfg  •  SUB_USB \ USBSELECTIVESUSPEND = 0",
                CurrentValue = usbSuspendOff
                    ? Localizer.S("diagnostics_engine.usb_suspend_disabled")
                    : Localizer.S("diagnostics_engine.default_suspend_enabled"),
                Explanation = Localizer.S("diagnostics_engine.prevents_micro_dropouts_on_wireless_mous"),
                ApplyBtn = Localizer.S("risky_drivers.disable"),
                RestoreBtn = Localizer.S("diagnostics_engine.restore"),
                Status = usbSuspendOff ? FixStatus.Applied : FixStatus.Optional,
                RequiresAdmin = false,
                CanRestore = usbSuspendOff,
                RegExportContent = ""
            });

            // ==================== GROUP 4: SYSTEM & MAINTENANCE ====================
            bool pagefileOk = IsAutomaticPagefileEnabled();
            list.Add(new RegistryFixItem
            {
                Id = "pagefile_auto",
                Group = FixGroup.SystemMaint,
                VendorTag = "RAM / OS",
                Title = Localizer.S("diagnostics_engine.system_managed_virtual_memory_pagefile"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management  •  PagingFiles",
                CurrentValue = pagefileOk
                    ? Localizer.S("diagnostics_engine.automatic_management_active")
                    : Localizer.S("diagnostics_engine.warning_pagefile_disabled_or_restricted"),
                Explanation = Localizer.S("diagnostics_engine.even_with_32gb_ram_many_modern"),
                ApplyBtn = Localizer.S("diagnostics_engine.set_automatic"),
                RestoreBtn = "",
                Status = pagefileOk ? FixStatus.Applied : FixStatus.Recommended,
                RequiresAdmin = true,
                CanRestore = false,
                RegExportContent = ""
            });

            int? dvrVal = ReadRegDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled");
            bool dvrOff = (dvrVal.HasValue && dvrVal.Value == 0);
            list.Add(new RegistryFixItem
            {
                Id = "gamedvr",
                Group = FixGroup.SystemMaint,
                VendorTag = "GAMING",
                Title = Localizer.S("diagnostics_engine.disable_background_xbox_game_dvr_capture"),
                RegPathDisplay = @"HKCU\System\GameConfigStore  •  GameDVR_Enabled = 0",
                CurrentValue = dvrOff
                    ? Localizer.S("diagnostics_engine.game_dvr_disabled")
                    : Localizer.S("diagnostics_engine.default_active_in_background"),
                Explanation = Localizer.S("diagnostics_engine.frees_gpu_encoder_resources_and_prevents"),
                ApplyBtn = Localizer.S("risky_drivers.disable"),
                RestoreBtn = Localizer.S("risky_drivers.re_enable"),
                Status = dvrOff ? FixStatus.Applied : FixStatus.Recommended,
                RequiresAdmin = false,
                CanRestore = dvrOff,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_CURRENT_USER\\System\\GameConfigStore]\r\n\"GameDVR_Enabled\"=dword:00000000\r\n"
            });

            int? hiberVal = ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled");
            bool fastStartupOff = (hiberVal.HasValue && hiberVal.Value == 0);
            list.Add(new RegistryFixItem
            {
                Id = "fast_startup",
                Group = FixGroup.SystemMaint,
                VendorTag = "WINDOWS",
                Title = Localizer.S("diagnostics_engine.disable_windows_fast_startup_hiberboot"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power  •  HiberbootEnabled = 0",
                CurrentValue = fastStartupOff
                    ? Localizer.S("diagnostics_engine.disabled_clean_cold_boots")
                    : Localizer.S("diagnostics_engine.default_kernel_hibernation_active"),
                Explanation = Localizer.S("diagnostics_engine.forces_windows_to_initialize_kernel_and"),
                ApplyBtn = Localizer.S("risky_drivers.disable"),
                RestoreBtn = Localizer.S("risky_drivers.re_enable"),
                Status = fastStartupOff ? FixStatus.Applied : (primaryAmd ? FixStatus.Recommended : FixStatus.Optional),
                RequiresAdmin = true,
                CanRestore = fastStartupOff,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power]\r\n\"HiberbootEnabled\"=dword:00000000\r\n"
            });

            list.Add(new RegistryFixItem
            {
                Id = "sfc_repair",
                Group = FixGroup.SystemMaint,
                VendorTag = "SFC / DISM",
                Title = Localizer.S("diagnostics_engine.verify_system_integrity_sfc_scannow"),
                RegPathDisplay = "sfc /scannow",
                CurrentValue = Localizer.S("diagnostics_engine.repair_tool_ready"),
                Explanation = Localizer.S("diagnostics_engine.opens_an_administrator_console_running_s"),
                ApplyBtn = Localizer.S("diagnostics_engine.run_sfc"),
                RestoreBtn = "",
                Status = FixStatus.Info,
                RequiresAdmin = false,
                CanRestore = false,
                RegExportContent = ""
            });

            // Niche Fix A: Windows 11 24H2/25H2 Alt-Tab Freeze (OverlayMinFPS = 0)
            int? minFpsVal = ReadRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS");
            bool minFpsFixed = (minFpsVal.HasValue && minFpsVal.Value == 0);
            list.Add(new RegistryFixItem
            {
                Id = "dwm_alt_tab",
                Group = FixGroup.Gpu,
                VendorTag = "WIN11 24H2+",
                Title = Localizer.S("diagnostics_engine.fix_windows_11_24h2_game_alt"),
                RegPathDisplay = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm -> OverlayMinFPS",
                CurrentValue = minFpsFixed
                    ? "OverlayMinFPS = 0 (Actif)"
                    : Localizer.S("diagnostics_engine.windows_default_not_set"),
                Explanation = Localizer.S("diagnostics_engine.eliminates_the_3_8_second_partial"),
                ApplyBtn = Localizer.S("diagnostics_engine.fix_alt_tab"),
                RestoreBtn = Localizer.S("diagnostics_engine.default"),
                Status = minFpsFixed ? FixStatus.Applied : FixStatus.Optional,
                RequiresAdmin = true,
                CanRestore = minFpsVal.HasValue,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\Dwm]\r\n\"OverlayMinFPS\"=dword:00000000\r\n"
            });

            // Niche Fix B: Realtek 2.5GbE EEE / Green Ethernet & MediaTek RZ616 Wi-Fi Power-Save Disable
            string nicNames;
            bool nicHasRisk = SystemHealthScanner.InspectNicPowerSavingRisk(out nicNames);
            if (!string.IsNullOrEmpty(nicNames))
            {
                list.Add(new RegistryFixItem
                {
                    Id = "nic_powersave",
                    Group = FixGroup.StoragePcie,
                    VendorTag = "2.5GbE / WIFI",
                    Title = Localizer.S("diagnostics_engine.disable_2_5gbe_eee_green_ethernet"),
                    RegPathDisplay = @"HKLM\...\Class\{4d36e972-...} -> *EEE=0, EnableGreenEthernet=0, PnPCapabilities=24",
                    CurrentValue = nicHasRisk
                        ? (nicNames + " — " + Localizer.S("diagnostics_engine.eee_power_save_active"))
                        : (nicNames + " — " + Localizer.S("diagnostics_engine.stabilized_eee_off")),
                    Explanation = Localizer.S("diagnostics_engine.disables_unstable_power_saving_modes_gre"),
                    ApplyBtn = Localizer.S("diagnostics_engine.stabilize_nic"),
                    RestoreBtn = Localizer.S("diagnostics_engine.default"),
                    Status = nicHasRisk ? FixStatus.Optional : FixStatus.Applied,
                    RequiresAdmin = true,
                    CanRestore = !nicHasRisk,
                    RegExportContent = ""
                });
            }

            // Detection v2: risky kernel drivers, and build-gated fixes (by build number, not the "24H2" label).
            list.AddRange(Detection.RiskyDrivers.FixItems());
            if (Detection.WindowsRelease.Current.Build < 26100) list.RemoveAll(f => f.Id == "dwm_alt_tab");
            // HMB fix only where it can matter (Windows 11 24H2+ with an NVMe drive); kept when applied so it stays undoable.
            if (!hmbFixed && !Detection.NvmeDrives.HmbIssueApplies) list.RemoveAll(f => f.Id == "nvme_hmb");

            return list;
        }

        /// <summary>Operations that write to HKLM / services / power plan and therefore need elevation.</summary>
        public static bool OpNeedsAdmin(string opName)
        {
            return Detection.RiskyDrivers.IsOp(opName) || opName == "crashdump_apply" || Optimize.OptOps.NeedsAdmin(opName) ||
                   opName == "mpo_apply" || opName == "mpo_restore" ||
                                  opName == "dwm_alt_tab_apply" || opName == "dwm_alt_tab_restore" ||
                                  opName == "nic_powersave_apply" || opName == "nic_powersave_restore" ||
                                  opName == "tdr_apply" || opName == "tdr_restore" ||
                                  opName == "amd_ulps_apply" || opName == "amd_ulps_restore" ||
                                  opName == "amd_issue_detection_apply" || opName == "amd_issue_detection_restore" ||
                                  opName == "wu_driver_lock_apply" || opName == "wu_driver_lock_restore" ||
                                  opName == "nvme_hmb_apply" || opName == "nvme_hmb_restore" ||
                                  opName == "pagefile_auto_apply" ||
                                  opName == "hags_apply" ||
                                  opName == "disable_risky_oem" || opName == "disable_ryzen_igpu" ||
                                  opName == "fast_startup_apply" || opName == "fast_startup_restore" ||
                                  opName == "restore_snapshot" || opName == "create_restore_point";
        }

        public static bool ExecuteFixOperation(string opName, out string messageFr, out string messageEn)
        {
            if (OpNeedsAdmin(opName) && !IsAdministrator())
            {
                int timeout = opName == "create_restore_point" ? 60000 : 30000;
                ElevationOutcome outcome = Elevation.RunSelf("--elevated-op " + opName, timeout);
                switch (outcome)
                {
                    case ElevationOutcome.Success:
                        messageFr = Localizer.Ref("engine.diagnostics_engine.windows_registry_updated_successfully_a");
                        messageEn = messageFr;
                        return true;
                    case ElevationOutcome.Cancelled:
                        messageFr = Localizer.Ref("engine.diagnostics_engine.operation_cancelled_at_the_uac_administr");
                        messageEn = messageFr;
                        return false;
                    case ElevationOutcome.TimedOut:
                        messageFr = Localizer.Ref("engine.diagnostics_engine.the_administrator_operation_did_not_comp");
                        messageEn = messageFr;
                        return false;
                    default:
                        messageFr = Localizer.Ref("engine.diagnostics_engine.the_administrator_operation_failed_see_l");
                        messageEn = messageFr;
                        return false;
                }
            }
            return ExecuteFixOperationInProc(opName, out messageFr, out messageEn);
        }

        /// <summary>Runs one fix in this process (caller must already hold the rights it needs).</summary>
        internal static bool ExecuteFixOperationInProc(string opName, out string messageFr, out string messageEn)
        {
            messageFr = Localizer.Ref("engine.diagnostics_engine.operation_completed_successfully");
            messageEn = messageFr;

            try
            {
                if (opName != "restore_snapshot" && opName != "create_restore_point")
                {
                    BackupManager.EnsureInitialSnapshotSaved();
                }
                if (Optimize.OptOps.IsOp(opName))
                    return Optimize.OptOps.Execute(opName, out messageFr, out messageEn);
                if (Detection.RiskyDrivers.IsOp(opName))
                    return Detection.RiskyDrivers.Execute(opName, out messageFr, out messageEn);
                if (opName == "crashdump_apply")
                    return Detection.DetectionChecks.EnableCrashDumps(out messageFr, out messageEn);
                switch (opName)
                {
                    case "mpo_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", 5);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.multiplane_overlay_mpo_disabled_overlayt");
                        messageEn = messageFr;
                        return true;

                    case "mpo_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.mpo_restored_to_default");
                        messageEn = messageFr;
                        return true;

                    case "dwm_alt_tab_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS", 0);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.windows_11_24h2_alt_tab_freeze");
                        messageEn = messageFr;
                        return true;

                    case "dwm_alt_tab_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.overlayminfps_restored_to_default");
                        messageEn = messageFr;
                        return true;

                    case "nic_powersave_apply":
                        SetNicPowerSaving(false);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.network_eee_green_ethernet_power_off");
                        messageEn = messageFr;
                        return true;

                    case "nic_powersave_restore":
                        SetNicPowerSaving(true);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.network_power_saving_restored_to_default");
                        messageEn = messageFr;
                        return true;

                    case "tdr_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay", 10);
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay", 10);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.tdr_timeout_set_to_10_seconds");
                        messageEn = messageFr;
                        return true;

                    case "tdr_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay");
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.tdr_timeout_restored_to_2s_default");
                        messageEn = messageFr;
                        return true;

                    case "amd_ulps_apply":
                        SetAmdUlps(0);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.amd_ulps_disabled_enableulps_0_reboot");
                        messageEn = messageFr;
                        return true;

                    case "amd_ulps_restore":
                        SetAmdUlps(1);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.amd_ulps_re_enabled_enableulps_1");
                        messageEn = messageFr;
                        return true;

                    case "amd_issue_detection_apply":
                        SetAmdIssueDetection(false);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.amd_issue_detection_disabled_enableissue");
                        messageEn = messageFr;
                        return true;

                    case "amd_issue_detection_restore":
                        SetAmdIssueDetection(true);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.amd_issue_detection_restored_to_default");
                        messageEn = messageFr;
                        return true;

                    case "wu_driver_lock_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1);
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.automatic_windows_update_driver_overwrit");
                        messageEn = messageFr;
                        return true;

                    case "wu_driver_lock_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate");
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 1);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.windows_update_driver_downloads_re_enabl");
                        messageEn = messageFr;
                        return true;

                    case "shader_cache_apply":
                        int removed = PurgeShaderCaches();
                        _cachedShaderMb = -1;
                        messageFr = Localizer.Ref("engine.diagnostics_engine.cleanup_finished_compiled_shader_cache_f", removed);
                        messageEn = messageFr;
                        return true;

                    case "hags_apply":
                        int? curHags = ReadRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode");
                        int nextHags = (!curHags.HasValue || curHags.Value == 2) ? 1 : 2;
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", nextHags);
                        messageFr = nextHags == 2 ? "✓ HAGS activé (HwSchMode = 2). Redémarrez le PC." : "✓ HAGS désactivé (HwSchMode = 1). Redémarrez le PC.";
                        messageEn = nextHags == 2 ? "✓ HAGS enabled (HwSchMode = 2). Reboot PC." : "✓ HAGS disabled (HwSchMode = 1). Reboot PC.";
                        return true;

                    case "chrome_graphite_apply":
                        return SetChromiumSkiaGraphite(@"Google\Chrome\User Data\Local State", "skia-graphite@5", true, "Chrome", out messageFr, out messageEn);

                    case "chrome_graphite_restore":
                        return SetChromiumSkiaGraphite(@"Google\Chrome\User Data\Local State", "skia-graphite@5", false, "Chrome", out messageFr, out messageEn);

                    case "edge_graphite_apply":
                        return SetChromiumSkiaGraphite(@"Microsoft\Edge\User Data\Local State", "skia-graphite@2", true, "Edge", out messageFr, out messageEn);

                    case "edge_graphite_restore":
                        return SetChromiumSkiaGraphite(@"Microsoft\Edge\User Data\Local State", "skia-graphite@2", false, "Edge", out messageFr, out messageEn);

                    case "discord_hwaccel_apply":
                        return SetDiscordHwAccel(false, out messageFr, out messageEn);

                    case "discord_hwaccel_restore":
                        return SetDiscordHwAccel(true, out messageFr, out messageEn);

                    case "nvme_hmb_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy", 2);
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "IoTimeoutValue", 60);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.nvme_ssd_hmb_protection_64mb_cap");
                        messageEn = messageFr;
                        return true;

                    case "nvme_hmb_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy");
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "IoTimeoutValue");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.nvme_hmb_settings_restored_to_default");
                        messageEn = messageFr;
                        return true;

                    case "pcie_aspm_apply":
                        SetPowerAc("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.pcie_link_state_power_management_aspm");
                        messageEn = messageFr;
                        return true;

                    case "pcie_aspm_restore":
                        SetPowerAc("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 1);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.pcie_aspm_restored_to_moderate");
                        messageEn = messageFr;
                        return true;

                    case "usb_suspend_apply":
                        SetPowerAc("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.usb_selective_suspend_disabled_on_ac");
                        messageEn = messageFr;
                        return true;

                    case "usb_suspend_restore":
                        SetPowerAc("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 1);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.usb_selective_suspend_re_enabled");
                        messageEn = messageFr;
                        return true;

                    case "pagefile_auto_apply":
                        using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                        using (RegistryKey mk = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true))
                        {
                            if (mk != null) ChangeRecorder.Registry(mk, "PagingFiles");
                            if (mk != null)
                                mk.SetValue("PagingFiles", new string[] { @"?:\pagefile.sys" }, RegistryValueKind.MultiString);
                        }
                        messageFr = Localizer.Ref("engine.diagnostics_engine.automatic_windows_pagefile_restored_rebo");
                        messageEn = messageFr;
                        return true;

                    case "gamedvr_apply":
                        WriteRegDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                        WriteRegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.xbox_game_dvr_capture_disabled");
                        messageEn = messageFr;
                        return true;

                    case "gamedvr_restore":
                        WriteRegDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1);
                        WriteRegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.xbox_game_dvr_capture_re_enabled");
                        messageEn = messageFr;
                        return true;

                    case "fast_startup_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.windows_fast_startup_disabled");
                        messageEn = messageFr;
                        return true;

                    case "fast_startup_restore":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.windows_fast_startup_re_enabled");
                        messageEn = messageFr;
                        return true;

                    case "sfc_repair_apply":
                        // Crash dumps (%LOCALAPPDATA%\CrashDumps) are left alone: they are the evidence Dump Finder
                        // diagnoses from, and deleting them here used to wipe crashes not analysed yet.
                        if (!Elevation.Launch("cmd.exe", "/k echo === SFC /scannow === & sfc /scannow"))
                        {
                            messageFr = Localizer.Ref("engine.diagnostics_engine.the_sfc_console_was_refused_at");
                            messageEn = messageFr;
                            return false;
                        }
                        messageFr = Localizer.Ref("engine.diagnostics_engine.elevated_sfc_scannow_console_opened");
                        messageEn = messageFr;
                        return true;

                    case "restore_snapshot":
                        bool restOk = BackupManager.RestoreSnapshotElevatedInternal();
                        messageFr = restOk ? "✓ Sauvegarde initiale du Registre restaurée." : "Impossible de restaurer la sauvegarde.";
                        messageEn = restOk ? "✓ Initial Registry snapshot restored." : "Could not restore Registry snapshot.";
                        return restOk;

                    case "create_restore_point":
                        bool rpOk = BackupManager.CreateSystemRestorePointElevatedInternal();
                        messageFr = rpOk ? "✓ Point de restauration Windows créé avec succès." : "La Protection du système est désactivée sur C: ou la limite quotidienne de points est atteinte.";
                        messageEn = rpOk ? "✓ Windows System Restore Point created successfully." : "System Protection is disabled on C: or daily restore point limit reached.";
                        return rpOk;

                    case "disable_risky_oem":
                        string neutralDetails;
                        int stoppedCnt = NeutralizeRiskyOemComponents(out neutralDetails);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.risky_component_s_neutralized", stoppedCnt, neutralDetails);
                        messageEn = messageFr;
                        return true;

                    case "disable_ryzen_igpu":
                        ChangeRecorder.Registry(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\AMD External Events Utility", "Start");
                        RunHiddenCommand("sc.exe", "stop \"AMD External Events Utility\"");
                        RunHiddenCommand("sc.exe", "config \"AMD External Events Utility\" start= disabled");
                        messageFr = Localizer.Ref("engine.diagnostics_engine.amd_external_events_service_stopped_and");
                        messageEn = messageFr;
                        return true;

                    case "disable_autohdr":
                        SetAutoHdr(false);
                        messageFr = Localizer.Ref("engine.diagnostics_engine.auto_hdr_disabled_successfully");
                        messageEn = messageFr;
                        return true;
                }
            }
            catch (Exception ex)
            {
                messageFr = Localizer.Ref("engine.diagnostics_engine.error", ex.Message);
                messageEn = messageFr;
                return false;
            }
            return false;
        }

        private static void InspectAmdUlps(out int foundCount, out int enabledCount)
        {
            foundCount = 0;
            enabledCount = 0;
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey ck = bk.OpenSubKey(GpuClassKeyPath, false))
                {
                    if (ck == null) return;
                    foreach (string sub in ck.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        using (RegistryKey sk = ck.OpenSubKey(sub, false))
                        {
                            if (sk == null) continue;
                            object v = sk.GetValue("EnableUlps");
                            if (v != null)
                            {
                                foundCount++;
                                if (Convert.ToInt32(v) != 0) enabledCount++;
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private static void SetAmdUlps(int val)
        {
            using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey ck = bk.OpenSubKey(GpuClassKeyPath, false))
            {
                if (ck == null) return;
                foreach (string sub in ck.GetSubKeyNames())
                {
                    if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                    using (RegistryKey sk = ck.OpenSubKey(sub, true))
                    {
                        if (sk == null) continue;
                        if (sk.GetValue("EnableUlps") != null)
                        {
                            ChangeRecorder.Registry(sk, "EnableUlps");
                            sk.SetValue("EnableUlps", val, RegistryValueKind.DWord);
                            if (sk.GetValue("EnableUlps_NA") != null)
                            {
                                ChangeRecorder.Registry(sk, "EnableUlps_NA");
                                sk.SetValue("EnableUlps_NA", val.ToString(), RegistryValueKind.String);
                            }
                        }
                    }
                }
            }
        }

        private static bool InspectAmdIssueDetection(out bool hasAmdDriverKey)
        {
            hasAmdDriverKey = false;
            bool disabledFound = false;
            string[] keys = new[]
            {
                @"SYSTEM\CurrentControlSet\Services\amdwddmg",
                @"SYSTEM\CurrentControlSet\Services\amdkmdag"
            };
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                {
                    foreach (string k in keys)
                    {
                        using (RegistryKey sk = bk.OpenSubKey(k, false))
                        {
                            if (sk == null) continue;
                            hasAmdDriverKey = true;
                            object v = sk.GetValue("EnableIssueDetection");
                            if (v != null && Convert.ToInt32(v) == 0)
                                disabledFound = true;
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return disabledFound;
        }

        private static void SetAmdIssueDetection(bool restoreDefault)
        {
            string[] keys = new[]
            {
                @"SYSTEM\CurrentControlSet\Services\amdwddmg",
                @"SYSTEM\CurrentControlSet\Services\amdkmdag"
            };
            using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                foreach (string k in keys)
                {
                    using (RegistryKey sk = bk.OpenSubKey(k, true))
                    {
                        if (sk == null) continue;
                        ChangeRecorder.Registry(sk, "EnableIssueDetection");
                        if (restoreDefault)
                        {
                            if (sk.GetValue("EnableIssueDetection") != null)
                                sk.DeleteValue("EnableIssueDetection", false);
                        }
                        else
                        {
                            sk.SetValue("EnableIssueDetection", 0, RegistryValueKind.DWord);
                        }
                    }
                }
            }
        }

        private static string[] GetShaderCacheDirs()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string localLow = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\LocalLow");
            return new string[]
            {
                Path.Combine(local, "D3DSCache"),
                Path.Combine(local, @"NVIDIA\DXCache"),
                Path.Combine(local, @"NVIDIA\GLCache"),
                Path.Combine(localLow, @"NVIDIA\PerDriverVersion\DXCache"),
                Path.Combine(local, @"AMD\DxCache"),
                Path.Combine(local, @"AMD\DxcCache"),
                Path.Combine(local, @"AMD\GLCache")
            };
        }

        private static double GetShaderCachesSizeMb()
        {
            long bytes = 0;
            foreach (string d in GetShaderCacheDirs())
            {
                if (!Directory.Exists(d)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                    {
                        try { bytes += new FileInfo(f).Length; } catch { }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }
            return bytes / (1024.0 * 1024.0);
        }

        private static int PurgeShaderCaches()
        {
            int count = 0;
            foreach (string d in GetShaderCacheDirs())
            {
                if (!Directory.Exists(d)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(d, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(f); count++; } catch { }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }
            return count;
        }

        private static bool IsAutomaticPagefileEnabled()
        {
            try
            {
                using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey mk = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", false))
                {
                    if (mk == null) return true;
                    object auto = mk.GetValue("AutomaticManagedPagefile");
                    if (auto is int && (int)auto == 1) return true;
                    string[] pf = mk.GetValue("PagingFiles") as string[];
                    if (pf == null || pf.Length == 0) return false;
                    // Entries: "?:\pagefile.sys" (all drives, system managed), "C:\pagefile.sys 0 0" (system managed size)
                    // or "C:\pagefile.sys <initial MB> <max MB>" (fixed). A tiny fixed one (e.g. 16 MB) is exactly the
                    // "pagefile restricted" case this check exists for, so it no longer counts as OK.
                    const int MinFixedMaxMb = 4096;
                    foreach (string entry in pf)
                    {
                        if (string.IsNullOrWhiteSpace(entry) || entry.IndexOf("pagefile.sys", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        string[] parts = entry.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3) return true; // no sizes: system managed
                        int initMb, maxMb;
                        if (!int.TryParse(parts[parts.Length - 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out initMb) ||
                            !int.TryParse(parts[parts.Length - 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out maxMb))
                            return true; // unknown format: do not raise a false alarm
                        if (maxMb == 0 || maxMb >= MinFixedMaxMb) return true;
                    }
                    return false;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return true; }
        }

        private static string GetDiscordHwAccelStatus()
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"discord\settings.json");
            if (!File.Exists(path)) return "not_installed";
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                if (Regex.IsMatch(raw, @"""ENABLE_HARDWARE_ACCELERATION""\s*:\s*false", RegexOptions.IgnoreCase))
                    return "disabled";
                return "enabled";
            }
            catch (Exception ex) { Log.Warn(ex); return "enabled"; }
        }

        private static bool SetDiscordHwAccel(bool enable, out string msgFr, out string msgEn)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"discord\settings.json");
            if (!File.Exists(path))
            {
                msgFr = Localizer.Ref("engine.diagnostics_engine.discord_settings_json_file_not_found");
                msgEn = msgFr;
                return false;
            }

            string raw = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var dict = ser.Deserialize<Dictionary<string, object>>(raw) ?? new Dictionary<string, object>();
            dict["ENABLE_HARDWARE_ACCELERATION"] = enable;
            ChangeRecorder.File(path);
            File.WriteAllText(path, ser.Serialize(dict), new UTF8Encoding(false));

            msgFr = enable
                ? "Accélération matérielle Discord réactivée (redémarrez Discord)."
                : "✓ Accélération matérielle GPU désactivée dans Discord (redémarrez Discord pour appliquer).";
            msgEn = enable
                ? "Discord GPU acceleration re-enabled (restart Discord)."
                : "✓ GPU Hardware Acceleration disabled in Discord (restart Discord to apply).";
            return true;
        }

        private static string GetChromiumSkiaGraphiteStatus(string relLocalState)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relLocalState);
            if (!File.Exists(path)) return "not_installed";
            try
            {
                string raw = File.ReadAllText(path, Encoding.UTF8);
                if (raw.IndexOf("\"skia-graphite@5\"", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    raw.IndexOf("\"skia-graphite@2\"", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "disabled";
                return "default";
            }
            catch (Exception ex) { Log.Warn(ex); return "default"; }
        }

        public static bool IsProcessRunning(string procName)
        {
            try { return Process.GetProcessesByName(procName).Length > 0; }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        public static void CloseProcessGracefully(string procName)
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero) p.CloseMainWindow(); } catch { }
                }
                System.Threading.Thread.Sleep(1000);
                foreach (var p in Process.GetProcessesByName(procName))
                {
                    try { if (!p.HasExited) p.Kill(); } catch { }
                }
                System.Threading.Thread.Sleep(400);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private static bool SetChromiumSkiaGraphite(string relLocalState, string flagToken, bool disableGraphite, string browserLabel, out string msgFr, out string msgEn)
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), relLocalState);
            if (!File.Exists(path))
            {
                msgFr = Localizer.Ref("engine.diagnostics_engine.local_state_file_not_found", browserLabel);
                msgEn = msgFr;
                return false;
            }

            string raw = File.ReadAllText(path, Encoding.UTF8);
            JavaScriptSerializer ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 256 };
            var root = ser.Deserialize<Dictionary<string, object>>(raw);
            if (!root.ContainsKey("browser") || !(root["browser"] is Dictionary<string, object>))
                root["browser"] = new Dictionary<string, object>();

            var browser = (Dictionary<string, object>)root["browser"];
            List<string> flags = new List<string>();
            if (browser.ContainsKey("enabled_labs_experiments") && browser["enabled_labs_experiments"] is IEnumerable)
            {
                foreach (object item in (IEnumerable)browser["enabled_labs_experiments"])
                {
                    string s = Convert.ToString(item);
                    if (!s.StartsWith("skia-graphite", StringComparison.OrdinalIgnoreCase))
                        flags.Add(s);
                }
            }

            if (disableGraphite) flags.Add(flagToken);
            browser["enabled_labs_experiments"] = flags.ToArray();
            ChangeRecorder.File(path);
            File.WriteAllText(path, ser.Serialize(root), new UTF8Encoding(false));

            msgFr = disableGraphite
                ? ("✓ Skia Graphite désactivé dans " + browserLabel + " (" + flagToken + ").")
                : ("Skia Graphite remis par défaut dans " + browserLabel + ".");
            msgEn = disableGraphite
                ? ("✓ Skia Graphite disabled in " + browserLabel + " (" + flagToken + ").")
                : ("Skia Graphite restored to default in " + browserLabel + ".");
            return true;
        }

        public static int? ReadRegDword(RegistryHive hive, string subKey, string valueName)
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (RegistryKey k = baseKey.OpenSubKey(subKey, false))
                {
                    if (k == null) return null;
                    object v = k.GetValue(valueName);
                    if (v == null) return null;
                    return Convert.ToInt32(v);
                }
            }
            catch (Exception ex) { Log.Warn(ex); return null; }
        }

        private static void WriteRegDword(RegistryHive hive, string subKey, string valueName, int val)
        {
            ChangeRecorder.Registry(hive, subKey, valueName);
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey k = baseKey.CreateSubKey(subKey, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                k.SetValue(valueName, val, RegistryValueKind.DWord);
            }
        }

        private static void DeleteRegValue(RegistryHive hive, string subKey, string valueName)
        {
            ChangeRecorder.Registry(hive, subKey, valueName);
            using (RegistryKey baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            using (RegistryKey k = baseKey.OpenSubKey(subKey, true))
            {
                if (k != null) k.DeleteValue(valueName, false);
            }
        }

        /// <summary>Sets an AC power-plan value, recording the previous one for undo.</summary>
        private static void SetPowerAc(string subGuid, string settingGuid, int value)
        {
            ChangeRecorder.Power(subGuid, settingGuid, GetPowerCfgAcIndex(subGuid, settingGuid));
            RunHiddenCommand("powercfg", "/setacvalueindex SCHEME_CURRENT " + subGuid + " " + settingGuid + " " + value);
        }

        private static int GetPowerCfgAcIndex(string subGuid, string settingGuid)
        {
            try
            {
                string output = RunHiddenCommand("powercfg", "/q SCHEME_CURRENT " + subGuid + " " + settingGuid);
                MatchCollection matches = Regex.Matches(output, @"0x([0-9a-fA-F]{8})");
                if (matches.Count >= 1)
                {
                    string acHex = matches.Count >= 2 ? matches[matches.Count - 2].Groups[1].Value : matches[0].Groups[1].Value;
                    return Convert.ToInt32(acHex, 16);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return 0;
        }

        private static string RunHiddenCommand(string exe, string args)
        {
            // Full System32 path, both pipes drained, killed after 15 s (powercfg / sc can hang on a broken service).
            return SysTools.Run(exe, args, 15000).Output;
        }

        private static string SafeFormatDescription(EventRecord rec)
        {
            try { return rec.FormatDescription() ?? ""; }
            catch (Exception ex) { Log.Warn(ex); return ""; }
        }

        private static void SetNicPowerSaving(bool enablePowerSave)
        {
            using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (RegistryKey ck = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}", false))
            {
                if (ck == null) return;
                foreach (string sub in ck.GetSubKeyNames())
                {
                    if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                    using (RegistryKey sk = ck.OpenSubKey(sub, true))
                    {
                        if (sk == null) continue;
                        string desc = Convert.ToString(sk.GetValue("DriverDesc") ?? "").Trim();
                        if (!Regex.IsMatch(desc, @"(Realtek.*2\.5GbE|Realtek.*Gaming|MediaTek.*Wi-Fi|RZ616|MT792|Realtek.*8852)", RegexOptions.IgnoreCase))
                            continue;

                        string valStr = enablePowerSave ? "1" : "0";
                        foreach (string vn in new[] { "*EEE", "AdvancedEEE", "EnableGreenEthernet", "PowerSavingMode", "PnPCapabilities" })
                            ChangeRecorder.Registry(sk, vn);
                        if (sk.GetValue("*EEE") != null) sk.SetValue("*EEE", valStr, RegistryValueKind.String);
                        if (sk.GetValue("AdvancedEEE") != null) sk.SetValue("AdvancedEEE", valStr, RegistryValueKind.String);
                        if (sk.GetValue("EnableGreenEthernet") != null) sk.SetValue("EnableGreenEthernet", valStr, RegistryValueKind.String);
                        if (sk.GetValue("PowerSavingMode") != null) sk.SetValue("PowerSavingMode", valStr, RegistryValueKind.String);
                        sk.SetValue("PnPCapabilities", enablePowerSave ? 0 : 24, RegistryValueKind.DWord);
                    }
                }
            }
        }

        public static int NeutralizeRiskyOemComponents(out string details)
        {
            int stopped = 0;
            List<string> actions = new List<string>();

            string[] procs = new string[] { "GbtCloudMatrix", "PatriotViperRGB", "AAChal" };
            foreach (var p in procs)
            {
                try
                {
                    Process[] running = Process.GetProcessesByName(p);
                    foreach (var proc in running)
                    {
                        proc.Kill();
                        stopped++;
                        actions.Add(p);
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }

            string[] services = new string[] { "GigabyteUpdateService", "cfosspeed" };
            foreach (var s in services)
            {
                try
                {
                    ChangeRecorder.Registry(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\" + s, "Start");
                    RunHiddenCommand("sc.exe", "stop " + s);
                    RunHiddenCommand("sc.exe", "config " + s + " start= disabled");
                    stopped++;
                    actions.Add(s);
                }
                catch (Exception ex) { Log.Warn(ex); }
            }

            details = actions.Count > 0 ? string.Join(", ", actions.ToArray()) : "aucun";
            return stopped;
        }

        public static bool SetAutoHdr(bool enable)
        {
            try
            {
                using (RegistryKey uk = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\DirectX\UserGpuPreferences"))
                {
                    if (uk != null)
                    {
                        string def = Convert.ToString(uk.GetValue("DirectXUserGlobalSettings") ?? "");
                        if (enable)
                        {
                            if (def.IndexOf("AutoHDREnable=", StringComparison.OrdinalIgnoreCase) >= 0)
                                def = Regex.Replace(def, @"AutoHDREnable=\d", "AutoHDREnable=1");
                            else
                                def = (def + ";AutoHDREnable=1;").Trim(';');
                        }
                        else
                        {
                            if (def.IndexOf("AutoHDREnable=", StringComparison.OrdinalIgnoreCase) >= 0)
                                def = Regex.Replace(def, @"AutoHDREnable=\d", "AutoHDREnable=0");
                            else
                                def = (def + ";AutoHDREnable=0;").Trim(';');
                        }
                        ChangeRecorder.Registry(uk, "DirectXUserGlobalSettings");
                        uk.SetValue("DirectXUserGlobalSettings", def, RegistryValueKind.String);
                        return true;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return false;
        }

        private static List<string> ExtractProps(EventRecord rec)
        {
            List<string> list = new List<string>();
            try
            {
                if (rec.Properties != null)
                {
                    foreach (var p in rec.Properties)
                        list.Add(p != null && p.Value != null ? p.Value.ToString() : "");
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        private static string ExtractBugCheckCode(string msg, List<string> props)
        {
            Match m = Regex.Match(msg ?? "", @"0x[0-9a-fA-F]{8}");
            if (m.Success) return m.Value;
            if (props != null && props.Count > 0 && props[0].StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return props[0];
            return "BugCheck";
        }

        private class EventRecordData
        {
            public DateTime Time;
            public int Id;
            public string Provider;
            public string Msg;
            public List<string> Props;
            /// <summary>Named &lt;Data Name="…"&gt; values (only read where the meaning depends on them, e.g. Kernel-Power 41).</summary>
            public Dictionary<string, string> Named;
        }

        /// <summary>Event data by name, from the event XML (the property list alone has no names).</summary>
        private static Dictionary<string, string> NamedEventData(EventRecord rec)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var doc = System.Xml.Linq.XDocument.Parse(rec.ToXml());
                foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "Data"))
                {
                    var name = el.Attribute("Name");
                    if (name != null && !string.IsNullOrEmpty(name.Value)) d[name.Value] = el.Value;
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return d;
        }

        /// <summary>Decimal or 0x-hex number from named event data; 0 when absent or unreadable.</summary>
        internal static long NamedNumber(Dictionary<string, string> named, string key)
        {
            string v;
            if (named == null || !named.TryGetValue(key, out v) || string.IsNullOrWhiteSpace(v)) return 0;
            v = v.Trim();
            long n;
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return long.TryParse(v.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out n) ? n : 0;
            return long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n) ? n : 0;
        }
    }
}
