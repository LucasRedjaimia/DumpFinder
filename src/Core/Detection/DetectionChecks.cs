using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace WinCrashDoctor.Detection
{
    /// <summary>Detection v2 checks shown under Hardware › Conflicts (Windows support, GPU driver version, dump capture, live dumps).</summary>
    public static class DetectionChecks
    {

        public static IEnumerable<ConflictCheckItem> All(HardwareSummary hw)
        {
            var list = new List<ConflictCheckItem>();
            Try(list, () => WindowsSupport());
            Try(list, () => GpuDriverVersion(hw));
            Try(list, () => DumpCapture());
            Try(list, () => LiveKernelReports());
            try { list.AddRange(RiskyDrivers.WarningItems()); } // drivers only the downloaded data knows (read-only)
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }

        private static void Try(List<ConflictCheckItem> list, Func<ConflictCheckItem> make)
        {
            try { var c = make(); if (c != null) list.Add(c); }
            catch (Exception ex) { Log.Warn(ex); }
        }

        // ==================== Windows release & support ====================
        public static ConflictCheckItem WindowsSupport()
        {
            WindowsRelease w = WindowsRelease.Current;
            DateTime? end = w.EndOfServicing;
            DateTime today = DateTime.Today;
            bool expired = end.HasValue && today > end.Value;
            bool soon = end.HasValue && !expired && (end.Value - today).TotalDays <= 90;

            string explanation;
            if (expired)
                explanation = string.Format(Localizer.S("detection_checks.this_release_has_not_received_updates"), WindowsRelease.Date(end.Value));
            else if (soon)
                explanation = string.Format(Localizer.S("detection_checks.this_release_stops_getting_updates_after"), WindowsRelease.Date(end.Value));
            else
                explanation = Localizer.S("detection_checks.supported_release_microsoft_fixes_includ");

            return new ConflictCheckItem
            {
                Tag = "WINDOWS",
                Title = Localizer.S("detection_checks.windows_version_and_support"),
                StatusText = w.Label + (expired ? Localizer.S("detection_checks.out_of_support") : ""),
                Explanation = explanation,
                Level = expired ? HealthLevel.Warning : (soon ? HealthLevel.Info : HealthLevel.Good),
                QuickActionLabel = Localizer.S("detection_checks.windows_update"),
                QuickActionCommand = "ms-settings:windowsupdate"
            };
        }

        // ==================== Graphics driver version ====================
        /// <summary>"32.0.15.9559" → "595.59" (the last five digits of the Windows version are NVIDIA's).</summary>
        public static string NvidiaVersion(string wmi)
        {
            if (string.IsNullOrEmpty(wmi)) return null;
            string[] p = wmi.Split('.');
            if (p.Length < 4) return null;
            string tail = p[2] + p[3].PadLeft(4, '0');
            if (tail.Length < 5) return null;
            tail = tail.Substring(tail.Length - 5);
            return tail.Substring(0, 3) + "." + tail.Substring(3);
        }

        private static double Ver(string v)
        {
            double d;
            return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : 0;
        }

        /// <summary>
        /// NVIDIA releases with a documented problem, and what to do instead (data/detection.json, nvidia section:
        /// exact problem releases first, then minimum releases per GPU family).
        /// </summary>
        private static string NvidiaProblem(string version, string gpuName)
        {
            DetectionData data = DetectionData.Current;
            DetectionData.NvidiaProblem bad = data.NvidiaProblems.FirstOrDefault(p => p.Version == version);
            if (bad != null) return Localizer.S(bad.ReasonKey);

            double v = Ver(version);
            string gpu = NoSpaces(gpuName);
            foreach (DetectionData.NvidiaMinimum m in data.NvidiaMinimums)
            {
                bool family = gpu.Length > 0 && m.GpuNames.Any(n => gpu.IndexOf(NoSpaces(n), StringComparison.OrdinalIgnoreCase) >= 0);
                if (family && v > 0 && v < Ver(m.MinVersion)) return Localizer.S(m.ReasonKey);
            }
            return null;
        }

        /// <summary>"RTX 5080" and "RTX5080" compare equal.</summary>
        private static string NoSpaces(string s) { return new string((s ?? "").Where(c => !char.IsWhiteSpace(c)).ToArray()); }

        public static ConflictCheckItem GpuDriverVersion(HardwareSummary hw)
        {
            if (hw == null || string.IsNullOrEmpty(hw.GpuDriver)) return null;
            if (!hw.HasNvidiaGpu)
            {
                return new ConflictCheckItem
                {
                    Tag = Localizer.S("detection_checks.graphics_driver"),
                    Title = Localizer.S("detection_checks.graphics_driver_version"),
                    StatusText = hw.GpuName + " · " + hw.GpuDriver,
                    Explanation = Localizer.S("detection_checks.no_list_of_problem_releases_is"),
                    Level = HealthLevel.Info
                };
            }

            string nv = NvidiaVersion(hw.GpuDriver);
            string problem = nv != null ? NvidiaProblem(nv, hw.GpuName) : null;
            return new ConflictCheckItem
            {
                Tag = Localizer.S("detection_checks.graphics_driver"),
                Title = Localizer.S("detection_checks.graphics_driver_version"),
                StatusText = "NVIDIA " + (nv ?? "?") + "  (" + hw.GpuDriver + ")",
                Explanation = problem ?? Localizer.S("detection_checks.this_release_is_not_one_of"),
                Level = problem != null ? HealthLevel.Warning : HealthLevel.Good,
                GuideTitle = problem != null ? Localizer.S("detection_checks.switching_drivers_cleanly") : null,
                GuideSteps = problem != null ? Localizer.S("detection_checks.1_download_the_advised_release_from") : null,
                QuickActionLabel = problem != null ? Localizer.S("detection_checks.nvidia_drivers") : null,
                QuickActionCommand = problem != null ? "https://www.nvidia.com/drivers/" : null
            };
        }

        // ==================== Crash dump capture ====================
        private const string CrashControl = @"SYSTEM\CurrentControlSet\Control\CrashControl";

        private static int? ReadDword(string sub, string name)
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(sub))
                {
                    object v = k != null ? k.GetValue(name) : null;
                    return v is int ? (int?)(int)v : null;
                }
            }
            catch { return null; }
        }

        private static bool HasPagefile()
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management"))
                {
                    var v = k != null ? k.GetValue("PagingFiles") as string[] : null;
                    return v != null && v.Any(s => !string.IsNullOrWhiteSpace(s));
                }
            }
            catch { return true; }
        }

        private static int CountEvents(string provider, int id, int days)
        {
            int n = 0;
            try
            {
                string utc = DateTime.Now.AddDays(-days).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.000Z", CultureInfo.InvariantCulture);
                string xpath = "*[System[Provider[@Name='" + provider + "'] and (EventID=" + id + ") and TimeCreated[@SystemTime >= '" + utc + "']]]";
                using (var r = new EventLogReader(new EventLogQuery("System", PathType.LogName, xpath)))
                {
                    EventRecord rec;
                    while ((rec = r.ReadEvent()) != null) { rec.Dispose(); n++; }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return n;
        }

        public static ConflictCheckItem DumpCapture()
        {
            int? mode = ReadDword(CrashControl, "CrashDumpEnabled");
            bool off = mode.HasValue && mode.Value == 0;
            bool pagefile = HasPagefile();
            int failed = CountEvents("volmgr", 161, 60);
            int written = CountEvents("volmgr", 162, 60);
            bool bad = off || !pagefile || failed > 0;

            string modeName;
            switch (mode ?? 7)
            {
                case 0: modeName = Localizer.S("detection_checks.disabled"); break;
                case 1: modeName = Localizer.S("detection_checks.complete"); break;
                case 2: modeName = Localizer.S("detection_checks.kernel"); break;
                case 3: modeName = Localizer.S("detection_checks.small_minidump"); break;
                default: modeName = Localizer.S("detection_checks.automatic"); break;
            }

            var reasons = new List<string>();
            if (off) reasons.Add(Localizer.S("detection_checks.dump_writing_is_turned_off"));
            if (!pagefile) reasons.Add(Localizer.S("detection_checks.there_is_no_page_file_windows"));
            if (failed > 0) reasons.Add(string.Format(Localizer.S("detection_checks.dump_write_s_failed_in_60"), failed));

            return new ConflictCheckItem
            {
                Tag = Localizer.S("detection_checks.blue_screens"),
                Title = Localizer.S("detection_checks.blue_screen_recording_dumps"),
                StatusText = string.Format(Localizer.S("detection_checks.mode_dump_s_written_failure_s"), modeName, written, failed),
                Explanation = bad
                    ? Localizer.S("detection_checks.without_a_dump_a_blue_screen") + string.Join(" ; ", reasons) + "."
                    : Localizer.S("detection_checks.windows_is_set_up_to_save"),
                Level = bad ? HealthLevel.Warning : HealthLevel.Good,
                InAppFixOp = off ? "crashdump_apply" : (!pagefile || failed > 0 ? "pagefile_auto_apply" : null),
                InAppFixLabel = off
                    ? Localizer.S("detection_checks.turn_dumps_back_on")
                    : (!pagefile || failed > 0 ? Localizer.S("detection_checks.automatic_page_file") : null)
            };
        }

        /// <summary>CrashDumpEnabled = 7 (automatic memory dump, the Windows default). Recorded for undo.</summary>
        public static bool EnableCrashDumps(out string messageFr, out string messageEn)
        {
            ChangeRecorder.Registry(RegistryHive.LocalMachine, CrashControl, "CrashDumpEnabled");
            using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            using (var k = bk.CreateSubKey(CrashControl))
                k.SetValue("CrashDumpEnabled", 7, RegistryValueKind.DWord);
            messageFr = "✓ Enregistrement des écrans bleus réactivé (vidage mémoire automatique).";
            messageEn = "✓ Blue screen recording turned back on (automatic memory dump).";
            return true;
        }

        // ==================== Live kernel reports ====================
        public static ConflictCheckItem LiveKernelReports()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "LiveKernelReports");
            if (!Directory.Exists(dir)) return null;

            var title = Localizer.S("detection_checks.hangs_windows_recovered_from_livekernelr");
            var tag = Localizer.S("detection_checks.live_dumps");
            List<FileInfo> files;
            try
            {
                files = new DirectoryInfo(dir).EnumerateFiles("*.dmp", SearchOption.AllDirectories)
                                             .Where(f => (DateTime.Now - f.LastWriteTime).TotalDays <= 30).ToList();
            }
            catch (UnauthorizedAccessException)
            {
                return new ConflictCheckItem
                {
                    Tag = tag,
                    Title = title,
                    StatusText = Localizer.S("detection_checks.readable_in_administrator_mode_only"),
                    Explanation = Localizer.S("detection_checks.when_the_graphics_or_usb_network"),
                    Level = HealthLevel.Info
                };
            }

            if (files.Count == 0)
                return new ConflictCheckItem
                {
                    Tag = tag,
                    Title = title,
                    StatusText = Localizer.S("detection_checks.none_in_30_days"),
                    Explanation = Localizer.S("detection_checks.windows_did_not_have_to_restart"),
                    Level = HealthLevel.Good
                };

            // Header (and, for triage-style live dumps, the culprit driver) of each file.
            var codes = new Dictionary<string, int>();
            var culprits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            bool gpu = false;
            foreach (FileInfo f in files.OrderByDescending(x => x.LastWriteTime).Take(40))
            {
                KernelDumpInfo info = null;
                try
                {
                    int len = (int)Math.Min(f.Length, 16L << 20);
                    var buf = new byte[len];
                    using (var fs = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        int read = 0;
                        while (read < len) { int n = fs.Read(buf, read, len - read); if (n <= 0) break; read += n; }
                    }
                    info = KernelDumpParser.Parse(buf);
                }
                catch (Exception ex) { Log.Warn(ex, f.FullName); }

                string key = f.Directory.Name + (info != null ? " 0x" + info.BugCheckCode.ToString("X") : "");
                codes[key] = codes.ContainsKey(key) ? codes[key] + 1 : 1;
                if (info != null && (info.BugCheckCode == 0x141 || info.BugCheckCode == 0x117 || info.BugCheckCode == 0x1A8 || info.BugCheckCode == 0x193)) gpu = true;
                if (f.Directory.Name.StartsWith("WATCHDOG", StringComparison.OrdinalIgnoreCase)) gpu = true;
                if (info != null && !string.IsNullOrEmpty(info.Culprit))
                    culprits[info.Culprit] = culprits.ContainsKey(info.Culprit) ? culprits[info.Culprit] + 1 : 1;
            }

            string summary = string.Join(", ", codes.OrderByDescending(kv => kv.Value).Select(kv => kv.Key + " ×" + kv.Value));
            string culprit = culprits.Count > 0 ? culprits.OrderByDescending(kv => kv.Value).First().Key : null;
            return new ConflictCheckItem
            {
                Tag = tag,
                Title = title,
                StatusText = string.Format(Localizer.S("detection_checks.in_30_days"), files.Count, summary),
                Explanation = (gpu
                    ? Localizer.S("detection_checks.the_graphics_driver_hung_and_windows")
                    : Localizer.S("detection_checks.a_driver_hung_and_windows_restarted"))
                    + (culprit != null ? " " + Localizer.S("detection_checks.driver_most_often_involved") + culprit + "." : ""),
                Level = HealthLevel.Warning
            };
        }
    }
}
