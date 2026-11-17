using System.Text.Json;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Detection tables loaded from mac-detection.json (embedded). Data only: patterns, thresholds and
/// vendor prefixes. A file that does not validate is rejected as a whole.
/// </summary>
public sealed class DetectionData
{
    public int Version { get; init; }
    public string Updated { get; init; } = "";
    public int[] SupportedMacOSMajors { get; init; } = Array.Empty<int>();
    public ThresholdSet Thresholds { get; init; } = new();
    public PanicPattern[] PanicPatterns { get; init; } = Array.Empty<PanicPattern>();
    public ShutdownToken[] ShutdownTokens { get; init; } = Array.Empty<ShutdownToken>();
    public KextVendor[] KextVendors { get; init; } = Array.Empty<KextVendor>();

    public sealed class ThresholdSet
    {
        public int RecentDays { get; init; } = 30;
        public int RepeatedCrashes { get; init; } = 3;
        public int BatteryMaxCapacityPct { get; init; } = 80;
        public int BatteryCycles { get; init; } = 1000;
        public int DiskFreeWarnPct { get; init; } = 10;
        public int DiskFreeCriticalPct { get; init; } = 5;
        public int LongUptimeDays { get; init; } = 30;
    }

    public sealed record PanicPattern(string Id, string[] Match, string Title, string Explanation, string Advice);
    public sealed record ShutdownToken(string Token, string Meaning, bool Abnormal);
    public sealed record KextVendor(string Prefix, string Category);

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    static DetectionData? _embedded;

    public static DetectionData Embedded => _embedded ??= LoadEmbedded();

    static DetectionData LoadEmbedded()
    {
        using var s = typeof(DetectionData).Assembly.GetManifestResourceStream("Data.mac-detection.json")
            ?? throw new InvalidOperationException("embedded mac-detection.json missing");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd()) ?? throw new InvalidOperationException("embedded mac-detection.json is invalid");
    }

    /// <summary>Parses and validates; returns null when anything is off.</summary>
    public static DetectionData? Parse(string json)
    {
        DetectionData? d;
        try { d = JsonSerializer.Deserialize<DetectionData>(json, Options); }
        catch (JsonException) { return null; }
        if (d is null || d.Version < 1 || d.SupportedMacOSMajors.Length == 0) return null;
        foreach (var p in d.PanicPatterns)
            if (string.IsNullOrWhiteSpace(p.Id) || p.Match is null || p.Match.Length == 0 || p.Match.Any(string.IsNullOrWhiteSpace)
                || string.IsNullOrWhiteSpace(p.Title)) return null;
        foreach (var t in d.ShutdownTokens)
            if (string.IsNullOrWhiteSpace(t.Token) || string.IsNullOrWhiteSpace(t.Meaning)) return null;
        foreach (var v in d.KextVendors)
            if (string.IsNullOrWhiteSpace(v.Prefix) || v.Prefix.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase)) return null;
        return d;
    }

    public PanicPattern? MatchPanic(string panicText)
    {
        foreach (var p in PanicPatterns)
            if (p.Match.Any(m => panicText.Contains(m, StringComparison.OrdinalIgnoreCase)))
                return p;
        return null;
    }

    public string? VendorCategory(string bundleId) =>
        KextVendors.FirstOrDefault(v => bundleId.StartsWith(v.Prefix, StringComparison.OrdinalIgnoreCase))?.Category;
}
