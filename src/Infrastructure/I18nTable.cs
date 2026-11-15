using System;
using System.Collections.Generic;
using System.Linq;

namespace WinCrashDoctor
{
    /// <summary>
    /// Translations of engine texts that are built in FR + EN only (stop-code explanations, fix results, driver
    /// explanations, report headings). Key = the exact English text, in Locales/&lt;code&gt;.engine.json
    /// (en.engine.json lists every known English text: it is the template for new languages).
    /// </summary>
    public static class I18nTable
    {
        public static string Translate(string en, AppLang lang)
        {
            if (en == null || lang == AppLang.EN || lang == AppLang.FR) return null;
            Dictionary<string, string> map = Localizer.Table(Localizer.Code(lang) + ".engine");
            if (map.Count == 0) return null;

            string t;
            if (map.TryGetValue(en, out t) && !string.IsNullOrEmpty(t)) return t;

            // Composite texts ("<stop-code explanation> <driver explanation>"): translate the known
            // leading sentence block, then the rest (kept in English if unknown).
            foreach (string k in KeysLongestFirst(map))
            {
                if (k.Length < 40 || en.Length <= k.Length + 1 || en[k.Length] != ' ' || !en.StartsWith(k, StringComparison.Ordinal))
                    continue;
                string head = map[k];
                if (string.IsNullOrEmpty(head)) return null;
                string rest = en.Substring(k.Length + 1);
                return head + " " + (Translate(rest, lang) ?? rest);
            }
            return null;
        }

        public static int Count { get { return Localizer.Table("en.engine").Count; } }

        private static readonly Dictionary<Dictionary<string, string>, string[]> Sorted = new Dictionary<Dictionary<string, string>, string[]>();

        private static string[] KeysLongestFirst(Dictionary<string, string> map)
        {
            lock (Sorted)
            {
                string[] keys;
                if (!Sorted.TryGetValue(map, out keys))
                {
                    keys = map.Keys.OrderByDescending(k => k.Length).ToArray();
                    Sorted[map] = keys;
                }
                return keys;
            }
        }
    }
}
