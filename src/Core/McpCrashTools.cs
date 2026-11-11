using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>MCP tools built on the same crash catalog as the app (crash detail, report export).</summary>
    public static class McpCrashTools
    {
        private static ScanResult Scan()
        {
            return DiagnosticsEngine.RunScan(DateTime.Now.AddDays(-Stability.WindowDays));
        }

        /// <summary>Short stable id for a crash entry (its key can contain long event text).</summary>
        public static string IdOf(CrashEntry c)
        {
            using (var sha = SHA1.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(c.Key));
                return BitConverter.ToString(h, 0, 5).Replace("-", "").ToLowerInvariant();
            }
        }

        public static string CrashDetail(string crashId)
        {
            ScanResult res = Scan();
            var crashes = CrashCatalog.Build(res).Where(c => (DateTime.Now - c.Time).TotalDays <= Stability.WindowDays).ToList();
            var sb = new StringBuilder();

            if (string.IsNullOrEmpty(crashId))
            {
                sb.AppendLine("=== CRASHES, LAST 30 DAYS (" + crashes.Count + ") ===");
                foreach (var c in crashes.Take(60))
                    sb.AppendLine(string.Format("- crash_id: {0} | {1:yyyy-MM-dd HH:mm} | {2} | {3} | {4}{5}",
                        IdOf(c), c.Time, c.Category, c.Title, c.TechLine, c.Dump != null ? " | dump" : ""));
                sb.AppendLine();
                sb.AppendLine("Call again with crash_id for the full detail of one crash.");
                return sb.ToString();
            }

            var e = crashes.FirstOrDefault(c => IdOf(c) == crashId);
            if (e == null) return "Unknown crash_id '" + crashId + "'. Call without crash_id to list crashes.";

            sb.AppendLine("=== " + e.Title + " ===");
            sb.AppendLine("When: " + e.Time.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine("Category: " + e.Category);
            sb.AppendLine("Plain explanation: " + CrashCatalog.PlainExplanation(e));
            if (e.Event != null)
            {
                sb.AppendLine("Event: " + e.Event.Source + " / " + e.Event.CodeOrId);
                sb.AppendLine("Windows message: " + e.Event.DetailsEn);
            }
            if (e.Dump != null)
            {
                var d = e.Dump;
                sb.AppendLine();
                sb.AppendLine("--- Dump " + d.FileName + " (" + d.DumpType + ") ---");
                sb.AppendLine("Stop code: " + d.BugCheckOrException);
                if (d.BugCheckParameters != null) sb.AppendLine("Parameters: " + string.Join(", ", d.BugCheckParameters));
                sb.AppendLine("Faulting module: " + d.PrimaryCulpritModule);
                if (!string.IsNullOrEmpty(d.CulpritEvidenceEn)) sb.AppendLine("Evidence: " + d.CulpritEvidenceEn);
                if (d.StackModules != null && d.StackModules.Count > 0) sb.AppendLine("Drivers on the raw stack: " + string.Join(" -> ", d.StackModules));
                var drv = DriverInventory.FindByImage(res.Drivers, d.PrimaryCulpritModule);
                if (drv != null) sb.AppendLine("Installed driver: " + drv.DeviceName + " - " + drv.Provider + " " + drv.Version + (drv.Date.HasValue ? " (" + drv.Date.Value.ToString("yyyy-MM-dd") + ")" : ""));
                var deep = DeepAnalyzer.TryLoadCached(!string.IsNullOrEmpty(d.LocalCopyPath) ? d.LocalCopyPath : d.FullPath);
                if (deep != null && deep.Ok)
                {
                    sb.AppendLine("WinDbg !analyze: bucket " + deep.BucketId + ", symbol " + deep.SymbolName);
                    if (deep.StackFrames != null) sb.AppendLine("Stack:\n  " + string.Join("\n  ", deep.StackFrames.Take(12)));
                }
            }
            var fixes = CrashCatalog.RelatedFixIds(e)
                .Select(id => res.Fixes.FirstOrDefault(f => f.Id == id))
                .Where(f => f != null && f.IsApplicableTo(res.Hardware))
                .ToList();
            if (fixes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- Related fixes (wincrash_apply_fix) ---");
                foreach (var f in fixes) sb.AppendLine("- fix_id: " + f.Id + " | " + f.Status + " | " + f.Title + " | current: " + f.CurrentValue);
            }
            return sb.ToString();
        }

        public static string ExportReport(string format)
        {
            ScanResult res = Scan();
            var crashes = CrashCatalog.Build(res);
            DateTime now = DateTime.Now;
            var window = res.Events.Where(ev => (now - ev.Timestamp).TotalDays <= Stability.WindowDays).ToList();
            int own = Stability.Score(window, now);
            double? win = Reliability.Latest(res.Reliability);
            var data = new ReportData
            {
                Scan = res,
                Crashes = crashes,
                CrashScore = own,
                WindowsIndex = win,
                Score = Stability.Blend(own, win),
                History = History.Scans(),
                Journal = History.Journal()
            };

            switch (format)
            {
                case "json":
                    return ReportBuilder.Json(data);
                case "html":
                    string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DumpFinder", "reports");
                    Directory.CreateDirectory(dir);
                    string path = Path.Combine(dir, "DumpFinder_" + now.ToString("yyyyMMdd_HHmm") + ".html");
                    File.WriteAllText(path, ReportBuilder.Html(data), new UTF8Encoding(false));
                    return "HTML report saved: " + path;
                default:
                    return ReportBuilder.Markdown(data);
            }
        }
    }
}
