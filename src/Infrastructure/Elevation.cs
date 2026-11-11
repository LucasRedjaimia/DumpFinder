using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;

namespace WinCrashDoctor
{
    public enum ElevationOutcome
    {
        Success,
        Failed,
        Cancelled,
        TimedOut
    }

    /// <summary>
    /// Single place for every UAC round-trip (previously copy-pasted in 5 files).
    /// The manifest stays asInvoker; privileged work runs in a short-lived elevated
    /// copy of this exe (--elevated-op / --scan-admin-json).
    /// </summary>
    public static class Elevation
    {
        private const int ErrorCancelled = 1223; // ERROR_CANCELLED: user refused the UAC prompt

        public static string ExePath
        {
            get { return Assembly.GetExecutingAssembly().Location; }
        }

        public static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex);
                return false;
            }
        }

        /// <summary>
        /// Runs this exe elevated with <paramref name="arguments"/> and waits for it.
        /// Blocking: call from a background thread.
        /// </summary>
        public static ElevationOutcome RunSelf(string arguments, int timeoutMs)
        {
            return Run(ExePath, arguments, timeoutMs);
        }

        /// <summary>Runs any executable elevated and waits for it. Blocking.</summary>
        public static ElevationOutcome Run(string fileName, string arguments, int timeoutMs)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    Verb = "runas",
                    UseShellExecute = true
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return ElevationOutcome.Failed;
                    if (!p.WaitForExit(timeoutMs))
                    {
                        Log.Warn("Elevated process timed out after " + timeoutMs + " ms: " + arguments);
                        return ElevationOutcome.TimedOut;
                    }
                    if (p.ExitCode != 0) Log.Warn("Elevated process exit code " + p.ExitCode + ": " + arguments);
                    return p.ExitCode == 0 ? ElevationOutcome.Success : ElevationOutcome.Failed;
                }
            }
            catch (Exception ex)
            {
                if (IsUacRefusal(ex)) return ElevationOutcome.Cancelled;
                Log.Error(ex, arguments);
                return ElevationOutcome.Failed;
            }
        }

        /// <summary>Starts an elevated process without waiting (e.g. an admin console). Returns false if refused.</summary>
        public static bool Launch(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments ?? "",
                    Verb = "runas",
                    UseShellExecute = true
                };
                using (Process.Start(psi)) { }
                return true;
            }
            catch (Exception ex)
            {
                if (!IsUacRefusal(ex)) Log.Error(ex, fileName + " " + arguments);
                return false;
            }
        }

        private static bool IsUacRefusal(Exception ex)
        {
            var w32 = ex as Win32Exception;
            return w32 != null && w32.NativeErrorCode == ErrorCancelled;
        }

        /// <summary>Relaunches the whole app as administrator. Returns false if the user refused.</summary>
        public static bool RestartAppElevated()
        {
            return Launch(ExePath, "");
        }
    }
}
