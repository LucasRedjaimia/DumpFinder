using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace WinCrashDoctor
{
    /// <summary>Another Dump Finder process running on this session: its exe and version.</summary>
    public sealed class RunningCopy
    {
        public int Pid;
        public string Path;
        public Version Version;
    }

    /// <summary>
    /// Other Dump Finder processes, so a new version does not leave the old one running: after a manual update
    /// the old exe often keeps living in the tray (Sentinel), and an in-app update must stop every process of the
    /// exe it replaced. Only processes whose exe is Dump Finder (product name) are considered.
    /// </summary>
    public static class RunningCopies
    {
        /// <summary>Dump Finder processes other than this one (exe names "DumpFinder…" or the legacy "WinCrashDoctor").</summary>
        public static List<RunningCopy> Find()
        {
            var list = new List<RunningCopy>();
            int self = Process.GetCurrentProcess().Id;
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == self) continue;
                    string name = p.ProcessName;
                    if (!name.StartsWith("DumpFinder", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("WinCrashDoctor", StringComparison.OrdinalIgnoreCase)) continue;
                    string path = p.MainModule.FileName;              // throws for processes of another user or elevated ones
                    FileVersionInfo v = p.MainModule.FileVersionInfo;
                    if (v.ProductName != "Dump Finder") continue;
                    list.Add(new RunningCopy { Pid = p.Id, Path = path, Version = UpdateChecker.ParseVersion(v.FileVersion) });
                }
                catch (Exception) { }
                finally { p.Dispose(); }
            }
            return list;
        }

        /// <summary>The copies older than <paramref name="current"/> (the leftovers of a manual update).</summary>
        public static List<RunningCopy> OlderThan(IEnumerable<RunningCopy> copies, Version current)
        {
            return copies.Where(c => c.Version != null && current != null && c.Version < current).ToList();
        }

        /// <summary>The copies started from one of <paramref name="paths"/> (an in-app update replaces that exe).</summary>
        public static List<RunningCopy> From(IEnumerable<RunningCopy> copies, params string[] paths)
        {
            return copies.Where(c => paths.Any(p => !string.IsNullOrEmpty(p) && SamePath(c.Path, p))).ToList();
        }

        /// <summary>Ends these processes (they are Dump Finder, its data files are written atomically). Returns how many ended.</summary>
        public static int Close(IEnumerable<RunningCopy> copies)
        {
            int closed = 0;
            foreach (RunningCopy c in copies)
            {
                try
                {
                    using (Process p = Process.GetProcessById(c.Pid))
                    {
                        if (!SamePath(p.MainModule.FileName, c.Path)) continue;   // the pid was reused meanwhile
                        p.Kill();
                        if (p.WaitForExit(5000)) closed++;
                    }
                }
                catch (ArgumentException) { closed++; }                        // already gone
                catch (Exception ex) { Log.Warn(ex, "close Dump Finder " + c.Pid); }
            }
            return closed;
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }
    }
}
