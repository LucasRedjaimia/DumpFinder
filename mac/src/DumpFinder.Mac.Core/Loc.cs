using System.ComponentModel;
using System.Globalization;
using System.Text.Json;

namespace DumpFinder.Mac.Core;

/// <summary>
/// Interface texts, same format as the Windows Localizer (src/Infrastructure/Localizer.cs):
/// Locales/&lt;code&gt;.json "key": "text", plural values "one: … | other: …", fallback to English.
/// Lookup order: Mac file of the language, shared file of the language, Mac English, shared English, the key.
/// Bindable: {Binding [home.hello], Source={x:Static core:Loc.Instance}} follows language changes.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    // Same order as the Windows AppLang enum and Localizer tables.
    public static readonly string[] Codes = { "fr", "en", "es", "de", "it", "pt", "pl", "nl", "tr", "ru", "uk", "zh", "ja", "ko" };
    static readonly string[] Cultures = { "fr-FR", "en-US", "es-ES", "de-DE", "it-IT", "pt-BR", "pl-PL", "nl-NL", "tr-TR", "ru-RU", "uk-UA", "zh-CN", "ja-JP", "ko-KR" };
    public static readonly string[] Natives = { "Français", "English", "Español", "Deutsch", "Italiano", "Português", "Polski", "Nederlands", "Türkçe", "Русский", "Українська", "简体中文", "日本語", "한국어" };

    public static string Current { get; private set; } = "en";
    public static CultureInfo Culture => CultureInfo.GetCultureInfo(Cultures[Math.Max(0, Array.IndexOf(Codes, Current))]);
    public static bool IsCjk => Current is "zh" or "ja" or "ko";

    public event PropertyChangedEventHandler? PropertyChanged;
    public static event Action? LanguageChanged;

    /// <summary>Indexer used by XAML bindings. Legacy emoji prefixes ("📄 Report") are dropped: icons are drawn apart.</summary>
    public string this[string key] => Plain(S(key));

    public static void SetLanguage(string code)
    {
        if (Array.IndexOf(Codes, code) < 0) code = "en";
        Current = code;
        CultureInfo.CurrentCulture = Culture;
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs("Item[]"));
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(null));
        LanguageChanged?.Invoke();
    }

    public static string SystemLanguage()
    {
        var two = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant();
        return Array.IndexOf(Codes, two) >= 0 ? two : "en";
    }

    public static string S(string key)
    {
        foreach (var table in new[] { "MacLocales." + Current, "Locales." + Current, "MacLocales.en", "Locales.en" })
            if (Table(table).TryGetValue(key, out var v) && !string.IsNullOrEmpty(v)) return v;
        return key;
    }

    /// <summary>S + string.Format in the app culture; a bad placeholder never throws.</summary>
    public static string F(string key, params object[] args) => SafeFormat(S(key), args);

    /// <summary>Plural text: picks the CLDR form of <paramref name="count"/>; {0} is the count.</summary>
    public static string P(string key, long count, params object[] args)
    {
        string raw = S(key), chosen = raw;
        if (raw.IndexOf(':') > 0 && raw.IndexOf('|') > 0)
        {
            var forms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split('|'))
            {
                int c = part.IndexOf(':');
                if (c > 0) forms[part[..c].Trim()] = part[(c + 1)..].Trim();
            }
            if (!forms.TryGetValue(PluralCategory(Current, count), out chosen!) && !forms.TryGetValue("other", out chosen!)) chosen = raw;
        }
        return SafeFormat(chosen, new object[] { count }.Concat(args).ToArray());
    }

    /// <summary>CLDR cardinal plural category (copy of the Windows Localizer.PluralCategory).</summary>
    public static string PluralCategory(string lang, long n)
    {
        long a = Math.Abs(n), m10 = a % 10, m100 = a % 100;
        switch (lang)
        {
            case "fr": case "pt": return a <= 1 ? "one" : "other";
            case "pl":
                if (a == 1) return "one";
                return m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "few" : "many";
            case "ru": case "uk":
                if (m10 == 1 && m100 != 11) return "one";
                return m10 >= 2 && m10 <= 4 && (m100 < 12 || m100 > 14) ? "few" : "many";
            case "zh": case "ja": case "ko": return "other";
            default: return a == 1 ? "one" : "other";
        }
    }

    /// <summary>"3 Nov" in the app language (Windows Localizer.DayMonth).</summary>
    public static string DayMonth(DateTime d) => d.ToString(IsCjk ? Culture.DateTimeFormat.MonthDayPattern : "d MMM", Culture);

    /// <summary>Drops a leading legacy emoji / dingbat ("📄 Report" → "Report"); the Windows app turns them into icons.</summary>
    public static string Plain(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        int i = 0;
        var e = StringInfo.GetTextElementEnumerator(s);
        if (!e.MoveNext()) return s;
        var first = (string)e.GetTextElement();
        int cp = char.ConvertToUtf32(first, 0);
        bool symbol = cp >= 0x2190 && cp <= 0x2BFF || cp >= 0x1F000 || cp == 0x25CF;
        if (!symbol) return s;
        i = first.Length;
        while (i < s.Length && (s[i] == ' ' || s[i] == '️')) i++;
        return s[i..];
    }

    static string SafeFormat(string format, object[] args)
    {
        if (args.Length == 0) return format;
        try { return string.Format(Culture, format, args); }
        catch (FormatException) { return format; }
    }

    static readonly Dictionary<string, Dictionary<string, string>> Tables = new();

    static Dictionary<string, string> Table(string name)
    {
        lock (Tables)
        {
            if (Tables.TryGetValue(name, out var t)) return t;
            t = new Dictionary<string, string>(StringComparer.Ordinal);
            using var s = typeof(Loc).Assembly.GetManifestResourceStream(name + ".json");
            if (s is not null)
            {
                try { t = JsonSerializer.Deserialize<Dictionary<string, string>>(s) ?? t; }
                catch (JsonException) { /* unreadable file: fall back to the next table */ }
            }
            Tables[name] = t;
            return t;
        }
    }
}
