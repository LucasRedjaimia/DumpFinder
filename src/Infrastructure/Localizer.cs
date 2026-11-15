using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace WinCrashDoctor
{
    public static class Localizer
    {
        public static AppLang Current = AppLang.FR;

        public static string AppDataDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DumpFinder");
                if (!Directory.Exists(dir))
                {
                    try
                    {
                        Directory.CreateDirectory(dir);
                        // Seamlessly import existing settings from WinCrashDoctor if present
                        string legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinCrashDoctor");
                        string legacySettings = Path.Combine(legacyDir, "settings.json");
                        string targetSettings = Path.Combine(dir, "settings.json");
                        if (File.Exists(legacySettings) && !File.Exists(targetSettings))
                            File.Copy(legacySettings, targetSettings);
                    }
                    catch (Exception ex) { Log.Warn(ex); }
                }
                return dir;
            }
        }

        private static string SettingsPath
        {
            get { return Path.Combine(AppDataDir, "settings.json"); }
        }

        public static AppSettings CachedSettings = new AppSettings();

        /// <summary>Set by the --screenshot harness: settings are read but never written.</summary>
        public static bool ReadOnlySettings;

        public static AppSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath, Encoding.UTF8);
                    var ser = new JavaScriptSerializer();
                    var s = ser.Deserialize<AppSettings>(json);
                    if (s != null)
                    {
                        if (string.IsNullOrEmpty(s.OllamaModel)) s.OllamaModel = "llama3.2";
                        s.AiApiKey = SecretStore.Unprotect(s.AiApiKeyProtected);

                        // Migration: settings written by v5/v6.0 held the key in clear text.
                        var raw = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                        object legacy;
                        if (raw != null && raw.TryGetValue("AiApiKey", out legacy) && legacy is string && ((string)legacy).Length > 0)
                        {
                            if (string.IsNullOrEmpty(s.AiApiKey)) s.AiApiKey = (string)legacy;
                            CachedSettings = s;
                            SaveFullSettings(s); // rewrites the file without the clear-text key
                        }
                        CachedSettings = s;
                        return s;
                    }
                }
            }
            catch (Exception ex)
            {
                // Unreadable settings: keep a copy (it holds the encrypted API key) before defaults overwrite it.
                Log.Warn(ex);
                AtomicFile.Quarantine(SettingsPath);
            }

            CachedSettings = new AppSettings
            {
                Language = (int)DetectSystemLanguage(),
                Theme = (int)AppThemeMode.SystemAuto,
                TraySentinelEnabled = false,
                AiProvider = (int)AiProviderType.Gemini,
                AiApiKey = "",
                OllamaModel = "llama3.2"
            };
            return CachedSettings;
        }

        public static void SaveSettings(AppLang lang, AppThemeMode theme, bool sentinel)
        {
            if (CachedSettings == null) CachedSettings = new AppSettings();
            CachedSettings.Language = (int)lang;
            CachedSettings.Theme = (int)theme;
            CachedSettings.TraySentinelEnabled = sentinel;
            SaveFullSettings(CachedSettings);
        }

        public static void SaveFullSettings(AppSettings settings)
        {
            if (ReadOnlySettings) { CachedSettings = settings; return; }
            try
            {
                CachedSettings = settings;
                if (string.IsNullOrEmpty(settings.AiApiKey))
                    settings.AiApiKeyProtected = null;
                else
                {
                    // If encryption fails, keep the previously saved ciphertext rather than silently dropping the key.
                    string protectedKey = SecretStore.Protect(settings.AiApiKey);
                    if (protectedKey != null) settings.AiApiKeyProtected = protectedKey;
                    else Log.Warn("API key could not be encrypted; previous saved key kept");
                }
                var ser = new JavaScriptSerializer();
                string json = ser.Serialize(settings);
                AtomicFile.WriteAllText(SettingsPath, json, Encoding.UTF8);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        // ==================== Languages ====================
        // One row per AppLang value (same order): code (= Locales/<code>.json), culture, native name.
        private static readonly string[] Codes    = { "fr", "en", "es", "de", "it", "pt", "pl", "nl", "tr", "ru", "uk", "zh", "ja", "ko" };
        private static readonly string[] Cultures = { "fr-FR", "en-US", "es-ES", "de-DE", "it-IT", "pt-BR", "pl-PL", "nl-NL", "tr-TR", "ru-RU", "uk-UA", "zh-CN", "ja-JP", "ko-KR" };
        private static readonly string[] Natives  = { "Français", "English", "Español", "Deutsch", "Italiano", "Português", "Polski", "Nederlands", "Türkçe", "Русский", "Українська", "简体中文", "日本語", "한국어" };

        /// <summary>Two-letter code of a language ("fr", "en"…), also the name of its Locales file.</summary>
        public static string Code(AppLang lang)
        {
            int i = (int)lang;
            return i >= 0 && i < Codes.Length ? Codes[i] : "en";
        }

        /// <summary>Language for a two-letter code, or null when the app does not have it.</summary>
        public static AppLang? FromCode(string code)
        {
            int i = Array.IndexOf(Codes, (code ?? "").ToLowerInvariant());
            return i >= 0 ? (AppLang?)(AppLang)i : null;
        }

        public static AppLang DetectSystemLanguage()
        {
            // "zh-TW" / "zh-HK" also get Simplified Chinese: closer to the reader than English.
            return FromCode(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) ?? AppLang.EN;
        }

        /// <summary>Culture matching the app language (dates follow the UI, not the Windows locale).</summary>
        public static CultureInfo Culture
        {
            get
            {
                int i = (int)Current;
                return CultureInfo.GetCultureInfo(i >= 0 && i < Cultures.Length ? Cultures[i] : "en-US");
            }
        }

        public static string LanguageDisplayName(AppLang lang)
        {
            int i = (int)lang;
            return i >= 0 && i < Natives.Length ? Natives[i] : "English";
        }

        public static bool IsCjk(AppLang lang) { return lang == AppLang.ZH || lang == AppLang.JA || lang == AppLang.KO; }

        /// <summary>Short day + month in the app language: "3 Nov", "3 нояб.", "11月3日", "11월 3일".</summary>
        public static string DayMonth(DateTime d)
        {
            return d.ToString(IsCjk(Current) ? Culture.DateTimeFormat.MonthDayPattern : "d MMM", Culture);
        }

        /// <summary>Short date with year in the app language: "3 Nov 2025", "2025/11/3", "2025. 11. 3."…</summary>
        public static string DayMonthYear(DateTime d)
        {
            return d.ToString(IsCjk(Current) ? Culture.DateTimeFormat.ShortDatePattern : "d MMM yyyy", Culture);
        }

        // ==================== Strings (Locales/<code>.json, embedded) ====================
        private static readonly object TablesSync = new object();
        private static readonly Dictionary<string, Dictionary<string, string>> Tables = new Dictionary<string, Dictionary<string, string>>();

        /// <summary>Embedded "Locales.&lt;name&gt;.json" as a dictionary (empty when missing or unreadable). Cached.</summary>
        internal static Dictionary<string, string> Table(string name)
        {
            lock (TablesSync)
            {
                Dictionary<string, string> t;
                if (Tables.TryGetValue(name, out t)) return t;
                t = new Dictionary<string, string>(StringComparer.Ordinal);
                try
                {
                    using (Stream s = typeof(Localizer).Assembly.GetManifestResourceStream("Locales." + name + ".json"))
                    {
                        if (s != null)
                        {
                            using (var r = new StreamReader(s, Encoding.UTF8))
                            {
                                var parsed = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Dictionary<string, string>>(r.ReadToEnd());
                                if (parsed != null) t = new Dictionary<string, string>(parsed, StringComparer.Ordinal);
                            }
                        }
                    }
                }
                catch (Exception ex) { Log.Warn(ex, "Locales." + name); }
                Tables[name] = t;
                return t;
            }
        }

        // ==================== Pseudo-locale (layout testing) ====================
        /// <summary>
        /// "qps" pseudo-language, only from "--screenshot --lang qps": English text with accented letters and
        /// about 40% extra length, wrapped in [ ]. A missing "]" on a capture means the text is cut off; any
        /// plain-ASCII sentence left means a text that bypasses the Locales files. Never offered in Settings.
        /// </summary>
        public static bool Pseudo;

        private const string PseudoFrom = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string PseudoTo   = "àƀçđéƒĝĥîĵķĺɱñöþǫŕšţûṽŵẋýžÀßÇĐÉƑĜĤÎĴĶĹṀÑÖÞǪŔŠŢÛṼŴẊÝŽ";

        internal static string PseudoText(string s)
        {
            var sb = new StringBuilder(s.Length * 2);
            foreach (string seg in s.Split('|'))
            {
                if (sb.Length > 0) sb.Append('|');
                // Keep a plural category label ("one:", "few:"…) readable for P().
                int start = 0;
                int colon = seg.IndexOf(':');
                if (colon > 0 && Array.IndexOf(new[] { "zero", "one", "two", "few", "many", "other" }, seg.Substring(0, colon).Trim()) >= 0)
                {
                    sb.Append(seg, 0, colon + 1);
                    start = colon + 1;
                    while (start < seg.Length && seg[start] == ' ') sb.Append(seg[start++]);
                }
                string body = seg.Substring(start);
                string trail = body.Length - body.TrimEnd().Length > 0 ? body.Substring(body.TrimEnd().Length) : "";
                body = body.TrimEnd();
                // A leading "✓ " / "⚡ " stays first: DfButton turns it into an icon.
                string glyph, rest;
                if (Glyph.TrySplitLegacyPrefix(body, out glyph, out rest))
                {
                    sb.Append(body, 0, body.Length - rest.Length);
                    body = rest;
                }
                int letters = 0;
                sb.Append('[');
                int depth = 0;
                foreach (char c in body)
                {
                    if (c == '{') depth++;
                    if (depth == 0)
                    {
                        int k = PseudoFrom.IndexOf(c);
                        if (k >= 0) { sb.Append(PseudoTo[k]); letters++; continue; }
                    }
                    else if (c == '}') depth--;
                    sb.Append(c);
                }
                if (letters > 0) sb.Append(' ').Append('·', Math.Max(1, (letters * 2 + 4) / 5));
                sb.Append(']').Append(trail);
            }
            return sb.ToString();
        }

        /// <summary>UI text by key, in the current language; falls back to English, then to the key itself.</summary>
        public static string S(string key)
        {
            return Text(Current, key);
        }

        /// <summary>Text by key in a given language (falls back to English, then to the key).</summary>
        public static string Text(AppLang lang, string key)
        {
            string v;
            if (Pseudo && lang == AppLang.EN && Table("en").TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return PseudoText(v);
            if (Table(Code(lang)).TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            if (lang != AppLang.EN && Table("en").TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) return v;
            Log.Warn("Missing text: " + key);
            return key;
        }

        /// <summary>UI text by key with {0}, {1}… placeholders filled in (same formatting as the former concatenations).</summary>
        public static string F(string key, params object[] args)
        {
            return SafeFormat(S(key), key, args);
        }

        // ==================== Deferred texts (engine) ====================
        // The diagnostic engine builds texts while scanning, long before (and sometimes in another process than)
        // they are shown, and the same text is shown in the UI language, sent in English to MCP / AI / exports, and
        // kept in history. Instead of a finished sentence it stores a reference: key + values, rendered on display.
        // Format: \u0001key(\u0002arg)*\u0003 — references may be concatenated with plain text and nested in args.
        private const char RefStart = '\u0001', RefArg = '\u0002', RefEnd = '\u0003', RefTyped = '\u0004';

        /// <summary>Reference to a text, rendered later with <see cref="Render(string)"/> in the language of display.</summary>
        public static string Ref(string key, params object[] args)
        {
            var sb = new StringBuilder();
            sb.Append(RefStart).Append(key);
            if (args != null)
            {
                foreach (object a in args) sb.Append(RefArg).Append(EncodeArg(a));
            }
            return sb.Append(RefEnd).ToString();
        }

        /// <summary>True when the string holds at least one deferred reference.</summary>
        public static bool IsRef(string s) { return s != null && s.IndexOf(RefStart) >= 0; }

        /// <summary>Text with every reference rendered in the current language (plain text is returned as is).</summary>
        public static string Render(string s) { return Render(s, Current); }

        /// <summary>English rendering (MCP, AI prompts, exports, logs).</summary>
        public static string RenderEn(string s) { return Render(s, AppLang.EN); }

        public static string Render(string s, AppLang lang)
        {
            if (!IsRef(s)) return s;
            try
            {
                int i = 0;
                return RenderSpan(s, ref i, lang, false);
            }
            catch (Exception ex)
            {
                Log.Warn(ex, "Bad text reference");
                // Never show control characters: drop them.
                var sb = new StringBuilder();
                foreach (char c in s) if (c > RefTyped) sb.Append(c);
                return sb.ToString();
            }
        }

        /// <summary>Renders plain text and references until the end, or until an argument separator / end of the enclosing reference.</summary>
        private static string RenderSpan(string s, ref int i, AppLang lang, bool insideArg)
        {
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i];
                if (insideArg && (c == RefArg || c == RefEnd)) break;
                if (c == RefStart) { i++; sb.Append(RenderRef(s, ref i, lang)); continue; }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private static string RenderRef(string s, ref int i, AppLang lang)
        {
            int keyStart = i;
            while (i < s.Length && s[i] != RefArg && s[i] != RefEnd) i++;
            string key = s.Substring(keyStart, i - keyStart);
            var args = new List<object>();
            while (i < s.Length && s[i] == RefArg)
            {
                i++;
                if (i < s.Length && s[i] == RefTyped)
                {
                    int start = i;
                    while (i < s.Length && s[i] != RefArg && s[i] != RefEnd) i++;
                    args.Add(DecodeArg(s.Substring(start, i - start)));
                }
                else args.Add(RenderSpan(s, ref i, lang, true));
            }
            if (i < s.Length && s[i] == RefEnd) i++;
            string template = Text(lang, key);
            if (args.Count == 0) return template;
            try { return string.Format(CultureOf(lang), template, args.ToArray()); }
            catch (FormatException ex) { Log.Warn(ex, "Bad placeholders in text " + key); return template; }
        }

        /// <summary>Numbers and dates keep their type, so "{0:F1}" or "{0:HH:mm}" still format them in the display language.</summary>
        private static string EncodeArg(object a)
        {
            if (a == null) return "";
            if (a is string) return (string)a; // may itself hold (balanced) references: rendered recursively
            var inv = CultureInfo.InvariantCulture;
            if (a is int || a is long || a is short || a is byte || a is sbyte || a is ushort) return RefTyped + "l:" + Convert.ToInt64(a, inv).ToString(inv);
            if (a is uint || a is ulong) return RefTyped + "u:" + Convert.ToUInt64(a, inv).ToString(inv);
            if (a is double || a is float) return RefTyped + "d:" + Convert.ToDouble(a, inv).ToString("R", inv);
            if (a is decimal) return RefTyped + "m:" + ((decimal)a).ToString(inv);
            if (a is DateTime) return RefTyped + "t:" + ((DateTime)a).Ticks.ToString(inv) + ":" + (int)((DateTime)a).Kind;
            return Convert.ToString(a, inv);
        }

        private static object DecodeArg(string enc)
        {
            var inv = CultureInfo.InvariantCulture;
            string v = enc.Length > 3 ? enc.Substring(3) : "";
            switch (enc.Length > 1 ? enc[1] : ' ')
            {
                case 'l': long l; return long.TryParse(v, NumberStyles.Integer, inv, out l) ? (object)l : v;
                case 'u': ulong u; return ulong.TryParse(v, NumberStyles.Integer, inv, out u) ? (object)u : v;
                case 'd': double d; return double.TryParse(v, NumberStyles.Float, inv, out d) ? (object)d : v;
                case 'm': decimal m; return decimal.TryParse(v, NumberStyles.Number, inv, out m) ? (object)m : v;
                case 't':
                    string[] p = v.Split(':');
                    long ticks; int kind;
                    if (p.Length == 2 && long.TryParse(p[0], NumberStyles.Integer, inv, out ticks) && int.TryParse(p[1], out kind))
                        return new DateTime(ticks, (DateTimeKind)kind);
                    return v;
                default: return v;
            }
        }

        private static CultureInfo CultureOf(AppLang lang)
        {
            int i = (int)lang;
            return CultureInfo.GetCultureInfo(i >= 0 && i < Cultures.Length ? Cultures[i] : "en-US");
        }

        /// <summary>
        /// Text that depends on a count. The value holds one form per plural category, separated by "|":
        /// "one: {0} new crash | other: {0} new crashes" (categories: zero, one, two, few, many, other; CLDR rules).
        /// A value without categories is used as is. {0} is the count, later placeholders come from <paramref name="args"/>.
        /// </summary>
        public static string P(string key, long count, params object[] args)
        {
            string raw = S(key);
            string chosen = raw;
            if (raw.IndexOf(':') > 0 && raw.IndexOf('|') > 0)
            {
                var forms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string part in raw.Split('|'))
                {
                    int c = part.IndexOf(':');
                    if (c <= 0) continue;
                    forms[part.Substring(0, c).Trim()] = part.Substring(c + 1).Trim();
                }
                string cat = PluralCategory(Current, count);
                if (!forms.TryGetValue(cat, out chosen) && !forms.TryGetValue("other", out chosen)) chosen = raw;
            }
            var all = new object[(args != null ? args.Length : 0) + 1];
            all[0] = count;
            if (args != null) Array.Copy(args, 0, all, 1, args.Length);
            return SafeFormat(chosen, key, all);
        }

        /// <summary>CLDR cardinal plural category for an integer count.</summary>
        public static string PluralCategory(AppLang lang, long n)
        {
            long a = Math.Abs(n), m10 = a % 10, m100 = a % 100;
            switch (lang)
            {
                case AppLang.FR:
                case AppLang.PT: // pt-BR
                    return a <= 1 ? "one" : "other";
                case AppLang.PL:
                    if (a == 1) return "one";
                    return m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "few" : "many";
                case AppLang.RU:
                case AppLang.UK:
                    if (m10 == 1 && m100 != 11) return "one";
                    return m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "few" : "many";
                case AppLang.ZH:
                case AppLang.JA:
                case AppLang.KO:
                    return "other";
                default: // en, es, de, it, nl, tr
                    return a == 1 ? "one" : "other";
            }
        }

        private static string SafeFormat(string format, string key, object[] args)
        {
            if (args == null || args.Length == 0) return format;
            // Dates and numbers inside a text follow the app language ("{2:d MMM}" → "3 Nov" / "3 нояб." / "11月3日").
            try { return string.Format(Culture, format, args); }
            catch (FormatException ex)
            {
                Log.Warn(ex, "Bad placeholders in text " + key);
                return format;
            }
        }

        /// <summary>
        /// Former inline form (fr, en, es, de, it, pt), kept for compatibility. Prefer <see cref="S"/> with a key in
        /// Locales: languages added later only exist there, and get the English text here.
        /// </summary>
        public static string L(string fr, string en, string es, string de, string it, string pt)
        {
            switch (Current)
            {
                case AppLang.FR: return fr;
                case AppLang.ES: return es;
                case AppLang.DE: return de;
                case AppLang.IT: return it;
                case AppLang.PT: return pt;
                default:         return en;
            }
        }

        /// <summary>
        /// FR/EN engine text built at run time. Other languages look the English text up in
        /// Locales/&lt;code&gt;.engine.json (I18nTable); texts not listed there stay in English.
        /// </summary>
        public static string T(string fr, string en)
        {
            // Deferred engine texts carry the same reference in both fields: render it in the display language.
            if (IsRef(en)) return Render(en);
            if (IsRef(fr)) return Render(fr);
            if (Current == AppLang.FR) return fr ?? en;
            if (Current == AppLang.EN || en == null) return en ?? fr;
            return I18nTable.Translate(en, Current) ?? en;
        }

        public static string CategoryName(CrashCategory cat)
        {
            switch (cat)
            {
                case CrashCategory.BSOD:
                    return Localizer.S("localizer.blue_screen_bsod");
                case CrashCategory.GpuDriver:
                    return Localizer.S("localizer.gpu_driver");
                case CrashCategory.StorageNvme:
                    return Localizer.S("localizer.nvme_storage");
                case CrashCategory.HardwareWhea:
                    return Localizer.S("localizer.hardware_whea");
                case CrashCategory.MemoryExhaustion:
                    return Localizer.S("localizer.memory_ram");
                case CrashCategory.BrowserGpu:
                    return Localizer.S("localizer.browser_gpu");
                case CrashCategory.AppCrash:
                    return Localizer.S("localizer.app_crash");
                case CrashCategory.UnexpectedReboot:
                    return Localizer.S("localizer.abrupt_reboot");
                default:
                    return "Info";
            }
        }

        public static string FixGroupName(FixGroup g)
        {
            switch (g)
            {
                case FixGroup.Gpu:
                    return Localizer.S("localizer.graphics_gpu");
                case FixGroup.BrowserApps:
                    return Localizer.S("localizer.browsers_discord");
                case FixGroup.StoragePcie:
                    return Localizer.S("localizer.nvme_pcie_usb");
                case FixGroup.SystemMaint:
                    return Localizer.S("localizer.system_repair");
                default:
                    return Localizer.S("localizer.all");
            }
        }

        public static string StatusBadgeText(FixStatus status)
        {
            switch (status)
            {
                case FixStatus.Applied:
                    return Localizer.S("system_health_scanner.active");
                case FixStatus.Recommended:
                    return Localizer.S("localizer.recommended");
                case FixStatus.Optional:
                    return Localizer.S("localizer.optional");
                default:
                    return Localizer.S("localizer.tool");
            }
        }

        public static string HealthBadgeText(HealthLevel lvl)
        {
            switch (lvl)
            {
                case HealthLevel.Good:
                    return Localizer.S("localizer.healthy");
                case HealthLevel.Warning:
                    return Localizer.S("localizer.check");
                case HealthLevel.Critical:
                    return Localizer.S("localizer.critical");
                default:
                    return "Info";
            }
        }
    }
}
