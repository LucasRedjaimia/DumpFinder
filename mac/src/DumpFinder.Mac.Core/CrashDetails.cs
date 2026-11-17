using System.Globalization;
using System.Text;
using WinCrashDoctor;

namespace DumpFinder.Mac.Core;

public sealed record KeyValue(string Key, string Value, bool Highlight = false);

/// <summary>
/// Content of the crash detail panel (Windows: CrashCatalog.PlainExplanation, the dump analysis grid,
/// the evidence line, the full message and ForumMarkdown), for Mac panics and .ips reports.
/// </summary>
public static class CrashDetails
{
    /// <summary>"In plain words": what happened, without jargon.</summary>
    public static string Plain(CrashEntry c, DetectionData data)
    {
        switch (c.Category)
        {
            case CrashCategory.BSOD:
                var p = c.Panic!;
                var text = MacText.PanicExplanation(p.PatternId, data);
                if (text.Length == 0) text = Loc.S("mac.plain.panic");
                return SuspectExtension(c) is { } kext ? text + " " + Loc.F("mac.plain.panic_kext", kext) : text;
            case CrashCategory.UnexpectedReboot:
                return Loc.S("mac.plain.restart");
            case CrashCategory.MemoryExhaustion:
                return Loc.S("mac.plain.jetsam");
            default:
                return AppExplanation(c.Report);
        }
    }

    static string AppExplanation(CrashReport? r)
    {
        if (r is null) return "";
        string key = r.Termination.Contains("SIGTERM_TIMEOUT", StringComparison.OrdinalIgnoreCase) ? "sigterm_timeout"
            : r.ExceptionType switch
            {
                "EXC_BAD_ACCESS" => "bad_access",
                "EXC_CRASH" when r.Signal == "SIGABRT" => "abort",
                "EXC_CRASH" when r.Signal == "SIGKILL" => "kill",
                "EXC_BREAKPOINT" => "breakpoint",
                "EXC_BAD_INSTRUCTION" => "bad_instruction",
                "EXC_RESOURCE" => "resource",
                "EXC_GUARD" => "guard",
                _ => "other",
            };
        return Loc.S("mac.plain.exc." + key);
    }

    /// <summary>The non-Apple kernel extension blamed for a panic (first in the backtrace), if any.</summary>
    public static string? SuspectExtension(CrashEntry c) =>
        c.Panic?.BacktraceExtensions.FirstOrDefault(id => !id.StartsWith("com.apple.", StringComparison.OrdinalIgnoreCase));

    /// <summary>Why that extension is blamed (Windows: the culprit evidence line).</summary>
    public static string Evidence(CrashEntry c) =>
        SuspectExtension(c) is { } kext ? Loc.F("mac.evidence.kext", kext) : "";

    /// <summary>Key / value grid under ANALYSIS.</summary>
    public static List<KeyValue> Analysis(CrashEntry c)
    {
        var kv = new List<KeyValue>();
        void Add(string key, string? value, bool hl = false) { if (!string.IsNullOrWhiteSpace(value)) kv.Add(new KeyValue(key, value, hl)); }

        if (c.Panic is { } p)
        {
            Add(Loc.S("mac.analysis.extension"), SuspectExtension(c), true);
            Add(Loc.S("mac.analysis.message"), p.Message);
            Add(Plain0(Loc.S("crashes.process")), p.PanickedProcess);
            if (p.BacktraceExtensions.Count > 0) Add(Loc.S("mac.analysis.backtrace"), string.Join(" → ", p.BacktraceExtensions));
            Add(Loc.S("mac.analysis.last_kext"), p.LastStartedExtension);
            Add("macOS", p.OsVersion);
            Add(Plain0(Loc.S("crashes.file")), Path.GetFileName(p.File) + (p.FileSize > 0 ? "  (" + Size(p.FileSize) + ")" : ""));
        }
        else if (c.Report is { } r)
        {
            Add(Plain0(Loc.S("crashes.process")), r.Process + (r.AppVersion.Length > 0 ? " " + r.AppVersion : ""));
            Add(Loc.S("mac.analysis.bundle"), r.BundleId);
            Add(Plain0(Loc.S("crashes.code")), string.Join(" · ", new[] { r.ExceptionType, r.Signal }.Where(s => s.Length > 0)), true);
            Add(Loc.S("mac.analysis.termination"), r.Termination);
            Add(Loc.S("mac.analysis.parent"), r.ParentProcess);
            Add(Loc.S("mac.analysis.path"), Privacy.Redact(r.ProcPath));
            Add("macOS", r.OsVersion);
            Add(Plain0(Loc.S("crashes.file")), Path.GetFileName(r.File) + (r.FileSize > 0 ? "  (" + Size(r.FileSize) + ")" : ""));
        }
        return kv;
    }

    /// <summary>"Full report": the panic string, or the beginning of the .ips file.</summary>
    public static string FullText(CrashEntry c)
    {
        if (c.Panic is { } p) return p.PanicText;
        if (c.Report is { } r)
        {
            try
            {
                using var reader = new StreamReader(r.File);
                var buf = new char[12000];
                int n = reader.ReadBlock(buf, 0, buf.Length);
                return new string(buf, 0, n) + (reader.EndOfStream ? "" : "\n…");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ""; }
        }
        return "";
    }

    /// <summary>Markdown summary for a forum post (English like the Windows version, personal details removed).</summary>
    public static string ForumMarkdown(CrashEntry c, ScanResult? res)
    {
        var sb = new StringBuilder();
        sb.AppendLine("**" + c.Title + "**");
        sb.AppendLine();
        sb.AppendLine("- When: " + c.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (c.Panic is { } p)
        {
            sb.AppendLine("- Kernel panic: `" + p.Message + "`");
            if (p.PanickedProcess.Length > 0) sb.AppendLine("- Panicked task: `" + p.PanickedProcess + "`");
            if (p.BacktraceExtensions.Count > 0) sb.AppendLine("- Extensions in backtrace: `" + string.Join(" -> ", p.BacktraceExtensions) + "`");
            if (p.LastStartedExtension.Length > 0) sb.AppendLine("- Last started kext: `" + p.LastStartedExtension + "`");
        }
        else if (c.Report is { } r)
        {
            sb.AppendLine("- Process: `" + r.Process + (r.AppVersion.Length > 0 ? " " + r.AppVersion : "") + "`");
            if (r.ExceptionType.Length > 0) sb.AppendLine("- Exception: `" + string.Join(" / ", new[] { r.ExceptionType, r.Signal }.Where(s => s.Length > 0)) + "`");
            if (r.Termination.Length > 0) sb.AppendLine("- Termination: `" + r.Termination + "`");
        }
        else if (c.TechLine.Length > 0) sb.AppendLine("- Details: `" + c.TechLine + "`");
        if (res is not null)
        {
            var hw = res.Hardware;
            sb.AppendLine();
            sb.AppendLine("**System**");
            sb.AppendLine("- Mac: " + hw.MachineName + " (" + hw.ModelId + "), " + hw.Chip + ", " + hw.Memory);
            sb.AppendLine("- OS: " + hw.OsVersion);
            var third = res.KernelExtensions.Where(k => k.Loaded).Select(k => k.BundleId).ToList();
            if (third.Count > 0) sb.AppendLine("- Third-party kexts loaded: `" + string.Join(", ", third) + "`");
            if (c.Report is { } rr)
            {
                int same = res.Reports.Count(x => x.Process == rr.Process && x.Kind == rr.Kind);
                if (same > 1) sb.AppendLine("- Same crash seen " + same + " times in the scanned period");
            }
        }
        sb.AppendLine();
        sb.AppendLine("_Generated by Dump Finder for Mac_");
        return Privacy.Redact(sb.ToString());
    }

    public static string Size(long bytes) =>
        bytes >= 1 << 20 ? (bytes / 1048576.0).ToString("0.#", Loc.Culture) + " MB"
        : bytes >= 1 << 10 ? (bytes / 1024.0).ToString("0", Loc.Culture) + " KB"
        : bytes + " B";

    /// <summary>Windows labels may end with a colon or space: "Process: " → "Process".</summary>
    static string Plain0(string s) => Loc.Plain(s).Trim().TrimEnd(':').Trim();
}
