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
            try
            {
                EventLogQuery query = new EventLogQuery("System", PathType.LogName, xpath);
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
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                                bugCheckTimes.Add(ts);
                            }
                            else if (provider.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0 && id == 41)
                            {
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.IndexOf("nvlddmkm", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("amdwddmg", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("amdkmdag", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("amdkmdap", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("igfx", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("igdkmd", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     (provider.Equals("Display", StringComparison.OrdinalIgnoreCase) && id == 4101))
                            {
                                if (rec.Level == 1 || rec.Level == 2 || rec.Level == 3 || id == 153 || id == 14 || id == 4101)
                                {
                                    rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                                }
                            }
                            else if (provider.IndexOf("WHEA-Logger", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.IndexOf("stornvme", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     provider.IndexOf("storahci", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     (provider.Equals("disk", StringComparison.OrdinalIgnoreCase) && (id == 7 || id == 51 || id == 153)) ||
                                     (provider.IndexOf("Ntfs", StringComparison.OrdinalIgnoreCase) >= 0 && id == 55))
                            {
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.IndexOf("Resource-Exhaustion-Detector", StringComparison.OrdinalIgnoreCase) >= 0 && id == 2004)
                            {
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if (provider.Equals("NDIS", StringComparison.OrdinalIgnoreCase) && id == 10400)
                            {
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                            else if ((provider.IndexOf("TPM", StringComparison.OrdinalIgnoreCase) >= 0) && (id == 14 || id == 1796))
                            {
                                rawRecords.Add(new EventRecordData { Time = ts, Id = id, Provider = provider, Msg = SafeFormatDescription(rec), Props = ExtractProps(rec) });
                            }
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

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
                        DetailsFr = explainFr + "\n\nMessage Windows :\n" + r.Msg,
                        DetailsEn = explainEn + "\n\nWindows Message:\n" + r.Msg
                    });
                }
                else if (r.Provider.IndexOf("Kernel-Power", StringComparison.OrdinalIgnoreCase) >= 0 && r.Id == 41)
                {
                    bool hasPairedBsod = bugCheckTimes.Any(bt => Math.Abs((bt - r.Time).TotalMinutes) <= 3.0);
                    if (!hasPairedBsod)
                    {
                        list.Add(new CrashEventItem
                        {
                            Timestamp = r.Time,
                            Category = CrashCategory.UnexpectedReboot,
                            Source = "Kernel-Power",
                            CodeOrId = "Event 41",
                            SummaryFr = "Redémarrage brutal SANS Minidump (Coupure alim / Curve Optimizer Ryzen / Hard Reset)",
                            SummaryEn = "Sudden reboot WITHOUT Minidump (PSU trip / Ryzen Curve Optimizer / Hard Reset)",
                            DetailsFr = "Le PC a coupé ou redémarré sans que Windows ait pu écrire de fichier .dmp.\nCauses principales sur Reddit : (1) En charge 3D lourde : sécurité alimentation PSU ou câble 12VHPWR mal clipsé ; (2) Au repos sur Ryzen : Curve Optimizer trop négatif ; (3) Extinction forcée au bouton.\n\nMessage Windows :\n" + r.Msg,
                            DetailsEn = "The PC lost power or reset without writing any .dmp minidump.\nTop causes on Reddit: (1) Under heavy 3D load: PSU OCP trip or loose 12VHPWR/PCIe cable; (2) At idle on Ryzen: overly negative Curve Optimizer; (3) Forced physical reset.\n\nWindows Message:\n" + r.Msg
                        });
                    }
                }
                else if (r.Provider.IndexOf("WHEA-Logger", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    string sumFr, sumEn, detFr, detEn;
                    if (r.Id == 18)
                    {
                        sumFr = "Erreur Matérielle CPU Critique : WHEA Event 18 (Cache Hierarchy — Typique Ryzen Curve Optimizer / Idle)";
                        sumEn = "Critical CPU Hardware Fault: WHEA Event 18 (Cache Hierarchy — Classic Ryzen Curve Optimizer / Idle)";
                        detFr = "WHEA Event 18 indique qu'un cœur du processeur a manqué de tension lors d'une transition basse consommation. Sur CPU AMD Ryzen, baissez votre Curve Optimizer négatif dans le BIOS et activez 'Typical Current Idle'.\n\n" + r.Msg;
                        detEn = "WHEA Event 18 means a CPU core starved for voltage during a low-load C-state transition. On AMD Ryzen, relax your negative Curve Optimizer in BIOS and set Power Supply Idle Control to 'Typical Current Idle'.\n\n" + r.Msg;
                    }
                    else if (r.Id == 17)
                    {
                        sumFr = "Erreur Bus PCI-Express : WHEA Event 17 (Câble Riser ITX Gen4/Gen5 ou ASPM PCIe)";
                        sumEn = "PCI-Express Bus Error: WHEA Event 17 (ITX PCIe Riser Cable Signal or PCIe ASPM)";
                        detFr = "WHEA Event 17 signale des erreurs de transmission sur le port PCIe (très fréquent avec un câble riser PCIe en boîtier ITX lorsque le BIOS est en Auto/Gen5 au lieu de Gen4, ou avec ASPM actif).\n\n" + r.Msg;
                        detEn = "WHEA Event 17 indicates corrected/uncorrected AER errors on a PCIe port (very common with SFF PCIe riser cables running at Gen5 instead of Gen4, or aggressive PCIe ASPM).\n\n" + r.Msg;
                    }
                    else
                    {
                        sumFr = "Erreur matérielle détectée (WHEA-Logger ID " + r.Id + ")";
                        sumEn = "Hardware error reported (WHEA-Logger ID " + r.Id + ")";
                        detFr = "Erreur matérielle CPU / RAM / PCIe signalée au noyau.\n\n" + r.Msg;
                        detEn = "Low-level CPU / RAM / PCIe hardware error reported to Windows.\n\n" + r.Msg;
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
                        DetailsFr = "Le SSD NVMe a cessé de répondre temporairement, figeant Windows. Sous Windows 11 24H2+, c'est fréquemment causé par l'allocation HMB sur les SSD sans DRAM (WD SN770/SN580, Lexar, Maxio) ou l'ASPM PCIe. Appliquez le correctif NVMe HMB dans l'onglet Correctifs.\n\n" + r.Msg,
                        DetailsEn = "Your NVMe SSD stalled temporarily, freezing Windows. On Windows 11 24H2+, this is commonly caused by excessive HMB allocation on DRAM-less drives (WD SN770/SN580, Lexar, Maxio) or PCIe ASPM. Apply the NVMe HMB Fix in the Fixes tab.\n\n" + r.Msg
                    });
                }
                else if (r.Provider.IndexOf("Resource-Exhaustion-Detector", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.MemoryExhaustion,
                        Source = "Virtual Memory OOM",
                        CodeOrId = "Event 2004",
                        SummaryFr = "Saturation de la Mémoire Virtuelle Windows (Pagefile insuffisant ou fuite mémoire)",
                        SummaryEn = "Windows Virtual Memory Exhaustion (Disabled/Small Pagefile or RAM Leak)",
                        DetailsFr = "Windows a manqué de mémoire virtuelle validée (Commit Charge), provoquant la fermeture brutale des jeux ou de Chrome. Vérifiez que le Fichier d'échange (Pagefile) est bien en gestion automatique dans l'onglet Correctifs !\n\n" + r.Msg,
                        DetailsEn = "Windows ran out of committable virtual memory, causing games or browsers to crash immediately. Ensure Automatic Windows Pagefile is enabled in the Fixes tab!\n\n" + r.Msg
                    });
                }
                else if (r.Provider.Equals("NDIS", StringComparison.OrdinalIgnoreCase) && r.Id == 10400)
                {
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.HardwareWhea,
                        Source = "NDIS Network Reset",
                        CodeOrId = "NDIS 10400",
                        SummaryFr = "Réinitialisation matérielle Carte Réseau / Wi-Fi (NDIS 10400 — EEE / Veille PCIe)",
                        SummaryEn = "Network / Wi-Fi Adapter Hardware Reset (NDIS 10400 — EEE / PCIe Sleep)",
                        DetailsFr = "L'adaptateur réseau a cessé de répondre et a dû être réinitialisé par Windows (très fréquent sur Realtek 2.5GbE et MediaTek RZ616 avec l'économie d'énergie EEE/Green Ethernet active). Appliquez le correctif Réseau Anti-Freeze dans l'onglet Correctifs.\n\n" + r.Msg,
                        DetailsEn = "The network adapter hung and was reset by Windows (notorious on Realtek 2.5GbE and MediaTek RZ616 when EEE/Green Ethernet power saving is active). Apply the Network Anti-Freeze fix in Fixes.\n\n" + r.Msg
                    });
                }
                else if (r.Provider.IndexOf("TPM", StringComparison.OrdinalIgnoreCase) >= 0 && (r.Id == 14 || r.Id == 1796))
                {
                    list.Add(new CrashEventItem
                    {
                        Timestamp = r.Time,
                        Category = CrashCategory.HardwareWhea,
                        Source = r.Provider,
                        CodeOrId = "TPM " + r.Id,
                        SummaryFr = "Erreur Module Sécurité TPM 2.0 / fTPM (" + r.Provider + " Event " + r.Id + ")",
                        SummaryEn = "TPM 2.0 / fTPM Security Module Error (" + r.Provider + " Event " + r.Id + ")",
                        DetailsFr = "Erreur matérielle ou d'attestation TPM 2.0 (peut causer des micro-saccades fTPM ou le rejet d'anti-cheats comme Vanguard/RICOCHET). Une mise à jour du BIOS AGESA/UEFI corrige généralement ce bug.\n\n" + r.Msg,
                        DetailsEn = "TPM 2.0 hardware or attestation error (can cause fTPM stutters or Vanguard/RICOCHET anti-cheat rejections). Updating motherboard BIOS typically resolves this.\n\n" + r.Msg
                    });
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
                        SummaryFr = "Timeout / Crash du pilote " + vendorLabel + " (" + r.Provider + " ID " + r.Id + " — " + propJoined + ")",
                        SummaryEn = vendorLabel + " Driver Timeout / Fault (" + r.Provider + " ID " + r.Id + " — " + propJoined + ")",
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
                            string extraHintFr = "";
                            string extraHintEn = "";
                            if (excCode.IndexOf("c0000005", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                extraHintFr = hw.IsIntel13Or14Gen
                                    ? "\n\n⚠ Note CPU Intel 13e/14e Gen : L'erreur 0xc0000005 en jeu (notamment Unreal Engine) provient souvent des limites de puissance du BIOS ou d'un microcode antérieur au 0x12F."
                                    : "\n\n💡 Info : 0xc0000005 (Access Violation) indique un accès mémoire invalide (bug du jeu, shader corrompu à purger, ou profil RAM XMP/EXPO limite).";
                                extraHintEn = hw.IsIntel13Or14Gen
                                    ? "\n\n⚠ Intel 13th/14th Gen Note: 0xc0000005 crashes in games (especially Unreal Engine) are frequently caused by Raptor Lake Vmin / BIOS power limits (make sure the BIOS ships microcode 0x12F or later)."
                                    : "\n\n💡 Hint: 0xc0000005 (Access Violation) indicates an invalid memory access (game bug, corrupted shader cache, or borderline XMP/EXPO RAM).";
                            }

                            list.Add(new CrashEventItem
                            {
                                Timestamp = ts,
                                Category = CrashCategory.AppCrash,
                                Source = appName,
                                CodeOrId = isHang ? "Hang 1002" : ("Crash " + (string.IsNullOrEmpty(excCode) ? "1000" : excCode)),
                                SummaryFr = isHang
                                    ? ("Freeze d'application : " + appName + " ne répondait plus")
                                    : ("Crash d'application : " + appName + " (module : " + modName + ")"),
                                SummaryEn = isHang
                                    ? ("Application Freeze: " + appName + " stopped responding")
                                    : ("Application Crash: " + appName + " (faulting module: " + modName + ")"),
                                DetailsFr = (string.IsNullOrEmpty(excCode) ? "" : "Code d'exception : " + excCode + "\n") + msg + extraHintFr,
                                DetailsEn = (string.IsNullOrEmpty(excCode) ? "" : "Exception code: " + excCode + "\n") + msg + extraHintEn
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

            foreach (var b in browserDirs)
            {
                if (!Directory.Exists(b.Path)) continue;
                try
                {
                    foreach (string file in Directory.GetFiles(b.Path, "*.dmp"))
                    {
                        FileInfo fi = new FileInfo(file);
                        if (startTime.HasValue && fi.LastWriteTime < startTime.Value)
                            continue;

                        string engine, decoder, url, errorDetail;
                        ParseChromiumDumpMetadata(file, out engine, out decoder, out url, out errorDetail);

                        string extra = "";
                        if (!string.IsNullOrEmpty(engine)) extra += engine;
                        if (!string.IsNullOrEmpty(decoder)) extra += (extra.Length > 0 ? " | " : "") + decoder;
                        if (!string.IsNullOrEmpty(url)) extra += (extra.Length > 0 ? " | " : "") + url;
                        if (string.IsNullOrEmpty(extra)) extra = fi.Name;

                        list.Add(new CrashEventItem
                        {
                            Timestamp = fi.LastWriteTime,
                            Category = CrashCategory.BrowserGpu,
                            Source = b.Name,
                            CodeOrId = string.IsNullOrEmpty(engine) ? "Crashpad .dmp" : engine,
                            SummaryFr = "Crash interne " + b.Name + " (" + extra + ")",
                            SummaryEn = b.Name + " internal crash dump (" + extra + ")",
                            DetailsFr = "Fichier dump navigateur : " + file + "\n" +
                                        (string.IsNullOrEmpty(engine) ? "" : "Moteur GPU détecté : " + engine + "\n") +
                                        (string.IsNullOrEmpty(decoder) ? "" : "Décodeur vidéo actif : " + decoder + "\n") +
                                        (string.IsNullOrEmpty(url) ? "" : "URL active lors du crash : " + url + "\n") +
                                        (string.IsNullOrEmpty(errorDetail) ? "" : "Erreur interne : " + errorDetail + "\n"),
                            DetailsEn = "Browser dump file: " + file + "\n" +
                                        (string.IsNullOrEmpty(engine) ? "" : "Detected GPU Engine: " + engine + "\n") +
                                        (string.IsNullOrEmpty(decoder) ? "" : "Active Video Decoder: " + decoder + "\n") +
                                        (string.IsNullOrEmpty(url) ? "" : "Active URL at crash: " + url + "\n") +
                                        (string.IsNullOrEmpty(errorDetail) ? "" : "Internal error: " + errorDetail + "\n")
                        });
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
            }
        }

        private static void ParseChromiumDumpMetadata(string filePath, out string engine, out string decoder, out string url, out string errorLine)
        {
            engine = ""; decoder = ""; url = ""; errorLine = "";
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
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
                    famFr = "Écran bleu GPU"; famEn = "GPU blue screen"; break;
                case 0x124: case 0x9C: case 0x101: case 0x12B: case 0x80:
                    famFr = "Erreur matérielle fatale"; famEn = "Fatal hardware error"; break;
                case 0xEF: case 0x7A: case 0xF4: case 0x154: case 0x24: case 0x7B: case 0x133:
                    famFr = "Écran bleu système / stockage"; famEn = "System / storage blue screen"; break;
                case 0x9F: case 0xA0:
                    famFr = "Écran bleu alimentation pilote"; famEn = "Driver power blue screen"; break;
                case 0x1A: case 0x4E: case 0x50: case 0x0A: case 0x3B: case 0x19: case 0xC2: case 0xD1: case 0x139: case 0x13A:
                    famFr = "Écran bleu mémoire / pilote"; famEn = "Memory / driver blue screen"; break;
                default:
                    famFr = "Écran bleu Windows"; famEn = "Windows blue screen"; break;
            }
            titleFr = famFr + " : " + name + " (" + hex + ")";
            titleEn = famEn + ": " + name + " (" + hex + ")";

            // Only what the stop code establishes: no assumed symptoms, they differ from one PC to another.
            if (bc != null && bc.ExplanationFr != null)
            {
                descFr = bc.ExplanationFr;
                descEn = bc.ExplanationEn;
            }
            else
            {
                descFr = "Redémarrage suite à une erreur critique du noyau Windows (" + name + ", " + hex + ").";
                descEn = "System rebooted from a Windows kernel stop error (" + name + ", " + hex + ").";
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
                        messageFr = "✓ Modification enregistrée dans le Registre Windows ! (Un redémarrage du PC est conseillé).";
                        messageEn = "✓ Windows Registry updated successfully! (A PC reboot is recommended).";
                        return true;
                    case ElevationOutcome.Cancelled:
                        messageFr = "Opération annulée au contrôle Administrateur (UAC).";
                        messageEn = "Operation cancelled at the UAC Administrator prompt.";
                        return false;
                    case ElevationOutcome.TimedOut:
                        messageFr = "L'opération Administrateur n'a pas répondu à temps.";
                        messageEn = "The Administrator operation did not complete in time.";
                        return false;
                    default:
                        messageFr = "L'opération Administrateur a échoué (détails dans le journal).";
                        messageEn = "The Administrator operation failed (see log for details).";
                        return false;
                }
            }
            return ExecuteFixOperationInProc(opName, out messageFr, out messageEn);
        }

        /// <summary>Runs one fix in this process (caller must already hold the rights it needs).</summary>
        internal static bool ExecuteFixOperationInProc(string opName, out string messageFr, out string messageEn)
        {
            messageFr = "Opération effectuée avec succès.";
            messageEn = "Operation completed successfully.";

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
                        messageFr = "✓ Multiplane Overlay (MPO) désactivé (OverlayTestMode = 5).";
                        messageEn = "✓ Multiplane Overlay (MPO) disabled (OverlayTestMode = 5).";
                        return true;

                    case "mpo_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode");
                        messageFr = "MPO remis par défaut.";
                        messageEn = "MPO restored to default.";
                        return true;

                    case "dwm_alt_tab_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS", 0);
                        messageFr = "✓ Correctif Alt+Tab Windows 11 24H2+ activé (OverlayMinFPS = 0).";
                        messageEn = "✓ Windows 11 24H2+ Alt+Tab freeze fix applied (OverlayMinFPS = 0).";
                        return true;

                    case "dwm_alt_tab_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS");
                        messageFr = "OverlayMinFPS remis par défaut.";
                        messageEn = "OverlayMinFPS restored to default.";
                        return true;

                    case "nic_powersave_apply":
                        SetNicPowerSaving(false);
                        messageFr = "✓ Économie EEE / Green Ethernet & mise en veille réseau désactivées (redémarrez le PC pour appliquer).";
                        messageEn = "✓ Network EEE / Green Ethernet & power-off disabled (reboot PC to apply).";
                        return true;

                    case "nic_powersave_restore":
                        SetNicPowerSaving(true);
                        messageFr = "Économie d'énergie réseau remise par défaut.";
                        messageEn = "Network power-saving restored to default.";
                        return true;

                    case "tdr_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay", 10);
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay", 10);
                        messageFr = "✓ Délai TDR passé à 10 secondes (TdrDelay = 10 & TdrDdiDelay = 10).";
                        messageEn = "✓ TDR timeout set to 10 seconds (TdrDelay = 10 & TdrDdiDelay = 10).";
                        return true;

                    case "tdr_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay");
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay");
                        messageFr = "Délai TDR remis à 2 secondes par défaut.";
                        messageEn = "TDR timeout restored to 2s default.";
                        return true;

                    case "amd_ulps_apply":
                        SetAmdUlps(0);
                        messageFr = "✓ ULPS AMD désactivé (EnableUlps = 0). Redémarrez le PC pour appliquer.";
                        messageEn = "✓ AMD ULPS disabled (EnableUlps = 0). Reboot PC to apply.";
                        return true;

                    case "amd_ulps_restore":
                        SetAmdUlps(1);
                        messageFr = "ULPS AMD réactivé (EnableUlps = 1).";
                        messageEn = "AMD ULPS re-enabled (EnableUlps = 1).";
                        return true;

                    case "amd_issue_detection_apply":
                        SetAmdIssueDetection(false);
                        messageFr = "✓ AMD Issue Detection désactivé (EnableIssueDetection = 0). Redémarrez le PC.";
                        messageEn = "✓ AMD Issue Detection disabled (EnableIssueDetection = 0). Reboot PC.";
                        return true;

                    case "amd_issue_detection_restore":
                        SetAmdIssueDetection(true);
                        messageFr = "AMD Issue Detection remis par défaut.";
                        messageEn = "AMD Issue Detection restored to default.";
                        return true;

                    case "wu_driver_lock_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1);
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 0);
                        messageFr = "✓ Mises à jour automatiques de pilotes bloquées dans Windows Update !";
                        messageEn = "✓ Automatic Windows Update driver overwrites blocked!";
                        return true;

                    case "wu_driver_lock_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate");
                        WriteRegDword(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", 1);
                        messageFr = "Téléchargement de pilotes via Windows Update réautorisé.";
                        messageEn = "Windows Update driver downloads re-enabled.";
                        return true;

                    case "shader_cache_apply":
                        int removed = PurgeShaderCaches();
                        _cachedShaderMb = -1;
                        messageFr = "✓ Nettoyage terminé : " + removed + " fichier(s) de cache de shaders purgés.";
                        messageEn = "✓ Cleanup finished: " + removed + " compiled shader cache file(s) purged.";
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
                        messageFr = "✓ Protection SSD NVMe HMB (64 Mo + Timeout 60s) activée ! Redémarrez le PC.";
                        messageEn = "✓ NVMe SSD HMB protection (64MB cap + 60s timeout) enabled! Reboot PC.";
                        return true;

                    case "nvme_hmb_restore":
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy");
                        DeleteRegValue(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "IoTimeoutValue");
                        messageFr = "Paramètres NVMe HMB remis par défaut.";
                        messageEn = "NVMe HMB settings restored to default.";
                        return true;

                    case "pcie_aspm_apply":
                        SetPowerAc("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 0);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = "✓ Économie d'énergie PCIe (ASPM) désactivée sur secteur.";
                        messageEn = "✓ PCIe Link State Power Management (ASPM) disabled on AC.";
                        return true;

                    case "pcie_aspm_restore":
                        SetPowerAc("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", 1);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = "Économie PCIe remise sur Modérée.";
                        messageEn = "PCIe ASPM restored to Moderate.";
                        return true;

                    case "usb_suspend_apply":
                        SetPowerAc("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 0);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = "✓ Suspension Sélective USB désactivée sur secteur.";
                        messageEn = "✓ USB Selective Suspend disabled on AC.";
                        return true;

                    case "usb_suspend_restore":
                        SetPowerAc("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", 1);
                        RunHiddenCommand("powercfg", "/setactive SCHEME_CURRENT");
                        messageFr = "Suspension Sélective USB réactivée.";
                        messageEn = "USB Selective Suspend re-enabled.";
                        return true;

                    case "pagefile_auto_apply":
                        using (RegistryKey bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                        using (RegistryKey mk = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management", true))
                        {
                            if (mk != null) ChangeRecorder.Registry(mk, "PagingFiles");
                            if (mk != null)
                                mk.SetValue("PagingFiles", new string[] { @"?:\pagefile.sys" }, RegistryValueKind.MultiString);
                        }
                        messageFr = "✓ Fichier d'échange (Pagefile) rétabli en gestion automatique ! Redémarrez le PC.";
                        messageEn = "✓ Automatic Windows Pagefile restored! Reboot PC.";
                        return true;

                    case "gamedvr_apply":
                        WriteRegDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
                        WriteRegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0);
                        messageFr = "✓ Capture Xbox Game DVR désactivée.";
                        messageEn = "✓ Xbox Game DVR capture disabled.";
                        return true;

                    case "gamedvr_restore":
                        WriteRegDword(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 1);
                        WriteRegDword(RegistryHive.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1);
                        messageFr = "Capture Xbox Game DVR réactivée.";
                        messageEn = "Xbox Game DVR capture re-enabled.";
                        return true;

                    case "fast_startup_apply":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 0);
                        messageFr = "✓ Démarrage Rapide (Fast Startup) désactivé.";
                        messageEn = "✓ Windows Fast Startup disabled.";
                        return true;

                    case "fast_startup_restore":
                        WriteRegDword(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", 1);
                        messageFr = "Démarrage Rapide réactivé.";
                        messageEn = "Windows Fast Startup re-enabled.";
                        return true;

                    case "sfc_repair_apply":
                        // Crash dumps (%LOCALAPPDATA%\CrashDumps) are left alone: they are the evidence Dump Finder
                        // diagnoses from, and deleting them here used to wipe crashes not analysed yet.
                        if (!Elevation.Launch("cmd.exe", "/k echo === SFC /scannow === & sfc /scannow"))
                        {
                            messageFr = "La console SFC a été refusée au contrôle UAC.";
                            messageEn = "The SFC console was refused at the UAC prompt.";
                            return false;
                        }
                        messageFr = "✓ Console Administrateur SFC /scannow ouverte.";
                        messageEn = "✓ Elevated SFC /scannow console opened.";
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
                        messageFr = string.Format("✓ {0} composant(s) à risque neutralisé(s) ({1}).", stoppedCnt, neutralDetails);
                        messageEn = string.Format("✓ {0} risky component(s) neutralized ({1}).", stoppedCnt, neutralDetails);
                        return true;

                    case "disable_ryzen_igpu":
                        ChangeRecorder.Registry(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\AMD External Events Utility", "Start");
                        RunHiddenCommand("sc.exe", "stop \"AMD External Events Utility\"");
                        RunHiddenCommand("sc.exe", "config \"AMD External Events Utility\" start= disabled");
                        messageFr = "✓ Service AMD External Events arrêté et désactivé.";
                        messageEn = "✓ AMD External Events service stopped and disabled.";
                        return true;

                    case "disable_autohdr":
                        SetAutoHdr(false);
                        messageFr = "✓ Auto HDR désactivé avec succès.";
                        messageEn = "✓ Auto HDR disabled successfully.";
                        return true;
                }
            }
            catch (Exception ex)
            {
                messageFr = "Erreur : " + ex.Message;
                messageEn = "Error: " + ex.Message;
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
                msgFr = "Fichier settings.json de Discord introuvable.";
                msgEn = "Discord settings.json file not found.";
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
                msgFr = "Fichier Local State de " + browserLabel + " introuvable.";
                msgEn = browserLabel + " Local State file not found.";
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
        }
    }
}
