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
            // Since 6.4.1 the exe is published as DumpFinder-<version>.exe: the download button must still point to it.
            UpdateInfo named = UpdateChecker.Parse(sample.Replace("DumpFinder.exe", "DumpFinder-9.1.0.exe"));
            expect("exe nommé avec sa version (DumpFinder-9.1.0.exe) trouvé", named != null && named.DownloadUrl == UpdateChecker.RepoUrl + "/releases/download/v9.1.0/DumpFinder-9.1.0.exe");
            UpdateInfo foreign = UpdateChecker.Parse(sample.Replace(UpdateChecker.RepoUrl + "/releases/download", "https://evil.example"));
            expect("asset hors repo remplacé par la page de la release", foreign != null && UpdateChecker.IsOwnUrl(foreign.DownloadUrl));

            sb.AppendLine();
            sb.AppendLine("=== Mise à jour dans l'app ===");
            SelfUpdateCases(expect);

            sb.AppendLine();
            sb.AppendLine("=== Tables de détection (data/detection.json) ===");
            DetectionDataCases(expect, sb);

            sb.AppendLine();
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                UpdateInfo live = UpdateChecker.GetLatestAsync(cts.Token).GetAwaiter().GetResult();
                sb.AppendLine(live == null
                    ? "GitHub : aucune release publiée lue (repo absent, sans release, ou hors ligne)"
                    : "GitHub : dernière release " + live.Version.ToString(3) + " · " + live.DownloadUrl + (live.Version > UpdateChecker.CurrentVersion ? " → mise à jour proposée" : " → déjà à jour")
                      + " · installable dans l'app : " + (SelfUpdate.CannotInstall(live) ?? "oui (" + live.ExeSize + " octets, SHA-256 " + live.ExeSha256.Substring(0, 12) + "…)"));
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
        /// Downloaded detection tables: validation, version rule, size cap, and the security limit (a downloaded
        /// driver can be detected, never disabled or reverted). In memory only: nothing is written to the cache.
        /// </summary>
        /// <summary>
        /// In-app update, without downloading anything: checksum and size read from the release, allowed download
        /// hosts, the file checks (on a copy of this exe), the swap with rollback and the cleanup, in a temp folder.
        /// </summary>
        private static void SelfUpdateCases(Action<string, bool> expect)
        {
            string hex = new string('a', 64);
            expect("empreinte GitHub lue (sha256:…), valeurs invalides refusées",
                UpdateChecker.Sha256Of("sha256:" + hex.ToUpperInvariant()) == hex && UpdateChecker.Sha256Of("sha256:abc") == null
                && UpdateChecker.Sha256Of("md5:" + hex) == null && UpdateChecker.Sha256Of("sha256:" + new string('g', 64)) == null && UpdateChecker.Sha256Of(null) == null);

            string withDigest = "{\"tag_name\":\"v9.1.0\",\"html_url\":\"" + UpdateChecker.RepoUrl + "/releases/tag/v9.1.0\",\"draft\":false,\"prerelease\":false," +
                                "\"assets\":[{\"name\":\"DumpFinder-9.1.0.exe\",\"size\":9476096,\"digest\":\"sha256:" + hex + "\",\"browser_download_url\":\"" + UpdateChecker.RepoUrl + "/releases/download/v9.1.0/DumpFinder-9.1.0.exe\"}]}";
            UpdateInfo u = UpdateChecker.Parse(withDigest);
            UpdateInfo noDigest = UpdateChecker.Parse(withDigest.Replace(",\"digest\":\"sha256:" + hex + "\"", ""));
            UpdateInfo foreign = UpdateChecker.Parse(withDigest.Replace(UpdateChecker.RepoUrl + "/releases/download", "https://evil.example"));
            expect("release installable dans l'app seulement avec un exe du repo et son empreinte",
                u != null && u.ExeSha256 == hex && u.ExeSize == 9476096 && SelfUpdate.CannotInstall(u) == null
                && SelfUpdate.CannotInstall(noDigest) != null && foreign.ExeUrl == null && SelfUpdate.CannotInstall(foreign) != null);
            expect("téléchargement limité aux serveurs GitHub en HTTPS",
                SelfUpdate.IsDownloadHost(new Uri("https://github.com/x")) && SelfUpdate.IsDownloadHost(new Uri("https://release-assets.githubusercontent.com/x"))
                && SelfUpdate.IsDownloadHost(new Uri("https://objects.githubusercontent.com/x")) && !SelfUpdate.IsDownloadHost(new Uri("http://github.com/x"))
                && !SelfUpdate.IsDownloadHost(new Uri("https://evil.example/x")) && !SelfUpdate.IsDownloadHost(new Uri("https://github.com.evil.example/x")));

            string dir = Path.Combine(Path.GetTempPath(), "dumpfinder-update-selftest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                // File checks on a copy of this exe: right size, checksum, product and version accepted; anything else refused.
                string copy = Path.Combine(dir, "copy.download");
                File.Copy(SelfUpdate.ExePath, copy);
                var me = new UpdateInfo { Version = UpdateChecker.CurrentVersion, ExeSize = new FileInfo(copy).Length, ExeSha256 = SelfUpdate.Sha256(copy) };
                var otherHash = new UpdateInfo { Version = me.Version, ExeSize = me.ExeSize, ExeSha256 = hex };
                var otherVersion = new UpdateInfo { Version = new Version(me.Version.Major + 1, 0, 0), ExeSize = me.ExeSize, ExeSha256 = me.ExeSha256 };
                var otherSize = new UpdateInfo { Version = me.Version, ExeSize = me.ExeSize + 1, ExeSha256 = me.ExeSha256 };
                expect("fichier vérifié : accepté s'il est identique, refusé si empreinte, version ou taille diffèrent",
                    SelfUpdate.Verify(copy, me) == null && SelfUpdate.Verify(copy, otherHash) != null && SelfUpdate.Verify(copy, otherVersion) != null && SelfUpdate.Verify(copy, otherSize) != null);

                // Swap: the new file takes the exe's name, the old one is kept aside, then cleaned up.
                string exe = Path.Combine(dir, "App.exe"), dl = Path.Combine(dir, "App.download");
                File.WriteAllText(exe, "old");
                File.WriteAllText(dl, "new");
                string old = SelfUpdate.Swap(dl, exe);
                bool swapped = File.ReadAllText(exe) == "new" && File.ReadAllText(old) == "old" && !File.Exists(dl) && Path.GetFileName(old).StartsWith("App.old_", StringComparison.Ordinal);
                int cleaned = SelfUpdate.CleanupOld(exe);
                expect("échange : le nouvel exe prend le nom, l'ancien est mis de côté puis supprimé au démarrage suivant", swapped && cleaned == 1 && !File.Exists(old) && File.Exists(exe));

                // A failed swap (the new file vanished) puts the current exe back.
                bool threw = false;
                try { SelfUpdate.Swap(Path.Combine(dir, "missing.download"), exe); } catch (UpdateException) { threw = true; }
                expect("échange raté : l'exe actuel est remis en place", threw && File.ReadAllText(exe) == "new" && Directory.GetFiles(dir, "App.old_*").Length == 0);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch (Exception) { }
            }
        }

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
