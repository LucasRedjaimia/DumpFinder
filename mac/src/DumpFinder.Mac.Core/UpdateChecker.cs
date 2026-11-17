using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DumpFinder.Mac.Core;

/// <summary>A newer Mac release (Windows UpdateInfo).</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Title, string Notes, string DownloadUrl, string PageUrl, DateTime? PublishedAt);

/// <summary>One line of release notes rendered as text (Windows MainWindow.Updates RenderNotes).</summary>
public sealed record NoteLine(string Text, bool Heading, int Indent, bool Spacer)
{
    public bool IsBody => !Heading && !Spacer;
}

/// <summary>
/// GitHub release check, same rules as the Windows UpdateChecker (anonymous, 10 s timeout, drafts and
/// pre-releases ignored, only links of this repository opened). The Mac line has its own tags (mac-v0.2.0) and its
/// releases are never "latest" (that one is Windows), so the list of releases is read and the newest mac-v* kept.
/// Nothing is downloaded or installed: the button opens the release in the browser.
/// </summary>
public static partial class UpdateChecker
{
    public const string Owner = "LucasRedjaimia";
    public const string Repo = "DumpFinder";
    public const string RepoUrl = "https://github.com/" + Owner + "/" + Repo;
    public const string TagPrefix = "mac-v";
    const string ReleasesApi = "https://api.github.com/repos/" + Owner + "/" + Repo + "/releases?per_page=30";

    public static Version CurrentVersion
    {
        get
        {
            var v = Assembly.GetEntryAssembly()?.GetName().Version ?? typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }
    }

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    /// <summary>The newest Mac release if it is newer than this app, else null (also on any network error).</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        var latest = await GetLatestAsync(ct).ConfigureAwait(false);
        return latest is not null && latest.Version > CurrentVersion ? latest : null;
    }

    public static async Task<UpdateInfo?> GetLatestAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("DumpFinder-UpdateCheck");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var resp = await http.GetAsync(ReleasesApi, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            return PickLatest(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or OperationCanceledException or JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Newest published (not draft, not pre-release) mac-v* release of a GitHub /releases list.</summary>
    public static UpdateInfo? PickLatest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
        UpdateInfo? best = null;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            var info = Parse(rel);
            if (info is not null && (best is null || info.Version > best.Version)) best = info;
        }
        return best;
    }

    static UpdateInfo? Parse(JsonElement rel)
    {
        if (Bool(rel, "draft") || Bool(rel, "prerelease")) return null;
        var tag = Str(rel, "tag_name");
        if (!tag.StartsWith(TagPrefix, StringComparison.OrdinalIgnoreCase)) return null;   // v* tags are Windows releases
        var v = ParseVersion(tag);
        if (v is null) return null;

        var page = Str(rel, "html_url");
        string? zip = null;
        if (rel.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                var name = Str(a, "name");
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && (zip is null || name.Contains("mac", StringComparison.OrdinalIgnoreCase)))
                    zip = Str(a, "browser_download_url");
            }

        var fallback = IsOwnUrl(page) ? page : RepoUrl + "/releases";
        return new UpdateInfo(v, tag, Str(rel, "name"), Str(rel, "body").Trim(),
            IsOwnUrl(zip) ? zip! : fallback, fallback,
            DateTime.TryParse(Str(rel, "published_at"), null, System.Globalization.DateTimeStyles.RoundtripKind, out var p) ? p.ToLocalTime() : null);
    }

    /// <summary>"mac-v0.2.0" → 0.2.0, "v6.1.0-beta" → 6.1.0, "0.3" → 0.3.0, "latest" → null (Windows ParseVersion + the mac- prefix).</summary>
    public static Version? ParseVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        var s = tag.Trim();
        if (s.StartsWith("mac-", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        s = s.TrimStart('v', 'V');
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) s = s[..cut];
        if (!Version.TryParse(s.Contains('.') ? s : s + ".0", out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    /// <summary>Only links of this repository are ever opened.</summary>
    public static bool IsOwnUrl(string? url) =>
        url is not null && (url.StartsWith(RepoUrl + "/", StringComparison.Ordinal)
                            || url.StartsWith("https://raw.githubusercontent.com/" + Owner + "/" + Repo + "/", StringComparison.Ordinal));

    /// <summary>
    /// Release notes as plain lines (Windows RenderNotes): "#" headings, "-"/"*"/"+" bullets with 16 px per
    /// two spaces of indent, [text](url) → text, **, __ and ` dropped, blank line → spacer.
    /// </summary>
    public static List<NoteLine> NoteLines(string markdown)
    {
        var lines = new List<NoteLine>();
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (raw.Trim().Length == 0)
            {
                if (lines.Count > 0 && !lines[^1].Spacer) lines.Add(new NoteLine("", false, 0, true));
                continue;
            }
            int spaces = raw.Length - raw.TrimStart(' ').Length;
            var t = raw.Trim();
            bool heading = false;
            int indent = 0;
            if (t.StartsWith('#')) { heading = true; t = t.TrimStart('#').Trim(); }
            else if (t.Length > 1 && t[0] is '-' or '*' or '+' && t[1] == ' ') { t = "•  " + t[2..].Trim(); indent = 16 * (spaces / 2); }
            t = LinkRx().Replace(t, "$1").Replace("**", "").Replace("__", "").Replace("`", "");
            lines.Add(new NoteLine(t, heading, indent, false));
        }
        while (lines.Count > 0 && lines[^1].Spacer) lines.RemoveAt(lines.Count - 1);
        return lines;
    }

    static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    static string Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    [GeneratedRegex(@"\[([^\]]+)\]\([^)]*\)")]
    private static partial Regex LinkRx();
}
