using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>Design tokens that are not colours (type scale, radii, icon font).</summary>
    public static class Ds
    {
        /// <summary>Body text font for the current language (Segoe UI + the matching CJK font first).</summary>
        public static FontFamily UiFont { get { return WithCjk("Segoe UI Variable Text, Segoe UI"); } }
        /// <summary>Title font for the current language.</summary>
        public static FontFamily DisplayFont { get { return WithCjk("Segoe UI Variable Display, Segoe UI"); } }

        private static readonly Dictionary<string, FontFamily> FontCache = new Dictionary<string, FontFamily>();

        /// <summary>
        /// Segoe UI has no Chinese / Japanese / Korean glyphs: WPF then takes them from the next font in the list.
        /// The current language's font comes first, so shared Han characters get the right regional shapes
        /// (Japanese kanji are not drawn with Chinese forms, and the reverse). All Windows 10/11 editions ship them.
        /// </summary>
        private static FontFamily WithCjk(string latin)
        {
            string cjk;
            switch (Localizer.Current)
            {
                case AppLang.JA: cjk = "Yu Gothic UI, Meiryo UI, Microsoft YaHei UI, Malgun Gothic"; break;
                case AppLang.KO: cjk = "Malgun Gothic, Microsoft YaHei UI, Yu Gothic UI"; break;
                default:         cjk = "Microsoft YaHei UI, Yu Gothic UI, Malgun Gothic"; break; // zh, and a fallback for everyone
            }
            string spec = latin + ", " + cjk;
            lock (FontCache)
            {
                FontFamily f;
                if (!FontCache.TryGetValue(spec, out f)) { f = new FontFamily(spec); FontCache[spec] = f; }
                return f;
            }
        }
        public static readonly FontFamily IconFont = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public static readonly FontFamily MonoFont = new FontFamily("Cascadia Mono, Consolas");

        /// <summary>Snaps any legacy font size onto the 7-step scale: 11 / 12 / 13 / 15 / 18 / 24 / 28.</summary>
        public static double Type(double size)
        {
            if (size < 11.5) return 11;
            if (size < 12.4) return 12;
            if (size < 14) return 13;
            if (size < 17) return 15;
            if (size < 20) return 18;
            if (size < 26) return 24;
            return 28;
        }

        public static TextBlock Icon(string glyph, double size = 14)
        {
            return new TextBlock
            {
                Text = glyph,
                FontFamily = IconFont,
                FontSize = size,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
        }
    }

    /// <summary>Segoe Fluent Icons code points used across the app.</summary>
    public static class Glyph
    {
        public const string GlobalNav = "";
        public const string ChevronDown = "";
        public const string Cancel = "";
        public const string Settings = "";
        public const string Search = "";
        public const string Refresh = "";
        public const string CheckMark = "";
        public const string Save = "";
        public const string ChevronLeft = "";
        public const string Globe = "";
        public const string Undo = "";
        public const string Warning = "";
        public const string Home = "";
        public const string Dictionary = "";
        public const string Document = "";
        public const string OpenInNew = "";
        public const string Close = "";
        public const string Copy = "";
        public const string Accept = "";
        public const string Repair = "";
        public const string Minimize = "";
        public const string Maximize = "";
        public const string Restore = "";
        public const string Completed = "";
        public const string Lightning = "";
        public const string Info = "";
        public const string Component = "";
        public const string Health = "";
        public const string Robot = "";
        public const string Diagnostic = "";
        public const string Shield = "";
        public const string ErrorBadge = "";
        public const string Lightbulb = "";
        public const string DevTools = "";
        public const string Video = "";
        public const string Stop = "";
        public const string Speed = "";
        public const string Play = "";

        /// <summary>Legacy emoji / dingbat prefixes in localized labels → Fluent glyphs.</summary>
        public static readonly Dictionary<int, string> FromLegacySymbol = new Dictionary<int, string>
        {
            { 0x2713, CheckMark },   // ✓
            { 0x26A1, Lightning },   // ⚡
            { 0x1F3AF, Component },  // 🎯
            { 0x1F6E1, Shield },     // 🛡
            { 0x1F6E0, Repair },     // 🛠
            { 0x1F4D6, Dictionary }, // 📖
            { 0x1F50D, Search },     // 🔍
            { 0x2699, Settings },    // ⚙
            { 0x1F4A1, Lightbulb },  // 💡
            { 0x1F4CB, Copy },       // 📋
            { 0x26A0, Warning },     // ⚠
            { 0x25C2, ChevronLeft }, // ◂
            { 0x2715, Cancel },      // ✕
            { 0x1F310, Globe },      // 🌐
            { 0x1F9E0, Robot },      // 🧠
            { 0x1F9EA, Diagnostic }, // 🧪
            { 0x27F3, Refresh },     // ⟳
            { 0x1F4C4, Document },   // 📄
            { 0x1F4BE, Save },       // 💾
            { 0x21BA, Undo },        // ↺
            { 0x1F3AC, Video },      // 🎬
            { 0x23F9, Stop },        // ⏹
            { 0x1F525, Lightning },  // 🔥
            { 0x2728, Lightbulb },   // ✨
        };

        /// <summary>
        /// Splits "📋 Copier" into (Copy glyph, "Copier"). Returns false when the text has no known prefix.
        /// </summary>
        public static bool TrySplitLegacyPrefix(string text, out string glyph, out string rest)
        {
            glyph = null;
            rest = text;
            if (string.IsNullOrEmpty(text)) return false;
            int cp;
            int len;
            try
            {
                cp = char.ConvertToUtf32(text, 0);
                len = char.IsSurrogatePair(text, 0) ? 2 : 1;
            }
            catch (ArgumentException)
            {
                return false;
            }
            string g;
            if (!FromLegacySymbol.TryGetValue(cp, out g)) return false;
            int i = len;
            if (i < text.Length && text[i] == '️') i++; // emoji presentation selector
            glyph = g;
            rest = text.Substring(i).TrimStart();
            return true;
        }
    }

    /// <summary>
    /// Button that renders legacy "emoji + label" strings as a Fluent icon + label,
    /// so all existing localized captions get consistent iconography for free.
    /// </summary>
    public class DfButton : Button
    {
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            base.OnContentChanged(oldContent, newContent);
            var s = newContent as string;
            string glyph, rest;
            if (s == null || !Glyph.TrySplitLegacyPrefix(s, out glyph, out rest)) return;

            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            TextBlock icon = Ds.Icon(glyph, Math.Max(12, FontSize));
            icon.Margin = new Thickness(0, 1, rest.Length > 0 ? 7 : 0, 0);
            panel.Children.Add(icon);
            if (rest.Length > 0)
                panel.Children.Add(new TextBlock { Text = rest, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            AutomationNameFrom(rest);
            Content = panel;
        }

        private void AutomationNameFrom(string label)
        {
            System.Windows.Automation.AutomationProperties.SetName(this, label);
        }
    }
}
