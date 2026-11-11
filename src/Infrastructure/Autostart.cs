using System;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    /// <summary>Per-user "start with Windows" entry (HKCU Run key, no admin needed). Starts silently in the tray.</summary>
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "DumpFinder";
        public const string TrayArg = "--tray";

        private static string Command
        {
            get { return "\"" + Elevation.ExePath + "\" " + TrayArg; }
        }

        public static bool IsEnabled
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                    {
                        var v = k != null ? k.GetValue(ValueName) as string : null;
                        return v != null && v.IndexOf(TrayArg, StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
                catch (Exception ex)
                {
                    Log.Warn(ex);
                    return false;
                }
            }
        }

        public static bool Set(bool enabled)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return false;
                    if (enabled) k.SetValue(ValueName, Command, RegistryValueKind.String);
                    else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
                }
                return true;
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Autostart");
                return false;
            }
        }

        /// <summary>Keeps the Run entry pointing at the current exe if the user moved it.</summary>
        public static void RefreshPathIfEnabled()
        {
            if (IsEnabled) Set(true);
        }
    }
}
