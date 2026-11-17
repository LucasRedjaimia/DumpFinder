using DumpFinder.Mac.Core;

namespace DumpFinder.Mac;

/// <summary>
/// AI assistant, same as Windows (MainWindow.AiPage.cs + the crash drawer): provider, key and model in
/// Settings, "Ask the AI" streaming into the crash panel (second click stops), and the whole-system check-up
/// opened from Home when there is no crash to look at.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>Key of the stream owner: a crash key, or this one for the system check-up.</summary>
    const string SystemReviewKey = "\u0001system-review";

    public RelayCommand AskAiCommand { get; private set; } = null!;
    public RelayCommand SystemReviewCommand { get; private set; } = null!;
    public RelayCommand CloseSystemReviewCommand { get; private set; } = null!;
    public RelayCommand CopyAiPromptCommand { get; private set; } = null!;

    string _apiKey = "";
    CancellationTokenSource? _aiCts, _keySave;
    string? _aiKey;

    void InitAi()
    {
        _apiKey = Offline ? "" : SecretStore.Load();
        AskAiCommand = new(() => RunAiAsync(_detail is { } c ? AiAssistant.FocusQuestion(c) : "", _detail?.Key ?? SystemReviewKey));
        SystemReviewCommand = new(() => { OpenSystemReview(); return Task.CompletedTask; });
        CloseSystemReviewCommand = new(() => { SystemReviewVisible = false; return Task.CompletedTask; });
        CopyAiPromptCommand = new(async () =>
        {
            await _platform.CopyTextAsync(AiAssistant.BuildPrompt(_result, _summary, ""));
            ShowToast("success", Loc.S("ai.ai_report_copied_paste_it_into"));
        });
        ResetAiOutput();
        RefreshMcp();
    }

    // ---------- Settings › AI assistant ----------
    public string[] AiProviders { get; } = { "Google Gemini", "Anthropic Claude", "Ollama (local)" };
    public string[] ClaudeModelNames { get; } = ClaudeAdvisor.Models.Select(m => m[1]).ToArray();

    AiProvider Provider => Enum.IsDefined(typeof(AiProvider), _settings.AiProvider) ? (AiProvider)_settings.AiProvider : AiProvider.Gemini;

    public int AiProviderIndex
    {
        get => (int)Provider;
        set
        {
            if (value < 0 || value == _settings.AiProvider) return;
            _settings.AiProvider = value;
            _settings.Save();
            foreach (var n in new[] { nameof(AiProviderIndex), nameof(IsClaude), nameof(IsOllama), nameof(NeedsKey), nameof(AiCredentialCaption) }) Raise(n);
            ResetAiOutput();
        }
    }
    public bool IsClaude => Provider == AiProvider.Claude;
    public bool IsOllama => Provider == AiProvider.Ollama;
    public bool NeedsKey => !IsOllama;
    public string AiCredentialCaption => Loc.S(Provider switch
    {
        AiProvider.Ollama => "ai.local_ollama_model",
        AiProvider.Claude => "ai.anthropic_claude_api_key_stored_locally",
        _ => "ai.google_gemini_api_key_stored_locally",
    });

    public int ClaudeModelIndex
    {
        get => Math.Max(0, Array.FindIndex(ClaudeAdvisor.Models, m => m[0] == _settings.ClaudeModel));
        set
        {
            if (value < 0 || value >= ClaudeAdvisor.Models.Length) return;
            _settings.ClaudeModel = ClaudeAdvisor.Models[value][0];
            _settings.Save();
            Raise();
        }
    }

    public string OllamaModel
    {
        get => _settings.OllamaModel.Length > 0 ? _settings.OllamaModel : "llama3.2";
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            _settings.OllamaModel = value.Trim();
            _settings.Save();
        }
    }

    /// <summary>The key typed in Settings; saved to the Keychain 0.8 s after the last keystroke.</summary>
    public string ApiKey
    {
        get => _apiKey;
        set
        {
            if (value == _apiKey) return;
            _apiKey = value ?? "";
            Raise();
            ResetAiOutput();
            _keySave?.Cancel();
            var cts = _keySave = new CancellationTokenSource();
            var key = _apiKey;
            _ = Task.Delay(800, cts.Token).ContinueWith(t =>
            {
                if (t.IsCanceled || Offline) return;
                if (!SecretStore.Save(key))
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowToast("danger", Loc.S("mac.ai.key_not_saved")));
            }, TaskScheduler.Default);
        }
    }

    bool AiReady => IsOllama || _apiKey.Trim().Length > 0;

    // ---------- Output area (crash panel and system check-up) ----------
    string _aiOutput = "", _aiTone = "muted";
    public string AiOutput { get => _aiOutput; private set => Set(ref _aiOutput, value); }
    /// <summary>muted (hint / thinking), primary (answer), warn (error reply), danger (exception).</summary>
    public string AiTone { get => _aiTone; private set { Set(ref _aiTone, value); Raise(nameof(AiMuted)); Raise(nameof(AiWarn)); Raise(nameof(AiDanger)); } }
    public bool AiMuted => AiTone == "muted";
    public bool AiWarn => AiTone == "warn";
    public bool AiDanger => AiTone == "danger";
    bool _aiBusy;
    public bool AiBusy { get => _aiBusy; private set { Set(ref _aiBusy, value); Raise(nameof(AskAiLabel)); Raise(nameof(RunCheckUpLabel)); } }
    public string AskAiLabel => Loc.Plain(Loc.S(AiBusy ? "crashes.stop" : "crashes.ask_the_ai"));
    public string RunCheckUpLabel => Loc.Plain(Loc.S(AiBusy ? "crashes.stop" : "crashes.run_check_up"));

    void ResetAiOutput()
    {
        if (AiBusy) return;
        AiOutput = Loc.S(AiReady ? "crashes.ask_the_ai_sends_this_crash" : "crashes.no_ai_assistant_set_up_yet");
        AiTone = "muted";
    }

    /// <summary>
    /// Windows PrepareDrawerAiFor: when the panel switches to another crash, a running stream for the previous
    /// one is cancelled; a stream for the same crash keeps going.
    /// </summary>
    void OnDetailChanged(string? key)
    {
        if (AiBusy && _aiKey == key) return;
        CancelAi();
        ResetAiOutput();
    }

    void CancelAi()
    {
        var cts = _aiCts;
        _aiCts = null;   // the old run sees it is no longer current and leaves the panel alone
        _aiKey = null;
        AiBusy = false;
        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
    }

    async Task RunAiAsync(string question, string key)
    {
        if (AiBusy) { _aiCts?.Cancel(); return; }   // second click = stop
        if (!AiReady)
        {
            ShowToast("warn", Loc.S("crashes.set_up_an_ai_assistant_in"));
            Page = TabSettings;
            return;
        }
        var prompt = AiAssistant.BuildPrompt(_result, _summary, question);
        var provider = Provider;
        var model = provider == AiProvider.Claude ? _settings.ClaudeModel : provider == AiProvider.Ollama ? OllamaModel : null;

        var cts = new CancellationTokenSource();
        _aiCts = cts;
        _aiKey = key;
        AiBusy = true;
        AiOutput = Loc.S("crashes.thinking");
        AiTone = "muted";
        bool IsCurrent() => ReferenceEquals(_aiCts, cts);

        bool first = true;
        void OnDelta(string chunk) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (!IsCurrent()) return;
            if (first) { AiOutput = ""; AiTone = "primary"; first = false; }
            AiOutput += chunk;
        });

        try
        {
            var reply = await Task.Run(() => AiAssistant.StreamAsync(provider, _apiKey, model, prompt, OnDelta, cts.Token));
            // Let queued chunks land, then show the authoritative final text.
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            if (IsCurrent())
            {
                AiOutput = reply.Text;
                AiTone = reply.Ok ? "primary" : "warn";
            }
        }
        catch (Exception e)
        {
            if (IsCurrent()) { AiOutput = e.Message; AiTone = "danger"; }
        }
        finally
        {
            if (IsCurrent()) { _aiCts = null; _aiKey = null; AiBusy = false; }
            cts.Dispose();
        }
    }

    // ---------- Whole-system check-up (Home, when there is no crash to focus on) ----------
    bool _systemReviewVisible;
    public bool SystemReviewVisible
    {
        get => _systemReviewVisible;
        private set { Set(ref _systemReviewVisible, value); Raise(nameof(DetailShown)); }
    }
    /// <summary>The crash panel is shown only when the check-up panel is not.</summary>
    public bool DetailShown => DetailVisible && !SystemReviewVisible;

    void OpenSystemReview()
    {
        Page = TabCrashes;
        CrashTab = 0;
        if (!(AiBusy && _aiKey == SystemReviewKey)) { CancelAi(); ResetAiOutput(); }
        SystemReviewVisible = true;
    }

    public RelayCommand RunCheckUpCommand => _runCheckUp ??= new(() => RunAiAsync("", SystemReviewKey));
    RelayCommand? _runCheckUp;

    // ---------- Settings › Integrations: MCP server card ----------
    public RelayCommand RegisterClaudeDesktopCommand => _regClaude ??= new(() =>
    {
        bool ok = McpServer.RegisterInClaudeDesktop(out var err);
        RefreshMcp();
        ShowToast(ok ? "success" : "danger", ok ? Loc.S("ai.dump_finder_added_to_claude_desktop") : "MCP : " + err);
        return Task.CompletedTask;
    });
    public RelayCommand RegisterAntigravityCommand => _regAg ??= new(() =>
    {
        bool ok = McpServer.RegisterInAntigravity(out var err);
        RefreshMcp();
        ShowToast(ok ? "success" : "danger", ok ? Loc.F("ai.dump_finder_mcp_server_enabled_in", Privacy.Redact(McpServer.AntigravityConfigPath)) : "MCP : " + err);
        return Task.CompletedTask;
    });
    public RelayCommand CopyClaudeCodeCommand => _copyCc ??= new(() => CopyCommandAsync(McpServer.ClaudeCodeCommand));
    public RelayCommand CopyGeminiCommand => _copyGem ??= new(() => CopyCommandAsync(McpServer.GeminiCliCommand));
    RelayCommand? _regClaude, _regAg, _copyCc, _copyGem;

    async Task CopyCommandAsync(string text)
    {
        await _platform.CopyTextAsync(text);
        ShowToast("success", Loc.S("ai.copied") + text);
    }

    bool _mcpRegistered;
    string _mcpStatus = "", _mcpPreview = "";
    public bool McpRegistered { get => _mcpRegistered; private set => Set(ref _mcpRegistered, value); }
    public string McpStatus { get => _mcpStatus; private set => Set(ref _mcpStatus, value); }
    public string McpPreview { get => _mcpPreview; private set => Set(ref _mcpPreview, value); }

    /// <summary>Windows RefreshMcpRegistrationStatus: badge and the configuration preview.</summary>
    void RefreshMcp()
    {
        bool cd = !Offline && McpServer.IsRegisteredInClaudeDesktop(), ag = !Offline && McpServer.IsRegisteredInAntigravity();
        McpRegistered = cd || ag;
        McpStatus = Loc.Plain(Loc.S(McpRegistered ? "ai.connected" : "ai.ready_to_enable")) + (cd ? " · Claude Desktop" : "") + (ag ? " · Antigravity" : "");
        var exe = McpServer.ExePath.Replace("\\", "\\\\").Replace("\"", "\\\"");
        // Full paths, as on Windows: the line is meant to be pasted into a client configuration ("~" is not expanded there).
        McpPreview = (
            "// Claude Desktop : " + McpServer.ClaudeDesktopConfigPath + "\n" +
            "{ \"mcpServers\": { \"" + McpServer.ServerName + "\": { \"command\": \"" + exe + "\", \"args\": [\"--mcp\"] } } }\n" +
            "// Tools (7) : scan_system • check_hardware_and_conflicts • analyze_panic_report • get_crash_detail • list_fixes • apply_fix (dry run + on-screen approval) • export_report");
    }
}
