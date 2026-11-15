using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinCrashDoctor
{
    /// <summary>
    /// Outils › Historique: score trend across scans and the fix journal with exact, per-change undo.
    /// Also records each scan and flags crashes that are new since the previous one.
    /// </summary>
    public partial class MainWindow
    {
        private ScrollViewer _svHistory;
        private Button _btnToolsSubHistory;
        private TextBlock _lblHistTrendTitle, _lblHistTrendSummary, _lblHistJournalTitle, _lblHistJournalHint;
        private Canvas _histSpark;
        private StackPanel _histScansList;
        private StackPanel _journalList;
        private HashSet<string> _newCrashKeys = new HashSet<string>();

        private void BuildHistoryView()
        {
            _svHistory = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
            StackPanel stack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };

            // Trend card
            Border trend = MakeCard(new Thickness(18, 14, 18, 14), new Thickness(0, 0, 0, 16));
            StackPanel ts = new StackPanel();
            _lblHistTrendTitle = MakeSectionTitle();
            ts.Children.Add(_lblHistTrendTitle);
            _lblHistTrendSummary = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) }, "TextSecondary");
            ts.Children.Add(_lblHistTrendSummary);
            _histSpark = new Canvas { Height = 56, ClipToBounds = true, Margin = new Thickness(0, 0, 0, 8) };
            _histSpark.SizeChanged += (s, e) => DrawHistorySpark();
            ts.Children.Add(_histSpark);
            _histScansList = new StackPanel();
            ts.Children.Add(_histScansList);
            trend.Child = ts;
            stack.Children.Add(trend);

            // Journal card
            Border jc = MakeCard(new Thickness(18, 14, 18, 10), new Thickness(0));
            StackPanel js = new StackPanel();
            _lblHistJournalTitle = MakeSectionTitle();
            js.Children.Add(_lblHistJournalTitle);
            _lblHistJournalHint = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 8) }, "TextSecondary");
            js.Children.Add(_lblHistJournalHint);
            _journalList = new StackPanel();
            js.Children.Add(_journalList);
            jc.Child = js;
            stack.Children.Add(jc);

            _svHistory.Content = stack;
            Grid.SetRow(_svHistory, 1);
            _pageWinTools.Children.Add(_svHistory);

            // Third segment in Outils
            var seg = _btnToolsSubReport.Parent as StackPanel;
            _btnToolsSubReport.Margin = new Thickness(0, 0, 6, 0);
            _btnToolsSubHistory = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(14, 6, 14, 6), 12);
            _btnToolsSubHistory.Click += (s, e) => SwitchToolsSubTab(2);
            if (seg != null) seg.Children.Add(_btnToolsSubHistory);

            // "Journal" shortcut in Correctifs, next to the rollback button
            var fixesBar = _btnRestoreSnapshot != null ? _btnRestoreSnapshot.Parent as Panel : null;
            if (fixesBar != null)
            {
                Button journal = CreateFlatButton(Localizer.S("history.journal"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 6, 12, 6), 12);
                journal.Margin = new Thickness(0, 0, 8, 0);
                journal.VerticalAlignment = VerticalAlignment.Center;
                journal.ToolTip = Localizer.S("history.every_change_dump_finder_made_undoable");
                journal.Click += (s, e) => { SwitchTab(TabTools); SwitchToolsSubTab(2); };
                fixesBar.Children.Insert(Math.Max(0, fixesBar.Children.IndexOf(_btnRestoreSnapshot)), journal);
            }
        }

        private void RefreshHistoryLabels()
        {
            if (_btnToolsSubHistory == null) return;
            _btnToolsSubHistory.Content = Localizer.S("crashes.history");
            _lblHistTrendTitle.Text = Localizer.S("history.stability_over_time");
            _lblHistJournalTitle.Text = Localizer.S("history.change_journal");
            _lblHistJournalHint.Text = Localizer.S("history.every_fix_records_the_exact_value");
        }

        // ==================== SCAN RECORDING ====================
        private void RecordScan(ScanResult res)
        {
            DateTime now = DateTime.Now;
            var recent = _crashEntries.Where(c => (now - c.Time).TotalDays <= Stability.WindowDays).ToList();

            // Crashes that appeared since the previous scan (first launch only remembers them).
            _newCrashKeys = new HashSet<string>(History.TakeNewCrashes(recent.Select(c => c.Key)));
            if (_newCrashKeys.Count > 0)
            {
                int severe = recent.Count(c => _newCrashKeys.Contains(c.Key) && Stability.IsSevere(c.Category));
                SetToast(Localizer.P("history.new_crashes", _newCrashKeys.Count)
                         + (severe > 0 ? Localizer.P("history.new_crashes_severe", severe) : Localizer.S("common.sentence_end")), severe > 0 ? "DangerFg" : "WarnFg");
                RenderCrashList();
            }

            var window = (res.Events ?? new List<CrashEventItem>()).Where(e => (now - e.Timestamp).TotalDays <= Stability.WindowDays).ToList();
            History.AddScan(new ScanRecord
            {
                Time = now,
                Score = _homeScore,
                CrashScore = Stability.Score(window, now),
                WindowsIndex = Reliability.Latest(res.Reliability),
                Crashes30 = recent.Count,
                Severe30 = recent.Count(c => Stability.IsSevere(c.Category)),
                Diagnosis = res.DiagnosisTitle
            });
            RenderHistoryTrend();
        }

        // ==================== TREND ====================
        private List<ScanRecord> _histDaily = new List<ScanRecord>();

        private void RenderHistoryTrend()
        {
            if (_histScansList == null) return;
            // One point per day: the last scan of each day.
            _histDaily = History.Scans()
                .GroupBy(s => s.Time.Date)
                .Select(g => g.OrderBy(s => s.Time).Last())
                .OrderBy(s => s.Time)
                .ToList();

            _histScansList.Children.Clear();
            if (_histDaily.Count == 0)
            {
                _lblHistTrendSummary.Text = Localizer.S("history.history_builds_up_with_every_scan");
                DrawHistorySpark();
                return;
            }

            var last = _histDaily[_histDaily.Count - 1];
            var first = _histDaily[0];
            int delta = last.Score - first.Score;
            _lblHistTrendSummary.Text = _histDaily.Count == 1
                ? Localizer.F("history.first_scan_recorded_100_come_back", last.Score)
                : Localizer.F("history.trend_summary", last.Score, first.Score, Localizer.DayMonth(first.Time), delta >= 0 ? "+" : "", delta,
                              (last.Time.Date - first.Time.Date).Days);

            foreach (var s in _histDaily.AsEnumerable().Reverse().Take(8))
            {
                Grid row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(ResFg(new TextBlock { Text = s.Time.ToString("ddd d MMM", Localizer.Culture), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, "TextSecondary"));
                StabilityLevel lv = Stability.LevelOf(s.Score);
                Border pill = MakePill(s.Score + "/100", lv == StabilityLevel.Stable ? "SuccessSoftBg" : lv == StabilityLevel.Fragile ? "WarnSoftBg" : "DangerSoftBg", lv == StabilityLevel.Stable ? "SuccessFg" : lv == StabilityLevel.Fragile ? "WarnFg" : "DangerFg");
                Grid.SetColumn(pill, 1);
                row.Children.Add(pill);
                TextBlock d = ResFg(new TextBlock
                {
                    Text = Localizer.F("history.crash_es_in_30_d", s.Crashes30)
                           + (s.WindowsIndex.HasValue ? "  ·  Windows " + s.WindowsIndex.Value.ToString("0.0") : ""),
                    FontSize = 12,
                    Margin = new Thickness(12, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center
                }, "TextMuted");
                Grid.SetColumn(d, 2);
                row.Children.Add(d);
                _histScansList.Children.Add(row);
            }
            DrawHistorySpark();
        }

        private void DrawHistorySpark()
        {
            if (_histSpark == null) return;
            _histSpark.Children.Clear();
            double w = _histSpark.ActualWidth, h = _histSpark.ActualHeight;
            if (w < 20 || _histDaily.Count < 2) { _histSpark.Visibility = _histDaily.Count < 2 ? Visibility.Collapsed : Visibility.Visible; return; }
            _histSpark.Visibility = Visibility.Visible;
            var line = new Polyline { StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round };
            line.SetResourceReference(Shape.StrokeProperty, "AccentText");
            for (int i = 0; i < _histDaily.Count; i++)
            {
                double x = 4 + i * (w - 8) / (_histDaily.Count - 1);
                double y = 4 + (1 - _histDaily[i].Score / 100.0) * (h - 8);
                line.Points.Add(new Point(x, y));
            }
            _histSpark.Children.Add(line);
        }

        // ==================== JOURNAL ====================
        private void RenderJournal()
        {
            if (_journalList == null) return;
            _journalList.Children.Clear();
            var entries = History.Journal();
            if (entries.Count == 0)
            {
                _journalList.Children.Add(MakeEmptyLine(Glyph.Info, "TextMuted",
                    Localizer.S("history.no_changes_yet_applied_fixes_will")));
                return;
            }
            for (int i = 0; i < Math.Min(60, entries.Count); i++)
                _journalList.Children.Add(BuildJournalRow(entries[i], i == 0));
        }

        private UIElement BuildJournalRow(JournalEntry j, bool first)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            bool reverted = j.RevertedAt.HasValue;
            row.Children.Add(MakeIconTile(!j.Ok ? Glyph.ErrorBadge : reverted ? Glyph.Undo : Glyph.Completed,
                !j.Ok ? "DangerSoftBg" : reverted ? "BgSubtle" : "SuccessSoftBg",
                !j.Ok ? "DangerFg" : reverted ? "TextSecondary" : "SuccessFg"));

            StackPanel t = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(ResFg(new TextBlock { Text = j.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary"));
            int n = j.Changes != null ? j.Changes.Count : 0;
            string sub = j.Time.ToString("g", CultureInfo.CurrentUICulture) + "  ·  "
                + (j.Ok ? Localizer.F("history.value_s_changed", n) : Localizer.Render(j.Message));
            if (reverted)
                sub += "  ·  " + Localizer.S("history.undone_on") + j.RevertedAt.Value.ToString("g", CultureInfo.CurrentUICulture);
            t.Children.Add(ResFg(new TextBlock { Text = sub, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }, "TextMuted"));
            if (n > 0)
            {
                t.ToolTip = string.Join("\n", j.Changes.Select(c => "• " + c.Describe()));
            }
            Grid.SetColumn(t, 1);
            row.Children.Add(t);

            UIElement right;
            if (j.Ok && j.Reversible && !reverted)
            {
                Button undo = CreateFlatButton(Localizer.S("history.undo"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 5, 12, 5), 12);
                undo.VerticalAlignment = VerticalAlignment.Center;
                undo.Click += async (s, e) => await UndoJournalEntry(j, s);
                right = undo;
            }
            else if (j.Ok && !j.Reversible)
            {
                right = MakePill(Localizer.S("history.not_undoable"), "BgSubtle", "TextMuted");
            }
            else right = new Border();
            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            return ResBorder(new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 10, 0, 10), Child = row }, "BorderCard");
        }

        private async Task UndoJournalEntry(JournalEntry j, object sender = null)
        {
            bool ok;
            string error = null;
            using (IDisposable fixLock = TryBeginFix(sender))
            {
                if (fixLock == null) return;
                using (BeginBusy(Localizer.S("history.undoing")))
                {
                    ok = await Task.Run(() => FixRunner.Revert(j.Changes, out error));
                    if (ok)
                    {
                        History.MarkReverted(j.Id);
                        if (_lastResult != null)
                        {
                            _lastResult.Fixes = await Task.Run(() => DiagnosticsEngine.InspectAllFixes(false));
                            RenderScanResult(_lastResult);
                        }
                    }
                }
            }
            RenderJournal();
            SetToast(ok
                    ? Localizer.S("history.change_undone") + j.Title
                    : Localizer.S("history.could_not_undo") + error,
                ok ? "SuccessFg" : "WarnFg");
        }
    }
}
