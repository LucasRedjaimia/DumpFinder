using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace DumpFinder.Mac.Core;

/// <summary>Same order as the Windows AiProviderType (the saved setting is an index).</summary>
public enum AiProvider { Gemini = 0, Claude = 1, Ollama = 2 }

public sealed record AiReply(bool Ok, string Text);

/// <summary>
/// The "Ask the AI" assistant, same providers and prompt layout as Windows (src/Core/AiAssistant.cs):
/// Claude through the official Anthropic SDK, Google Gemini over REST (server-sent events), Ollama on this Mac.
/// Everything sent out goes through <see cref="Privacy.Redact"/>.
/// </summary>
public static partial class AiAssistant
{
    /// <summary>
    /// The whole diagnosis as plain text: role, the user's question, this Mac, the automated verdict,
    /// the Hardware and Conflicts cards, the latest kernel panics and crashes, third-party extensions.
    /// </summary>
    public static string BuildPrompt(ScanResult? r, StabilitySummary? s, string question)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.S("mac.ai.role"));
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(question))
        {
            sb.AppendLine("=== USER CONTEXT / SPECIFIC QUESTION ===");
            sb.AppendLine(question.Trim());
            sb.AppendLine();
        }
        if (r is null || s is null)
        {
            sb.AppendLine("(System scan still loading.)");
            return Privacy.Redact(sb.ToString());
        }

        var hw = r.Hardware;
        sb.AppendLine("=== THIS MAC ===");
        sb.AppendLine($"Model       : {hw.MachineName} ({hw.ModelId})");
        sb.AppendLine($"Chip        : {hw.Chip} · {hw.PerformanceCores}P + {hw.EfficiencyCores}E cores · GPU {hw.GpuCores} cores {hw.Metal}".TrimEnd());
        sb.AppendLine($"Memory      : {hw.Memory} {hw.MemoryType}".TrimEnd());
        sb.AppendLine($"macOS       : {hw.OsVersion} ({hw.Kernel})");
        if (r.Boot.BootTime is { } bt) sb.AppendLine($"Last boot   : {bt.LocalDateTime:yyyy-MM-dd HH:mm} · shutdown reason: {r.Boot.ShutdownReason}");
        sb.AppendLine();

        sb.AppendLine("=== AUTOMATED DIAGNOSIS ===");
        sb.AppendLine($"{s.Score}/100 · {s.VerdictTitle}");
        sb.AppendLine(s.VerdictBody);
        var last30 = s.Crashes.Where(c => (DateTime.Now - c.Time).TotalDays <= 30).ToList();
        sb.AppendLine("Counters (30 days) => " + string.Join(" | ",
            last30.GroupBy(c => c.Category).Select(g => $"{CrashTimeline.CategoryName(g.Key)}: {g.Count()}")));
        sb.AppendLine();

        var cards = HardwareChecks.Build(r, DetectionData.Embedded, DateTimeOffset.Now);
        sb.AppendLine("=== HARDWARE CHECKS ===");
        foreach (var c in cards.Where(c => c.Tab == HardwareChecks.TabHardware))
            sb.AppendLine($"- [{c.Tag}] ({c.Level}) {c.Title}: {c.Value} — {c.Detail}");
        sb.AppendLine();
        sb.AppendLine("=== SECURITY, EXTENSIONS & CONFLICTS ===");
        foreach (var c in cards.Where(c => c.Tab == HardwareChecks.TabConflicts))
            sb.AppendLine($"- [{c.Tag}] ({c.Level}) {c.Title}: {c.Value} — {c.Detail}");
        foreach (var e in HardwareChecks.Extensions(r))
            sb.AppendLine($"- {(e.Kernel ? "kext" : "system extension")} {e.BundleId} {e.Version} {(e.Loaded ? "loaded" : "not loaded")}{(e.Crashes > 0 ? $", in {e.Crashes} panic backtrace(s)" : "")}");
        sb.AppendLine();

        if (r.Panics.Count > 0)
        {
            sb.AppendLine("=== KERNEL PANICS ===");
            foreach (var p in r.Panics.OrderByDescending(p => p.Time).Take(8))
                sb.AppendLine($"- [{p.Time?.LocalDateTime:yyyy-MM-dd HH:mm}] {Path.GetFileName(p.File)} | {p.Message} | Process: {p.PanickedProcess} | Extensions in backtrace: {string.Join(", ", p.BacktraceExtensions)}");
            sb.AppendLine();
        }
        if (s.Crashes.Count > 0)
        {
            sb.AppendLine("=== RECENT CRASHES (TOP 15) ===");
            foreach (var c in s.Crashes.Take(15))
                sb.AppendLine($"- [{c.Time:yyyy-MM-dd HH:mm}] [{c.Category}] {c.Title} — {c.TechLine}");
        }
        return Privacy.Redact(sb.ToString());
    }

    /// <summary>Windows CrashCatalog.AiFocusQuestion: the crash the user opened, with its evidence.</summary>
    public static string FocusQuestion(CrashEntry c)
    {
        var sb = new StringBuilder(Loc.S("crash_catalog.focus_on_this_specific_crash_explain"));
        sb.Append("Crash: ").Append(c.Title).Append(" @ ").Append(c.Time.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)).Append(". ");
        foreach (var kv in CrashDetails.Analysis(c)) sb.Append(kv.Key).Append(": ").Append(kv.Value).Append(". ");
        if (CrashDetails.Evidence(c) is { Length: > 0 } ev) sb.Append("Evidence: ").Append(ev).Append(' ');
        if (c.Panic is { } p && p.PanicText.Length > 0)
            sb.Append("Panic string: ").Append(p.PanicText.Length > 3000 ? p.PanicText[..3000] + "…" : p.PanicText);
        return Privacy.Redact(sb.ToString());
    }

    /// <summary>Streams an answer; <paramref name="onDelta"/> gets chunks on a background thread. Never throws.</summary>
    public static async Task<AiReply> StreamAsync(AiProvider provider, string apiKey, string? model, string prompt, Action<string> onDelta, CancellationToken ct)
    {
        try
        {
            switch (provider)
            {
                case AiProvider.Claude:
                    if (string.IsNullOrWhiteSpace(apiKey)) return MissingKey();
                    return await ClaudeAdvisor.StreamAsync(apiKey, model, prompt, onDelta, ct).ConfigureAwait(false);
                case AiProvider.Gemini:
                    if (string.IsNullOrWhiteSpace(apiKey)) return MissingKey();
                    return await StreamGeminiAsync(apiKey.Trim(), prompt, onDelta, ct).ConfigureAwait(false);
                default:
                    return await StreamOllamaAsync(model, prompt, onDelta, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            return new AiReply(false, Loc.S("engine.ai_assistant.request_cancelled"));
        }
        catch (HttpRequestException) when (provider == AiProvider.Ollama)
        {
            return new AiReply(false, Loc.F("engine.ai_assistant.could_not_reach_ollama", string.IsNullOrWhiteSpace(model) ? "llama3.2" : model!));
        }
        catch (HttpRequestException e)
        {
            return new AiReply(false, "HTTP : " + e.Message);
        }
        catch (Exception e)
        {
            // SDK errors (authentication, rate limit, overload) carry a readable message.
            return new AiReply(false, e.GetType().Name.Replace("Exception", "") + " : " + e.Message);
        }
    }

    static AiReply MissingKey() => new(false, Loc.S("engine.ai_assistant.no_api_key_configured_add_it"));

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    // ---------- Google Gemini (REST, server-sent events) ----------
    static string? _geminiModel;

    /// <summary>Newest "flash" model that supports generateContent on this key (cached), as on Windows.</summary>
    static async Task<string> ResolveGeminiModelAsync(string apiKey, CancellationToken ct)
    {
        if (_geminiModel is not null) return _geminiModel;
        const string fallback = "gemini-2.5-flash";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
            req.Headers.Add("x-goog-api-key", apiKey);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(15));
            using var resp = await Http.SendAsync(req, cts.Token).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false));
            var names = new List<string>();
            if (doc.RootElement.TryGetProperty("models", out var models))
                foreach (var m in models.EnumerateArray())
                {
                    var name = (m.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "").Replace("models/", "");
                    bool gen = m.TryGetProperty("supportedGenerationMethods", out var meth) && meth.EnumerateArray().Any(x => x.GetString() == "generateContent");
                    if (gen && name.StartsWith("gemini-", StringComparison.Ordinal) && name.Contains("flash")
                        && !new[] { "lite", "exp", "preview", "image", "tts", "live" }.Any(name.Contains))
                        names.Add(name);
                }
            _geminiModel = names.OrderByDescending(VersionOf).ThenBy(n => n.Length).FirstOrDefault() ?? fallback;
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
        {
            _geminiModel = fallback;
        }
        return _geminiModel;
    }

    static double VersionOf(string name)
    {
        var m = GeminiVersionRx().Match(name);
        return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    static async Task<AiReply> StreamGeminiAsync(string apiKey, string prompt, Action<string> onDelta, CancellationToken ct)
    {
        var model = await ResolveGeminiModelAsync(apiKey, ct).ConfigureAwait(false);
        var body = JsonSerializer.Serialize(new
        {
            contents = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            generationConfig = new { maxOutputTokens = 8192 },
        });
        using var req = new HttpRequestMessage(HttpMethod.Post, $"https://generativelanguage.googleapis.com/v1beta/models/{model}:streamGenerateContent?alt=sse")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-goog-api-key", apiKey);
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            var err = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new AiReply(false, $"HTTP {(int)resp.StatusCode} : " + (err.Length > 500 ? err[..500] + "..." : err));
        }
        var text = new StringBuilder();
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var piece = GeminiChunkText(line[5..].Trim());
            if (piece.Length == 0) continue;
            text.Append(piece);
            onDelta(piece);
        }
        return text.Length > 0 ? new AiReply(true, text.ToString().Trim()) : new AiReply(false, $"Gemini ({model}) : empty answer.");
    }

    /// <summary>Text parts of the first candidate of one SSE chunk ("" when there is none).</summary>
    public static string GeminiChunkText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0) return "";
            if (!cands[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return "";
            var sb = new StringBuilder();
            foreach (var p in parts.EnumerateArray())
                if (p.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String) sb.Append(t.GetString());
            return sb.ToString();
        }
        catch (JsonException) { return ""; }
    }

    // ---------- Ollama (local, NDJSON stream) ----------
    static async Task<AiReply> StreamOllamaAsync(string? modelName, string prompt, Action<string> onDelta, CancellationToken ct)
    {
        var model = string.IsNullOrWhiteSpace(modelName) ? "llama3.2" : modelName.Trim();
        var body = JsonSerializer.Serialize(new { model, prompt, stream = true });
        using var req = new HttpRequestMessage(HttpMethod.Post, "http://127.0.0.1:11434/api/generate")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var text = new StringBuilder();
        using var reader = new StreamReader(await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false), Encoding.UTF8);
        while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0) continue;
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("error", out var err)) return new AiReply(false, "Ollama : " + err);
            if (doc.RootElement.TryGetProperty("response", out var r) && r.GetString() is { Length: > 0 } piece)
            {
                text.Append(piece);
                onDelta(piece);
            }
        }
        return new AiReply(text.Length > 0, text.ToString().Trim());
    }

    [GeneratedRegex(@"gemini-(\d+(?:\.\d+)?)")]
    private static partial Regex GeminiVersionRx();
}

/// <summary>
/// Claude through the official Anthropic SDK, same request as Windows (src/Core/ClaudeAdvisor.cs): streamed,
/// effort medium, server-side refusal fallback on Opus / Sonnet, refusal reported in the app language.
/// </summary>
public static class ClaudeAdvisor
{
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>Models offered in Settings (id, label). The first one is the default.</summary>
    public static readonly string[][] Models =
    {
        new[] { "claude-opus-5-5", "Claude Opus 5.5" },
        new[] { "claude-sonnet-5-5", "Claude Sonnet 5.5" },
        new[] { "claude-haiku-5-5", "Claude Haiku 5.5" },
    };

    public static async Task<AiReply> StreamAsync(string apiKey, string? model, string prompt, Action<string> onDelta, CancellationToken ct)
    {
        model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        // Pin the endpoint: the key typed in Settings is for api.anthropic.com, whatever
        // ANTHROPIC_BASE_URL / credential profiles might exist in the environment.
        var client = new AnthropicClient { ApiKey = apiKey.Trim(), BaseUrl = "https://api.anthropic.com" };

        var parameters = new MessageCreateParams
        {
            Model = model,
            MaxTokens = 64000,
            // Crash triage is a focused question: medium effort keeps answers quick and thorough enough.
            OutputConfig = new BetaOutputConfig { Effort = Effort.Medium },
            Messages = [new BetaUserMessageParam { Content = prompt }],
        };
        // Opus / Sonnet: if a safety classifier declines, the server re-serves the request on a fallback model.
        if (!model.StartsWith("claude-haiku", StringComparison.Ordinal))
            parameters = parameters with { Betas = [AnthropicBeta.ServerSideFallback2026_07_01], Fallbacks = new Default() };

        var text = new StringBuilder();
        string? stopReason = null;
        await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, ct).WithCancellation(ct))
        {
            if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
            {
                text.Append(t.Text);
                onDelta(t.Text);
            }
            else if (ev.TryPickDelta(out var md) && md.Delta.StopReason is not null)
            {
                stopReason = md.Delta.StopReason.ToString();
            }
        }
        if (stopReason?.Contains("refusal", StringComparison.OrdinalIgnoreCase) == true && text.Length == 0)
            return new AiReply(false, Loc.S("engine.claude_advisor.claude_declined_this_request_rephrase_th"));
        return new AiReply(text.Length > 0, text.ToString().Trim());
    }
}
