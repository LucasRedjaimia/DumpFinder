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
            Width = 1240;
            Height = 820;
            MinWidth = 1000;
            MinHeight = 680;
            Title = "Dump Finder";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            FontFamily = Ds.UiFont;
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

            InitTraySentinel(initialSettings != null && initialSettings.TraySentinelEnabled);
            BuildLayout();

            Closing += (s, e) =>
            {
                if (_sentinelEnabled && !_forceExit)
                {
                    e.Cancel = true;
                    Hide();
                    if (_traySentinel != null) _traySentinel.ShowMinimizedHintOnce();
                    return;
                }

                SystemEvents.UserPreferenceChanged -= OnWindowsPreferenceChanged;
                // Closing the app mid-test is not a crash: drop the "test running" marker.
                if (_stressRunner.IsRunning) { _stressRunner.RequestStop(); History.ClearStressMarker(); }
                if (_traySentinel != null) _traySentinel.Dispose();
            };

            Loaded += (s, e) => { RunScanAsync(); StartupUpdateCheck(); };
        }

        private void OnWindowsPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (ThemeManager.CurrentMode == AppThemeMode.SystemAuto)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    ThemeManager.ApplyTheme(this, AppThemeMode.SystemAuto);
                    RefreshDynamicThemeAccents();
                }));
            }
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

        private static Border ResBorder(Border b, string key)
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
            Grid.SetRow(navStack, 0);
            sideGrid.Children.Add(navStack);

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
                Clipboard.SetText(_txtReportBox.Text ?? "");
                SetToast(Localizer.L("Rapport copié dans le presse-papiers.", "Report copied to clipboard.", "Informe copiado al portapapeles.", "Bericht in die Zwischenablage kopiert.", "Rapporto copiato negli appunti.", "Relatório copiado para a área de transferência."), "SuccessFg");
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

        private async void SwitchLanguage(AppLang lang)
        {
            Localizer.Current = lang;
            SaveCurrentPreferences();
            PopulateLocalizedDropdowns();
            RefreshStaticLabels();
            UpdateSentinelButtonVisual();

            ScanResult res = _lastResult;
            if (res == null) return;

            var smart = _cachedAdminSmart;
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
            _cbTheme.Items.Add(Localizer.L("Système (Windows)", "System (Windows)", "Sistema (Windows)", "System (Windows)", "Sistema (Windows)", "Sistema (Windows)"));
            _cbTheme.Items.Add(Localizer.L("Mode sombre", "Dark mode", "Modo oscuro", "Dunkelmodus", "Modalità scura", "Modo escuro"));
            _cbTheme.Items.Add(Localizer.L("Mode clair", "Light mode", "Modo claro", "Hellmodus", "Modalità chiara", "Modo claro"));
            _cbTheme.SelectedIndex = curTheme;

            _cbTimeframe.Items.Clear();
            _cbTimeframe.Items.Add(Localizer.L("Depuis le démarrage", "Since last boot", "Desde el inicio", "Seit Systemstart", "Dall'ultimo avvio", "Desde a inicialização"));
            _cbTimeframe.Items.Add(Localizer.L("3 derniers jours", "Last 3 days", "Últimos 3 días", "Letzte 3 Tage", "Ultimi 3 giorni", "Últimos 3 dias"));
            _cbTimeframe.Items.Add(Localizer.L("30 derniers jours", "Last 30 days", "Últimos 30 días", "Letzte 30 Tage", "Ultimi 30 giorni", "Últimos 30 dias"));
            _cbTimeframe.Items.Add(Localizer.L("Tout l'historique", "All history", "Todo el historial", "Gesamter Verlauf", "Tutta la cronologia", "Todo o histórico"));
            _cbTimeframe.SelectedIndex = _selectedRangeMode;

            _cbEventCategory.Items.Clear();
            _cbEventCategory.Items.Add(Localizer.L("Toutes les catégories", "All categories", "Todas las categorías", "Alle Kategorien", "Tutte le categorie", "Todas as categorias"));
            _cbEventCategory.Items.Add(Localizer.L("Écrans bleus & Reboots", "BSOD & Reboots", "Pantallas azules y reinicios", "Bluescreens & Neustarts", "BSOD e riavvii", "Telas azuis e reinícios"));
            _cbEventCategory.Items.Add(Localizer.L("Pilote graphique (GPU)", "Graphics driver (GPU)", "Controlador gráfico (GPU)", "Grafiktreiber (GPU)", "Driver grafico (GPU)", "Driver de vídeo (GPU)"));
            _cbEventCategory.Items.Add(Localizer.L("Stockage SSD NVMe", "NVMe SSD storage", "Almacenamiento SSD NVMe", "NVMe-SSD-Speicher", "Archiviazione SSD NVMe", "Armazenamento SSD NVMe"));
            _cbEventCategory.Items.Add(Localizer.L("Matériel WHEA & RAM", "WHEA Hardware & RAM", "Hardware WHEA y RAM", "WHEA-Hardware & RAM", "Hardware WHEA e RAM", "Hardware WHEA e RAM"));
            _cbEventCategory.Items.Add(Localizer.L("Navigateurs & Applications", "Browsers & Applications", "Navegadores y aplicaciones", "Browser & Anwendungen", "Browser e applicazioni", "Navegadores e aplicativos"));
            _cbEventCategory.SelectedIndex = _eventFilterMode;

            _suppressDropdownEvents = false;
        }

        private void RefreshStaticLabels()
        {
            Title = "Dump Finder";

            _navLabels[TabHome].Text = Localizer.L("Accueil", "Home", "Inicio", "Start", "Home", "Início");
            _navLabels[TabCrashes].Text = Localizer.L("Crashs", "Crashes", "Fallos", "Abstürze", "Crash", "Falhas");
            _navLabels[TabHardware].Text = Localizer.L("Matériel", "Hardware", "Hardware", "Hardware", "Hardware", "Hardware");
            _navLabels[TabFixes].Text = Localizer.L("Correctifs", "Fixes", "Correcciones", "Korrekturen", "Correzioni", "Correções");
            _navLabels[TabOptimize].Text = Localizer.L("Optimiser", "Optimize", "Optimizar", "Optimieren", "Ottimizza", "Otimizar");
            _navLabels[TabTools].Text = Localizer.L("Outils", "Tools", "Herramientas", "Werkzeuge", "Strumenti", "Ferramentas");
            _navLabels[TabSettings].Text = Localizer.L("Paramètres", "Settings", "Ajustes", "Einstellungen", "Impostazioni", "Configurações");

            _btnElevate.Content = Localizer.L("🛡️ Mode Administrateur", "🛡️ Run as Admin", "🛡️ Modo Administrador", "🛡️ Als Admin starten", "🛡️ Avvia come Admin", "🛡️ Iniciar como Admin");

            RefreshHealthPageLabels();
            RefreshWinToolsPageLabels();
            RefreshOptimizeLabels();
            RefreshAiPageLabels();
            RefreshHomeLabels();
            RefreshCrashesLabels();
            RefreshSettingsLabels();
            RefreshToolsSubLabels();
            RefreshDriverLabels();
            RefreshHistoryLabels();
            RefreshReportLabels();



            // Page 2 (Fixes)
            _lblFixesTitle.Text = Localizer.L("Correctifs & Protections", "Fixes & Protections", "Correcciones y protecciones", "Korrekturen & Schutz", "Correzioni e protezioni", "Correções e proteções");
            _lblFixesSub.Text = Localizer.L(
                "Sauvegarde automatique du Registre avant toute modification. Annulable à tout moment.",
                "Automatic Registry snapshot saved before any tweak. Revertible anytime in one click.",
                "Copia automática del Registro antes de cualquier ajuste. Reversible en 1 clic.",
                "Automatische Registry-Sicherung vor jeder Änderung. Jederzeit umkehrbar.",
                "Backup automatico del Registro prima di ogni modifica. Annullabile in 1 clic.",
                "Backup automático do Registro antes de qualquer ajuste. Reversível em 1 clique.");
            _btnCreateRestorePt.Content = Localizer.L("🛡️ Point de restauration", "🛡️ Restore point", "🛡️ Punto de restauración", "🛡️ Wiederherstellungspunkt", "🛡️ Punto di ripristino", "🛡️ Ponto de restauração");
            _btnRestoreSnapshot.Content = Localizer.L("↺ Annuler les modifications", "↺ Rollback Fixes", "↺ Deshacer cambios", "↺ Änderungen zurücksetzen", "↺ Annulla modifiche", "↺ Desfazer alterações");
            _btnApplyAllRec.Content = Localizer.L("⚡ Appliquer les recommandés", "⚡ Apply recommended", "⚡ Aplicar recomendados", "⚡ Empfohlene anwenden", "⚡ Applica consigliati", "⚡ Aplicar recomendados");
            RefreshSnapshotButtonState();

            FixGroup[] groups = new[] { FixGroup.All, FixGroup.Gpu, FixGroup.BrowserApps, FixGroup.StoragePcie, FixGroup.SystemMaint };
            for (int i = 0; i < groups.Length; i++)
                _fixGroupBtns[i].Content = Localizer.FixGroupName(groups[i]);

            RefreshStressPageLabels();

            // Page 6 (Guide & Report)
            _btnCopyReport.Content = Localizer.L("📋 Copier le rapport", "📋 Copy report", "📋 Copiar informe", "📋 Bericht kopieren", "📋 Copia rapporto", "📋 Copiar relatório");
            _btnSaveReport.Content = Localizer.L("💾 Enregistrer (.txt)", "💾 Save (.txt)", "💾 Guardar (.txt)", "💾 Speichern (.txt)", "💾 Salva (.txt)", "💾 Salvar (.txt)");
            _btnExportFullReg.Content = Localizer.L("📄 Exporter pack .reg", "📄 Export .reg pack", "📄 Exportar .reg", "📄 .reg-Paket exportieren", "📄 Esporta .reg", "📄 Exportar .reg");

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

        private async void RunScanAsync()
        {
            _btnRescan.IsEnabled = false;
            SetToast(Localizer.L("Analyse en cours...", "Scanning system...", "Analizando el sistema...", "System wird geprüft...", "Analisi in corso...", "Analisando o sistema..."), "TextSecondary");

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
                SetToast(Localizer.L("L'analyse a échoué (détails dans le journal).", "Scan failed (see log for details).", "El análisis falló (ver registro).", "Prüfung fehlgeschlagen (siehe Protokoll).", "Analisi non riuscita (vedi log).", "A análise falhou (ver log)."), "DangerFg");
                return;
            }
            finally
            {
                _btnRescan.IsEnabled = true;
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
                Localizer.L(
                    string.Format("Dernière analyse à {0:HH:mm} — {1} crash(s) sur 30 jours.", DateTime.Now, crashCount),
                    string.Format("Last scan at {0:HH:mm} — {1} crash(es) in 30 days.", DateTime.Now, crashCount),
                    string.Format("Último análisis a las {0:HH:mm} — {1} fallo(s) en 30 días.", DateTime.Now, crashCount),
                    string.Format("Letzte Prüfung um {0:HH:mm} — {1} Absturz/Abstürze in 30 Tagen.", DateTime.Now, crashCount),
                    string.Format("Ultima analisi alle {0:HH:mm} — {1} crash in 30 giorni.", DateTime.Now, crashCount),
                    string.Format("Última análise às {0:HH:mm} — {1} falha(s) em 30 dias.", DateTime.Now, crashCount)),
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
                    _lblHwFixesStatus.Text = Localizer.L(
                        string.Format("🎯 Filtre actif ({0}) : {1} correctif(s) adaptés à votre matériel ({2} masqués).", hwTag, filtered.Count, hiddenCount),
                        string.Format("🎯 Active filter ({0}): {1} fix(es) tailored for your hardware ({2} hidden).", hwTag, filtered.Count, hiddenCount),
                        string.Format("🎯 Filtro activo ({0}): {1} corrección(es) para su hardware ({2} ocultas).", hwTag, filtered.Count, hiddenCount),
                        string.Format("🎯 Aktiver Filter ({0}): {1} passende Korrektur(en) ({2} ausgeblendet).", hwTag, filtered.Count, hiddenCount),
                        string.Format("🎯 Filtro attivo ({0}): {1} correzione/i per il vostro hardware ({2} nascoste).", hwTag, filtered.Count, hiddenCount),
                        string.Format("🎯 Filtro ativo ({0}): {1} correção(ões) para o seu hardware ({2} ocultas).", hwTag, filtered.Count, hiddenCount));

                    _btnToggleHardwareFixesFilter.Content = Localizer.L(
                        string.Format("Afficher tout ({0})", totalInGroup),
                        string.Format("Show all ({0})", totalInGroup),
                        string.Format("Mostrar todo ({0})", totalInGroup),
                        string.Format("Alle anzeigen ({0})", totalInGroup),
                        string.Format("Mostra tutto ({0})", totalInGroup),
                        string.Format("Mostrar tudo ({0})", totalInGroup));
                }
                else
                {
                    _lblHwFixesStatus.Text = Localizer.L(
                        string.Format("ℹ️ Affichage complet : {0} correctif(s) (tous constructeurs : AMD, Intel, NVIDIA).", filtered.Count),
                        string.Format("ℹ️ Showing all {0} fix(es) across all platforms (AMD, Intel, NVIDIA).", filtered.Count),
                        string.Format("ℹ️ Mostrando todas las {0} correcciones de todas las plataformas.", filtered.Count),
                        string.Format("ℹ️ Alle {0} Korrekturen anzeigen (alle Hersteller).", filtered.Count),
                        string.Format("ℹ️ Mostra tutte le {0} correzioni (tutti i produttori).", filtered.Count),
                        string.Format("ℹ️ Mostrando todas as {0} correções de todas as plataformas.", filtered.Count));

                    _btnToggleHardwareFixesFilter.Content = Localizer.L(
                        string.Format("🎯 Filtrer par mon matériel ({0})", hwTag),
                        string.Format("🎯 Filter by my hardware ({0})", hwTag),
                        string.Format("🎯 Filtrar por mi hardware ({0})", hwTag),
                        string.Format("🎯 Nach meiner Hardware filtern ({0})", hwTag),
                        string.Format("🎯 Filtra per il mio hardware ({0})", hwTag),
                        string.Format("🎯 Filtrar pelo meu hardware ({0})", hwTag));
                }
            }

            int recCount = filtered.Count(f => f.Status == FixStatus.Recommended);
            _btnApplyAllRec.IsEnabled = (recCount > 0);
            if (recCount > 0)
            {
                _btnApplyAllRec.Content = Localizer.L(
                    string.Format("⚡ Appliquer les recommandés ({0})", recCount),
                    string.Format("⚡ Apply recommended ({0})", recCount),
                    string.Format("⚡ Aplicar recomendados ({0})", recCount),
                    string.Format("⚡ Empfohlene anwenden ({0})", recCount),
                    string.Format("⚡ Applica consigliati ({0})", recCount),
                    string.Format("⚡ Aplicar recomendadas ({0})", recCount));
            }
            else
            {
                _btnApplyAllRec.Content = Localizer.L(
                    "⚡ Aucun correctif requis",
                    "⚡ No fix required",
                    "⚡ Sin correcciones requeridas",
                    "⚡ Keine Korrektur nötig",
                    "⚡ Nessuna correzione richiesta",
                    "⚡ Nenhuma correção necessária");
            }

            foreach (var fix in filtered)
                _fixesListPanel.Children.Add(BuildFixCard(fix, ExecuteSingleFix));
        }

        /// <summary>One fix / setting card (title, status, explanation, primary button, technical drawer). Shared with Optimiser.</summary>
        private Border BuildFixCard(RegistryFixItem fix, Action<string, string> run)
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
                }, "BgCard"), fix.Status == FixStatus.Recommended ? "WarnBorder" : "BorderCard");

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
                btnPrimary.Click += (s, e) => run(fixId + "_apply", fixId);
                right.Children.Add(btnPrimary);

                Border drawer = ResBg(new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 10, 0, 0),
                    Visibility = Visibility.Collapsed
                }, "BgSubtle");

                Button btnMore = CreateFlatButton("▾", "BgSubtle", "TextSecondary", "BorderCard", new Thickness(9, 6, 9, 6), 12);
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
                    Text = Localizer.L("État actuel : ", "Current state: ", "Estado actual: ", "Aktueller Status: ", "Stato attuale: ", "Estado atual: ") + fix.CurrentValue,
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
                    btnRest.Click += (s, e) => run(fixId + "_restore", fixId);
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
                    _lblHwGuideStatus.Text = Localizer.L(
                        string.Format("🎯 Guide personnalisé ({0}) : {1} fiche(s) pertinentes ({2} masquées).", hwTag, entries.Count, hiddenCount),
                        string.Format("🎯 Targeted Guide ({0}): {1} article(s) for your hardware ({2} hidden).", hwTag, entries.Count, hiddenCount),
                        string.Format("🎯 Guía personalizada ({0}): {1} artículo(s) relevantes ({2} ocultos).", hwTag, entries.Count, hiddenCount),
                        string.Format("🎯 Personalisierter Ratgeber ({0}): {1} passende(r) Artikel ({2} ausgeblendet).", hwTag, entries.Count, hiddenCount),
                        string.Format("🎯 Guida personalizzata ({0}): {1} articolo/i per il vostro hardware ({2} nascosti).", hwTag, entries.Count, hiddenCount),
                        string.Format("🎯 Guia personalizado ({0}): {1} artigo(s) para o seu hardware ({2} ocultos).", hwTag, entries.Count, hiddenCount));

                    _btnToggleHardwareGuideFilter.Content = Localizer.L(
                        string.Format("Afficher tout ({0})", allEntries.Count),
                        string.Format("Show all ({0})", allEntries.Count),
                        string.Format("Mostrar todo ({0})", allEntries.Count),
                        string.Format("Alle anzeigen ({0})", allEntries.Count),
                        string.Format("Mostra tutto ({0})", allEntries.Count),
                        string.Format("Mostrar tudo ({0})", allEntries.Count));
                }
                else
                {
                    _lblHwGuideStatus.Text = Localizer.L(
                        string.Format("ℹ️ Guide universel : {0} fiche(s) (tous composants : Intel, AMD, NVIDIA).", entries.Count),
                        string.Format("ℹ️ Universal Guide: {0} article(s) across all architectures.", entries.Count),
                        string.Format("ℹ️ Guía universal: {0} artículos de todas las plataformas.", entries.Count),
                        string.Format("ℹ️ Vollständiger Ratgeber: {0} Artikel (alle Plattformen).", entries.Count),
                        string.Format("ℹ️ Guida universale: {0} articoli (tutte le architetture).", entries.Count),
                        string.Format("ℹ️ Guia universal: {0} artigos de todas as plataformas.", entries.Count));

                    _btnToggleHardwareGuideFilter.Content = Localizer.L(
                        string.Format("🎯 Cibler mon matériel ({0})", hwTag),
                        string.Format("🎯 Target my hardware ({0})", hwTag),
                        string.Format("🎯 Filtrar por mi hardware ({0})", hwTag),
                        string.Format("🎯 Nach meiner Hardware filtern ({0})", hwTag),
                        string.Format("🎯 Filtra per il mio hardware ({0})", hwTag),
                        string.Format("🎯 Filtrar pelo meu hardware ({0})", hwTag));
                }
            }

            for (int idx = 0; idx < entries.Count; idx++)
            {
                var k = entries[idx];
                bool openDefault = (idx == 0);
                Brush tagBrush = ThemeManager.Hex(k.BadgeColorHex);

                Border card = ResBorder(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 13, 16, 13),
                    Margin = new Thickness(0, 0, 0, 8),
                    Cursor = Cursors.Hand
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
                body.Children.Add(MakeKbParagraph(Localizer.L("Symptômes : ", "Symptoms: ", "Síntomas: ", "Symptome: ", "Sintomi: ", "Sintomas: "), k.Symptoms, "TextSecondary"));
                body.Children.Add(MakeKbParagraph(Localizer.L("Cause : ", "Root cause: ", "Causa: ", "Ursache: ", "Causa: ", "Causa: "), k.Cause, "WarnFg"));
                body.Children.Add(MakeKbParagraph(Localizer.L("Solution : ", "How to fix: ", "Solución: ", "Lösung: ", "Soluzione: ", "Solução: "), k.Solution, "SuccessFg"));
                sp.Children.Add(body);

                card.Child = sp;
                card.MouseLeftButtonUp += (s, e) =>
                {
                    bool show = body.Visibility != Visibility.Visible;
                    body.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                    chev.Text = show ? "▴" : "▾";
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
                ? new FixOutcome { Ok = false, MessageFr = "Aucun résultat.", MessageEn = "No result." }
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
                Message = r.Message,
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
                case "restore_snapshot": return Localizer.L("Restauration de la sauvegarde initiale du Registre", "Initial Registry snapshot restored", "Restauración del Registro inicial", "Ursprüngliche Registry wiederhergestellt", "Ripristino del Registro iniziale", "Restauração do Registro inicial");
                case "create_restore_point": return Localizer.L("Point de restauration Windows", "Windows restore point", "Punto de restauración", "Wiederherstellungspunkt", "Punto di ripristino", "Ponto de restauração");
                case "crashdump_apply": return Localizer.L("Réactivation de l'enregistrement des écrans bleus", "Blue screen recording turned back on", "Registro de pantallas azules reactivado", "Bluescreen-Aufzeichnung wieder aktiviert", "Registrazione schermate blu riattivata", "Registro de telas azuis reativado");
                case "disable_risky_oem": return Localizer.L("Neutralisation des logiciels constructeur à risque", "Risky OEM software neutralized", "Software OEM neutralizado", "Riskante OEM-Software neutralisiert", "Software OEM neutralizzato", "Software OEM neutralizado");
                case "disable_ryzen_igpu": return Localizer.L("Service AMD External Events désactivé", "AMD External Events service disabled", "Servicio AMD External Events desactivado", "AMD External Events-Dienst deaktiviert", "Servizio AMD External Events disattivato", "Serviço AMD External Events desativado");
                case "disable_autohdr": return Localizer.L("Auto HDR désactivé", "Auto HDR disabled", "Auto HDR desactivado", "Auto HDR deaktiviert", "Auto HDR disattivato", "Auto HDR desativado");
            }
            if (op.EndsWith("_restore", StringComparison.Ordinal))
                return Localizer.L("Remis par défaut : ", "Reset to default: ", "Restablecido: ", "Zurückgesetzt: ", "Ripristinato: ", "Restaurado: ") + title;
            return title;
        }

        /// <summary>Closes Chrome/Edge (after confirmation) before fixes that rewrite their settings. False = user declined.</summary>
        private async Task<bool> ConfirmBrowserClose(string fixId)
        {
            string proc = fixId == "chrome_graphite" ? "chrome" : fixId == "edge_graphite" ? "msedge" : null;
            if (proc == null || !DiagnosticsEngine.IsProcessRunning(proc)) return true;
            string name = proc == "chrome" ? "Google Chrome" : "Microsoft Edge";
            var ans = MessageBox.Show(this,
                Localizer.L("Fermer " + name + " proprement pour appliquer ce réglage ?", "Close " + name + " gracefully to apply this setting?", "¿Cerrar " + name + " para aplicar este ajuste?", name + " schließen, um diese Einstellung anzuwenden?", "Chiudere " + name + " per applicare l'impostazione?", "Fechar o " + name + " para aplicar este ajuste?"),
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
            get { return Localizer.L("Application en cours…", "Applying…", "Aplicando…", "Wird angewendet…", "Applicazione in corso…", "Aplicando…"); }
        }

        private async void ExecuteSingleFix(string opCode, string fixId)
        {
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

        private async void OnApplyAllRecommendedClick(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null) return;
            var pending = _lastResult.Fixes
                .Where(f => f.Status == FixStatus.Recommended && (!_onlyHardwareRelevantFixes || f.IsApplicableTo(_lastResult.Hardware)))
                .ToList();
            if (pending.Count == 0)
            {
                SetToast(Localizer.L("Tous les correctifs recommandés sont déjà actifs.", "All recommended fixes are already active.", "Todas las correcciones recomendadas ya están activas.", "Alle empfohlenen Korrekturen sind bereits aktiv.", "Tutte le correzioni consigliate sono già attive.", "Todas as correções recomendadas já estão ativas."), "SuccessFg");
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
                _lastResult.Fixes = await Task.Run(() => DiagnosticsEngine.InspectAllFixes(false));
                RenderScanResult(_lastResult);
            }

            int applied = results.Count(r => r.Ok), failed = results.Count - applied;
            foreach (var r in results.Where(r => !r.Ok)) Log.Warn("Fix " + r.Op + " failed: " + r.MessageEn);
            if (failed == 0)
                SetToast(Localizer.L(
                    applied + " correctif(s) appliqué(s). Chacun est annulable dans Outils › Historique.",
                    applied + " fix(es) applied. Each one can be undone in Tools › History.",
                    applied + " corrección(es) aplicada(s).",
                    applied + " Korrektur(en) angewendet.",
                    applied + " correzione/i applicata/e.",
                    applied + " correção(ões) aplicada(s)."), "SuccessFg");
            else
                SetToast(Localizer.L(
                    applied + " correctif(s) appliqué(s), " + failed + " en échec.",
                    applied + " fix(es) applied, " + failed + " failed.",
                    applied + " aplicada(s), " + failed + " fallida(s).",
                    applied + " angewendet, " + failed + " fehlgeschlagen.",
                    applied + " applicata/e, " + failed + " non riuscita/e.",
                    applied + " aplicada(s), " + failed + " com falha."), "WarnFg");
        }
        private void SaveRegSnippet(string name, string content)
        {
            SaveFileDialog sfd = new SaveFileDialog { FileName = "fix_" + name + ".reg", Filter = "Registry File (*.reg)|*.reg" };
            if (sfd.ShowDialog(this) == true)
            {
                File.WriteAllText(sfd.FileName, content, Encoding.ASCII);
                SetToast("Saved: " + sfd.FileName, "SuccessFg");
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
                File.WriteAllText(sfd.FileName, _txtReportBox.Text ?? "", Encoding.UTF8);
                SetToast("Saved: " + sfd.FileName, "SuccessFg");
            }
        }

        private void OnElevateClick(object sender, RoutedEventArgs e)
        {
            if (Elevation.RestartAppElevated())
                Application.Current.Shutdown();
            else
                SetToast(Localizer.L("Élévation annulée.", "Elevation cancelled.", "Elevación cancelada.", "Erhöhung abgebrochen.", "Elevazione annullata.", "Elevação cancelada."), "WarnFg");
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
