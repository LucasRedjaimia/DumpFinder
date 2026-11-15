using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public static class ThemeManager
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public static AppThemeMode CurrentMode = AppThemeMode.SystemAuto;
        public static bool IsDark { get; private set; }

        private static ResourceDictionary _baseControlStyles;

        public static bool IsWindowsUsingLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("AppsUseLightTheme");
                        if (val != null)
                            return Convert.ToInt32(val) != 0;
                    }
                }
            }
            catch (Exception ex) { Log.Warn(ex); }
            return true;
        }

        public static SolidColorBrush Hex(string hex)
        {
            var b = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            b.Freeze();
            return b;
        }

        public static void ApplyTheme(Window win, AppThemeMode mode)
        {
            CurrentMode = mode;
            bool dark = (mode == AppThemeMode.Dark) || (mode == AppThemeMode.SystemAuto && !IsWindowsUsingLightTheme());
            IsDark = dark;

            EnsureControlStylesLoaded(win);

            ResourceDictionary r = win.Resources;
            HighContrastApplied = SystemParameters.HighContrast || ForceHighContrast;
            if (HighContrastApplied)
            {
                // Windows contrast theme: its colours win over the app's own (light / dark choice ignored).
                ApplyHighContrast(r);
                IsDark = Luminance(SystemColors.WindowColor) < 0.4;
                ApplyTitleBarTheme(win, IsDark);
                return;
            }
            if (dark)
            {
                // "Ink" dark palette: neutral-violet greys, indigo brand, coral reserved for faults.
                r["BgShell"]       = Hex("#100F18"); // window backdrop when Mica is unavailable
                r["BgWindow"]      = Hex("#0B0B12"); // content canvas
                r["BgSidebar"]     = Hex("#100F18");
                r["BgCard"]        = Hex("#16151F");
                r["BgSubtle"]      = Hex("#1F1D2B");
                r["BgInput"]       = Hex("#12111A");
                r["BorderCard"]    = Hex("#252334");
                r["BorderStrong"]  = Hex("#353149");

                r["TextPrimary"]   = Hex("#F2F1F7");
                r["TextSecondary"] = Hex("#A6A3B8");
                r["TextMuted"]     = Hex("#8C88A3"); // ≥ 4.5:1 on every dark surface (BgSubtle included)

                r["Accent"]        = Hex("#6A55F0"); // white button text 5:1
                r["AccentSoftBg"]  = Hex("#221C44");
                r["AccentText"]    = Hex("#A99CFF");
                r["OnAccent"]      = Hex("#FFFFFF");

                r["SuccessFg"]     = Hex("#3DD68C");
                r["SuccessSoftBg"] = Hex("#0E2A1E");
                r["SuccessBorder"] = Hex("#1A5A3D");

                r["WarnFg"]        = Hex("#F5B544");
                r["WarnSoftBg"]    = Hex("#2C2210");
                r["WarnBorder"]    = Hex("#6B4E14");

                r["DangerFg"]      = Hex("#FF7A6E");
                r["DangerSoftBg"]  = Hex("#33161A");
                r["DangerBorder"]  = Hex("#7A2A30");

                r["HoverOverlay"]  = Hex("#0FFFFFFF");
                r["PressOverlay"]  = Hex("#1AFFFFFF");
                r["FocusRing"]     = Hex("#A99CFF");
                r["ToastBg"]       = Hex("#1F1D2B");
                r["ScrimBg"]       = Hex("#99050409");

                r["ChartBg"]       = Hex("#100F18");
                r["ChartGrid"]     = Hex("#232133");
                r["ChartPower"]    = Hex("#A99CFF");
                r["ChartTemp"]     = Hex("#F5B544");
            }
            else
            {
                r["BgShell"]       = Hex("#EFEDF6");
                r["BgWindow"]      = Hex("#F8F7FC");
                r["BgSidebar"]     = Hex("#EFEDF6");
                r["BgCard"]        = Hex("#FFFFFF");
                r["BgSubtle"]      = Hex("#F1EFF8");
                r["BgInput"]       = Hex("#FAFAFD");
                r["BorderCard"]    = Hex("#E5E2EF");
                r["BorderStrong"]  = Hex("#CFCAE0");

                r["TextPrimary"]   = Hex("#14121F");
                r["TextSecondary"] = Hex("#55516A");
                r["TextMuted"]     = Hex("#66627C"); // ≥ 4.5:1 on BgShell / BgSubtle

                r["Accent"]        = Hex("#5B47F5");
                r["AccentSoftBg"]  = Hex("#ECE9FF");
                r["AccentText"]    = Hex("#4632D8");
                r["OnAccent"]      = Hex("#FFFFFF");

                r["SuccessFg"]     = Hex("#0B7A4C");
                r["SuccessSoftBg"] = Hex("#E9F8F0");
                r["SuccessBorder"] = Hex("#B5E6CD");

                r["WarnFg"]        = Hex("#955800");
                r["WarnSoftBg"]    = Hex("#FFF6E5");
                r["WarnBorder"]    = Hex("#F2D08A");

                r["DangerFg"]      = Hex("#C0302A");
                r["DangerSoftBg"]  = Hex("#FDEEEC");
                r["DangerBorder"]  = Hex("#F5C4BF");

                r["HoverOverlay"]  = Hex("#0A14121F");
                r["PressOverlay"]  = Hex("#1414121F");
                r["FocusRing"]     = Hex("#5B47F5");
                r["ToastBg"]       = Hex("#FFFFFF");
                r["ScrimBg"]       = Hex("#6614121F");

                r["ChartBg"]       = Hex("#FAFAFD");
                r["ChartGrid"]     = Hex("#E5E2EF");
                r["ChartPower"]    = Hex("#5B47F5");
                r["ChartTemp"]     = Hex("#C27A00");
            }

            ApplyTitleBarTheme(win, dark);
        }

        /// <summary>Capture harness only (--high-contrast): use the contrast palette with the current system colours.</summary>
        public static bool ForceHighContrast;

        /// <summary>True when the last ApplyTheme used the Windows contrast theme colours.</summary>
        public static bool HighContrastApplied { get; private set; }

        /// <summary>
        /// Palette from the Windows contrast theme (Settings › Accessibility › Contrast themes): every token maps
        /// to a system colour, so text, links, selection and borders keep the user's chosen pairs. Status colours
        /// (success / warning / danger) are not used to carry meaning there: icons and labels already do.
        /// </summary>
        private static void ApplyHighContrast(ResourceDictionary r)
        {
            Brush window = SystemColors.WindowBrush, text = SystemColors.WindowTextBrush;
            Brush highlight = SystemColors.HighlightBrush, onHighlight = SystemColors.HighlightTextBrush;
            Brush link = SystemColors.HotTrackBrush, button = SystemColors.ControlBrush;

            r["BgShell"] = window; r["BgWindow"] = window; r["BgSidebar"] = window; r["BgCard"] = window;
            r["BgSubtle"] = button; r["BgInput"] = window;
            r["BorderCard"] = text; r["BorderStrong"] = text;

            r["TextPrimary"] = text; r["TextSecondary"] = text; r["TextMuted"] = text;

            r["Accent"] = highlight; r["AccentSoftBg"] = window; r["AccentText"] = link; r["OnAccent"] = onHighlight;

            foreach (string tone in new[] { "Success", "Warn", "Danger" })
            {
                r[tone + "Fg"] = text;
                r[tone + "SoftBg"] = window;
                r[tone + "Border"] = text;
            }

            Color hl = SystemColors.HighlightColor;
            r["HoverOverlay"] = Frozen(new SolidColorBrush(Color.FromArgb(0x40, hl.R, hl.G, hl.B)));
            r["PressOverlay"] = Frozen(new SolidColorBrush(Color.FromArgb(0x70, hl.R, hl.G, hl.B)));
            r["FocusRing"] = text;
            r["ToastBg"] = window;
            Color w = SystemColors.WindowColor;
            r["ScrimBg"] = Frozen(new SolidColorBrush(Color.FromArgb(0xB0, w.R, w.G, w.B)));

            r["ChartBg"] = window; r["ChartGrid"] = SystemColors.GrayTextBrush;
            r["ChartPower"] = highlight; r["ChartTemp"] = link;
        }

        private static Brush Frozen(SolidColorBrush b) { b.Freeze(); return b; }

        private static double Luminance(Color c) { return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0; }

        public static void ApplyTitleBarTheme(Window win, bool dark)
        {
            try
            {
                // Dark caption attribute + Mica / solid backdrop (no-op until the HWND exists).
                WindowShell.ApplyBackdrop(win);
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        private static void EnsureControlStylesLoaded(Window win)
        {
            if (_baseControlStyles != null) return;

            _baseControlStyles = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/DumpFinder;component/UI/Themes/Controls.xaml", UriKind.Absolute)
            };
            win.Resources.MergedDictionaries.Add(_baseControlStyles);
            win.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/DumpFinder;component/UI/Themes/Brand.xaml", UriKind.Absolute)
            });
        }
    }
}
