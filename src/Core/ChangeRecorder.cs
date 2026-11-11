using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    /// <summary>One state captured just before Dump Finder changed it.</summary>
    public class ChangeRecord
    {
        /// <summary>"reg", "file" or "power".</summary>
        public string Kind { get; set; }

        // Registry
        public string KeyPath { get; set; }      // e.g. HKEY_LOCAL_MACHINE\SYSTEM\...
        public string ValueName { get; set; }
        public bool Existed { get; set; }
        public string ValueKind { get; set; }    // RegistryValueKind name
        public string Data { get; set; }         // serialized previous data

        // File
        public string FilePath { get; set; }
        public string BackupPath { get; set; }

        // powercfg (AC value of the current scheme)
        public string PowerSub { get; set; }
        public string PowerSetting { get; set; }
        public int PowerOldAc { get; set; }

        public string Describe()
        {
            switch (Kind)
            {
                case "reg": return KeyPath + " \\ " + ValueName + " = " + (Existed ? Data : "(absent)");
                case "file": return FilePath;
                case "power": return "powercfg " + PowerSub + " " + PowerSetting + " = " + PowerOldAc;
                default: return Kind;
            }
        }
    }

    /// <summary>
    /// Captures the exact previous state of everything a fix touches (registry values, settings files,
    /// power plan values) so each change can be undone precisely, not just reset to a default.
    /// Usage: Begin() → run the fix (helpers call Registry/File/Power before writing) → End().
    /// </summary>
    public static class ChangeRecorder
    {
        [ThreadStatic] private static List<ChangeRecord> _scope;
        [ThreadStatic] private static HashSet<string> _seen;

        /// <summary>Registry roots a revert may write to (anything else is refused, even when elevated).</summary>
        private static readonly string[] AllowedRegistryPrefixes =
        {
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\Dwm",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate",
            @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\stornvme\Parameters",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\amdwddmg",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\amdkmdag",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\AMD External Events Utility",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\GigabyteUpdateService",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\cfosspeed",
            @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\CrashControl",
            @"HKEY_CURRENT_USER\System\GameConfigStore",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\GameDVR",
            @"HKEY_CURRENT_USER\Software\Microsoft\DirectX\UserGpuPreferences"
        };

        /// <summary>Power settings a revert may touch (PCIe ASPM, USB selective suspend).</summary>
        private static readonly string[] AllowedPowerSettings =
        {
            "ee12f906-d277-404b-b6da-e5fa1a576df5",
            "48e6b7a6-50f5-4782-a5d4-53bb8f07e226"
        };

        public static string BackupDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder", "backups"); }
        }

        public static void Begin()
        {
            _scope = new List<ChangeRecord>();
            _seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        public static List<ChangeRecord> End()
        {
            var r = _scope ?? new List<ChangeRecord>();
            _scope = null;
            _seen = null;
            return r;
        }

        private static bool Active { get { return _scope != null; } }

        /// <summary>Records a registry value through an already-open key (call before SetValue/DeleteValue).</summary>
        public static void Registry(RegistryKey key, string valueName)
        {
            if (!Active || key == null) return;
            if (!_seen.Add("reg|" + key.Name + "|" + valueName)) return;
            try
            {
                var rec = new ChangeRecord { Kind = "reg", KeyPath = key.Name, ValueName = valueName };
                object v = key.GetValue(valueName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (v != null)
                {
                    RegistryValueKind kind = key.GetValueKind(valueName);
                    rec.Existed = true;
                    rec.ValueKind = kind.ToString();
                    rec.Data = Serialize(v, kind);
                }
                _scope.Add(rec);
            }
            catch (Exception ex) { Log.Warn(ex, key.Name + "\\" + valueName); }
        }

        /// <summary>Records a registry value by path (the key may not exist yet).</summary>
        public static void Registry(RegistryHive hive, string subKey, string valueName)
        {
            if (!Active) return;
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var k = bk.OpenSubKey(subKey, false))
                {
                    if (k != null) { Registry(k, valueName); return; }
                    string full = bk.Name + "\\" + subKey;
                    if (_seen.Add("reg|" + full + "|" + valueName))
                        _scope.Add(new ChangeRecord { Kind = "reg", KeyPath = full, ValueName = valueName, Existed = false });
                }
            }
            catch (Exception ex) { Log.Warn(ex, subKey + "\\" + valueName); }
        }

        /// <summary>Backs up a settings file before it is rewritten.</summary>
        public static void File(string path)
        {
            if (!Active || string.IsNullOrEmpty(path) || !_seen.Add("file|" + path)) return;
            try
            {
                if (!System.IO.File.Exists(path)) return;
                Directory.CreateDirectory(BackupDir);
                string backup = Path.Combine(BackupDir, DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + "_" + Path.GetFileName(path) + ".bak");
                System.IO.File.Copy(path, backup, true);
                _scope.Add(new ChangeRecord { Kind = "file", FilePath = path, BackupPath = backup });
            }
            catch (Exception ex) { Log.Warn(ex, path); }
        }

        public static void Power(string sub, string setting, int oldAc)
        {
            if (!Active || !_seen.Add("power|" + setting)) return;
            _scope.Add(new ChangeRecord { Kind = "power", PowerSub = sub, PowerSetting = setting, PowerOldAc = oldAc });
        }

        /// <summary>True when undoing requires administrator rights (HKLM or power plan).</summary>
        public static bool RequiresAdmin(IEnumerable<ChangeRecord> changes)
        {
            return changes != null && changes.Any(c =>
                c.Kind == "power" ||
                (c.Kind == "reg" && c.KeyPath != null && c.KeyPath.StartsWith("HKEY_LOCAL_MACHINE", StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>Puts every recorded state back, newest first. Returns the number of items restored.</summary>
        public static int Revert(List<ChangeRecord> changes, out string error)
        {
            error = null;
            int done = 0;
            if (changes == null) return 0;
            for (int i = changes.Count - 1; i >= 0; i--)
            {
                var c = changes[i];
                try
                {
                    switch (c.Kind)
                    {
                        case "reg":
                            if (!IsAllowedKey(c.KeyPath)) { error = "refused: " + c.KeyPath; continue; }
                            RevertRegistry(c);
                            done++;
                            break;
                        case "file":
                            if (string.IsNullOrEmpty(c.BackupPath) || !System.IO.File.Exists(c.BackupPath)) { error = "backup missing: " + c.FilePath; continue; }
                            if (!c.BackupPath.StartsWith(BackupDir, StringComparison.OrdinalIgnoreCase)) { error = "refused: " + c.BackupPath; continue; }
                            System.IO.File.Copy(c.BackupPath, c.FilePath, true);
                            done++;
                            break;
                        case "power":
                            if (!AllowedPowerSettings.Contains(c.PowerSetting, StringComparer.OrdinalIgnoreCase)) { error = "refused: " + c.PowerSetting; continue; }
                            RunHidden("powercfg", "/setacvalueindex SCHEME_CURRENT " + c.PowerSub + " " + c.PowerSetting + " " + c.PowerOldAc);
                            RunHidden("powercfg", "/setactive SCHEME_CURRENT");
                            done++;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, c.Describe());
                    error = ex.Message;
                }
            }
            return done;
        }

        private static bool IsAllowedKey(string keyPath)
        {
            if (keyPath == null) return false;
            return AllowedRegistryPrefixes.Any(p => keyPath.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                || Detection.RiskyDrivers.IsRevertableKey(keyPath) // only services that load a catalog driver
                || Optimize.OptOps.IsAllowedKey(keyPath);                // Optimiser settings and startup approvals
        }

        private static void RevertRegistry(ChangeRecord c)
        {
            int slash = c.KeyPath.IndexOf('\\');
            string root = slash > 0 ? c.KeyPath.Substring(0, slash) : c.KeyPath;
            string sub = slash > 0 ? c.KeyPath.Substring(slash + 1) : "";
            RegistryHive hive = root.Equals("HKEY_CURRENT_USER", StringComparison.OrdinalIgnoreCase) ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
            using (var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
            {
                if (!c.Existed)
                {
                    using (var k = bk.OpenSubKey(sub, true))
                    {
                        if (k != null && k.GetValue(c.ValueName) != null) k.DeleteValue(c.ValueName, false);
                    }
                    return;
                }
                using (var k = bk.CreateSubKey(sub, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    RegistryValueKind kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), c.ValueKind);
                    k.SetValue(c.ValueName, Deserialize(c.Data, kind), kind);
                }
            }
        }

        private static string Serialize(object v, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.MultiString: return string.Join("\0", (string[])v);
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])v);
                case RegistryValueKind.DWord: return unchecked((uint)(int)v).ToString();
                case RegistryValueKind.QWord: return unchecked((ulong)(long)v).ToString();
                default: return Convert.ToString(v);
            }
        }

        private static object Deserialize(string data, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.MultiString: return (data ?? "").Split('\0');
                case RegistryValueKind.Binary: return Convert.FromBase64String(data ?? "");
                case RegistryValueKind.DWord: return unchecked((int)uint.Parse(data));
                case RegistryValueKind.QWord: return unchecked((long)ulong.Parse(data));
                default: return data ?? "";
            }
        }

        private static void RunHidden(string exe, string args)
        {
            var psi = new ProcessStartInfo(exe, args) { CreateNoWindow = true, UseShellExecute = false };
            using (var p = Process.Start(psi)) { p.WaitForExit(5000); }
        }

        // ---- Command-line transport (elevated child): base64 JSON, never a file another process could swap.
        public static string ToArg(object o)
        {
            string json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(o);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        public static T FromArg<T>(string arg)
        {
            string json = Encoding.UTF8.GetString(Convert.FromBase64String(arg));
            return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<T>(json);
        }
    }
}
