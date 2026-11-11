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
        private static string L(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

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
            D("Clipchamp.Clipchamp", true, () => "Clipchamp", () => L("éditeur vidéo en ligne", "online video editor", "editor de vídeo en línea", "Online-Videoeditor", "editor video online", "editor de vídeo online")),
            D("Microsoft.BingNews", true, () => L("Actualités (Microsoft Start)", "News (Microsoft Start)", "Noticias", "Nachrichten", "Notizie", "Notícias")),
            D("Microsoft.BingSearch", true, () => L("Recherche Bing", "Bing Search", "Búsqueda de Bing", "Bing-Suche", "Ricerca Bing", "Pesquisa Bing")),
            D("Microsoft.MicrosoftSolitaireCollection", true, () => "Microsoft Solitaire Collection", () => L("avec publicités", "with ads", "con anuncios", "mit Werbung", "con pubblicità", "com anúncios")),
            D("Microsoft.Getstarted", true, () => L("Astuces", "Tips", "Consejos", "Tipps", "Suggerimenti", "Dicas")),
            D("Microsoft.MicrosoftOfficeHub", true, () => "Microsoft 365 (Office)", () => L("raccourci promotionnel, pas Office lui-même", "promotional shortcut, not Office itself", "acceso promocional, no Office", "Werbeverknüpfung, nicht Office selbst", "collegamento promozionale, non Office", "atalho promocional, não o Office")),
            D("Microsoft.WindowsFeedbackHub", true, () => L("Hub de commentaires", "Feedback Hub", "Centro de opiniones", "Feedback-Hub", "Hub di Feedback", "Hub de Comentários")),
            D("Microsoft.549981C3F5F10", true, () => "Cortana", () => L("abandonnée par Microsoft", "discontinued by Microsoft", "abandonada por Microsoft", "von Microsoft eingestellt", "abbandonata da Microsoft", "descontinuada pela Microsoft")),
            D("Microsoft.Windows.DevHome", true, () => "Dev Home", () => L("abandonnée par Microsoft", "discontinued by Microsoft", "abandonada por Microsoft", "von Microsoft eingestellt", "abbandonata da Microsoft", "descontinuada pela Microsoft")),
            D("Microsoft.MixedReality.Portal", true, () => L("Portail de réalité mixte", "Mixed Reality Portal", "Portal de realidad mixta", "Mixed-Reality-Portal", "Portale realtà mista", "Portal de Realidade Misturada")),
            D("Microsoft.Microsoft3DViewer", true, () => L("Visionneuse 3D", "3D Viewer", "Visor 3D", "3D-Viewer", "Visualizzatore 3D", "Visualizador 3D")),
            D("7EE7776C.LinkedInforWindows", true, () => "LinkedIn"),
            D("BytedancePte.Ltd.TikTok", true, () => "TikTok"),
            D("king.com.CandyCrushSaga", true, () => "Candy Crush Saga"),
            D("king.com.CandyCrushSodaSaga", true, () => "Candy Crush Soda Saga"),

            // Offered, unchecked: useful to some people
            D("Microsoft.BingWeather", false, () => L("Météo", "Weather", "El Tiempo", "Wetter", "Meteo", "Clima")),
            D("Microsoft.Todos", false, () => "Microsoft To Do"),
            D("Microsoft.PowerAutomateDesktop", false, () => "Power Automate"),
            D("Microsoft.WindowsMaps", false, () => L("Cartes", "Maps", "Mapas", "Karten", "Mappe", "Mapas")),
            D("Microsoft.OutlookForWindows", false, () => L("Nouvel Outlook", "New Outlook", "Nuevo Outlook", "Neues Outlook", "Nuovo Outlook", "Novo Outlook")),
            D("MSTeams", false, () => L("Microsoft Teams (personnel)", "Microsoft Teams (personal)", "Microsoft Teams (personal)", "Microsoft Teams (privat)", "Microsoft Teams (personale)", "Microsoft Teams (pessoal)")),
            D("Microsoft.Copilot", false, () => "Copilot"),
            D("Microsoft.YourPhone", false, () => L("Lien avec Windows (téléphone)", "Phone Link", "Vincular al teléfono", "Smartphone-Link", "Collegamento al telefono", "Vincular ao Celular")),
            D("Microsoft.GamingApp", false, () => "Xbox", () => L("nécessaire pour le Game Pass PC", "needed for PC Game Pass", "necesaria para Game Pass", "für PC Game Pass nötig", "serve per Game Pass", "necessário para o Game Pass")),
            D("Microsoft.XboxGamingOverlay", false, () => "Xbox Game Bar", () => L("Win+G, captures et compteur FPS", "Win+G, captures and FPS counter", "Win+G, capturas y FPS", "Win+G, Aufnahmen und FPS", "Win+G, catture e FPS", "Win+G, capturas e FPS")),
            D("Microsoft.ZuneVideo", false, () => L("Films et TV", "Movies & TV", "Películas y TV", "Filme & TV", "Film e TV", "Filmes e TV")),
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
