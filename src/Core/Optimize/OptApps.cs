using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace WinCrashDoctor.Optimize
{
    public sealed class AppEntry
    {
        public string Name;          // Appx package name
        public string FullName;      // PackageFullName (what removal needs)
        public string Title;
        public string Note;
        public bool Suggested;       // checked by default (pure bloat)
    }

    /// <summary>
    /// Preinstalled Store apps, removed for the current user only (Remove-AppxPackage): no admin rights,
    /// nothing system-wide, and every app can be reinstalled from the Microsoft Store.
    /// Only packages on this list are ever offered.
    /// </summary>
    public static class OptApps
    {

        private sealed class Def
        {
            public string Name;
            public bool Suggested;
            public Func<string> Title, Note;
        }

        private static Def D(string name, bool suggested, Func<string> title, Func<string> note = null)
        {
            return new Def { Name = name, Suggested = suggested, Title = title, Note = note };
        }

        private static readonly Def[] Catalog =
        {
            // Checked by default: ads, demos, abandoned apps
            D("Clipchamp.Clipchamp", true, () => "Clipchamp", () => Localizer.S("opt_apps.online_video_editor")),
            D("Microsoft.BingNews", true, () => Localizer.S("opt_apps.news_microsoft_start")),
            D("Microsoft.BingSearch", true, () => Localizer.S("opt_apps.bing_search")),
            D("Microsoft.MicrosoftSolitaireCollection", true, () => "Microsoft Solitaire Collection", () => Localizer.S("opt_apps.with_ads")),
            D("Microsoft.Getstarted", true, () => Localizer.S("opt_apps.tips")),
            D("Microsoft.MicrosoftOfficeHub", true, () => "Microsoft 365 (Office)", () => Localizer.S("opt_apps.promotional_shortcut_not_office_itself")),
            D("Microsoft.WindowsFeedbackHub", true, () => Localizer.S("opt_apps.feedback_hub")),
            D("Microsoft.549981C3F5F10", true, () => "Cortana", () => Localizer.S("opt_apps.discontinued_by_microsoft")),
            D("Microsoft.Windows.DevHome", true, () => "Dev Home", () => Localizer.S("opt_apps.discontinued_by_microsoft")),
            D("Microsoft.MixedReality.Portal", true, () => Localizer.S("opt_apps.mixed_reality_portal")),
            D("Microsoft.Microsoft3DViewer", true, () => Localizer.S("opt_apps.3d_viewer")),
            D("7EE7776C.LinkedInforWindows", true, () => "LinkedIn"),
            D("BytedancePte.Ltd.TikTok", true, () => "TikTok"),
            D("king.com.CandyCrushSaga", true, () => "Candy Crush Saga"),
            D("king.com.CandyCrushSodaSaga", true, () => "Candy Crush Soda Saga"),

            // Offered, unchecked: useful to some people
            D("Microsoft.BingWeather", false, () => Localizer.S("opt_apps.weather")),
            D("Microsoft.Todos", false, () => "Microsoft To Do"),
            D("Microsoft.PowerAutomateDesktop", false, () => "Power Automate"),
            D("Microsoft.WindowsMaps", false, () => Localizer.S("opt_apps.maps")),
            D("Microsoft.OutlookForWindows", false, () => Localizer.S("opt_apps.new_outlook")),
            D("MSTeams", false, () => Localizer.S("opt_apps.microsoft_teams_personal")),
            D("Microsoft.Copilot", false, () => "Copilot"),
            D("Microsoft.YourPhone", false, () => Localizer.S("opt_apps.phone_link")),
            D("Microsoft.GamingApp", false, () => "Xbox", () => Localizer.S("opt_apps.needed_for_pc_game_pass")),
            D("Microsoft.XboxGamingOverlay", false, () => "Xbox Game Bar", () => Localizer.S("opt_apps.win_g_captures_and_fps_counter")),
            D("Microsoft.ZuneVideo", false, () => Localizer.S("opt_apps.movies_tv")),
            D("SpotifyAB.SpotifyMusic", false, () => "Spotify"),
            D("Disney.37853FC22B2CE", false, () => "Disney+"),
            D("AmazonVideo.PrimeVideo", false, () => "Prime Video")
        };

        /// <summary>Installed packages that are on the list (runs PowerShell, ~2 s).</summary>
        public static List<AppEntry> Scan()
        {
            var installed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string json = RunPowerShell("Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable } | Select-Object Name,PackageFullName | ConvertTo-Json -Compress", 30000);
            try
            {
                object parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json ?? "");
                var items = parsed as object[] ?? (parsed != null ? new[] { parsed } : new object[0]);
                foreach (var o in items)
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null) continue;
                    string n = Convert.ToString(d.ContainsKey("Name") ? d["Name"] : null);
                    string f = Convert.ToString(d.ContainsKey("PackageFullName") ? d["PackageFullName"] : null);
                    if (!string.IsNullOrEmpty(n) && !string.IsNullOrEmpty(f) && !installed.ContainsKey(n)) installed[n] = f;
                }
            }
            catch (Exception ex) { Log.Warn(ex); }

            return Catalog.Where(d => installed.ContainsKey(d.Name))
                          .Select(d => new AppEntry { Name = d.Name, FullName = installed[d.Name], Title = d.Title(), Note = d.Note != null ? d.Note() : null, Suggested = d.Suggested })
                          .ToList();
        }

        private static readonly Regex SafeFullName = new Regex(@"^[A-Za-z0-9._~\-]+$");

        /// <summary>Removes the given packages for the current user. Returns (removed, failed) package titles.</summary>
        public static void Remove(IList<AppEntry> apps, out List<string> removed, out List<string> failed)
        {
            removed = new List<string>();
            failed = new List<string>();
            var known = new HashSet<string>(Catalog.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
            var ok = apps.Where(a => known.Contains(a.Name) && SafeFullName.IsMatch(a.FullName ?? "")).ToList();
            if (ok.Count == 0) return;

            var sb = new StringBuilder();
            sb.Append("$ProgressPreference='SilentlyContinue'; foreach ($p in @(");
            sb.Append(string.Join(",", ok.Select(a => "'" + a.FullName + "'")));
            sb.Append(")) { try { Remove-AppxPackage -Package $p -ErrorAction Stop; \"OK|$p\" } catch { \"ERR|$p\" } }");
            string output = RunPowerShell(sb.ToString(), 180000) ?? "";
            foreach (AppEntry a in ok)
            {
                if (output.IndexOf("OK|" + a.FullName, StringComparison.OrdinalIgnoreCase) >= 0) removed.Add(a.Title);
                else failed.Add(a.Title);
            }
        }

        public static bool OneDriveInstalled
        {
            get
            {
                try
                {
                    return File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\OneDrive\OneDrive.exe"))
                        || File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft OneDrive\OneDrive.exe"));
                }
                catch { return false; }
            }
        }

        private static string RunPowerShell(string script, int timeoutMs)
        {
            try
            {
                string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
                var psi = new ProcessStartInfo
                {
                    FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"WindowsPowerShell\v1.0\powershell.exe"),
                    Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + encoded,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var p = Process.Start(psi))
                {
                    var outTask = p.StandardOutput.ReadToEndAsync();
                    p.StandardError.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs)) { try { p.Kill(); } catch { } return null; }
                    return outTask.Result;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return null; }
        }
    }
}
