using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    public static class McpServer
    {
        public const string ServerName = "dumpfinder";
        public const string ServerVersion = "6.0.0";

        public static string GetExePath()
        {
            return Assembly.GetExecutingAssembly().Location;
        }

        public static string GetAntigravityConfigPath()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".gemini", "config", "mcp_config.json");
        }

        public static string GetClaudeCodeCommand()
        {
            return string.Format("claude mcp add {0} -- \"{1}\" --mcp", ServerName, GetExePath());
        }

        public static string GetGeminiCliCommand()
        {
            return string.Format("gemini mcp add {0} \"{1}\" --mcp", ServerName, GetExePath());
        }

        public static bool IsRegisteredInAntigravity()
        {
            try
            {
                string path = GetAntigravityConfigPath();
                if (!File.Exists(path)) return false;
                string raw = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(raw)) return false;

                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var root = ser.Deserialize<Dictionary<string, object>>(raw);
                if (root == null || !root.ContainsKey("mcpServers")) return false;
                var servers = root["mcpServers"] as IDictionary<string, object>;
                return servers != null && (servers.ContainsKey(ServerName) || servers.ContainsKey("wincrashdoctor"));
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        public static bool RegisterInAntigravity(out string configPath, out string errorMessage)
        {
            configPath = GetAntigravityConfigPath();
            errorMessage = null;
            try
            {
                string exePath = GetExePath();
                MergeServerIntoConfigFile(configPath, exePath);

                // Also sync legacy/IDE path if parent directory exists on disk
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string ideDir = Path.Combine(home, ".gemini", "antigravity-ide");
                if (Directory.Exists(ideDir))
                {
                    try { MergeServerIntoConfigFile(Path.Combine(ideDir, "mcp_config.json"), exePath); } catch { }
                }

                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static void MergeServerIntoConfigFile(string filePath, string exePath)
        {
            string dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Dictionary<string, object> root = null;

            if (File.Exists(filePath))
            {
                string existing = File.ReadAllText(filePath, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(existing))
                {
                    try { root = ser.Deserialize<Dictionary<string, object>>(existing); }
                    catch (Exception ex) { Log.Warn(ex); root = null; }
                }
            }

            if (root == null)
                root = new Dictionary<string, object>();

            Dictionary<string, object> servers = null;
            if (root.ContainsKey("mcpServers") && root["mcpServers"] is Dictionary<string, object>)
            {
                servers = (Dictionary<string, object>)root["mcpServers"];
            }
            else
            {
                servers = new Dictionary<string, object>();
                root["mcpServers"] = servers;
            }

            var entry = new Dictionary<string, object>
            {
                { "command", exePath },
                { "args", new[] { "--mcp" } }
            };
            servers[ServerName] = entry;

            string compact = ser.Serialize(root);
            string pretty = PrettyPrintJson(compact);
            File.WriteAllText(filePath, pretty + Environment.NewLine, new UTF8Encoding(false));
        }

        // ==================== STDIO JSON-RPC 2.0 LOOP ====================
        public static int RunStdioLoop()
        {
            // Default to English descriptions inside MCP unless user configured FR, keep deterministic UTF-8
            Localizer.Current = AppLang.EN;
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

            using (Stream stdin = Console.OpenStandardInput())
            using (Stream stdout = Console.OpenStandardOutput())
            using (StreamReader reader = new StreamReader(stdin, new UTF8Encoding(false)))
            using (StreamWriter writer = new StreamWriter(stdout, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" })
            {
                while (true)
                {
                    string line;
                    try
                    {
                        line = reader.ReadLine();
                    }
                    catch (Exception ex) { Log.Warn(ex); break; }

                    if (line == null)
                        break; // EOF

                    string trimmed = line.Trim();
                    if (trimmed.Length == 0)
                        continue;

                    // Optional Content-Length header compatibility if any client sends LSP-style framing
                    if (trimmed.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        int contentLen = 0;
                        int.TryParse(trimmed.Substring("Content-Length:".Length).Trim(), out contentLen);
                        string hdrLine;
                        while ((hdrLine = reader.ReadLine()) != null && hdrLine.Trim().Length > 0) { }
                        if (contentLen > 0)
                        {
                            char[] buf = new char[contentLen];
                            int readTotal = 0;
                            while (readTotal < contentLen)
                            {
                                int n = reader.Read(buf, readTotal, contentLen - readTotal);
                                if (n <= 0) break;
                                readTotal += n;
                            }
                            trimmed = new string(buf, 0, readTotal).Trim();
                        }
                        else continue;
                    }

                    Dictionary<string, object> req;
                    try
                    {
                        req = ser.Deserialize<Dictionary<string, object>>(trimmed);
                    }
                    catch (Exception parseEx)
                    {
                        WriteError(writer, ser, null, -32700, "Parse error: " + parseEx.Message);
                        continue;
                    }

                    if (req == null)
                        continue;

                    bool hasId = req.ContainsKey("id") && req["id"] != null;
                    object reqId = hasId ? req["id"] : null;
                    string method = req.ContainsKey("method") && req["method"] != null ? req["method"].ToString() : "";
                    var reqParams = req.ContainsKey("params") ? req["params"] as Dictionary<string, object> : null;

                    // JSON-RPC notifications (no id) must not receive a response
                    if (!hasId)
                        continue;

                    try
                    {
                        object resultObj = DispatchMethod(method, reqParams);
                        if (resultObj == UnknownMethodSentinel)
                        {
                            WriteError(writer, ser, reqId, -32601, "Method not found: " + method);
                        }
                        else
                        {
                            var resp = new Dictionary<string, object>
                            {
                                { "jsonrpc", "2.0" },
                                { "id", reqId },
                                { "result", resultObj }
                            };
                            writer.WriteLine(ser.Serialize(resp));
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteError(writer, ser, reqId, -32603, "Internal error: " + ex.Message);
                    }
                }
            }

            return 0;
        }

        private static readonly object UnknownMethodSentinel = new object();

        private static object DispatchMethod(string method, Dictionary<string, object> reqParams)
        {
            switch (method)
            {
                case "initialize":
                {
                    string clientProto = "2024-11-05";
                    if (reqParams != null && reqParams.ContainsKey("protocolVersion") && reqParams["protocolVersion"] != null)
                    {
                        string requested = reqParams["protocolVersion"].ToString();
                        if (requested == "2024-11-05" || requested == "2025-03-26" || requested == "2025-06-18")
                            clientProto = requested;
                    }

                    return new Dictionary<string, object>
                    {
                        { "protocolVersion", clientProto },
                        { "capabilities", new Dictionary<string, object>
                            {
                                { "tools", new Dictionary<string, object>() }
                            }
                        },
                        { "serverInfo", new Dictionary<string, object>
                            {
                                { "name", ServerName },
                                { "version", ServerVersion }
                            }
                        }
                    };
                }

                case "ping":
                    return new Dictionary<string, object>();

                case "resources/list":
                    return new Dictionary<string, object> { { "resources", new object[0] } };

                case "prompts/list":
                    return new Dictionary<string, object> { { "prompts", new object[0] } };

                case "tools/list":
                    return new Dictionary<string, object> { { "tools", BuildToolsCatalog() } };

                case "tools/call":
                    return HandleToolCall(reqParams);

                default:
                    return UnknownMethodSentinel;
            }
        }

        private static object[] BuildToolsCatalog()
        {
            return new object[]
            {
                new Dictionary<string, object>
                {
                    { "name", "wincrash_scan_system" },
                    { "description", "Scan Windows crash logs (BSOD, LiveKernelEvent 141/117/193 GPU TDR, WHEA hardware errors, stornvme SSD resets, Realtek NDIS 10400 resets, TPM timeouts, browser/app crashes), hardware summary, and smart root-cause diagnosis." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "days", new Dictionary<string, object>
                                        {
                                            { "type", "integer" },
                                            { "description", "Number of past days to scan (default: 7, use 0 for full history)." }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "wincrash_check_hardware_and_conflicts" },
                    { "description", "Audit BIOS age, Intel 13th/14th Gen microcode, DDR4/DDR5 RAM XMP/EXPO speed, Resizable BAR (critical on Intel Arc), GPU PCIe link width (x16 vs degraded x8/x1), SSD SMART health, overlays, Ring-0 Anti-Cheat vs Kernel Shadow Stacks, and Realtek 2.5GbE / MediaTek Wi-Fi power-saving risks." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>() }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "wincrash_analyze_minidumps" },
                    { "description", "Decode Windows BSOD minidumps (PAGEDU64) and user/LiveKernelReport dumps (MDMP) to extract BugCheck / Exception codes, offending .sys/.dll driver modules, and plain-English root-cause explanations." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "file_path", new Dictionary<string, object>
                                        {
                                            { "type", "string" },
                                            { "description", "Optional full path to a specific .dmp file. Omit to scan all accessible system and WER crash dumps." }
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "wincrash_list_fixes" },
                    { "description", "List all 1-click stability fixes in Dump Finder (DWM MPO fix, DWM Alt-Tab OverlayMinFPS, TDR 10s, Chrome/Edge Skia Graphite, PCIe ASPM off, Realtek 2.5GbE EEE/Green Ethernet off, Fast Startup off, Windows Update GPU driver lock, Shader Cache purge, SFC/DISM) with live Registry values." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>() }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "wincrash_apply_fix" },
                    { "description", "Apply or restore a Dump Finder stability fix by fix_id (see wincrash_list_fixes). SAFE BY DEFAULT: without confirm=true it only describes what would change (dry run). With confirm=true the user is asked on screen to approve; nothing changes unless they click Yes. Every applied change is recorded with its exact previous value and can be undone from Dump Finder (Tools > History)." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "fix_id", new Dictionary<string, object>
                                        {
                                            { "type", "string" },
                                            { "description", "Identifier of the fix from wincrash_list_fixes." }
                                        }
                                    },
                                    { "restore", new Dictionary<string, object>
                                        {
                                            { "type", "boolean" },
                                            { "description", "Set true to revert/restore the fix to Windows default; false (default) to apply the fix." }
                                        }
                                    },
                                    { "confirm", new Dictionary<string, object>
                                        {
                                            { "type", "boolean" },
                                            { "description", "false (default) = dry run, nothing is changed. true = request the change; the user must approve it on screen. Only set true after the user agreed in the conversation." }
                                        }
                                    }
                                }
                            },
                            { "required", new[] { "fix_id" } }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "wincrash_get_crash_detail" },
                    { "description", "Without crash_id: list the crashes of the last 30 days with their crash_id. With crash_id: full detail of one crash - plain explanation, stop code, faulting driver and the evidence for it, drivers on the stack, WinDbg !analyze result when available, installed driver version, and related fix_ids." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "crash_id", new Dictionary<string, object> { { "type", "string" }, { "description", "Identifier returned by the list mode. Omit to list crashes." } } }
                                }
                            }
                        }
                    }
                },
                new Dictionary<string, object>
                {
                    { "name", "wincrash_export_report" },
                    { "description", "Full diagnostic report of this PC (score, crashes, decoded blue screens, protections, drivers, change journal). format: 'markdown' (default, returned inline), 'json' (returned inline) or 'html' (saved to Documents\\DumpFinder\\reports, path returned)." },
                    { "inputSchema", new Dictionary<string, object>
                        {
                            { "type", "object" },
                            { "properties", new Dictionary<string, object>
                                {
                                    { "format", new Dictionary<string, object> { { "type", "string" }, { "enum", new[] { "markdown", "json", "html" } } } }
                                }
                            }
                        }
                    }
                }
            };
        }

        private static object HandleToolCall(Dictionary<string, object> reqParams)
        {
            string toolName = (reqParams != null && reqParams.ContainsKey("name") && reqParams["name"] != null)
                ? reqParams["name"].ToString()
                : "";
            var args = (reqParams != null && reqParams.ContainsKey("arguments"))
                ? reqParams["arguments"] as Dictionary<string, object>
                : null;

            try
            {
                switch (toolName)
                {
                    case "wincrash_scan_system":
                    {
                        int days = 7;
                        if (args != null && args.ContainsKey("days") && args["days"] != null)
                            int.TryParse(args["days"].ToString(), out days);

                        DateTime? start = days > 0 ? (DateTime?)DateTime.Now.AddDays(-days) : null;
                        ScanResult res = DiagnosticsEngine.RunScan(start);
                        return MakeToolTextResult(FormatScanSummaryText(res, days), false);
                    }

                    case "wincrash_check_hardware_and_conflicts":
                    {
                        HardwareSummary hw = DiagnosticsEngine.GetHardwareSummary();
                        var hwChecks = SystemHealthScanner.ScanHardwareHealth(hw, null);
                        var conflicts = SystemHealthScanner.ScanSoftwareConflicts(hw);

                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== HARDWARE, BIOS, RAM, PCIe & SSD CHECKS ===");
                        sb.AppendLine(string.Format("CPU: {0} | GPU: {1} ({2}) | Board: {3}", hw.CpuName, hw.GpuName, hw.GpuDriver, hw.BoardName));
                        foreach (var c in hwChecks)
                        {
                            sb.AppendLine(string.Format("- [{0}] ({1}) {2}: {3}", c.CategoryTag, c.Level, c.Title, c.ValueSummary));
                            sb.AppendLine("  Detail: " + c.Detail);
                        }

                        sb.AppendLine();
                        sb.AppendLine("=== SOFTWARE CONFLICTS, ANTI-CHEAT, NIC & DRIVER REMNANTS ===");
                        foreach (var c in conflicts)
                        {
                            sb.AppendLine(string.Format("- [{0}] ({1}) {2} => {3}", c.Tag, c.Level, c.Title, c.StatusText));
                            sb.AppendLine("  Detail: " + c.Explanation);
                        }
                        return MakeToolTextResult(sb.ToString(), false);
                    }

                    case "wincrash_analyze_minidumps":
                    {
                        string filePath = (args != null && args.ContainsKey("file_path") && args["file_path"] != null)
                            ? args["file_path"].ToString().Trim()
                            : "";

                        StringBuilder sb = new StringBuilder();
                        if (!string.IsNullOrEmpty(filePath))
                        {
                            if (!File.Exists(filePath))
                                return MakeToolTextResult("Minidump file not found: " + filePath, true);
                            var item = MinidumpAnalyzer.AnalyzeDumpFile(filePath);
                            if (item == null)
                                return MakeToolTextResult("Unable to parse .dmp header: " + filePath, true);

                            sb.AppendLine(string.Format("File: {0} ({1:dd/MM/yyyy HH:mm})", item.FileName, item.CrashTime));
                            sb.AppendLine("Type: " + item.DumpType);
                            sb.AppendLine("BugCheck / Exception: " + item.BugCheckOrException);
                            sb.AppendLine("Primary Culprit Module: " + item.PrimaryCulpritModule);
                            sb.AppendLine("Process: " + item.ProcessName);
                            sb.AppendLine("Related Modules: " + item.RelatedModules);
                            sb.AppendLine("Analysis: " + item.HumanExplanationEn);
                        }
                        else
                        {
                            bool needsAdmin;
                            var dumps = MinidumpAnalyzer.ScanAllAccessibleDumps(out needsAdmin);
                            sb.AppendLine(string.Format("Decoded Crash Dumps Found: {0} (C:\\Windows\\Minidump requires elevation: {1})", dumps.Count, needsAdmin));
                            foreach (var d in dumps)
                            {
                                sb.AppendLine(string.Format("- [{0:yyyy-MM-dd HH:mm}] {1} | {2} | Culprit: {3} | Process: {4}",
                                    d.CrashTime, d.FileName, d.BugCheckOrException, d.PrimaryCulpritModule, d.ProcessName));
                                sb.AppendLine("  Analysis: " + d.HumanExplanationEn);
                            }
                        }
                        return MakeToolTextResult(sb.ToString(), false);
                    }

                    case "wincrash_list_fixes":
                    {
                        var fixes = DiagnosticsEngine.InspectAllFixes(false);
                        StringBuilder sb = new StringBuilder();
                        sb.AppendLine("=== AVAILABLE 1-CLICK STABILITY FIXES ===");
                        foreach (var f in fixes)
                        {
                            sb.AppendLine(string.Format("- fix_id: \"{0}\" | [{1}] Status: {2} | Current: {3}", f.Id, f.VendorTag, f.Status, f.CurrentValue));
                            sb.AppendLine("  Title: " + f.Title);
                            sb.AppendLine("  Registry/Target: " + f.RegPathDisplay);
                            sb.AppendLine("  Explanation: " + f.Explanation);
                        }
                        return MakeToolTextResult(sb.ToString(), false);
                    }

                    case "wincrash_apply_fix":
                    {
                        string fixId = (args != null && args.ContainsKey("fix_id") && args["fix_id"] != null)
                            ? args["fix_id"].ToString().Trim()
                            : "";
                        bool restore = false;
                        if (args != null && args.ContainsKey("restore") && args["restore"] != null)
                            bool.TryParse(args["restore"].ToString(), out restore);

                        if (string.IsNullOrEmpty(fixId))
                            return MakeToolTextResult("Missing required parameter 'fix_id'.", true);

                        bool confirm = false;
                        if (args != null && args.ContainsKey("confirm") && args["confirm"] != null)
                            bool.TryParse(args["confirm"].ToString(), out confirm);
                        return ApplyFixGuarded(fixId, restore, confirm);
                    }

                    case "wincrash_get_crash_detail":
                    {
                        string crashId = (args != null && args.ContainsKey("crash_id") && args["crash_id"] != null) ? args["crash_id"].ToString().Trim() : "";
                        return MakeToolTextResult(McpCrashTools.CrashDetail(crashId), false);
                    }

                    case "wincrash_export_report":
                    {
                        string format = (args != null && args.ContainsKey("format") && args["format"] != null) ? args["format"].ToString().Trim().ToLowerInvariant() : "markdown";
                        return MakeToolTextResult(McpCrashTools.ExportReport(format), false);
                    }

                    default:
                        return MakeToolTextResult("Unknown tool: " + toolName, true);
                }
            }
            catch (Exception ex)
            {
                return MakeToolTextResult("Tool execution error: " + ex.Message, true);
            }
        }

        /// <summary>
        /// apply_fix with two independent gates: the agent must pass confirm=true, then the user must click Yes
        /// in a dialog on their own screen. Applied changes go through the recorder and the app's journal (undoable).
        /// </summary>
        private static Dictionary<string, object> ApplyFixGuarded(string fixId, bool restore, bool confirm)
        {
            var fix = DiagnosticsEngine.InspectAllFixes(false).FirstOrDefault(f => string.Equals(f.Id, fixId, StringComparison.OrdinalIgnoreCase));
            if (fix == null) return MakeToolTextResult("Unknown fix_id '" + fixId + "'. Call wincrash_list_fixes first.", true);
            string opCode = fix.Id + (restore ? "_restore" : "_apply");
            string action = (restore ? "Restore to Windows default: " : "Apply: ") + fix.Title;

            if (!confirm)
            {
                return MakeToolTextResult(
                    "[DRY RUN] Nothing was changed.\n" + action + "\nTarget: " + fix.RegPathDisplay + "\nCurrent value: " + fix.CurrentValue +
                    "\nNeeds administrator rights: " + DiagnosticsEngine.OpNeedsAdmin(opCode) +
                    "\nTo proceed, ask the user, then call again with confirm=true. They will also have to approve it on screen.", false);
            }

            var answer = System.Windows.Forms.MessageBox.Show(
                Localizer.T("Un agent IA connecté par MCP demande à modifier votre PC :\n\n", "An AI agent connected through MCP asks to change your PC:\n\n") + action +
                "\n\n" + fix.RegPathDisplay + "\n" + Localizer.T("Valeur actuelle : ", "Current value: ") + fix.CurrentValue +
                "\n\n" + Localizer.T("La valeur actuelle est sauvegardée : vous pourrez annuler depuis Dump Finder (Outils › Historique).\n\nAutoriser ?", "The current value is saved: you can undo it from Dump Finder (Tools > History).\n\nAllow?"),
                "Dump Finder — MCP", System.Windows.Forms.MessageBoxButtons.YesNo, System.Windows.Forms.MessageBoxIcon.Warning,
                System.Windows.Forms.MessageBoxDefaultButton.Button2, System.Windows.Forms.MessageBoxOptions.DefaultDesktopOnly);
            if (answer != System.Windows.Forms.DialogResult.Yes)
                return MakeToolTextResult("[DECLINED] The user refused this change on screen. Nothing was modified.", true);

            var result = FixRunner.Run(new[] { opCode }).FirstOrDefault();
            if (result == null) return MakeToolTextResult("[FAILED] No result.", true);
            History.AddJournal(new[]
            {
                new JournalEntry
                {
                    Id = Guid.NewGuid().ToString("N"), Time = DateTime.Now, Op = opCode, FixId = fix.Id,
                    Title = (restore ? "↺ " : "") + fix.Title + " (MCP)", Ok = result.Ok, Message = result.MessageEn,
                    Changes = result.Changes ?? new List<ChangeRecord>(), Reversible = result.Ok && FixRunner.IsReversible(opCode, result.Changes)
                }
            });
            return MakeToolTextResult((result.Ok ? "[SUCCESS] " : "[FAILED] ") + result.MessageEn +
                (result.Ok ? "\nRecorded in the change journal (" + (result.Changes != null ? result.Changes.Count : 0) + " value(s)); undoable from Dump Finder." : ""), !result.Ok);
        }

        // ==================== Claude Desktop ====================
        public static string GetClaudeDesktopConfigPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude_desktop_config.json");
        }

        public static bool IsRegisteredInClaudeDesktop()
        {
            try
            {
                string path = GetClaudeDesktopConfigPath();
                if (!File.Exists(path)) return false;
                var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(File.ReadAllText(path, Encoding.UTF8));
                var servers = root != null && root.ContainsKey("mcpServers") ? root["mcpServers"] as IDictionary<string, object> : null;
                return servers != null && servers.ContainsKey(ServerName);
            }
            catch (Exception ex) { Log.Warn(ex); return false; }
        }

        /// <summary>Adds Dump Finder to Claude Desktop's MCP servers (other entries are kept). Restart Claude Desktop afterwards.</summary>
        public static bool RegisterInClaudeDesktop(out string configPath, out string errorMessage)
        {
            configPath = GetClaudeDesktopConfigPath();
            errorMessage = null;
            try
            {
                if (File.Exists(configPath)) File.Copy(configPath, configPath + ".bak", true);
                MergeServerIntoConfigFile(configPath, GetExePath());
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        private static string FormatScanSummaryText(ScanResult res, int days)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Format("=== DUMP FINDER 6 SYSTEM SCAN ({0}) ===", days > 0 ? ("Last " + days + " days") : "Full History"));
            sb.AppendLine(string.Format("CPU: {0}", res.Hardware.CpuName));
            sb.AppendLine(string.Format("GPU: {0} (Driver {1})", res.Hardware.GpuName, res.Hardware.GpuDriver));
            sb.AppendLine(string.Format("Motherboard: {0} | OS: {1} | Last Boot: {2:yyyy-MM-dd HH:mm}", res.Hardware.BoardName, res.Hardware.OsVersion, res.Hardware.LastBoot));
            sb.AppendLine();
            sb.AppendLine("[SMART ROOT-CAUSE DIAGNOSIS]");
            sb.AppendLine("Title: " + res.DiagnosisTitle);
            sb.AppendLine("Summary: " + res.DiagnosisBody);
            sb.AppendLine(string.Format("Counts => BSOD/Reboots: {0} | GPU TDR: {1} | Storage NVMe: {2} | WHEA/RAM/NIC/TPM: {3} | Apps/Browsers: {4}",
                res.BsodAndRebootCount, res.GpuTdrCount, res.StorageNvmeCount, res.WheaAndMemCount, res.BrowserAndAppCount));

            var riskyHw = res.HardwareChecks.Where(h => h.Level == HealthLevel.Warning || h.Level == HealthLevel.Critical).ToList();
            if (riskyHw.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[HARDWARE WARNINGS]");
                foreach (var h in riskyHw)
                    sb.AppendLine(string.Format("- [{0}] {1}: {2}", h.CategoryTag, h.ValueSummary, h.Detail));
            }

            var riskyConf = res.ConflictChecks.Where(c => c.Level == HealthLevel.Warning || c.Level == HealthLevel.Critical).ToList();
            if (riskyConf.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[CONFLICT WARNINGS]");
                foreach (var c in riskyConf)
                    sb.AppendLine(string.Format("- [{0}] {1}: {2}", c.Tag, c.StatusText, c.Explanation));
            }

            if (res.Minidumps != null && res.Minidumps.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[RECENT CRASH DUMPS (.DMP)]");
                foreach (var d in res.Minidumps.Take(8))
                    sb.AppendLine(string.Format("- [{0:yyyy-MM-dd HH:mm}] {1} | {2} | Culprit: {3} ({4})", d.CrashTime, d.FileName, d.BugCheckOrException, d.PrimaryCulpritModule, d.ProcessName));
            }

            if (res.Events != null && res.Events.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("[RECENT CRASH EVENTS (Top {0} of {1})]", Math.Min(20, res.Events.Count), res.Events.Count));
                foreach (var ev in res.Events.Take(20))
                    sb.AppendLine(string.Format("- [{0}] [{1}] {2}: {3}", ev.DisplayTime, ev.Category, ev.CodeOrId, ev.SummaryEn));
            }

            var recFixes = res.Fixes.Where(f => f.Status == FixStatus.Recommended).ToList();
            if (recFixes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("[RECOMMENDED PENDING FIXES]");
                foreach (var f in recFixes)
                    sb.AppendLine(string.Format("- fix_id=\"{0}\": {1} (Current: {2})", f.Id, f.Title, f.CurrentValue));
            }

            return sb.ToString();
        }

        private static Dictionary<string, object> MakeToolTextResult(string text, bool isError)
        {
            return new Dictionary<string, object>
            {
                { "content", new object[]
                    {
                        new Dictionary<string, object>
                        {
                            { "type", "text" },
                            { "text", text }
                        }
                    }
                },
                { "isError", isError }
            };
        }

        private static void WriteError(StreamWriter writer, JavaScriptSerializer ser, object id, int code, string message)
        {
            var errResp = new Dictionary<string, object>
            {
                { "jsonrpc", "2.0" },
                { "id", id },
                { "error", new Dictionary<string, object>
                    {
                        { "code", code },
                        { "message", message }
                    }
                }
            };
            writer.WriteLine(ser.Serialize(errResp));
        }

        private static string PrettyPrintJson(string json)
        {
            StringBuilder sb = new StringBuilder();
            int indent = 0;
            bool inQuotes = false;
            bool escaped = false;

            foreach (char ch in json)
            {
                if (escaped)
                {
                    sb.Append(ch);
                    escaped = false;
                    continue;
                }
                if (ch == '\\' && inQuotes)
                {
                    sb.Append(ch);
                    escaped = true;
                    continue;
                }
                if (ch == '"')
                {
                    inQuotes = !inQuotes;
                    sb.Append(ch);
                    continue;
                }
                if (!inQuotes)
                {
                    if (ch == '{' || ch == '[')
                    {
                        sb.Append(ch);
                        sb.AppendLine();
                        indent++;
                        sb.Append(new string(' ', indent * 2));
                        continue;
                    }
                    if (ch == '}' || ch == ']')
                    {
                        sb.AppendLine();
                        indent = Math.Max(0, indent - 1);
                        sb.Append(new string(' ', indent * 2));
                        sb.Append(ch);
                        continue;
                    }
                    if (ch == ',')
                    {
                        sb.Append(ch);
                        sb.AppendLine();
                        sb.Append(new string(' ', indent * 2));
                        continue;
                    }
                    if (ch == ':')
                    {
                        sb.Append(": ");
                        continue;
                    }
                }
                sb.Append(ch);
            }
            return sb.ToString();
        }
    }
}
