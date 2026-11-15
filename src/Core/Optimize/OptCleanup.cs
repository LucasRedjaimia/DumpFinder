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

        private static string WinDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Windows); } }

        private static string PathOf(string id)
        {
            switch (id)
            {
                case "usertemp": return SafeUserTemp();
                case "wintemp": return Path.Combine(WinDir, "Temp");
                case "wucache": return Path.Combine(WinDir, @"SoftwareDistribution\Download");
                case "docache": return Path.Combine(WinDir, @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache");
                default: return null;
            }
        }

        /// <summary>
        /// %TEMP%, only when it is the usual per-user temp folder (%LOCALAPPDATA%\Temp or one of its subfolders,
        /// e.g. Temp\2 in remote sessions). A TEMP variable pointing at C:\, the profile or Documents returns null:
        /// cleaning "every file older than 24 h" there would delete personal files.
        /// </summary>
        private static string SafeUserTemp()
        {
            try
            {
                string temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\');
                string expected = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp")).TrimEnd('\\');
                if (temp.Equals(expected, StringComparison.OrdinalIgnoreCase) || temp.StartsWith(expected + "\\", StringComparison.OrdinalIgnoreCase))
                    return temp;
                Log.Warn("Temp cleanup skipped: %TEMP% is not the usual user temp folder (" + temp + ")");
            }
            catch (Exception ex) { Log.Warn(ex); }
            return null;
        }

        /// <summary>Temp folders keep anything touched in the last 24 h (files an app may still be using).</summary>
        private static bool OnlyOld(string id) { return id == "usertemp" || id == "wintemp"; }

        public static List<CleanItem> Scan()
        {
            var items = new List<CleanItem>
            {
                new CleanItem { Id = "usertemp", Admin = false,
                    Title = Localizer.S("opt_cleanup.your_account_s_temporary_files"),
                    Explanation = Localizer.S("opt_cleanup.temp_folder_files_changed_in_the") },
                new CleanItem { Id = "wintemp", Admin = true,
                    Title = Localizer.S("opt_cleanup.windows_temporary_files"),
                    Explanation = Localizer.S("opt_cleanup.c_windows_temp_folder_same_precaution") },
                new CleanItem { Id = "wucache", Admin = true,
                    Title = Localizer.S("opt_cleanup.already_installed_windows_updates_downlo"),
                    Explanation = Localizer.S("opt_cleanup.softwaredistribution_download_folder_win") },
                new CleanItem { Id = "docache", Admin = true,
                    Title = Localizer.S("opt_cleanup.delivery_optimization_cache"),
                    Explanation = Localizer.S("opt_cleanup.update_pieces_kept_for_sharing_emptied") }
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
            return it != null ? Localizer.S("opt_cleanup.cleanup") + it.Title : null;
        }

        public static bool Execute(string op, out string messageFr, out string messageEn)
        {
            string id = IdOf(op);
            string dir = PathOf(id);
            if (dir == null && id == "usertemp")
            {
                messageFr = Localizer.Ref("engine.opt_cleanup.cleanup_skipped_the_temp_variable_does");
                messageEn = messageFr;
                return false;
            }
            if (dir == null) { messageFr = Localizer.Ref("engine.opt_cleanup.unknown_item"); messageEn = messageFr; return false; }

            long freed = 0; int deleted = 0, skipped = 0;
            if (id == "docache")
            {
                long before = DirSize(dir);
                RunHidden("powershell.exe", "-NoProfile -NonInteractive -Command \"Delete-DeliveryOptimizationCache -Force\"", 120000);
                freed = Math.Max(0, before - DirSize(dir));
            }
            else if (Directory.Exists(dir))
            {
                // Where the folder really is on disk (C:\Windows\Temp may itself be redirected by Windows).
                string rootFinal = SafeDelete.FinalPath(dir);
                if (rootFinal == null)
                {
                    messageFr = Localizer.Ref("engine.opt_cleanup.folder_not_readable_nothing_was_deleted");
                    messageEn = messageFr;
                    return false;
                }
                DateTime cutoff = DateTime.Now.AddHours(-24);
                foreach (string f in SafeFiles(dir))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (OnlyOld(id) && fi.LastWriteTime > cutoff) continue;
                        long len = fi.Length;
                        // Deleted through a handle whose real location is checked first: a folder swapped for a
                        // junction after the scan cannot redirect the (possibly elevated) delete into System32.
                        if (SafeDelete.Delete(f, rootFinal)) { freed += len; deleted++; }
                        else skipped++;
                    }
                    catch { skipped++; } // in use: left alone
                }
                foreach (string d in SafeDirs(dir).OrderByDescending(x => x.Length))
                {
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) SafeDelete.Delete(d, rootFinal); } catch { }
                }
            }

            double gb = freed / (1024.0 * 1024 * 1024);
            messageFr = Localizer.Ref("engine.opt_cleanup.freed_gb", gb)
                        + (skipped > 0 ? Localizer.Ref("engine.opt_cleanup.files_in_use_left", skipped) : "")
                        + Localizer.Ref("common.sentence_end");
            messageEn = messageFr;
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
            SysTools.Run(exe, args, timeout);
        }
    }
}
