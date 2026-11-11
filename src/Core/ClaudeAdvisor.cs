using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anthropic;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace WinCrashDoctor
{
    /// <summary>
    /// Claude crash advisor through the official Anthropic SDK: streamed answer, explicit effort,
    /// refusal handling, and server-side refusal fallbacks.
    /// </summary>
    public static class ClaudeAdvisor
    {
        public const string DefaultModel = "claude-opus-5-5";

        /// <summary>Models offered in Settings (id, label). The first one is the default.</summary>
        public static readonly string[][] Models =
        {
            new[] { "claude-opus-5-5", "Claude Opus 5.5" },
            new[] { "claude-sonnet-5-5", "Claude Sonnet 5.5" },
            new[] { "claude-haiku-5-5", "Claude Haiku 5.5" }
        };

        public static async Task<AiReply> StreamAsync(string apiKey, string model, string prompt, Action<string> onDelta, CancellationToken ct)
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
                Messages = [new BetaUserMessageParam { Content = prompt }]
            };
            // Opus / Sonnet: if a safety classifier declines, the server re-serves the request on a fallback model.
            if (!model.StartsWith("claude-haiku", StringComparison.Ordinal))
            {
                parameters = parameters with
                {
                    Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                    Fallbacks = new Default()
                };
            }

            var text = new StringBuilder();
            string stopReason = null;
            await foreach (var ev in client.Beta.Messages.CreateStreaming(parameters, ct).WithCancellation(ct))
            {
                if (ev.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var t))
                {
                    text.Append(t.Text);
                    onDelta?.Invoke(t.Text);
                }
                else if (ev.TryPickDelta(out var md) && md.Delta.StopReason != null)
                {
                    stopReason = md.Delta.StopReason.ToString();
                }
            }

            if (stopReason != null && stopReason.IndexOf("refusal", StringComparison.OrdinalIgnoreCase) >= 0 && text.Length == 0)
            {
                return new AiReply
                {
                    Ok = false,
                    Text = Localizer.T(
                        "Claude a refusé de répondre à cette demande. Reformulez la question ou utilisez « Copier pour forum ».",
                        "Claude declined this request. Rephrase the question or use \"Copy for forum\".")
                };
            }
            return new AiReply { Ok = text.Length > 0, Text = text.ToString().Trim() };
        }
    }

    public class AiReply
    {
        public bool Ok { get; set; }
        public string Text { get; set; }
    }
}
