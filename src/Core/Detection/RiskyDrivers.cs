using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using Microsoft.Win32;

namespace WinCrashDoctor.Detection
{
    /// <summary>
    /// Kernel drivers with a documented record of crashes or abuse (vulnerable-driver blocklist, Defender
    /// detections, Microsoft known issues). Found by the file their service loads, whatever the service is named.
    /// Disabling = service Start = 4, recorded by ChangeRecorder so the journal can put the exact value back.
    /// </summary>
    public static class RiskyDrivers
    {

        internal sealed class Def
        {
            public string File;
            public Func<string> Owner;
            public Func<string> Why;
            /// <summary>Recommended (not just optional) when the driver is actually loaded.</summary>
            public bool RecommendWhenLoaded;
        }

        private static readonly Def[] Catalog =
        {
            new Def
            {
                File = "inpoutx64.sys",
                RecommendWhenLoaded = true,
                Owner = () => Localizer.S("risky_drivers.rgb_and_monitoring_software"),
                Why = () => Localizer.S("risky_drivers.microsoft_identified_it_in_august_2026")
            },
            new Def
            {
                File = "WinRing0x64.sys",
                Owner = () => Localizer.S("risky_drivers.older_fan_control_rgb_or_monitoring"),
                Why = () => Localizer.S("risky_drivers.vulnerable_driver_cve_2020_14979_flagged")
            },
            new Def
            {
                File = "RTCore64.sys",
                Owner = () => Localizer.S("risky_drivers.msi_afterburner_rivatuner"),
                Why = () => Localizer.S("risky_drivers.vulnerable_driver_cve_2019_16098_on")
            },
            new Def
            {
                File = "gdrv.sys",
                Owner = () => Localizer.S("risky_drivers.older_gigabyte_utilities_app_center_easy"),
                Why = () => Localizer.S("risky_drivers.vulnerable_driver_cve_2018_19320_on")
            },
            new Def
            {
                File = "ene.sys",
                Owner = () => Localizer.S("risky_drivers.ene_rgb_software_memory_sticks_motherboa"),
                Why = () => Localizer.S("risky_drivers.rgb_driver_that_windows_reports_as")
            }
        };

        public sealed class Found
        {
            public string Service;
            public string File;
            public int Start;
            public bool Running;
            internal Def Def;
            public bool Disabled { get { return Start == 4; } }
        }

        private const string ServicesKey = @"SYSTEM\CurrentControlSet\Services";

        /// <summary>Services whose ImagePath points at a catalog driver.</summary>
        public static List<Found> Scan()
        {
            var list = new List<Found>();
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var svcs = bk.OpenSubKey(ServicesKey))
                {
                    if (svcs == null) return list;
                    foreach (string name in svcs.GetSubKeyNames())
                    {
                        try // some service keys are not readable by a standard user: skip them, not the whole scan
                        {
                        using (var k = svcs.OpenSubKey(name))
                        {
                            if (k == null) continue;
                            string img = Convert.ToString(k.GetValue("ImagePath") ?? "");
                            if (img.Length == 0) continue;
                            // ImagePath may be quoted and carry arguments: keep the path part only.
                            int sys = img.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
                            string path = sys > 0 ? img.Substring(0, sys + 4).TrimStart('"')
                                        : (img.StartsWith("\"") ? img.Substring(1).Split('"')[0] : img.Split(' ')[0]);
                            string file = path.Substring(path.LastIndexOf('\\') + 1); // not Path.GetFileName: it rejects "\??\" prefixes
                            Def def = Catalog.FirstOrDefault(d => d.File.Equals(file, StringComparison.OrdinalIgnoreCase));
                            if (def == null) continue;
                            object start = k.GetValue("Start");
                            list.Add(new Found { Service = name, File = def.File, Start = start is int ? (int)start : 3, Running = IsRunning(name), Def = def });
                        }
                        }
                        catch (Exception) { } // unreadable key, or an ImagePath with quotes/arguments Path.GetFileName rejects
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        private static bool IsRunning(string service)
        {
            try
            {
                using (var sc = new ServiceController(service))
                    return sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
            }
            catch { return false; }
        }

        // ==================== Fix items (Correctifs page, Home, MCP) ====================
        public const string IdPrefix = "drv_";

        public static IEnumerable<RegistryFixItem> FixItems()
        {
            foreach (Found f in Scan())
            {
                string state = f.Disabled
                    ? Localizer.S("risky_drivers.disabled")
                    : f.Running
                        ? Localizer.S("risky_drivers.currently_loaded")
                        : Localizer.S("risky_drivers.installed_not_loaded_windows_may_already");
                yield return new RegistryFixItem
                {
                    Id = IdPrefix + f.Service,
                    Group = FixGroup.SystemMaint,
                    VendorTag = Localizer.S("risky_drivers.kernel_driver"),
                    HardwareTarget = "ALL",
                    Title = string.Format(Localizer.S("risky_drivers.disable_the_driver"), f.File, f.Def.Owner()),
                    RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Services\" + f.Service + "  •  Start = 4",
                    CurrentValue = state,
                    Explanation = f.Def.Why(),
                    ApplyBtn = Localizer.S("risky_drivers.disable"),
                    RestoreBtn = Localizer.S("risky_drivers.re_enable"),
                    Status = f.Disabled ? FixStatus.Applied : (f.Running && f.Def.RecommendWhenLoaded ? FixStatus.Recommended : FixStatus.Optional),
                    RequiresAdmin = true,
                    CanRestore = f.Disabled,
                    RegExportContent = ""
                };
            }
        }

        public static bool IsOp(string op) { return op != null && op.StartsWith(IdPrefix, StringComparison.Ordinal) && (op.EndsWith("_apply", StringComparison.Ordinal) || op.EndsWith("_restore", StringComparison.Ordinal)); }

        /// <summary>
        /// drv_&lt;service&gt;_apply / _restore. Only services that currently point at a catalog driver are accepted,
        /// so an op string (e.g. from an MCP client) can never disable an arbitrary service.
        /// </summary>
        public static bool Execute(string op, out string messageFr, out string messageEn)
        {
            bool apply = op.EndsWith("_apply", StringComparison.Ordinal);
            string service = op.Substring(IdPrefix.Length, op.Length - IdPrefix.Length - (apply ? 6 : 8));
            Found f = Scan().FirstOrDefault(x => x.Service.Equals(service, StringComparison.OrdinalIgnoreCase));
            if (f == null)
            {
                messageFr = "Pilote inconnu ou absent : " + service;
                messageEn = "Unknown or missing driver: " + service;
                return false;
            }

            string sub = ServicesKey + "\\" + f.Service;
            ChangeRecorder.Registry(RegistryHive.LocalMachine, sub, "Start");
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.OpenSubKey(sub, true))
            {
                if (k == null) { messageFr = "Clé de service introuvable."; messageEn = "Service key not found."; return false; }
                k.SetValue("Start", apply ? 4 : StartBeforeDisable(f.Service), RegistryValueKind.DWord);
            }
            if (apply)
            {
                try
                {
                    using (var sc = new ServiceController(f.Service))
                        if (sc.Status == ServiceControllerStatus.Running) sc.Stop();
                }
                catch (Exception ex) { Log.Warn(ex); }
                messageFr = "✓ Pilote " + f.File + " désactivé (Start = 4). Redémarre le PC pour qu'il ne soit plus chargé.";
                messageEn = "✓ Driver " + f.File + " disabled (Start = 4). Reboot so it is no longer loaded.";
            }
            else
            {
                messageFr = "Pilote " + f.File + " réactivé (démarrage à la demande).";
                messageEn = "Driver " + f.File + " re-enabled (demand start).";
            }
            return true;
        }

        /// <summary>
        /// Startup type the driver had before Dump Finder disabled it, read from the change journal (a driver that
        /// loaded at boot, Start 0/1/2, must not come back as "demand start"). 3 when unknown.
        /// </summary>
        private static int StartBeforeDisable(string service)
        {
            try
            {
                string keyPath = @"HKEY_LOCAL_MACHINE\" + ServicesKey + "\\" + service;
                foreach (JournalEntry j in History.Journal()) // newest first
                {
                    if (!j.Ok || j.Changes == null || !string.Equals(j.Op, IdPrefix + service + "_apply", StringComparison.OrdinalIgnoreCase)) continue;
                    ChangeRecord c = j.Changes.FirstOrDefault(x => x.Kind == "reg" && x.Existed
                        && string.Equals(x.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(x.ValueName, "Start", StringComparison.OrdinalIgnoreCase));
                    int start;
                    if (c != null && int.TryParse(c.Data, NumberStyles.Integer, CultureInfo.InvariantCulture, out start) && start >= 0 && start <= 3)
                        return start;
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return 3;
        }

        /// <summary>Lets ChangeRecorder undo a change to a catalog driver's service key (and nothing else).</summary>
        public static bool IsRevertableKey(string keyPath)
        {
            const string prefix = @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\";
            if (keyPath == null || !keyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
            string service = keyPath.Substring(prefix.Length);
            if (service.Length == 0 || service.Contains("\\")) return false;
            return Scan().Any(x => x.Service.Equals(service, StringComparison.OrdinalIgnoreCase));
        }
    }
}
