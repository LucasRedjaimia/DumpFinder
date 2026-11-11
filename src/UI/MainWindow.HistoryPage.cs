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
                Button journal = CreateFlatButton(Localizer.L("📖 Journal", "📖 Journal", "📖 Registro", "📖 Protokoll", "📖 Registro", "📖 Registro"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 6, 12, 6), 12);
                journal.Margin = new Thickness(0, 0, 8, 0);
                journal.VerticalAlignment = VerticalAlignment.Center;
                journal.ToolTip = Localizer.L("Toutes les modifications faites par Dump Finder, annulables une par une.", "Every change Dump Finder made, undoable one by one.", "Todos los cambios, reversibles uno a uno.", "Alle Änderungen, einzeln umkehrbar.", "Tutte le modifiche, annullabili una per una.", "Todas as alterações, reversíveis uma a uma.");
                journal.Click += (s, e) => { SwitchTab(TabTools); SwitchToolsSubTab(2); };
                fixesBar.Children.Insert(Math.Max(0, fixesBar.Children.IndexOf(_btnRestoreSnapshot)), journal);
            }
        }

        private void RefreshHistoryLabels()
        {
            if (_btnToolsSubHistory == null) return;
            _btnToolsSubHistory.Content = Localizer.L("Historique", "History", "Historial", "Verlauf", "Cronologia", "Histórico");
            _lblHistTrendTitle.Text = Localizer.L("Évolution de la stabilité", "Stability over time", "Evolución de la estabilidad", "Stabilitätsverlauf", "Andamento della stabilità", "Evolução da estabilidade");
            _lblHistJournalTitle.Text = Localizer.L("Journal des modifications", "Change journal", "Registro de cambios", "Änderungsprotokoll", "Registro delle modifiche", "Registro de alterações");
            _lblHistJournalHint.Text = Localizer.L(
                "Chaque correctif note la valeur exacte qu'il remplace. « Annuler » remet précisément l'état d'avant, pas une valeur par défaut.",
                "Every fix records the exact value it replaces. \"Undo\" puts back precisely the previous state, not a default value.",
                "Cada corrección guarda el valor exacto que reemplaza. «Deshacer» restaura el estado anterior.",
                "Jede Korrektur speichert den exakten vorherigen Wert. „Rückgängig“ stellt genau diesen Zustand wieder her.",
                "Ogni correzione salva il valore esatto che sostituisce. «Annulla» ripristina lo stato precedente.",
                "Cada correção guarda o valor exato que substitui. «Desfazer» restaura o estado anterior.");
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
                SetToast(Localizer.L(
                    _newCrashKeys.Count + " nouveau(x) crash(s) depuis la dernière analyse" + (severe > 0 ? ", dont " + severe + " grave(s)." : "."),
                    _newCrashKeys.Count + " new crash(es) since the last scan" + (severe > 0 ? ", " + severe + " severe." : "."),
                    _newCrashKeys.Count + " fallo(s) nuevo(s) desde el último análisis.",
                    _newCrashKeys.Count + " neue(r) Absturz/Abstürze seit der letzten Prüfung.",
                    _newCrashKeys.Count + " nuovo/i crash dall'ultima analisi.",
                    _newCrashKeys.Count + " nova(s) falha(s) desde a última análise."), severe > 0 ? "DangerFg" : "WarnFg");
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
                _lblHistTrendSummary.Text = Localizer.L("L'historique se construit à chaque analyse.", "History builds up with every scan.", "El historial se construye con cada análisis.", "Der Verlauf entsteht mit jeder Prüfung.", "La cronologia si costruisce a ogni analisi.", "O histórico se forma a cada análise.");
                DrawHistorySpark();
                return;
            }

            var last = _histDaily[_histDaily.Count - 1];
            var first = _histDaily[0];
            int delta = last.Score - first.Score;
            _lblHistTrendSummary.Text = _histDaily.Count == 1
                ? Localizer.L("Première analyse enregistrée : " + last.Score + "/100. Revenez dans quelques jours pour voir l'effet de vos correctifs.",
                              "First scan recorded: " + last.Score + "/100. Come back in a few days to see the effect of your fixes.",
                              "Primer análisis: " + last.Score + "/100.", "Erste Prüfung: " + last.Score + "/100.", "Prima analisi: " + last.Score + "/100.", "Primeira análise: " + last.Score + "/100.")
                : Localizer.L(
                    string.Format("{0}/100 aujourd'hui, {1}/100 le {2:d MMM} ({3}{4} points sur {5} jours d'analyses).", last.Score, first.Score, first.Time, delta >= 0 ? "+" : "", delta, (last.Time.Date - first.Time.Date).Days),
                    string.Format("{0}/100 today, {1}/100 on {2:d MMM} ({3}{4} points over {5} days of scans).", last.Score, first.Score, first.Time, delta >= 0 ? "+" : "", delta, (last.Time.Date - first.Time.Date).Days),
                    string.Format("{0}/100 hoy, {1}/100 el {2:d MMM}.", last.Score, first.Score, first.Time),
                    string.Format("{0}/100 heute, {1}/100 am {2:d MMM}.", last.Score, first.Score, first.Time),
                    string.Format("{0}/100 oggi, {1}/100 il {2:d MMM}.", last.Score, first.Score, first.Time),
                    string.Format("{0}/100 hoje, {1}/100 em {2:d MMM}.", last.Score, first.Score, first.Time));

            foreach (var s in _histDaily.AsEnumerable().Reverse().Take(8))
            {
                Grid row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(ResFg(new TextBlock { Text = s.Time.ToString("ddd d MMM", CultureInfo.CurrentUICulture), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, "TextSecondary"));
                StabilityLevel lv = Stability.LevelOf(s.Score);
                Border pill = MakePill(s.Score + "/100", lv == StabilityLevel.Stable ? "SuccessSoftBg" : lv == StabilityLevel.Fragile ? "WarnSoftBg" : "DangerSoftBg", lv == StabilityLevel.Stable ? "SuccessFg" : lv == StabilityLevel.Fragile ? "WarnFg" : "DangerFg");
                Grid.SetColumn(pill, 1);
                row.Children.Add(pill);
                TextBlock d = ResFg(new TextBlock
                {
                    Text = Localizer.L(s.Crashes30 + " crash(s) sur 30 j", s.Crashes30 + " crash(es) in 30 d", s.Crashes30 + " fallo(s) en 30 d", s.Crashes30 + " Absturz/Abstürze in 30 T", s.Crashes30 + " crash in 30 g", s.Crashes30 + " falha(s) em 30 d")
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
                    Localizer.L("Aucune modification pour l'instant. Les correctifs appliqués apparaîtront ici.", "No changes yet. Applied fixes will show up here.", "Ningún cambio todavía.", "Noch keine Änderungen.", "Nessuna modifica per ora.", "Nenhuma alteração ainda.")));
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
                + (j.Ok ? Localizer.L(n + " valeur(s) modifiée(s)", n + " value(s) changed", n + " valor(es)", n + " Wert(e)", n + " valore/i", n + " valor(es)") : j.Message);
            if (reverted)
                sub += "  ·  " + Localizer.L("annulé le ", "undone on ", "deshecho el ", "rückgängig am ", "annullato il ", "desfeito em ") + j.RevertedAt.Value.ToString("g", CultureInfo.CurrentUICulture);
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
                Button undo = CreateFlatButton(Localizer.L("↺ Annuler", "↺ Undo", "↺ Deshacer", "↺ Rückgängig", "↺ Annulla", "↺ Desfazer"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 5, 12, 5), 12);
                undo.VerticalAlignment = VerticalAlignment.Center;
                undo.Click += async (s, e) => await UndoJournalEntry(j);
                right = undo;
            }
            else if (j.Ok && !j.Reversible)
            {
                right = MakePill(Localizer.L("Non annulable", "Not undoable", "No reversible", "Nicht umkehrbar", "Non annullabile", "Não reversível"), "BgSubtle", "TextMuted");
            }
            else right = new Border();
            Grid.SetColumn(right, 2);
            row.Children.Add(right);

            return ResBorder(new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(0, 10, 0, 10), Child = row }, "BorderCard");
        }

        private async Task UndoJournalEntry(JournalEntry j)
        {
            bool ok;
            string error = null;
            using (BeginBusy(Localizer.L("Annulation en cours…", "Undoing…", "Deshaciendo…", "Wird rückgängig gemacht…", "Annullamento…", "Desfazendo…")))
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
            RenderJournal();
            SetToast(ok
                    ? Localizer.L("Modification annulée : ", "Change undone: ", "Cambio deshecho: ", "Änderung rückgängig: ", "Modifica annullata: ", "Alteração desfeita: ") + j.Title
                    : Localizer.L("Annulation impossible : ", "Could not undo: ", "No se pudo deshacer: ", "Rückgängig nicht möglich: ", "Impossibile annullare: ", "Não foi possível desfazer: ") + error,
                ok ? "SuccessFg" : "WarnFg");
        }
    }
}
