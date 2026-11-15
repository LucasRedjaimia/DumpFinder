using System;
using System.Text.RegularExpressions;

namespace WinCrashDoctor
{
    /// <summary>
    /// Removes personal details from text that leaves the PC (AI prompts, MCP answers, exported reports):
    /// the Windows account name inside paths, and the path / query of web addresses (only the site is kept).
    /// </summary>
    public static class Privacy
    {
        private static readonly Regex UrlRx = new Regex(@"\b(https?)://([A-Za-z0-9\.\-]+)(:[0-9]+)?[^\s""'<>|]*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex UsersRx = new Regex(@"(?i)([A-Z]:\\Users\\)([^\\\r\n""']+)");

        /// <summary>"https://site.example/path?q=1" → "https://site.example".</summary>
        public static string UrlOrigin(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            Match m = UrlRx.Match(url);
            return m.Success ? m.Groups[1].Value.ToLowerInvariant() + "://" + m.Groups[2].Value : url;
        }

        public static string Redact(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            try
            {
                string s = text;
                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(profile) && profile.Length > 3)
                    s = Regex.Replace(s, Regex.Escape(profile.TrimEnd('\\')), "%USERPROFILE%", RegexOptions.IgnoreCase);
                // Any other account folder (dump copied from another profile, admin account…).
                s = UsersRx.Replace(s, m => m.Groups[2].Value.Equals("Public", StringComparison.OrdinalIgnoreCase)
                                            || m.Groups[2].Value.Equals("Default", StringComparison.OrdinalIgnoreCase)
                                            ? m.Value : m.Groups[1].Value + "<user>");
                s = UrlRx.Replace(s, m => m.Groups[1].Value.ToLowerInvariant() + "://" + m.Groups[2].Value);
                return s;
            }
            catch (Exception ex)
            {
                Log.Warn(ex);
                return text;
            }
        }
    }
}
