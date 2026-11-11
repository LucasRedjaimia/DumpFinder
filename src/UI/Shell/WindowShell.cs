using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    /// <summary>
    /// Custom window chrome: content extends into the title bar, Windows 11 Mica backdrop
    /// when available (22H2+), rounded corners, and a solid fallback everywhere else.
    /// Caption buttons are drawn by the app (see TitleBar).
    /// </summary>
    public static class WindowShell
    {
        public const double TitleBarHeight = 44;

        /// <summary>Set by the --screenshot mode: RenderTargetBitmap cannot capture DWM backdrops.</summary>
        public static bool ForceSolidBackground;

        private static int? _build;

        public static int WindowsBuild
        {
            get
            {
                if (_build == null)
                {
                    int b = 0;
                    try
                    {
                        using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                        {
                            if (k != null) int.TryParse(Convert.ToString(k.GetValue("CurrentBuildNumber")), out b);
                        }
                    }
                    catch (Exception ex) { Log.Warn(ex); }
                    _build = b;
                }
                return _build.Value;
            }
        }

        public static bool IsWindows11 { get { return WindowsBuild >= 22000; } }

        /// <summary>DWMWA_SYSTEMBACKDROP_TYPE is official from 22H2 (22621).</summary>
        public static bool MicaSupported
        {
            get { return !ForceSolidBackground && WindowsBuild >= 22621 && SystemParameters.HighContrast == false; }
        }

        public static bool UsingMica { get; private set; }

        /// <summary>
        /// With an extended glass frame DWM always paints the real caption buttons (which also gives
        /// Snap Layouts on hover), so the app only draws its own buttons in the solid fallback.
        /// Decided once at install time.
        /// </summary>
        public static bool NativeCaptionButtons { get; private set; }

        /// <summary>Installs WindowChrome. Call before the window is shown.</summary>
        public static void Install(Window win)
        {
            NativeCaptionButtons = MicaSupported;
            var chrome = new WindowChrome
            {
                CaptionHeight = TitleBarHeight,
                ResizeBorderThickness = new Thickness(6),
                CornerRadius = new CornerRadius(0),
                GlassFrameThickness = NativeCaptionButtons ? new Thickness(-1) : new Thickness(0),
                UseAeroCaptionButtons = NativeCaptionButtons
            };
            WindowChrome.SetWindowChrome(win, chrome);

            win.SourceInitialized += (s, e) => OnSourceInitialized(win);
            win.StateChanged += (s, e) => UpdateMaximizedPadding(win);
        }

        private static void OnSourceInitialized(Window win)
        {
            IntPtr hwnd = new WindowInteropHelper(win).Handle;
            if (hwnd == IntPtr.Zero) return;

            HideNativeCaption(hwnd);

            if (IsWindows11)
            {
                int round = DWMWCP_ROUND;
                DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
            }

            ApplyBackdrop(win);
            UpdateMaximizedPadding(win);
        }

        /// <summary>(Re)applies Mica or the solid fallback, e.g. after a theme switch.</summary>
        public static void ApplyBackdrop(Window win)
        {
            IntPtr hwnd = new WindowInteropHelper(win).Handle;
            bool dark = ThemeManager.IsDark;

            if (hwnd != IntPtr.Zero)
            {
                int useDark = dark ? 1 : 0;
                if (DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref useDark, sizeof(int)) != 0)
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref useDark, sizeof(int));
            }

            bool mica = false;
            if (MicaSupported && hwnd != IntPtr.Zero)
            {
                try
                {
                    HwndSource src = HwndSource.FromHwnd(hwnd);
                    if (src != null && src.CompositionTarget != null)
                        src.CompositionTarget.BackgroundColor = Colors.Transparent;

                    var margins = new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 };
                    DwmExtendFrameIntoClientArea(hwnd, ref margins);

                    int backdrop = DWMSBT_MAINWINDOW;
                    mica = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) == 0;
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Mica");
                    mica = false;
                }
            }

            UsingMica = mica;
            if (mica)
                win.Background = Brushes.Transparent;
            else
                win.SetResourceReference(Window.BackgroundProperty, "BgShell");
        }

        /// <summary>A maximized chrome-less window overhangs the screen by the resize frame: pad it back in.</summary>
        private static void UpdateMaximizedPadding(Window win)
        {
            var root = win.Content as FrameworkElement;
            if (root == null) return;
            if (win.WindowState == WindowState.Maximized)
            {
                int frame = GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER);
                double scale = 1.0;
                var src = PresentationSource.FromVisual(win);
                if (src != null && src.CompositionTarget != null) scale = src.CompositionTarget.TransformFromDevice.M11;
                double px = frame * scale;
                root.Margin = new Thickness(px);
            }
            else
            {
                root.Margin = new Thickness(0);
            }
        }

        private static void HideNativeCaption(IntPtr hwnd)
        {
            try
            {
                // Hide the native title text and icon only: the system menu (Alt+Space) and,
                // in Mica mode, the native caption buttons stay.
                var opts = new WTA_OPTIONS
                {
                    dwFlags = WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON,
                    dwMask = WTNCA_NODRAWCAPTION | WTNCA_NODRAWICON | WTNCA_NOSYSMENU
                };
                SetWindowThemeAttribute(hwnd, WINDOWTHEMEATTRIBUTETYPE.WTA_NONCLIENT, ref opts, (uint)Marshal.SizeOf(typeof(WTA_OPTIONS)));
            }
            catch (Exception ex) { Log.Warn(ex); }
        }

        // ---------------- interop ----------------
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;
        private const int DWMWCP_ROUND = 2;
        private const int DWMSBT_MAINWINDOW = 2; // Mica
        private const int SM_CXSIZEFRAME = 32;
        private const int SM_CXPADDEDBORDER = 92;
        private const uint WTNCA_NODRAWCAPTION = 0x1;
        private const uint WTNCA_NODRAWICON = 0x2;
        private const uint WTNCA_NOSYSMENU = 0x4;

        [StructLayout(LayoutKind.Sequential)]
        private struct MARGINS { public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight; }

        [StructLayout(LayoutKind.Sequential)]
        private struct WTA_OPTIONS { public uint dwFlags; public uint dwMask; }

        private enum WINDOWTHEMEATTRIBUTETYPE : uint { WTA_NONCLIENT = 1 }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("dwmapi.dll")]
        private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref MARGINS margins);

        [DllImport("uxtheme.dll")]
        private static extern int SetWindowThemeAttribute(IntPtr hwnd, WINDOWTHEMEATTRIBUTETYPE attr, ref WTA_OPTIONS opts, uint size);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int index);
    }
}
