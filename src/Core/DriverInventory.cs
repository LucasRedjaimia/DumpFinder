using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;

namespace WinCrashDoctor
{
    public class DriverInfo
    {
        public string DeviceName { get; set; }
        public string DeviceClass { get; set; }
        public string Provider { get; set; }
        public string Version { get; set; }
        public DateTime? Date { get; set; }
        public string InfName { get; set; }
        /// <summary>Kernel image file name, e.g. "nvlddmkm.sys" (null when the device has no service).</summary>
        public string ImageName { get; set; }
        public string ImagePath { get; set; }

        public int AgeDays { get { return Date.HasValue ? (int)(DateTime.Now - Date.Value).TotalDays : -1; } }

        /// <summary>Drivers older than two years deserve a look (except plain system/chipset entries).</summary>
        public bool IsOld { get { return AgeDays > 730; } }
    }

    /// <summary>
    /// Third-party driver inventory: Win32_PnPSignedDriver (what is installed) joined with
    /// Win32_PnPEntity (device → service) and Win32_SystemDriver (service → .sys file),
    /// so a crash module like "nvlddmkm.sys" can be traced back to the device and its driver version.
    /// </summary>
    public static class DriverInventory
    {
        public static List<DriverInfo> Scan()
        {
            var result = new List<DriverInfo>();
            try
            {
                var serviceByDevice = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Query("SELECT DeviceID, Service FROM Win32_PnPEntity", mo =>
                {
                    string id = Str(mo, "DeviceID"), svc = Str(mo, "Service");
                    if (id != null && svc != null) serviceByDevice[id] = svc;
                });

                var imageByService = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                Query("SELECT Name, PathName FROM Win32_SystemDriver", mo =>
                {
                    string name = Str(mo, "Name"), path = Str(mo, "PathName");
                    if (name != null && path != null) imageByService[name] = NormalizeKernelPath(path);
                });

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Query("SELECT DeviceID, DeviceName, DeviceClass, DriverProviderName, DriverVersion, DriverDate, InfName FROM Win32_PnPSignedDriver", mo =>
                {
                    string name = Str(mo, "DeviceName");
                    string provider = Str(mo, "DriverProviderName");
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(provider)) return;
                    if (provider.IndexOf("Microsoft", StringComparison.OrdinalIgnoreCase) >= 0) return;

                    string version = Str(mo, "DriverVersion");
                    if (!seen.Add(name + "|" + version)) return;

                    DateTime? date = null;
                    string raw = Str(mo, "DriverDate");
                    if (raw != null)
                    {
                        try { date = ManagementDateTimeConverter.ToDateTime(raw); }
                        catch (Exception) { }
                    }

                    string svc, image = null;
                    string devId = Str(mo, "DeviceID");
                    if (devId != null && serviceByDevice.TryGetValue(devId, out svc)) imageByService.TryGetValue(svc, out image);

                    result.Add(new DriverInfo
                    {
                        DeviceName = name.Trim(),
                        DeviceClass = Str(mo, "DeviceClass") ?? "",
                        Provider = provider.Trim(),
                        Version = version,
                        Date = date,
                        InfName = Str(mo, "InfName"),
                        ImagePath = image,
                        ImageName = image != null ? Path.GetFileName(image) : null
                    });
                });
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "DriverInventory");
            }
            return result.OrderBy(d => d.DeviceClass).ThenBy(d => d.DeviceName).ToList();
        }

        public static DriverInfo FindByImage(List<DriverInfo> drivers, string image)
        {
            if (drivers == null || string.IsNullOrEmpty(image)) return null;
            string key = Path.GetFileName(image.Trim());
            return drivers.FirstOrDefault(d => string.Equals(d.ImageName, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Version info of a kernel image on disk (company, version, file date) — "" when not found.</summary>
        public static string DescribeImage(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return "";
                var vi = FileVersionInfo.GetVersionInfo(imagePath);
                return string.Format("{0} {1} · {2:d}", vi.CompanyName, vi.FileVersion, File.GetLastWriteTime(imagePath)).Trim();
            }
            catch (Exception ex)
            {
                Log.Warn(ex, imagePath);
                return "";
            }
        }

        private static string NormalizeKernelPath(string p)
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string s = p.Trim().Trim('"');
            if (s.StartsWith(@"\??\", StringComparison.Ordinal)) s = s.Substring(4);
            if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) s = Path.Combine(windir, s.Substring(12));
            else if (s.StartsWith(@"System32\", StringComparison.OrdinalIgnoreCase)) s = Path.Combine(windir, s);
            return s;
        }

        private static void Query(string wql, Action<ManagementObject> each)
        {
            using (var searcher = new ManagementObjectSearcher(wql))
            using (var col = searcher.Get())
            {
                foreach (ManagementObject mo in col)
                {
                    using (mo)
                    {
                        try { each(mo); }
                        catch (Exception ex) { Log.Warn(ex, wql); }
                    }
                }
            }
        }

        private static string Str(ManagementObject mo, string prop)
        {
            object v = mo[prop];
            return v == null ? null : v.ToString();
        }
    }
}
