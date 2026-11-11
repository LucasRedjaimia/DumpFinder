using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WinCrashDoctor.Optimize
{
    public sealed class CleanItem
    {
        public string Id;
        public string Title;
        public string Explanation;
        public bool Admin;
        public long Bytes = -1;      // -1 = not readable without admin rights
        public int Files;
        public string Op { get { return OptCleanup.Prefix + Id + "_apply"; } }
    }

    /// <summary>
    /// Disk cleanup limited to caches Windows rebuilds on its own. Crash dumps and error reports are never
    /// touched: they are what Dump Finder diagnoses from.
    /// </summary>
    public static class OptCleanup
    {
        public const string Prefix = "optcl_";
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        private static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }

        private static string PathOf(string id)
        {
            switch (id)
            {
                case "usertemp": return Path.GetTempPath();
                case "wintemp": return Path.Combine(WinDir, "Temp");
                case "wucache": return Path.Combine(WinDir, @"SoftwareDistribution\Download");
                case "docache": return Path.Combine(WinDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");
                default: return null;
            }
        }

        /// <summary>Temp folders keep anything touched in the last 24 h (files an app may still be using).</summary>
        private static bool OnlyOld(string id) { return id == "usertemp" || id == "wintemp"; }

        public static List<CleanItem> Scan()
        {
            var items = new List<CleanItem>
            {
                new CleanItem { Id = "usertemp", Admin = false,
                    Title = L("Fichiers temporaires de ton compte", "Your account's temporary files", "Archivos temporales de tu cuenta", "Temporäre Dateien deines Kontos", "File temporanei del tuo account", "Arquivos temporários da sua conta"),
                    Explanation = L("Dossier %TEMP%. Les fichiers modifiés dans les dernières 24 h sont gardés (une application peut encore s'en servir).", "%TEMP% folder. Files changed in the last 24 h are kept (an app may still be using them).", "Carpeta %TEMP%. Se conservan los archivos de las últimas 24 h.", "Ordner %TEMP%. Dateien der letzten 24 Std. bleiben erhalten.", "Cartella %TEMP%. I file delle ultime 24 h vengono mantenuti.", "Pasta %TEMP%. Arquivos das últimas 24 h são mantidos.") },
                new CleanItem { Id = "wintemp", Admin = true,
                    Title = L("Fichiers temporaires de Windows", "Windows temporary files", "Archivos temporales de Windows", "Temporäre Windows-Dateien", "File temporanei di Windows", "Arquivos temporários do Windows"),
                    Explanation = L("Dossier C:\\Windows\\Temp, mêmes précautions (24 h).", "C:\\Windows\\Temp folder, same precaution (24 h).", "Carpeta C:\\Windows\\Temp, misma precaución (24 h).", "Ordner C:\\Windows\\Temp, gleiche Vorsicht (24 Std.).", "Cartella C:\\Windows\\Temp, stessa precauzione (24 h).", "Pasta C:\\Windows\\Temp, mesma precaução (24 h).") },
                new CleanItem { Id = "wucache", Admin = true,
                    Title = L("Mises à jour Windows déjà installées (téléchargements)", "Already-installed Windows updates (downloads)", "Actualizaciones ya instaladas (descargas)", "Bereits installierte Updates (Downloads)", "Aggiornamenti già installati (download)", "Atualizações já instaladas (downloads)"),
                    Explanation = L("Dossier SoftwareDistribution\\Download : Windows Update retélécharge ce dont il a encore besoin.", "SoftwareDistribution\\Download folder: Windows Update downloads again whatever it still needs.", "Carpeta SoftwareDistribution\\Download: Windows Update vuelve a descargar lo que necesite.", "Ordner SoftwareDistribution\\Download: Windows Update lädt Benötigtes erneut.", "Cartella SoftwareDistribution\\Download: Windows Update riscarica ciò che serve.", "Pasta SoftwareDistribution\\Download: o Windows Update baixa de novo o que precisar.") },
                new CleanItem { Id = "docache", Admin = true,
                    Title = L("Cache d'optimisation de la distribution", "Delivery Optimization cache", "Caché de optimización de distribución", "Cache der Übermittlungsoptimierung", "Cache di ottimizzazione recapito", "Cache de otimização de entrega"),
                    Explanation = L("Morceaux de mises à jour gardés pour être partagés ; vidé avec la commande officielle Delete-DeliveryOptimizationCache.", "Update pieces kept for sharing; emptied with the official Delete-DeliveryOptimizationCache command.", "Partes de actualizaciones para compartir; se vacía con el comando oficial.", "Update-Teile zum Teilen; wird mit dem offiziellen Befehl geleert.", "Parti di aggiornamenti da condividere; svuotata con il comando ufficiale.", "Partes de atualizações para compartilhar; esvaziado com o comando oficial.") }
            };
            foreach (CleanItem it in items) Measure(it);
            return items;
        }

        private static void Measure(CleanItem it)
        {
            string dir = PathOf(it.Id);
            try
            {
                if (!Directory.Exists(dir)) { it.Bytes = 0; return; }
                Directory.GetFileSystemEntries(dir); // throws when the folder itself needs admin rights
                long total = 0; int n = 0;
                DateTime cutoff = DateTime.Now.AddHours(-24);
                foreach (string path in SafeFiles(dir)) // per-folder: one protected subfolder does not stop the count
                {
                    try
                    {
                        var f = new FileInfo(path);
                        if (OnlyOld(it.Id) && f.LastWriteTime > cutoff) continue;
                        total += f.Length; n++;
                    }
                    catch { }
                }
                it.Bytes = total; it.Files = n;
            }
            catch (UnauthorizedAccessException) { it.Bytes = -1; }
            catch (Exception ex) { Log.Warn(ex, dir); it.Bytes = -1; }
        }

        // ==================== Operations ====================
        public static bool IsOp(string op) { return op != null && op.StartsWith(Prefix, StringComparison.Ordinal) && op.EndsWith("_apply", StringComparison.Ordinal); }

        private static string IdOf(string op) { return op.Substring(Prefix.Length, op.Length - Prefix.Length - 6); }

        public static bool NeedsAdmin(string op) { return IsOp(op) && IdOf(op) != "usertemp"; }

        public static string Title(string op)
        {
            if (!IsOp(op)) return null;
            CleanItem it = new[] { "usertemp", "wintemp", "wucache", "docache" }.Contains(IdOf(op)) ? Scan().FirstOrDefault(x => x.Id == IdOf(op)) : null;
            return it != null ? L("Nettoyage : ", "Cleanup: ", "Limpieza: ", "Bereinigung: ", "Pulizia: ", "Limpeza: ") + it.Title : null;
        }

        public static bool Execute(string op, out string messageFr, out string messageEn)
        {
            string id = IdOf(op);
            string dir = PathOf(id);
            if (dir == null) { messageFr = "Élément inconnu."; messageEn = "Unknown item."; return false; }

            long freed = 0; int deleted = 0, skipped = 0;
            if (id == "docache")
            {
                long before = DirSize(dir);
                RunHidden("powershell.exe", "-NoProfile -NonInteractive -Command \"Delete-DeliveryOptimizationCache -Force\"", 120000);
                freed = Math.Max(0, before - DirSize(dir));
            }
            else if (Directory.Exists(dir))
            {
                DateTime cutoff = DateTime.Now.AddHours(-24);
                foreach (string f in SafeFiles(dir))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (OnlyOld(id) && fi.LastWriteTime > cutoff) continue;
                        long len = fi.Length;
                        fi.Attributes = FileAttributes.Normal;
                        fi.Delete();
                        freed += len; deleted++;
                    }
                    catch { skipped++; } // in use: left alone
                }
                foreach (string d in SafeDirs(dir).OrderByDescending(x => x.Length))
                {
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
                }
            }

            string gb = (freed / (1024.0 * 1024 * 1024)).ToString("0.00");
            messageFr = "✓ " + gb + " Go libérés" + (skipped > 0 ? " (" + skipped + " fichier(s) en cours d'utilisation laissés en place)" : "") + ".";
            messageEn = "✓ " + gb + " GB freed" + (skipped > 0 ? " (" + skipped + " file(s) in use left alone)" : "") + ".";
            return true;
        }

        private static IEnumerable<string> SafeFiles(string dir)
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                string d = stack.Pop();
                string[] files = new string[0], subs = new string[0];
                try { files = Directory.GetFiles(d); subs = Directory.GetDirectories(d); } catch { }
                foreach (string f in files) yield return f;
                foreach (string s in subs)
                {
                    // Never follow junctions / symlinks out of the cache folder.
                    try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(s);
                }
            }
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            var result = new List<string>();
            try
            {
                foreach (string s in Directory.GetDirectories(dir))
                {
                    try { if ((File.GetAttributes(s) & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    result.Add(s);
                    result.AddRange(SafeDirs(s));
                }
            }
            catch { }
            return result;
        }

        private static long DirSize(string dir)
        {
            long t = 0;
            foreach (string f in SafeFiles(dir)) { try { t += new FileInfo(f).Length; } catch { } }
            return t;
        }

        private static void RunHidden(string exe, string args, int timeout)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo { FileName = exe, Arguments = args, CreateNoWindow = true, UseShellExecute = false }))
                    p.WaitForExit(timeout);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }
    }
}
