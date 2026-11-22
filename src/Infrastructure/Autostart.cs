using System;
using System.IO;
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
            get { return BuildCommand(Elevation.ExePath); }
        }

        internal static string BuildCommand(string exePath)
        {
            return "\"" + exePath + "\" " + TrayArg;
        }

        /// <summary>Raw command currently stored in the Run key, or null.</summary>
        private static string RegisteredCommand
        {
            get
            {
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                        return k != null ? k.GetValue(ValueName) as string : null;
                }
                catch (Exception ex)
                {
                    Log.Warn(ex);
                    return null;
                }
            }
        }

        public static bool IsEnabled
        {
            get { return IsOurCommand(RegisteredCommand); }
        }

        private static bool IsOurCommand(string command)
        {
            return command != null && command.IndexOf(TrayArg, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Registers (or removes) the entry for the exe currently running. Used by the explicit toggle.</summary>
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

        /// <summary>
        /// Called on every normal launch. Repoints the entry at the current exe only when the registered exe is
        /// gone (the user moved or deleted it). A second copy (another download, a dev build, a temp copy) must
        /// never take over an entry that still points at a valid exe.
        /// </summary>
        public static void RefreshPathIfEnabled()
        {
            string registered = RegisteredCommand;
            if (ShouldRepoint(registered, Elevation.ExePath, File.Exists)) Set(true);
        }

        /// <summary>
        /// Pure decision behind <see cref="RefreshPathIfEnabled"/>. True only when the option is on (our entry
        /// with --tray), it does not already point at <paramref name="currentExe"/>, and the exe it names cannot
        /// be found: either the file is missing, or the command cannot be parsed into a path at all.
        /// </summary>
        internal static bool ShouldRepoint(string registeredCommand, string currentExe, Func<string, bool> fileExists)
        {
            if (!IsOurCommand(registeredCommand) || string.IsNullOrEmpty(currentExe)) return false;
            string registeredExe = ParseExePath(registeredCommand);
            if (registeredExe == null) return true; // unusable entry: Windows cannot start anything from it
            if (SamePath(registeredExe, currentExe)) return false;
            bool exists;
            try { exists = fileExists(registeredExe); }
            catch { exists = true; } // when in doubt, leave the user's entry alone
            return !exists;
        }

        /// <summary>
        /// Extracts the exe path from a Run command: <c>"C:\path\DumpFinder.exe" --tray</c>. An unquoted path is
        /// accepted when it is followed by the tray argument. Returns null for a malformed command.
        /// </summary>
        internal static string ParseExePath(string command)
        {
            if (command == null) return null;
            string s = Environment.ExpandEnvironmentVariables(command).Trim();
            string path;
            if (s.StartsWith("\""))
            {
                int end = s.IndexOf('"', 1);
                if (end < 0) return null;
                path = s.Substring(1, end - 1);
            }
            else
            {
                int arg = s.IndexOf(" " + TrayArg, StringComparison.OrdinalIgnoreCase);
                path = arg >= 0 ? s.Substring(0, arg) : s;
            }
            path = path.Trim();
            if (path.Length == 0 || path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
            if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            try
            {
                if (!Path.IsPathRooted(path)) return null;
                return Path.GetFullPath(path);
            }
            catch
            {
                return null;
            }
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch { return string.Equals(a, b, StringComparison.OrdinalIgnoreCase); }
        }
    }
}
