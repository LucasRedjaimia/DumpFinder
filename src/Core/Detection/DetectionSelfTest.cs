using System;
using System.Linq;
using System.Text;

namespace WinCrashDoctor.Detection
{
    /// <summary>--detect-selftest &lt;out.txt&gt;: read-only checks of the detection v2 rules on this PC (never applies a fix).</summary>
    internal static class DetectionSelfTest
    {
        public static int Run(string outPath)
        {
            Localizer.ReadOnlySettings = true;
            Localizer.Current = AppLang.FR;
            var sb = new StringBuilder();
            int bad = 0;
            Action<string, bool> expect = (label, ok) => { if (!ok) bad++; sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label); };

            // Version mapping (Windows → NVIDIA numbering)
            expect("32.0.15.9559 → 595.59", DetectionChecks.NvidiaVersion("32.0.15.9559") == "595.59");
            expect("32.0.16.2114 → 621.14", DetectionChecks.NvidiaVersion("32.0.16.2114") == "621.14");
            expect("31.0.15.7283 → 572.83", DetectionChecks.NvidiaVersion("31.0.15.7283") == "572.83");

            // Bad-version rules on a fake RTX 5080
            var fake = new HardwareSummary { HasNvidiaGpu = true, GpuName = "NVIDIA GeForce RTX 5080" };
            fake.GpuDriver = "32.0.15.9559";
            expect("595.59 signalée", DetectionChecks.GpuDriverVersion(fake).Level == HealthLevel.Warning);
            fake.GpuDriver = "32.0.16.2101";
            expect("621.01 signalée", DetectionChecks.GpuDriverVersion(fake).Level == HealthLevel.Warning);
            fake.GpuDriver = "31.0.15.7270";
            expect("572.70 sur RTX 50 signalée", DetectionChecks.GpuDriverVersion(fake).Level == HealthLevel.Warning);
            fake.GpuDriver = "32.0.16.2114";
            expect("621.14 non signalée", DetectionChecks.GpuDriverVersion(fake).Level == HealthLevel.Good);

            // Safety: an op string can only target a catalog driver
            string fr, en;
            expect("op sur un service hors catalogue refusée (Tcpip)", !RiskyDrivers.Execute("drv_Tcpip_apply", out fr, out en));
            expect("annulation hors catalogue refusée (Tcpip)", !RiskyDrivers.IsRevertableKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\Tcpip"));
            expect("sous-clé refusée", !RiskyDrivers.IsRevertableKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\inpoutx64\Parameters"));

            // This PC
            sb.AppendLine();
            sb.AppendLine("=== Ce PC ===");
            var w = WindowsRelease.Current;
            sb.AppendLine("Windows : " + w.Label + " · fin de support : " + (w.EndOfServicing.HasValue ? w.EndOfServicing.Value.ToString("yyyy-MM-dd") : "non annoncée"));
            foreach (var f in RiskyDrivers.Scan())
            {
                sb.AppendLine("Pilote à risque : " + f.File + " (service " + f.Service + ", Start=" + f.Start + ", " + (f.Running ? "chargé" : "non chargé") + ")");
                expect("annulation autorisée pour " + f.Service, RiskyDrivers.IsRevertableKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\" + f.Service));
            }
            foreach (var item in RiskyDrivers.FixItems())
                sb.AppendLine("Correctif : " + item.Id + " · " + item.Status + " · " + item.CurrentValue);

            HardwareSummary hw = DiagnosticsEngine.GetHardwareSummary();
            foreach (var c in DetectionChecks.All(hw))
            {
                sb.AppendLine();
                sb.AppendLine("[" + c.Level + "] " + c.Title);
                sb.AppendLine("   " + c.StatusText);
                sb.AppendLine("   " + c.Explanation);
                if (c.InAppFixOp != null) sb.AppendLine("   correctif proposé : " + c.InAppFixOp);
            }
            sb.AppendLine();
            Localizer.LoadSettings(); // the BIOS check reads the saved confirmation
            sb.AppendLine("Correctif Alt+Tab présent : " + DiagnosticsEngine.InspectAllFixes().Any(f => f.Id == "dwm_alt_tab") + " (build " + w.Build + ")");
            // HMB fix: shown only on Windows 11 24H2+ with an NVMe drive (or when already applied, to keep it undoable)
            var fixes = DiagnosticsEngine.InspectAllFixes();
            var hmb = fixes.FirstOrDefault(f => f.Id == "nvme_hmb");
            sb.AppendLine("SSD NVMe : " + (NvmeDrives.Models.Count == 0 ? "aucun" : string.Join(", ", NvmeDrives.Models.Select(m => m + (NvmeDrives.IsDramLess(m) ? " (sans DRAM)" : "")))));
            expect("correctif HMB " + (hmb != null ? "présent" : "masqué") + " (build " + w.Build + ", NVMe : " + NvmeDrives.Models.Count + ")",
                   (hmb != null) == (NvmeDrives.HmbIssueApplies || (hmb != null && hmb.Status == FixStatus.Applied)));
            expect("règle HMB : Windows 10 / 11 23H2 exclus", NvmeDrives.HmbIssueBuild == 26100 && (w.Build >= 26100) == NvmeDrives.WindowsHasHmbIssue);

            // BIOS: an old BIOS offers "already the latest"; once confirmed, the card is no longer a warning
            var bios = SystemHealthScanner.ScanHardwareHealth(hw, null).FirstOrDefault(c => c.CategoryTag != null && c.CategoryTag.StartsWith("BIOS"));
            expect("carte BIOS présente", bios != null);
            if (bios != null && bios.Level == HealthLevel.Good)
            {
                sb.AppendLine("BIOS réel : " + bios.ValueSummary + " · " + bios.Level + " (simulation d'un BIOS de 3 ans)");
                expect("BIOS récent : pas de bouton", bios.InAppFixOp == null);
                SystemHealthScanner.TestBiosDate = DateTime.Now.AddYears(-3);
                bios = SystemHealthScanner.ScanHardwareHealth(hw, null).First(c => c.CategoryTag.StartsWith("BIOS"));
                expect("BIOS de 3 ans signalé", bios.Level == HealthLevel.Warning);
            }
            if (bios != null)
            {
                sb.AppendLine("BIOS : " + bios.ValueSummary + " · " + bios.Level);
                if (bios.Level == HealthLevel.Warning)
                {
                    expect("bouton « déjà la dernière version » proposé", bios.InAppFixOp != null && bios.InAppFixOp.StartsWith(SystemHealthScanner.BiosAckOp));
                    string saved = Localizer.CachedSettings.BiosLatestAck;
                    Localizer.CachedSettings.BiosLatestAck = bios.InAppFixOp.Substring(SystemHealthScanner.BiosAckOp.Length); // in memory only (read-only settings)
                    var acked = SystemHealthScanner.ScanHardwareHealth(hw, null).First(c => c.CategoryTag.StartsWith("BIOS"));
                    expect("après confirmation : plus d'alerte (" + acked.Level + ")", acked.Level == HealthLevel.Good && acked.InAppFixOp == null);
                    Localizer.CachedSettings.BiosLatestAck = saved;
                    sb.AppendLine("   texte : " + bios.Detail);
                }
                else expect("BIOS récent : pas de bouton", bios.InAppFixOp == null);
            }
            SystemHealthScanner.TestBiosDate = null;
            sb.AppendLine("Entrées de la base de connaissances : " + KnowledgeBase.GetEntries().Count);
            sb.AppendLine("0x18B expliqué : " + (BugCheckCatalog.Get(0x18B) != null && BugCheckCatalog.Get(0x18B).ExplanationFr != null));
            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }
    }
}
