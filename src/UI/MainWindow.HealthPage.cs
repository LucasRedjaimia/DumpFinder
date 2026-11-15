using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace WinCrashDoctor
{
    public partial class MainWindow : Window
    {
        private int _healthSubTab = 0; // 0 = Hardware & BIOS, 1 = Conflicts & Drivers, 2 = Minidumps (.dmp)
        private List<DiskSmartInfo> _cachedAdminSmart;

        // Page 1 Controls (Health, Conflicts & Minidumps)
        private Grid _pageHealth;
        private TextBlock _lblHealthTitle;
        private TextBlock _lblHealthSub;
        private Button _btnHealthSubHw;
        private Button _btnHealthSubConf;
        private Button _btnHealthSubStress;

        // Sub 0: Hardware & BIOS
        private ScrollViewer _svHealthHw;
        private StackPanel _healthHwPanel;
        private Border _hwAdminBanner;
        private TextBlock _lblHwAdminHint;
        private Button _btnHwAdminScan;
        private StackPanel _hwCardsContainer;

        // Sub 1: Software Conflicts & Remnants
        private ScrollViewer _svHealthConf;
        private StackPanel _healthConfPanel;

        // Crashs page: .dmp tools (built in MainWindow.CrashesPage.cs)
        private Button _btnOpenDmpFile;
        private Button _btnAdminScanDumps;
        private bool _adminDeepScanRunning;

        // Tray Sentinel State
        private TraySentinel _traySentinel;
        private bool _sentinelEnabled = false;
        private bool _forceExit = false;
        private Button _btnSentinelToggle;

        // Fixes Header Extra Buttons
        private Button _btnCreateRestorePt;
        private Button _btnRestoreSnapshot;

        private void InitTraySentinel(bool initialEnabled)
        {
            _sentinelEnabled = initialEnabled;
            _traySentinel = new TraySentinel(
                onOpen: () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    Show();
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                })),
                onRescan: () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    // First Show() of a tray-started window raises Loaded, which scans already:
                    // only rescan explicitly when the window was loaded before.
                    bool wasLoaded = _loadedOnce;
                    Show();
                    if (WindowState == WindowState.Minimized)
                        WindowState = WindowState.Normal;
                    Activate();
                    if (wasLoaded) RunScanAsync();
                })),
                onExit: () => Dispatcher.BeginInvoke(new Action(() =>
                {
                    _forceExit = true;
                    Close();
                })),
                onNavigate: NavigateFromTray);

            if (_sentinelEnabled)
            {
                _traySentinel.SetEnabled(true);
            }
        }

        private void ToggleSentinel()
        {
            _sentinelEnabled = !_sentinelEnabled;
            if (_traySentinel != null)
                _traySentinel.SetEnabled(_sentinelEnabled);

            UpdateSentinelButtonVisual();
            SaveCurrentPreferences();

            SetToast(
                _sentinelEnabled
                    ? Localizer.S("health.sentinel_enabled_background_tray_monitor")
                    : Localizer.S("health.sentinel_disabled"),
                _sentinelEnabled ? "SuccessFg" : "TextMuted");
        }

        private void UpdateSentinelButtonVisual()
        {
            if (_btnSentinelToggle == null) return;

            string label = _sentinelEnabled
                ? Localizer.S("health.sentinel_active")
                : Localizer.S("health.sentinel_off");

            _btnSentinelToggle.Content = label;
            _btnSentinelToggle.SetResourceReference(Button.BackgroundProperty, _sentinelEnabled ? "SuccessSoftBg" : "BgCard");
            _btnSentinelToggle.SetResourceReference(Button.ForegroundProperty, _sentinelEnabled ? "SuccessFg" : "TextSecondary");
            _btnSentinelToggle.SetResourceReference(Button.BorderBrushProperty, _sentinelEnabled ? "SuccessBorder" : "BorderCard");

            if (_traySentinel != null)
                _traySentinel.RefreshMenuLocalization();
        }

        private void SaveCurrentPreferences()
        {
            Localizer.SaveSettings(Localizer.Current, ThemeManager.CurrentMode, _sentinelEnabled);
        }

        // ==================== PAGE 1: HEALTH, CONFLICTS & MINIDUMPS ====================
        private void BuildHealthPage()
        {
            _pageHealth = new Grid { Visibility = Visibility.Collapsed };
            _pageHealth.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header + 3-pill Segmented Switch
            _pageHealth.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Sub-view Content

            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            StackPanel left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            _lblHealthTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblHealthSub = ResFg(new TextBlock { FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
            left.Children.Add(_lblHealthTitle);
            left.Children.Add(_lblHealthSub);
            Grid.SetColumn(left, 0);
            hdr.Children.Add(left);

            StackPanel seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnHealthSubHw = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(12, 6, 12, 6), 12.2);
            _btnHealthSubHw.FontWeight = FontWeights.SemiBold;
            _btnHealthSubHw.Margin = new Thickness(0, 0, 6, 0);
            _btnHealthSubHw.Click += (s, e) => SwitchHealthSubTab(0);

            _btnHealthSubConf = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12.2);
            _btnHealthSubConf.FontWeight = FontWeights.SemiBold;
            _btnHealthSubConf.Margin = new Thickness(0, 0, 6, 0);
            _btnHealthSubConf.Click += (s, e) => SwitchHealthSubTab(1);

            _btnHealthSubStress = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12.2);
            _btnHealthSubStress.FontWeight = FontWeights.SemiBold;
            _btnHealthSubStress.Click += (s, e) => SwitchHealthSubTab(2);

            seg.Children.Add(_btnHealthSubHw);
            seg.Children.Add(_btnHealthSubConf);
            seg.Children.Add(_btnHealthSubStress);
            Grid.SetColumn(seg, 1);
            hdr.Children.Add(seg);
            Grid.SetRow(hdr, 0);
            _pageHealth.Children.Add(hdr);

            // Sub 0: Hardware & BIOS
            _svHealthHw = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _healthHwPanel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };

            _hwAdminBanner = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 10),
                Visibility = Visibility.Collapsed
            }, "AccentSoftBg"), "BorderStrong");

            Grid bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblHwAdminHint = ResFg(new TextBlock
            {
                FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 12, 0)
            }, "TextPrimary");
            Grid.SetColumn(_lblHwAdminHint, 0);
            bannerGrid.Children.Add(_lblHwAdminHint);

            _btnHwAdminScan = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(12, 5, 12, 5), 11.8);
            _btnHwAdminScan.FontWeight = FontWeights.SemiBold;
            _btnHwAdminScan.VerticalAlignment = VerticalAlignment.Center;
            _btnHwAdminScan.Click += OnRunAdminDeepScanClick;
            Grid.SetColumn(_btnHwAdminScan, 1);
            bannerGrid.Children.Add(_btnHwAdminScan);
            _hwAdminBanner.Child = bannerGrid;

            _healthHwPanel.Children.Add(_hwAdminBanner);
            _hwCardsContainer = new StackPanel();
            _healthHwPanel.Children.Add(_hwCardsContainer);
            _svHealthHw.Content = _healthHwPanel;
            Grid.SetRow(_svHealthHw, 1);
            _pageHealth.Children.Add(_svHealthHw);

            // Sub 1: Software Conflicts & Remnants
            _svHealthConf = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
            _healthConfPanel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            _svHealthConf.Content = _healthConfPanel;
            Grid.SetRow(_svHealthConf, 1);
            _pageHealth.Children.Add(_svHealthConf);

        }

        public void SwitchHealthSubTab(int sub)
        {
            _healthSubTab = sub;
            _svHealthHw.Visibility = (sub == 0) ? Visibility.Visible : Visibility.Collapsed;
            _svHealthConf.Visibility = (sub == 1) ? Visibility.Visible : Visibility.Collapsed;
            _pageStress.Visibility = (sub == 2) ? Visibility.Visible : Visibility.Collapsed;
            if (_svHealthDrivers != null) _svHealthDrivers.Visibility = (sub == 3) ? Visibility.Visible : Visibility.Collapsed;
            if (_btnHealthSubDrivers != null) StyleTogglePill(_btnHealthSubDrivers, sub == 3);
            FrameworkElement[] views = { _svHealthHw, _svHealthConf, _pageStress, _svHealthDrivers };
            AnimatePageIn(views[Math.Max(0, Math.Min(3, sub))]);

            StyleTogglePill(_btnHealthSubHw, sub == 0);
            StyleTogglePill(_btnHealthSubConf, sub == 1);
            StyleTogglePill(_btnHealthSubStress, sub == 2);
        }

        private void RefreshHealthPageLabels()
        {
            if (_lblHealthTitle == null) return;

            _lblHealthTitle.Text = Localizer.S("main_window.hardware");
            _lblHealthSub.Text = Localizer.S("health.bios_memory_pcie_link_ssd_software");

            _btnHealthSubHw.Content = Localizer.S("health.hardware_bios");
            _btnHealthSubConf.Content = Localizer.S("health.conflicts_drivers");

            _btnHealthSubStress.Content = Localizer.S("health.stability_test");

            _lblHwAdminHint.Text = Localizer.S("health.unlock_deep_nvme_smart_telemetry_tempera");

            _btnHwAdminScan.Content = Localizer.S("health.admin_smart_minidump_scan");
        }

        private void RenderHealthAndDumps(ScanResult res)
        {
            if (_hwCardsContainer == null || res == null) return;

            bool showAdminBanner = !res.Hardware.IsAdmin && _cachedAdminSmart == null;
            _hwAdminBanner.Visibility = showAdminBanner ? Visibility.Visible : Visibility.Collapsed;

            // 1. Hardware Cards
            _hwCardsContainer.Children.Clear();
            if (res.HardwareChecks != null)
            {
                foreach (var hc in res.HardwareChecks)
                {
                    _hwCardsContainer.Children.Add(BuildHealthInfoCard(hc));
                }
            }

            // 2. Software Conflict Cards
            _healthConfPanel.Children.Clear();
            if (res.ConflictChecks != null)
            {
                foreach (var cc in res.ConflictChecks)
                {
                    _healthConfPanel.Children.Add(BuildHealthInfoCard(cc));
                }
            }

            // Update Sidebar Health Badge
            int hwWarn = (res.HardwareChecks != null ? res.HardwareChecks.Count(h => h.Level == HealthLevel.Warning || h.Level == HealthLevel.Critical) : 0)
                       + (res.ConflictChecks != null ? res.ConflictChecks.Count(c => c.Level == HealthLevel.Warning || c.Level == HealthLevel.Critical) : 0);

            if (_navHealthBadge != null)
            {
                _navHealthBadge.Visibility = hwWarn > 0 ? Visibility.Visible : Visibility.Collapsed; RefreshNavBadgesForCollapse();
                _lblNavHealthCount.Text = hwWarn.ToString();
            }
        }

        private UIElement BuildHealthInfoCard(IHealthCardData item)
        {
            if (item == null) return new Border();

            HealthLevel level = item.Level;
            string bBg = "BgSubtle", bFg = "TextSecondary", bBd = "BorderCard", cardBd = "BorderCard";
            switch (level)
            {
                case HealthLevel.Good:
                    bBg = "SuccessSoftBg"; bFg = "SuccessFg"; bBd = "SuccessBorder"; break;
                case HealthLevel.Warning:
                    bBg = "WarnSoftBg"; bFg = "WarnFg"; bBd = "WarnBorder"; cardBd = "WarnBorder"; break;
                case HealthLevel.Critical:
                    bBg = "DangerSoftBg"; bFg = "DangerFg"; bBd = "DangerFg"; cardBd = "DangerFg"; break;
                case HealthLevel.Info:
                    bBg = "AccentSoftBg"; bFg = "AccentText"; bBd = "BorderCard"; break;
            }

            Border card = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 13, 16, 13),
                Margin = new Thickness(0, 0, 0, 10)
            }, "BgCard"), cardBd);

            StackPanel sp = new StackPanel();
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            WrapPanel titleRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            titleRow.Children.Add(ResBg(new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 9, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Child = ResFg(new TextBlock { Text = item.Tag, FontSize = 11, FontWeight = FontWeights.Bold }, "AccentText")
            }, "AccentSoftBg"));

            titleRow.Children.Add(ResFg(new TextBlock
            {
                Text = item.Title,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            }, "TextPrimary"));

            Grid.SetColumn(titleRow, 0);
            top.Children.Add(titleRow);

            Border badge = ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = ResFg(new TextBlock { Text = Localizer.HealthBadgeText(level), FontSize = 11, FontWeight = FontWeights.SemiBold }, bFg)
            }, bBg), bBd);
            Grid.SetColumn(badge, 1);
            top.Children.Add(badge);
            sp.Children.Add(top);

            sp.Children.Add(ResFg(new TextBlock
            {
                Text = item.ValueSummary,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 7, 0, 4),
                TextWrapping = TextWrapping.Wrap
            }, "TextPrimary"));

            sp.Children.Add(ResFg(new TextBlock
            {
                Text = item.Detail,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            }, "TextSecondary"));

            // Interactive Actions Bar (In-App Fix, Guide Drawer, Quick Tool Action)
            bool hasInAppFix = !string.IsNullOrEmpty(item.InAppFixOp);
            bool hasGuide = !string.IsNullOrEmpty(item.GuideSteps);
            bool hasQuick = !string.IsNullOrEmpty(item.QuickActionLabel);

            if (hasInAppFix || hasGuide || hasQuick)
            {
                WrapPanel actionsRow = new WrapPanel { Margin = new Thickness(0, 11, 0, 0) };

                // 1. In-App Remediation Button or "Already Applied" badge
                if (item.InAppFixApplied)
                {
                    Border appliedBadge = ResBorder(ResBg(new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(10, 5, 10, 5),
                        Margin = new Thickness(0, 0, 8, 4),
                        VerticalAlignment = VerticalAlignment.Center,
                        Child = ResFg(new TextBlock
                        {
                            Text = Localizer.S("health.optimized_in_app"),
                            FontSize = 11,
                            FontWeight = FontWeights.SemiBold
                        }, "SuccessFg")
                    }, "SuccessSoftBg"), "SuccessBorder");
                    actionsRow.Children.Add(appliedBadge);
                }
                else if (hasInAppFix)
                {
                    Button btnFix = CreateFlatButton(
                        item.InAppFixLabel ?? Localizer.S("health.fix_in_app"),
                        "Accent", "OnAccent", "Accent",
                        new Thickness(12, 6, 12, 6), 11.5);
                    btnFix.FontWeight = FontWeights.SemiBold;
                    btnFix.Margin = new Thickness(0, 0, 8, 4);
                    btnFix.Click += (s, e) => ExecuteInAppHealthFix(item, s);
                    actionsRow.Children.Add(btnFix);
                }

                // 2. Expandable Accordion Step-by-Step Guide
                Button btnGuide = null;
                Border guideDrawer = null;
                if (hasGuide)
                {
                    btnGuide = CreateFlatButton(
                        Localizer.S("health.learn_more"),
                        "BgSubtle", "TextPrimary", "BorderCard",
                        new Thickness(11, 6, 11, 6), 11.5);
                    btnGuide.FontWeight = FontWeights.SemiBold;
                    btnGuide.Margin = new Thickness(0, 0, 8, 4);

                    guideDrawer = ResBorder(ResBg(new Border
                    {
                        CornerRadius = new CornerRadius(8),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(14, 12, 14, 12),
                        Margin = new Thickness(0, 8, 0, 2),
                        Visibility = Visibility.Collapsed
                    }, "BgSubtle"), "BorderCard");

                    StackPanel guideSp = new StackPanel();
                    if (!string.IsNullOrEmpty(item.GuideTitle))
                    {
                        guideSp.Children.Add(ResFg(new TextBlock
                        {
                            Text = item.GuideTitle,
                            FontSize = 12,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 0, 0, 6)
                        }, "TextPrimary"));
                    }

                    guideSp.Children.Add(ResFg(new TextBlock
                    {
                        Text = item.GuideSteps,
                        FontSize = 12,
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 18
                    }, "TextSecondary"));

                    guideDrawer.Child = guideSp;

                    bool isExpanded = false;
                    btnGuide.Click += (s, e) =>
                    {
                        isExpanded = !isExpanded;
                        guideDrawer.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
                        btnGuide.Content = isExpanded
                            ? Localizer.S("health.hide_guide")
                            : Localizer.S("health.learn_more");
                    };

                    actionsRow.Children.Add(btnGuide);
                }

                // 3. Quick Action Button (launch tool / settings / search)
                if (hasQuick)
                {
                    Button btnQuick = CreateFlatButton(
                        item.QuickActionLabel,
                        "BgSubtle", "TextSecondary", "BorderCard",
                        new Thickness(11, 6, 11, 6), 11.5);
                    btnQuick.FontWeight = FontWeights.Normal;
                    btnQuick.Margin = new Thickness(0, 0, 8, 4);
                    btnQuick.Click += (s, e) => ExecuteQuickAction(item.QuickActionCommand, item.WebSearchQuery);
                    actionsRow.Children.Add(btnQuick);
                }

                sp.Children.Add(actionsRow);

                if (guideDrawer != null)
                {
                    sp.Children.Add(guideDrawer);
                }
            }

            card.Child = sp;
            return card;
        }

        private async void ExecuteInAppHealthFix(IHealthCardData item, object sender = null)
        {
            if (item == null || string.IsNullOrEmpty(item.InAppFixOp)) return;

            // "Already the latest BIOS": a local preference, not a system change.
            if (item.InAppFixOp.StartsWith(SystemHealthScanner.BiosAckOp, StringComparison.Ordinal))
            {
                AppSettings st = Localizer.CachedSettings ?? Localizer.LoadSettings();
                st.BiosLatestAck = item.InAppFixOp.Substring(SystemHealthScanner.BiosAckOp.Length);
                Localizer.SaveFullSettings(st);
                SetToast(Localizer.S("health.noted_the_bios_warning_only_comes"), "SuccessFg");
                RunScanAsync();
                return;
            }

            FixOutcome outcome;
            using (IDisposable fixLock = TryBeginFix(sender))
            {
                if (fixLock == null) return;
                using (BeginBusy(PendingFixMessage))
                {
                    outcome = await RunFixAsync(item.InAppFixOp);
                }
            }

            if (outcome.Ok)
            {
                item.InAppFixApplied = true;
                string toastMsg = outcome.Message;
                SetToast(string.IsNullOrEmpty(toastMsg) ? "✓ Correctif appliqué !" : toastMsg, "SuccessFg");
                // Rescan to refresh entire UI state cleanly
                RunScanAsync();
            }
            else
            {
                string toastErr = outcome.Message;
                SetToast(string.IsNullOrEmpty(toastErr) ? "Échec de l'application du correctif." : toastErr, "DangerFg");
            }
        }

        private void ExecuteQuickAction(string cmd, string webQuery)
        {
            try
            {
                // In-app navigation: handled here, never passed to the shell (even when unknown).
                if (TryNavigate(cmd)) return;

                if (!string.IsNullOrEmpty(webQuery))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = webQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                            ? webQuery
                            : ("https://www.google.com/search?q=" + Uri.EscapeDataString(webQuery)),
                        UseShellExecute = true
                    });
                    return;
                }

                if (!string.IsNullOrEmpty(cmd))
                {
                    if (cmd.StartsWith("tool:"))
                    {
                        string toolId = cmd.Substring(5).Trim();
                        switch (toolId.ToLowerInvariant())
                        {
                            case "devmgmt":
                                Process.Start(new ProcessStartInfo { FileName = "devmgmt.msc", UseShellExecute = true });
                                return;
                            case "diskmgmt":
                                Process.Start(new ProcessStartInfo { FileName = "diskmgmt.msc", UseShellExecute = true });
                                return;
                            case "appsfeatures":
                                Process.Start(new ProcessStartInfo { FileName = "ms-settings:appsfeatures", UseShellExecute = true });
                                return;
                            case "device_security":
                                Process.Start(new ProcessStartInfo { FileName = "windowsdefender:device-security", UseShellExecute = true });
                                return;
                            case "ncpa":
                                Process.Start(new ProcessStartInfo { FileName = "ncpa.cpl", UseShellExecute = true });
                                return;
                            case "mdsched":
                                Process.Start(new ProcessStartInfo { FileName = "mdsched.exe", UseShellExecute = true });
                                return;
                        }
                    }

                    Process.Start(new ProcessStartInfo
                    {
                        FileName = cmd,
                        UseShellExecute = true
                    });
                }
            }
            catch (Exception ex)
            {
                SetToast(Localizer.S("health.unable_to_open_action") + ex.Message, "DangerFg");
            }
        }

        private async void OnOpenDumpFileClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog
            {
                Title = "Analyze Windows Crash Dump (.dmp)",
                Filter = "Crash Dump (*.dmp)|*.dmp|All files (*.*)|*.*"
            };
            if (ofd.ShowDialog(this) == true)
            {
                string path = ofd.FileName;
                MinidumpReportItem item;
                using (BeginBusy(Localizer.S("health.analyzing_dump")))
                {
                    item = await Task.Run(() => MinidumpAnalyzer.AnalyzeDumpFile(path));
                }
                if (item == null)
                {
                    SetToast(Localizer.S("health.could_not_decode_this_dmp_file"), "DangerFg");
                    return;
                }

                if (_lastResult != null)
                {
                    if (_lastResult.Minidumps == null) _lastResult.Minidumps = new List<MinidumpReportItem>();
                    _lastResult.Minidumps.RemoveAll(d => string.Equals(d.FullPath, item.FullPath, StringComparison.OrdinalIgnoreCase));
                    _lastResult.Minidumps.Insert(0, item);
                    RebuildCrashEntries(_lastResult);
                    _txtReportBox.Text = GeneratePlainReportV4(_lastResult);
                    var entry = _crashEntries.FirstOrDefault(c => c.Dump == item);
                    if (entry != null) OpenCrash(entry.Key);
                }
                SetToast(
                    Localizer.F("health.dump_decoded_culprit_module", item.PrimaryCulpritModule, item.BugCheckOrException),
                    "SuccessFg");
            }
        }

        private async void OnRunAdminDeepScanClick(object sender, RoutedEventArgs e)
        {
            if (_lastResult == null || _adminDeepScanRunning) return;
            string jsonPath = Path.Combine(Localizer.AppDataDir, "admin_deep_scan.json");
            try { if (File.Exists(jsonPath)) File.Delete(jsonPath); } catch (Exception ex) { Log.Warn(ex, jsonPath); }

            _adminDeepScanRunning = true;
            _btnHwAdminScan.IsEnabled = false;
            _btnAdminScanDumps.IsEnabled = false;

            bool ok;
            try
            {
                ok = await Task.Run(() =>
                    Elevation.RunSelf("--scan-admin-json \"" + jsonPath + "\"", 60000) == ElevationOutcome.Success
                    && File.Exists(jsonPath));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Admin deep scan");
                ok = false;
            }
            finally
            {
                _adminDeepScanRunning = false;
                _btnHwAdminScan.IsEnabled = true;
                _btnAdminScanDumps.IsEnabled = true;
            }

            if (!ok)
            {
                SetToast(Localizer.S("health.administrator_scan_cancelled"), "WarnFg");
                return;
            }

            try
            {
                var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
                var payload = ser.Deserialize<AdminDeepScanPayload>(File.ReadAllText(jsonPath, Encoding.UTF8));
                if (payload != null)
                {
                    _cachedAdminSmart = payload.SmartCounters;
                    ScanResult res = _lastResult;
                    if (res == null) return;
                    // WMI + nvidia-smi: slow, keep it off the UI thread (we resume on it after the await).
                    HardwareSummary hw = res.Hardware;
                    List<DiskSmartInfo> smart = _cachedAdminSmart;
                    var checks = await Task.Run(() => SystemHealthScanner.ScanHardwareHealth(hw, smart));
                    res.HardwareChecks = checks;
                    res.MinidumpFolderNeedsAdmin = false;

                    if (payload.KernelDumps != null)
                    {
                        if (res.Minidumps == null) res.Minidumps = new List<MinidumpReportItem>();
                        foreach (var kd in payload.KernelDumps)
                        {
                            kd.CrashTime = History.Local(kd.CrashTime);
                            if (!res.Minidumps.Any(x => string.Equals(x.FullPath, kd.FullPath, StringComparison.OrdinalIgnoreCase)))
                                res.Minidumps.Add(kd);
                        }
                        res.Minidumps = res.Minidumps.OrderByDescending(d => d.CrashTime).ToList();
                    }

                    // A rescan may have replaced the result during the hardware probe.
                    if (ReferenceEquals(res, _lastResult)) RenderScanResult(res);
                    SetToast(Localizer.S("health.deep_smart_telemetry_and_c_windows"), "SuccessFg");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Admin deep scan payload");
                SetToast("Error: " + ex.Message, "DangerFg");
            }
        }

        // ==================== RESTORE POINT & SNAPSHOT BACKUP HANDLERS ====================
        private void RefreshSnapshotButtonState()
        {
            if (_btnRestoreSnapshot == null) return;
            bool hasSnap = BackupManager.HasSnapshot();
            _btnRestoreSnapshot.Visibility = hasSnap ? Visibility.Visible : Visibility.Collapsed;
            if (hasSnap)
            {
                string dt = BackupManager.GetSnapshotDateDisplay();
                _btnRestoreSnapshot.ToolTip = Localizer.F("health.restore_initial_registry_keys_backed_up", dt);
            }
        }

        private async void OnCreateRestorePointClick(object sender, RoutedEventArgs e)
        {
            // The fix slot disables the button and re-enables it when disposed (even on exceptions).
            using (IDisposable fixLock = TryBeginFix(_btnCreateRestorePt))
            {
                if (fixLock == null) return;
                SetToast(Localizer.S("health.creating_windows_system_restore_point"), "AccentText");

                string mFr = "", mEn = "";
                bool ok = await Task.Run(() => DiagnosticsEngine.ExecuteFixOperation("create_restore_point", out mFr, out mEn));

                SetToast(Localizer.T(mFr, mEn), ok ? "SuccessFg" : "WarnFg");
            }
        }

        private async void OnRestoreSnapshotClick(object sender, RoutedEventArgs e)
        {
            if (!BackupManager.HasSnapshot()) return;
            string dt = BackupManager.GetSnapshotDateDisplay();
            var ans = MessageBox.Show(this,
                Localizer.F("health.restore_all_registry_keys_to_their", dt),
                "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (ans != MessageBoxResult.Yes) return;

            using (IDisposable fixLock = TryBeginFix(sender))
            {
                if (fixLock == null) return;
                using (BeginBusy(PendingFixMessage))
                {
                    FixOutcome outcome = await RunFixAsync("restore_snapshot");
                    SetToast(outcome.Message, outcome.Ok ? "SuccessFg" : "DangerFg");

                    if (outcome.Ok && _lastResult != null)
                    {
                        _lastResult.Fixes = await Task.Run(() => DiagnosticsEngine.InspectAllFixes(false));
                        RenderScanResult(_lastResult);
                    }
                }
            }
        }

        // ==================== FULL V5.0 REPORT GENERATOR ====================
        private string GeneratePlainReportV4(ScanResult res)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("========================================================================");
            sb.AppendLine("  DUMP FINDER 6 — FULL SYSTEM DIAGNOSTIC REPORT");
            sb.AppendLine("  Date        : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)); // unambiguous in an English report
            sb.AppendLine("========================================================================");
            sb.AppendLine("CPU           : " + res.Hardware.CpuName);
            sb.AppendLine("GPU           : " + res.Hardware.GpuName + " (" + res.Hardware.GpuDriver + ")");
            sb.AppendLine("Motherboard   : " + res.Hardware.BoardName);
            sb.AppendLine("OS            : " + res.Hardware.OsVersion);
            sb.AppendLine("Last Boot     : " + res.Hardware.LastBoot.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine();

            sb.AppendLine("[SMART DIAGNOSIS]");
            sb.AppendLine(res.DiagnosisTitle);
            sb.AppendLine(res.DiagnosisBody);
            sb.AppendLine();

            if (res.HardwareChecks != null && res.HardwareChecks.Count > 0)
            {
                sb.AppendLine("[HARDWARE, BIOS, RAM EXPO/XMP, REBAR & SSD SMART]");
                foreach (var hc in res.HardwareChecks)
                    sb.AppendLine(string.Format("• [{0}] {1} => {2}", hc.CategoryTag, hc.ValueSummary, Localizer.HealthBadgeText(hc.Level)));
                sb.AppendLine();
            }

            if (res.ConflictChecks != null && res.ConflictChecks.Count > 0)
            {
                sb.AppendLine("[SOFTWARE CONFLICTS, OVERLAYS & DRIVER REMNANTS]");
                foreach (var cc in res.ConflictChecks)
                    sb.AppendLine(string.Format("• [{0}] {1} ({2})", cc.Tag, cc.StatusText, Localizer.HealthBadgeText(cc.Level)));
                sb.AppendLine();
            }

            if (res.Minidumps != null && res.Minidumps.Count > 0)
            {
                sb.AppendLine("[DECODED CRASH DUMPS (.DMP) (" + res.Minidumps.Count + ")]");
                foreach (var d in res.Minidumps)
                    sb.AppendLine(string.Format("• [{0:dd/MM/yyyy HH:mm}] {1} | {2} | Culprit: {3} | Process: {4}", d.CrashTime, d.FileName, d.BugCheckOrException, d.PrimaryCulpritModule, d.ProcessName));
                sb.AppendLine();
            }

            sb.AppendLine("[PROTECTIONS & REGISTRY SETTINGS]");
            foreach (var f in res.Fixes)
                sb.AppendLine("• " + f.Title + " => " + f.CurrentValue);
            sb.AppendLine();

            sb.AppendLine("[EVENT TIMELINE (" + res.Events.Count + ")]");
            foreach (var ev in res.Events.Take(60))
                sb.AppendLine(string.Format("[{0}] [{1}] {2} — {3}", ev.DisplayTime, Localizer.CategoryName(ev.Category), ev.CodeOrId, Localizer.T(ev.SummaryFr, ev.SummaryEn)));

            return sb.ToString();
        }
    }
}
