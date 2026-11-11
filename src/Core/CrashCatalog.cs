using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>
    /// One crash as the user sees it: a Windows event, the .dmp file written for it, or both.
    /// </summary>
    public class CrashEntry
    {
        public CrashEventItem Event { get; set; }
        public MinidumpReportItem Dump { get; set; }

        public DateTime Time
        {
            get { return Event != null ? Event.Timestamp : (Dump != null ? Dump.CrashTime : DateTime.MinValue); }
        }

        public CrashCategory Category
        {
            get
            {
                if (Event != null) return Event.Category;
                if (Dump != null && Dump.DumpType != null && Dump.DumpType.IndexOf("Kernel", StringComparison.OrdinalIgnoreCase) >= 0)
                    return CrashCategory.BSOD;
                return CrashCategory.AppCrash;
            }
        }

        public string Title
        {
            get
            {
                if (Event != null) return Localizer.T(Event.SummaryFr, Event.SummaryEn);
                if (Dump == null) return "";
                string who = !string.IsNullOrEmpty(Dump.ProcessName) ? Dump.ProcessName : Dump.FileName;
                return Localizer.L("Fichier de crash : ", "Crash dump: ", "Volcado: ", "Absturzabbild: ", "Dump di crash: ", "Dump de falha: ") + who;
            }
        }

        /// <summary>Short technical line: code · culprit module.</summary>
        public string TechLine
        {
            get
            {
                var parts = new List<string>();
                if (Event != null && !string.IsNullOrEmpty(Event.CodeOrId)) parts.Add(Event.CodeOrId);
                else if (Dump != null && !string.IsNullOrEmpty(Dump.BugCheckOrException)) parts.Add(Dump.BugCheckOrException);
                if (Dump != null && !string.IsNullOrEmpty(Dump.PrimaryCulpritModule)) parts.Add(Dump.PrimaryCulpritModule);
                else if (Event != null && !string.IsNullOrEmpty(Event.Source)) parts.Add(Event.Source);
                return string.Join(" · ", parts);
            }
        }

        public string Key
        {
            get
            {
                if (Event != null) return "ev|" + Event.Timestamp.Ticks + "|" + Event.CodeOrId;
                return "dmp|" + (Dump != null ? Dump.FullPath : "");
            }
        }
    }

    public static class CrashCatalog
    {
        /// <summary>
        /// Merges events and dump files. A dump is linked to an event when the event text names the
        /// file (Event 1001 does), otherwise when it was written shortly before the event was logged.
        /// Dumps without an event become entries of their own.
        /// </summary>
        public static List<CrashEntry> Build(ScanResult res)
        {
            var entries = new List<CrashEntry>();
            if (res == null) return entries;

            var dumps = res.Minidumps != null ? res.Minidumps.ToList() : new List<MinidumpReportItem>();
            var used = new HashSet<MinidumpReportItem>();

            foreach (var ev in res.Events ?? new List<CrashEventItem>())
            {
                var entry = new CrashEntry { Event = ev };
                string text = (ev.DetailsFr ?? "") + " " + (ev.DetailsEn ?? "");

                MinidumpReportItem match = dumps.FirstOrDefault(d => !used.Contains(d)
                    && !string.IsNullOrEmpty(d.FileName)
                    && text.IndexOf(d.FileName, StringComparison.OrdinalIgnoreCase) >= 0);

                if (match == null && (ev.Category == CrashCategory.BSOD || ev.Category == CrashCategory.AppCrash || ev.Category == CrashCategory.BrowserGpu))
                {
                    bool kernel = ev.Category == CrashCategory.BSOD;
                    match = dumps
                        .Where(d => !used.Contains(d) && IsKernel(d) == kernel)
                        .Where(d => (ev.Timestamp - d.CrashTime).TotalMinutes >= -5 && (ev.Timestamp - d.CrashTime).TotalMinutes <= 45)
                        .OrderBy(d => Math.Abs((ev.Timestamp - d.CrashTime).TotalMinutes))
                        .FirstOrDefault();
                }

                if (match != null)
                {
                    entry.Dump = match;
                    used.Add(match);
                }
                entries.Add(entry);
            }

            foreach (var d in dumps.Where(d => !used.Contains(d)))
                entries.Add(new CrashEntry { Dump = d });

            return entries.OrderByDescending(e => e.Time).ToList();
        }

        public static bool IsKernel(MinidumpReportItem d)
        {
            return d != null && d.DumpType != null && d.DumpType.IndexOf("Kernel", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Fix ids most likely to address this crash, best first.</summary>
        /// <summary>Stop code of the crash, from the decoded dump or the event's code (0 when unknown).</summary>
        public static uint StopCode(CrashEntry e)
        {
            if (e.Dump != null && e.Dump.BugCheckCode != 0) return e.Dump.BugCheckCode;
            if (e.Event == null || e.Event.Category != CrashCategory.BSOD || string.IsNullOrEmpty(e.Event.CodeOrId)) return 0;
            var m = System.Text.RegularExpressions.Regex.Match(e.Event.CodeOrId, @"0x([0-9A-Fa-f]{1,8})");
            uint code;
            return m.Success && uint.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out code) ? code : 0;
        }

        public static List<string> RelatedFixIds(CrashEntry e)
        {
            var ids = new List<string>();
            var bc = BugCheckCatalog.Get(StopCode(e));
            if (bc != null) ids.AddRange(bc.FixIds);
            string code = ((e.Event != null ? e.Event.CodeOrId : "") + " " + (e.Dump != null ? e.Dump.BugCheckOrException : "")).ToLowerInvariant();
            string module = (e.Dump != null ? e.Dump.PrimaryCulpritModule ?? "" : "").ToLowerInvariant();
            bool gpuCode = code.Contains("116") || code.Contains("117") || code.Contains("119") || code.Contains("141")
                           || module.Contains("nvlddmkm") || module.Contains("amdkmdag") || module.Contains("atikmdag") || module.Contains("igdkmd") || module.Contains("dxgkrnl");
            bool storageCode = code.Contains("0xef") || code.Contains("129") || code.Contains("153") || module.Contains("stornvme") || module.Contains("storport");

            switch (e.Category)
            {
                case CrashCategory.GpuDriver:
                    ids.AddRange(new[] { "mpo", "tdr", "wu_driver_lock", "amd_ulps", "amd_issue_detection", "hags", "shader_cache" });
                    break;
                case CrashCategory.BSOD:
                    if (gpuCode) ids.AddRange(new[] { "mpo", "tdr", "wu_driver_lock", "amd_ulps", "hags" });
                    else if (storageCode) ids.AddRange(new[] { "nvme_hmb", "pcie_aspm" });
                    else if (code.Contains("1a") || code.Contains("3b") || code.Contains("50")) ids.AddRange(new[] { "pagefile_auto", "sfc_repair" });
                    else ids.AddRange(new[] { "sfc_repair", "fast_startup" });
                    break;
                case CrashCategory.UnexpectedReboot:
                    ids.AddRange(new[] { "fast_startup", "pcie_aspm" });
                    break;
                case CrashCategory.StorageNvme:
                    ids.AddRange(new[] { "nvme_hmb", "pcie_aspm" });
                    break;
                case CrashCategory.HardwareWhea:
                    ids.AddRange(new[] { "pcie_aspm", "usb_suspend" });
                    break;
                case CrashCategory.MemoryExhaustion:
                    ids.AddRange(new[] { "pagefile_auto" });
                    break;
                case CrashCategory.BrowserGpu:
                    ids.AddRange(new[] { "chrome_graphite", "edge_graphite", "mpo", "discord_hwaccel" });
                    break;
                case CrashCategory.AppCrash:
                    if (gpuCode) ids.AddRange(new[] { "mpo", "tdr" });
                    break;
            }
            return ids.Distinct().ToList();
        }

        /// <summary>Plain-language explanation: the dump decoder's text when present, else the event details.</summary>
        public static string PlainExplanation(CrashEntry e)
        {
            if (e.Dump != null)
            {
                string s = Localizer.T(e.Dump.HumanExplanationFr, e.Dump.HumanExplanationEn);
                if (!string.IsNullOrWhiteSpace(s)) return s.Trim();
            }
            var bc = BugCheckCatalog.Get(StopCode(e));
            if (bc != null && bc.ExplanationFr != null) return Localizer.T(bc.ExplanationFr, bc.ExplanationEn);
            if (e.Event != null)
            {
                string d = Localizer.T(e.Event.DetailsFr, e.Event.DetailsEn) ?? "";
                // Event details often append the raw Windows message after a blank line: keep the human part.
                int cut = d.IndexOf("\n\n", StringComparison.Ordinal);
                if (cut < 0) cut = d.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                return (cut > 40 ? d.Substring(0, cut) : d).Trim();
            }
            return "";
        }

        /// <summary>Markdown summary ready to paste on Reddit / vendor forums.</summary>
        public static string ForumMarkdown(CrashEntry e, ScanResult res)
        {
            var sb = new StringBuilder();
            sb.AppendLine("**" + e.Title + "**");
            sb.AppendLine();
            sb.AppendLine("- When: " + e.Time.ToString("yyyy-MM-dd HH:mm"));
            if (e.Event != null) sb.AppendLine("- Event: `" + e.Event.Source + " / " + e.Event.CodeOrId + "`");
            if (e.Dump != null)
            {
                sb.AppendLine("- Dump: `" + e.Dump.FileName + "` (" + e.Dump.DumpType + ")");
                if (!string.IsNullOrEmpty(e.Dump.BugCheckOrException)) sb.AppendLine("- Bugcheck / exception: `" + e.Dump.BugCheckOrException + "`");
                if (!string.IsNullOrEmpty(e.Dump.PrimaryCulpritModule)) sb.AppendLine("- Suspected module: `" + e.Dump.PrimaryCulpritModule + "`");
                if (e.Dump.BugCheckParameters != null) sb.AppendLine("- Parameters: `" + string.Join(", ", e.Dump.BugCheckParameters) + "`");
                if (e.Dump.StackModules != null && e.Dump.StackModules.Count > 0) sb.AppendLine("- Drivers on stack: `" + string.Join(" -> ", e.Dump.StackModules) + "`");
                var deepF = DeepAnalyzer.TryLoadCached(!string.IsNullOrEmpty(e.Dump.LocalCopyPath) ? e.Dump.LocalCopyPath : e.Dump.FullPath);
                if (deepF != null && deepF.Ok)
                {
                    sb.AppendLine("- WinDbg bucket: `" + deepF.BucketId + "`");
                    if (deepF.StackFrames != null && deepF.StackFrames.Count > 0)
                    {
                        sb.AppendLine();
                        sb.AppendLine("```");
                        foreach (var fr in deepF.StackFrames.Take(10)) sb.AppendLine(fr);
                        sb.AppendLine("```");
                    }
                }
            }
            if (res != null && res.Hardware != null)
            {
                var hw = res.Hardware;
                sb.AppendLine();
                sb.AppendLine("**System**");
                sb.AppendLine("- CPU: " + hw.CpuName);
                sb.AppendLine("- GPU: " + hw.GpuName + " (driver " + hw.GpuDriver + ")");
                if (!string.IsNullOrEmpty(hw.BoardName)) sb.AppendLine("- Board: " + hw.BoardName);
                if (!string.IsNullOrEmpty(hw.OsVersion)) sb.AppendLine("- OS: " + hw.OsVersion);
            }
            if (res != null && res.Events != null)
            {
                int same = res.Events.Count(x => e.Event != null && x.CodeOrId == e.Event.CodeOrId);
                if (same > 1) sb.AppendLine("- Same error seen " + same + " times in the scanned period");
            }
            sb.AppendLine();
            sb.AppendLine("_Generated by Dump Finder_");
            return sb.ToString();
        }

        /// <summary>Question appended to the structured system prompt to focus the AI on one crash.</summary>
        public static string AiFocusQuestion(CrashEntry e)
        {
            var sb = new StringBuilder();
            sb.Append(Localizer.L(
                "Concentre-toi sur ce crash précis et explique en français simple la cause la plus probable, puis donne 3 actions concrètes classées par priorité. ",
                "Focus on this specific crash: explain the most likely root cause in plain language, then give 3 concrete actions ranked by priority. ",
                "Céntrate en este fallo: explica la causa más probable en lenguaje sencillo y da 3 acciones concretas por prioridad. ",
                "Konzentriere dich auf diesen Absturz: erkläre die wahrscheinlichste Ursache einfach und nenne 3 konkrete Maßnahmen nach Priorität. ",
                "Concentrati su questo crash: spiega la causa più probabile in modo semplice e indica 3 azioni concrete per priorità. ",
                "Foque neste travamento: explique a causa mais provável em linguagem simples e dê 3 ações concretas por prioridade. "));
            sb.Append("Crash: ").Append(e.Title).Append(" @ ").Append(e.Time.ToString("yyyy-MM-dd HH:mm")).Append(". ");
            if (e.Event != null) sb.Append("Event ").Append(e.Event.Source).Append(" / ").Append(e.Event.CodeOrId).Append(". ").Append(e.Event.DetailsEn).Append(" ");
            if (e.Dump != null)
            {
                sb.Append("Dump ").Append(e.Dump.FileName).Append(" (").Append(e.Dump.DumpType).Append("), code ")
                  .Append(e.Dump.BugCheckOrException).Append(", module ").Append(e.Dump.PrimaryCulpritModule)
                  .Append(", related: ").Append(e.Dump.RelatedModules).Append(".");
                if (e.Dump.StackModules != null && e.Dump.StackModules.Count > 0)
                    sb.Append(" Drivers on the raw stack: ").Append(string.Join(" -> ", e.Dump.StackModules)).Append(".");
                if (e.Dump.BugCheckParameters != null) sb.Append(" Parameters: ").Append(string.Join(", ", e.Dump.BugCheckParameters)).Append(".");
                string path = !string.IsNullOrEmpty(e.Dump.LocalCopyPath) ? e.Dump.LocalCopyPath : e.Dump.FullPath;
                var deep = DeepAnalyzer.TryLoadCached(path);
                if (deep != null && deep.Ok)
                    sb.Append(" WinDbg !analyze: bucket ").Append(deep.BucketId).Append(", symbol ").Append(deep.SymbolName)
                      .Append(", stack: ").Append(string.Join(" | ", deep.StackFrames ?? new List<string>())).Append(".");
            }
            return sb.ToString();
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            bool fr = Localizer.Current == AppLang.FR;
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + (fr ? " Ko" : " KB");
            return (bytes / (1024.0 * 1024.0)).ToString("0.0") + (fr ? " Mo" : " MB");
        }
    }
}
