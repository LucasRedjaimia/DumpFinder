using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace WinCrashDoctor.Detection
{
    /// <summary>
    /// NVMe drives on this PC. The stornvme HMB allocation issue only exists on Windows 11 24H2+ (build 26100)
    /// with an NVMe drive, so the HMB fix and advice are shown only when both are true.
    /// </summary>
    public static class NvmeDrives
    {
        /// <summary>First Windows build with the HMB issue (data/detection.json, nvme.hmbIssueBuild).</summary>
        public static int HmbIssueBuild { get { return DetectionData.Current.HmbIssueBuild; } }

        private static List<string> _models;

        /// <summary>Friendly names of the NVMe drives (read once per process).</summary>
        public static IReadOnlyList<string> Models
        {
            get
            {
                if (_models == null) _models = Read();
                return _models;
            }
        }

        /// <summary>DRAM-less models known to rely on the Host Memory Buffer (data/detection.json, nvme.dramLessModels).</summary>
        public static bool IsDramLess(string model)
        {
            return !string.IsNullOrEmpty(model)
                && DetectionData.Current.DramLessModels.Any(t => model.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        public static bool WindowsHasHmbIssue { get { return WindowsRelease.Current.Build >= HmbIssueBuild; } }

        /// <summary>True when the HMB fix can concern this PC: Windows 11 24H2+ and at least one NVMe drive.</summary>
        public static bool HmbIssueApplies { get { return WindowsHasHmbIssue && Models.Count > 0; } }

        private static List<string> Read()
        {
            var list = new List<string>();
            try
            {
                using (var s = Wmi.Searcher(@"\\.\root\Microsoft\Windows\Storage", "SELECT FriendlyName, BusType FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject mo in s.Get())
                    {
                        if (Convert.ToUInt16(mo["BusType"] ?? 0) == 17) // 17 = NVMe
                            list.Add(Convert.ToString(mo["FriendlyName"]).Trim());
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return list;
        }
    }
}
