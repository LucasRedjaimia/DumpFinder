using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>
    /// Crashs: one list for events and .dmp files, a detail panel per crash (plain explanation,
    /// dump analysis, related fixes, AI, forum copy) and the "common crashes" encyclopedia.
    /// </summary>
    public partial class MainWindow
    {
        private Grid _pageCrashes;
        private TextBlock _lblCrashesTitle;
        private TextBlock _lblCrashesSub;
        private Button _btnCrashSubList;
        private Button _btnCrashSubGuide;
        private Grid _crashListView;
        private StackPanel _crashListPanel;
        private Border _crashDrawer;
        private ColumnDefinition _crashDrawerColumn;
        private StackPanel _crashFilterBar;

        private List<CrashEntry> _crashEntries = new List<CrashEntry>();
        private string _selectedCrashKey;
        private bool _crashGuideActive;

        // AI state of the panel (one request at a time)
        private bool _drawerAiBusy;

        private void BuildCrashesPage()
        {
            _pageCrashes = new Grid { Visibility = Visibility.Collapsed };
            _pageCrashes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pageCrashes.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pageCrashes.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            // Header
            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel titles = new StackPanel();
            _lblCrashesTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblCrashesSub = ResFg(new TextBlock { FontSize = 13, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
            titles.Children.Add(_lblCrashesTitle);
            titles.Children.Add(_lblCrashesSub);
            hdr.Children.Add(titles);

            StackPanel seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnCrashSubList = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 6, 14, 6), 12);
            _btnCrashSubList.Margin = new Thickness(0, 0, 6, 0);
            _btnCrashSubList.Click += (s, e) => SwitchCrashSubTab(false);
            _btnCrashSubGuide = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(14, 6, 14, 6), 12);
            _btnCrashSubGuide.Click += (s, e) => SwitchCrashSubTab(true);
            seg.Children.Add(_btnCrashSubList);
            seg.Children.Add(_btnCrashSubGuide);
            Grid.SetColumn(seg, 1);
            hdr.Children.Add(seg);
            _pageCrashes.Children.Add(hdr);

            // Filter bar: period + category + dump tools
            _crashFilterBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
            _cbTimeframe = new ComboBox { MinWidth = 170, Margin = new Thickness(0, 0, 8, 0) };
            _cbTimeframe.SelectionChanged += (s, e) =>
            {
                if (_suppressDropdownEvents || _cbTimeframe.SelectedIndex < 0) return;
                int previous = _selectedRangeMode;
                _selectedRangeMode = _cbTimeframe.SelectedIndex;
                // The scan covers 30 days; only "all history" needs a deeper scan.
                if (_selectedRangeMode == 3 && previous != 3) RunScanAsync();
                else if (_lastResult != null) RenderCrashList();
            };
            _crashFilterBar.Children.Add(_cbTimeframe);
            _cbEventCategory = new ComboBox { MinWidth = 190, Margin = new Thickness(0, 0, 8, 0) };
            _cbEventCategory.SelectionChanged += (s, e) =>
            {
                if (_suppressDropdownEvents || _cbEventCategory.SelectedIndex < 0) return;
                _eventFilterMode = _cbEventCategory.SelectedIndex;
                if (_lastResult != null) RenderCrashList();
            };
            _crashFilterBar.Children.Add(_cbEventCategory);
            _btnOpenDmpFile = CreateFlatButton("", "BgCard", "TextPrimary", "BorderCard", new Thickness(12, 6, 12, 6), 12);
            _btnOpenDmpFile.Margin = new Thickness(0, 0, 8, 0);
            _btnOpenDmpFile.Click += OnOpenDumpFileClick;
            _crashFilterBar.Children.Add(_btnOpenDmpFile);
            _btnAdminScanDumps = CreateFlatButton("", "AccentSoftBg", "AccentText", "AccentSoftBg", new Thickness(12, 6, 12, 6), 12);
            _btnAdminScanDumps.Click += OnRunAdminDeepScanClick;
            _crashFilterBar.Children.Add(_btnAdminScanDumps);
            Grid.SetRow(_crashFilterBar, 1);
            _pageCrashes.Children.Add(_crashFilterBar);

            // Body: list + detail panel
            _crashListView = new Grid();
            _crashListView.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 340 });
            _crashDrawerColumn = new ColumnDefinition { Width = new GridLength(0) };
            _crashListView.ColumnDefinitions.Add(_crashDrawerColumn);

            Border listCard = MakeCard(new Thickness(6), new Thickness(0));
            ScrollViewer svList = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _crashListPanel = new StackPanel();
            svList.Content = _crashListPanel;
            listCard.Child = svList;
            _crashListView.Children.Add(listCard);

            _crashDrawer = MakeCard(new Thickness(0), new Thickness(16, 0, 0, 0));
            _crashDrawer.Visibility = Visibility.Collapsed;
            Grid.SetColumn(_crashDrawer, 1);
            _crashListView.Children.Add(_crashDrawer);

            Grid.SetRow(_crashListView, 2);
            _pageCrashes.Children.Add(_crashListView);

            // Encyclopedia (re-parented from the former Guide page)
            DetachFromParent(_svGuide);
            _svGuide.Visibility = Visibility.Collapsed;
            Grid.SetRow(_svGuide, 1);
            Grid.SetRowSpan(_svGuide, 2);
            _pageCrashes.Children.Add(_svGuide);
        }

        private static void DetachFromParent(FrameworkElement el)
        {
            if (el == null) return;
            var p = el.Parent;
            if (p is Panel) ((Panel)p).Children.Remove(el);
            else if (p is Decorator) ((Decorator)p).Child = null;
            else if (p is ContentControl) ((ContentControl)p).Content = null;
        }

        private void SwitchCrashSubTab(bool guide)
        {
            _crashGuideActive = guide;
            _crashFilterBar.Visibility = guide ? Visibility.Collapsed : Visibility.Visible;
            _crashListView.Visibility = guide ? Visibility.Collapsed : Visibility.Visible;
            _svGuide.Visibility = guide ? Visibility.Visible : Visibility.Collapsed;
            StyleTogglePill(_btnCrashSubList, !guide);
            StyleTogglePill(_btnCrashSubGuide, guide);
            if (guide) AnimatePageIn(_svGuide); else AnimatePageIn(_crashListView);
        }

        private void RefreshCrashesLabels()
        {
            if (_lblCrashesTitle == null) return;
            _lblCrashesTitle.Text = Localizer.S("crashes.crashes");
            _lblCrashesSub.Text = Localizer.S("crashes.each_crash_brings_together_the_windows");
            _btnCrashSubGuide.Content = Localizer.S("crashes.common_crashes");
            _btnOpenDmpFile.Content = Localizer.S("crashes.open_a_dmp");
            _btnAdminScanDumps.Content = Localizer.S("crashes.read_c_windows_minidump");
            _btnAdminScanDumps.ToolTip = Localizer.S("crashes.blue_screen_dumps_are_protected_administ");
            UpdateCrashListTabLabel();
        }

        private void UpdateCrashListTabLabel()
        {
            int n = FilteredCrashes().Count();
            string baseLabel = Localizer.S("crashes.history");
            _btnCrashSubList.Content = n > 0 ? baseLabel + " (" + n + ")" : baseLabel;
        }

        /// <summary>Rebuilds the merged crash list from the last scan (called on every scan / language switch).</summary>
        private void RebuildCrashEntries(ScanResult res)
        {
            _crashEntries = CrashCatalog.Build(res);
        }

        private IEnumerable<CrashEntry> FilteredCrashes()
        {
            if (_lastResult == null) return Enumerable.Empty<CrashEntry>();
            DateTime? start = GetStartTimeForRange(_lastResult.Hardware);
            IEnumerable<CrashEntry> q = _crashEntries;
            if (start.HasValue) q = q.Where(c => c.Time >= start.Value);
            switch (_eventFilterMode)
            {
                case 1: q = q.Where(c => c.Category == CrashCategory.BSOD || c.Category == CrashCategory.UnexpectedReboot); break;
                case 2: q = q.Where(c => c.Category == CrashCategory.GpuDriver); break;
                case 3: q = q.Where(c => c.Category == CrashCategory.StorageNvme); break;
                case 4: q = q.Where(c => c.Category == CrashCategory.HardwareWhea || c.Category == CrashCategory.MemoryExhaustion); break;
                case 5: q = q.Where(c => c.Category == CrashCategory.BrowserGpu || c.Category == CrashCategory.AppCrash); break;
            }
            return q;
        }

        private void RenderCrashList()
        {
            if (_crashListPanel == null) return;
            UpdateCrashListTabLabel();
            _btnAdminScanDumps.Visibility = (_lastResult != null && _lastResult.MinidumpFolderNeedsAdmin) ? Visibility.Visible : Visibility.Collapsed;

            var list = FilteredCrashes().ToList();
            _crashListPanel.Children.Clear();

            if (list.Count == 0)
            {
                _crashListPanel.Children.Add(MakeEmptyState(Glyph.Completed, "SuccessFg",
                    Localizer.S("crashes.no_crash_in_this_period"),
                    Localizer.S("crashes.widen_the_period_or_category_above")));
                CloseCrashDrawer();
                return;
            }

            if (_selectedCrashKey == null || !list.Any(c => c.Key == _selectedCrashKey))
                _selectedCrashKey = list[0].Key;

            foreach (var c in list.Take(150))
                _crashListPanel.Children.Add(BuildCrashRow(c));

            var selected = list.FirstOrDefault(c => c.Key == _selectedCrashKey);
            if (selected != null) ShowCrashDrawer(selected);
        }

        private UIElement BuildCrashRow(CrashEntry c)
        {
            bool selected = c.Key == _selectedCrashKey;
            string bg, fg;
            CategoryColors(c.Category, out bg, out fg);

            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(MakeIconTile(CategoryGlyph(c.Category), bg, fg));

            StackPanel t = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            StackPanel line1 = new StackPanel { Orientation = Orientation.Horizontal };
            line1.Children.Add(ResFg(new TextBlock { Text = Localizer.CategoryName(c.Category), FontSize = 11, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center }, fg));
            if (_newCrashKeys.Contains(c.Key))
            {
                Border nw = MakePill(Localizer.S("crashes.new"), "DangerSoftBg", "DangerFg");
                nw.Padding = new Thickness(6, 0, 6, 1);
                nw.Margin = new Thickness(0, 0, 6, 0);
                line1.Children.Add(nw);
            }
            if (c.Dump != null)
            {
                Border dmp = MakePill(".dmp", "BgSubtle", "TextSecondary");
                dmp.Padding = new Thickness(6, 0, 6, 1);
                line1.Children.Add(dmp);
            }
            t.Children.Add(line1);
            t.Children.Add(ResFg(new TextBlock { Text = c.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }, "TextPrimary"));
            if (!string.IsNullOrEmpty(c.TechLine))
                t.Children.Add(ResFg(new TextBlock { Text = c.TechLine, FontSize = 11, FontFamily = Ds.MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }, "TextMuted"));
            Grid.SetColumn(t, 1);
            row.Children.Add(t);

            TextBlock when = ResFg(new TextBlock { Text = c.Time.ToString("dd/MM HH:mm"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, "TextMuted");
            Grid.SetColumn(when, 2);
            row.Children.Add(when);

            Border wrap = new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 2),
                Cursor = Cursors.Hand,
                Focusable = true,
                Child = row
            };
            if (selected)
            {
                wrap.SetResourceReference(Border.BackgroundProperty, "AccentSoftBg");
                wrap.SetResourceReference(Border.BorderBrushProperty, "Accent");
            }
            else
            {
                wrap.Background = Brushes.Transparent;
                wrap.BorderBrush = Brushes.Transparent;
                wrap.MouseEnter += (s, e) => wrap.SetResourceReference(Border.BackgroundProperty, "HoverOverlay");
                wrap.MouseLeave += (s, e) => wrap.Background = Brushes.Transparent;
            }
            string key = c.Key;
            wrap.MouseLeftButtonUp += (s, e) => SelectCrash(key);
            wrap.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) SelectCrash(key); };
            System.Windows.Automation.AutomationProperties.SetName(wrap, Localizer.CategoryName(c.Category) + " — " + c.Title + " — " + c.Time.ToString("g"));
            return wrap;
        }

        private void SelectCrash(string key)
        {
            _selectedCrashKey = key;
            RenderCrashList();
        }

        /// <summary>Navigates to Crashs and opens the given crash (used by the home page).</summary>
        private void OpenCrash(string key)
        {
            _selectedCrashKey = key;
            var c = _crashEntries.FirstOrDefault(x => x.Key == key);
            if (c != null && _lastResult != null)
            {
                DateTime? start = GetStartTimeForRange(_lastResult.Hardware);
                if (start.HasValue && c.Time < start.Value)
                {
                    _selectedRangeMode = 2;
                    _suppressDropdownEvents = true;
                    _cbTimeframe.SelectedIndex = 2;
                    _suppressDropdownEvents = false;
                }
            }
            _eventFilterMode = 0;
            _suppressDropdownEvents = true;
            _cbEventCategory.SelectedIndex = 0;
            _suppressDropdownEvents = false;
            SwitchTab(TabCrashes);
            if (_crashGuideActive) SwitchCrashSubTab(false);
            RenderCrashList();
        }

        private void CloseCrashDrawer()
        {
            _crashDrawer.Visibility = Visibility.Collapsed;
            _crashDrawerColumn.Width = new GridLength(0);
        }

        // ==================== DETAIL PANEL ====================
        private TextBox _drawerAiOutput;
        private Button _btnDrawerAi;

        private void ShowCrashDrawer(CrashEntry c)
        {
            // Another crash: stop its AI stream so the new "Ask the AI" button starts instead of cancelling.
            bool keepAiStream = PrepareDrawerAiFor(c.Key);
            TextBox previousAiOutput = _drawerAiOutput;
            _crashDrawerColumn.Width = new GridLength(400);
            _crashDrawer.Visibility = Visibility.Visible;

            Grid shell = new Grid();
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel p = new StackPanel { Margin = new Thickness(20, 18, 16, 12) };
            sv.Content = p;
            shell.Children.Add(sv);

            // Pills + close
            Grid top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            WrapPanel pills = new WrapPanel();
            string bg, fg;
            CategoryColors(c.Category, out bg, out fg);
            Border catPill = MakePill(Localizer.CategoryName(c.Category), bg, fg);
            catPill.Margin = new Thickness(0, 0, 6, 0);
            pills.Children.Add(catPill);
            if (c.Dump != null)
                pills.Children.Add(MakePill(Localizer.S("crashes.dump_decoded"), "BgSubtle", "TextSecondary"));
            top.Children.Add(pills);
            Button close = CreateNavButton(ResFg(Ds.Icon(Glyph.Cancel, 11), "TextMuted"));
            close.Width = 28;
            close.Height = 28;
            close.Padding = new Thickness(0);
            close.Margin = new Thickness(0);
            close.ToolTip = Localizer.S("crashes.close_panel");
            close.Click += (s, e) => CloseCrashDrawer();
            Grid.SetColumn(close, 1);
            top.Children.Add(close);
            p.Children.Add(top);

            p.Children.Add(ResFg(new TextBlock { Text = c.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 4) }, "TextPrimary"));
            p.Children.Add(ResFg(new TextBlock { Text = (c.TechLine.Length > 0 ? c.TechLine + "  ·  " : "") + c.Time.ToString("f", Localizer.Culture), FontSize = 12, FontFamily = Ds.MonoFont, TextWrapping = TextWrapping.Wrap }, "TextMuted"));

            // Plain explanation
            string plain = CrashCatalog.PlainExplanation(c);
            if (!string.IsNullOrWhiteSpace(plain))
            {
                Border box = ResBorder(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(12),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 11, 14, 12),
                    Margin = new Thickness(0, 16, 0, 0)
                }, "BgSubtle"), "BorderCard");
                TextBlock tb = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19 }, "TextSecondary");
                var lead = new System.Windows.Documents.Run(Localizer.S("crashes.in_plain_words")) { FontWeight = FontWeights.SemiBold };
                lead.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
                tb.Inlines.Add(lead);
                tb.Inlines.Add(new System.Windows.Documents.Run(plain));
                box.Child = tb;
                p.Children.Add(box);
            }

            // Dump analysis
            if (c.Dump != null)
            {
                p.Children.Add(DrawerLabel(Localizer.S("crashes.dump_analysis")));
                Grid kv = new Grid();
                kv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AddKv(kv, Localizer.S("crashes.faulting_module"), c.Dump.PrimaryCulpritModule, "DangerFg");
                AddKv(kv, Localizer.S("crashes.code"), c.Dump.BugCheckOrException, null);
                if (!string.Equals(c.Dump.ProcessName, "System", StringComparison.OrdinalIgnoreCase))
                    AddKv(kv, Localizer.S("crashes.process"), c.Dump.ProcessName, null);
                if (c.Dump.StackModules != null && c.Dump.StackModules.Count > 0)
                    AddKv(kv, Localizer.S("crashes.drivers_on_the_stack"), string.Join(" → ", c.Dump.StackModules), null);
                if (c.Dump.BugCheckParameters != null && c.Dump.BugCheckParameters.Count == 4)
                    AddKv(kv, Localizer.S("crashes.parameters"), string.Join("\n", c.Dump.BugCheckParameters), null);
                if (c.Dump.OsBuild > 0)
                    AddKv(kv, Localizer.S("crashes.windows_build"), c.Dump.OsBuild.ToString(), null);
                AddKv(kv, Localizer.S("crashes.file"), c.Dump.FileName + "  (" + CrashCatalog.FormatSize(c.Dump.FileSizeBytes) + ")", null);
                p.Children.Add(kv);

                string evidence = c.Dump.StructuredParse
                    ? Localizer.T(c.Dump.CulpritEvidenceFr, c.Dump.CulpritEvidenceEn)
                    : Localizer.S("crashes.unstructured_dump_the_module_shown_is");
                if (!string.IsNullOrEmpty(evidence))
                {
                    StackPanel ev = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                    TextBlock evIcon = ResFg(Ds.Icon(c.Dump.StructuredParse ? Glyph.CheckMark : Glyph.Info, 11), c.Dump.StructuredParse ? "SuccessFg" : "TextMuted");
                    evIcon.Margin = new Thickness(0, 2, 7, 0);
                    evIcon.VerticalAlignment = VerticalAlignment.Top;
                    ev.Children.Add(evIcon);
                    ev.Children.Add(ResFg(new TextBlock { Text = evidence, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 }, "TextSecondary"));
                    p.Children.Add(ev);
                }

                DriverInfo drv = _lastResult != null ? DriverInventory.FindByImage(_lastResult.Drivers, c.Dump.PrimaryCulpritModule) : null;
                if (drv != null)
                {
                    p.Children.Add(DrawerLabel(Localizer.S("crashes.installed_driver")));
                    Border db = ResBorder(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(12, 9, 10, 9) }, "BorderCard");
                    Grid dg = new Grid();
                    dg.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    dg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    StackPanel dt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                    dt.Children.Add(ResFg(new TextBlock { Text = drv.DeviceName, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, "TextPrimary"));
                    string ver = drv.Provider + " " + drv.Version + (drv.Date.HasValue ? " · " + Localizer.DayMonthYear(drv.Date.Value) : "");
                    dt.Children.Add(ResFg(new TextBlock { Text = ver, FontSize = 11, FontFamily = Ds.MonoFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }, drv.IsOld ? "WarnFg" : "TextMuted"));
                    dg.Children.Add(dt);
                    Button upd = CreateFlatButton(Localizer.S("crashes.update"), "BgCard", "AccentText", "BorderCard", new Thickness(10, 4, 10, 4), 12);
                    upd.VerticalAlignment = VerticalAlignment.Center;
                    string q = drv.Provider + " " + drv.DeviceName + " driver download";
                    upd.Click += (s, e) => ExecuteQuickAction(null, q);
                    Grid.SetColumn(upd, 1);
                    dg.Children.Add(upd);
                    db.Child = dg;
                    p.Children.Add(db);
                }

                p.Children.Add(BuildDeepAnalysisSection(c));
            }
            else if (c.Event != null && (c.Category == CrashCategory.BSOD) && _lastResult != null && _lastResult.MinidumpFolderNeedsAdmin)
            {
                p.Children.Add(DrawerLabel(Localizer.S("crashes.dump_analysis")));
                Button unlock = CreateFlatButton(_btnAdminScanDumps.Content as string ?? Localizer.S("crashes.read_c_windows_minidump_2"),
                    "AccentSoftBg", "AccentText", "AccentSoftBg", new Thickness(12, 6, 12, 6), 12);
                unlock.HorizontalAlignment = HorizontalAlignment.Left;
                unlock.Click += OnRunAdminDeepScanClick;
                p.Children.Add(ResFg(new TextBlock
                {
                    Text = Localizer.S("crashes.this_blue_screen_s_dmp_file"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 8)
                }, "TextSecondary"));
                p.Children.Add(unlock);
            }

            // Raw event (collapsed by default)
            if (c.Event != null)
            {
                string raw = Localizer.T(c.Event.DetailsFr, c.Event.DetailsEn);
                if (!string.IsNullOrWhiteSpace(raw) && raw.Trim() != plain)
                {
                    Expander ex = new Expander { Margin = new Thickness(0, 14, 0, 0) };
                    ex.Header = ResFg(new TextBlock { Text = Localizer.S("crashes.full_windows_message"), FontSize = 12, FontWeight = FontWeights.SemiBold }, "TextSecondary");
                    ex.SetResourceReference(Control.ForegroundProperty, "TextSecondary");
                    TextBox rawBox = ResFg(new TextBox
                    {
                        Text = c.Event.Source + " · " + c.Event.CodeOrId + "\n\n" + raw,
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.Wrap,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        FontFamily = Ds.MonoFont,
                        FontSize = 11,
                        Margin = new Thickness(0, 8, 0, 0)
                    }, "TextSecondary");
                    ex.Content = rawBox;
                    p.Children.Add(ex);
                }
            }

            // Related fixes
            var ids = CrashCatalog.RelatedFixIds(c);
            var related = (_lastResult != null && _lastResult.Fixes != null)
                ? ids.Select(id => _lastResult.Fixes.FirstOrDefault(f => f.Id == id))
                     .Where(f => f != null && f.IsApplicableTo(_lastResult.Hardware) && f.Status != FixStatus.Info)
                     .Take(4).ToList()
                : new List<RegistryFixItem>();
            if (related.Count > 0)
            {
                p.Children.Add(DrawerLabel(Localizer.S("crashes.related_fixes")));
                foreach (var f in related)
                    p.Children.Add(BuildDrawerFixRow(f));
            }

            // AI answer area
            p.Children.Add(DrawerLabel(Localizer.S("crashes.ai_opinion")));
            _drawerAiOutput = ResFg(new TextBox
            {
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                Text = AiHintText()
            }, "TextSecondary");
            p.Children.Add(_drawerAiOutput);

            // Footer actions
            Border foot = ResBorder(new Border { BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(16, 12, 16, 14) }, "BorderCard");
            Grid fg2 = new Grid();
            fg2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fg2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            fg2.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _btnDrawerAi = CreatePrimaryButton(Localizer.S("crashes.ask_the_ai"), "Accent", "OnAccent", new Thickness(12, 7, 12, 7), 13);
            _btnDrawerAi.HorizontalAlignment = HorizontalAlignment.Stretch;
            CrashEntry captured = c;
            string capturedKey = c.Key;
            _btnDrawerAi.Click += (s, e) => RunDrawerAi(AiAssistant.BuildStructuredPrompt(_lastResult, CrashCatalog.AiFocusQuestion(captured)), capturedKey);
            fg2.Children.Add(_btnDrawerAi);
            if (keepAiStream) ReattachDrawerAi(previousAiOutput);
            Button forum = CreateFlatButton(Localizer.S("crashes.copy_for_forum"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 7, 12, 7), 13);
            forum.HorizontalAlignment = HorizontalAlignment.Stretch;
            forum.Click += (s, e) =>
            {
                try
                {
                    Clipboard.SetText(CrashCatalog.ForumMarkdown(captured, _lastResult));
                    SetToast(Localizer.S("crashes.summary_copied_ready_to_paste_on"), "SuccessFg");
                }
                catch (Exception ex)
                {
                    Log.Warn(ex, "Clipboard");
                    SetToast(Localizer.S("ai.clipboard_busy_try_again"), "WarnFg");
                }
            };
            Grid.SetColumn(forum, 2);
            fg2.Children.Add(forum);
            foot.Child = fg2;
            Grid.SetRow(foot, 1);
            shell.Children.Add(foot);

            _crashDrawer.Child = shell;
        }

        // ==================== DEEP ANALYSIS (WinDbg) ====================
        private readonly HashSet<string> _deepRunning = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private static string ReadableDumpPath(MinidumpReportItem d)
        {
            if (d == null) return null;
            if (!string.IsNullOrEmpty(d.LocalCopyPath) && System.IO.File.Exists(d.LocalCopyPath)) return d.LocalCopyPath;
            try
            {
                using (System.IO.File.OpenRead(d.FullPath)) { }
                return d.FullPath;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private UIElement BuildDeepAnalysisSection(CrashEntry c)
        {
            StackPanel host = new StackPanel();
            host.Children.Add(DrawerLabel(Localizer.S("crashes.windbg_analysis")));
            string path = ReadableDumpPath(c.Dump);
            DeepAnalysisResult cached = path != null ? DeepAnalyzer.TryLoadCached(path) : null;

            if (cached != null && cached.Ok)
            {
                Grid kv = new Grid();
                kv.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                kv.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                AddKv(kv, "Bucket", cached.BucketId, "DangerFg");
                AddKv(kv, Localizer.S("crashes.image"), cached.ImageName + (string.IsNullOrEmpty(cached.ImageVersion) ? "" : "  (" + cached.ImageVersion + ")"), null);
                AddKv(kv, Localizer.S("crashes.symbol"), cached.SymbolName, null);
                host.Children.Add(kv);
                if (cached.StackFrames != null && cached.StackFrames.Count > 0)
                {
                    Expander ex = new Expander { Margin = new Thickness(0, 4, 0, 0) };
                    ex.Header = ResFg(new TextBlock { Text = Localizer.S("crashes.symbolized_call_stack"), FontSize = 12, FontWeight = FontWeights.SemiBold }, "TextSecondary");
                    ex.SetResourceReference(Control.ForegroundProperty, "TextSecondary");
                    ex.Content = ResFg(new TextBox
                    {
                        Text = string.Join("\n", cached.StackFrames),
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.NoWrap,
                        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        FontFamily = Ds.MonoFont,
                        FontSize = 11,
                        Margin = new Thickness(0, 6, 0, 0)
                    }, "TextSecondary");
                    host.Children.Add(ex);
                }
                return host;
            }

            TextBlock status = ResFg(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) }, "TextSecondary");
            host.Children.Add(status);

            if (path == null)
            {
                status.Text = Localizer.S("crashes.the_dmp_file_is_protected_read");
                return host;
            }

            if (!DeepAnalyzer.IsAvailable)
            {
                status.Text = Localizer.S("crashes.microsoft_s_debugger_windbg_gives_the");
                Button install = CreateFlatButton(Localizer.S("crashes.install_windbg"), "BgCard", "AccentText", "BorderCard", new Thickness(12, 6, 12, 6), 12);
                install.HorizontalAlignment = HorizontalAlignment.Left;
                install.Click += (s, e) =>
                {
                    if (DeepAnalyzer.LaunchInstaller())
                        SetToast(Localizer.S("crashes.windbg_install_started_come_back_to"), "AccentText");
                };
                host.Children.Add(install);
                return host;
            }

            bool running = _deepRunning.Contains(path);
            status.Text = running
                ? Localizer.S("crashes.analyzing_20_s_longer_the_first")
                : (cached != null && !cached.Ok
                    ? Localizer.S("crashes.the_last_analysis_failed") + cached.Error
                    : Localizer.S("crashes.windbg_is_installed_run_analyze_v"));
            Button run = CreateFlatButton(Localizer.S("crashes.run_windbg_analysis"), "AccentSoftBg", "AccentText", "AccentSoftBg", new Thickness(12, 6, 12, 6), 12);
            run.HorizontalAlignment = HorizontalAlignment.Left;
            run.IsEnabled = !running;
            string key = c.Key;
            run.Click += async (s, e) =>
            {
                _deepRunning.Add(path);
                if (_selectedCrashKey == key) RenderCrashList();
                DeepAnalysisResult r;
                try
                {
                    r = await System.Threading.Tasks.Task.Run(() => DeepAnalyzer.Analyze(path));
                }
                finally
                {
                    _deepRunning.Remove(path);
                }
                SetToast(r.Ok
                        ? Localizer.S("crashes.windbg_analysis_done") + r.BucketId
                        : Localizer.S("crashes.windbg_analysis_failed") + r.Error,
                    r.Ok ? "SuccessFg" : "WarnFg");
                if (_selectedCrashKey == key) RenderCrashList();
            };
            host.Children.Add(run);
            return host;
        }

        private TextBlock DrawerLabel(string text)
        {
            return ResFg(new TextBlock
            {
                Text = text.ToUpperInvariant(),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 20, 0, 8)
            }, "TextMuted");
        }

        private void AddKv(Grid kv, string key, string value, string valueBrush)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            int r = kv.RowDefinitions.Count;
            kv.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock k = ResFg(new TextBlock { Text = key, FontSize = 12, Margin = new Thickness(0, 0, 14, 6) }, "TextMuted");
            Grid.SetRow(k, r);
            kv.Children.Add(k);
            TextBlock v = ResFg(new TextBlock { Text = value, FontSize = 12, FontFamily = Ds.MonoFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) }, valueBrush ?? "TextPrimary");
            Grid.SetRow(v, r);
            Grid.SetColumn(v, 1);
            kv.Children.Add(v);
        }

        private UIElement BuildDrawerFixRow(RegistryFixItem f)
        {
            Border b = ResBorder(new Border
            {
                CornerRadius = new CornerRadius(10),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 10, 9),
                Margin = new Thickness(0, 0, 0, 6)
            }, "BorderCard");
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel t = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            t.Children.Add(ResFg(new TextBlock { Text = f.Title, FontSize = 12, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, "TextPrimary"));
            t.Children.Add(ResFg(new TextBlock { Text = f.CurrentValue, FontSize = 11, FontFamily = Ds.MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }, "TextMuted"));
            g.Children.Add(t);

            UIElement right;
            if (f.Status == FixStatus.Applied)
            {
                right = MakePill(Localizer.S("crashes.active"), "SuccessSoftBg", "SuccessFg");
            }
            else
            {
                string id = f.Id;
                Button apply = CreateFlatButton(Localizer.S("opt_tweaks.apply"),
                    f.Status == FixStatus.Recommended ? "Accent" : "BgCard",
                    f.Status == FixStatus.Recommended ? "OnAccent" : "AccentText",
                    f.Status == FixStatus.Recommended ? "Accent" : "BorderCard",
                    new Thickness(11, 4, 11, 4), 12);
                apply.VerticalAlignment = VerticalAlignment.Center;
                apply.Click += (s, e) => ExecuteSingleFix(id + "_apply", id, s);
                right = apply;
            }
            Grid.SetColumn(right, 1);
            g.Children.Add(right);
            b.Child = g;
            return b;
        }

        private string AiHintText()
        {
            var prov = Localizer.CachedSettings != null ? (AiProviderType)Localizer.CachedSettings.AiProvider : AiProviderType.Gemini;
            bool ready = prov == AiProviderType.OllamaLocal || (Localizer.CachedSettings != null && !string.IsNullOrEmpty(Localizer.CachedSettings.AiApiKey));
            return ready
                ? Localizer.S("crashes.ask_the_ai_sends_this_crash")
                : Localizer.S("crashes.no_ai_assistant_set_up_yet");
        }

        private System.Threading.CancellationTokenSource _aiCts;
        /// <summary>Crash key (or <see cref="SystemReviewAiKey"/>) the running AI stream belongs to.</summary>
        private string _drawerAiKey;
        private const string SystemReviewAiKey = "\u0001system-review";

        /// <summary>Stops the running drawer AI stream (if any) and frees the panel for a new request.</summary>
        private void CancelDrawerAi()
        {
            var cts = _aiCts;
            _aiCts = null;          // the old run sees it is no longer current and leaves the UI alone
            _drawerAiBusy = false;
            _drawerAiKey = null;
            if (cts != null)
            {
                try { cts.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }

        /// <summary>
        /// Called before the drawer is rebuilt for <paramref name="key"/>: a stream for another crash is
        /// cancelled; a stream for the same crash keeps running and is re-attached to the new controls.
        /// </summary>
        private bool PrepareDrawerAiFor(string key)
        {
            if (!_drawerAiBusy) return false;
            if (_drawerAiKey == key) return true;
            CancelDrawerAi();
            return false;
        }

        /// <summary>Re-attaches a still-running stream to a rebuilt drawer (same crash).</summary>
        private void ReattachDrawerAi(TextBox previousOutput)
        {
            if (_btnDrawerAi != null) _btnDrawerAi.Content = Localizer.S("crashes.stop");
            if (_drawerAiOutput != null && previousOutput != null && !ReferenceEquals(previousOutput, _drawerAiOutput))
            {
                _drawerAiOutput.Text = previousOutput.Text;
                _drawerAiOutput.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            }
        }

        private async void RunDrawerAi(string prompt, string aiKey)
        {
            if (_drawerAiOutput == null) return;
            if (_drawerAiBusy)
            {
                // Second click = stop.
                if (_aiCts != null) _aiCts.Cancel();
                return;
            }
            var s = Localizer.CachedSettings ?? new AppSettings();
            AiProviderType provider = Enum.IsDefined(typeof(AiProviderType), s.AiProvider) ? (AiProviderType)s.AiProvider : AiProviderType.Gemini;
            string key = s.AiApiKey ?? "";
            string model = provider == AiProviderType.Claude ? s.ClaudeModel
                         : provider == AiProviderType.OllamaLocal ? (string.IsNullOrWhiteSpace(s.OllamaModel) ? "llama3.2" : s.OllamaModel)
                         : null;
            if (provider != AiProviderType.OllamaLocal && string.IsNullOrEmpty(key))
            {
                SetToast(Localizer.S("crashes.set_up_an_ai_assistant_in"), "WarnFg");
                SwitchTab(TabSettings);
                return;
            }

            _drawerAiBusy = true;
            _drawerAiKey = aiKey;
            var cts = new System.Threading.CancellationTokenSource();
            _aiCts = cts;
            // The drawer can be rebuilt for the same crash while streaming: always write to the
            // current controls, and only while this run is still the current one.
            Func<bool> isCurrent = () => ReferenceEquals(_aiCts, cts);
            object btnContent = _btnDrawerAi != null ? _btnDrawerAi.Content : null;
            if (_btnDrawerAi != null) _btnDrawerAi.Content = Localizer.S("crashes.stop");
            _drawerAiOutput.Text = Localizer.S("crashes.thinking");
            _drawerAiOutput.SetResourceReference(Control.ForegroundProperty, "TextMuted");

            bool first = true;
            Action<string> onDelta = chunk => Dispatcher.BeginInvoke(new Action(() =>
            {
                TextBox target = _drawerAiOutput;
                if (!isCurrent() || target == null) return;
                if (first) { target.Text = ""; target.SetResourceReference(Control.ForegroundProperty, "TextPrimary"); first = false; }
                target.AppendText(chunk);
            }));

            try
            {
                AiReply reply = await AiAssistant.StreamAsync(provider, key, model, prompt, onDelta, cts.Token);
                // Let queued chunks land, then show the authoritative final text.
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
                if (isCurrent() && _drawerAiOutput != null)
                {
                    _drawerAiOutput.Text = reply.Text;
                    _drawerAiOutput.SetResourceReference(Control.ForegroundProperty, reply.Ok ? "TextPrimary" : "WarnFg");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "AI query");
                if (isCurrent() && _drawerAiOutput != null)
                {
                    _drawerAiOutput.Text = ex.Message;
                    _drawerAiOutput.SetResourceReference(Control.ForegroundProperty, "DangerFg");
                }
            }
            finally
            {
                if (isCurrent())
                {
                    _drawerAiBusy = false;
                    _drawerAiKey = null;
                    _aiCts = null;
                    if (_btnDrawerAi != null) _btnDrawerAi.Content = btnContent;
                }
                cts.Dispose();
            }
        }
        /// <summary>Whole-system AI review from the home page when there is no crash to focus on.</summary>
        private void OpenSystemAiReview()
        {
            SwitchTab(TabCrashes);
            bool keepAiStream = PrepareDrawerAiFor(SystemReviewAiKey);
            TextBox previousAiOutput = _drawerAiOutput;
            if (_crashGuideActive) SwitchCrashSubTab(false);
            _crashDrawerColumn.Width = new GridLength(400);
            _crashDrawer.Visibility = Visibility.Visible;

            StackPanel p = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
            p.Children.Add(MakePill(Localizer.S("crashes.full_check_up"), "AccentSoftBg", "AccentText"));
            p.Children.Add(ResFg(new TextBlock { Text = Localizer.S("crashes.ai_system_check_up"), FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 12, 0, 12) }, "TextPrimary"));
            _drawerAiOutput = ResFg(new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 13, Text = AiHintText() }, "TextSecondary");
            p.Children.Add(_drawerAiOutput);
            _btnDrawerAi = CreatePrimaryButton(Localizer.S("crashes.run_check_up"), "Accent", "OnAccent", new Thickness(14, 7, 14, 7), 13);
            _btnDrawerAi.HorizontalAlignment = HorizontalAlignment.Left;
            _btnDrawerAi.Margin = new Thickness(0, 14, 0, 0);
            _btnDrawerAi.Click += (s, e) => RunDrawerAi(AiAssistant.BuildStructuredPrompt(_lastResult, ""), SystemReviewAiKey);
            p.Children.Add(_btnDrawerAi);
            if (keepAiStream) ReattachDrawerAi(previousAiOutput);
            _crashDrawer.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = p };
        }

        private UIElement MakeEmptyState(string glyph, string fgKey, string title, string body)
        {
            StackPanel sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(32, 48, 32, 48), MaxWidth = 460 };
            Border ic = ResBg(new Border { Width = 52, Height = 52, CornerRadius = new CornerRadius(16), HorizontalAlignment = HorizontalAlignment.Center, Child = ResFg(Ds.Icon(glyph, 22), fgKey) }, "BgSubtle");
            sp.Children.Add(ic);
            sp.Children.Add(ResFg(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 6) }, "TextPrimary"));
            sp.Children.Add(ResFg(new TextBlock { Text = body, FontSize = 13, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, LineHeight = 19 }, "TextSecondary"));
            return sp;
        }
    }
}
