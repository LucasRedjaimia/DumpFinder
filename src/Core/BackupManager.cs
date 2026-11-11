using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public class RegistryBackupData
    {
        public string CreatedAt { get; set; }
        public Dictionary<string, int> Values { get; set; } // -999999 means value did not exist originally
    }

    public static class BackupManager
    {
        private const int MissingSentinel = -999999;

        public static string SnapshotPath
        {
            get { return Path.Combine(Localizer.AppDataDir, "initial_registry_backup.json"); }
        }

        public static bool HasSnapshot()
        {
            return File.Exists(SnapshotPath);
        }

        public static string GetSnapshotDateDisplay()
        {
            try
            {
                if (!File.Exists(SnapshotPath)) return "";
                var ser = new JavaScriptSerializer();
                var data = ser.Deserialize<RegistryBackupData>(File.ReadAllText(SnapshotPath, Encoding.UTF8));
                return data != null ? (data.CreatedAt ?? "") : "";
            }
            catch (Exception ex) { Log.Warn(ex); return ""; }
        }

        public static void EnsureInitialSnapshotSaved()
        {
            if (File.Exists(SnapshotPath)) return;
            SaveSnapshotNow();
        }

        public static void SaveSnapshotNow()
        {
            try
            {
                var dict = new Dictionary<string, int>
                {
                    { "OverlayTestMode", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode") },
                    { "OverlayMinFPS", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS") },
                    { "TdrDelay", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay") },
                    { "TdrDdiDelay", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay") },
                    { "HwSchMode", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode") },
                    { "ExcludeWUDriversInQualityUpdate", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate") },
                    { "SearchOrderConfig", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig") },
                    { "HmbAllocationPolicy", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy") },
                    { "IoTimeoutValue", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "IoTimeoutValue") },
                    { "GameDVR_Enabled", ReadDwordOrMissing(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled") },
                    { "HiberbootEnabled", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled") },
                    { "AmdWddmgIssueDet", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\amdwddmg", "EnableIssueDetection") },
                    { "AmdKmdagIssueDet", ReadDwordOrMissing(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\amdkmdag", "EnableIssueDetection") }
                };

                var payload = new RegistryBackupData
                {
                    CreatedAt = DateTime.Now.ToString("dd/MM/yyyy HH:mm"),
                    Values = dict
                };

                var ser = new JavaScriptSerializer();
                File.WriteAllText(SnapshotPath, ser.Serialize(payload), Encoding.UTF8);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        public static bool RestoreSnapshot()
        {
            if (!File.Exists(SnapshotPath)) return false;

            if (!Elevation.IsAdministrator())
                return Elevation.RunSelf("--elevated-op restore_snapshot", 30000) == ElevationOutcome.Success;

            return RestoreSnapshotElevatedInternal();
        }

        public static bool RestoreSnapshotElevatedInternal()
        {
            try
            {
                if (!File.Exists(SnapshotPath)) return false;
                var ser = new JavaScriptSerializer();
                var data = ser.Deserialize<RegistryBackupData>(File.ReadAllText(SnapshotPath, Encoding.UTF8));
                if (data == null || data.Values == null) return false;

                ApplyOrDelete(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayTestMode", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\Dwm", "OverlayMinFPS", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDelay", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "TdrDdiDelay", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching", "SearchOrderConfig", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "HmbAllocationPolicy", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\stornvme\Parameters\Device", "IoTimeoutValue", data.Values);
                ApplyOrDelete(RegistryHive.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", data.Values);
                ApplyOrDelete(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Control\Session Manager\Power", "HiberbootEnabled", data.Values);
                ApplyOrDeleteIfKeyExists(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\amdwddmg", "EnableIssueDetection", "AmdWddmgIssueDet", data.Values);
                ApplyOrDeleteIfKeyExists(RegistryHive.LocalMachine, @"SYSTEM\CurrentControlSet\Services\amdkmdag", "EnableIssueDetection", "AmdKmdagIssueDet", data.Values);
                return true;
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        public static bool CreateSystemRestorePoint()
        {
            if (!Elevation.IsAdministrator())
                return Elevation.RunSelf("--elevated-op create_restore_point", 60000) == ElevationOutcome.Success;

            return CreateSystemRestorePointElevatedInternal();
        }

        public static bool CreateSystemRestorePointElevatedInternal()
        {
            try
            {
                // Allow immediate creation even if another restore point was created in the last 24h
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var srKey = baseKey.CreateSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"))
                {
                    if (srKey != null)
                        srKey.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
                }

                ManagementScope scope = new ManagementScope(@"\\localhost\root\default");
                ManagementPath path = new ManagementPath("SystemRestore");
                using (ManagementClass mc = new ManagementClass(scope, path, new ObjectGetOptions()))
                {
                    ManagementBaseObject inParams = mc.GetMethodParameters("CreateRestorePoint");
                    inParams["Description"] = "Dump Finder Safety Point (" + DateTime.Now.ToString("yyyy-MM-dd HH:mm") + ")";
                    inParams["RestorePointType"] = 12; // MODIFY_SETTINGS
                    inParams["EventType"] = 100;       // BEGIN_SYSTEM_CHANGE
                    ManagementBaseObject outParams = mc.InvokeMethod("CreateRestorePoint", inParams, null);
                    uint ret = Convert.ToUInt32(outParams["ReturnValue"]);
                    return ret == 0;
                }
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        private static int ReadDwordOrMissing(RegistryHive hive, string subKey, string name)
        {
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var sk = bk.OpenSubKey(subKey, false))
                {
                    if (sk == null) return MissingSentinel;
                    object v = sk.GetValue(name);
                    if (v == null) return MissingSentinel;
                    return Convert.ToInt32(v);
                }
            }
            catch (Exception ex) { Log.Warn(ex); return MissingSentinel; }
        }

        private static void ApplyOrDelete(RegistryHive hive, string subKey, string name, Dictionary<string, int> map)
        {
            if (!map.ContainsKey(name)) return;
            int val = map[name];
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                {
                    if (val == MissingSentinel)
                    {
                        using (var sk = bk.OpenSubKey(subKey, true))
                        {
                            if (sk != null && sk.GetValue(name) != null)
                                sk.DeleteValue(name, false);
                        }
                    }
                    else
                    {
                        using (var sk = bk.CreateSubKey(subKey))
                        {
                            if (sk != null)
                                sk.SetValue(name, val, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private static void ApplyOrDeleteIfKeyExists(RegistryHive hive, string subKey, string valueName, string backupKeyName, Dictionary<string, int> map)
        {
            if (!map.ContainsKey(backupKeyName)) return;
            int val = map[backupKeyName];
            try
            {
                using (var bk = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64))
                using (var sk = bk.OpenSubKey(subKey, true))
                {
                    if (sk == null) return;
                    if (val == MissingSentinel)
                    {
                        if (sk.GetValue(valueName) != null)
                            sk.DeleteValue(valueName, false);
                    }
                    else
                    {
                        sk.SetValue(valueName, val, RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
        }
    }
}
