using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace WinCrashDoctor
{
    /// <summary>Result of a console tool run by <see cref="SysTools.Run"/>.</summary>
    public sealed class ToolResult
    {
        public bool Started;
        public bool TimedOut;
        public int ExitCode = -1;
        public string Output = "";
        public string Error = "";

        public bool Ok { get { return Started && !TimedOut && ExitCode == 0; } }
    }

    /// <summary>
    /// One way to run Windows console tools:
    /// - always by full path (System32), never through the search order, which starts with the folder of
    ///   DumpFinder.exe (often Downloads, where a planted "powercfg.exe" would otherwise run, possibly as admin);
    /// - stdout and stderr read asynchronously (a full pipe can no longer deadlock the child);
    /// - a hard timeout that kills the process tree instead of waiting forever on a hung tool.
    /// </summary>
    public static class SysTools
    {
        /// <summary>Full path of a tool in System32 (e.g. "powercfg.exe"). Names that already hold a folder are returned as is.</summary>
        public static string Path(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return exeName;
            if (exeName.IndexOf('\\') >= 0 || exeName.IndexOf('/') >= 0) return exeName;
            if (!exeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exeName += ".exe";
            if (exeName.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
                return System.IO.Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            return System.IO.Path.Combine(Environment.SystemDirectory, exeName);
        }

        /// <summary>
        /// nvidia-smi from the NVIDIA driver (System32 on DCH drivers, NVSMI on older ones), or null when absent.
        /// Never resolved through PATH or the current folder.
        /// </summary>
        public static string NvidiaSmiPath
        {
            get
            {
                string sys = System.IO.Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe");
                if (File.Exists(sys)) return sys;
                string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                string legacy = System.IO.Path.Combine(pf, @"NVIDIA Corporation\NVSMI\nvidia-smi.exe");
                return File.Exists(legacy) ? legacy : null;
            }
        }

        /// <summary>Runs a console tool hidden and waits at most <paramref name="timeoutMs"/>. Never throws.</summary>
        public static ToolResult Run(string exe, string arguments, int timeoutMs, Encoding outputEncoding = null)
        {
            var r = new ToolResult();
            if (string.IsNullOrEmpty(exe)) return r;
            var psi = new ProcessStartInfo(Path(exe), arguments ?? "")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Environment.SystemDirectory
            };
            if (outputEncoding != null)
            {
                psi.StandardOutputEncoding = outputEncoding;
                psi.StandardErrorEncoding = outputEncoding;
            }

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();
            try
            {
                using (var p = new Process { StartInfo = psi })
                using (var outDone = new System.Threading.ManualResetEvent(false))
                using (var errDone = new System.Threading.ManualResetEvent(false))
                {
                    p.OutputDataReceived += (s, e) => { if (e.Data == null) SafeSet(outDone); else lock (stdout) stdout.AppendLine(e.Data); };
                    p.ErrorDataReceived += (s, e) => { if (e.Data == null) SafeSet(errDone); else lock (stderr) stderr.AppendLine(e.Data); };
                    p.Start();
                    r.Started = true;
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(Math.Max(1, timeoutMs)))
                    {
                        r.TimedOut = true;
                        Kill(p);
                        Log.Warn("Tool timed out after " + timeoutMs + " ms: " + psi.FileName + " " + arguments);
                    }
                    else
                    {
                        // Drain the asynchronous readers (bounded: a grandchild could keep the pipe open).
                        outDone.WaitOne(2000);
                        errDone.WaitOne(2000);
                        r.ExitCode = p.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, psi.FileName + " " + arguments);
            }
            lock (stdout) r.Output = stdout.ToString();
            lock (stderr) r.Error = stderr.ToString();
            return r;
        }

        private static void SafeSet(System.Threading.ManualResetEvent ev)
        {
            try { ev.Set(); } catch (ObjectDisposedException) { }
        }

        private static void Kill(Process p)
        {
            try
            {
                // taskkill /T also ends the tool's own children (e.g. a console spawned by a script).
                using (var k = Process.Start(new ProcessStartInfo(Path("taskkill.exe"), "/PID " + p.Id + " /T /F")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                }))
                {
                    if (k != null) k.WaitForExit(3000);
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            try { if (!p.HasExited) p.Kill(); } catch (Exception) { }
        }
    }
}
