using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinCrashDoctor.Detection
{
    /// <summary>
    /// Detection tables (problem NVIDIA releases, DRAM-less NVMe models, Windows end-of-servicing dates, risky
    /// kernel drivers) read from data/detection.json. The copy embedded in the exe is the default; a newer copy can
    /// be downloaded from this repository at launch and is cached in %LOCALAPPDATA%\DumpFinder\data.
    ///
    /// The file only holds data: texts are Locales keys (never free text), version numbers, model tokens, dates and
    /// driver file names, all strictly validated. It can never name a registry key, a service or an executable to
    /// act on: only drivers of the EMBEDDED catalog can be disabled or reverted (RiskyDrivers.IsDisableableFile);
    /// a driver added by a downloaded file is shown as a warning, nothing more.
    /// </summary>
    public sealed class DetectionData
    {
        public const int SupportedSchema = 1;
        public const int MaxBytes = 256 * 1024;
        public const string RawUrl = UpdateChecker.RawPrefix + "main/data/detection.json";

        public sealed class NvidiaProblem { public string Version; public string ReasonKey; }
        public sealed class NvidiaMinimum { public string[] GpuNames; public string MinVersion; public string ReasonKey; }
        public sealed class WindowsEnd { public int Build; public DateTime Consumer; public DateTime Enterprise; public DateTime? ConsumerEsu; }
        public sealed class Driver { public string File; public string OwnerKey; public string WhyKey; public bool RecommendWhenLoaded; }

        public int Version { get; private set; }
        public DateTime Updated { get; private set; }
        /// <summary>"embedded", "cache" or "download".</summary>
        public string Source { get; private set; }

        public IReadOnlyList<NvidiaProblem> NvidiaProblems { get; private set; }
        public IReadOnlyList<NvidiaMinimum> NvidiaMinimums { get; private set; }
        public int HmbIssueBuild { get; private set; }
        public IReadOnlyList<string> DramLessModels { get; private set; }
        public DateTime OlderWindowsEnd { get; private set; }
        public IReadOnlyDictionary<int, WindowsEnd> WindowsReleases { get; private set; }
        public IReadOnlyList<Driver> RiskyDrivers { get; private set; }

        // ==================== Current tables ====================
        private static readonly object Sync = new object();
        private static volatile DetectionData _embedded;
        private static volatile DetectionData _current;

        /// <summary>The copy built into the exe. The only source of truth for what may be disabled or reverted.</summary>
        public static DetectionData Embedded
        {
            get
            {
                if (_embedded == null)
                {
                    lock (Sync)
                    {
                        if (_embedded == null)
                        {
                            string err;
                            _embedded = Parse(ReadEmbedded(), "embedded", null, out err);
                            if (_embedded == null)
                            {
                                Log.Warn(new InvalidDataException(err), "embedded detection.json");
                                _embedded = Empty();
                            }
                        }
                    }
                }
                return _embedded;
            }
        }

        /// <summary>The tables in use: the embedded copy, or a newer validated cache / download.</summary>
        public static DetectionData Current
        {
            get
            {
                if (_current == null)
                {
                    lock (Sync)
                    {
                        if (_current == null) _current = LoadCacheOrEmbedded();
                    }
                }
                return _current;
            }
        }

        /// <summary>Replaces the tables in use (tests). Null goes back to embedded + cache.</summary>
        internal static void SetCurrentForTest(DetectionData d) { lock (Sync) _current = d; }

        public static string CachePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder", "data", "detection.json"); }
        }

        internal static string ReadEmbedded()
        {
            using (Stream s = typeof(DetectionData).Assembly.GetManifestResourceStream("Data.detection.json"))
            {
                if (s == null) return null;
                using (var r = new StreamReader(s, Encoding.UTF8)) return r.ReadToEnd();
            }
        }

        private static DetectionData LoadCacheOrEmbedded()
        {
            DetectionData emb = Embedded;
            try
            {
                string path = CachePath;
                if (File.Exists(path) && new FileInfo(path).Length <= MaxBytes)
                {
                    string err;
                    DetectionData cached = Parse(File.ReadAllText(path, Encoding.UTF8), "cache", emb, out err);
                    if (cached != null && cached.Version > emb.Version) return cached;
                    if (cached == null) Log.Warn(new InvalidDataException(err), "cached detection.json ignored");
                }
            }
            catch (Exception ex) { Log.Warn(ex, "cached detection.json"); }
            return emb;
        }

        // ==================== Online refresh ====================
        private static int _refreshStarted;

        /// <summary>
        /// Launch-time refresh, called by the startup update check (so the Settings › Updates switch turns it off).
        /// Once per process; never throws; any failure keeps the tables in use.
        /// </summary>
        public static async Task RefreshAsync()
        {
            if (Localizer.ReadOnlySettings || (Localizer.CachedSettings != null && Localizer.CachedSettings.UpdateCheckDisabled)) return;
            if (Interlocked.Exchange(ref _refreshStarted, 1) != 0) return;
            try
            {
                string json = await DownloadAsync().ConfigureAwait(false);
                if (json == null) return;
                string why;
                if (!Accept(json, true, out why)) Log.Info("detection.json not applied: " + why);
            }
            catch (Exception ex) { Log.Warn(ex, "detection.json refresh"); }
        }

        /// <summary>The published file, or null (offline, not found, too large). Same request style as UpdateChecker.</summary>
        internal static async Task<string> DownloadAsync()
        {
            try
            {
                if (!UpdateChecker.IsOwnUrl(RawUrl)) return null;
                if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = MaxBytes + 1 })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("DumpFinder-UpdateCheck");
                    using (HttpResponseMessage resp = await http.GetAsync(RawUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null;
                        long? len = resp.Content.Headers.ContentLength;
                        if (len.HasValue && len.Value > MaxBytes) return null;
                        using (Stream s = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            return ReadCapped(s, MaxBytes);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException)) Log.Warn(ex, "detection.json download");
                return null;
            }
        }

        /// <summary>UTF-8 text of the stream, or null when it is larger than <paramref name="cap"/> bytes.</summary>
        internal static string ReadCapped(Stream s, int cap)
        {
            var ms = new MemoryStream();
            var buf = new byte[8192];
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0)
            {
                if (ms.Length + n > cap) return null;
                ms.Write(buf, 0, n);
            }
            return new UTF8Encoding(false, true).GetString(ms.ToArray()); // invalid UTF-8 throws → caller fails silently
        }

        /// <summary>
        /// Validates a downloaded file and, when its version is newer than the tables in use, switches to it
        /// (and caches it when <paramref name="persist"/>). False with a reason otherwise.
        /// </summary>
        internal static bool Accept(string json, bool persist, out string why)
        {
            if (json != null && Encoding.UTF8.GetByteCount(json) > MaxBytes) { why = "larger than " + MaxBytes + " bytes"; return false; }
            DetectionData d = Parse(json, "download", Embedded, out why);
            if (d == null) return false;
            lock (Sync)
            {
                DetectionData cur = Current;
                if (d.Version <= cur.Version) { why = "version " + d.Version + " is not newer than " + cur.Version; return false; }
                _current = d;
            }
            if (persist)
            {
                try { AtomicFile.WriteAllText(CachePath, json, new UTF8Encoding(false)); }
                catch (Exception ex) { Log.Warn(ex, "detection.json cache"); }
            }
            why = null;
            return true;
        }

        // ==================== Parsing & validation ====================
        private static readonly Regex NvVersion = new Regex(@"^\d{3}\.\d{2}$", RegexOptions.CultureInvariant);
        private static readonly Regex Token = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 \-]{0,31}$", RegexOptions.CultureInvariant);
        private static readonly Regex DriverFile = new Regex(@"^[A-Za-z0-9_\-]{1,60}\.sys$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly string[] DriverFields = { "file", "owner", "why", "recommendWhenLoaded" };

        private static DetectionData Empty()
        {
            return new DetectionData
            {
                Source = "embedded",
                NvidiaProblems = new NvidiaProblem[0],
                NvidiaMinimums = new NvidiaMinimum[0],
                HmbIssueBuild = 26100,
                DramLessModels = new string[0],
                OlderWindowsEnd = new DateTime(2025, 10, 14),
                WindowsReleases = new Dictionary<int, WindowsEnd>(),
                RiskyDrivers = new Driver[0]
            };
        }

        private sealed class Invalid : Exception { public Invalid(string m) : base(m) { } }

        /// <summary>
        /// The tables, or null with the reason when anything does not match the schema. With <paramref name="embedded"/>
        /// (downloaded or cached copies), the embedded catalog's drivers are always kept: a download can add warnings,
        /// never remove a detection.
        /// </summary>
        internal static DetectionData Parse(string json, string source, DetectionData embedded, out string error)
        {
            error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(json)) throw new Invalid("empty");
                Dictionary<string, object> root;
                try { root = new JavaScriptSerializer { MaxJsonLength = MaxBytes * 2, RecursionLimit = 16 }.Deserialize<Dictionary<string, object>>(json); }
                catch (Exception ex) { throw new Invalid("not JSON: " + ex.Message); }
                if (root == null) throw new Invalid("not a JSON object");

                if (Int(root, "schema", 1, 1000) != SupportedSchema) throw new Invalid("unsupported schema");
                var d = new DetectionData
                {
                    Source = source,
                    Version = Int(root, "version", 1, int.MaxValue),
                    Updated = Date(root, "updated")
                };

                Dictionary<string, object> nv = Obj(root, "nvidia");
                d.NvidiaProblems = List(nv, "problemVersions", 200).Select(o => new NvidiaProblem
                {
                    Version = Match(Str(o, "version"), NvVersion, "nvidia version"),
                    ReasonKey = Key(Str(o, "reason"), "detection_checks.")
                }).ToArray();
                d.NvidiaMinimums = List(nv, "minimumVersions", 50).Select(o => new NvidiaMinimum
                {
                    GpuNames = Strings(o, "gpuNames", 50).Select(s => Match(s, Token, "gpu name")).ToArray(),
                    MinVersion = Match(Str(o, "minVersion"), NvVersion, "nvidia version"),
                    ReasonKey = Key(Str(o, "reason"), "detection_checks.")
                }).ToArray();

                Dictionary<string, object> nvme = Obj(root, "nvme");
                d.HmbIssueBuild = Int(nvme, "hmbIssueBuild", 10000, 99999);
                d.DramLessModels = Strings(nvme, "dramLessModels", 200).Select(s => Match(s, Token, "model token")).ToArray();

                Dictionary<string, object> win = Obj(root, "windows");
                d.OlderWindowsEnd = Date(win, "olderBuildsEnd");
                var rel = new Dictionary<int, WindowsEnd>();
                foreach (Dictionary<string, object> o in List(win, "releases", 100))
                {
                    var e = new WindowsEnd
                    {
                        Build = Int(o, "build", 10000, 99999),
                        Consumer = Date(o, "consumer"),
                        Enterprise = Date(o, "enterprise"),
                        ConsumerEsu = o.ContainsKey("consumerEsu") ? Date(o, "consumerEsu") : (DateTime?)null
                    };
                    if (rel.ContainsKey(e.Build)) throw new Invalid("duplicate build " + e.Build);
                    rel[e.Build] = e;
                }
                d.WindowsReleases = rel;

                var drivers = new List<Driver>();
                foreach (Dictionary<string, object> o in List(root, "riskyDrivers", 200))
                {
                    // Strict: a field this version does not know (e.g. "service", "disable") is refused, not ignored.
                    string extra = o.Keys.FirstOrDefault(k => !DriverFields.Contains(k, StringComparer.Ordinal));
                    if (extra != null) throw new Invalid("unknown driver field '" + extra + "'");
                    object rec;
                    var drv = new Driver
                    {
                        File = Match(Str(o, "file"), DriverFile, "driver file"),
                        OwnerKey = Key(Str(o, "owner"), "risky_drivers."),
                        WhyKey = Key(Str(o, "why"), "risky_drivers."),
                        RecommendWhenLoaded = o.TryGetValue("recommendWhenLoaded", out rec) && (rec is bool ? (bool)rec : throw new Invalid("recommendWhenLoaded is not a boolean"))
                    };
                    if (drivers.Any(x => x.File.Equals(drv.File, StringComparison.OrdinalIgnoreCase))) throw new Invalid("duplicate driver " + drv.File);
                    drivers.Add(drv);
                }
                if (embedded != null) // never lose an embedded detection
                    foreach (Driver e in embedded.RiskyDrivers)
                        if (!drivers.Any(x => x.File.Equals(e.File, StringComparison.OrdinalIgnoreCase))) drivers.Add(e);
                d.RiskyDrivers = drivers;
                return d;
            }
            catch (Invalid ex) { error = ex.Message; return null; }
            catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; return null; }
        }

        private static object Get(Dictionary<string, object> d, string k)
        {
            object v;
            if (d == null || !d.TryGetValue(k, out v) || v == null) throw new Invalid("missing '" + k + "'");
            return v;
        }

        private static Dictionary<string, object> Obj(Dictionary<string, object> d, string k)
        {
            var o = Get(d, k) as Dictionary<string, object>;
            if (o == null) throw new Invalid("'" + k + "' is not an object");
            return o;
        }

        private static string Str(Dictionary<string, object> d, string k)
        {
            var s = Get(d, k) as string;
            if (s == null) throw new Invalid("'" + k + "' is not a string");
            return s;
        }

        private static int Int(Dictionary<string, object> d, string k, int min, int max)
        {
            object v = Get(d, k);
            if (!(v is int)) throw new Invalid("'" + k + "' is not an integer");
            int i = (int)v;
            if (i < min || i > max) throw new Invalid("'" + k + "' out of range");
            return i;
        }

        private static DateTime Date(Dictionary<string, object> d, string k)
        {
            DateTime t;
            if (!DateTime.TryParseExact(Str(d, k), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) throw new Invalid("'" + k + "' is not a yyyy-MM-dd date");
            return t;
        }

        private static IEnumerable<object> Items(Dictionary<string, object> d, string k, int max)
        {
            object v = Get(d, k);
            var e = v as IEnumerable;
            if (e == null || v is string || v is IDictionary) throw new Invalid("'" + k + "' is not an array");
            List<object> l = e.Cast<object>().ToList();
            if (l.Count > max) throw new Invalid("'" + k + "' has too many entries");
            return l;
        }

        private static List<Dictionary<string, object>> List(Dictionary<string, object> d, string k, int max)
        {
            return Items(d, k, max).Select(o => o as Dictionary<string, object> ?? throw new Invalid("'" + k + "' holds a non-object")).ToList();
        }

        private static List<string> Strings(Dictionary<string, object> d, string k, int max)
        {
            List<string> l = Items(d, k, max).Select(o => o as string ?? throw new Invalid("'" + k + "' holds a non-string")).ToList();
            if (l.Count == 0) throw new Invalid("'" + k + "' is empty");
            return l;
        }

        private static string Match(string s, Regex re, string what)
        {
            if (!re.IsMatch(s)) throw new Invalid("invalid " + what + " '" + s + "'");
            return s;
        }

        /// <summary>A Locales key of the expected section that exists in the English table: the file never carries text.</summary>
        private static string Key(string key, string prefix)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || !Localizer.Table("en").ContainsKey(key)) throw new Invalid("unknown text key '" + key + "'");
            return key;
        }
    }
}
