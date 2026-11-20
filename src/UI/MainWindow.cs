using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public partial class MainWindow : Window
    {
        // UI State
        private int _activeTab = 0;          // 0 = Overview, 1 = Health & Conflicts, 2 = Fixes, 3 = Stress, 4 = Guide/Report
        private int _selectedRangeMode = 2;  // 0 = Boot, 1 = 3d, 2 = 30d, 3 = All
        private int _eventFilterMode = 0;    // 0 = All, 1 = BSOD, 2 = GPU, 3 = NVMe, 4 = WHEA/RAM, 5 = Apps
        private FixGroup _fixGroupFilter = FixGroup.All;
        private bool _suppressDropdownEvents = false;
        private ScanResult _lastResult;

        // Sidebar Controls
        private Button[] _navButtons = new Button[NavCount];
        private Border[] _navIndicators = new Border[NavCount];
        private TextBlock[] _navLabels = new TextBlock[NavCount];

        // Navigation (v6): 5 sections + Settings
        private const int TabHome = 0, TabCrashes = 1, TabHardware = 2, TabFixes = 3, TabOptimize = 4, TabTools = 5, TabSettings = 6, NavCount = 7;
        private Border _navHealthBadge;
        private TextBlock _lblNavHealthCount;
        private Border _navFixesBadge;
        private TextBlock _lblNavFixesCount;

        private TextBlock _lblPrefLang;
        private ComboBox _cbLanguage;
        private TextBlock _lblPrefTheme;
        private ComboBox _cbTheme;
        private Border _bdHwProfileBadge;
        private TextBlock _lblHwProfileBadge;
        private TextBlock _lblHwGpu;
        private TextBlock _lblHwCpu;
        private Button _btnElevate;

        // Page Containers
        private Grid _pageFixes;
        private Grid _pageStress;
        private Grid _pageGuide;

        // Hardware Adaptive Filtering
        private bool _onlyHardwareRelevantFixes = true;
        private Border _bdHwFixesBanner;
        private TextBlock _lblHwFixesStatus;
        private Button _btnToggleHardwareFixesFilter;

        private bool _onlyHardwareRelevantGuide = true;
        private Border _bdHwGuideBanner;
        private TextBlock _lblHwGuideStatus;
        private Button _btnToggleHardwareGuideFilter;

        // Shared scan controls (hosted by Home / Crashs)
        private ComboBox _cbTimeframe;
        private Button _btnRescan;
        private ComboBox _cbEventCategory;

        // Page 1: Fixes
        private TextBlock _lblFixesTitle;
        private TextBlock _lblFixesSub;
        private Button _btnApplyAllRec;
        private Button[] _fixGroupBtns = new Button[5];
        private StackPanel _fixesListPanel;

        // Page 3: Guide & Report
        private ScrollViewer _svGuide;
        private StackPanel _guideListPanel;
        private Grid _reportSubPanel;
        private Button _btnCopyReport;
        private Button _btnSaveReport;
        private Button _btnExportFullReg;
        private TextBox _txtReportBox;

        // Footer Toast
        private TextBlock _lblToast;

        public MainWindow() : this(Localizer.LoadSettings())
        {
        }

        public MainWindow(AppSettings initialSettings)
        {
            _appSettings = initialSettings ?? Localizer.LoadSettings();
            // Small screens (e.g. 1366x768 at 125 % = ~1093x576 DIP usable): never start, or be
            // forced, larger than the work area, or the status line and Settings become unreachable.
            Rect workArea = SystemParameters.WorkArea;
            MinWidth = Math.Min(1000, Math.Max(400, workArea.Width));
            MinHeight = Math.Min(540, Math.Max(300, workArea.Height));
            Width = Math.Max(MinWidth, Math.Min(1240, workArea.Width));
            Height = Math.Max(MinHeight, Math.Min(820, workArea.Height));
            Title = "Dump Finder";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ApplyLanguageTypography();
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            UseLayoutRounding = true;

            AppThemeMode startTheme = AppThemeMode.SystemAuto;
            if (initialSettings != null && Enum.IsDefined(typeof(AppThemeMode), initialSettings.Theme))
                startTheme = (AppThemeMode)initialSettings.Theme;

            WindowShell.Install(this);
            ThemeManager.ApplyTheme(this, startTheme);
            SetResourceReference(ForegroundProperty, "TextPrimary");

            try
            {
                Icon = System.Windows.Media.Imaging.BitmapFrame.Create(
                    new Uri("pack://application:,,,/DumpFinder;component/Assets/dumpfinder.ico", UriKind.Absolute));
            }
            catch (Exception ex) { Log.Warn(ex); }

            SystemEvents.UserPreferenceChanged += OnWindowsPreferenceChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;

            InitTraySentinel(initialSettings != null && initialSettings.TraySentinelEnabled);
            BuildLayout();
            InstallKeyboardShortcuts();

            Closing += OnWindowClosing;
            Closed += (s, e) => RunExitCleanup();
            if (Application.Current != null)
            {
                // Logoff / shutdown / Application.Shutdown: always a real exit, never "hide to tray".
                Application.Current.SessionEnding += (s, e) => _forceExit = true;
                Application.Current.Exit += (s, e) => RunExitCleanup();
            }
            StateChanged += (s, e) => { if (WindowState == WindowState.Normal) FitToWorkArea(); };

            Loaded += (s, e) =>
            {
                if (_loadedOnce) return;
                _loadedOnce = true;
                FitToWorkArea();
                RunScanAsync();
                StartupUpdateCheck();
            };

            // Leftover test files from an interrupted disk stress test: clean them in the background
            // (here rather than in Loaded so a tray-only start cleans them too).
            Task.Run(() =>
            {
                try { WinCrashDoctor.Stress.DiskStress.SweepOrphans(); }
                catch (Exception ex) { Log.Warn(ex, "DiskStress.SweepOrphans"); }
            });
        }

        private bool _loadedOnce;
        private bool _exitCleanupDone;

        private void OnWindowClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_forceExit && _stressRunner.IsRunning)
            {
                var ans = MessageBox.Show(this,
                    Localizer.S("main_window.a_stability_test_is_running_yes"),
                    "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (ans != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
                // Same as the Stop button: the runner finishes its stage and clears the marker itself.
                _stressRunner.RequestStop();
                if (_btnStopStress != null) _btnStopStress.IsEnabled = false;
                if (_lblStressStatus != null)
                    _lblStressStatus.Text = Localizer.S("main_window.stopping");
            }

            if (_sentinelEnabled && !_forceExit)
            {
                e.Cancel = true;
                Hide();
                if (_traySentinel != null) _traySentinel.ShowMinimizedHintOnce();
                return;
            }

            RunExitCleanup();
        }

        /// <summary>Real exit (window close without Sentinel, tray Quit, Run as admin, logoff): runs once.</summary>
        private void RunExitCleanup()
        {
            if (_exitCleanupDone) return;
            _exitCleanupDone = true;

            try
            {
                SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
                SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
            }
            catch (Exception ex) { Log.Warn(ex, "Exit: SystemEvents"); }

            try
            {
                // Closing the app mid-test is not a crash: drop the "test running" marker.
                if (_stressRunner.IsRunning) { _stressRunner.RequestStop(); History.ClearStressMarker(); }
            }
            catch (Exception ex) { Log.Warn(ex, "Exit: stress"); }

            try { CancelDrawerAi(); }
            catch (Exception ex) { Log.Warn(ex, "Exit: AI"); }

            try
            {
                if (_traySentinel != null)
                {
                    _traySentinel.Dispose();
                    _traySentinel = null;
                }
            }
            catch (Exception ex) { Log.Warn(ex, "Exit: tray"); }
        }

        /// <summary>Keeps the restored window inside the (primary) work area.</summary>
        private void FitToWorkArea()
        {
            if (WindowState != WindowState.Normal) return;
            Rect wa = SystemParameters.WorkArea;
            if (wa.Width <= 0 || wa.Height <= 0) return;
            if (double.IsNaN(Left) || double.IsNaN(Top)) return;
            // SystemParameters.WorkArea is the primary monitor: leave windows on other monitors alone.
            bool onPrimary = Left >= wa.Left - 16 && Left < wa.Right && Top >= wa.Top - 16 && Top < wa.Bottom;
            if (!onPrimary) return;
            if (ActualWidth > wa.Width || Width > wa.Width) Width = Math.Max(MinWidth, wa.Width);
            if (ActualHeight > wa.Height || Height > wa.Height) Height = Math.Max(MinHeight, wa.Height);
            double w = double.IsNaN(Width) ? ActualWidth : Width;
            double h = double.IsNaN(Height) ? ActualHeight : Height;
            if (Left + w > wa.Right) Left = Math.Max(wa.Left, wa.Right - w);
            if (Top + h > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - h);
            if (Left < wa.Left && w <= wa.Width) Left = wa.Left;
            if (Top < wa.Top) Top = wa.Top;
        }

        private void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            // Also when a contrast theme is on: switching between two of them changes the system colours.
            if (ThemeManager.CurrentMode == AppThemeMode.SystemAuto || ThemeManager.HighContrastApplied)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ThemeManager.ApplyTheme(this, ThemeManager.CurrentMode);
                    RefreshDynamicThemeAccents();
                }));
            }
        }

        /// <summary>Contrast theme switched on / off in Windows: re-apply the palette (WPF has already updated SystemColors).</summary>
        private void OnSystemParameterChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != "HighContrast") return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (SystemParameters.HighContrast == ThemeManager.HighContrastApplied) return;
                ThemeManager.ApplyTheme(this, ThemeManager.CurrentMode);
                RefreshDynamicThemeAccents();
            }));
        }

        // Resource binding helpers
        private static T ResBg<T>(T el, string key) where T : FrameworkElement
        {
            if (el is Border) ((Border)(object)el).SetResourceReference(Border.BackgroundProperty, key);
            else if (el is Control) ((Control)(object)el).SetResourceReference(Control.BackgroundProperty, key);
            else if (el is Panel) ((Panel)(object)el).SetResourceReference(Panel.BackgroundProperty, key);
            return el;
        }

        private static T ResFg<T>(T el, string key) where T : FrameworkElement
        {
            if (el is TextBlock) ((TextBlock)(object)el).SetResourceReference(TextBlock.ForegroundProperty, key);
            else if (el is Control) ((Control)(object)el).SetResourceReference(Control.ForegroundProperty, key);
            return el;
        }

        private static T ResBorder<T>(T b, string key) where T : Border
        {
            b.SetResourceReference(Border.BorderBrushProperty, key);
            return b;
        }

        private Brush GetBrush(string key)
        {
            return (Brush)Resources[key];
        }

        // ==================== ROOT SHELL (TITLE BAR + SIDEBAR + MAIN AREA) ====================
        private const double SidebarExpandedWidth = 236;
        private const double SidebarCollapsedWidth = 68;
        private ColumnDefinition _sidebarColumn;
        private Border _sidebar;
        private StackPanel _sidebarBottomStack;
        private TextBlock[] _navIcons = new TextBlock[NavCount];
        private bool _sidebarCollapsed;

        private void BuildLayout()
        {
            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(WindowShell.TitleBarHeight) });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _sidebarColumn = new ColumnDefinition { Width = new GridLength(SidebarExpandedWidth) };
            shell.ColumnDefinitions.Add(_sidebarColumn);
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 0. TITLE BAR (spans both columns; drag area handled by WindowChrome)
            UIElement titleBar = BuildTitleBar();
            Grid.SetRow(titleBar, 0);
            Grid.SetColumnSpan(titleBar, 2);
            shell.Children.Add(titleBar);

            // 1. LEFT SIDEBAR (transparent: sits on the Mica / shell backdrop)
            _sidebar = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(12, 6, 12, 14)
            };

            Grid sideGrid = new Grid();
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Nav
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Hardware summary
            sideGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Settings

            // Navigation: 6 sections (Settings lives at the bottom of the sidebar)
            StackPanel navStack = new StackPanel();
            string[] icons = new[] { Glyph.Home, Glyph.Diagnostic, Glyph.Component, Glyph.Repair, Glyph.Speed, Glyph.DevTools, Glyph.Settings };
            for (int i = 0; i < NavCount; i++)
            {
                int tabIdx = i;
                Grid rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(4) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                Border indicator = ResBg(new Border
                {
                    Width = 3,
                    Height = 16,
                    CornerRadius = new CornerRadius(1.5),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(-8, 0, 0, 0),
                    Visibility = Visibility.Hidden
                }, "Accent");
                _navIndicators[i] = indicator;
                Grid.SetColumn(indicator, 0);
                rowGrid.Children.Add(indicator);

                TextBlock ico = ResFg(Ds.Icon(icons[i], 16), "TextSecondary");
                _navIcons[i] = ico;
                Grid.SetColumn(ico, 1);
                rowGrid.Children.Add(ico);

                TextBlock lbl = ResFg(new TextBlock
                {
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(8, 0, 4, 0)
                }, "TextPrimary");
                _navLabels[i] = lbl;
                Grid.SetColumn(lbl, 2);
                rowGrid.Children.Add(lbl);

                if (i == TabHardware)
                {
                    _navHealthBadge = ResBg(new Border
                    {
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(7, 1, 7, 2),
                        MinWidth = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = Visibility.Collapsed
                    }, "AccentSoftBg");
                    _lblNavHealthCount = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, "AccentText");
                    _navHealthBadge.Child = _lblNavHealthCount;
                    Grid.SetColumn(_navHealthBadge, 3);
                    rowGrid.Children.Add(_navHealthBadge);
                }
                else if (i == TabFixes)
                {
                    _navFixesBadge = ResBg(new Border
                    {
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(7, 1, 7, 2),
                        MinWidth = 20,
                        VerticalAlignment = VerticalAlignment.Center,
                        Visibility = Visibility.Collapsed
                    }, "WarnSoftBg");
                    _lblNavFixesCount = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center }, "WarnFg");
                    _navFixesBadge.Child = _lblNavFixesCount;
                    Grid.SetColumn(_navFixesBadge, 3);
                    rowGrid.Children.Add(_navFixesBadge);
                }

                Button navBtn = CreateNavButton(rowGrid);
                navBtn.Click += (s, e) => SwitchTab(tabIdx);
                _navButtons[i] = navBtn;
                if (i == TabSettings) _navSettingsButton = navBtn;
                else navStack.Children.Add(navBtn);
            }
            // Scrollable on short windows so every section stays reachable. The negative margin /
            // padding pair keeps the active indicator (drawn 8 px left of the button) unclipped.
            navStack.Margin = new Thickness(8, 0, 0, 0);
            ScrollViewer navScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(-8, 0, 0, 0),
                Focusable = false,
                Content = navStack
            };
            Grid.SetRow(navScroll, 0);
            sideGrid.Children.Add(navScroll);

            // Bottom Preferences, Tray Sentinel & Compact Hardware Info
            StackPanel bottomStack = new StackPanel();
            _sidebarBottomStack = bottomStack;

            // Re-trigger Guided Tour Button
            _btnTourGuide = CreateFlatButton("", "AccentSoftBg", "AccentText", "AccentSoftBg", new Thickness(10, 6, 10, 6), 12);
            _btnTourGuide.HorizontalContentAlignment = HorizontalAlignment.Left;
            _btnTourGuide.Margin = new Thickness(0, 0, 0, 12);
            _btnTourGuide.Click += (s, e) => ReplayIntroduction();

            // Language Dropdown
            _lblPrefLang = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4) }, "TextMuted");

            _cbLanguage = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            AppLang[] langs = (AppLang[])Enum.GetValues(typeof(AppLang));
            foreach (AppLang l in langs)
                _cbLanguage.Items.Add(Localizer.LanguageDisplayName(l));
            _cbLanguage.SelectedIndex = (int)Localizer.Current;
            _cbLanguage.SelectionChanged += (s, e) =>
            {
                if (_suppressDropdownEvents || _cbLanguage.SelectedIndex < 0) return;
                SwitchLanguage((AppLang)_cbLanguage.SelectedIndex);
            };

            // Theme Dropdown
            _lblPrefTheme = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(2, 0, 0, 4) }, "TextMuted");

            _cbTheme = new ComboBox { Margin = new Thickness(0, 0, 0, 10) };
            _cbTheme.SelectionChanged += (s, e) =>
            {
                if (_suppressDropdownEvents || _cbTheme.SelectedIndex < 0) return;
                ThemeManager.ApplyTheme(this, (AppThemeMode)_cbTheme.SelectedIndex);
                SaveCurrentPreferences();
                RefreshDynamicThemeAccents();
            };

            // Tray Sentinel 1-Click Toggle Pill
            _btnSentinelToggle = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(10, 6, 10, 6), 12);
            _btnSentinelToggle.HorizontalContentAlignment = HorizontalAlignment.Left;
            _btnSentinelToggle.Margin = new Thickness(0, 0, 0, 12);
            _btnSentinelToggle.Click += (s, e) => ToggleSentinel();

            // Compact Hardware Box
            Border hwBox = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            }, "BgCard"), "BorderCard");
            StackPanel hwStack = new StackPanel();

            _bdHwProfileBadge = ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(7, 2, 7, 3),
                Margin = new Thickness(0, 0, 0, 8),
                HorizontalAlignment = HorizontalAlignment.Left
            }, "AccentSoftBg");
            _lblHwProfileBadge = ResFg(new TextBlock
            {
                Text = "Détection matériel...",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            }, "AccentText");
            _bdHwProfileBadge.Child = _lblHwProfileBadge;
            hwStack.Children.Add(_bdHwProfileBadge);

            _lblHwGpu = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary");
            _lblHwCpu = ResFg(new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary");
            hwStack.Children.Add(_lblHwGpu);
            hwStack.Children.Add(_lblHwCpu);

            _btnElevate = CreateFlatButton("", "WarnSoftBg", "WarnFg", "WarnSoftBg", new Thickness(10, 5, 10, 5), 11);
            _btnElevate.Margin = new Thickness(0, 9, 0, 0);
            _btnElevate.Click += OnElevateClick;
            hwStack.Children.Add(_btnElevate);

            hwBox.Child = hwStack;
            bottomStack.Children.Add(hwBox);

            Grid.SetRow(bottomStack, 1);
            sideGrid.Children.Add(bottomStack);

            _navSettingsButton.Margin = new Thickness(0, 8, 0, 0);
            Grid.SetRow(_navSettingsButton, 2);
            sideGrid.Children.Add(_navSettingsButton);

            _sidebar.Child = sideGrid;
            Grid.SetRow(_sidebar, 1);
            Grid.SetColumn(_sidebar, 0);
            shell.Children.Add(_sidebar);

            // 2. RIGHT MAIN CONTENT: an elevated "canvas" with a rounded top-left corner
            Border canvas = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(12, 0, 0, 0),
                BorderThickness = new Thickness(1, 1, 0, 0)
            }, "BgWindow"), "BorderCard");

            Grid mainArea = new Grid();
            mainArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid pageHost = new Grid { Margin = new Thickness(32, 26, 32, 10), ClipToBounds = false };
            // Legacy builders first: their blocks are re-hosted by the v6 sections below.
            BuildHealthPage();
            BuildFixesPage();
            BuildStressPage();
            BuildWinToolsPage();
            BuildAiPage();
            BuildGuidePage();
            BuildHomePage();
            BuildCrashesPage();
            BuildSettingsPage();
            BuildOptimizePage();
            HostStressInHardware();
            HostDriversInHardware();
            HostGpuCurveInHardware();
            HostReportInTools();
            BuildHistoryView();
            AddReportButtons();

            pageHost.Children.Add(_pageHome);
            pageHost.Children.Add(_pageCrashes);
            pageHost.Children.Add(_pageHealth);
            pageHost.Children.Add(_pageFixes);
            pageHost.Children.Add(_pageOptimize);
            pageHost.Children.Add(_pageWinTools);
            pageHost.Children.Add(_pageSettings);
            Grid.SetRow(pageHost, 0);
            mainArea.Children.Add(pageHost);

            // Quiet status line (neutral progress / "last scan" info). Outcomes go to toasts.
            StackPanel statusLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(32, 4, 32, 10) };
            _statusDot = ResBg(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center }, "TextMuted");
            statusLine.Children.Add(_statusDot);
            _lblToast = ResFg(new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center }, "TextMuted");
            statusLine.Children.Add(_lblToast);
            Grid.SetRow(statusLine, 1);
            mainArea.Children.Add(statusLine);

            canvas.Child = mainArea;
            Grid.SetRow(canvas, 1);
            Grid.SetColumn(canvas, 1);
            shell.Children.Add(canvas);

            _rootGrid = new Grid();
            _rootGrid.Children.Add(shell);
            _rootGrid.Children.Add(BuildToastHost());
            BuildOnboardingOverlay();
            _rootGrid.Children.Add(_onboardingOverlay);
            BuildUpdateOverlay();
            _rootGrid.Children.Add(_updateOverlay);
            Content = _rootGrid;

            PopulateLocalizedDropdowns();
            RefreshStaticLabels();
            UpdateSentinelButtonVisual();
            RefreshSnapshotButtonState();
            ApplySidebarCollapsed(_appSettings != null && _appSettings.SidebarCollapsed, false);
            SwitchTab(0);
        }

        // ==================== PAGE 2: CLEAN 1-CLICK FIXES ====================
        private void BuildFixesPage()
        {
            _pageFixes = new Grid { Visibility = Visibility.Collapsed };
            _pageFixes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Header
            _pageFixes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 1: Hardware Filter Banner
            _pageFixes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: Clean Group Pills
            _pageFixes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 3: Cards

            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
            _lblFixesTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblFixesSub = ResFg(new TextBlock { FontSize = 13, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
            left.Children.Add(_lblFixesTitle);
            left.Children.Add(_lblFixesSub);
            Grid.SetColumn(left, 0);
            hdr.Children.Add(left);

            StackPanel actionsBar = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            _btnCreateRestorePt = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 12.0);
            _btnCreateRestorePt.Height = 34;
            _btnCreateRestorePt.FontWeight = FontWeights.SemiBold;
            _btnCreateRestorePt.Margin = new Thickness(0, 0, 8, 0);
            _btnCreateRestorePt.Click += OnCreateRestorePointClick;
            actionsBar.Children.Add(_btnCreateRestorePt);

            _btnRestoreSnapshot = CreateFlatButton("", "BgCard", "AccentText", "BorderStrong", new Thickness(14, 7, 14, 7), 12.0);
            _btnRestoreSnapshot.Height = 34;
            _btnRestoreSnapshot.FontWeight = FontWeights.SemiBold;
            _btnRestoreSnapshot.Margin = new Thickness(0, 0, 8, 0);
            _btnRestoreSnapshot.Click += OnRestoreSnapshotClick;
            actionsBar.Children.Add(_btnRestoreSnapshot);

            _btnApplyAllRec = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(18, 7, 18, 7), 12.5);
            _btnApplyAllRec.Height = 34;
            _btnApplyAllRec.Click += OnApplyAllRecommendedClick;
            actionsBar.Children.Add(_btnApplyAllRec);

            Grid.SetColumn(actionsBar, 1);
            hdr.Children.Add(actionsBar);

            Grid.SetRow(hdr, 0);
            _pageFixes.Children.Add(hdr);

            // Hardware Adaptive Filter Banner
            _bdHwFixesBanner = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 10)
            }, "AccentSoftBg"), "BorderCard");

            Grid bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _lblHwFixesStatus = ResFg(new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }, "AccentText");
            Grid.SetColumn(_lblHwFixesStatus, 0);
            bannerGrid.Children.Add(_lblHwFixesStatus);

            _btnToggleHardwareFixesFilter = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(11, 4, 11, 4), 11.5);
            _btnToggleHardwareFixesFilter.FontWeight = FontWeights.SemiBold;
            _btnToggleHardwareFixesFilter.Click += (s, e) =>
            {
                _onlyHardwareRelevantFixes = !_onlyHardwareRelevantFixes;
                if (_lastResult != null) RenderFixesCards(_lastResult.Fixes);
            };
            Grid.SetColumn(_btnToggleHardwareFixesFilter, 1);
            bannerGrid.Children.Add(_btnToggleHardwareFixesFilter);

            _bdHwFixesBanner.Child = bannerGrid;
            Grid.SetRow(_bdHwFixesBanner, 1);
            _pageFixes.Children.Add(_bdHwFixesBanner);

            // Segmented Filter Pills
            WrapPanel grpBar = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            FixGroup[] groups = new[] { FixGroup.All, FixGroup.Gpu, FixGroup.BrowserApps, FixGroup.StoragePcie, FixGroup.SystemMaint };
            for (int i = 0; i < groups.Length; i++)
            {
                FixGroup gVal = groups[i];
                Button b = CreateFlatButton("", "BgCard", "TextSecondary", "BorderStrong", new Thickness(13, 6, 13, 6), 12.2);
                b.Margin = new Thickness(0, 0, 7, 4);
                b.FontWeight = FontWeights.SemiBold;
                b.Click += (s, e) =>
                {
                    _fixGroupFilter = gVal;
                    UpdateFixGroupStyles();
                    if (_lastResult != null) RenderFixesCards(_lastResult.Fixes);
                };
                _fixGroupBtns[i] = b;
                grpBar.Children.Add(b);
            }
            Grid.SetRow(grpBar, 2);
            _pageFixes.Children.Add(grpBar);

            ScrollViewer svFixes = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _fixesListPanel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            svFixes.Content = _fixesListPanel;
            Grid.SetRow(svFixes, 3);
            _pageFixes.Children.Add(svFixes);

            UpdateFixGroupStyles();
        }

        // ==================== PAGE 2: CLEAN STRESS TEST ====================
        // ==================== PAGE 3: ACCORDION GUIDE & REPORT ====================
        private void BuildGuidePage()
        {
            _pageGuide = new Grid { Visibility = Visibility.Collapsed };
            _pageGuide.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pageGuide.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Sub 0: Accordion Encyclopedia with Hardware Filter
            _svGuide = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel guideWrapper = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };

            _bdHwGuideBanner = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 10)
            }, "AccentSoftBg"), "BorderCard");

            Grid gBannerGrid = new Grid();
            gBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gBannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _lblHwGuideStatus = ResFg(new TextBlock
            {
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }, "AccentText");
            Grid.SetColumn(_lblHwGuideStatus, 0);
            gBannerGrid.Children.Add(_lblHwGuideStatus);

            _btnToggleHardwareGuideFilter = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(11, 4, 11, 4), 11.5);
            _btnToggleHardwareGuideFilter.FontWeight = FontWeights.SemiBold;
            _btnToggleHardwareGuideFilter.Click += (s, e) =>
            {
                _onlyHardwareRelevantGuide = !_onlyHardwareRelevantGuide;
                RenderKnowledgeBase();
            };
            Grid.SetColumn(_btnToggleHardwareGuideFilter, 1);
            gBannerGrid.Children.Add(_btnToggleHardwareGuideFilter);

            _bdHwGuideBanner.Child = gBannerGrid;
            guideWrapper.Children.Add(_bdHwGuideBanner);

            _guideListPanel = new StackPanel();
            guideWrapper.Children.Add(_guideListPanel);

            _svGuide.Content = guideWrapper;
            Grid.SetRow(_svGuide, 1);
            _pageGuide.Children.Add(_svGuide);

            // Sub 1: Report
            _reportSubPanel = new Grid { Visibility = Visibility.Collapsed };
            _reportSubPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _reportSubPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            StackPanel repBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            _btnCopyReport = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(16, 7, 16, 7), 12.2);
            _btnCopyReport.Height = 34;
            _btnCopyReport.Margin = new Thickness(0, 0, 8, 0);
            _btnCopyReport.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(_txtReportBox.Text ?? "");
                    SetToast(Localizer.S("main_window.report_copied_to_clipboard"), "SuccessFg");
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Clipboard");
                    SetToast(Localizer.S("ai.clipboard_busy_try_again"), "WarnFg");
                }
            };

            _btnSaveReport = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 12.2);
            _btnSaveReport.Height = 34;
            _btnSaveReport.FontWeight = FontWeights.SemiBold;
            _btnSaveReport.Margin = new Thickness(0, 0, 8, 0);
            _btnSaveReport.Click += OnSaveReportClick;

            _btnExportFullReg = CreateFlatButton("", "BgCard", "AccentText", "BorderStrong", new Thickness(14, 7, 14, 7), 12.2);
            _btnExportFullReg.Height = 34;
            _btnExportFullReg.FontWeight = FontWeights.SemiBold;
            _btnExportFullReg.Click += OnExportCombinedRegClick;

            repBar.Children.Add(_btnCopyReport);
            repBar.Children.Add(_btnSaveReport);
            repBar.Children.Add(_btnExportFullReg);
            Grid.SetRow(repBar, 0);
            _reportSubPanel.Children.Add(repBar);

            _txtReportBox = ResFg(ResBg(new TextBox
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16),
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }, "BgCard"), "TextPrimary");
            _txtReportBox.SetResourceReference(TextBox.BorderBrushProperty, "BorderCard");
            Grid.SetRow(_txtReportBox, 1);
            _reportSubPanel.Children.Add(_txtReportBox);

            Grid.SetRow(_reportSubPanel, 1);
            _pageGuide.Children.Add(_reportSubPanel);
        }

        // ==================== BUTTON TEMPLATES (design system v6, see UI/Themes/Controls.xaml) ====================
        private Button CreateNavButton(UIElement content)
        {
            Button btn = new DfButton
            {
                Content = content,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(8, 9, 10, 9),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                FocusVisualStyle = null
            };
            btn.SetResourceReference(Control.TemplateProperty, "DfNavButtonTemplate");
            return btn;
        }

        private Button CreateFlatButton(string text, string bgKey, string fgKey, string borderKey, Thickness pad, double fontSize)
        {
            Button btn = new DfButton
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(Math.Max(pad.Left, 10), Math.Max(pad.Top, 5), Math.Max(pad.Right, 10), Math.Max(pad.Bottom, 5)),
                FontSize = Ds.Type(fontSize),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null
            };
            btn.Content = text;
            btn.SetResourceReference(Button.BackgroundProperty, bgKey);
            btn.SetResourceReference(Button.ForegroundProperty, fgKey);
            btn.SetResourceReference(Button.BorderBrushProperty, borderKey);
            btn.SetResourceReference(Control.TemplateProperty, "DfButtonTemplate");
            return btn;
        }

        private Button CreatePrimaryButton(string text, string bgKey, string fgKey, Thickness pad, double fontSize)
        {
            Button btn = new DfButton
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(Math.Max(pad.Left, 14), Math.Max(pad.Top, 7), Math.Max(pad.Right, 14), Math.Max(pad.Bottom, 7)),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = Ds.Type(fontSize),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FocusVisualStyle = null
            };
            btn.Content = text;
            btn.SetResourceReference(Button.BackgroundProperty, bgKey);
            btn.SetResourceReference(Button.ForegroundProperty, fgKey);
            btn.SetResourceReference(Button.BorderBrushProperty, bgKey);
            btn.SetResourceReference(Control.TemplateProperty, "DfButtonTemplate");
            return btn;
        }

        // ==================== STATE, NAVIGATION & LOCALIZATION ====================
        public void SwitchTab(int idx)
        {
            if (idx < 0 || idx >= NavCount) idx = TabHome;
            _activeTab = idx;
            FrameworkElement[] pages = { _pageHome, _pageCrashes, _pageHealth, _pageFixes, _pageOptimize, _pageWinTools, _pageSettings };
            for (int p = 0; p < pages.Length; p++)
                pages[p].Visibility = (p == idx) ? Visibility.Visible : Visibility.Collapsed;

            for (int i = 0; i < NavCount; i++)
            {
                bool active = (i == idx);
                if (active)
                {
                    _navButtons[i].SetResourceReference(Button.BackgroundProperty, "HoverOverlay");
                    _navLabels[i].FontWeight = FontWeights.SemiBold;
                    _navLabels[i].SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                    _navIcons[i].SetResourceReference(TextBlock.ForegroundProperty, "AccentText");
                    _navIndicators[i].Visibility = Visibility.Visible;
                }
                else
                {
                    _navButtons[i].Background = Brushes.Transparent;
                    _navLabels[i].FontWeight = FontWeights.Normal;
                    _navLabels[i].SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    _navIcons[i].SetResourceReference(TextBlock.ForegroundProperty, "TextSecondary");
                    _navIndicators[i].Visibility = Visibility.Hidden;
                }
            }

            AnimatePageIn(pages[idx]);
            OnUserSwitchedTab(idx);
        }

        private void StyleTogglePill(Button b, bool active)
        {
            b.SetResourceReference(Button.BackgroundProperty, active ? "Accent" : "BgCard");
            b.SetResourceReference(Button.ForegroundProperty, active ? "OnAccent" : "TextSecondary");
            b.SetResourceReference(Button.BorderBrushProperty, active ? "Accent" : "BorderStrong");
        }

        private void UpdateFixGroupStyles()
        {
            FixGroup[] groups = new[] { FixGroup.All, FixGroup.Gpu, FixGroup.BrowserApps, FixGroup.StoragePcie, FixGroup.SystemMaint };
            for (int i = 0; i < groups.Length; i++)
                StyleTogglePill(_fixGroupBtns[i], _fixGroupFilter == groups[i]);
        }

        private void SwitchLanguage(AppLang lang)
        {
            Localizer.Current = lang;
            ApplyLanguageTypography();
            SaveCurrentPreferences();
            PopulateLocalizedDropdowns();
            RefreshStaticLabels();
            UpdateSentinelButtonVisual();
            RelocalizeScanResult();
        }

        /// <summary>
        /// Window font and xml:lang for the current language (inherited by every element): picks the right CJK
        /// font, line breaking and hyphenation rules for Chinese / Japanese / Korean text.
        /// </summary>
        private void ApplyLanguageTypography()
        {
            FontFamily = Ds.UiFont;
            try { Language = System.Windows.Markup.XmlLanguage.GetLanguage(Localizer.Culture.IetfLanguageTag); }
            catch (Exception ex) { Log.Warn(ex); }
        }

        // Relocalization state (UI thread only): one pass at a time, never during a scan.
        private bool _relocalizeRunning;
        private bool _relocalizePending;

        /// <summary>
        /// Rebuilds the scan-derived texts in the current language. Deferred while a scan runs
        /// (the scan completion triggers it) and serialized: a request during a pass is queued once.
        /// </summary>
        private async void RelocalizeScanResult()
        {
            if (_scanInProgress || _relocalizeRunning)
            {
                _relocalizePending = true;
                return;
            }
            _relocalizePending = false;

            ScanResult res = _lastResult;
            if (res == null) return;

            AppLang lang = Localizer.Current;
            var smart = _cachedAdminSmart;
            _relocalizeRunning = true;
            try
            {
                using (BeginBusy(null))
                {
                    try
                    {
                        // Re-localizing re-probes registry/WMI: run it off the UI thread.
                        await Task.Run(() =>
                        {
                            res.Fixes = DiagnosticsEngine.InspectAllFixes(false);
                            DiagnosticsEngine.RefreshLocalizedHealthAndConflicts(res, smart);
                            DiagnosticsEngine.BuildSmartDiagnosis(res);
                        });
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "SwitchLanguage");
                    }
                }
                // A newer scan or language switch may have replaced the result meanwhile.
                if (ReferenceEquals(res, _lastResult) && Localizer.Current == lang)
                    RenderScanResult(res);
            }
            finally
            {
                _relocalizeRunning = false;
            }

            if (_relocalizePending && !_scanInProgress)
                RelocalizeScanResult();
        }

        private void RefreshDynamicThemeAccents()
        {
            if (_lastResult != null)
                RenderScanResult(_lastResult);
            RenderKnowledgeBase();
            RedrawStressChart();
        }

        private void PopulateLocalizedDropdowns()
        {
            _suppressDropdownEvents = true;

            int curTheme = (int)ThemeManager.CurrentMode;
            _cbTheme.Items.Clear();
            _cbTheme.Items.Add(Localizer.S("main_window.system_windows"));
            _cbTheme.Items.Add(Localizer.S("main_window.dark_mode"));
            _cbTheme.Items.Add(Localizer.S("main_window.light_mode"));
            _cbTheme.SelectedIndex = curTheme;

            System.Windows.Automation.AutomationProperties.SetName(_cbTimeframe, Localizer.S("crashes.filter_period"));
            System.Windows.Automation.AutomationProperties.SetName(_cbEventCategory, Localizer.S("crashes.filter_category"));
            _cbTimeframe.Items.Clear();
            _cbTimeframe.Items.Add(Localizer.S("main_window.since_last_boot"));
            _cbTimeframe.Items.Add(Localizer.S("main_window.last_3_days"));
            _cbTimeframe.Items.Add(Localizer.S("main_window.last_30_days"));
            _cbTimeframe.Items.Add(Localizer.S("main_window.all_history"));
            _cbTimeframe.SelectedIndex = _selectedRangeMode;

            _cbEventCategory.Items.Clear();
            _cbEventCategory.Items.Add(Localizer.S("main_window.all_categories"));
            _cbEventCategory.Items.Add(Localizer.S("main_window.bsod_reboots"));
            _cbEventCategory.Items.Add(Localizer.S("main_window.graphics_driver_gpu"));
            _cbEventCategory.Items.Add(Localizer.S("main_window.nvme_ssd_storage"));
            _cbEventCategory.Items.Add(Localizer.S("main_window.whea_hardware_ram"));
            _cbEventCategory.Items.Add(Localizer.S("main_window.browsers_applications"));
            _cbEventCategory.SelectedIndex = _eventFilterMode;

            _suppressDropdownEvents = false;
        }

        private void RefreshStaticLabels()
        {
            Title = "Dump Finder";

            _navLabels[TabHome].Text = Localizer.S("main_window.home");
            _navLabels[TabCrashes].Text = Localizer.S("crashes.crashes");
            _navLabels[TabHardware].Text = Localizer.S("main_window.hardware");
            _navLabels[TabFixes].Text = Localizer.S("main_window.fixes");
            _navLabels[TabOptimize].Text = Localizer.S("main_window.optimize");
            _navLabels[TabTools].Text = Localizer.S("main_window.tools");
            _navLabels[TabSettings].Text = Localizer.S("main_window.settings");

            _btnElevate.Content = Localizer.S("main_window.run_as_admin");

            RefreshShellLabels();
            RefreshHealthPageLabels();
            RefreshWinToolsPageLabels();
            RefreshOptimizeLabels();
            RefreshAiPageLabels();
            RefreshHomeLabels();
            RefreshCrashesLabels();
            RefreshSettingsLabels();
            RefreshToolsSubLabels();
            RefreshDriverLabels();
            RefreshGpuCurveLabels();
            RefreshHistoryLabels();
            RefreshReportLabels();



            // Page 2 (Fixes)
            _lblFixesTitle.Text = Localizer.S("main_window.fixes_protections");
            _lblFixesSub.Text = Localizer.S("main_window.automatic_registry_snapshot_saved_before");
            _btnCreateRestorePt.Content = Localizer.S("main_window.restore_point");
            _btnRestoreSnapshot.Content = Localizer.S("main_window.rollback_fixes");
            _btnApplyAllRec.Content = Localizer.S("main_window.apply_recommended");
            RefreshSnapshotButtonState();

            FixGroup[] groups = new[] { FixGroup.All, FixGroup.Gpu, FixGroup.BrowserApps, FixGroup.StoragePcie, FixGroup.SystemMaint };
            for (int i = 0; i < groups.Length; i++)
                _fixGroupBtns[i].Content = Localizer.FixGroupName(groups[i]);

            RefreshStressPageLabels();

            // Page 6 (Guide & Report)
            _btnCopyReport.Content = Localizer.S("main_window.copy_report");
            _btnSaveReport.Content = Localizer.S("main_window.save_txt");
            _btnExportFullReg.Content = Localizer.S("main_window.export_reg_pack");

            RenderKnowledgeBase();
            RefreshReportLabels();
        }

        // ==================== SCANNING & RENDERING ====================
        private DateTime? GetStartTimeForRange(HardwareSummary hw)
        {
            switch (_selectedRangeMode)
            {
                case 0: return hw.LastBoot.AddMinutes(-1);
                case 1: return DateTime.Now.AddDays(-3);
                case 2: return DateTime.Now.AddDays(-30);
                default: return null;
            }
        }

        // Scan state (UI thread only): one scan at a time; a request during a scan is queued once.
        private bool _scanInProgress;
        private bool _scanQueued;

        private async void RunScanAsync()
        {
            if (_scanInProgress)
            {
                _scanQueued = true;
                return;
            }
            _scanInProgress = true;
            _scanQueued = false;
            if (_btnRescan != null) _btnRescan.IsEnabled = false;
            try
            {
                await RunScanCoreAsync();
            }
            finally
            {
                _scanInProgress = false;
                if (_btnRescan != null) _btnRescan.IsEnabled = true;
                OnScanFinished();
            }
        }

        /// <summary>Runs what was requested while a scan was busy: a queued rescan first, else a pending relocalization.</summary>
        private void OnScanFinished()
        {
            if (_scanQueued)
            {
                _scanQueued = false;
                RunScanAsync();
            }
            else if (_relocalizePending)
            {
                RelocalizeScanResult();
            }
        }

        private async Task RunScanCoreAsync()
        {
            SetToast(Localizer.S("main_window.scanning_system"), "TextSecondary");

            ScanResult res;
            try
            {
                // WMI hardware probing is slow on first call: keep it off the UI thread too.
                res = await Task.Run(() =>
                {
                    HardwareSummary hw = DiagnosticsEngine.GetHardwareSummary();
                    return DiagnosticsEngine.RunScan(GetStartTimeForRange(hw));
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "RunScan");
                SetToast(Localizer.S("main_window.scan_failed_see_log_for_details"), "DangerFg");
                return;
            }

            _lastResult = res;
            RenderScanResult(res);
            RecordScan(res);

            if (!_initialTourTriggered && (_appSettings == null || !_appSettings.HasCompletedTour))
            {
                _initialTourTriggered = true;
                var op = Dispatcher.BeginInvoke(new Action(() => StartOnboarding()), System.Windows.Threading.DispatcherPriority.Background);
            }
            int crashCount = _crashEntries.Count(c => (DateTime.Now - c.Time).TotalDays <= Stability.WindowDays);
            SetToast(
                Localizer.F("main_window.last_scan_at_0_hh_mm", DateTime.Now, crashCount),
                "TextMuted");
        }

        private void RenderScanResult(ScanResult res)
        {
            RefreshStressResultsIfShown(); // fix titles in the stress verdict come from the scan
            _lblHwGpu.Text = res.Hardware.GpuName;
            _lblHwCpu.Text = res.Hardware.CpuName;
            _btnElevate.Visibility = res.Hardware.IsAdmin ? Visibility.Collapsed : Visibility.Visible;
            if (_lblHwProfileBadge != null)
                _lblHwProfileBadge.Text = res.Hardware.GetProfileBadge();

            // Sidebar badge for recommended fixes
            int recCount = res.Fixes.Count(f => f.Status == FixStatus.Recommended);
            _navFixesBadge.Visibility = recCount > 0 ? Visibility.Visible : Visibility.Collapsed; RefreshNavBadgesForCollapse();
            _lblNavFixesCount.Text = recCount.ToString();

            RebuildCrashEntries(res);
            RenderHome(res);
            RenderCrashList();
            RenderHealthAndDumps(res);
            RenderDrivers(res);
            RenderFixesCards(res.Fixes);
            RenderKnowledgeBase();
            RenderSettingsState(res);
            RefreshSnapshotButtonState();
            _txtReportBox.Text = GeneratePlainReportV4(res);
        }

        // ==================== CLEAN SETTINGS-STYLE FIX CARDS ====================
        private void RenderFixesCards(List<RegistryFixItem> allFixes)
        {
            _fixesListPanel.Children.Clear();

            int totalInGroup = (_fixGroupFilter == FixGroup.All)
                ? allFixes.Count
                : allFixes.Count(f => f.Group == _fixGroupFilter);

            var filtered = _fixGroupFilter == FixGroup.All
                ? allFixes
                : allFixes.Where(f => f.Group == _fixGroupFilter).ToList();

            if (_onlyHardwareRelevantFixes && _lastResult != null && _lastResult.Hardware != null)
            {
                filtered = filtered.Where(f => f.IsApplicableTo(_lastResult.Hardware)).ToList();
            }

            // Update banner text & toggle button
            if (_lblHwFixesStatus != null && _btnToggleHardwareFixesFilter != null)
            {
                string hwTag = (_lastResult != null && _lastResult.Hardware != null) ? _lastResult.Hardware.GetProfileBadge() : "PC";
                int hiddenCount = totalInGroup - filtered.Count;
                if (_onlyHardwareRelevantFixes)
                {
                    _lblHwFixesStatus.Text = Localizer.F("main_window.active_filter_fix_es_tailored_for", hwTag, filtered.Count, hiddenCount);

                    _btnToggleHardwareFixesFilter.Content = Localizer.F("main_window.show_all", totalInGroup);
                }
                else
                {
                    _lblHwFixesStatus.Text = Localizer.F("main_window.showing_all_fix_es_across_all", filtered.Count);

                    _btnToggleHardwareFixesFilter.Content = Localizer.F("main_window.filter_by_my_hardware", hwTag);
                }
            }

            int recCount = filtered.Count(f => f.Status == FixStatus.Recommended);
            _btnApplyAllRec.IsEnabled = (recCount > 0);
            if (recCount > 0)
            {
                _btnApplyAllRec.Content = Localizer.F("main_window.apply_recommended_2", recCount);
            }
            else
            {
                _btnApplyAllRec.Content = Localizer.S("main_window.no_fix_required");
            }

            foreach (var fix in filtered)
            {
                Border card = BuildFixCard(fix, ExecuteSingleFix);
                card.Tag = fix.Id; // OpenFix finds its card by id
                _fixesListPanel.Children.Add(card);
            }
            ApplyPendingFixFocus();
        }

        /// <summary>Resting border of a fix card (also restored after the OpenFix highlight).</summary>
        private static string FixCardBorderKey(RegistryFixItem fix)
        {
            return fix.Status == FixStatus.Recommended ? "WarnBorder" : "BorderCard";
        }

        /// <summary>One fix / setting card (title, status, explanation, primary button, technical drawer). Shared with Optimiser.</summary>
        private Border BuildFixCard(RegistryFixItem fix, Action<string, string, object> run)
        {
                string stBg = "BgSubtle", stFg = "TextSecondary", stBd = "BorderCard";
                if (fix.Status == FixStatus.Applied) { stBg = "SuccessSoftBg"; stFg = "SuccessFg"; stBd = "SuccessBorder"; }
                else if (fix.Status == FixStatus.Recommended) { stBg = "WarnSoftBg"; stFg = "WarnFg"; stBd = "WarnBorder"; }

                Border card = ResBorder(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 13, 16, 13),
                    Margin = new Thickness(0, 0, 0, 8)
                }, "BgCard"), FixCardBorderKey(fix));

                StackPanel cardStack = new StackPanel();
                Grid mainRow = new Grid();
                mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                mainRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                // Left Info
                StackPanel left = new StackPanel { Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
                WrapPanel head = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };

                head.Children.Add(ResFg(new TextBlock
                {
                    Text = fix.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                }, "TextPrimary"));

                head.Children.Add(ResBorder(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(7, 1, 7, 1),
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = ResFg(new TextBlock { Text = Localizer.StatusBadgeText(fix.Status), FontSize = 11, FontWeight = FontWeights.SemiBold }, stFg)
                }, stBg), stBd));

                if (!string.IsNullOrEmpty(fix.VendorTag))
                {
                    head.Children.Add(ResBg(new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 1, 6, 1),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = ResFg(new TextBlock { Text = fix.VendorTag, FontSize = 11, FontWeight = FontWeights.SemiBold }, "TextMuted")
                    }, "BgSubtle"));
                }
                left.Children.Add(head);

                left.Children.Add(ResFg(new TextBlock
                {
                    Text = fix.Explanation,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                }, "TextSecondary"));

                Grid.SetColumn(left, 0);
                mainRow.Children.Add(left);

                // Right Actions (Single Primary Button + Details Chevron)
                StackPanel right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                string fixId = fix.Id;
                bool alreadyApplied = (fix.Status == FixStatus.Applied && fix.Id != "hags" && fix.Id != "shader_cache" && fix.Id != "sfc_repair");
                bool isRecommended = (fix.Status == FixStatus.Recommended);

                string btnBg = isRecommended ? "Accent" : (alreadyApplied ? "BgSubtle" : "BgCard");
                string btnFg = isRecommended ? "OnAccent" : (alreadyApplied ? "TextMuted" : "TextPrimary");
                string btnBd = isRecommended ? "Accent" : (alreadyApplied ? "BorderCard" : "BorderStrong");

                Button btnPrimary = CreateFlatButton(
                    alreadyApplied ? ("✓ " + Localizer.StatusBadgeText(FixStatus.Applied)) : fix.ApplyBtn,
                    btnBg, btnFg, btnBd,
                    new Thickness(13, 6, 13, 6), 12.2);
                btnPrimary.MinWidth = 118;
                btnPrimary.FontWeight = FontWeights.SemiBold;
                btnPrimary.Click += (s, e) => run(fixId + "_apply", fixId, s);
                right.Children.Add(btnPrimary);

                Border drawer = ResBg(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 10, 0, 0),
                    Visibility = Visibility.Collapsed
                }, "BgSubtle");

                Button btnMore = CreateFlatButton("▾", "BgSubtle", "TextSecondary", "BorderCard", new Thickness(9, 6, 9, 6), 12);
                btnMore.ToolTip = Localizer.S("main_window.details");
                btnMore.Margin = new Thickness(6, 0, 0, 0);
                btnMore.Click += (s, e) =>
                {
                    bool show = drawer.Visibility != Visibility.Visible;
                    drawer.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    btnMore.Content = show ? "▴" : "▾";
                };
                right.Children.Add(btnMore);

                Grid.SetColumn(right, 1);
                mainRow.Children.Add(right);
                cardStack.Children.Add(mainRow);

                // Expandable Technical Drawer (Current Value + Registry Path + Restore + Export .reg)
                Grid drwGrid = new Grid();
                drwGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                drwGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                StackPanel drwLeft = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                drwLeft.Children.Add(ResFg(new TextBlock
                {
                    Text = Localizer.S("main_window.current_state") + fix.CurrentValue,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                }, "TextPrimary"));
                drwLeft.Children.Add(ResFg(new TextBlock
                {
                    Text = fix.RegPathDisplay,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                }, "TextMuted"));
                Grid.SetColumn(drwLeft, 0);
                drwGrid.Children.Add(drwLeft);

                StackPanel drwBtns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                if (fix.CanRestore && !string.IsNullOrEmpty(fix.RestoreBtn))
                {
                    Button btnRest = CreateFlatButton(fix.RestoreBtn, "BgCard", "TextSecondary", "BorderCard", new Thickness(10, 4, 10, 4), 11.5);
                    btnRest.Margin = new Thickness(8, 0, 0, 0);
                    btnRest.Click += (s, e) => run(fixId + "_restore", fixId, s);
                    drwBtns.Children.Add(btnRest);
                }
                if (!string.IsNullOrEmpty(fix.RegExportContent))
                {
                    string regContent = fix.RegExportContent;
                    Button btnReg = CreateFlatButton(".reg", "BgCard", "AccentText", "BorderCard", new Thickness(10, 4, 10, 4), 11.5);
                    btnReg.Margin = new Thickness(6, 0, 0, 0);
                    btnReg.Click += (s, e) => SaveRegSnippet(fixId, regContent);
                    drwBtns.Children.Add(btnReg);
                }
                Grid.SetColumn(drwBtns, 1);
                drwGrid.Children.Add(drwBtns);

                drawer.Child = drwGrid;
                cardStack.Children.Add(drawer);
                card.Child = cardStack;
                return card;
        }

        // ==================== ACCORDION CRASH GUIDE ====================
        private void RenderKnowledgeBase()
        {
            if (_guideListPanel == null) return;
            _guideListPanel.Children.Clear();
            var allEntries = KnowledgeBase.GetEntries();
            var entries = allEntries;

            if (_onlyHardwareRelevantGuide && _lastResult != null && _lastResult.Hardware != null)
            {
                entries = allEntries.Where(k => k.IsApplicableTo(_lastResult.Hardware)).ToList();
            }

            if (_lblHwGuideStatus != null && _btnToggleHardwareGuideFilter != null)
            {
                string hwTag = (_lastResult != null && _lastResult.Hardware != null) ? _lastResult.Hardware.GetProfileBadge() : "PC";
                int hiddenCount = allEntries.Count - entries.Count;
                if (_onlyHardwareRelevantGuide)
                {
                    _lblHwGuideStatus.Text = Localizer.F("main_window.targeted_guide_article_s_for_your", hwTag, entries.Count, hiddenCount);

                    _btnToggleHardwareGuideFilter.Content = Localizer.F("main_window.show_all", allEntries.Count);
                }
                else
                {
                    _lblHwGuideStatus.Text = Localizer.F("main_window.universal_guide_article_s_across_all", entries.Count);

                    _btnToggleHardwareGuideFilter.Content = Localizer.F("main_window.target_my_hardware", hwTag);
                }
            }

            for (int idx = 0; idx < entries.Count; idx++)
            {
                var k = entries[idx];
                bool openDefault = (idx == 0);
                Brush tagBrush = ThemeManager.Hex(k.BadgeColorHex);

                ClickableCard card = ResBorder(ResBg(new ClickableCard
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 13, 16, 13),
                    Margin = new Thickness(0, 0, 0, 8),
                    Expanded = openDefault
                }, "BgCard"), "BorderCard");

                StackPanel sp = new StackPanel();
                Grid hdr = new Grid();
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });

                hdr.Children.Add(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 10, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = k.Tag, Foreground = tagBrush, FontSize = 11, FontWeight = FontWeights.Bold }
                }, "BgSubtle"));

                TextBlock title = ResFg(new TextBlock
                {
                    Text = k.Title,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                }, "TextPrimary");
                Grid.SetColumn(title, 1);
                hdr.Children.Add(title);

                TextBlock chev = ResFg(new TextBlock
                {
                    Text = openDefault ? "▴" : "▾",
                    FontSize = 12,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                }, "TextMuted");
                Grid.SetColumn(chev, 2);
                hdr.Children.Add(chev);
                sp.Children.Add(hdr);

                StackPanel body = new StackPanel
                {
                    Margin = new Thickness(0, 10, 0, 2),
                    Visibility = openDefault ? Visibility.Visible : Visibility.Collapsed
                };
                body.Children.Add(MakeKbParagraph(Localizer.S("main_window.symptoms"), k.Symptoms, "TextSecondary"));
                body.Children.Add(MakeKbParagraph(Localizer.S("main_window.root_cause"), k.Cause, "WarnFg"));
                body.Children.Add(MakeKbParagraph(Localizer.S("main_window.how_to_fix"), k.Solution, "SuccessFg"));
                sp.Children.Add(body);

                card.Child = sp;
                System.Windows.Automation.AutomationProperties.SetName(card, title.Text);
                card.Activated += () =>
                {
                    bool show = body.Visibility != Visibility.Visible;
                    body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    chev.Text = show ? "▴" : "▾";
                    card.Expanded = show;
                };
                _guideListPanel.Children.Add(card);
            }
        }

        private UIElement MakeKbParagraph(string label, string content, string labelBrushKey)
        {
            TextBlock tb = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) }, "TextPrimary");
            Run rLabel = new Run(label) { FontWeight = FontWeights.SemiBold };
            rLabel.SetResourceReference(TextElement.ForegroundProperty, labelBrushKey);
            tb.Inlines.Add(rLabel);
            tb.Inlines.Add(new Run(content));
            return tb;
        }

        // ==================== FIX EXECUTION & EXPORTS ====================
        private sealed class FixOutcome
        {
            public bool Ok;
            public string MessageFr;
            public string MessageEn;
            public string Message { get { return Localizer.T(MessageFr, MessageEn); } }
        }

        /// <summary>Runs one fix (possibly behind a UAC prompt) off the UI thread and records it in the journal.</summary>
        private async Task<FixOutcome> RunFixAsync(string opCode)
        {
            var results = await RunFixBatchAsync(new[] { opCode });
            var r = results.FirstOrDefault();
            return r == null
                ? new FixOutcome { Ok = false, MessageFr = Localizer.Ref("engine.main_window.no_result"), MessageEn = Localizer.Ref("engine.main_window.no_result") }
                : new FixOutcome { Ok = r.Ok, MessageFr = r.MessageFr, MessageEn = r.MessageEn };
        }

        /// <summary>Runs several fixes with ONE elevation prompt, then journals every result.</summary>
        private async Task<List<FixRunResult>> RunFixBatchAsync(IList<string> ops)
        {
            var results = await Task.Run(() => FixRunner.Run(ops));
            JournalResults(results);
            return results;
        }

        private void JournalResults(List<FixRunResult> results)
        {
            if (results == null || results.Count == 0) return;
            var entries = results.Select(r => new JournalEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Time = DateTime.Now,
                Op = r.Op,
                FixId = FixIdOf(r.Op),
                Title = OpTitle(r.Op),
                Ok = r.Ok,
                Message = Localizer.IsRef(r.MessageEn) ? r.MessageEn : r.Message, // a deferred reference is re-rendered in the display language
                Changes = r.Changes ?? new List<ChangeRecord>(),
                Reversible = r.Ok && FixRunner.IsReversible(r.Op, r.Changes)
            }).ToList();
            History.AddJournal(entries);
            RenderJournal();
        }

        private static string FixIdOf(string op)
        {
            if (op.EndsWith("_apply", StringComparison.Ordinal)) return op.Substring(0, op.Length - 6);
            if (op.EndsWith("_restore", StringComparison.Ordinal)) return op.Substring(0, op.Length - 8);
            return op;
        }

        /// <summary>Human title of an operation, from the current fix list when possible.</summary>
        private string OpTitle(string op)
        {
            string optTitle = Optimize.OptOps.IsOp(op) ? Optimize.OptOps.Title(op) : null;
            if (optTitle != null) return optTitle;
            string id = FixIdOf(op);
            var fix = _lastResult != null && _lastResult.Fixes != null ? _lastResult.Fixes.FirstOrDefault(f => f.Id == id) : null;
            string title = fix != null ? fix.Title : id;
            switch (op)
            {
                case "restore_snapshot": return Localizer.S("main_window.initial_registry_snapshot_restored");
                case "create_restore_point": return Localizer.S("main_window.windows_restore_point");
                case "crashdump_apply": return Localizer.S("main_window.blue_screen_recording_turned_back_on");
                case "disable_risky_oem": return Localizer.S("main_window.risky_oem_software_neutralized");
                case "disable_ryzen_igpu": return Localizer.S("main_window.amd_external_events_service_disabled");
                case "disable_autohdr": return Localizer.S("main_window.auto_hdr_disabled");
            }
            if (op.EndsWith("_restore", StringComparison.Ordinal))
                return Localizer.S("main_window.reset_to_default") + title;
            return title;
        }

        /// <summary>Closes Chrome/Edge (after confirmation) before fixes that rewrite their settings. False = user declined.</summary>
        private async Task<bool> ConfirmBrowserClose(string fixId)
        {
            string proc = fixId == "chrome_graphite" ? "chrome" : fixId == "edge_graphite" ? "msedge" : null;
            if (proc == null || !DiagnosticsEngine.IsProcessRunning(proc)) return true;
            string name = proc == "chrome" ? "Google Chrome" : "Microsoft Edge";
            var ans = MessageBox.Show(this,
                Localizer.F("main_window.close_gracefully_to_apply_this_setting", name),
                "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes) return false;
            await Task.Run(() => DiagnosticsEngine.CloseProcessGracefully(proc));
            return true;
        }
        private int _busyDepth;

        /// <summary>Shows a wait cursor and a pending message while background work runs.</summary>
        private IDisposable BeginBusy(string pendingMessage)
        {
            if (_busyDepth++ == 0) Mouse.OverrideCursor = Cursors.AppStarting;
            if (!string.IsNullOrEmpty(pendingMessage)) SetToast(pendingMessage, "TextSecondary");
            return new BusyScope(this);
        }

        private sealed class BusyScope : IDisposable
        {
            private MainWindow _owner;
            public BusyScope(MainWindow owner) { _owner = owner; }
            public void Dispose()
            {
                if (_owner == null) return;
                if (--_owner._busyDepth == 0) Mouse.OverrideCursor = null;
                _owner = null;
            }
        }

        private static string PendingFixMessage
        {
            get { return Localizer.S("main_window.applying"); }
        }

        // ==================== FIX RE-ENTRANCY GUARD ====================
        /// <summary>True while a fix, a fix batch, a cleanup, an app removal or an undo runs (UI thread only).</summary>
        private bool _fixInProgress;

        /// <summary>
        /// Enters the single "system change" slot and disables the clicked button. Returns null (and
        /// tells the user) when another change is already running. Dispose the scope (using/finally).
        /// </summary>
        private IDisposable TryBeginFix(object sender)
        {
            if (_fixInProgress)
            {
                SetToast(Localizer.S("main_window.another_operation_is_already_running_wai"), "WarnFg");
                return null;
            }
            _fixInProgress = true;
            Button b = sender as Button;
            bool wasEnabled = b != null && b.IsEnabled;
            if (b != null) b.IsEnabled = false;
            return new FixScope(this, b, wasEnabled);
        }

        private sealed class FixScope : IDisposable
        {
            private MainWindow _owner;
            private readonly Button _button;
            private readonly bool _wasEnabled;
            public FixScope(MainWindow owner, Button button, bool wasEnabled) { _owner = owner; _button = button; _wasEnabled = wasEnabled; }
            public void Dispose()
            {
                if (_owner == null) return;
                _owner._fixInProgress = false;
                if (_button != null && _wasEnabled) _button.IsEnabled = true;
                _owner = null;
            }
        }

        private async void ExecuteSingleFix(string opCode, string fixId, object sender = null)
        {
            using (IDisposable fixLock = TryBeginFix(sender))
            {
                if (fixLock == null) return;
                if (opCode.EndsWith("_apply", StringComparison.Ordinal) && !await ConfirmBrowserClose(fixId)) return;

                using (BeginBusy(PendingFixMessage))
                {
                    FixOutcome outcome = await RunFixAsync(opCode);
                    SetToast(outcome.Message, outcome.Ok ? "SuccessFg" : "DangerFg");

                    if (_lastResult != null)
                    {
                        bool deepShader = fixId == "shader_cache";
                        _lastResult.Fixes = await Task.Run(() => DiagnosticsEngine.InspectAllFixes(deepShader));
                        RenderScanResult(_lastResult);
                    }
                }
            }
        }

        private async void OnApplyAllRecommendedClick(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null) return;
            using (IDisposable fixLock = TryBeginFix(sender))
            {
                if (fixLock == null) return;
                var pending = _lastResult.Fixes
                    .Where(f => f.Status == FixStatus.Recommended && (!_onlyHardwareRelevantFixes || f.IsApplicableTo(_lastResult.Hardware)))
                    .ToList();
                if (pending.Count == 0)
                {
                    SetToast(Localizer.S("main_window.all_recommended_fixes_are_already_active"), "SuccessFg");
                    return;
                }

                // Browser confirmations first, then everything in one batch (a single UAC prompt).
                var ops = new List<string>();
                foreach (var fix in pending)
                {
                    if (!await ConfirmBrowserClose(fix.Id)) continue;
                    ops.Add(fix.Id + "_apply");
                }
                if (ops.Count == 0) return;

                List<FixRunResult> results;
                using (BeginBusy(PendingFixMessage))
                {
                    results = await RunFixBatchAsync(ops);
                    if (_lastResult != null)
                    {
                        _lastResult.Fixes = await Task.Run(() => DiagnosticsEngine.InspectAllFixes(false));
                        RenderScanResult(_lastResult);
                    }
                }

                int applied = results.Count(r => r.Ok), failed = results.Count - applied;
                foreach (var r in results.Where(r => !r.Ok)) Log.Warn("Fix " + r.Op + " failed: " + Localizer.RenderEn(r.MessageEn));
                if (failed == 0)
                    SetToast(Localizer.F("main_window.fix_es_applied_each_one_can", applied), "SuccessFg");
                else
                    SetToast(Localizer.F("main_window.fix_es_applied_failed", applied, failed), "WarnFg");
            }
        }

        private static string SavedPrefix
        {
            get { return Localizer.S("main_window.saved"); }
        }

        private static string SaveFailedPrefix
        {
            get { return Localizer.S("main_window.could_not_save_the_file"); }
        }

        private void SaveRegSnippet(string name, string content)
        {
            SaveFileDialog sfd = new SaveFileDialog { FileName = "fix_" + name + ".reg", Filter = "Registry File (*.reg)|*.reg" };
            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, content, Encoding.ASCII);
                    SetToast(SavedPrefix + sfd.FileName, "SuccessFg");
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Save .reg");
                    SetToast(SaveFailedPrefix + ex.Message, "DangerFg");
                }
            }
        }

        private void OnExportCombinedRegClick(object sender, RoutedEventArgs e)
        {
            string reg =
                "Windows Registry Editor Version 5.00\r\n\r\n" +
                "; Dump Finder v6 Registry Pack\r\n" +
                "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows\\Dwm]\r\n" +
                "\"OverlayTestMode\"=dword:00000005\r\n" +
                "\"OverlayMinFPS\"=dword:00000000\r\n\r\n" +
                "[HKEY_LOCAL_MACHINE\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers]\r\n" +
                "\"HwSchMode\"=dword:00000002\r\n" +
                "\"TdrDelay\"=dword:0000000a\r\n" +
                "\"TdrDdiDelay\"=dword:0000000a\r\n\r\n" +
                "[HKEY_LOCAL_MACHINE\\SOFTWARE\\Policies\\Microsoft\\Windows\\WindowsUpdate]\r\n" +
                "\"ExcludeWUDriversInQualityUpdate\"=dword:00000001\r\n\r\n" +
                "[HKEY_CURRENT_USER\\System\\GameConfigStore]\r\n" +
                "\"GameDVR_Enabled\"=dword:00000000\r\n";

            SaveRegSnippet("dumpfinder_pack", reg);
        }

        private void OnSaveReportClick(object sender, RoutedEventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog
            {
                FileName = "DumpFinder_Report_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".txt",
                Filter = "Text Report (*.txt)|*.txt"
            };
            if (sfd.ShowDialog(this) == true)
            {
                try
                {
                    File.WriteAllText(sfd.FileName, _txtReportBox.Text ?? "", Encoding.UTF8);
                    SetToast(SavedPrefix + sfd.FileName, "SuccessFg");
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Save report");
                    SetToast(SaveFailedPrefix + ex.Message, "DangerFg");
                }
            }
        }

        private void OnElevateClick(object sender, RoutedEventArgs e)
        {
            if (Elevation.RestartAppElevated())
            {
                // Same as the tray "Quit": a real exit. The Closing handler must not hide to tray,
                // and the cleanup (tray icon, stress marker, SystemEvents, AI stream) must run.
                _forceExit = true;
                RunExitCleanup();
                Application.Current.Shutdown();
            }
            else
                SetToast(Localizer.S("main_window.elevation_cancelled"), "WarnFg");
        }

        /// <summary>
        /// Feedback entry point used everywhere. Outcomes (success / warning / error / accent)
        /// become toasts; neutral progress text goes to the quiet status line.
        /// </summary>
        private void SetToast(string text, string fgKey)
        {
            if (string.IsNullOrEmpty(text)) return;
            switch (fgKey)
            {
                case "SuccessFg":
                    ShowToast(text, ToastKind.Success);
                    break;
                case "WarnFg":
                    ShowToast(text, ToastKind.Warning);
                    break;
                case "DangerFg":
                    ShowToast(text, ToastKind.Error);
                    break;
                case "AccentText":
                    SetStatus(text, true);
                    break;
                default:
                    SetStatus(text, false);
                    break;
            }
        }
    }
}
