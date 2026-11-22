using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using WinCrashDoctor.Detection;

namespace WinCrashDoctor
{
    /// <summary>--update-selftest &lt;out.txt&gt;: version parsing, link safety, release parsing, then one real request to GitHub.</summary>
    internal static class UpdateSelfTest
    {
        public static int Run(string outPath)
        {
            var sb = new StringBuilder();
            int bad = 0;
            Action<string, bool> expect = (label, ok) => { if (!ok) bad++; sb.AppendLine((ok ? "[OK]   " : "[FAIL] ") + label); };

            expect("version de l'exe : " + UpdateChecker.CurrentVersionText, UpdateChecker.CurrentVersion.Major > 0);
            expect("v6.1.0 → 6.1.0", UpdateChecker.ParseVersion("v6.1.0") == new Version(6, 1, 0));
            expect("6.2 → 6.2.0", UpdateChecker.ParseVersion("6.2") == new Version(6, 2, 0));
            expect("V7.0.0.0 → 7.0.0", UpdateChecker.ParseVersion("V7.0.0.0") == new Version(7, 0, 0));
            expect("v6.1.0-beta → 6.1.0", UpdateChecker.ParseVersion("v6.1.0-beta") == new Version(6, 1, 0));
            expect("« latest » refusé", UpdateChecker.ParseVersion("latest") == null);
            expect("lien du repo accepté", UpdateChecker.IsOwnUrl(UpdateChecker.RepoUrl + "/releases/download/v6.1.0/DumpFinder.exe"));
            expect("lien externe refusé", !UpdateChecker.IsOwnUrl("https://evil.example/DumpFinder.exe"));
            expect("autre repo refusé", !UpdateChecker.IsOwnUrl("https://github.com/" + UpdateChecker.Owner + "/DumpFinderX/releases"));

            string sample = "{\"tag_name\":\"v9.1.0\",\"name\":\"Dump Finder 9.1.0\",\"draft\":false,\"prerelease\":false," +
                            "\"html_url\":\"" + UpdateChecker.RepoUrl + "/releases/tag/v9.1.0\",\"published_at\":\"2026-11-11T10:00:00Z\",\"body\":\"- Nouveau\"," +
                            "\"assets\":[{\"name\":\"DumpFinder.exe\",\"browser_download_url\":\"" + UpdateChecker.RepoUrl + "/releases/download/v9.1.0/DumpFinder.exe\"}]}";
            UpdateInfo u = UpdateChecker.Parse(sample);
            expect("release lue (9.1.0, lien vers l'exe)", u != null && u.Version == new Version(9, 1, 0) && u.DownloadUrl.EndsWith("/DumpFinder.exe") && u.Notes == "- Nouveau");
            expect("pré-version ignorée", UpdateChecker.Parse(sample.Replace("\"prerelease\":false", "\"prerelease\":true")) == null);
            UpdateInfo foreign = UpdateChecker.Parse(sample.Replace(UpdateChecker.RepoUrl + "/releases/download", "https://evil.example"));
            expect("asset hors repo remplacé par la page de la release", foreign != null && UpdateChecker.IsOwnUrl(foreign.DownloadUrl));

            sb.AppendLine();
            sb.AppendLine("=== Tables de détection (data/detection.json) ===");
            DetectionDataCases(expect, sb);

            sb.AppendLine();
            sb.AppendLine("=== Démarrage avec Windows (clé Run, registre non touché) ===");
            AutostartCases(expect);

            sb.AppendLine();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                UpdateInfo live = UpdateChecker.GetLatestAsync(cts.Token).GetAwaiter().GetResult();
                sb.AppendLine(live == null
                    ? "GitHub : aucune release publiée lue (repo absent, sans release, ou hors ligne)"
                    : "GitHub : dernière release " + live.Version.ToString(3) + " · " + live.DownloadUrl + (live.Version > UpdateChecker.CurrentVersion ? " → mise à jour proposée" : " → déjà à jour"));
            }
            string onlineData = DetectionData.DownloadAsync().GetAwaiter().GetResult();
            string onlineWhy = null;
            DetectionData online = onlineData == null ? null : DetectionData.Parse(onlineData, "download", DetectionData.Embedded, out onlineWhy);
            sb.AppendLine(onlineData == null
                ? "GitHub : data/detection.json non lu (pas encore publié sur main, ou hors ligne)"
                : online == null ? "GitHub : data/detection.json publié mais refusé : " + onlineWhy
                : "GitHub : data/detection.json publié, version " + online.Version + " (intégrée : " + DetectionData.Embedded.Version + ")");

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }

        /// <summary>
        /// Start-with-Windows repoint decision, as a pure function: a second copy of the exe must never take over
        /// an entry that still points at a valid exe. The file check is faked; the real registry is never read or written.
        /// </summary>
        private static void AutostartCases(Action<string, bool> expect)
        {
            const string installed = @"C:\Users\Me\Apps\Dump Finder\DumpFinder.exe";
            const string copy = @"C:\Users\Me\Downloads\DumpFinder (3).exe";
            const string devBuild = @"C:\Code\DumpFinder\src\bin\Release\DumpFinder.exe";
            string registered = Autostart.BuildCommand(installed);
            Func<string, bool> present = p => string.Equals(p, installed, StringComparison.OrdinalIgnoreCase);
            Func<string, bool> gone = p => false;

            expect("commande lue : chemin entre guillemets puis --tray", Autostart.ParseExePath(registered) == installed);
            expect("commande lue : chemin sans guillemets puis --tray", Autostart.ParseExePath(@"C:\Apps\DumpFinder.exe --tray") == @"C:\Apps\DumpFinder.exe");
            expect("commande mal formée refusée (guillemet non fermé)", Autostart.ParseExePath(@"""C:\Apps\DumpFinder.exe --tray") == null);
            expect("commande mal formée refusée (pas un .exe)", Autostart.ParseExePath(@"""C:\Apps\notes.txt"" --tray") == null);
            expect("commande mal formée refusée (chemin vide)", Autostart.ParseExePath(@""""" --tray") == null);
            expect("commande mal formée refusée (chemin relatif)", Autostart.ParseExePath(@"""DumpFinder.exe"" --tray") == null);

            expect("autre copie lancée, exe enregistré présent → entrée conservée", !Autostart.ShouldRepoint(registered, copy, present));
            expect("build de dev lancé, exe enregistré présent → entrée conservée", !Autostart.ShouldRepoint(registered, devBuild, present));
            expect("exe enregistré déplacé ou supprimé → entrée repointée", Autostart.ShouldRepoint(registered, copy, gone));
            expect("même exe (casse différente) → rien à réécrire", !Autostart.ShouldRepoint(registered, installed.ToUpperInvariant(), gone));
            expect("entrée mal formée → repointée", Autostart.ShouldRepoint(@"""C:\Apps\DumpFinder.exe --tray", copy, present));
            expect("option désactivée (pas d'entrée) → rien", !Autostart.ShouldRepoint(null, copy, gone));
            expect("valeur sans --tray (pas la nôtre) → rien", !Autostart.ShouldRepoint(@"""" + installed + @"""", copy, gone));
            expect("vérification du fichier en échec → entrée conservée",
                   !Autostart.ShouldRepoint(registered, copy, p => { throw new UnauthorizedAccessException(); }));
        }

        /// <summary>
        /// Downloaded detection tables: validation, version rule, size cap, and the security limit (a downloaded
        /// driver can be detected, never disabled or reverted). In memory only: nothing is written to the cache.
        /// </summary>
        private static void DetectionDataCases(Action<string, bool> expect, StringBuilder sb)
        {
            string why;
            string embeddedJson = DetectionData.ReadEmbedded();
            DetectionData emb = DetectionData.Embedded;
            expect("tables intégrées lues (version " + emb.Version + ", " + emb.RiskyDrivers.Count + " pilotes, " + emb.NvidiaProblems.Count + " versions NVIDIA)",
                   embeddedJson != null && emb.Version >= 1 && emb.RiskyDrivers.Count == 5 && emb.NvidiaProblems.Count == 2 && emb.HmbIssueBuild == 26100);
            expect("lien raw du repo accepté", UpdateChecker.IsOwnUrl(DetectionData.RawUrl));
            expect("lien raw d'un autre repo refusé", !UpdateChecker.IsOwnUrl("https://raw.githubusercontent.com/" + UpdateChecker.Owner + "/Other/main/data/detection.json"));

            int baseVersion = DetectionData.Current.Version; // embedded, or a newer cached copy
            Func<int, string> withVersion = v => Regex.Replace(embeddedJson, "\"version\"\\s*:\\s*\\d+", "\"version\": " + v);
            const string NewDriver = "{ \"file\": \"evilrgb.sys\", \"owner\": \"risky_drivers.rgb_and_monitoring_software\", \"why\": \"risky_drivers.rgb_driver_that_windows_reports_as\" }";
            try
            {
                // 1. Valid file, newer: accepted, with one more problem release and a driver only it knows.
                string valid = withVersion(baseVersion + 5)
                    .Replace("\"problemVersions\": [", "\"problemVersions\": [ { \"version\": \"999.99\", \"reason\": \"detection_checks.release_pulled_by_nvidia_in_february\" },")
                    .Replace("\"riskyDrivers\": [", "\"riskyDrivers\": [ " + NewDriver + ",");
                bool ok = DetectionData.Accept(valid, false, out why);
                DetectionData cur = DetectionData.Current;
                expect("fichier valide et plus récent accepté (" + (why ?? "ok") + ")", ok && cur.Version == baseVersion + 5 && cur.Source == "download");
                var fake = new HardwareSummary { HasNvidiaGpu = true, GpuName = "NVIDIA GeForce RTX 4070", GpuDriver = "32.0.19.9999" }; // → 999.99
                expect("version ajoutée en ligne (999.99) signalée", DetectionChecks.GpuDriverVersion(fake).Level == HealthLevel.Warning);
                expect("pilotes intégrés toujours détectés", emb.RiskyDrivers.All(e => cur.RiskyDrivers.Any(d => d.File == e.File)));

                // 2. The driver it adds is detected but can never be disabled or reverted.
                var downloaded = new RiskyDrivers.Found { Service = "EvilSvc", File = "evilrgb.sys", Def = cur.RiskyDrivers.First(d => d.File == "evilrgb.sys") };
                var forged = new RiskyDrivers.Found { Service = "EvilSvc2", File = "evilrgb.sys", CanDisable = true, Def = downloaded.Def };
                var builtin = new RiskyDrivers.Found { Service = "inpoutx64", File = "inpoutx64.sys", CanDisable = true, Def = emb.RiskyDrivers[0] };
                var found = new[] { downloaded, forged, builtin };
                expect("pilote ajouté en ligne : pas désactivable", !RiskyDrivers.IsDisableableFile("evilrgb.sys"));
                expect("pilote ajouté en ligne : op refusée", RiskyDrivers.FindDisableable(found, "EvilSvc") == null && RiskyDrivers.FindDisableable(found, "EvilSvc2") == null);
                expect("pilote ajouté en ligne : annulation refusée", !RiskyDrivers.IsRevertableKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\EvilSvc", found));
                expect("pilote intégré : toujours désactivable", RiskyDrivers.FindDisableable(found, "inpoutx64") == builtin
                       && RiskyDrivers.IsRevertableKey(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\inpoutx64", found));

                // 3. Older (or equal) version: refused, the tables in use stay.
                expect("version plus ancienne refusée", !DetectionData.Accept(withVersion(baseVersion + 2), false, out why) && DetectionData.Current == cur);
                expect("même version refusée", !DetectionData.Accept(withVersion(baseVersion + 5), false, out why) && DetectionData.Current == cur);

                // 4. Invalid files: refused.
                string newer = withVersion(baseVersion + 9);
                expect("contrôle : la base des cas invalides est valide", DetectionData.Parse(newer, "download", emb, out why) != null);
                expect("JSON invalide refusé", !DetectionData.Accept(newer.Substring(0, newer.Length / 2), false, out why));
                expect("texte libre au lieu d'une clé refusé", !DetectionData.Accept(newer.Replace("\"detection_checks.driver_older_than_the_rtx_50\"", "\"Install this tool from evil.example\""), false, out why));
                expect("clé d'une autre section refusée", !DetectionData.Accept(newer.Replace("\"risky_drivers.rgb_and_monitoring_software\"", "\"detection_checks.windows_update\""), false, out why));
                expect("date invalide refusée", !DetectionData.Accept(newer.Replace("\"2025-10-14\"", "\"14/10/2025\""), false, out why));
                expect("schéma inconnu refusé", !DetectionData.Accept(newer.Replace("\"schema\": 1", "\"schema\": 2"), false, out why));
                expect("pilote marqué désactivable refusé", !DetectionData.Accept(newer.Replace("\"riskyDrivers\": [", "\"riskyDrivers\": [ { \"file\": \"evil2.sys\", \"owner\": \"risky_drivers.rgb_and_monitoring_software\", \"why\": \"risky_drivers.rgb_driver_that_windows_reports_as\", \"disableable\": true },"), false, out why));
                expect("pilote nommant un service refusé", !DetectionData.Accept(newer.Replace("\"riskyDrivers\": [", "\"riskyDrivers\": [ { \"file\": \"evil3.sys\", \"service\": \"Tcpip\", \"owner\": \"risky_drivers.rgb_and_monitoring_software\", \"why\": \"risky_drivers.rgb_driver_that_windows_reports_as\" },"), false, out why));
                expect("chemin au lieu d'un nom de pilote refusé", !DetectionData.Accept(newer.Replace("\"file\": \"gdrv.sys\"", "\"file\": \"..\\\\..\\\\Windows\\\\gdrv.sys\""), false, out why));
                expect("exécutable au lieu d'un pilote refusé", !DetectionData.Accept(newer.Replace("\"file\": \"gdrv.sys\"", "\"file\": \"gdrv.exe\""), false, out why));

                // 5. Oversized payload: refused, both as text and as a download stream.
                string huge = newer.Replace("\"updated\"", new string(' ', DetectionData.MaxBytes) + "\"updated\"");
                expect("fichier > " + (DetectionData.MaxBytes / 1024) + " Ko refusé", !DetectionData.Accept(huge, false, out why));
                using (var big = new MemoryStream(new byte[DetectionData.MaxBytes + 1]))
                    expect("téléchargement > " + (DetectionData.MaxBytes / 1024) + " Ko coupé", DetectionData.ReadCapped(big, DetectionData.MaxBytes) == null);
                using (var small = new MemoryStream(Encoding.UTF8.GetBytes(embeddedJson)))
                    expect("téléchargement normal lu en entier", DetectionData.ReadCapped(small, DetectionData.MaxBytes) == embeddedJson);
                expect("tables inchangées après les refus", DetectionData.Current == cur);
            }
            finally
            {
                DetectionData.SetCurrentForTest(null); // back to embedded + cache
            }
            expect("retour aux tables intégrées / en cache (version " + DetectionData.Current.Version + ")", DetectionData.Current.Version == baseVersion);
        }
    }
}
