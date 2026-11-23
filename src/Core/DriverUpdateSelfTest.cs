using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>--driver-update-selftest &lt;out.txt&gt;: version/date rules, then one real Windows Update search matched against this PC's drivers (read-only).</summary>
    internal static class DriverUpdateSelfTest
    {
        public static int Run(string outPath)
        {
            var sb = new StringBuilder();
            int bad = 0;
            Action<string, bool> expect = (label, ok) => { if (!ok) bad++; sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label); };

            DateTime d2017 = new DateTime(2017, 8, 6), d2020 = new DateTime(2020, 10, 9);
            expect("date plus récente → plus récent", DriverUpdateCheck.IsNewer(d2020, "1.16.4.14", d2017, "1.16.1.14"));
            expect("date plus ancienne → pas plus récent, même avec une version plus haute", !DriverUpdateCheck.IsNewer(d2017, "9.0.0.0", d2020, "1.0.0.0"));
            expect("même date, version plus haute → plus récent", DriverUpdateCheck.IsNewer(d2017, "1.16.2.0", d2017, "1.16.1.14"));
            expect("même date, même version → pas plus récent", !DriverUpdateCheck.IsNewer(d2017, "1.16.1.14", d2017, "1.16.1.14"));
            expect("sans date ni version lisible → pas plus récent", !DriverUpdateCheck.IsNewer(null, null, d2017, "1.0"));
            expect("version tirée du titre « … - MEDIA - 1.16.4.14 »", DriverUpdateCheck.VersionFromTitle("Creative Technology Ltd. - MEDIA - 1.16.4.14") == "1.16.4.14");
            expect("version tirée du titre « … Update (2.21.4.0) »", DriverUpdateCheck.VersionFromTitle("SAMSUNG Electronics Co., Ltd.  AndroidUsbDeviceClass Driver Update (2.21.4.0)") == "2.21.4.0");
            expect("titre sans version → null", DriverUpdateCheck.VersionFromTitle("Realtek - Net") == null);

            var drv = new DriverInfo { DeviceName = "Sound Blaster Play! 3", Version = "1.16.1.14", Date = d2017,
                HardwareIds = new List<string> { "USB\\VID_041E&PID_324D&REV_0100&MI_00", "USB\\VID_041E&PID_324D&MI_00" } };
            var offers = new List<DriverUpdateOffer>
            {
                new DriverUpdateOffer { HardwareId = "usb\\vid_041e&pid_324d&mi_00", Version = "1.16.4.14", Date = "2020-10-09", Title = "Creative" },
                new DriverUpdateOffer { HardwareId = "usb\\vid_dead&pid_beef", Version = "9.9.9.9", Date = "2026-01-01", Title = "Autre" }
            };
            DriverUpdateOffer hit = DriverUpdateCheck.FindNewer(offers, drv);
            expect("offre trouvée par ID matériel (casse ignorée)", hit != null && hit.Version == "1.16.4.14");
            drv.Version = "1.16.4.14"; drv.Date = d2020;
            expect("pilote déjà à jour → aucune offre", DriverUpdateCheck.FindNewer(offers, drv) == null);
            expect("pilote sans ID matériel → aucune offre", DriverUpdateCheck.FindNewer(offers, new DriverInfo { Version = "1.0" }) == null);

            sb.AppendLine();
            sb.AppendLine("=== Windows Update (recherche réelle, rien n'est installé) ===");
            try
            {
                DriverUpdateResult live = DriverUpdateCheck.Get(true);
                sb.AppendLine("Offres de pilotes : " + live.Offers.Count + " · vérifié " + live.CheckedAt);
                foreach (var o in live.Offers) sb.AppendLine("  " + o.HardwareId + " · " + o.Version + " · " + o.Date + " · " + o.Title);
                List<DriverInfo> installed = DriverInventory.Scan();
                expect("inventaire : IDs matériels lus", installed.Any(x => x.HardwareIds != null && x.HardwareIds.Count > 0));
                sb.AppendLine("Pilotes tiers installés : " + installed.Count);
                foreach (var d in installed)
                {
                    DriverUpdateOffer n = DriverUpdateCheck.FindNewer(live.Offers, d);
                    if (n != null) sb.AppendLine("  PLUS RÉCENT : " + d.DeviceName + " " + d.Version + " → " + n.Version + " (" + n.Date + ")");
                    else if (d.IsOld) sb.AppendLine("  ancien, rien de plus récent : " + d.DeviceName + " " + d.Version);
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("Windows Update injoignable : " + DriverUpdateCheck.Describe(ex));
            }

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "RÉSULTAT : tout est OK" : "RÉSULTAT : " + bad + " échec(s)");
            File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8);
            return bad == 0 ? 0 : 1;
        }
    }
}
