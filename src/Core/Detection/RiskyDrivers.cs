using System;
using System.Collections.Generic;
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
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

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
                Owner = () => L("logiciels RGB et de monitoring", "RGB and monitoring software", "software RGB y de monitorización", "RGB- und Monitoring-Software", "software RGB e di monitoraggio", "softwares RGB e de monitoramento"),
                Why = () => L("Microsoft l'a identifié en août 2026 comme la cause de gels, fermetures et redémarrages en jeu après la mise à jour KB5121003 (ARC Raiders, The Finals…). Le contournement officiel est de désactiver ce service ; le contrôle RGB peut cesser de fonctionner.",
                              "Microsoft identified it in August 2026 as the cause of in-game freezes, closures and restarts after update KB5121003 (ARC Raiders, The Finals…). The official workaround is to disable this service; RGB control may stop working.",
                              "Microsoft lo identificó en agosto de 2026 como causa de bloqueos, cierres y reinicios en juegos tras KB5121003. La solución oficial es desactivar el servicio; el control RGB puede dejar de funcionar.",
                              "Microsoft hat ihn im August 2026 als Ursache für Einfrieren, Schließen und Neustarts in Spielen nach KB5121003 benannt. Offizielle Umgehung: Dienst deaktivieren; die RGB-Steuerung kann ausfallen.",
                              "Microsoft lo ha indicato ad agosto 2026 come causa di blocchi, chiusure e riavvii nei giochi dopo KB5121003. La soluzione ufficiale è disattivare il servizio; il controllo RGB può smettere di funzionare.",
                              "A Microsoft o identificou em agosto de 2026 como causa de travamentos, fechamentos e reinícios em jogos após a KB5121003. A solução oficial é desativar o serviço; o controle RGB pode parar.")
            },
            new Def
            {
                File = "WinRing0x64.sys",
                Owner = () => L("anciens outils de ventilation, RGB ou monitoring", "older fan-control, RGB or monitoring tools", "herramientas antiguas de ventiladores, RGB o monitorización", "ältere Lüfter-, RGB- oder Monitoring-Tools", "vecchi strumenti per ventole, RGB o monitoraggio", "ferramentas antigas de ventoinhas, RGB ou monitoramento"),
                Why = () => L("Pilote vulnérable (CVE-2020-14979) signalé par Microsoft Defender depuis 2025. Les versions récentes de ces outils utilisent PawnIO à la place : mets-les à jour, puis désactive ce pilote.",
                              "Vulnerable driver (CVE-2020-14979) flagged by Microsoft Defender since 2025. Recent versions of these tools use PawnIO instead: update them, then disable this driver.",
                              "Controlador vulnerable (CVE-2020-14979) marcado por Microsoft Defender desde 2025. Las versiones recientes usan PawnIO: actualízalas y desactiva este controlador.",
                              "Anfälliger Treiber (CVE-2020-14979), seit 2025 von Microsoft Defender gemeldet. Neuere Versionen nutzen PawnIO: aktualisieren, dann diesen Treiber deaktivieren.",
                              "Driver vulnerabile (CVE-2020-14979) segnalato da Microsoft Defender dal 2025. Le versioni recenti usano PawnIO: aggiornale, poi disattiva questo driver.",
                              "Driver vulnerável (CVE-2020-14979) sinalizado pelo Microsoft Defender desde 2025. Versões recentes usam PawnIO: atualize-as e desative este driver.")
            },
            new Def
            {
                File = "RTCore64.sys",
                Owner = () => L("MSI Afterburner / RivaTuner", "MSI Afterburner / RivaTuner", "MSI Afterburner / RivaTuner", "MSI Afterburner / RivaTuner", "MSI Afterburner / RivaTuner", "MSI Afterburner / RivaTuner"),
                Why = () => L("Pilote vulnérable (CVE-2019-16098) inscrit sur la liste de blocage des pilotes vulnérables de Microsoft. Afterburner en a besoin pour l'overclocking : garde-le seulement si tu t'en sers, et à jour.",
                              "Vulnerable driver (CVE-2019-16098) on Microsoft's vulnerable driver blocklist. Afterburner needs it for overclocking: keep it only if you use that, and keep it updated.",
                              "Controlador vulnerable (CVE-2019-16098) en la lista de bloqueo de Microsoft. Afterburner lo necesita para overclock: consérvalo solo si lo usas y actualizado.",
                              "Anfälliger Treiber (CVE-2019-16098) auf Microsofts Sperrliste. Afterburner braucht ihn zum Übertakten: nur behalten, wenn genutzt, und aktuell halten.",
                              "Driver vulnerabile (CVE-2019-16098) nella blocklist di Microsoft. Afterburner ne ha bisogno per l'overclock: tienilo solo se lo usi, e aggiornato.",
                              "Driver vulnerável (CVE-2019-16098) na lista de bloqueio da Microsoft. O Afterburner precisa dele para overclock: mantenha só se usar, e atualizado.")
            },
            new Def
            {
                File = "gdrv.sys",
                Owner = () => L("anciens utilitaires Gigabyte (App Center, EasyTune)", "older Gigabyte utilities (App Center, EasyTune)", "utilidades antiguas de Gigabyte (App Center, EasyTune)", "ältere Gigabyte-Tools (App Center, EasyTune)", "vecchie utility Gigabyte (App Center, EasyTune)", "utilitários antigos da Gigabyte (App Center, EasyTune)"),
                Why = () => L("Pilote vulnérable (CVE-2018-19320) inscrit sur la liste de blocage de Microsoft ; il n'est plus nécessaire avec les outils Gigabyte récents.",
                              "Vulnerable driver (CVE-2018-19320) on Microsoft's blocklist; recent Gigabyte tools no longer need it.",
                              "Controlador vulnerable (CVE-2018-19320) en la lista de bloqueo de Microsoft; las herramientas recientes de Gigabyte ya no lo necesitan.",
                              "Anfälliger Treiber (CVE-2018-19320) auf Microsofts Sperrliste; aktuelle Gigabyte-Tools brauchen ihn nicht mehr.",
                              "Driver vulnerabile (CVE-2018-19320) nella blocklist di Microsoft; i tool Gigabyte recenti non ne hanno più bisogno.",
                              "Driver vulnerável (CVE-2018-19320) na lista de bloqueio da Microsoft; ferramentas recentes da Gigabyte não precisam mais dele.")
            },
            new Def
            {
                File = "ene.sys",
                Owner = () => L("logiciels RGB ENE (barrettes mémoire, cartes mères)", "ENE RGB software (memory sticks, motherboards)", "software RGB ENE (memorias, placas base)", "ENE-RGB-Software (Speichermodule, Mainboards)", "software RGB ENE (memorie, schede madri)", "softwares RGB ENE (memórias, placas-mãe)"),
                Why = () => L("Pilote RGB que Windows signale comme incompatible avec l'intégrité de la mémoire, et cause connue d'écrans bleus. Mets à jour ou désinstalle le logiciel RGB qui l'installe.",
                              "RGB driver that Windows reports as incompatible with Memory Integrity, and a known cause of blue screens. Update or uninstall the RGB software that installs it.",
                              "Controlador RGB que Windows marca como incompatible con la integridad de memoria y causa conocida de pantallas azules. Actualiza o desinstala el software RGB.",
                              "RGB-Treiber, den Windows als inkompatibel mit der Speicherintegrität meldet, bekannte Bluescreen-Ursache. RGB-Software aktualisieren oder deinstallieren.",
                              "Driver RGB che Windows segnala come incompatibile con l'integrità della memoria, causa nota di schermate blu. Aggiorna o disinstalla il software RGB.",
                              "Driver RGB que o Windows aponta como incompatível com a integridade de memória e causa conhecida de telas azuis. Atualize ou desinstale o software RGB.")
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
                    ? L("Désactivé", "Disabled", "Desactivado", "Deaktiviert", "Disattivato", "Desativado")
                    : f.Running
                        ? L("Chargé en ce moment", "Currently loaded", "Cargado ahora", "Derzeit geladen", "Caricato ora", "Carregado agora")
                        : L("Installé, non chargé (Windows le bloque peut-être déjà)", "Installed, not loaded (Windows may already block it)", "Instalado, no cargado (Windows quizá ya lo bloquea)", "Installiert, nicht geladen (Windows blockiert ihn evtl. schon)", "Installato, non caricato (Windows potrebbe già bloccarlo)", "Instalado, não carregado (o Windows talvez já o bloqueie)");
                yield return new RegistryFixItem
                {
                    Id = IdPrefix + f.Service,
                    Group = FixGroup.SystemMaint,
                    VendorTag = L("PILOTE NOYAU", "KERNEL DRIVER", "CONTROLADOR", "KERNELTREIBER", "DRIVER KERNEL", "DRIVER DE KERNEL"),
                    HardwareTarget = "ALL",
                    Title = string.Format(L("Désactiver le pilote {0} ({1})", "Disable the {0} driver ({1})", "Desactivar el controlador {0} ({1})", "Treiber {0} deaktivieren ({1})", "Disattiva il driver {0} ({1})", "Desativar o driver {0} ({1})"), f.File, f.Def.Owner()),
                    RegPathDisplay = @"HKLM\SYSTEM\CurrentControlSet\Services\" + f.Service + "  •  Start = 4",
                    CurrentValue = state,
                    Explanation = f.Def.Why(),
                    ApplyBtn = L("Désactiver", "Disable", "Desactivar", "Deaktivieren", "Disattiva", "Desativar"),
                    RestoreBtn = L("Réactiver", "Re-enable", "Reactivar", "Aktivieren", "Riattiva", "Reativar"),
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
                k.SetValue("Start", apply ? 4 : 3, RegistryValueKind.DWord);
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
