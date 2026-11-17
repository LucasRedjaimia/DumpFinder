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
        Tool("mac_list_fixes",
            "List the Dump Finder fixes for this Mac (browser and Discord GPU acceleration, Power Nap, automatic security updates, First Aid) with their live state: Active, Recommended (a crash or panic of the last 30 days points at it), Optional or Tool.",
            new JsonObject()),
        Tool("mac_apply_fix",
            "Apply or restore a Dump Finder fix by fix_id (see mac_list_fixes). SAFE BY DEFAULT: without confirm=true it only describes what would change (dry run). With confirm=true the user is asked on screen to approve; nothing changes unless they click Allow, and settings that need administrator rights also ask for the macOS password. Every applied change is recorded with its previous value and can be undone from Dump Finder (Tools > History).",
            new JsonObject
            {
                ["fix_id"] = new JsonObject { ["type"] = "string", ["description"] = "Identifier of the fix from mac_list_fixes." },
                ["restore"] = new JsonObject { ["type"] = "boolean", ["description"] = "Set true to restore the macOS default; false (default) to apply the fix." },
                ["confirm"] = new JsonObject { ["type"] = "boolean", ["description"] = "false (default) = dry run, nothing is changed. true = request the change; the user must approve it on screen. Only set true after the user agreed in the conversation." },
            }, required: "fix_id"),
        Tool("mac_export_report",
            "Full diagnostic report of this Mac (score, crashes, kernel panics, checks, extensions). format: 'markdown' (default, returned inline), 'json' (returned inline) or 'html' (saved to ~/Documents/DumpFinder/reports, path returned).",
            new JsonObject { ["format"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("markdown", "json", "html") } }),
    };

    static JsonObject Tool(string name, string description, JsonObject properties, string? required = null)
    {
        var schema = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required is not null) schema["required"] = new JsonArray(required);
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = schema,
            ["annotations"] = new JsonObject { ["readOnlyHint"] = name is not ("mac_export_report" or "mac_apply_fix"), ["destructiveHint"] = false },
        };
    }

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
                "mac_list_fixes" => (ListFixes(), false),
                "mac_apply_fix" => ApplyFix(StrArg(args, "fix_id"), BoolArg(args, "restore"), BoolArg(args, "confirm")),
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

    static bool BoolArg(JsonObject a, string key) => a[key] is { } n && (n.GetValueKind() == JsonValueKind.True || n.ToString().Equals("true", StringComparison.OrdinalIgnoreCase));

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

    // ---------- Fixes ----------
    static List<FixItem> Fixes()
    {
        var (r, s) = Scan(30);
        return FixCatalog.Inspect(r, s);
    }

    static string ListFixes()
    {
        var sb = new StringBuilder("=== AVAILABLE FIXES ===\n");
        foreach (var f in Fixes())
        {
            sb.AppendLine($"- fix_id: \"{f.Id}\" | [{f.Tag}] Status: {f.Status} | Current: {f.CurrentValue}{(f.Admin ? " | needs the administrator password" : "")}");
            sb.AppendLine("  Title: " + f.Title);
            sb.AppendLine("  Target: " + f.Target);
            sb.AppendLine("  Explanation: " + f.Explanation);
        }
        return sb.ToString().TrimEnd();
    }

    /// <summary>Is this app open? (test hook: the self-test does not depend on what runs on the Mac)</summary>
    public static Func<string, bool> AppRunning { get; set; } = app => Proc.Run("/usr/bin/pgrep", new[] { "-x", app }, 3000).ExitCode == 0;

    /// <summary>Test hook: the self-test answers the on-screen approval.</summary>
    public static Func<string, bool>? Approve { get; set; }

    /// <summary>Windows ApplyFixGuarded: dry run by default; with confirm, an on-screen Allow / Don't Allow (default: Don't Allow).</summary>
    static (string, bool) ApplyFix(string id, bool restore, bool confirm)
    {
        if (id.Length == 0) return ("Missing required parameter 'fix_id'.", true);
        var fix = Fixes().FirstOrDefault(f => f.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        if (fix is null) return ($"Unknown fix_id '{id}'. Call mac_list_fixes first.", true);
        if (fix.IsTool) return ($"[REFUSED] '{fix.Id}' is a tool that opens Disk Utility, not a setting. Ask the user to run it from Dump Finder (Fixes page).", true);
        if (fix.Status == FixStatus.Info) return ($"[REFUSED] {fix.Title}: {fix.CurrentValue}.", true);
        if (restore && !fix.CanRestore) return ($"Nothing to restore for '{fix.Id}': {fix.CurrentValue}.", true);
        var action = (restore ? "Restore the macOS default: " : "Apply: ") + fix.Title;
        if (!confirm)
            return ("[DRY RUN] Nothing was changed.\n" + action + "\nTarget: " + fix.Target + "\nCurrent value: " + fix.CurrentValue +
                    "\nNeeds the administrator password: " + fix.Admin +
                    "\nTo proceed, ask the user, then call again with confirm=true. They will also have to approve it on screen.", false);
        if (fix.Process.Length > 0 && AppRunning(fix.Process))
            return ($"[REFUSED] {fix.Process} is open: it rewrites its settings when it quits. Ask the user to quit {fix.Process}, then call again.", true);

        // The approval box speaks the user's language (the MCP answers stay in English).
        var lang = Loc.Current;
        var saved = AppSettings.Load().Language;
        if (saved.Length > 0) Loc.SetLanguage(saved);
        var text = Loc.S("engine.mcp_server.an_ai_agent_connected_through_mcp") + (restore ? Loc.S("main_window.reset_to_default") : "") + fix.Title +
                   "\n\n" + fix.Target + "\n" + Loc.S("engine.mcp_server.current_value") + fix.CurrentValue + "\n\n" + Loc.S("engine.mcp_server.the_current_value_is_saved_you");
        var allow = Loc.S("mac.mcp.allow");
        var deny = Loc.S("mac.mcp.deny");
        Loc.SetLanguage(lang);
        bool approved = Approve?.Invoke(text) ?? AskOnScreen(text, allow, deny);
        if (!approved) return ("[DECLINED] The user refused this change on screen. Nothing was modified.", true);

        var op = fix.Id + (restore ? "_restore" : "_apply");
        var res = FixCatalog.RunAndRecord(new[] { op }, _ => (restore ? "↺ " : "") + fix.Title + " (MCP)")[0];
        return ((res.Ok ? "[SUCCESS] " : "[FAILED] ") + res.Message +
                (res.Ok && res.Changes.Count > 0 ? $"\nRecorded in the change journal ({res.Changes.Count} value(s)); undoable from Dump Finder." : ""), !res.Ok);
    }

    /// <summary>A native dialog from this headless process; the default button is the refusal; gives up after 2 minutes.</summary>
    static bool AskOnScreen(string text, string allow, string deny)
    {
        var script = $"display dialog \"{Admin.AppleScriptEscape(text)}\" with title \"Dump Finder — MCP\" buttons {{\"{Admin.AppleScriptEscape(deny)}\", \"{Admin.AppleScriptEscape(allow)}\"}} " +
                     $"default button \"{Admin.AppleScriptEscape(deny)}\" cancel button \"{Admin.AppleScriptEscape(deny)}\" with icon caution giving up after 120";
        var r = Proc.Run("/usr/bin/osascript", new[] { "-e", script }, 130000);
        return r.ExitCode == 0 && r.Stdout.Contains("button returned:" + allow, StringComparison.Ordinal) && !r.Stdout.Contains("gave up:true", StringComparison.Ordinal);
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
