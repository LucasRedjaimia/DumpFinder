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

        public bool IsWindows11 { get { return Build >= 22000; } }
        public string ProductName { get { return IsWindows11 ? "Windows 11" : "Windows 10"; } }

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
        /// Last day of updates for this build and edition (Microsoft lifecycle), or null when still supported
        /// with no announced date in this table. LTSC releases are not tracked.
        /// </summary>
        public DateTime? EndOfServicing
        {
            get
            {
                if (IsLtsc) return null;
                bool ent = IsEnterpriseLike;
                switch (Build)
                {
                    case 19045: return new DateTime(2025, 10, 14);                                   // Windows 10 22H2 (all editions; ESU aside)
                    case 22000: return ent ? new DateTime(2024, 10, 8) : new DateTime(2023, 10, 10);  // 11 21H2
                    case 22621: return ent ? new DateTime(2025, 10, 14) : new DateTime(2024, 10, 8);  // 11 22H2
                    case 22631: return ent ? new DateTime(2026, 11, 10) : new DateTime(2025, 11, 11); // 11 23H2
                    case 26100: return ent ? new DateTime(2027, 10, 12) : new DateTime(2026, 10, 13); // 11 24H2
                    default:
                        if (Build < 19045) return new DateTime(2025, 10, 14);
                        return null;
                }
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
                    case "corecountryspecific": return Localizer.L("Famille", "Home", "Home", "Home", "Home", "Home");
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
