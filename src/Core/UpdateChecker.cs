using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    public sealed class UpdateInfo
    {
        public Version Version;
        public string Tag;
        public string Title;
        public string Notes;        // release description (Markdown)
        public string DownloadUrl;  // the .exe (or .zip) asset, else the release page
        public string PageUrl;
        public DateTime? PublishedAt;
        public string ExeUrl;       // the .exe asset itself (null when the release has none)
        public string ExeSha256;    // its SHA-256 as GitHub computed it ("digest"), lowercase hex; null when not given
        public long ExeSize;        // its size in bytes, 0 when unknown
    }

    /// <summary>
    /// Asks GitHub for the latest published release of this app. One anonymous HTTPS request, no data sent besides
    /// a generic User-Agent; any failure (offline, no release yet, rate limit) is silent.
    /// </summary>
    public static class UpdateChecker
    {
        public const string Owner = "LucasRedjaimia";
        public const string Repo = "DumpFinder";
        public const string RepoUrl = "https://github.com/" + Owner + "/" + Repo;
        private const string LatestApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases/latest";

        public static Version CurrentVersion
        {
            get
            {
                Version v = Assembly.GetExecutingAssembly().GetName().Version;
                return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            }
        }

        public static string CurrentVersionText { get { return CurrentVersion.ToString(3); } }

        /// <summary>
        /// Launch-time check (runs only when "check for updates at launch" is on): the latest release when it is newer
        /// than this exe, otherwise null. Also starts the background refresh of the detection tables
        /// (data/detection.json), which shares the same on/off switch.
        /// </summary>
        public static async Task<UpdateInfo> CheckAsync(CancellationToken ct)
        {
            Task refresh = Detection.DetectionData.RefreshAsync(); // never throws; own 10 s timeout
            UpdateInfo latest = await GetLatestAsync(ct).ConfigureAwait(false);
            await refresh.ConfigureAwait(false);
            return latest != null && latest.Version > CurrentVersion ? latest : null;
        }

        public static async Task<UpdateInfo> GetLatestAsync(CancellationToken ct)
        {
            try
            {
                // .NET 4.8 defaults to SystemDefault (TLS 1.2 + 1.3 per Windows); OR-ing Tls12 into it would pin TLS 1.2 only.
                if (ServicePointManager.SecurityProtocol != SecurityProtocolType.SystemDefault)
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("DumpFinder-UpdateCheck");
                    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                    using (HttpResponseMessage resp = await http.GetAsync(LatestApi, ct).ConfigureAwait(false))
                    {
                        if (!resp.IsSuccessStatusCode) return null; // 404 = no release published yet
                        string json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                        return Parse(json);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!(ex is OperationCanceledException)) Log.Warn(ex, "update check");
                return null;
            }
        }

        internal static UpdateInfo Parse(string json)
        {
            var ser = new JavaScriptSerializer { MaxJsonLength = 4 * 1024 * 1024 };
            var root = ser.Deserialize<Dictionary<string, object>>(json);
            if (root == null) return null;
            if (Bool(root, "draft") || Bool(root, "prerelease")) return null;

            string tag = Str(root, "tag_name");
            Version v = ParseVersion(tag);
            if (v == null) return null;

            string page = Str(root, "html_url");
            string exe = null, zip = null, exeDigest = null;
            long exeSize = 0;
            var assets = root.ContainsKey("assets") ? root["assets"] as IEnumerable : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name") ?? "";
                    string url = Str(a, "browser_download_url");
                    if (exe == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        exe = url;
                        exeDigest = Sha256Of(Str(a, "digest"));
                        object size;
                        if (a.TryGetValue("size", out size) && size != null) long.TryParse(Convert.ToString(size, System.Globalization.CultureInfo.InvariantCulture), out exeSize);
                    }
                    else if (zip == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = url;
                }
            }

            DateTime published;
            return new UpdateInfo
            {
                ExeUrl = IsOwnUrl(exe) ? exe : null,
                ExeSha256 = exeDigest,
                ExeSize = exeSize,
                Version = v,
                Tag = tag,
                Title = Str(root, "name"),
                Notes = (Str(root, "body") ?? "").Trim(),
                PageUrl = IsOwnUrl(page) ? page : RepoUrl + "/releases/latest",
                DownloadUrl = IsOwnUrl(exe) ? exe : IsOwnUrl(zip) ? zip : (IsOwnUrl(page) ? page : RepoUrl + "/releases/latest"),
                PublishedAt = DateTime.TryParse(Str(root, "published_at"), out published) ? published : (DateTime?)null
            };
        }

        /// <summary>"v6.1.0", "6.1", "V6.1.0.0" → 6.1.0 (null when it is not a version).</summary>
        public static Version ParseVersion(string tag)
        {
            if (string.IsNullOrWhiteSpace(tag)) return null;
            string s = tag.Trim().TrimStart('v', 'V');
            int dash = s.IndexOfAny(new[] { '-', '+', ' ' });
            if (dash > 0) s = s.Substring(0, dash);
            Version v;
            if (!Version.TryParse(s.Contains(".") ? s : s + ".0", out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        /// <summary>Raw files of this repository (data/detection.json).</summary>
        public const string RawPrefix = "https://raw.githubusercontent.com/" + Owner + "/" + Repo + "/";

        /// <summary>Only links into this repository (pages, or its raw files) are ever opened or downloaded.</summary>
        public static bool IsOwnUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   (url.StartsWith(RepoUrl + "/", StringComparison.OrdinalIgnoreCase) || url.StartsWith(RawPrefix, StringComparison.OrdinalIgnoreCase)) &&
                   Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        /// <summary>"sha256:ab12…" (GitHub asset digest) → "ab12…" when it is 64 hex characters, else null.</summary>
        internal static string Sha256Of(string digest)
        {
            if (string.IsNullOrEmpty(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) return null;
            string hex = digest.Substring(7).Trim().ToLowerInvariant();
            if (hex.Length != 64) return null;
            foreach (char c in hex) if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return null;
            return hex;
        }

        private static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v as string : null; }
        private static bool Bool(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
    }
}
