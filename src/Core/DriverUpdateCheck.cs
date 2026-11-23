using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    /// <summary>A driver Windows Update offers for one hardware ID of this PC.</summary>
    public class DriverUpdateOffer
    {
        public string HardwareId { get; set; }
        public string Title { get; set; }
        public string Provider { get; set; }
        /// <summary>Version parsed from the update title ("1.16.4.14"); null when the title has none.</summary>
        public string Version { get; set; }
        /// <summary>Driver date as "yyyy-MM-dd" (kept as text: JavaScriptSerializer shifts DateTime to UTC).</summary>
        public string Date { get; set; }

        public DateTime? DateValue
        {
            get
            {
                DateTime d;
                return DateTime.TryParseExact(Date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d) ? d : (DateTime?)null;
            }
        }
    }

    public class DriverUpdateResult
    {
        /// <summary>When Windows Update was asked, as "o" round-trip text (local time).</summary>
        public string CheckedAt { get; set; }
        public List<DriverUpdateOffer> Offers { get; set; }

        public DateTime CheckedAtValue
        {
            get
            {
                DateTime d;
                return DateTime.TryParse(CheckedAt ?? "", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out d) ? d : DateTime.MinValue;
            }
        }
    }

    /// <summary>
    /// Asks Windows Update (the Windows Update Agent COM API, the same catalog as Settings › Windows Update ›
    /// Optional updates) which drivers it has for this PC's hardware, so the driver list can tell
    /// "a newer version exists" apart from "old, but nothing newer is published". Read-only: it searches, never
    /// downloads or installs. The answer is cached for a few hours because the search takes 10–60 seconds.
    /// </summary>
    public static class DriverUpdateCheck
    {
        public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(12);
        public static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(3);

        private static string CachePath
        {
            get { return Path.Combine(Localizer.AppDataDir, "driver_updates.json"); }
        }

        /// <summary>Cached answer when fresh enough (unless <paramref name="force"/>), otherwise a new search. Throws when Windows Update cannot be reached.</summary>
        public static DriverUpdateResult Get(bool force)
        {
            if (!force)
            {
                DriverUpdateResult cached = LoadCache();
                if (cached != null)
                {
                    TimeSpan age = DateTime.Now - cached.CheckedAtValue;
                    if (age >= TimeSpan.Zero && age < CacheLifetime) return cached;
                }
            }

            Task<List<DriverUpdateOffer>> search = Task.Run(() => SearchWindowsUpdate());
            if (!search.Wait(SearchTimeout)) throw new TimeoutException("Windows Update search timed out");
            var result = new DriverUpdateResult { CheckedAt = DateTime.Now.ToString("o", CultureInfo.InvariantCulture), Offers = search.Result };
            SaveCache(result);
            return result;
        }

        /// <summary>Short reason for the UI: a Windows Update error code, or "timeout".</summary>
        public static string Describe(Exception ex)
        {
            var agg = ex as AggregateException;
            if (agg != null && agg.InnerException != null) ex = agg.InnerException;
            var tie = ex as TargetInvocationException;
            if (tie != null && tie.InnerException != null) ex = tie.InnerException;
            if (ex is TimeoutException) return "timeout";
            if (ex is COMException) return "0x" + ex.HResult.ToString("X8");
            return ex.GetType().Name;
        }

        /// <summary>The newest offer for this driver's hardware that is newer than what is installed, or null.</summary>
        public static DriverUpdateOffer FindNewer(IEnumerable<DriverUpdateOffer> offers, DriverInfo d)
        {
            if (offers == null || d == null || d.HardwareIds == null || d.HardwareIds.Count == 0) return null;
            var ids = new HashSet<string>(d.HardwareIds, StringComparer.OrdinalIgnoreCase);
            return offers
                .Where(o => o != null && o.HardwareId != null && ids.Contains(o.HardwareId))
                .Where(o => IsNewer(o.DateValue, o.Version, d.Date, d.Version))
                .OrderByDescending(o => o.DateValue ?? DateTime.MinValue)
                .ThenByDescending(o => ParseVersion(o.Version))
                .FirstOrDefault();
        }

        /// <summary>
        /// Windows ranks drivers for the same device by date first, then version: an offer is newer when its
        /// date is later, or the dates match and its version is higher. Unknown values never count as newer.
        /// </summary>
        public static bool IsNewer(DateTime? offerDate, string offerVersion, DateTime? installedDate, string installedVersion)
        {
            if (offerDate.HasValue && installedDate.HasValue)
            {
                if (offerDate.Value.Date > installedDate.Value.Date) return true;
                if (offerDate.Value.Date < installedDate.Value.Date) return false;
            }
            Version ov = ParseVersion(offerVersion), iv = ParseVersion(installedVersion);
            return ov != null && iv != null && ov > iv;
        }

        public static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            Match m = Regex.Match(s, @"\d+(\.\d+){1,3}");
            Version v;
            return m.Success && Version.TryParse(m.Value, out v) ? v : null;
        }

        /// <summary>Last version-looking token of a Windows Update title ("… - MEDIA - 1.16.4.14", "… Update (2.21.4.0)").</summary>
        public static string VersionFromTitle(string title)
        {
            if (string.IsNullOrEmpty(title)) return null;
            MatchCollection all = Regex.Matches(title, @"\b\d+(\.\d+){2,3}\b");
            return all.Count > 0 ? all[all.Count - 1].Value : null;
        }

        // ==================== Windows Update Agent (late-bound COM, no interop assembly) ====================

        private static List<DriverUpdateOffer> SearchWindowsUpdate()
        {
            var offers = new List<DriverUpdateOffer>();
            Type sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session", true);
            object session = Activator.CreateInstance(sessionType);
            try
            {
                Set(session, "ClientApplicationID", "Dump Finder");
                object searcher = Call(session, "CreateUpdateSearcher");
                Set(searcher, "ServerSelection", 2); // ssWindowsUpdate: the public catalog, even on a WSUS-managed PC
                Set(searcher, "Online", true);
                object found = Call(searcher, "Search", "IsInstalled=0 and Type='Driver'");
                object updates = Get(found, "Updates");
                int count = Convert.ToInt32(Get(updates, "Count"));
                for (int i = 0; i < count; i++)
                {
                    object u = Get(updates, "Item", i);
                    try
                    {
                        string title = Convert.ToString(Get(u, "Title"));
                        object rawDate = Get(u, "DriverVerDate");
                        offers.Add(new DriverUpdateOffer
                        {
                            HardwareId = Convert.ToString(Get(u, "DriverHardwareID")),
                            Title = title,
                            Provider = Convert.ToString(Get(u, "DriverProvider")),
                            Version = VersionFromTitle(title),
                            Date = rawDate is DateTime ? ((DateTime)rawDate).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null
                        });
                    }
                    catch (Exception ex) { Log.Warn(ex, "DriverUpdateCheck item"); }
                    finally { Release(u); }
                }
                Release(updates);
                Release(found);
                Release(searcher);
            }
            finally { Release(session); }
            return offers;
        }

        private static object Get(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.GetProperty, null, o, args);
        }

        private static void Set(object o, string name, object value)
        {
            o.GetType().InvokeMember(name, BindingFlags.SetProperty, null, o, new[] { value });
        }

        private static object Call(object o, string name, params object[] args)
        {
            return o.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, o, args);
        }

        private static void Release(object o)
        {
            try { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
            catch (Exception) { }
        }

        // ==================== Cache ====================

        private static DriverUpdateResult LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var r = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<DriverUpdateResult>(File.ReadAllText(CachePath, Encoding.UTF8));
                return r != null && r.Offers != null ? r : null;
            }
            catch (Exception ex) { Log.Warn(ex, CachePath); return null; }
        }

        private static void SaveCache(DriverUpdateResult r)
        {
            try { AtomicFile.WriteAllText(CachePath, new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(r), Encoding.UTF8); }
            catch (Exception ex) { Log.Warn(ex, CachePath); }
        }
    }
}
