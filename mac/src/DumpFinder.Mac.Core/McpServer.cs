using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Model Context Protocol server, same protocol layer as Windows (src/Core/McpServer.cs): "DumpFinder --mcp"
/// speaks JSON-RPC over stdio (one JSON object per line; Content-Length framing also read), headless, English.
/// The tools read this Mac (the Windows tools read the Event Log, minidumps and the registry); the fix tools
/// (list / apply with on-screen approval) come with the Mac fixes. Every answer goes through Privacy.Redact.
/// </summary>
public static class McpServer
{
    public const string ServerName = "dumpfinder";
    static readonly string[] Protocols = { "2024-11-05", "2025-03-26", "2025-06-18" };

    /// <summary>Scan provider (days, 0 = full history); the self-test swaps it for a synthetic scan.</summary>
    public static Func<int, (ScanResult Result, StabilitySummary Summary)> Scan { get; set; } = days =>
    {
        var r = DiagnosticsEngine.ScanAsync(maxAgeDays: days > 0 ? Math.Max(days, 30) : 3650).GetAwaiter().GetResult();
        return (r, CrashTimeline.Build(r, DetectionData.Embedded, DateTime.Now));
    };

    public static string ServerVersion => UpdateChecker.CurrentVersionText;

    public static int RunStdio()
    {
        Loc.SetLanguage("en");
        var stdin = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
        var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        while (true)
        {
            string? line;
            try { line = stdin.ReadLine(); } catch (IOException) { break; }
            if (line is null) break;
            line = line.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(line[15..].Trim(), out var len) || len <= 0) continue;
                while (stdin.ReadLine() is { Length: > 0 }) { }   // remaining headers until the blank line
                var buf = new char[len];
                int read = 0;
                while (read < len && stdin.Read(buf, read, len - read) is var n && n > 0) read += n;
                line = new string(buf, 0, read);
            }
            if (Handle(line) is { } response) stdout.WriteLine(response);
        }
        return 0;
    }

    /// <summary>One JSON-RPC message in, the response line out (null for a notification).</summary>
    public static string? Handle(string json)
    {
        JsonObject? req;
        try { req = JsonNode.Parse(json) as JsonObject; }
        catch (JsonException e) { return Error(null, -32700, "Parse error: " + e.Message); }
        if (req is null) return null;
        var id = req["id"];
        if (id is null) return null;   // notification: never answered
        var method = req["method"]?.GetValue<string>() ?? "";
        var prm = req["params"] as JsonObject;
        try
        {
            JsonNode result = method switch
            {
                "initialize" => new JsonObject
                {
                    ["protocolVersion"] = Protocols.Contains(prm?["protocolVersion"]?.GetValue<string>()) ? prm!["protocolVersion"]!.GetValue<string>() : Protocols[0],
                    ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                    ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = ServerVersion },
                },
                "ping" => new JsonObject(),
                "resources/list" => new JsonObject { ["resources"] = new JsonArray() },
                "prompts/list" => new JsonObject { ["prompts"] = new JsonArray() },
                "tools/list" => new JsonObject { ["tools"] = Tools() },
                "tools/call" => CallTool(prm),
                _ => throw new MissingMethodException(method),
            };
            return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result }.ToJsonString();
        }
        catch (MissingMethodException) { return Error(id, -32601, "Method not found: " + method); }
        catch (Exception e) { return Error(id, -32603, "Internal error: " + e.Message); }
    }

    static string Error(JsonNode? id, int code, string message) =>
        new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone(), ["error"] = new JsonObject { ["code"] = code, ["message"] = message } }.ToJsonString();

    // ---------- Tools ----------
    static JsonArray Tools() => new()
    {
        Tool("mac_scan_system",
            "Scan this Mac: kernel panics (with the third-party kernel extensions in their backtrace), unexpected restarts (watchdog, forced restart), app crashes and low-memory kills (Jetsam) from the diagnostic reports, hardware summary, and the stability score with its root-cause diagnosis.",
            new JsonObject { ["days"] = new JsonObject { ["type"] = "integer", ["description"] = "Number of past days to scan (default: 30, use 0 for full history)." } }),
        Tool("mac_check_hardware_and_conflicts",
            "Audit macOS version and support, Apple Silicon chip, unified memory pressure and swap, SSD SMART and free space, battery health, thermal throttling, screens, USB / Thunderbolt devices, SIP, Gatekeeper, third-party kernel and system extensions, apps that crash repeatedly, uptime.",
            new JsonObject()),
        Tool("mac_analyze_panic_report",
            "Decode macOS crash reports: .panic files and .ips reports (kernel panic, app crash, Jetsam). Extracts the panic string, panicked process, kernel extensions in the backtrace, exception type and signal, and a plain-English explanation.",
            new JsonObject { ["file_path"] = new JsonObject { ["type"] = "string", ["description"] = "Optional full path to a .panic or .ips file. Omit to list the kernel panics of the last 90 days." } }),
        Tool("mac_get_crash_detail",
            "Without crash_id: list the crashes of the last 30 days with their crash_id. With crash_id: full detail of one crash - plain explanation, analysis, evidence, suspect kernel extension and the full report text.",
            new JsonObject { ["crash_id"] = new JsonObject { ["type"] = "string", ["description"] = "Identifier returned by the list mode. Omit to list crashes." } }),
        Tool("mac_export_report",
            "Full diagnostic report of this Mac (score, crashes, kernel panics, checks, extensions). format: 'markdown' (default, returned inline), 'json' (returned inline) or 'html' (saved to ~/Documents/DumpFinder/reports, path returned).",
            new JsonObject { ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("markdown", "json", "html") } }),
    };

    static JsonObject Tool(string name, string description, JsonObject properties) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties },
        ["annotations"] = new JsonObject { ["readOnlyHint"] = name != "mac_export_report" },
    };

    static JsonObject CallTool(JsonObject? prm)
    {
        var name = prm?["name"]?.GetValue<string>() ?? "";
        var args = prm?["arguments"] as JsonObject ?? new JsonObject();
        try
        {
            var (text, isError) = name switch
            {
                "mac_scan_system" => (ScanSystem(IntArg(args, "days", 30)), false),
                "mac_check_hardware_and_conflicts" => (CheckHardware(), false),
                "mac_analyze_panic_report" => AnalyzeReport(StrArg(args, "file_path")),
                "mac_get_crash_detail" => (CrashDetail(StrArg(args, "crash_id")), false),
                "mac_export_report" => (ExportReport(StrArg(args, "format")), false),
                _ => ("Unknown tool: " + name, true),
            };
            return ToolResult(Privacy.Redact(text), isError);
        }
        catch (Exception e) { return ToolResult("Tool execution error: " + e.Message, true); }
    }

    static JsonObject ToolResult(string text, bool isError) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = isError,
    };

    static int IntArg(JsonObject a, string key, int fallback)
    {
        var n = a[key];
        if (n is null) return fallback;
        if (n.GetValueKind() == JsonValueKind.Number && n.GetValue<double>() is var d && d >= 0) return (int)d;
        return int.TryParse(n.ToString(), out var i) && i >= 0 ? i : fallback;
    }

    static string StrArg(JsonObject a, string key) => a[key]?.GetValueKind() == JsonValueKind.String ? a[key]!.GetValue<string>().Trim() : "";

    // ---------- Tool bodies ----------
    static string ScanSystem(int days)
    {
        var (r, s) = Scan(days);
        var now = DateTime.Now;
        var crashes = s.Crashes.Where(c => days == 0 || (now - c.Time).TotalDays <= days).ToList();
        var hw = r.Hardware;
        var sb = new StringBuilder();
        sb.AppendLine($"=== DUMP FINDER FOR MAC SYSTEM SCAN ({(days > 0 ? $"Last {days} days" : "Full History")}) ===");
        sb.AppendLine($"Mac: {hw.MachineName} ({hw.ModelId}) | Chip: {hw.Chip} | Memory: {hw.Memory}");
        sb.AppendLine($"macOS: {hw.OsVersion} | Last Boot: {r.Boot.BootTime?.LocalDateTime:yyyy-MM-dd HH:mm} | Shutdown reason: {r.Boot.ShutdownReason}");
        sb.AppendLine();
        sb.AppendLine("[SMART ROOT-CAUSE DIAGNOSIS]");
        sb.AppendLine($"Score: {s.Score}/100 ({s.Level})");
        sb.AppendLine("Title: " + s.VerdictTitle);
        sb.AppendLine("Summary: " + s.VerdictBody);
        sb.AppendLine("Counts => " + string.Join(" | ", crashes.GroupBy(c => c.Category).Select(g => $"{CrashTimeline.CategoryName(g.Key)}: {g.Count()}")));
        var warn = HardwareChecks.Build(r, DetectionData.Embedded, DateTimeOffset.Now).Where(c => c.Level >= HealthLevel.Warning).ToList();
        if (warn.Count > 0)
        {
            sb.AppendLine().AppendLine("[HARDWARE & CONFLICT WARNINGS]");
            foreach (var c in warn) sb.AppendLine($"- [{c.Tag}] {c.Title}: {c.Value} — {c.Detail}");
        }
        var panics = r.Panics.Where(p => days == 0 || p.Time is not { } t || (now - t.LocalDateTime).TotalDays <= days).OrderByDescending(p => p.Time).ToList();
        if (panics.Count > 0)
        {
            sb.AppendLine().AppendLine("[KERNEL PANICS]");
            foreach (var p in panics.Take(8))
                sb.AppendLine($"- [{p.Time?.LocalDateTime:yyyy-MM-dd HH:mm}] {Path.GetFileName(p.File)} | {p.Message} | Process: {p.PanickedProcess} | Extensions: {string.Join(", ", p.BacktraceExtensions)}");
        }
        if (crashes.Count > 0)
        {
            sb.AppendLine().AppendLine($"[RECENT CRASHES (Top {Math.Min(20, crashes.Count)} of {crashes.Count})]");
            foreach (var c in crashes.Take(20)) sb.AppendLine($"- [{c.Time:yyyy-MM-dd HH:mm}] [{c.Category}] {c.Title} | {c.TechLine}");
        }
        return sb.ToString().TrimEnd();
    }

    static string CheckHardware()
    {
        var (r, _) = Scan(30);
        var cards = HardwareChecks.Build(r, DetectionData.Embedded, DateTimeOffset.Now);
        var sb = new StringBuilder();
        sb.AppendLine("=== HARDWARE CHECKS ===");
        sb.AppendLine($"Mac: {r.Hardware.MachineName} | Chip: {r.Hardware.Chip} | macOS: {r.Hardware.OsVersion}");
        foreach (var c in cards.Where(c => c.Tab == HardwareChecks.TabHardware)) Card(sb, c);
        sb.AppendLine().AppendLine("=== SECURITY, EXTENSIONS & CONFLICTS ===");
        foreach (var c in cards.Where(c => c.Tab == HardwareChecks.TabConflicts)) Card(sb, c);
        var ext = HardwareChecks.Extensions(r);
        sb.AppendLine().AppendLine($"=== THIRD-PARTY EXTENSIONS ({ext.Count}) ===");
        foreach (var e in ext)
            sb.AppendLine($"- {(e.Kernel ? "kext" : "system extension")} {e.BundleId} {e.Version} | {(e.Loaded ? "loaded" : "not loaded")} {e.Detail} | in {e.Crashes} panic backtrace(s)");
        return sb.ToString().TrimEnd();

        static void Card(StringBuilder sb, HealthCard c)
        {
            sb.AppendLine($"- [{c.Tag}] ({c.Level}) {c.Title}: {c.Value}");
            sb.AppendLine("  Detail: " + c.Detail.Replace("\n", " "));
            if (c.GuideSteps.Length > 0) sb.AppendLine("  What to do: " + c.GuideSteps.Replace("\n", " "));
        }
    }

    static (string, bool) AnalyzeReport(string path)
    {
        if (path.Length > 0)
        {
            if (path.StartsWith('~')) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..];
            if (!File.Exists(path)) return ("Report file not found: " + path, true);
            var errors = new List<string>();
            var (p, rep) = DiagnosticReports.ParseFile(path, DetectionData.Embedded, errors);
            var fake = new ScanResult();
            if (p is not null) fake.Panics.Add(p);
            if (rep is not null) fake.Reports.Add(rep);
            var entry = CrashTimeline.Build(fake, DetectionData.Embedded, DateTime.Now).Crashes.FirstOrDefault();
            return entry is null ? ("Unable to read this file as a .panic / .ips report: " + path + (errors.Count > 0 ? " (" + errors[0] + ")" : ""), true)
                                 : (Detail(entry), false);
        }
        var (r, _) = Scan(90);
        var sb = new StringBuilder($"Kernel panic reports found: {r.Panics.Count} (last 90 days, /Library/Logs/DiagnosticReports)\n");
        foreach (var p in r.Panics.OrderByDescending(p => p.Time))
        {
            sb.AppendLine($"- [{p.Time?.LocalDateTime:yyyy-MM-dd HH:mm}] {Path.GetFileName(p.File)} | {MacText.PanicTitle(p.PatternId, DetectionData.Embedded)} | Process: {p.PanickedProcess} | Extensions: {string.Join(", ", p.BacktraceExtensions)}");
            if (MacText.PanicExplanation(p.PatternId, DetectionData.Embedded) is { Length: > 0 } ex) sb.AppendLine("  Analysis: " + ex);
        }
        return (sb.ToString().TrimEnd(), false);
    }

    /// <summary>First 5 bytes of SHA-1 of the crash key, as on Windows (10 hex characters).</summary>
    public static string CrashId(CrashEntry c) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(c.Key)), 0, 5).ToLowerInvariant();

    static string CrashDetail(string crashId)
    {
        var (_, s) = Scan(30);
        var recent = s.Crashes.Where(c => (DateTime.Now - c.Time).TotalDays <= 30).ToList();
        if (crashId.Length == 0)
        {
            var sb = new StringBuilder($"=== CRASHES, LAST 30 DAYS ({recent.Count}) ===\n");
            foreach (var c in recent.Take(60))
                sb.AppendLine($"- crash_id: {CrashId(c)} | {c.Time:yyyy-MM-dd HH:mm} | {c.Category} | {c.Title} | {c.TechLine}{(c.Panic is not null ? " | panic report" : "")}");
            return sb.AppendLine().Append("Call again with crash_id for the full detail of one crash.").ToString();
        }
        var hit = recent.FirstOrDefault(c => CrashId(c) == crashId.ToLowerInvariant());
        return hit is null ? $"Unknown crash_id '{crashId}'. Call without crash_id to list crashes." : Detail(hit);
    }

    static string Detail(CrashEntry c)
    {
        var sb = new StringBuilder($"=== {c.Title} ===\n");
        sb.AppendLine($"When: {c.Time:yyyy-MM-dd HH:mm}");
        sb.AppendLine("Category: " + CrashTimeline.CategoryName(c.Category));
        sb.AppendLine("Plain explanation: " + CrashDetails.Plain(c, DetectionData.Embedded));
        foreach (var kv in CrashDetails.Analysis(c)) sb.AppendLine($"{kv.Key}: {kv.Value}");
        if (CrashDetails.Evidence(c) is { Length: > 0 } ev) sb.AppendLine("Evidence: " + ev);
        if (CrashDetails.SuspectExtension(c) is { Length: > 0 } x) sb.AppendLine("Suspect extension: " + x);
        var full = CrashDetails.FullText(c);
        if (full.Length > 0) sb.AppendLine().AppendLine("--- Report ---").AppendLine(full.Length > 6000 ? full[..6000] + "\n…" : full);
        return sb.ToString().TrimEnd();
    }

    static string ExportReport(string format)
    {
        var (r, s) = Scan(90);
        switch (format.ToLowerInvariant())
        {
            case "json": return ReportBuilder.Json(r, s);
            case "html":
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DumpFinder", "reports");
                Directory.CreateDirectory(dir);
                var path = Path.Combine(dir, $"DumpFinder_{DateTime.Now:yyyyMMdd_HHmm}.html");
                File.WriteAllText(path, ReportBuilder.Html(r, s), new UTF8Encoding(false));
                return "HTML report saved: " + path;
            default: return ReportBuilder.Markdown(r, s);
        }
    }

    // ---------- Client registration (Settings › Integrations) ----------
    /// <summary>The binary to launch: inside the bundle, "Dump Finder.app/Contents/MacOS/DumpFinder".</summary>
    public static string ExePath => Environment.ProcessPath ?? "DumpFinder";

    static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static string ClaudeDesktopConfigPath => Path.Combine(Home, "Library", "Application Support", "Claude", "claude_desktop_config.json");
    /// <summary>Same place under the home folder as on Windows (%USERPROFILE%\.gemini\config\mcp_config.json).</summary>
    public static string AntigravityConfigPath => Path.Combine(Home, ".gemini", "config", "mcp_config.json");

    public static string ClaudeCodeCommand => $"claude mcp add {ServerName} -- \"{ExePath}\" --mcp";
    public static string GeminiCliCommand => $"gemini mcp add {ServerName} \"{ExePath}\" --mcp";

    public static bool IsRegisteredInClaudeDesktop() => HasServer(ClaudeDesktopConfigPath);
    public static bool IsRegisteredInAntigravity() => HasServer(AntigravityConfigPath);

    static bool HasServer(string path)
    {
        try
        {
            if (!File.Exists(path)) return false;
            return JsonNode.Parse(File.ReadAllText(path))?["mcpServers"] is JsonObject servers && servers.ContainsKey(ServerName);
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return false; }
    }

    public static bool RegisterInClaudeDesktop(out string error) => Register(ClaudeDesktopConfigPath, out error);

    public static bool RegisterInAntigravity(out string error)
    {
        if (!Register(AntigravityConfigPath, out error)) return false;
        var ide = Path.Combine(Home, ".gemini", "antigravity-ide");
        if (Directory.Exists(ide)) Register(Path.Combine(ide, "mcp_config.json"), out _);
        return true;
    }

    /// <summary>
    /// A downloaded app opened in place runs from a temporary, randomized copy (App Translocation):
    /// a client configured with that path would break at the next launch.
    /// </summary>
    public static bool IsTranslocated(string exe) => exe.Contains("/AppTranslocation/", StringComparison.Ordinal);

    static bool Register(string path, out string error)
    {
        if (IsTranslocated(ExePath)) { error = Loc.S("mac.mcp.move_to_applications"); return false; }
        try
        {
            MergeServerIntoConfig(path, ExePath);
            error = "";
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error = e.Message;
            return false;
        }
    }

    /// <summary>
    /// Adds "dumpfinder": { command, args: ["--mcp"] } under mcpServers and keeps everything else. A file that is
    /// not standard JSON (comments, trailing comma) is left untouched; otherwise a .bak copy is kept, then the
    /// new file replaces it atomically.
    /// </summary>
    public static void MergeServerIntoConfig(string path, string exe)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        JsonObject root = new();
        if (File.Exists(path) && File.ReadAllText(path) is { } text && text.Trim().Length > 0)
        {
            try { root = JsonNode.Parse(text) as JsonObject ?? throw new JsonException("not an object"); }
            catch (JsonException) { throw new InvalidDataException(Loc.F("mcp_server.this_configuration_file_is_not_standard", path)); }
            File.Copy(path, path + ".bak", overwrite: true);
        }
        if (root["mcpServers"] is not JsonObject servers)
        {
            servers = new JsonObject();
            root["mcpServers"] = servers;
        }
        servers[ServerName] = new JsonObject { ["command"] = exe, ["args"] = new JsonArray("--mcp") };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }
}
