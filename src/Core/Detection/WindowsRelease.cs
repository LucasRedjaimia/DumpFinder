using System;
using System.Globalization;
using Microsoft.Win32;

namespace WinCrashDoctor.Detection
{
    /// <summary>
    /// Windows version identified by build number + UBR + edition (not by the "24H2"-style label, and not by
    /// ProductName, which still says "Windows 10" on Windows 11). Knows when each release stops getting updates.
    /// </summary>
    public sealed class WindowsRelease
    {
        public int Build { get; private set; }
        public int Ubr { get; private set; }
        public string DisplayVersion { get; private set; }
        public string Edition { get; private set; }

        /// <summary>"Client", "Server" or "Server Core" (registry InstallationType).</summary>
        public string InstallationType { get; private set; }

        /// <summary>Windows Server shares build numbers with client releases (17763, 20348, 26100…) but not their lifecycle.</summary>
        public bool IsServer
        {
            get
            {
                return (InstallationType ?? "").StartsWith("Server", StringComparison.OrdinalIgnoreCase)
                    || (Edition ?? "").StartsWith("Server", StringComparison.OrdinalIgnoreCase);
            }
        }

        public bool IsWindows11 { get { return !IsServer && Build >= 22000; } }
        public string ProductName { get { return IsServer ? "Windows Server" : (IsWindows11 ? "Windows 11" : "Windows 10"); } }

        /// <summary>
        /// Windows 10 22H2 still installing monthly updates after its end of support (October 2025, build 19045.6456):
        /// the PC is enrolled in Extended Security Updates.
        /// </summary>
        public bool ReceivesExtendedSecurityUpdates { get { return Build == 19045 && Ubr > 6456; } }

        /// <summary>Enterprise / Education / IoT get one more year on most releases.</summary>
        public bool IsEnterpriseLike
        {
            get
            {
                string e = Edition ?? "";
                return e.IndexOf("Enterprise", StringComparison.OrdinalIgnoreCase) >= 0 || e.IndexOf("Education", StringComparison.OrdinalIgnoreCase) >= 0
                    || e.IndexOf("IoT", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        }

        public bool IsLtsc { get { return (Edition ?? "").IndexOf("LTSC", StringComparison.OrdinalIgnoreCase) >= 0 || (Edition ?? "").IndexOf("EnterpriseS", StringComparison.OrdinalIgnoreCase) >= 0; } }

        private static WindowsRelease _current;
        public static WindowsRelease Current
        {
            get
            {
                if (_current != null) return _current;
                var w = new WindowsRelease();
                try
                {
                    using (var bk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (var k = bk.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    {
                        if (k != null)
                        {
                            int b;
                            int.TryParse(Convert.ToString(k.GetValue("CurrentBuild") ?? k.GetValue("CurrentBuildNumber") ?? "0"), out b);
                            w.Build = b;
                            object ubr = k.GetValue("UBR");
                            w.Ubr = ubr is int ? (int)ubr : 0;
                            w.DisplayVersion = Convert.ToString(k.GetValue("DisplayVersion") ?? k.GetValue("ReleaseId") ?? "");
                            w.Edition = Convert.ToString(k.GetValue("EditionID") ?? "");
                            w.InstallationType = Convert.ToString(k.GetValue("InstallationType") ?? "");
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex); }
                if (w.Build == 0) w.Build = Environment.OSVersion.Version.Build;
                _current = w;
                return w;
            }
        }

        /// <summary>
        /// Last day of updates for this build and edition (Microsoft lifecycle, dates in data/detection.json), or null
        /// when still supported with no announced date in the table. LTSC and Server releases are not tracked.
        /// </summary>
        public DateTime? EndOfServicing
        {
            get
            {
                if (IsLtsc || IsServer) return null; // separate lifecycles, not tracked here
                bool ent = IsEnterpriseLike;
                DetectionData data = DetectionData.Current;
                DetectionData.WindowsEnd r;
                if (!data.WindowsReleases.TryGetValue(Build, out r))
                    return Build < 19045 ? data.OlderWindowsEnd : (DateTime?)null;
                // Windows 10 22H2 on ESU: consumer ESU runs one year; commercial ESU can be bought for up to three,
                // so the end date of an enrolled business PC is unknown here.
                if (ReceivesExtendedSecurityUpdates && r.ConsumerEsu.HasValue) return ent ? (DateTime?)null : r.ConsumerEsu.Value;
                return ent ? r.Enterprise : r.Consumer;
            }
        }

        public string Label
        {
            get
            {
                return ProductName + (string.IsNullOrEmpty(DisplayVersion) ? "" : " " + DisplayVersion)
                     + " · build " + Build + (Ubr > 0 ? "." + Ubr : "") + (string.IsNullOrEmpty(Edition) ? "" : " · " + EditionName);
            }
        }

        public string EditionName
        {
            get
            {
                switch ((Edition ?? "").ToLowerInvariant())
                {
                    case "core":
                    case "coresinglelanguage":
                    case "corecountryspecific": return Localizer.S("windows_release.home");
                    case "professional":
                    case "professionalworkstation":
                    case "professionaleducation": return "Pro";
                    case "enterprise": return "Enterprise";
                    case "education": return "Education";
                    default: return Edition;
                }
            }
        }

        public static string Date(DateTime d) { return d.ToString("d MMMM yyyy", Localizer.Culture); }
    }
}
