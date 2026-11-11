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

        /// <summary>The latest release when it is newer than this exe, otherwise null.</summary>
        public static async Task<UpdateInfo> CheckAsync(CancellationToken ct)
        {
            UpdateInfo latest = await GetLatestAsync(ct).ConfigureAwait(false);
            return latest != null && latest.Version > CurrentVersion ? latest : null;
        }

        public static async Task<UpdateInfo> GetLatestAsync(CancellationToken ct)
        {
            try
            {
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
            string exe = null, zip = null;
            var assets = root.ContainsKey("assets") ? root["assets"] as IEnumerable : null;
            if (assets != null)
            {
                foreach (object o in assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name") ?? "";
                    string url = Str(a, "browser_download_url");
                    if (exe == null && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) exe = url;
                    else if (zip == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) zip = url;
                }
            }

            DateTime published;
            return new UpdateInfo
            {
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

        /// <summary>Only links into this repository are ever opened.</summary>
        public static bool IsOwnUrl(string url)
        {
            return !string.IsNullOrEmpty(url) &&
                   url.StartsWith(RepoUrl + "/", StringComparison.OrdinalIgnoreCase) &&
                   Uri.IsWellFormedUriString(url, UriKind.Absolute);
        }

        private static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? v as string : null; }
        private static bool Bool(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v is bool && (bool)v; }
    }
}
