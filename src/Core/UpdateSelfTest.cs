using System;
using System.Text;
using System.Threading;

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
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
            {
                UpdateInfo live = UpdateChecker.GetLatestAsync(cts.Token).GetAwaiter().GetResult();
                sb.AppendLine(live == null
                    ? "GitHub : aucune release publiée lue (repo absent, sans release, ou hors ligne)"
                    : "GitHub : dernière release " + live.Version.ToString(3) + " · " + live.DownloadUrl + (live.Version > UpdateChecker.CurrentVersion ? " → mise à jour proposée" : " → déjà à jour"));
            }

            sb.AppendLine();
            sb.AppendLine(bad == 0 ? "TOUT EST CONFORME" : bad + " ÉCART(S)");
            System.IO.File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(false));
            return bad;
        }
    }
}
