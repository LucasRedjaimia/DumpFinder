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
                    using (var s = new ManagementObjectSearcher("SELECT Name FROM Win32_Processor"))
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
                    using (var s = new ManagementObjectSearcher("SELECT Name, DriverVersion FROM Win32_VideoController"))
                    {
                        foreach (ManagementObject mo in s.Get())
                        {
                            string name = Convert.ToString(mo["Name"]).Trim();
                            if (name.IndexOf("spacedesk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Remote", StringComparison.OrdinalIgnoreCase) >= 0)
                                continue;

                            if (name.IndexOf("AMD", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Radeon", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hw.HasAmdGpu = true;
                                if (Regex.IsMatch(name, @"RX\s*7[6789]\d{2}", RegexOptions.IgnoreCase))
                                    hw.IsAmdRx7000 = true;
                            }

                            if (name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0)
                                hw.HasNvidiaGpu = true;

                            if (name.IndexOf("Intel", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                name.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0)
                                hw.HasIntelArcGpu = true;

                            if (hw.GpuName == "Unknown GPU" ||
                                name.IndexOf("NVIDIA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Radeon RX", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                name.IndexOf("Arc", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                hw.GpuName = name;
                                hw.GpuDriver = Convert.ToString(mo["DriverVersion"]).Trim();
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }

                try
                {
                    using (var s = new ManagementObjectSearcher("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
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
                    using (var s = new ManagementObjectSearcher("SELECT Caption, BuildNumber, LastBootUpTime FROM Win32_OperatingSystem"))
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
                using (var s = new ManagementObjectSearcher("SELECT LastBootUpTime FROM Win32_OperatingSystem"))
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

            string xpath = "*[System[(EventID=1000 or EventID=1002)" + timeClause + "]]";
            try
            {
                EventLogQuery query = new EventLogQuery("Application", PathType.LogName, xpath);
                using (EventLogReader reader = new EventLogReader(query))
                {
                    int count = 0;
                    EventRecord rec;
                    while ((rec = reader.ReadEvent()) != null && count < 80)
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
                if (mUrl.Success) url = mUrl.Value;

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
            string pDate = startTime.HasValue ? startTime.Value.ToString("dd/MM/yyyy HH:mm") : "";
            string period = startTime.HasValue
                ? Localizer.L("depuis le " + pDate, "since " + pDate, "desde el " + pDate, "seit " + pDate, "dal " + pDate, "desde " + pDate)
                : Localizer.L("sur tout l'historique", "across all history", "en todo el historial", "im gesamten Verlauf", "in tutta la cronologia", "em todo o histórico");

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
                    res.DiagnosisTitle = Localizer.L(
                        "Système stable (" + period + ")",
                        "System is stable (" + period + ")",
                        "Sistema estable (" + period + ")",
                        "System ist stabil (" + period + ")",
                        "Sistema stabile (" + period + ")",
                        "Sistema estável (" + period + ")");
                    res.DiagnosisBody = Localizer.L(
                        "Aucun écran bleu, aucun timeout de pilote GPU, aucune erreur SSD NVMe ou matérielle (WHEA) ni aucun crash navigateur détecté.",
                        "No Blue Screens, GPU driver timeouts, NVMe stalls, WHEA hardware faults, or browser crashes were detected.",
                        "No se detectaron pantallas azules, fallos de controlador GPU, bloqueos NVMe, errores WHEA ni cierres de navegador.",
                        "Keine Bluescreens, GPU-Treiber-Timeouts, NVMe-Aussetzer, WHEA-Hardwarefehler oder Browser-Abstürze erkannt.",
                        "Nessuna schermata blu, timeout driver GPU, blocco NVMe, errore hardware WHEA o crash del browser rilevato.",
                        "Nenhuma tela azul, falha de driver GPU, travamento NVMe, erro WHEA ou falha de navegador detectada.");
                }
                else
                {
                    res.DiagnosisTitle = Localizer.L(
                        "Noyau et matériel stables (" + period + ")",
                        "Kernel & hardware stable (" + period + ")",
                        "Núcleo y hardware estables (" + period + ")",
                        "Kernel & Hardware stabil (" + period + ")",
                        "Kernel e hardware stabili (" + period + ")",
                        "Kernel e hardware estáveis (" + period + ")");
                    res.DiagnosisBody = Localizer.L(
                        "Aucun crash matériel ni écran bleu détecté. Seuls quelques événements logiciels isolés figurent dans l'historique.",
                        "Zero hardware faults or BSODs detected. Only minor isolated application events were logged.",
                        "Cero fallos de hardware o pantallas azules. Solo se registraron eventos aislados de aplicaciones.",
                        "Keine Hardwarefehler oder Bluescreens. Lediglich einzelne Anwendungsereignisse wurden protokolliert.",
                        "Zero errori hardware o BSOD. Sono presenti solo eventi applicativi isolati.",
                        "Zero falhas de hardware ou telas azuis. Apenas eventos isolados de aplicativos foram registrados.");
                }
            }
            else if (hasAmdTimeout)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = res.Hardware.IsAmdRx7000
                    ? Localizer.L(
                        "Instabilité RDNA 3 détectée : Radeon RX 7000 (Écran gris / Driver Timeout)",
                        "RDNA 3 Issue Detected: Radeon RX 7000 (Grey Screen / Driver Timeout)",
                        "Problema RDNA 3 detectado: Radeon RX 7000 (Pantalla gris / Timeout)",
                        "RDNA-3-Problem erkannt: Radeon RX 7000 (Grauer Bildschirm / Timeout)",
                        "Problema RDNA 3 rilevato: Radeon RX 7000 (Schermo grigio / Timeout)",
                        "Problema RDNA 3 detectado: Radeon RX 7000 (Tela cinza / Timeout)")
                    : Localizer.L(
                        "Instabilité détectée : AMD Radeon Driver Timeout (amdwddmg)",
                        "Issue detected: AMD Radeon Driver Timeout (amdwddmg)",
                        "Problema detectado: Tiempo agotado de controlador AMD Radeon",
                        "Problem erkannt: AMD Radeon Treiber-Timeout (amdwddmg)",
                        "Problema rilevato: Timeout del driver AMD Radeon",
                        "Problema detectado: Tempo limite do driver AMD Radeon");

                res.DiagnosisBody = Localizer.L(
                    "1) Dans « Correctifs », désactivez le Démarrage Rapide (cause n°1 de l'écran gris au boot), ULPS, MPO, HAGS, Issue Detection et verrouillez Windows Update. " +
                    "2) En jeu sous forte charge (RX 7800 XT / 7900), vérifiez que la carte est branchée avec 2 câbles PCIe 8-pins séparés (sans dédoubleur pigtail) et plafonnez la Fréquence Max vers 2450–2500 MHz dans Adrenalin.",
                    "1) In 'Fixes', disable Fast Startup (#1 cause of boot grey screens), ULPS, MPO, HAGS, Issue Detection, and lock Windows Update. " +
                    "2) Under heavy game load (RX 7800 XT / 7900), verify 2 separate 8-pin PCIe PSU cables are used (no daisy-chain pigtail) and cap Max Frequency to ~2450–2500 MHz in Adrenalin.",
                    "1) En 'Correcciones', desactive Inicio Rápido (causa n.º 1 de pantalla gris al arrancar), ULPS, MPO, HAGS y bloquee Windows Update. 2) En carga (RX 7800 XT), use 2 cables PCIe de 8 pines separados y limite la frecuencia máx. a ~2500 MHz en Adrenalin.",
                    "1) Unter 'Korrekturen': Schnellstart (Hauptursache für grauen Startbildschirm), ULPS, MPO, HAGS deaktivieren & Windows Update sperren. 2) Unter Last (RX 7800 XT): 2 separate 8-Pin-PCIe-Kabel nutzen und Max-Takt in Adrenalin auf ~2500 MHz begrenzen.",
                    "1) In 'Correzioni', disattiva Avvio Rapido (causa n°1 di schermo grigio all'avvio), ULPS, MPO, HAGS e blocca Windows Update. 2) Sotto carico (RX 7800 XT), usa 2 cavi PCIe 8-pin separati e limita la frequenza max a ~2500 MHz in Adrenalin.",
                    "1) Em 'Correções', desative Inicialização Rápida (causa nº 1 de tela cinza no boot), ULPS, MPO, HAGS e bloqueie o Windows Update. 2) Sob carga (RX 7800 XT), use 2 cabos PCIe 8-pin separados e limite o clock máx. a ~2500 MHz no Adrenalin.");
            }
            else if (hasGpu)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.L(
                    "Instabilité détectée : Pilote graphique / Timeout TDR (0x116)",
                    "Issue detected: Graphics Driver Timeout / TDR (0x116)",
                    "Problema detectado: Fallo del controlador gráfico / TDR (0x116)",
                    "Problem erkannt: Grafiktreiber-Timeout / TDR (0x116)",
                    "Problema rilevato: Timeout driver grafico / TDR (0x116)",
                    "Problema detectado: Falha do driver de vídeo / TDR (0x116)");
                res.DiagnosisBody = Localizer.L(
                    "Les crashs proviennent du pilote graphique (0 défaut CPU/RAM/Alim). " + (hasGraphite ? "Des crashs Skia Graphite ont aussi été détectés dans Chrome. " : "") + "Vérifiez vos protections dans « Correctifs ».",
                    "Crashes originate from the graphics driver (0 CPU/RAM/PSU faults). " + (hasGraphite ? "Skia Graphite video crashes were also found in Chrome. " : "") + "Review protections in 'Fixes'.",
                    "Los fallos provienen del controlador gráfico (0 errores de CPU/RAM). Revise las protecciones recomendadas en 'Correcciones'.",
                    "Die Abstürze stammen vom Grafiktreiber (0 CPU/RAM-Fehler). Prüfen Sie die empfohlenen Schutzmaßnahmen unter 'Korrekturen'.",
                    "I crash provengono dal driver grafico (0 errori CPU/RAM). Controlla le protezioni consigliate in 'Correzioni'.",
                    "Os travamentos vêm do driver de vídeo (0 falhas de CPU/RAM). Verifique as proteções recomendadas em 'Correções'.");
            }
            else if (hasNvme)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.L(
                    "Instabilité détectée : Décrochages contrôleur SSD NVMe (stornvme)",
                    "Issue detected: NVMe SSD Controller Resets (stornvme)",
                    "Problema detectado: Reinicios del controlador SSD NVMe (stornvme)",
                    "Problem erkannt: NVMe-SSD-Controller-Aussetzer (stornvme)",
                    "Problema rilevato: Reset del controller SSD NVMe (stornvme)",
                    "Problema detectado: Reinícios do controlador SSD NVMe (stornvme)");
                res.DiagnosisBody = Localizer.L(
                    "Votre SSD NVMe a enregistré des timeouts. Appliquez « Stabilisation SSD NVMe HMB » et « Désactiver PCIe ASPM » dans l'onglet Correctifs.",
                    "Your NVMe SSD logged controller timeouts. Apply 'NVMe HMB Stabilization' and 'Disable PCIe ASPM' in Fixes.",
                    "Su SSD NVMe registró bloqueos. Aplique 'Estabilización NVMe HMB' y 'Desactivar PCIe ASPM' en Correcciones.",
                    "Ihre NVMe-SSD meldete Timeouts. Aktivieren Sie 'NVMe-HMB-Stabilisierung' und deaktivieren Sie PCIe-ASPM.",
                    "L'SSD NVMe ha registrato timeout. Applica 'Stabilizzazione NVMe HMB' e 'Disattiva PCIe ASPM' in Correzioni.",
                    "Seu SSD NVMe registrou timeouts. Aplique 'Estabilização NVMe HMB' e 'Desativar PCIe ASPM' em Correções.");
            }
            else if (hasWhea)
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.L(
                    "Instabilité détectée : Erreurs matérielles WHEA (CPU / PCIe)",
                    "Issue detected: WHEA Hardware Faults (CPU / PCIe)",
                    "Problema detectado: Errores de hardware WHEA (CPU / PCIe)",
                    "Problem erkannt: WHEA-Hardwarefehler (CPU / PCIe)",
                    "Problema rilevato: Errori hardware WHEA (CPU / PCIe)",
                    "Problema detectado: Erros de hardware WHEA (CPU / PCIe)");
                res.DiagnosisBody = Localizer.L(
                    "Consultez les événements WHEA ci-dessous (WHEA 18 = Curve Optimizer Ryzen ; WHEA 17 = câble Riser PCIe) ainsi que le Guide des pannes.",
                    "Inspect the WHEA events below (WHEA 18 = Ryzen Curve Optimizer; WHEA 17 = PCIe Riser cable) and check the Crash Guide.",
                    "Revise los eventos WHEA abajo (WHEA 18 = Curve Optimizer Ryzen; WHEA 17 = cable Riser PCIe) y consulte la Guía.",
                    "Prüfen Sie die WHEA-Ereignisse unten (WHEA 18 = Ryzen Curve Optimizer; WHEA 17 = PCIe-Riser) sowie den Ratgeber.",
                    "Controlla gli eventi WHEA sotto (WHEA 18 = Curve Optimizer Ryzen; WHEA 17 = cavo Riser PCIe) e la Guida.",
                    "Verifique os eventos WHEA abaixo (WHEA 18 = Curve Optimizer Ryzen; WHEA 17 = cabo Riser PCIe) e o Guia.");
            }
            else
            {
                res.IsHealthy = false;
                res.DiagnosisTitle = Localizer.L(
                    "Événements critiques détectés (" + period + ")",
                    "Critical events detected (" + period + ")",
                    "Eventos críticos detectados (" + period + ")",
                    "Kritische Ereignisse erkannt (" + period + ")",
                    "Eventi critici rilevati (" + period + ")",
                    "Eventos críticos detectados (" + period + ")");
                res.DiagnosisBody = Localizer.L(
                    "Cliquez sur un événement de la liste pour afficher sa cause détaillée et consultez l'onglet Correctifs.",
                    "Click any event in the list below to inspect its root cause and review available 1-click fixes.",
                    "Haga clic en cualquier evento de la lista para ver su causa raíz y revise la pestaña Correcciones.",
                    "Klicken Sie unten auf ein Ereignis, um die Ursache anzuzeigen, und prüfen Sie den Reiter Korrekturen.",
                    "Clicca su un evento nella lista per vederne la causa e controlla la scheda Correzioni.",
                    "Clique em qualquer evento da lista para ver sua causa e revise a aba Correções.");
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
                Title = Localizer.L(
                    "Désactiver le Multiplane Overlay (MPO)",
                    "Disable Windows Multiplane Overlay (MPO)",
                    "Desactivar Multiplane Overlay (MPO)",
                    "Windows Multiplane Overlay (MPO) deaktivieren",
                    "Disattiva Multiplane Overlay (MPO)",
                    "Desativar Multiplane Overlay (MPO)"),
                RegPathDisplay = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm  •  OverlayTestMode = 5",
                CurrentValue = mpoFixed
                    ? Localizer.L("MPO désactivé (OverlayTestMode = 5)", "MPO disabled (OverlayTestMode = 5)", "MPO desactivado (OverlayTestMode = 5)", "MPO deaktiviert (OverlayTestMode = 5)", "MPO disattivato (OverlayTestMode = 5)", "MPO desativado (OverlayTestMode = 5)")
                    : Localizer.L("Par défaut (MPO actif)", "Default (MPO enabled)", "Por defecto (MPO activo)", "Standard (MPO aktiv)", "Predefinito (MPO attivo)", "Padrão (MPO ativo)"),
                Explanation = Localizer.L(
                    "Élimine la cause principale des écrans noirs, clignotements et crashs pilotes sur Chrome, YouTube et Discord, sans perte de FPS en jeu.",
                    "Eliminates the primary cause of black screens, flickering, and driver timeouts in Chrome, YouTube, and Discord with zero FPS loss.",
                    "Elimina la causa principal de pantallas negras y fallos de controlador en Chrome, YouTube y Discord sin perder FPS.",
                    "Behebt die Hauptursache für schwarze Bildschirme und Treiber-Timeouts in Chrome, YouTube und Discord ohne FPS-Verlust.",
                    "Elimina la causa principale di schermi neri e timeout driver su Chrome, YouTube e Discord senza perdita di FPS.",
                    "Elimina a principal causa de telas pretas e falhas de driver no Chrome, YouTube e Discord sem perda de FPS."),
                ApplyBtn = Localizer.L("Désactiver MPO", "Disable MPO", "Desactivar MPO", "MPO deaktivieren", "Disattiva MPO", "Desativar MPO"),
                RestoreBtn = Localizer.L("Par défaut", "Restore", "Restaurar", "Standard", "Ripristina", "Restaurar"),
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
                Title = Localizer.L(
                    "Étendre le délai de tolérance GPU (TdrDelay = 10s)",
                    "Extend GPU Timeout Tolerance (TdrDelay = 10s)",
                    "Ampliar tolerancia de espera GPU (TdrDelay = 10s)",
                    "GPU-Timeout-Toleranz erhöhen (TdrDelay = 10s)",
                    "Estendi tolleranza timeout GPU (TdrDelay = 10s)",
                    "Aumentar tolerância de tempo da GPU (TdrDelay = 10s)"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers  •  TdrDelay = 10, TdrDdiDelay = 10",
                CurrentValue = tdrFixed
                    ? Localizer.L("Délai configuré à " + tdrVal.Value + " s", "Timeout set to " + tdrVal.Value + " s", "Tiempo configurado en " + tdrVal.Value + " s", "Timeout auf " + tdrVal.Value + " s gesetzt", "Timeout impostato a " + tdrVal.Value + " s", "Tempo definido em " + tdrVal.Value + " s")
                    : Localizer.L("Par défaut (2 secondes seulement)", "Default (2 seconds only)", "Por defecto (solo 2 segundos)", "Standard (nur 2 Sekunden)", "Predefinito (solo 2 secondi)", "Padrão (apenas 2 segundos)"),
                Explanation = Localizer.L(
                    "Évite un écran noir fatal (0x116) lorsque la carte graphique met plus de 2 secondes à sortir de veille ou compiler un lourd shader.",
                    "Prevents fatal black-screen crashes (0x116) when the GPU takes slightly over 2 seconds to wake or compile heavy shaders.",
                    "Evita pantallazos negros fatales (0x116) cuando la GPU tarda más de 2 segundos en despertar o compilar shaders.",
                    "Verhindert Abstürze (0x116), wenn die Grafikkarte beim Aufwachen oder Shader-Kompilieren länger als 2 Sekunden benötigt.",
                    "Evita crash a schermo nero (0x116) quando la GPU impiega più di 2 secondi a risvegliarsi o compilare shader.",
                    "Evita telas pretas fatais (0x116) quando a GPU leva mais de 2 segundos para despertar ou compilar shaders."),
                ApplyBtn = Localizer.L("Passer à 10s", "Set to 10s", "Ajustar a 10s", "Auf 10s setzen", "Imposta 10s", "Definir 10s"),
                RestoreBtn = Localizer.L("Remettre 2s", "Reset 2s", "Restaurar 2s", "Auf 2s", "Ripristina 2s", "Restaurar 2s"),
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
                Title = Localizer.L(
                    "Désactiver la veille profonde AMD ULPS",
                    "Disable AMD Ultra Low Power State (ULPS)",
                    "Desactivar ahorro profundo AMD ULPS",
                    "AMD Tiefschlafmodus (ULPS) deaktivieren",
                    "Disattiva risparmio profondo AMD ULPS",
                    "Desativar economia profunda AMD ULPS"),
                RegPathDisplay = @"HKLM\...\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\000x  •  EnableUlps = 0",
                CurrentValue = ulpsFoundCount == 0
                    ? Localizer.L("Aucune clé AMD détectée", "No AMD ULPS key present", "Sin clave AMD detectada", "Kein AMD-Schlüssel vorhanden", "Nessuna chiave AMD rilevata", "Nenhuma chave AMD detectada")
                    : (ulpsDisabled
                        ? Localizer.L("ULPS désactivé (EnableUlps = 0)", "ULPS disabled (EnableUlps = 0)", "ULPS desactivado", "ULPS deaktiviert", "ULPS disattivato", "ULPS desativado")
                        : Localizer.L("ULPS actif (" + ulpsEnabledCount + " adaptateur)", "ULPS enabled (" + ulpsEnabledCount + " adapter)", "ULPS activo (" + ulpsEnabledCount + ")", "ULPS aktiv (" + ulpsEnabledCount + ")", "ULPS attivo (" + ulpsEnabledCount + ")", "ULPS ativo (" + ulpsEnabledCount + ")")),
                Explanation = Localizer.L(
                    "Sur GPU AMD Radeon (ex. RX 7800 XT), l'ULPS coupe trop fortement la tension et provoque des écrans gris/noirs au démarrage ou en sortie de veille.",
                    "On AMD Radeon GPUs (e.g. RX 7800 XT), ULPS drops idle/boot voltage too low and causes grey or black screens at startup and wake.",
                    "En gráficas AMD Radeon, ULPS reduce demasiado el voltaje y provoca pantallas grises o negras al iniciar o despertar.",
                    "Bei AMD-Radeon-GPUs senkt ULPS die Spannung zu stark ab und verursacht graue/schwarze Bildschirme beim Start oder Aufwachen.",
                    "Sulle GPU AMD Radeon, l'ULPS abbassa troppo la tensione causando schermi grigi o neri all'avvio o al risveglio.",
                    "Em GPUs AMD Radeon, o ULPS reduz demais a tensão e causa telas cinzas ou pretas na inicialização ou ao despertar."),
                ApplyBtn = Localizer.L("Désactiver ULPS", "Disable ULPS", "Desactivar ULPS", "ULPS deaktivieren", "Disattiva ULPS", "Desativar ULPS"),
                RestoreBtn = Localizer.L("Réactiver", "Re-enable", "Reactivar", "Aktivieren", "Riattiva", "Reativar"),
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
                    Title = Localizer.L(
                        "Désactiver AMD Issue Detection / Crash Defender (EnableIssueDetection = 0)",
                        "Disable AMD Issue Detection / Crash Defender (EnableIssueDetection = 0)",
                        "Desactivar AMD Issue Detection / Crash Defender (EnableIssueDetection = 0)",
                        "AMD Issue Detection / Crash Defender deaktivieren (EnableIssueDetection = 0)",
                        "Disattiva AMD Issue Detection / Crash Defender (EnableIssueDetection = 0)",
                        "Desativar AMD Issue Detection / Crash Defender (EnableIssueDetection = 0)"),
                    RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Services\amdwddmg & amdkmdag  •  EnableIssueDetection = 0",
                    CurrentValue = amdIssueDetOff
                        ? Localizer.L("Issue Detection désactivé (EnableIssueDetection = 0)", "Issue Detection disabled (EnableIssueDetection = 0)", "Desactivado (0)", "Deaktiviert (0)", "Disattivato (0)", "Desativado (0)")
                        : Localizer.L("Actif par défaut (surveillance agressive)", "Enabled by default", "Activo por defecto", "Standardmäßig aktiv", "Attivo per impostazione predefinita", "Ativo por padrão"),
                    Explanation = Localizer.L(
                        "Sur Radeon RX 6000/7000 (RX 7800 XT / 7900), la détection d'incident d'Adrenalin déclenche parfois de faux timeouts et des écrans gris lors des pics de charge.",
                        "On Radeon RX 6000/7000 (RX 7800 XT / 7900), kernel Issue Detection can trigger false-positive driver timeouts and grey screens during load spikes.",
                        "En Radeon RX 6000/7000, Issue Detection puede provocar falsos timeouts y pantallas grises durante picos de carga.",
                        "Auf Radeon RX 6000/7000 kann Issue Detection bei Lastspitzen falsche Treiber-Timeouts und graue Bildschirme auslösen.",
                        "Su Radeon RX 6000/7000, Issue Detection può causare falsi timeout e schermi grigi durante i picchi di carico.",
                        "Nas Radeon RX 6000/7000, o Issue Detection pode acionar falsos timeouts e telas cinzas em picos de carga."),
                    ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                    RestoreBtn = Localizer.L("Par défaut", "Default", "Por defecto", "Standard", "Predefinito", "Padrão"),
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
                Title = Localizer.L(
                    "Bloquer le remplacement du pilote GPU par Windows Update",
                    "Prevent Windows Update from Overwriting GPU Drivers",
                    "Evitar que Windows Update sobrescriba el controlador GPU",
                    "Überschreiben des GPU-Treibers durch Windows Update verhindern",
                    "Impedisci a Windows Update di sovrascrivere il driver GPU",
                    "Impedir que o Windows Update substitua o driver da GPU"),
                RegPathDisplay = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate  •  ExcludeWUDriversInQualityUpdate = 1",
                CurrentValue = wuLocked
                    ? Localizer.L("Pilotes verrouillés face à Windows Update", "Drivers locked against Windows Update", "Controladores protegidos", "Treiber-Updates gesperrt", "Driver protetti da Windows Update", "Drivers protegidos contra Windows Update")
                    : Localizer.L("Par défaut (remplacement automatique autorisé)", "Default (silent driver overwrite allowed)", "Por defecto (actualización automática permitida)", "Standard (automatisches Überschreiben erlaubt)", "Predefinito (sovrascrittura consentita)", "Padrão (substituição automática permitida)"),
                Explanation = Localizer.L(
                    "Empêche Windows Update d'écraser votre pilote AMD Adrenalin ou NVIDIA par une ancienne version générique incompatible.",
                    "Stops Windows Update from silently replacing your AMD Adrenalin or NVIDIA driver with an older generic build.",
                    "Impide que Windows Update reemplace su controlador AMD Adrenalin o NVIDIA por una versión antigua incompatible.",
                    "Verhindert, dass Windows Update Ihren AMD-Adrenalin- oder NVIDIA-Treiber heimlich durch eine alte Version ersetzt.",
                    "Impedisce a Windows Update di sostituire il driver AMD Adrenalin o NVIDIA con una vecchia versione generica.",
                    "Impede o Windows Update de substituir silenciosamente seu driver AMD Adrenalin ou NVIDIA por uma versão antiga."),
                ApplyBtn = Localizer.L("Verrouiller", "Lock Drivers", "Bloquear", "Sperren", "Blocca", "Bloquear"),
                RestoreBtn = Localizer.L("Autoriser", "Allow", "Permitir", "Erlauben", "Consenti", "Permitir"),
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
                Title = Localizer.L(
                    "Purger les caches de Shaders (DirectX, NVIDIA & AMD)",
                    "Purge Compiled GPU Shader Caches (DirectX, NVIDIA & AMD)",
                    "Limpiar cachés de Shaders (DirectX, NVIDIA y AMD)",
                    "GPU-Shader-Cache leeren (DirectX, NVIDIA & AMD)",
                    "Svuota cache degli Shader GPU (DirectX, NVIDIA e AMD)",
                    "Limpar cache de Shaders da GPU (DirectX, NVIDIA e AMD)"),
                RegPathDisplay = @"%LOCALAPPDATA%\D3DSCache  •  NVIDIA\DXCache  •  AMD\DxCache",
                CurrentValue = string.Format("{0:F1} MB", shaderMb),
                Explanation = Localizer.L(
                    "Élimine les shaders corrompus après une mise à jour de pilote ou de jeu (UE5, CoD, CS2). Les fichiers actifs sont ignorés.",
                    "Clears stale compiled shaders after driver or game updates (UE5, CoD, CS2). Active files in use are safely skipped.",
                    "Elimina shaders corruptos tras actualizar controladores o juegos (UE5, CoD, CS2). Los archivos en uso se omiten.",
                    "Entfernt veraltete Shader nach Treiber- oder Spiel-Updates (UE5, CoD, CS2). Aktive Dateien werden übersprungen.",
                    "Rimuove gli shader obsoleti dopo aggiornamenti driver o giochi (UE5, CoD, CS2). I file in uso vengono ignorati.",
                    "Remove shaders corrompidos após atualizar drivers ou jogos (UE5, CoD, CS2). Arquivos em uso são ignorados."),
                ApplyBtn = Localizer.L("Nettoyer", "Purge Cache", "Limpiar", "Bereinigen", "Pulisci", "Limpar"),
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
                Title = Localizer.L(
                    "Planification GPU à accélération matérielle (HAGS)",
                    "Hardware-Accelerated GPU Scheduling (HAGS)",
                    "Programación de GPU acelerada por hardware (HAGS)",
                    "Hardwarebeschleunigte GPU-Planung (HAGS)",
                    "Pianificazione GPU con accelerazione hardware (HAGS)",
                    "Agendamento de GPU acelerado por hardware (HAGS)"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers  •  HwSchMode",
                CurrentValue = hagsOn
                    ? (primaryAmd
                        ? Localizer.L("Activé (peut causer écrans gris / timeouts sur RX 7000)", "Enabled (can cause grey screens / timeouts on RX 7000)", "Activado (riesgo en RX 7000)", "Aktiviert (Risiko auf RX 7000)", "Attivo (rischio su RX 7000)", "Ativado (risco na RX 7000)")
                        : Localizer.L("Activé (requis pour DLSS Frame Gen)", "Enabled (required for DLSS Frame Gen)", "Activado (necesario para DLSS Frame Gen)", "Aktiviert (für DLSS Frame Gen nötig)", "Attivo (richiesto per DLSS Frame Gen)", "Ativado (necessário para DLSS Frame Gen)"))
                    : Localizer.L("Désactivé (HwSchMode = 1)", "Disabled (HwSchMode = 1)", "Desactivado (HwSchMode = 1)", "Deaktiviert (HwSchMode = 1)", "Disattivato (HwSchMode = 1)", "Desativado (HwSchMode = 1)"),
                Explanation = primaryAmd
                    ? Localizer.L(
                        "Sur AMD Radeon RX 6000/7000 (ex. RX 7800 XT) sous Windows 11 24H2+, désactiver HAGS corrige de nombreux écrans gris et pop-ups Driver Timeout en jeu DirectX 12.",
                        "On AMD Radeon RX 6000/7000 (e.g. RX 7800 XT) with Windows 11 24H2+, disabling HAGS resolves many grey screens and DirectX 12 Driver Timeouts.",
                        "En AMD Radeon RX 6000/7000 (RX 7800 XT), desactivar HAGS corrige pantallas grises y fallos Driver Timeout en juegos DX12.",
                        "Bei AMD Radeon RX 6000/7000 (z. B. RX 7800 XT) behebt das Deaktivieren von HAGS häufig graue Bildschirme und DX12-Treiber-Timeouts.",
                        "Su AMD Radeon RX 6000/7000 (es. RX 7800 XT), disattivare HAGS risolve molti schermi grigi e Driver Timeout in DX12.",
                        "Nas AMD Radeon RX 6000/7000 (ex. RX 7800 XT), desativar o HAGS resolve telas cinzas e Driver Timeouts em jogos DX12.")
                    : Localizer.L(
                        "Gardez HAGS activé sur RTX 40/50 pour le DLSS Frame Generation. Sur GPU AMD Radeon RX 7000 ou cartes plus anciennes subissant des freezes, le désactiver aide.",
                        "Keep HAGS enabled on RTX 40/50 for DLSS Frame Generation. On AMD Radeon RX 7000 or older cards experiencing freezes, disabling it helps.",
                        "Mantenga HAGS activo en RTX 40/50 para DLSS Frame Generation. En Radeon RX 7000 o tarjetas antiguas con bloqueos, desactivarlo ayuda.",
                        "Auf RTX 40/50 für DLSS Frame Generation aktiviert lassen. Bei Radeon RX 7000 oder älteren Karten mit Freezes kann Deaktivieren helfen.",
                        "Mantieni HAGS attivo su RTX 40/50 per DLSS Frame Generation. Su Radeon RX 7000 o schede con freeze può aiutare disattivarlo.",
                        "Mantenha o HAGS ativo nas RTX 40/50 para DLSS Frame Generation. Nas Radeon RX 7000 ou placas com travamentos, desativar ajuda."),
                ApplyBtn = hagsOn
                    ? Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar")
                    : Localizer.L("Activer", "Enable", "Activar", "Aktivieren", "Attiva", "Ativar"),
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
                Title = Localizer.L(
                    "Désactiver Skia Graphite dans Google Chrome",
                    "Disable Skia Graphite in Google Chrome",
                    "Desactivar Skia Graphite en Google Chrome",
                    "Skia Graphite in Google Chrome deaktivieren",
                    "Disattiva Skia Graphite in Google Chrome",
                    "Desativar Skia Graphite no Google Chrome"),
                RegPathDisplay = @"%LOCALAPPDATA%\Google\Chrome\User Data\Local State  •  skia-graphite@5",
                CurrentValue = chromeState == "not_installed"
                    ? Localizer.L("Non installé", "Not installed", "No instalado", "Nicht installiert", "Non installato", "Não instalado")
                    : (chromeGraphiteOff
                        ? Localizer.L("Skia Graphite désactivé", "Skia Graphite disabled", "Skia Graphite desactivado", "Skia Graphite deaktiviert", "Skia Graphite disattivato", "Skia Graphite desativado")
                        : Localizer.L("Par défaut (Skia Graphite actif)", "Default (Skia Graphite enabled)", "Por defecto (activo)", "Standard (aktiv)", "Predefinito (attivo)", "Padrão (ativo)")),
                Explanation = Localizer.L(
                    "Évite le crash de GraphiteDawnD3D11 en lecture vidéo YouTube 4K AV1/VP9 tout en conservant le décodage matériel fluide.",
                    "Prevents GraphiteDawnD3D11 from stalling Direct3D 11 during 4K YouTube playback while keeping hardware video decoding.",
                    "Evita que GraphiteDawnD3D11 bloquee Direct3D 11 al reproducir YouTube 4K manteniendo la decodificación por hardware.",
                    "Verhindert Direct3D-11-Hänger durch GraphiteDawnD3D11 bei 4K-YouTube-Videos bei voller Hardware-Dekodierung.",
                    "Evita il blocco di Direct3D 11 da parte di GraphiteDawnD3D11 nei video YouTube 4K mantenendo la decodifica hardware.",
                    "Evita que o GraphiteDawnD3D11 trave o Direct3D 11 em vídeos 4K do YouTube mantendo a decodificação por hardware."),
                ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                RestoreBtn = Localizer.L("Par défaut", "Restore", "Restaurar", "Standard", "Ripristina", "Restaurar"),
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
                Title = Localizer.L(
                    "Désactiver Skia Graphite dans Microsoft Edge",
                    "Disable Skia Graphite in Microsoft Edge",
                    "Desactivar Skia Graphite en Microsoft Edge",
                    "Skia Graphite in Microsoft Edge deaktivieren",
                    "Disattiva Skia Graphite in Microsoft Edge",
                    "Desativar Skia Graphite no Microsoft Edge"),
                RegPathDisplay = @"%LOCALAPPDATA%\Microsoft\Edge\User Data\Local State  •  skia-graphite@2",
                CurrentValue = edgeGraphiteOff
                    ? Localizer.L("Skia Graphite désactivé", "Skia Graphite disabled", "Skia Graphite desactivado", "Skia Graphite deaktiviert", "Skia Graphite disattivato", "Skia Graphite desativado")
                    : Localizer.L("Par défaut (Skia Graphite actif)", "Default (Skia Graphite enabled)", "Por defecto (activo)", "Standard (aktiv)", "Predefinito (attivo)", "Padrão (ativo)"),
                Explanation = Localizer.L(
                    "Protège Microsoft Edge contre les mêmes timeouts Direct3D 11 que Chrome en lecture vidéo matérielle.",
                    "Protects Microsoft Edge from the same Chromium Direct3D 11 video timeout bugs as Chrome.",
                    "Protege Microsoft Edge de los mismos bloqueos de vídeo Direct3D 11 que Chrome.",
                    "Schützt Microsoft Edge vor denselben Chromium-Direct3D-11-Videofehlern wie Chrome.",
                    "Protegge Microsoft Edge dagli stessi blocchi video Direct3D 11 di Chrome.",
                    "Protege o Microsoft Edge contra os mesmos travamentos de vídeo Direct3D 11 do Chrome."),
                ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                RestoreBtn = Localizer.L("Par défaut", "Restore", "Restaurar", "Standard", "Ripristina", "Restaurar"),
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
                Title = Localizer.L(
                    "Désactiver l'accélération matérielle dans Discord",
                    "Disable GPU Hardware Acceleration in Discord",
                    "Desactivar aceleración por hardware en Discord",
                    "Hardwarebeschleunigung in Discord deaktivieren",
                    "Disattiva accelerazione hardware in Discord",
                    "Desativar aceleração por hardware no Discord"),
                RegPathDisplay = @"%APPDATA%\discord\settings.json  •  ENABLE_HARDWARE_ACCELERATION = false",
                CurrentValue = discordStatus == "not_installed"
                    ? Localizer.L("Discord non détecté", "Discord not detected", "Discord no detectado", "Discord nicht erkannt", "Discord non rilevato", "Discord não detectado")
                    : (discordOff
                        ? Localizer.L("Accélération GPU désactivée", "GPU acceleration disabled", "Aceleración GPU desactivada", "GPU-Beschleunigung deaktiviert", "Accelerazione GPU disattivata", "Aceleração GPU desativada")
                        : Localizer.L("Par défaut (accélération active)", "Default (GPU acceleration enabled)", "Por defecto (activa)", "Standard (aktiv)", "Predefinito (attiva)", "Padrão (ativa)")),
                Explanation = Localizer.L(
                    "Évite les conflits d'overlays et de fréquences GPU lorsque Discord tourne en arrière-plan ou sur un second écran en jeu.",
                    "Prevents overlay and P-state conflicts when Discord runs in the background or on a second monitor while gaming.",
                    "Evita conflictos de superposición y estados de energía cuando Discord está abierto en segundo plano al jugar.",
                    "Vermeidet Overlay- und P-State-Konflikte, wenn Discord beim Spielen im Hintergrund oder auf einem Zweitmonitor läuft.",
                    "Evita conflitti di overlay e frequenze GPU quando Discord è aperto in background durante il gioco.",
                    "Evita conflitos de overlay e estados de energia quando o Discord roda em segundo plano durante jogos."),
                ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                RestoreBtn = Localizer.L("Réactiver", "Re-enable", "Reactivar", "Aktivieren", "Riattiva", "Reativar"),
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
                Title = Localizer.L(
                    "Stabiliser le cache HMB des SSD NVMe (Windows 11 24H2+)",
                    "Stabilize NVMe SSD Host Memory Buffer (Windows 11 24H2+)",
                    "Estabilizar caché HMB de SSD NVMe (Windows 11 24H2+)",
                    "NVMe-SSD Host Memory Buffer stabilisieren (Windows 11 24H2+)",
                    "Stabilizza cache HMB degli SSD NVMe (Windows 11 24H2+)",
                    "Estabilizar cache HMB de SSDs NVMe (Windows 11 24H2+)"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device  •  HmbAllocationPolicy = 2",
                CurrentValue = hmbFixed
                    ? Localizer.L("HMB plafonné à 64 Mo (Policy = " + hmbVal.Value + ")", "HMB safely capped at 64 MB", "HMB limitado a 64 MB", "HMB sicher auf 64 MB begrenzt", "HMB limitato a 64 MB", "HMB limitado a 64 MB")
                    : Localizer.L("Par défaut (allocation non plafonnée)", "Default (uncapped HMB allocation)", "Por defecto (sin límite)", "Standard (unbegrenzt)", "Predefinito (non limitato)", "Padrão (sem limite)"),
                Explanation = Localizer.L(
                    "Corrige les freezes complets stornvme Event 129 et écrans bleus 0xEF sous Windows 11 24H2+ pour les SSD NVMe sans DRAM.",
                    "Fixes total system freezes (stornvme Event 129) and 0xEF BSODs on Windows 11 24H2+ with DRAM-less NVMe SSDs.",
                    "Corrige congelamientos stornvme 129 y pantallazos 0xEF en Windows 11 24H2+ para unidades NVMe sin DRAM.",
                    "Behebt System-Freezes (stornvme 129) und 0xEF-Bluescreens unter Windows 11 24H2+ bei DRAM-losen NVMe-SSDs.",
                    "Risolve i freeze completi stornvme 129 e BSOD 0xEF su Windows 11 24H2+ con SSD NVMe DRAM-less.",
                    "Corrige congelamentos stornvme 129 e telas azuis 0xEF no Windows 11 24H2+ em SSDs NVMe sem DRAM."),
                ApplyBtn = Localizer.L("Stabiliser NVMe", "Apply Fix", "Aplicar", "Anwenden", "Applica", "Aplicar"),
                RestoreBtn = Localizer.L("Par défaut", "Restore", "Restaurar", "Standard", "Ripristina", "Restaurar"),
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
                Title = Localizer.L(
                    "Désactiver l'économie d'énergie du bus PCI-Express (ASPM)",
                    "Disable PCI-Express Link Power Management (ASPM)",
                    "Desactivar ahorro de energía PCI-Express (ASPM)",
                    "PCI-Express-Energiesparmodus (ASPM) deaktivieren",
                    "Disattiva risparmio energetico PCI-Express (ASPM)",
                    "Desativar economia de energia PCI-Express (ASPM)"),
                RegPathDisplay = @"PowerCfg  •  SUB_PCIEXPRESS \ ASPM = 0",
                CurrentValue = aspmOff
                    ? Localizer.L("ASPM désactivé sur secteur", "ASPM disabled on AC power", "ASPM desactivado en corriente", "ASPM im Netzbetrieb deaktiviert", "ASPM disattivato su rete", "ASPM desativado na tomada")
                    : Localizer.L("Économie PCIe active (niveau " + aspmAc + ")", "PCIe power saving active (level " + aspmAc + ")", "Ahorro PCIe activo", "PCIe-Sparmodus aktiv", "Risparmio PCIe attivo", "Economia PCIe ativa"),
                Explanation = Localizer.L(
                    "Maintient le signal électrique stable sur le port GPU et les SSD NVMe. Essentiel en boîtier ITX avec câble Riser PCIe.",
                    "Keeps full signaling stability on GPU and NVMe PCIe lanes. Essential for SFF/ITX builds with PCIe riser cables.",
                    "Mantiene estable la señal PCIe del GPU y SSD NVMe. Esencial en cajas ITX con cable Riser PCIe.",
                    "Hält das PCIe-Signal für GPU und NVMe stabil. Unverzichtbar bei ITX-Gehäusen mit PCIe-Riser-Kabel.",
                    "Mantiene stabile il segnale PCIe di GPU e NVMe. Essenziale nei case ITX con cavo Riser PCIe.",
                    "Mantém estável o sinal PCIe da GPU e NVMe. Essencial em gabinetes ITX com cabo Riser PCIe."),
                ApplyBtn = Localizer.L("Désactiver ASPM", "Disable ASPM", "Desactivar ASPM", "ASPM deaktivieren", "Disattiva ASPM", "Desativar ASPM"),
                RestoreBtn = Localizer.L("Modéré", "Moderate", "Moderado", "Moderat", "Moderato", "Moderado"),
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
                Title = Localizer.L(
                    "Désactiver la suspension sélective USB",
                    "Disable USB Selective Suspend on AC Power",
                    "Desactivar suspensión selectiva USB",
                    "Selektives USB-Energiesparen deaktivieren",
                    "Disattiva sospensione selettiva USB",
                    "Desativar suspensão seletiva USB"),
                RegPathDisplay = @"PowerCfg  •  SUB_USB \ USBSELECTIVESUSPEND = 0",
                CurrentValue = usbSuspendOff
                    ? Localizer.L("Suspension USB désactivée", "USB suspend disabled", "Suspensión USB desactivada", "USB-Energiesparen deaktiviert", "Sospensione USB disattivata", "Suspensão USB desativada")
                    : Localizer.L("Par défaut (suspension active)", "Default (suspend enabled)", "Por defecto (activa)", "Standard (aktiv)", "Predefinito (attiva)", "Padrão (ativa)"),
                Explanation = Localizer.L(
                    "Évite les micro-coupures de souris sans fil, cartes son USB / DAC et manettes sur chipsets AMD et Intel.",
                    "Prevents micro-dropouts on wireless mouse dongles, USB DACs/headsets, and controllers.",
                    "Evita microcortes en ratones inalámbricos, tarjetas de sonido USB / DAC y mandos.",
                    "Verhindert Aussetzer bei kabellosen Mäusen, USB-Soundkarten/DACs und Controllern.",
                    "Evita micro-disconnessioni di mouse wireless, schede audio USB / DAC e controller.",
                    "Evita micro-desconexões em mouses sem fio, placas de som USB / DAC e controles."),
                ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                RestoreBtn = Localizer.L("Par défaut", "Restore", "Restaurar", "Standard", "Ripristina", "Restaurar"),
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
                Title = Localizer.L(
                    "Fichier d'échange (Pagefile) géré automatiquement",
                    "System-Managed Virtual Memory (Pagefile)",
                    "Memoria virtual (Pagefile) automática",
                    "Automatische Auslagerungsdatei (Pagefile)",
                    "Memoria virtuale (Pagefile) automatica",
                    "Memória virtual (Pagefile) automática"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management  •  PagingFiles",
                CurrentValue = pagefileOk
                    ? Localizer.L("Gestion automatique active", "Automatic management active", "Gestión automática activa", "Automatische Verwaltung aktiv", "Gestione automatica attiva", "Gerenciamento automático ativo")
                    : Localizer.L("Attention : Pagefile désactivé ou bridé", "Warning: Pagefile disabled or restricted", "Atención: Pagefile desactivado", "Warnung: Pagefile deaktiviert", "Attenzione: Pagefile disattivato", "Atenção: Pagefile desativado"),
                Explanation = Localizer.L(
                    "Même avec 32 ou 64 Go de RAM, de nombreux jeux récents plantent immédiatement si le fichier d'échange Windows est désactivé.",
                    "Even with 32GB+ RAM, many modern games crash immediately if the Windows Pagefile is manually disabled.",
                    "Incluso con 32 GB+ de RAM, muchos juegos actuales se cierran si el archivo de paginación está desactivado.",
                    "Selbst mit 32+ GB RAM stürzen viele moderne Spiele sofort ab, wenn die Auslagerungsdatei deaktiviert wurde.",
                    "Anche con 32+ GB di RAM, molti giochi moderni vanno in crash se il file di paging è disattivato.",
                    "Mesmo com 32+ GB de RAM, muitos jogos modernos fecham na hora se o arquivo de paginação estiver desativado."),
                ApplyBtn = Localizer.L("Activer Auto", "Set Automatic", "Activar Auto", "Automatisch", "Imposta Auto", "Ativar Auto"),
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
                Title = Localizer.L(
                    "Désactiver l'enregistrement Xbox Game DVR en arrière-plan",
                    "Disable Background Xbox Game DVR Capture",
                    "Desactivar grabación Xbox Game DVR en segundo plano",
                    "Xbox Game DVR Hintergrundaufnahme deaktivieren",
                    "Disattiva registrazione Xbox Game DVR in background",
                    "Desativar gravação Xbox Game DVR em segundo plano"),
                RegPathDisplay = @"HKCU\System\GameConfigStore  •  GameDVR_Enabled = 0",
                CurrentValue = dvrOff
                    ? Localizer.L("Game DVR désactivé", "Game DVR disabled", "Game DVR desactivado", "Game DVR deaktiviert", "Game DVR disattivato", "Game DVR desativado")
                    : Localizer.L("Par défaut (actif en arrière-plan)", "Default (active in background)", "Por defecto (activo)", "Standard (im Hintergrund aktiv)", "Predefinito (attivo)", "Padrão (ativo)"),
                Explanation = Localizer.L(
                    "Libère l'encodeur vidéo du GPU et évite les micro-saccades tout en gardant le Mode Jeu Windows actif.",
                    "Frees GPU encoder resources and prevents capture micro-stutters while keeping Windows Game Mode enabled.",
                    "Libera el codificador de la GPU y evita tirones manteniendo activo el Modo Juego de Windows.",
                    "Entlastet den GPU-Encoder und verhindert Mikroruckler, während der Windows-Spielmodus aktiv bleibt.",
                    "Libera l'encoder della GPU ed evita micro-scatti mantenendo attiva la Modalità Gioco.",
                    "Libera o codificador da GPU e evita engasgos mantendo o Modo de Jogo do Windows ativo."),
                ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                RestoreBtn = Localizer.L("Réactiver", "Re-enable", "Reactivar", "Aktivieren", "Riattiva", "Reativar"),
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
                Title = Localizer.L(
                    "Désactiver le Démarrage rapide (Fast Startup)",
                    "Disable Windows Fast Startup (Hiberboot)",
                    "Desactivar Inicio rápido de Windows",
                    "Windows-Schnellstart (Hiberboot) deaktivieren",
                    "Disattiva Avvio rapido di Windows",
                    "Desativar Inicialização rápida do Windows"),
                RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power  •  HiberbootEnabled = 0",
                CurrentValue = fastStartupOff
                    ? Localizer.L("Désactivé (démarrage propre)", "Disabled (clean cold boots)", "Desactivado (arranque limpio)", "Deaktiviert (sauberer Neustart)", "Disattivato (avvio pulito)", "Desativado (boot limpo)")
                    : Localizer.L("Par défaut (hibernation noyau active)", "Default (kernel hibernation active)", "Por defecto (hibernación activa)", "Standard (Kernel-Ruhezustand aktiv)", "Predefinito (ibernazione attiva)", "Padrão (hibernação ativa)"),
                Explanation = Localizer.L(
                    "Force Windows à recharger proprement le noyau et le pilote GPU à chaque mise sous tension (élimine l'écran gris au démarrage sur Radeon RX 7000 / RX 7800 XT).",
                    "Forces Windows to initialize kernel and GPU drivers cleanly on every cold boot (eliminates the boot grey screen on Radeon RX 7000 / RX 7800 XT).",
                    "Obliga a Windows a recargar limpiamente el núcleo y el controlador GPU en cada encendido (elimina la pantalla gris al iniciar en RX 7000).",
                    "Zwingt Windows, Kernel und GPU-Treiber bei jedem Kaltstart frisch zu laden (behebt den grauen Startbildschirm bei Radeon RX 7000).",
                    "Forza Windows a ricaricare in modo pulito kernel e driver GPU ad ogni accensione (elimina lo schermo grigio all'avvio su RX 7000).",
                    "Força o Windows a recarregar o kernel e o driver da GPU de forma limpa (elimina a tela cinza na inicialização nas Radeon RX 7000)."),
                ApplyBtn = Localizer.L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                RestoreBtn = Localizer.L("Réactiver", "Re-enable", "Reactivar", "Aktivieren", "Riattiva", "Reativar"),
                Status = fastStartupOff ? FixStatus.Applied : (primaryAmd ? FixStatus.Recommended : FixStatus.Optional),
                RequiresAdmin = true,
                CanRestore = fastStartupOff,
                RegExportContent = "Windows Registry Editor Version 5.00\r\n\r\n[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Power]\r\n\"HiberbootEnabled\"=dword:00000000\r\n"
            });

            string crashDumpDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
            int dumpFiles = 0;
            double dumpMb = 0;
            if (Directory.Exists(crashDumpDir))
            {
                try
                {
                    var files = Directory.GetFiles(crashDumpDir, "*.dmp");
                    dumpFiles = files.Length;
                    dumpMb = files.Sum(f => new FileInfo(f).Length) / (1024.0 * 1024.0);
                }
                catch (Exception ex) { Log.Warn(ex); }
            }
            list.Add(new RegistryFixItem
            {
                Id = "sfc_repair",
                Group = FixGroup.SystemMaint,
                VendorTag = "SFC / DISM",
                Title = Localizer.L(
                    "Vérifier l'intégrité système (SFC /scannow) & nettoyer CrashDumps",
                    "Verify System Integrity (SFC /scannow) & Clean CrashDumps",
                    "Verificar archivos del sistema (SFC /scannow) y limpiar CrashDumps",
                    "Systemdateien prüfen (SFC /scannow) & CrashDumps bereinigen",
                    "Verifica integrità di sistema (SFC /scannow) e pulisci CrashDumps",
                    "Verificar integridade do sistema (SFC /scannow) e limpar CrashDumps"),
                RegPathDisplay = string.Format("DISM + sfc /scannow  •  CrashDumps: {0} ({1:F1} MB)", dumpFiles, dumpMb),
                CurrentValue = dumpFiles == 0
                    ? Localizer.L("0 dump local — Outil prêt", "0 local dumps — Repair tool ready", "0 volcados — Herramienta lista", "0 lokale Dumps — Bereit", "0 dump locali — Pronto", "0 dumps locais — Pronto")
                    : string.Format("{0} dump(s) ({1:F1} MB)", dumpFiles, dumpMb),
                Explanation = Localizer.L(
                    "Purge les anciens fichiers CrashDumps applicatifs et lance une console Administrateur exécutant SFC /scannow.",
                    "Cleans leftover local CrashDumps and opens an Administrator console running SFC /scannow.",
                    "Limpia los archivos CrashDumps locales y abre una consola de Administrador ejecutando SFC /scannow.",
                    "Löscht lokale CrashDumps und startet eine Administrator-Konsole mit SFC /scannow.",
                    "Pulisce i CrashDumps locali e avvia una console Amministratore con SFC /scannow.",
                    "Limpa os CrashDumps locais e abre um console Administrador executando SFC /scannow."),
                ApplyBtn = Localizer.L("Lancer SFC", "Run SFC", "Ejecutar SFC", "SFC starten", "Avvia SFC", "Executar SFC"),
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
                Title = Localizer.L(
                    "Corriger le freeze d'Alt+Tab en jeu sous Windows 11 24H2+ (OverlayMinFPS = 0)",
                    "Fix Windows 11 24H2+ Game Alt+Tab Freeze (OverlayMinFPS = 0)",
                    "Corregir bloqueo de Alt+Tab en juegos en Windows 11 24H2+ (OverlayMinFPS = 0)",
                    "Alt+Tab-Freeze unter Windows 11 24H2+ beheben (OverlayMinFPS = 0)",
                    "Correggi il freeze di Alt+Tab su Windows 11 24H2+ (OverlayMinFPS = 0)",
                    "Corrigir travamento de Alt+Tab no Windows 11 24H2+ (OverlayMinFPS = 0)"),
                RegPathDisplay = @"HKLM\SOFTWARE\Microsoft\Windows\Dwm -> OverlayMinFPS",
                CurrentValue = minFpsFixed
                    ? "OverlayMinFPS = 0 (Actif)"
                    : Localizer.L("Par défaut Windows (Non défini)", "Windows default (Not set)", "Por defecto", "Standard", "Predefinito", "Padrão"),
                Explanation = Localizer.L(
                    "Élimine le gel d'écran de 3 à 8 secondes lors d'un Alt+Tab entre un jeu plein écran sans bordure et Discord / Chrome sous Windows 11 24H2/25H2.",
                    "Eliminates the 3–8 second partial screen freeze when Alt+Tabbing between borderless games and Discord / Chrome on Windows 11 24H2/25H2.",
                    "Elimina el congelamiento de 3 a 8 s al hacer Alt+Tab entre juegos y Discord / Chrome en Windows 11 24H2+.",
                    "Behebt das 3–8-sekündige Einfrieren beim Alt+Tab-Wechsel zwischen Spielen und Discord / Chrome unter Win11 24H2+.",
                    "Elimina il blocco di 3–8 secondi facendo Alt+Tab tra giochi e Discord / Chrome su Windows 11 24H2+.",
                    "Elimina o congelamento de 3–8 segundos ao usar Alt+Tab entre jogos e Discord / Chrome no Windows 11 24H2+."),
                ApplyBtn = Localizer.L("Corriger Alt+Tab", "Fix Alt+Tab", "Corregir Alt+Tab", "Alt+Tab fixen", "Correggi Alt+Tab", "Corrigir Alt+Tab"),
                RestoreBtn = Localizer.L("Par défaut", "Default", "Por defecto", "Standard", "Predefinito", "Padrão"),
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
                    Title = Localizer.L(
                        "Désactiver la veille EEE / Green Ethernet 2.5GbE & Wi-Fi (Anti-Freeze NDIS)",
                        "Disable 2.5GbE EEE / Green Ethernet & Wi-Fi Sleep (NDIS Anti-Freeze)",
                        "Desactivar ahorro EEE / Green Ethernet 2.5GbE y Wi-Fi (Anti-Freeze NDIS)",
                        "2.5GbE EEE / Green Ethernet & WLAN-Ruhemodus deaktivieren",
                        "Disattiva risparmio EEE / Green Ethernet 2.5GbE e Wi-Fi",
                        "Desativar economia EEE / Green Ethernet 2.5GbE e Wi-Fi"),
                    RegPathDisplay = @"HKLM\...\Class\{4d36e972-...} -> *EEE=0, EnableGreenEthernet=0, PnPCapabilities=24",
                    CurrentValue = nicHasRisk
                        ? (nicNames + " — " + Localizer.L("Économie EEE / Veille active", "EEE / Power-Save active", "Ahorro EEE activo", "EEE-Sparmodus aktiv", "Risparmio EEE attivo", "Economia EEE ativa"))
                        : (nicNames + " — " + Localizer.L("Stabilisé (EEE Off)", "Stabilized (EEE Off)", "Estabilizado", "Stabilisiert", "Stabilizzato", "Estabilizado")),
                    Explanation = Localizer.L(
                        "Coupe les modes d'économie instables (Green Ethernet, EEE et coupure d'alimentation Windows) sur Realtek 2.5GbE et MediaTek/Realtek Wi-Fi 6E qui causent des micro-freezes et pertes Bluetooth.",
                        "Disables unstable power-saving modes (Green Ethernet, EEE, and Windows device power-off) on Realtek 2.5GbE and MediaTek/Realtek Wi-Fi 6E that cause NDIS stutters and Bluetooth drops.",
                        "Desactiva los modos de ahorro inestables en tarjetas Realtek 2.5GbE y Wi-Fi 6E que causan microcortes.",
                        "Deaktiviert instabile Energiesparmodi bei Realtek 2.5GbE und Wi-Fi 6E, die NDIS-Ruckler verursachen.",
                        "Disattiva i risparmi energetici instabili su Realtek 2.5GbE e Wi-Fi 6E che causano micro-freeze.",
                        "Desativa modos de economia instáveis na Realtek 2.5GbE e Wi-Fi 6E que causam travamentos NDIS."),
                    ApplyBtn = Localizer.L("Stabiliser Réseau", "Stabilize NIC", "Estabilizar Red", "Netzwerk fixen", "Stabilizza Rete", "Estabilizar Rede"),
                    RestoreBtn = Localizer.L("Par défaut", "Default", "Por defecto", "Standard", "Predefinito", "Padrão"),
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
                        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CrashDumps");
                        if (Directory.Exists(dir))
                        {
                            foreach (string f in Directory.GetFiles(dir, "*.dmp"))
                            {
                                try { File.Delete(f); } catch { }
                            }
                        }
                        if (!Elevation.Launch("cmd.exe", "/k echo === Verification et Reparation Integrite Windows (SFC /scannow) === & sfc /scannow"))
                        {
                            messageFr = "CrashDumps nettoyés, mais la console SFC a été refusée au contrôle UAC.";
                            messageEn = "CrashDumps purged, but the SFC console was refused at the UAC prompt.";
                            return false;
                        }
                        messageFr = "✓ CrashDumps nettoyés et console Administrateur SFC /scannow ouverte !";
                        messageEn = "✓ CrashDumps purged and elevated SFC /scannow console launched!";
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
                    string[] pf = mk.GetValue("PagingFiles") as string[];
                    if (pf == null || pf.Length == 0) return false;
                    return pf.Any(s => !string.IsNullOrWhiteSpace(s) && s.IndexOf("pagefile.sys", StringComparison.OrdinalIgnoreCase) >= 0);
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
            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (Process p = Process.Start(psi))
            {
                string s = p.StandardOutput.ReadToEnd();
                p.WaitForExit(4000);
                return s;
            }
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
