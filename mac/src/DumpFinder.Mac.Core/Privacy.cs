using System.Text.RegularExpressions;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Removes personal details from text that leaves the Mac (reports, forum copy, AI prompts), like the Windows
/// Privacy.Redact: the account name inside paths, the computer name (macOS puts it in some report file names),
/// and the path / query of web addresses (only the site is kept).
/// </summary>
public static partial class Privacy
{
    public static string Redact(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        string s = text;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('/');
        if (home.Length > 1) s = s.Replace(home, "~", StringComparison.Ordinal);
        s = UsersRx().Replace(s, m => m.Groups[2].Value is "Shared" or "Guest" ? m.Value : m.Groups[1].Value + "<user>");
        foreach (var name in MachineNames())
            s = Regex.Replace(s, Regex.Escape(name), "<mac>", RegexOptions.IgnoreCase);
        s = UrlRx().Replace(s, m => m.Groups[1].Value.ToLowerInvariant() + "://" + m.Groups[2].Value);
        return s;
    }

    /// <summary>Host name in its usual spellings: "MacBook-Pro-de-Lucas", "MacBook Pro de Lucas".</summary>
    static IEnumerable<string> MachineNames()
    {
        var n = Environment.MachineName;
        if (n.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) n = n[..^6];
        if (n.Length < 4) yield break;
        yield return n;
        if (n.Contains('-')) yield return n.Replace('-', ' ');
    }

    [GeneratedRegex(@"(/Users/)([^/\s""']+)")]
    private static partial Regex UsersRx();

    [GeneratedRegex(@"\b(https?)://([A-Za-z0-9\.\-]+)(:[0-9]+)?[^\s""'<>|]*", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRx();
}
