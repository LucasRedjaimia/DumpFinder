using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinCrashDoctor.Optimize;

namespace WinCrashDoctor
{
    /// <summary>
    /// Optimiser: Windows settings (ads, Bing, Copilot, privacy, gaming), preinstalled apps, startup programs,
    /// disk cleanup, and the "myths" we deliberately leave out. Everything goes through the fix pipeline
    /// (journal + exact undo) except app removal, which is reversible from the Microsoft Store.
    /// </summary>
    public partial class MainWindow
    {
        private Grid _pageOptimize;
        private TextBlock _lblOptTitle, _lblOptSub;
        private readonly Button[] _btnOptSub = new Button[6];
        private readonly ScrollViewer[] _optViews = new ScrollViewer[6];
        private readonly StackPanel[] _optPanels = new StackPanel[6];
        private int _optSub;
        private bool _optShownOnce;

        private List<AppEntry> _optApps;
        private List<StartupEntry> _optStartup;
        private List<CleanItem> _optClean;
        private bool _optAppsLoading, _optStartupLoading, _optCleanLoading;
        private readonly Dictionary<CheckBox, AppEntry> _optAppChecks = new Dictionary<CheckBox, AppEntry>();
        private readonly Dictionary<CheckBox, CleanItem> _optCleanChecks = new Dictionary<CheckBox, CleanItem>();
        private Button _btnOptRemoveApps, _btnOptClean;


        // ==================== BUILD ====================
        private void BuildOptimizePage()
        {
            _pageOptimize = new Grid { Visibility = Visibility.Collapsed };
            _pageOptimize.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pageOptimize.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var hdr = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var titles = new StackPanel();
            _lblOptTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblOptSub = ResFg(new TextBlock { FontSize = 13, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.Wrap }, "TextSecondary");
            titles.Children.Add(_lblOptTitle);
            titles.Children.Add(_lblOptSub);
            hdr.Children.Add(titles);

            var seg = new WrapPanel { VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Right };
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                var b = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12.2);
                b.FontWeight = FontWeights.SemiBold;
                b.Margin = new Thickness(6, 0, 0, 6);
                b.Click += (s, e) => SwitchOptimizeSub(idx);
                _btnOptSub[i] = b;
                seg.Children.Add(b);
            }
            Grid.SetColumn(seg, 1);
            hdr.Children.Add(seg);
            _pageOptimize.Children.Add(hdr);

            var host = new Grid();
            Grid.SetRow(host, 1);
            for (int i = 0; i < 6; i++)
            {
                _optPanels[i] = new StackPanel { Margin = new Thickness(0, 0, 8, 12) };
                _optViews[i] = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _optPanels[i], Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
                host.Children.Add(_optViews[i]);
            }
            _pageOptimize.Children.Add(host);

            _pageOptimize.IsVisibleChanged += (s, e) =>
            {
                if (!_pageOptimize.IsVisible) return;
                if (!_optShownOnce) { _optShownOnce = true; RenderOptimizeSub(); }
            };
        }

        private void RefreshOptimizeLabels()
        {
            _lblOptTitle.Text = Localizer.S("main_window.optimize");
            _lblOptSub.Text = Localizer.S("optimize.remove_the_clutter_and_tune_windows");
            string[] names =
            {
                Localizer.S("optimize.settings"),
                Localizer.S("optimize.apps"),
                Localizer.S("optimize.startup"),
                Localizer.S("optimize.cleanup"),
                Localizer.S("space.tab"),
                Localizer.S("optimize.myths")
            };
            for (int i = 0; i < 6; i++) { _btnOptSub[i].Content = names[i]; StyleTogglePill(_btnOptSub[i], i == _optSub); }
            if (_optShownOnce) RenderOptimizeSub();
        }

        public void SwitchOptimizeSub(int sub)
        {
            _optSub = sub;
            for (int i = 0; i < 6; i++)
            {
                _optViews[i].Visibility = i == sub ? Visibility.Visible : Visibility.Collapsed;
                StyleTogglePill(_btnOptSub[i], i == sub);
            }
            RenderOptimizeSub();
            AnimatePageIn(_optViews[sub]);
        }

        private void RenderOptimizeSub()
        {
            switch (_optSub)
            {
                case 0: RenderOptTweaks(); break;
                case 1: RenderOptApps(); break;
                case 2: RenderOptStartup(); break;
                case 3: RenderOptClean(); break;
                case OptSubStorage: RenderOptSpace(); break;
                default: RenderOptMyths(); break;
            }
        }

        // ==================== Shared bits ====================
        private TextBlock OptSectionTitle(string text)
        {
            return ResFg(new TextBlock { Text = text, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 8) }, "TextPrimary");
        }

        private TextBlock OptNote(string text)
        {
            return ResFg(new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8), LineHeight = 17 }, "TextMuted");
        }

        private Border OptCard(UIElement child)
        {
            Border b = ResBorder(ResBg(new Border { CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 0, 8) }, "BgCard"), "BorderCard");
            b.Child = child;
            return b;
        }

        private Grid OptActionBar(string note, Button action)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = OptNote(note);
            t.Margin = new Thickness(0, 0, 16, 0);
            t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            if (action != null) { Grid.SetColumn(action, 1); g.Children.Add(action); }
            return g;
        }

        /// <summary>Runs one Optimiser operation through the fix pipeline (UAC if needed, journal, undo).</summary>
        private async void ExecuteOptimizeOp(string op, string id, object sender = null)
        {
            using (IDisposable fixLock = TryBeginFix(sender))
            {
                if (fixLock == null) return;
                using (BeginBusy(PendingFixMessage))
                {
                    FixOutcome outcome = await RunFixAsync(op);
                    SetToast(outcome.Message, outcome.Ok ? "SuccessFg" : "DangerFg");
                }
            }
            if (OptStartup.IsOp(op)) _optStartup = null;
            RenderOptimizeSub();
        }

        // ==================== 0 · Settings ====================
        private void RenderOptTweaks()
        {
            var p = _optPanels[0];
            p.Children.Clear();
            List<RegistryFixItem> items = OptTweaks.Items();
            int rec = items.Count(i => i.Status == FixStatus.Recommended);

            var applyRec = CreateFlatButton(rec > 0
                    ? string.Format(Localizer.S("optimize.apply_recommended"), rec)
                    : Localizer.S("optimize.recommended_already_applied"),
                rec > 0 ? "Accent" : "BgSubtle", rec > 0 ? "OnAccent" : "TextMuted", rec > 0 ? "Accent" : "BorderCard", new Thickness(14, 7, 14, 7), 12.5);
            applyRec.FontWeight = FontWeights.SemiBold;
            applyRec.IsEnabled = rec > 0;
            applyRec.Click += async (s, e) =>
            {
                List<FixRunResult> results;
                using (IDisposable fixLock = TryBeginFix(s))
                {
                    if (fixLock == null) return;
                    var ops = OptTweaks.Items().Where(i => i.Status == FixStatus.Recommended).Select(i => i.Id + "_apply").ToList();
                    if (ops.Count == 0) return;
                    using (BeginBusy(PendingFixMessage)) results = await RunFixBatchAsync(ops);
                }
                int ok = results.Count(r => r.Ok);
                SetToast(string.Format(Localizer.S("optimize.setting_s_applied_some_take_effect"), ok), ok == results.Count ? "SuccessFg" : "WarnFg");
                RenderOptTweaks();
            };
            p.Children.Add(OptActionBar(Localizer.S("optimize.only_settings_and_policies_documented_by"), applyRec));

            foreach (OptGroup g in Enum.GetValues(typeof(OptGroup)))
            {
                var inGroup = items.Where(i => OptTweaks.GroupOf(i.Id) == g).ToList();
                if (inGroup.Count == 0) continue;
                p.Children.Add(OptSectionTitle(OptTweaks.GroupName(g)));
                foreach (var it in inGroup) p.Children.Add(BuildFixCard(it, ExecuteOptimizeOp));
            }
        }

        // ==================== 1 · Apps ====================
        private void RenderOptApps()
        {
            var p = _optPanels[1];
            p.Children.Clear();
            _optAppChecks.Clear();
            if (_optApps == null)
            {
                p.Children.Add(OptNote(Localizer.S("optimize.looking_for_installed_apps")));
                if (!_optAppsLoading)
                {
                    _optAppsLoading = true;
                    Task.Run(() => OptApps.Scan()).ContinueWith(t =>
                    {
                        _optAppsLoading = false;
                        _optApps = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<AppEntry>();
                        if (_optSub == 1) RenderOptApps();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return;
            }

            _btnOptRemoveApps = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12.5);
            _btnOptRemoveApps.FontWeight = FontWeights.SemiBold;
            _btnOptRemoveApps.Click += (s, e) => RemoveSelectedApps();
            p.Children.Add(OptActionBar(Localizer.S("optimize.removed_for_your_account_only_without"), _btnOptRemoveApps));

            if (_optApps.Count == 0)
                p.Children.Add(OptCard(OptNote(Localizer.S("optimize.none_of_the_listed_unnecessary_apps"))));

            AddAppSection(p, Localizer.S("optimize.clutter_checked_by_default"), _optApps.Where(a => a.Suggested));
            AddAppSection(p, Localizer.S("optimize.your_call"), _optApps.Where(a => !a.Suggested));

            if (OptApps.OneDriveInstalled)
            {
                p.Children.Add(OptSectionTitle("OneDrive"));
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var t = OptNote(Localizer.S("optimize.onedrive_is_not_a_store_app"));
                t.Margin = new Thickness(0, 0, 12, 0);
                t.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(t);
                var b = CreateFlatButton(Localizer.S("optimize.open_apps"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 6, 12, 6), 12);
                b.Click += (s, e) => { try { Process.Start(new ProcessStartInfo { FileName = "ms-settings:appsfeatures", UseShellExecute = true }); } catch { } };
                Grid.SetColumn(b, 1);
                row.Children.Add(b);
                p.Children.Add(OptCard(row));
            }
            UpdateRemoveAppsButton();
        }

        private void AddAppSection(StackPanel p, string title, IEnumerable<AppEntry> apps)
        {
            var list = apps.ToList();
            if (list.Count == 0) return;
            p.Children.Add(OptSectionTitle(title));
            var stack = new StackPanel();
            foreach (AppEntry a in list)
            {
                var content = new StackPanel();
                content.Children.Add(ResFg(new TextBlock { Text = a.Title, FontSize = 13, FontWeight = FontWeights.SemiBold }, "TextPrimary"));
                content.Children.Add(ResFg(new TextBlock { Text = (a.Note != null ? a.Note + "  ·  " : "") + a.Name, FontSize = 11.5 }, "TextMuted"));
                var cb = new CheckBox { IsChecked = a.Suggested, Content = content, Margin = new Thickness(0, 4, 0, 4), VerticalContentAlignment = VerticalAlignment.Center };
                cb.Checked += (s, e) => UpdateRemoveAppsButton();
                cb.Unchecked += (s, e) => UpdateRemoveAppsButton();
                _optAppChecks[cb] = a;
                stack.Children.Add(cb);
            }
            p.Children.Add(OptCard(stack));
        }

        private void UpdateRemoveAppsButton()
        {
            if (_btnOptRemoveApps == null) return;
            int n = _optAppChecks.Count(kv => kv.Key.IsChecked == true);
            _btnOptRemoveApps.IsEnabled = n > 0;
            _btnOptRemoveApps.Content = string.Format(Localizer.S("optimize.remove_selected"), n);
        }

        private async void RemoveSelectedApps()
        {
            var sel = _optAppChecks.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            if (sel.Count == 0) return;
            string msg = Localizer.S("optimize.remove_these_apps_for_your_account")
                         + string.Join("\n", sel.Select(a => "• " + a.Title))
                         + Localizer.S("optimize.they_can_be_reinstalled_from_the");
            if (MessageBox.Show(this, msg, "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            List<string> removed = null, failed = null;
            using (IDisposable fixLock = TryBeginFix(_btnOptRemoveApps))
            {
                if (fixLock == null) return;
                using (BeginBusy(Localizer.S("optimize.removing_apps")))
                    await Task.Run(() => OptApps.Remove(sel, out removed, out failed));
            }

            // Journal: listed with the other changes, marked "reinstall from the Store" instead of undoable.
            History.AddJournal(removed.Select(t => new JournalEntry
            {
                Id = Guid.NewGuid().ToString("N"),
                Time = DateTime.Now,
                Op = "optapp_remove",
                FixId = "optapp",
                Title = Localizer.S("optimize.app_removed") + t,
                Ok = true,
                Message = Localizer.S("optimize.can_be_reinstalled_from_the_microsoft"),
                Changes = new List<ChangeRecord>(),
                Reversible = false
            }).ToList());
            RenderJournal();

            if (failed.Count == 0)
                SetToast(string.Format(Localizer.S("optimize.app_s_removed"), removed.Count), "SuccessFg");
            else
                SetToast(string.Format(Localizer.S("optimize.removed_failed"), removed.Count, failed.Count, string.Join(", ", failed)), "WarnFg");
            _optApps = null;
            RenderOptApps();
        }

        // ==================== 2 · Startup ====================
        private void RenderOptStartup()
        {
            var p = _optPanels[2];
            p.Children.Clear();
            if (_optStartup == null)
            {
                p.Children.Add(OptNote(Localizer.S("optimize.reading_startup_programs")));
                if (!_optStartupLoading)
                {
                    _optStartupLoading = true;
                    Task.Run(() => OptStartup.Scan()).ContinueWith(t =>
                    {
                        _optStartupLoading = false;
                        _optStartup = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<StartupEntry>();
                        if (_optSub == 2) RenderOptStartup();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return;
            }

            int on = _optStartup.Count(e => e.Enabled);
            p.Children.Add(OptActionBar(string.Format(Localizer.S("optimize.program_s_start_with_windows_disabling"), on), null));

            if (_optStartup.Count == 0)
                p.Children.Add(OptCard(OptNote(Localizer.S("optimize.no_startup_programs"))));

            foreach (StartupEntry e in _optStartup)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var left = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
                var head = new WrapPanel();
                head.Children.Add(ResFg(new TextBlock { Text = e.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 8, 0) }, e.Enabled ? "TextPrimary" : "TextMuted"));
                string bg, fg;
                if (e.Enabled) { bg = "SuccessSoftBg"; fg = "SuccessFg"; } else { bg = "BgSubtle"; fg = "TextMuted"; }
                head.Children.Add(ResBg(new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(7, 1, 7, 1),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = ResFg(new TextBlock { Text = e.Enabled ? Localizer.S("optimize.at_startup") : Localizer.S("risky_drivers.disabled"), FontSize = 11, FontWeight = FontWeights.SemiBold }, fg)
                }, bg));
                left.Children.Add(head);
                left.Children.Add(ResFg(new TextBlock { Text = (e.Publisher ?? Localizer.S("optimize.unknown_publisher")) + "  ·  " + e.SourceLabel + (e.Admin ? "  ·  ADMIN" : ""), FontSize = 11.5, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary"));
                left.Children.Add(ResFg(new TextBlock { Text = e.Command, FontSize = 11, FontFamily = Ds.MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0), ToolTip = e.Command }, "TextMuted"));
                if (e.Publisher != null && e.Publisher.StartsWith("Microsoft", StringComparison.OrdinalIgnoreCase))
                    left.Children.Add(ResFg(new TextBlock { Text = Localizer.S("optimize.windows_component_best_left_on"), FontSize = 11.5, Margin = new Thickness(0, 3, 0, 0) }, "WarnFg"));
                row.Children.Add(left);

                StartupEntry entry = e;
                var b = CreateFlatButton(e.Enabled ? Localizer.S("risky_drivers.disable") : Localizer.S("risky_drivers.re_enable"),
                                         "BgCard", "TextPrimary", "BorderStrong", new Thickness(13, 6, 13, 6), 12);
                b.MinWidth = 110;
                b.VerticalAlignment = VerticalAlignment.Center;
                b.Click += (s, ev) => ExecuteOptimizeOp(entry.Op(entry.Enabled), null, s);
                Grid.SetColumn(b, 1);
                row.Children.Add(b);
                p.Children.Add(OptCard(row));
            }
        }

        // ==================== 3 · Cleanup ====================
        private static string SizeText(long bytes)
        {
            if (bytes < 0) return Localizer.S("optimize.size_readable_with_admin_rights");
            if (bytes < 1024L * 1024 * 1024) return (bytes / (1024.0 * 1024)).ToString("N0") + " " + Localizer.S("stress_text.mb");
            return (bytes / (1024.0 * 1024 * 1024)).ToString("0.00") + " " + Localizer.S("stress_text.gb");
        }

        private void RenderOptClean()
        {
            var p = _optPanels[3];
            p.Children.Clear();
            _optCleanChecks.Clear();
            if (_optClean == null)
            {
                p.Children.Add(OptNote(Localizer.S("optimize.measuring_reclaimable_space")));
                if (!_optCleanLoading)
                {
                    _optCleanLoading = true;
                    Task.Run(() => OptCleanup.Scan()).ContinueWith(t =>
                    {
                        _optCleanLoading = false;
                        _optClean = t.Status == TaskStatus.RanToCompletion ? t.Result : new List<CleanItem>();
                        if (_optSub == 3) RenderOptClean();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                }
                return;
            }

            _btnOptClean = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12.5);
            _btnOptClean.FontWeight = FontWeights.SemiBold;
            _btnOptClean.Click += async (s, e) =>
            {
                List<FixRunResult> results;
                using (IDisposable fixLock = TryBeginFix(s))
                {
                    if (fixLock == null) return;
                    var ops = _optCleanChecks.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value.Op).ToList();
                    if (ops.Count == 0) return;
                    using (BeginBusy(Localizer.S("optimize.cleaning"))) results = await RunFixBatchAsync(ops);
                }
                SetToast(string.Join("  ", results.Select(r => r.Message)), results.All(r => r.Ok) ? "SuccessFg" : "WarnFg");
                _optClean = null;
                RenderOptClean();
            };
            p.Children.Add(OptActionBar(Localizer.S("optimize.only_caches_windows_rebuilds_on_its"), _btnOptClean));

            foreach (CleanItem it in _optClean)
            {
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var content = new StackPanel();
                content.Children.Add(ResFg(new TextBlock { Text = it.Title + (it.Admin ? "  ·  ADMIN" : ""), FontSize = 13, FontWeight = FontWeights.SemiBold }, "TextPrimary"));
                content.Children.Add(ResFg(new TextBlock { Text = it.Explanation, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary"));
                var cb = new CheckBox { IsChecked = it.Bytes != 0, Content = content, VerticalContentAlignment = VerticalAlignment.Top };
                cb.Checked += (s, e) => UpdateCleanButton();
                cb.Unchecked += (s, e) => UpdateCleanButton();
                _optCleanChecks[cb] = it;
                row.Children.Add(cb);
                var size = ResFg(new TextBlock { Text = SizeText(it.Bytes), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0), MaxWidth = 200, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Right }, it.Bytes > 0 ? "AccentText" : "TextMuted");
                Grid.SetColumn(size, 1);
                row.Children.Add(size);
                p.Children.Add(OptCard(row));
            }
            UpdateCleanButton();
        }

        private void UpdateCleanButton()
        {
            if (_btnOptClean == null) return;
            var sel = _optCleanChecks.Where(kv => kv.Key.IsChecked == true).Select(kv => kv.Value).ToList();
            long known = sel.Where(i => i.Bytes > 0).Sum(i => i.Bytes);
            _btnOptClean.IsEnabled = sel.Count > 0;
            _btnOptClean.Content = string.Format(Localizer.S("optimize.clean_selected"), SizeText(known));
        }

        // ==================== 4 · Storage: MainWindow.StoragePage.cs ====================

        // ==================== 5 · Myths ====================
        private void RenderOptMyths()
        {
            var p = _optPanels[5];
            p.Children.Clear();
            p.Children.Add(OptActionBar(Localizer.S("optimize.widespread_optimizations_dump_finder_del"), null));
            foreach (var m in Myths())
            {
                var s = new StackPanel();
                s.Children.Add(ResFg(new TextBlock { Text = m.Item1, FontSize = 13, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, "TextPrimary"));
                s.Children.Add(ResFg(new TextBlock { Text = m.Item2, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), LineHeight = 17 }, "TextSecondary"));
                p.Children.Add(OptCard(s));
            }
        }

        private static IEnumerable<Tuple<string, string>> Myths()
        {
            yield return Tuple.Create(
                Localizer.S("optimize.disabling_microsoft_defender"),
                Localizer.S("optimize.next_to_no_gain_on_a"));
            yield return Tuple.Create(
                Localizer.S("optimize.turning_off_spectre_meltdown_mitigations"),
                Localizer.S("optimize.recent_cpus_ryzen_7000_9000_intel"));
            yield return Tuple.Create(
                Localizer.S("optimize.removing_the_page_file"),
                Localizer.S("optimize.when_ram_fills_up_apps_crash"));
            yield return Tuple.Create(
                Localizer.S("optimize.disabling_sysmain_superfetch_or_indexing"),
                Localizer.S("optimize.made_sense_in_the_slow_hard"));
            yield return Tuple.Create(
                Localizer.S("optimize.hpet_dynamic_tick_and_other_bcdedit"),
                Localizer.S("optimize.measurements_find_no_gain_sometimes_more"));
            yield return Tuple.Create(
                Localizer.S("optimize.registry_cleaners"),
                Localizer.S("optimize.a_few_kb_saved_for_a"));
            yield return Tuple.Create(
                Localizer.S("optimize.anti_lag_network_tweaks_nagle_networkthr"),
                Localizer.S("optimize.today_s_games_manage_their_own"));
            yield return Tuple.Create(
                Localizer.S("optimize.removing_edge_or_webview2"),
                Localizer.S("optimize.widgets_teams_outlook_and_many_game"));
            yield return Tuple.Create(
                Localizer.S("optimize.the_ultimate_performance_power_plan_on"),
                Localizer.S("optimize.stops_the_cpu_from_clocking_down"));
            yield return Tuple.Create(
                Localizer.S("optimize.disabling_useless_windows_services_in_bu"),
                Localizer.S("optimize.negligible_memory_saved_depending_on_the"));
        }
    }
}
