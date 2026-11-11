using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    /// <summary>Everything a report needs, gathered once by the UI.</summary>
    public class ReportData
    {
        public ScanResult Scan;
        public List<CrashEntry> Crashes;
        public int Score;
        public int CrashScore;
        public double? WindowsIndex;
        public List<ScanRecord> History;
        public List<JournalEntry> Journal;
    }

    /// <summary>
    /// Shareable reports: a self-contained HTML page (print-friendly), a JSON document for tooling,
    /// and a compact Markdown summary for forums.
    /// </summary>
    public static class ReportBuilder
    {
        private static string Version
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return v.Major + "." + v.Minor; }
        }

        private static string H(string s) { return WebUtility.HtmlEncode(s ?? ""); }
        private static string T(string fr, string en) { return Localizer.T(fr, en); }

        private static IEnumerable<CrashEntry> Recent(ReportData d)
        {
            DateTime now = DateTime.Now;
            return (d.Crashes ?? new List<CrashEntry>()).Where(c => (now - c.Time).TotalDays <= Stability.WindowDays);
        }

        // ==================== HTML ====================
        public static string Html(ReportData d)
        {
            var res = d.Scan;
            var hw = res.Hardware;
            var crashes = Recent(d).ToList();
            StabilityLevel lv = Stability.LevelOf(d.Score);
            string lvColor = lv == StabilityLevel.Stable ? "#0E8A57" : lv == StabilityLevel.Fragile ? "#A86400" : "#D33A33";
            string lvText = lv == StabilityLevel.Stable ? T("Stable", "Stable") : lv == StabilityLevel.Fragile ? T("Fragile", "Fragile") : T("Instable", "Unstable");

            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"").Append(Localizer.Current.ToString().ToLowerInvariant()).Append("\"><head><meta charset=\"utf-8\">");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.Append("<title>Dump Finder — ").Append(H(T("Rapport de diagnostic", "Diagnostic report"))).Append(" — ").Append(DateTime.Now.ToString("yyyy-MM-dd")).Append("</title>");
            sb.Append("<style>")
              .Append(":root{--ink:#14121F;--muted:#6F6B85;--line:#E5E2EF;--soft:#F6F5FB;--brand:#5B47F5;--bad:#D33A33;--warn:#A86400;--ok:#0E8A57}")
              .Append("*{box-sizing:border-box}body{margin:0;background:#fff;color:var(--ink);font:14px/1.55 'Segoe UI Variable Text','Segoe UI',system-ui,sans-serif}")
              .Append("main{max-width:980px;margin:0 auto;padding:40px 24px 64px}")
              .Append("header{display:flex;align-items:center;gap:14px;border-bottom:1px solid var(--line);padding-bottom:18px;margin-bottom:28px}")
              .Append("header h1{font-size:22px;margin:0;font-weight:650;letter-spacing:-.01em}header p{margin:2px 0 0;color:var(--muted)}")
              .Append("h2{font-size:17px;margin:32px 0 12px;font-weight:650}")
              .Append(".hero{display:flex;flex-wrap:wrap;gap:24px;align-items:center;background:var(--soft);border:1px solid var(--line);border-radius:14px;padding:20px 22px}")
              .Append(".score{font-size:44px;font-weight:650;line-height:1}.score small{display:block;font-size:12px;color:var(--muted);font-weight:500;margin-top:4px}")
              .Append(".pill{display:inline-block;font-size:12px;font-weight:600;padding:2px 9px;border-radius:999px;background:#fff;border:1px solid var(--line)}")
              .Append("table{width:100%;border-collapse:collapse;font-size:13px}th{text-align:left;color:var(--muted);font-weight:600;border-bottom:1px solid var(--line);padding:8px 10px}")
              .Append("td{border-bottom:1px solid var(--line);padding:8px 10px;vertical-align:top;overflow-wrap:break-word}td.mono{overflow-wrap:anywhere}code,.mono{font-family:'Cascadia Mono',Consolas,monospace;font-size:12px}")
              .Append(".card{border:1px solid var(--line);border-radius:12px;padding:14px 16px;margin-bottom:12px}.muted{color:var(--muted)}")
              .Append(".bad{color:var(--bad)}.warn{color:var(--warn)}.ok{color:var(--ok)}pre{background:var(--soft);border-radius:8px;padding:10px 12px;overflow:auto;font-size:12px}")
              .Append("dl{display:grid;grid-template-columns:max-content minmax(0,1fr);gap:4px 16px;margin:8px 0}dt{color:var(--muted)}dd{margin:0;overflow-wrap:anywhere}")
              .Append("footer{margin-top:40px;color:var(--muted);font-size:12px}@media (max-width:640px){main{padding:24px 16px 48px}table{font-size:12px}th,td{padding:6px}}@media print{main{padding:0}.card,.hero{break-inside:avoid}}")
              .Append("</style></head><body><main>");

            // Header with the brand mark
            sb.Append("<header><svg width=\"40\" height=\"40\" viewBox=\"0 0 512 512\" aria-hidden=\"true\"><defs>")
              .Append("<linearGradient id=\"b\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#1D1834\"/><stop offset=\"1\" stop-color=\"#0B0A13\"/></linearGradient>")
              .Append("<linearGradient id=\"i\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\"><stop offset=\"0\" stop-color=\"#A495FF\"/><stop offset=\"1\" stop-color=\"#6A55F7\"/></linearGradient>")
              .Append("<linearGradient id=\"f\" x1=\"0\" y1=\"0\" x2=\"1\" y2=\"0\"><stop offset=\"0\" stop-color=\"#FF5E62\"/><stop offset=\"1\" stop-color=\"#FFA24A\"/></linearGradient></defs>")
              .Append("<rect x=\"16\" y=\"16\" width=\"480\" height=\"480\" rx=\"124\" fill=\"url(#b)\"/><rect x=\"112\" y=\"132\" width=\"288\" height=\"64\" rx=\"22\" fill=\"url(#i)\"/>")
              .Append("<path d=\"M134 224H262L250 240L266 256L252 272L262 288H134a22 22 0 0 1-22-22V246a22 22 0 0 1 22-22Z\" fill=\"url(#i)\"/>")
              .Append("<path d=\"M292 214H414a22 22 0 0 1 22 22v20a22 22 0 0 1-22 22H292L282 262L296 246L280 230Z\" fill=\"url(#f)\"/>")
              .Append("<rect x=\"112\" y=\"316\" width=\"288\" height=\"64\" rx=\"22\" fill=\"url(#i)\" opacity=\".85\"/></svg>");
            sb.Append("<div><h1>").Append(H(T("Rapport de diagnostic", "Diagnostic report"))).Append("</h1><p>Dump Finder ").Append(Version).Append(" · ")
              .Append(H(DateTime.Now.ToString("f", CultureInfo.CurrentUICulture))).Append("</p></div></header>");

            // Summary
            sb.Append("<section class=\"hero\"><div class=\"score\" style=\"color:").Append(lvColor).Append("\">").Append(d.Score).Append("<small>/ 100 ").Append(H(T("stabilité", "stability"))).Append("</small></div><div>")
              .Append("<span class=\"pill\" style=\"color:").Append(lvColor).Append("\">").Append(H(lvText)).Append("</span> ");
            if (d.WindowsIndex.HasValue) sb.Append("<span class=\"pill\">").Append(H(T("Indice Windows ", "Windows index "))).Append(d.WindowsIndex.Value.ToString("0.0")).Append("/10</span>");
            sb.Append("<h2 style=\"margin:10px 0 4px\">").Append(H(res.DiagnosisTitle)).Append("</h2><div class=\"muted\">").Append(H(res.DiagnosisBody)).Append("</div>")
              .Append("<div class=\"muted\" style=\"margin-top:8px\">").Append(crashes.Count).Append(H(T(" crash(s) sur 30 jours, dont ", " crash(es) in 30 days, "))).Append(crashes.Count(c => Stability.IsSevere(c.Category)))
              .Append(H(T(" grave(s).", " severe."))).Append("</div></div></section>");

            // System
            sb.Append("<h2>").Append(H(T("Système", "System"))).Append("</h2><dl>");
            Dl(sb, "CPU", hw.CpuName); Dl(sb, "GPU", hw.GpuName + " — " + T("pilote ", "driver ") + hw.GpuDriver);
            Dl(sb, T("Carte mère", "Motherboard"), hw.BoardName); Dl(sb, "Windows", hw.OsVersion);
            Dl(sb, T("Dernier démarrage", "Last boot"), hw.LastBoot.ToString("g", CultureInfo.CurrentUICulture));
            sb.Append("</dl>");

            // Crash timeline
            sb.Append("<h2>").Append(H(T("Crashs des 30 derniers jours", "Crashes in the last 30 days"))).Append("</h2>");
            if (crashes.Count == 0) sb.Append("<p class=\"ok\">").Append(H(T("Aucun crash enregistré.", "No crash recorded."))).Append("</p>");
            else
            {
                sb.Append("<table><tr><th>").Append(H(T("Date", "Date"))).Append("</th><th>").Append(H(T("Type", "Type"))).Append("</th><th>")
                  .Append(H(T("Description", "Description"))).Append("</th><th>").Append(H(T("Code · module", "Code · module"))).Append("</th></tr>");
                foreach (var c in crashes.Take(80))
                {
                    string cls = Stability.IsSevere(c.Category) ? "bad" : (c.Category == CrashCategory.AppCrash ? "muted" : "warn");
                    sb.Append("<tr><td class=\"mono\">").Append(H(c.Time.ToString("yyyy-MM-dd HH:mm"))).Append("</td><td class=\"").Append(cls).Append("\">")
                      .Append(H(Localizer.CategoryName(c.Category))).Append("</td><td>").Append(H(c.Title)).Append("</td><td class=\"mono\">").Append(H(c.TechLine)).Append("</td></tr>");
                }
                sb.Append("</table>");
            }

            // Decoded dumps
            var dumps = crashes.Where(c => c.Dump != null && CrashCatalog.IsKernel(c.Dump)).ToList();
            if (dumps.Count > 0)
            {
                sb.Append("<h2>").Append(H(T("Écrans bleus décodés", "Decoded blue screens"))).Append("</h2>");
                foreach (var c in dumps)
                {
                    var m = c.Dump;
                    sb.Append("<div class=\"card\"><strong>").Append(H(m.BugCheckOrException)).Append("</strong> <span class=\"muted\">· ").Append(H(c.Time.ToString("g", CultureInfo.CurrentUICulture))).Append("</span>");
                    sb.Append("<p>").Append(H(CrashCatalog.PlainExplanation(c))).Append("</p><dl>");
                    Dl(sb, T("Module fautif", "Faulting module"), m.PrimaryCulpritModule);
                    Dl(sb, T("Preuve", "Evidence"), T(m.CulpritEvidenceFr, m.CulpritEvidenceEn));
                    if (m.StackModules != null && m.StackModules.Count > 0) Dl(sb, T("Pilotes sur la pile", "Drivers on the stack"), string.Join(" → ", m.StackModules));
                    if (m.BugCheckParameters != null) Dl(sb, T("Paramètres", "Parameters"), string.Join(", ", m.BugCheckParameters));
                    Dl(sb, T("Fichier", "File"), m.FileName);
                    var drv = DriverInventory.FindByImage(res.Drivers, m.PrimaryCulpritModule);
                    if (drv != null) Dl(sb, T("Pilote installé", "Installed driver"), drv.DeviceName + " — " + drv.Provider + " " + drv.Version);
                    sb.Append("</dl>");
                    var deep = DeepAnalyzer.TryLoadCached(!string.IsNullOrEmpty(m.LocalCopyPath) ? m.LocalCopyPath : m.FullPath);
                    if (deep != null && deep.Ok)
                    {
                        sb.Append("<div class=\"muted\">WinDbg <code>!analyze -v</code> : <code>").Append(H(deep.BucketId)).Append("</code></div>");
                        if (deep.StackFrames != null && deep.StackFrames.Count > 0) sb.Append("<pre>").Append(H(string.Join("\n", deep.StackFrames))).Append("</pre>");
                    }
                    sb.Append("</div>");
                }
            }

            // Fixes
            var fixes = (res.Fixes ?? new List<RegistryFixItem>()).Where(f => f.IsApplicableTo(hw)).ToList();
            sb.Append("<h2>").Append(H(T("Protections", "Protections"))).Append("</h2><table><tr><th>").Append(H(T("Correctif", "Fix"))).Append("</th><th>")
              .Append(H(T("État", "Status"))).Append("</th><th>").Append(H(T("Valeur actuelle", "Current value"))).Append("</th></tr>");
            foreach (var f in fixes)
            {
                string st = f.Status == FixStatus.Applied ? "<span class=\"ok\">" + H(T("Actif", "Active")) + "</span>"
                          : f.Status == FixStatus.Recommended ? "<span class=\"warn\">" + H(T("Recommandé", "Recommended")) + "</span>"
                          : "<span class=\"muted\">" + H(T("Optionnel", "Optional")) + "</span>";
                sb.Append("<tr><td>").Append(H(f.Title)).Append("</td><td>").Append(st).Append("</td><td class=\"mono\">").Append(H(f.CurrentValue)).Append("</td></tr>");
            }
            sb.Append("</table>");

            // Drivers of interest
            var drivers = (res.Drivers ?? new List<DriverInfo>())
                .Where(x => x.IsOld || crashes.Any(c => c.Dump != null && string.Equals(c.Dump.PrimaryCulpritModule, x.ImageName, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (drivers.Count > 0)
            {
                sb.Append("<h2>").Append(H(T("Pilotes à surveiller", "Drivers worth a look"))).Append("</h2><table><tr><th>").Append(H(T("Périphérique", "Device"))).Append("</th><th>")
                  .Append(H(T("Éditeur · version", "Provider · version"))).Append("</th><th>").Append(H(T("Date", "Date"))).Append("</th></tr>");
                foreach (var x in drivers)
                    sb.Append("<tr><td>").Append(H(x.DeviceName)).Append("</td><td class=\"mono\">").Append(H(x.Provider + " " + x.Version)).Append("</td><td class=\"")
                      .Append(x.IsOld ? "warn" : "").Append("\">").Append(x.Date.HasValue ? H(x.Date.Value.ToString("d", Localizer.Culture)) : "").Append("</td></tr>");
                sb.Append("</table>");
            }

            // Journal
            var journal = (d.Journal ?? new List<JournalEntry>()).Take(20).ToList();
            if (journal.Count > 0)
            {
                sb.Append("<h2>").Append(H(T("Modifications faites par Dump Finder", "Changes made by Dump Finder"))).Append("</h2><table>");
                foreach (var j in journal)
                    sb.Append("<tr><td class=\"mono\">").Append(H(j.Time.ToString("yyyy-MM-dd HH:mm"))).Append("</td><td>").Append(H(j.Title)).Append("</td><td class=\"")
                      .Append(j.Ok ? (j.RevertedAt.HasValue ? "muted" : "ok") : "bad").Append("\">")
                      .Append(H(!j.Ok ? T("Échec", "Failed") : j.RevertedAt.HasValue ? T("Annulé", "Undone") : T("Appliqué", "Applied"))).Append("</td></tr>");
                sb.Append("</table>");
            }

            sb.Append("<footer>").Append(H(T("Généré localement par Dump Finder. Aucune donnée n'a été envoyée.", "Generated locally by Dump Finder. No data was sent anywhere."))).Append("</footer>");
            sb.Append("</main></body></html>");
            return sb.ToString();
        }

        private static void Dl(StringBuilder sb, string k, string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return;
            sb.Append("<dt>").Append(H(k)).Append("</dt><dd>").Append(H(v)).Append("</dd>");
        }

        // ==================== JSON ====================
        public static string Json(ReportData d)
        {
            var res = d.Scan;
            var hw = res.Hardware;
            var doc = new Dictionary<string, object>
            {
                { "generator", "Dump Finder " + Version },
                { "generatedAt", DateTime.Now.ToString("o") },
                { "score", d.Score },
                { "crashScore", d.CrashScore },
                { "windowsReliabilityIndex", d.WindowsIndex },
                { "diagnosis", new Dictionary<string, object> { { "title", res.DiagnosisTitle }, { "body", res.DiagnosisBody }, { "healthy", res.IsHealthy } } },
                { "hardware", new Dictionary<string, object> {
                    { "cpu", hw.CpuName }, { "gpu", hw.GpuName }, { "gpuDriver", hw.GpuDriver }, { "board", hw.BoardName },
                    { "os", hw.OsVersion }, { "lastBoot", hw.LastBoot.ToString("o") } } },
                { "crashes", Recent(d).Select(c => CrashJson(c)).ToList() },
                { "fixes", (res.Fixes ?? new List<RegistryFixItem>()).Select(f => new Dictionary<string, object> {
                    { "id", f.Id }, { "title", f.Title }, { "status", f.Status.ToString() }, { "currentValue", f.CurrentValue }, { "applicable", f.IsApplicableTo(hw) } }).ToList() },
                { "drivers", (res.Drivers ?? new List<DriverInfo>()).Select(x => new Dictionary<string, object> {
                    { "device", x.DeviceName }, { "class", x.DeviceClass }, { "provider", x.Provider }, { "version", x.Version },
                    { "date", x.Date.HasValue ? x.Date.Value.ToString("yyyy-MM-dd") : null }, { "image", x.ImageName } }).ToList() },
                { "reliability", (res.Reliability ?? new List<ReliabilityPoint>()).Select(p => new Dictionary<string, object> { { "date", p.Date.ToString("yyyy-MM-dd") }, { "index", p.Index } }).ToList() },
                { "history", (d.History ?? new List<ScanRecord>()).Select(s => new Dictionary<string, object> { { "time", s.Time.ToString("o") }, { "score", s.Score }, { "crashes30", s.Crashes30 } }).ToList() },
                { "journal", (d.Journal ?? new List<JournalEntry>()).Select(j => new Dictionary<string, object> {
                    { "time", j.Time.ToString("o") }, { "op", j.Op }, { "title", j.Title }, { "ok", j.Ok }, { "reverted", j.RevertedAt.HasValue },
                    { "changes", (j.Changes ?? new List<ChangeRecord>()).Select(c => c.Describe()).ToList() } }).ToList() }
            };
            return PrettyJson(new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(doc));
        }

        private static Dictionary<string, object> CrashJson(CrashEntry c)
        {
            var o = new Dictionary<string, object>
            {
                { "time", c.Time.ToString("o") }, { "category", c.Category.ToString() }, { "title", c.Title }, { "code", c.TechLine }
            };
            if (c.Event != null) o["event"] = new Dictionary<string, object> { { "source", c.Event.Source }, { "id", c.Event.CodeOrId }, { "details", c.Event.DetailsEn } };
            if (c.Dump != null)
            {
                var m = c.Dump;
                var dump = new Dictionary<string, object>
                {
                    { "file", m.FileName }, { "type", m.DumpType }, { "code", m.BugCheckOrException }, { "culprit", m.PrimaryCulpritModule },
                    { "evidence", m.CulpritEvidenceEn }, { "parameters", m.BugCheckParameters }, { "stackModules", m.StackModules }, { "structured", m.StructuredParse }
                };
                var deep = DeepAnalyzer.TryLoadCached(!string.IsNullOrEmpty(m.LocalCopyPath) ? m.LocalCopyPath : m.FullPath);
                if (deep != null && deep.Ok)
                    dump["windbg"] = new Dictionary<string, object> { { "bucket", deep.BucketId }, { "image", deep.ImageName }, { "symbol", deep.SymbolName }, { "stack", deep.StackFrames } };
                o["dump"] = dump;
            }
            return o;
        }

        /// <summary>Minimal JSON pretty-printer (JavaScriptSerializer writes one line).</summary>
        private static string PrettyJson(string json)
        {
            var sb = new StringBuilder();
            int indent = 0;
            bool inString = false;
            for (int i = 0; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"' && (i == 0 || json[i - 1] != '\\')) inString = !inString;
                if (inString) { sb.Append(ch); continue; }
                switch (ch)
                {
                    case '{': case '[':
                        sb.Append(ch).Append('\n').Append(new string(' ', ++indent * 2)); break;
                    case '}': case ']':
                        sb.Append('\n').Append(new string(' ', --indent * 2)).Append(ch); break;
                    case ',':
                        sb.Append(ch).Append('\n').Append(new string(' ', indent * 2)); break;
                    case ':':
                        sb.Append(": "); break;
                    default:
                        sb.Append(ch); break;
                }
            }
            return sb.ToString();
        }

        // ==================== MARKDOWN ====================
        public static string Markdown(ReportData d)
        {
            var res = d.Scan;
            var hw = res.Hardware;
            var crashes = Recent(d).ToList();
            var sb = new StringBuilder();
            sb.AppendLine("## " + T("Diagnostic de mon PC", "My PC diagnosis") + " — Dump Finder " + Version);
            sb.AppendLine();
            sb.AppendLine("**" + res.DiagnosisTitle + "**  ");
            sb.AppendLine(T("Score de stabilité", "Stability score") + T(" : **", ": **") + d.Score + "/100**" + (d.WindowsIndex.HasValue ? " · " + T("indice Windows", "Windows index") + " " + d.WindowsIndex.Value.ToString("0.0") + "/10" : ""));
            sb.AppendLine();
            sb.AppendLine("| | |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| CPU | " + hw.CpuName + " |");
            sb.AppendLine("| GPU | " + hw.GpuName + " (" + T("pilote", "driver") + " " + hw.GpuDriver + ") |");
            if (!string.IsNullOrEmpty(hw.BoardName)) sb.AppendLine("| " + T("Carte mère", "Motherboard") + " | " + hw.BoardName + " |");
            if (!string.IsNullOrEmpty(hw.OsVersion)) sb.AppendLine("| Windows | " + hw.OsVersion + " |");
            sb.AppendLine();
            sb.AppendLine("### " + T("Crashs (30 jours)", "Crashes (30 days)") + " — " + crashes.Count);
            sb.AppendLine();
            sb.AppendLine("| " + T("Date", "Date") + " | " + T("Type", "Type") + " | " + T("Code · module", "Code · module") + " |");
            sb.AppendLine("|---|---|---|");
            foreach (var c in crashes.Where(c => c.Category != CrashCategory.AppCrash).Take(15))
                sb.AppendLine("| " + c.Time.ToString("yyyy-MM-dd HH:mm") + " | " + Localizer.CategoryName(c.Category) + " | `" + c.TechLine + "` |");
            int apps = crashes.Count(c => c.Category == CrashCategory.AppCrash);
            if (apps > 0) sb.AppendLine().AppendLine("_+ " + apps + " " + T("crash(s) d'applications", "application crash(es)") + "_");

            var suspects = crashes.Where(c => c.Dump != null && !string.IsNullOrEmpty(c.Dump.PrimaryCulpritModule))
                .GroupBy(c => c.Dump.PrimaryCulpritModule, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count()).Take(5).ToList();
            if (suspects.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("### " + T("Modules mis en cause", "Implicated modules"));
                foreach (var g in suspects) sb.AppendLine("- `" + g.Key + "` × " + g.Count());
            }
            sb.AppendLine();
            sb.AppendLine("_" + T("Généré par Dump Finder", "Generated by Dump Finder") + "_");
            return sb.ToString();
        }
    }
}
