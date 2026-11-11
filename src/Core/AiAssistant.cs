using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    public static class AiAssistant
    {
        public static string BuildStructuredPrompt(ScanResult res, string userQuestion)
        {
            StringBuilder sb = new StringBuilder();

            string roleInstruction = Localizer.L(
                "Tu es un ingénieur expert en diagnostic matériel PC, noyau Windows (BSOD, WHEA, TDR GPU, PCIe, DDR5, NVMe) et pilotes graphiques (NVIDIA, AMD, Intel Arc). Analyse le rapport technique réel ci-dessous issu de Dump Finder 6 et donne : 1) La cause racine la plus probable, 2) Les vérifications ou réglages précis (Registre, BIOS, pilote, matériel) par ordre de priorité, 3) Ce qu'il faut éviter de toucher inutilement. Réponds en français de façon claire et structurée.",
                "You are a senior PC hardware and Windows kernel crash engineer (BSOD, WHEA, GPU TDR, PCIe, DDR5, NVMe, NVIDIA/AMD/Intel Arc drivers). Analyze the real Dump Finder 6 telemetry below and provide: 1) Most likely root cause, 2) Prioritized actionable fixes (Registry, BIOS, driver, hardware), 3) What NOT to touch unnecessarily. Reply concisely in English.",
                "Eres un ingeniero experto en hardware de PC y fallos del kernel de Windows (BSOD, WHEA, GPU TDR, PCIe, DDR5, NVMe). Analiza el informe real de Dump Finder 6 y explica: 1) Causa raíz más probable, 2) Soluciones prioritarias paso a paso, 3) Qué no tocar innecesariamente. Responde en español.",
                "Du bist ein erfahrener Ingenieur für PC-Hardware und Windows-Kernel-Abstürze (BSOD, WHEA, GPU-TDR, PCIe, DDR5, NVMe). Analysiere den echten Dump Finder 6-Bericht unten und nenne: 1) Wahrscheinlichste Ursache, 2) Priorisierte Lösungsschritte, 3) Was nicht unnötig geändert werden sollte. Antworte auf Deutsch.",
                "Sei un ingegnere esperto in hardware PC e crash del kernel Windows (BSOD, WHEA, GPU TDR, PCIe, DDR5, NVMe). Analizza il report reale di Dump Finder 6 qui sotto e indica: 1) Causa principale più probabile, 2) Soluzioni prioritarie passo dopo passo, 3) Cosa evitare di toccare. Rispondi in italiano.",
                "Você é um engenheiro especialista em hardware de PC e falhas de kernel do Windows (BSOD, WHEA, GPU TDR, PCIe, DDR5, NVMe). Analise o relatório real do Dump Finder 6 abaixo e forneça: 1) Causa raiz mais provável, 2) Soluções prioritárias passo a passo, 3) O que evitar alterar sem necessidade. Responda em português.");

            sb.AppendLine(roleInstruction);
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(userQuestion))
            {
                sb.AppendLine("=== USER CONTEXT / SPECIFIC QUESTION ===");
                sb.AppendLine(userQuestion.Trim());
                sb.AppendLine();
            }

            if (res == null)
            {
                sb.AppendLine("(System scan still loading.)");
                return sb.ToString();
            }

            sb.AppendLine("=== SYSTEM HARDWARE ===");
            sb.AppendLine("CPU         : " + res.Hardware.CpuName);
            sb.AppendLine("GPU         : " + res.Hardware.GpuName + " (Driver: " + res.Hardware.GpuDriver + ")");
            sb.AppendLine("Motherboard : " + res.Hardware.BoardName);
            sb.AppendLine("OS          : " + res.Hardware.OsVersion);
            sb.AppendLine("Last Boot   : " + res.Hardware.LastBoot.ToString("yyyy-MM-dd HH:mm"));
            sb.AppendLine();

            sb.AppendLine("=== AUTOMATED DIAGNOSIS ===");
            sb.AppendLine(res.DiagnosisTitle);
            sb.AppendLine(res.DiagnosisBody);
            sb.AppendLine(string.Format("Counters => BSOD/Reboots: {0} | GPU TDR: {1} | NVMe: {2} | WHEA/RAM/NIC/TPM: {3} | Apps/Browsers: {4}",
                res.BsodAndRebootCount, res.GpuTdrCount, res.StorageNvmeCount, res.WheaAndMemCount, res.BrowserAndAppCount));
            sb.AppendLine();

            if (res.HardwareChecks != null && res.HardwareChecks.Count > 0)
            {
                sb.AppendLine("=== HARDWARE, BIOS, RAM, PCIe & SSD CHECKS ===");
                foreach (var h in res.HardwareChecks)
                    sb.AppendLine(string.Format("- [{0}] ({1}) {2} — {3}", h.CategoryTag, h.Level, h.ValueSummary, h.Detail));
                sb.AppendLine();
            }

            if (res.ConflictChecks != null && res.ConflictChecks.Count > 0)
            {
                sb.AppendLine("=== SOFTWARE CONFLICTS, ANTI-CHEAT, NIC & DRIVER STORE ===");
                foreach (var c in res.ConflictChecks)
                    sb.AppendLine(string.Format("- [{0}] ({1}) {2} — {3}", c.Tag, c.Level, c.StatusText, c.Explanation));
                sb.AppendLine();
            }

            if (res.Minidumps != null && res.Minidumps.Count > 0)
            {
                sb.AppendLine("=== DECODED CRASH DUMPS (.DMP) ===");
                foreach (var d in res.Minidumps.Take(8))
                {
                    sb.AppendLine(string.Format("- [{0:yyyy-MM-dd HH:mm}] {1} | {2} | Culprit: {3} | Process: {4} | Modules: {5}",
                        d.CrashTime, d.FileName, d.BugCheckOrException, d.PrimaryCulpritModule, d.ProcessName, d.RelatedModules));
                }
                sb.AppendLine();
            }

            if (res.Events != null && res.Events.Count > 0)
            {
                sb.AppendLine("=== RECENT CRASH EVENTS (TOP 15) ===");
                foreach (var ev in res.Events.Take(15))
                {
                    sb.AppendLine(string.Format("- [{0}] [{1}] {2} — {3}",
                        ev.DisplayTime, ev.Category, ev.CodeOrId, Localizer.T(ev.SummaryFr, ev.SummaryEn)));
                }
                sb.AppendLine();
            }

            if (res.Fixes != null && res.Fixes.Count > 0)
            {
                sb.AppendLine("=== CURRENT REGISTRY PROTECTIONS ===");
                foreach (var f in res.Fixes)
                    sb.AppendLine(string.Format("- [{0}] ({1}) {2} => {3}", f.Id, f.Status, f.Title, f.CurrentValue));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Streams an answer from the configured provider. <paramref name="onDelta"/> receives text chunks
        /// (on a background thread). Never throws: errors come back as a readable AiReply.
        /// </summary>
        public static async Task<AiReply> StreamAsync(AiProviderType provider, string apiKey, string model, string prompt, Action<string> onDelta, CancellationToken ct)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                switch (provider)
                {
                    case AiProviderType.Claude:
                        if (string.IsNullOrWhiteSpace(apiKey)) return MissingKey();
                        return await ClaudeAdvisor.StreamAsync(apiKey, model, prompt, onDelta, ct).ConfigureAwait(false);
                    case AiProviderType.Gemini:
                        if (string.IsNullOrWhiteSpace(apiKey)) return MissingKey();
                        return await Task.Run(() => StreamGemini(apiKey.Trim(), prompt, onDelta, ct), ct).ConfigureAwait(false);
                    case AiProviderType.OllamaLocal:
                        return await Task.Run(() => StreamOllama(model, prompt, onDelta, ct), ct).ConfigureAwait(false);
                    default:
                        return new AiReply { Ok = false, Text = "Unsupported AI provider." };
                }
            }
            catch (OperationCanceledException)
            {
                return new AiReply { Ok = false, Text = Localizer.T("Requête annulée.", "Request cancelled.") };
            }
            catch (WebException wex) when (provider == AiProviderType.OllamaLocal)
            {
                Log.Warn(wex, "Ollama");
                return new AiReply
                {
                    Ok = false,
                    Text = Localizer.T(
                        "Impossible de joindre Ollama sur http://127.0.0.1:11434. Lancez « ollama serve » et téléchargez le modèle (« ollama pull " + (model ?? "llama3.2") + " »).",
                        "Could not reach Ollama at http://127.0.0.1:11434. Start \"ollama serve\" and pull the model (\"ollama pull " + (model ?? "llama3.2") + "\").")
                };
            }
            catch (WebException wex)
            {
                Log.Warn(wex, provider.ToString());
                string body = ReadWebExceptionBody(wex);
                return new AiReply { Ok = false, Text = "HTTP : " + wex.Message + (string.IsNullOrEmpty(body) ? "" : "\r\n" + body) };
            }
            catch (Exception ex)
            {
                // SDK errors (authentication, rate limit, overload) carry a readable message.
                Log.Warn(ex, provider.ToString());
                return new AiReply { Ok = false, Text = ex.GetType().Name.Replace("Exception", "") + " : " + ex.Message };
            }
        }

        private static AiReply MissingKey()
        {
            return new AiReply
            {
                Ok = false,
                Text = Localizer.T(
                    "Aucune clé API configurée. Ajoutez-la dans Paramètres › Assistant IA, choisissez Ollama (local, sans clé), ou utilisez « Copier le prompt ».",
                    "No API key configured. Add it in Settings › AI assistant, choose Ollama (local, no key), or use \"Copy prompt\".")
            };
        }

        // ==================== Google Gemini (REST, server-sent events) ====================
        private static string _geminiModel;

        /// <summary>Picks the newest "flash" model that supports generateContent on this key (cached).</summary>
        private static string ResolveGeminiModel(string apiKey)
        {
            if (_geminiModel != null) return _geminiModel;
            string fallback = "gemini-2.5-flash";
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://generativelanguage.googleapis.com/v1beta/models?pageSize=200");
                req.Headers["x-goog-api-key"] = apiKey;
                req.Timeout = 15000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                {
                    var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, object>>(sr.ReadToEnd());
                    var models = root != null && root.ContainsKey("models") ? root["models"] as IList : null;
                    var names = new List<string>();
                    if (models != null)
                    {
                        foreach (var m in models)
                        {
                            var md = m as IDictionary<string, object>;
                            if (md == null || !md.ContainsKey("name")) continue;
                            string name = Convert.ToString(md["name"]).Replace("models/", "");
                            var methods = md.ContainsKey("supportedGenerationMethods") ? md["supportedGenerationMethods"] as IList : null;
                            bool gen = methods != null && methods.Cast<object>().Any(x => Convert.ToString(x) == "generateContent");
                            if (gen && name.StartsWith("gemini-") && name.Contains("flash")
                                && !name.Contains("lite") && !name.Contains("exp") && !name.Contains("preview") && !name.Contains("image") && !name.Contains("tts") && !name.Contains("live"))
                                names.Add(name);
                        }
                    }
                    // Highest version number first ("gemini-3-flash" > "gemini-2.5-flash").
                    _geminiModel = names.OrderByDescending(n => VersionOf(n)).ThenBy(n => n.Length).FirstOrDefault() ?? fallback;
                }
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Gemini model list");
                _geminiModel = fallback;
            }
            return _geminiModel;
        }

        private static double VersionOf(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, @"gemini-(\d+(?:\.\d+)?)");
            double v;
            return m.Success && double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static AiReply StreamGemini(string apiKey, string prompt, Action<string> onDelta, CancellationToken ct)
        {
            string model = ResolveGeminiModel(apiKey);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var body = new Dictionary<string, object>
            {
                { "contents", new object[] { new Dictionary<string, object> { { "role", "user" }, { "parts", new object[] { new Dictionary<string, object> { { "text", prompt } } } } } } },
                { "generationConfig", new Dictionary<string, object> { { "maxOutputTokens", 8192 } } }
            };
            var req = (HttpWebRequest)WebRequest.Create("https://generativelanguage.googleapis.com/v1beta/models/" + model + ":streamGenerateContent?alt=sse");
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Headers["x-goog-api-key"] = apiKey;
            req.Timeout = 120000;
            req.ReadWriteTimeout = 120000;
            byte[] payload = Encoding.UTF8.GetBytes(ser.Serialize(body));
            using (Stream rs = req.GetRequestStream()) rs.Write(payload, 0, payload.Length);

            var text = new StringBuilder();
            using (ct.Register(() => req.Abort()))
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                    var chunk = ser.Deserialize<Dictionary<string, object>>(line.Substring(5).Trim());
                    string piece = GeminiChunkText(chunk);
                    if (piece.Length == 0) continue;
                    text.Append(piece);
                    if (onDelta != null) onDelta(piece);
                }
            }
            return new AiReply { Ok = text.Length > 0, Text = text.Length > 0 ? text.ToString().Trim() : "Gemini (" + model + ") : réponse vide / empty answer." };
        }

        private static string GeminiChunkText(Dictionary<string, object> chunk)
        {
            var sb = new StringBuilder();
            var candidates = chunk != null && chunk.ContainsKey("candidates") ? chunk["candidates"] as IList : null;
            if (candidates == null || candidates.Count == 0) return "";
            var c0 = candidates[0] as IDictionary<string, object>;
            var content = c0 != null && c0.ContainsKey("content") ? c0["content"] as IDictionary<string, object> : null;
            var parts = content != null && content.ContainsKey("parts") ? content["parts"] as IList : null;
            if (parts == null) return "";
            foreach (var p in parts)
            {
                var pd = p as IDictionary<string, object>;
                if (pd != null && pd.ContainsKey("text") && pd["text"] != null) sb.Append(pd["text"]);
            }
            return sb.ToString();
        }

        // ==================== Ollama (local, NDJSON stream) ====================
        private static AiReply StreamOllama(string modelName, string prompt, Action<string> onDelta, CancellationToken ct)
        {
            string model = string.IsNullOrWhiteSpace(modelName) ? "llama3.2" : modelName.Trim();
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var req = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:11434/api/generate");
            req.Method = "POST";
            req.ContentType = "application/json; charset=utf-8";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 300000;
            byte[] payload = Encoding.UTF8.GetBytes(ser.Serialize(new Dictionary<string, object> { { "model", model }, { "prompt", prompt }, { "stream", true } }));
            using (Stream rs = req.GetRequestStream()) rs.Write(payload, 0, payload.Length);

            var text = new StringBuilder();
            using (ct.Register(() => req.Abort()))
            using (var resp = (HttpWebResponse)req.GetResponse())
            using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
            {
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    if (line.Length == 0) continue;
                    var obj = ser.Deserialize<Dictionary<string, object>>(line);
                    if (obj == null) continue;
                    if (obj.ContainsKey("error")) return new AiReply { Ok = false, Text = "Ollama : " + obj["error"] };
                    if (obj.ContainsKey("response") && obj["response"] != null)
                    {
                        string piece = obj["response"].ToString();
                        text.Append(piece);
                        if (onDelta != null) onDelta(piece);
                    }
                }
            }
            return new AiReply { Ok = text.Length > 0, Text = text.ToString().Trim() };
        }

        private static string ReadWebExceptionBody(WebException wex)
        {
            try
            {
                if (wex.Response != null)
                {
                    using (Stream s = wex.Response.GetResponseStream())
                    using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                    {
                        string text = sr.ReadToEnd();
                        if (text.Length > 500) text = text.Substring(0, 500) + "...";
                        return text;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return "";
        }
    }
}