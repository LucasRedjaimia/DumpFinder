namespace DumpFinder.Mac.Core;

/// <summary>One "Common crashes" article (Windows: KnowledgeEntry): tag, title, symptoms, cause, fix.</summary>
public sealed class KnowledgeEntry
{
    public required string Id { get; init; }
    public required string Tag { get; init; }
    public required string BadgeColorHex { get; init; }
    /// <summary>When the article applies only to some Macs ("Filter to my Mac"); null = every Mac.</summary>
    public Func<ScanResult, bool>? AppliesTo { get; init; }

    public string Title => Loc.S("mac.kb." + Id + ".title");
    public string Symptoms => Loc.S("mac.kb." + Id + ".symptoms");
    public string Cause => Loc.S("mac.kb." + Id + ".cause");
    public string Solution => Loc.S("mac.kb." + Id + ".fix");

    public bool IsApplicableTo(ScanResult? r) => r is null || AppliesTo is null || AppliesTo(r);
}

/// <summary>
/// Known Mac crash causes (Windows: KnowledgeBase.cs, 16 PC articles). Texts in Locales (mac.kb.*):
/// general, durable advice only (no version-specific claims), so the list stays true across macOS releases.
/// </summary>
public static class KnowledgeBase
{
    public static IReadOnlyList<KnowledgeEntry> Entries { get; } = new List<KnowledgeEntry>
    {
        new() { Id = "watchdog", Tag = "WINDOWSERVER • WATCHDOG", BadgeColorHex = "#EF4444" },
        new() { Id = "sleep_wake", Tag = "SLEEP / WAKE", BadgeColorHex = "#F43F5E" },
        new() { Id = "kexts", Tag = "KERNEL EXTENSIONS", BadgeColorHex = "#8B5CF6",
                AppliesTo = r => r.KernelExtensions.Count > 0 || r.Panics.Any(p => p.BacktraceExtensions.Any(id => !id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase))) },
        new() { Id = "docks", Tag = "USB-C • THUNDERBOLT • DOCKS", BadgeColorHex = "#0EA5E9" },
        new() { Id = "low_disk", Tag = "STORAGE • FREE SPACE", BadgeColorHex = "#F59E0B" },
        new() { Id = "memory", Tag = "MEMORY • JETSAM", BadgeColorHex = "#F97316" },
        new() { Id = "apps_update", Tag = "APPS • MACOS UPDATE", BadgeColorHex = "#6366F1" },
        new() { Id = "gpu", Tag = "GPU • DISPLAYS", BadgeColorHex = "#10B981" },
        new() { Id = "ssd", Tag = "SSD • DISK UTILITY", BadgeColorHex = "#14B8A6" },
        new() { Id = "heat", Tag = "HEAT", BadgeColorHex = "#EF4444" },
        new() { Id = "battery", Tag = "BATTERY • MACBOOK", BadgeColorHex = "#22C55E", AppliesTo = r => r.Hardware.HasBattery },
        new() { Id = "safe_mode", Tag = "SAFE MODE • DIAGNOSTICS", BadgeColorHex = "#64748B" },
        new() { Id = "panic_reports", Tag = "PANIC REPORTS", BadgeColorHex = "#A855F7" },
    };
}
