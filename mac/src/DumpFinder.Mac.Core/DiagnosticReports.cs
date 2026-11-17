using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Reads macOS diagnostic reports: kernel panics (*.panic, or *.ips with bug_type 210) and user-space
/// reports (*.ips: crashes, memory-pressure kills). Read-only, never throws on a malformed file.
/// </summary>
public static partial class DiagnosticReports
{
    public static readonly string SystemDir = "/Library/Logs/DiagnosticReports";
    public static string UserDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Logs/DiagnosticReports");

    const long MaxFileBytes = 8 * 1024 * 1024;
    const int MaxFiles = 1000;

    // bug_type values seen in the .ips header.
    const string BugPanic = "210";
    const string BugCrash = "309";
    const string BugJetsam = "298";

    public static (List<PanicReport> Panics, List<CrashReport> Reports) ReadAll(DetectionData data, TimeSpan maxAge, List<string> errors)
    {
        var panics = new List<PanicReport>();
        var reports = new List<CrashReport>();
        var cutoff = DateTime.Now - maxAge;

        foreach (var file in Enumerate(new[] { SystemDir, UserDir }, cutoff, errors))
        {
            string text;
            try
            {
                if (new FileInfo(file).Length > MaxFileBytes) continue;
                text = File.ReadAllText(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"Cannot read {Path.GetFileName(file)}: {e.Message}");
                continue;
            }

            var (header, body) = SplitIps(text);
            var bugType = Str(header, "bug_type");
            bool isPanic = file.EndsWith(".panic", StringComparison.OrdinalIgnoreCase) || bugType == BugPanic;
            if (isPanic)
            {
                var p = ParsePanic(file, header, body, File.GetLastWriteTime(file));
                Classify(p, data);
                panics.Add(p);
            }
            else if (file.EndsWith(".ips", StringComparison.OrdinalIgnoreCase))
            {
                reports.Add(ParseIps(file, header, body, bugType, File.GetLastWriteTime(file)));
            }
        }

        panics.Sort((a, b) => Nullable.Compare(b.Time, a.Time));
        reports.Sort((a, b) => Nullable.Compare(b.Time, a.Time));
        return (panics, reports);
    }

    static IEnumerable<string> Enumerate(string[] roots, DateTime cutoff, List<string> errors)
    {
        var files = new List<FileInfo>();
        foreach (var root in roots)
            foreach (var dir in new[] { root, Path.Combine(root, "Retired") })
            {
                if (!Directory.Exists(dir)) continue;
                try
                {
                    files.AddRange(new DirectoryInfo(dir).EnumerateFiles()
                        .Where(f => (f.Extension is ".ips" or ".panic") && f.LastWriteTime >= cutoff));
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    errors.Add($"Cannot list {dir}: {e.Message}");
                }
            }
        return files.OrderByDescending(f => f.LastWriteTime).Take(MaxFiles).Select(f => f.FullName);
    }

    /// <summary>An .ips/.panic file is a one-line JSON header followed by a JSON (or text) body.</summary>
    public static (JsonElement? Header, string Body) SplitIps(string text)
    {
        int nl = text.IndexOf('\n');
        string first = nl < 0 ? text : text[..nl];
        string rest = nl < 0 ? "" : text[(nl + 1)..];
        if (first.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(first);
                return (doc.RootElement.Clone(), rest);
            }
            catch (JsonException) { /* not a header: whole file is the body */ }
        }
        return (null, text);
    }

    public static PanicReport ParsePanic(string file, JsonElement? header, string body, DateTime fallbackTime)
    {
        string panicString = body;
        string os = Str(header, "os_version");
        if (body.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                panicString = Str(root, "panicString") is { Length: > 0 } ps ? ps
                            : Str(root, "macOSPanicString") is { Length: > 0 } ms ? ms
                            : body;
                if (os.Length == 0) os = Str(root, "build");
            }
            catch (JsonException) { /* keep the raw body */ }
        }

        var lines = panicString.Replace("\r", "").Split('\n');
        string message = lines.Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("panic(", StringComparison.Ordinal))
                         ?? lines.Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        if (message.Length > 400) message = message[..400] + "…";

        var task = PanickedTaskRx().Match(panicString);
        var last = LastKextRx().Match(panicString);

        return new PanicReport
        {
            File = file,
            Time = ParseTimestamp(Str(header, "timestamp")) ?? new DateTimeOffset(fallbackTime),
            OsVersion = os,
            Message = message,
            PanickedProcess = task.Success ? task.Groups[1].Value.Trim() : "",
            BacktraceExtensions = BacktraceExtensions(lines),
            LastStartedExtension = last.Success ? last.Groups[1].Value : "",
        };
    }

    /// <summary>Bundle ids under "Kernel Extensions in backtrace:", skipping "dependency:" lines.</summary>
    public static List<string> BacktraceExtensions(string[] lines)
    {
        var result = new List<string>();
        int i = Array.FindIndex(lines, l => l.Contains("Kernel Extensions in backtrace", StringComparison.OrdinalIgnoreCase));
        if (i < 0) return result;
        for (i++; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.Trim().Length == 0) break;
            if (l.TrimStart().StartsWith("dependency:", StringComparison.OrdinalIgnoreCase)) continue;
            var m = KextLineRx().Match(l);
            if (!m.Success) break;
            if (!result.Contains(m.Groups[1].Value)) result.Add(m.Groups[1].Value);
        }
        return result;
    }

    public static void Classify(PanicReport p, DetectionData data)
    {
        var pattern = data.MatchPanic(p.Message + "\n" + p.PanickedProcess)
                      ?? data.MatchPanic(string.Join("\n", p.BacktraceExtensions));
        if (pattern is null) return;
        p.PatternId = pattern.Id;
        p.Explanation = pattern.Explanation;
        p.Advice = pattern.Advice;
    }

    public static CrashReport ParseIps(string file, JsonElement? header, string body, string bugType, DateTime fallbackTime)
    {
        string exType = "", signal = "", termination = "", proc = Str(header, "name");
        if (proc.Length == 0) proc = Str(header, "app_name");
        if (bugType == BugCrash && body.TrimStart().StartsWith('{'))
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("exception", out var ex))
                {
                    exType = Str(ex, "type");
                    signal = Str(ex, "signal");
                }
                if (root.TryGetProperty("termination", out var term)) termination = Str(term, "indicator");
                if (proc.Length == 0) proc = Str(root, "procName");
            }
            catch (JsonException) { /* header only */ }
        }
        if (proc.Length == 0)
        {
            // JetsamEvent-2026-11-10-092629.ips → "JetsamEvent"
            var n = Path.GetFileNameWithoutExtension(file);
            var m = FileDateRx().Match(n);
            proc = m.Success ? n[..m.Index] : n;
        }

        return new CrashReport
        {
            File = file,
            BugType = bugType,
            Kind = bugType switch { BugCrash => ReportKind.Crash, BugJetsam => ReportKind.MemoryPressure, BugPanic => ReportKind.Panic, _ => ReportKind.Other },
            Process = proc,
            Time = ParseTimestamp(Str(header, "timestamp")) ?? new DateTimeOffset(fallbackTime),
            FirstParty = header is { } h && h.ValueKind == JsonValueKind.Object && h.TryGetProperty("is_first_party", out var fp)
                         && fp.ValueKind == JsonValueKind.Number && fp.GetInt32() == 1,
            ExceptionType = exType,
            Signal = signal,
            Termination = termination,
        };
    }

    /// <summary>"2026-11-16 15:57:22.00 +0100" → DateTimeOffset.</summary>
    public static DateTimeOffset? ParseTimestamp(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var m = TimestampRx().Match(s.Trim());
        if (!m.Success) return null;
        var iso = $"{m.Groups[1].Value}T{m.Groups[2].Value}{m.Groups[3].Value}:{m.Groups[4].Value}";
        return DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;
    }

    static string Str(JsonElement? e, string name) =>
        e is { ValueKind: JsonValueKind.Object } o && o.TryGetProperty(name, out var v)
            ? v.ValueKind switch
            {
                JsonValueKind.String => v.GetString() ?? "",
                JsonValueKind.Number => v.GetRawText(),
                _ => "",
            }
            : "";

    [GeneratedRegex(@"Panicked task 0x[0-9a-fA-F]+:.*?pid \d+:\s*([^\r\n]+)")]
    private static partial Regex PanickedTaskRx();

    [GeneratedRegex(@"last (?:started|loaded) kext at \d+:\s+([A-Za-z0-9_.\-]+)")]
    private static partial Regex LastKextRx();

    [GeneratedRegex(@"^\s+([A-Za-z0-9_\-]+(?:\.[A-Za-z0-9_\-]+)+)\(([^)]*)\)")]
    private static partial Regex KextLineRx();

    [GeneratedRegex(@"[-_]\d{4}-\d{2}-\d{2}")]
    private static partial Regex FileDateRx();

    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2}(?:\.\d+)?) ([+-]\d{2})(\d{2})$")]
    private static partial Regex TimestampRx();
}
