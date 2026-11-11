using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WinCrashDoctor
{
    /// <summary>
    /// v6 information architecture: re-hosts blocks built by the legacy page builders
    /// (stress test → Hardware, report → Tools) so their logic stays untouched.
    /// </summary>
    public partial class MainWindow
    {
        private ScrollViewer _svWinTools;
        private Button _btnToolsSubWindows;
        private Button _btnToolsSubReport;
        private int _toolsSubTab;

        /// <summary>The stress test becomes the third sub-view of Hardware; its own page header is dropped.</summary>
        private void HostStressInHardware()
        {
            var stressHeader = _lblStressTitle.Parent as FrameworkElement;
            if (stressHeader != null) stressHeader.Visibility = Visibility.Collapsed;
            _pageStress.Visibility = Visibility.Collapsed;
            Grid.SetRow(_pageStress, 1);
            _pageHealth.Children.Add(_pageStress);
        }

        // ==================== HARDWARE › DRIVERS ====================
        private ScrollViewer _svHealthDrivers;
        private StackPanel _driversPanel;
        private Button _btnHealthSubDrivers;
        private TextBlock _lblDriversSummary;

        private void HostDriversInHardware()
        {
            var seg = _btnHealthSubHw.Parent as StackPanel;
            _btnHealthSubDrivers = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12);
            _btnHealthSubDrivers.Margin = new Thickness(0, 0, 6, 0);
            _btnHealthSubDrivers.Click += (s, e) => SwitchHealthSubTab(3);
            if (seg != null) seg.Children.Insert(Math.Max(0, seg.Children.IndexOf(_btnHealthSubStress)), _btnHealthSubDrivers);

            _svHealthDrivers = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
            StackPanel wrap = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            _lblDriversSummary = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 0, 0, 12) }, "TextSecondary");
            wrap.Children.Add(_lblDriversSummary);
            Border card = MakeCard(new Thickness(6), new Thickness(0));
            _driversPanel = new StackPanel();
            card.Child = _driversPanel;
            wrap.Children.Add(card);
            _svHealthDrivers.Content = wrap;
            Grid.SetRow(_svHealthDrivers, 1);
            _pageHealth.Children.Add(_svHealthDrivers);
        }

        private void RefreshDriverLabels()
        {
            if (_btnHealthSubDrivers == null) return;
            _btnHealthSubDrivers.Content = Localizer.L("Pilotes", "Drivers", "Controladores", "Treiber", "Driver", "Drivers");
            _btnHealthSubConf.Content = Localizer.L("Conflits", "Conflicts", "Conflictos", "Konflikte", "Conflitti", "Conflitos");
        }

        /// <summary>Crash entries whose culprit (or stack) names this driver image.</summary>
        private int CrashCountForImage(string image)
        {
            if (string.IsNullOrEmpty(image)) return 0;
            return _crashEntries.Count(c => c.Dump != null &&
                (string.Equals(c.Dump.PrimaryCulpritModule, image, StringComparison.OrdinalIgnoreCase)));
        }

        private void RenderDrivers(ScanResult res)
        {
            if (_driversPanel == null) return;
            _driversPanel.Children.Clear();
            var drivers = res != null && res.Drivers != null ? res.Drivers : new System.Collections.Generic.List<DriverInfo>();

            var ordered = drivers
                .Select(d => new { D = d, Crashes = CrashCountForImage(d.ImageName) })
                .OrderByDescending(x => x.Crashes)
                .ThenByDescending(x => x.D.IsOld)
                .ThenBy(x => x.D.DeviceClass)
                .ThenBy(x => x.D.DeviceName)
                .ToList();

            int old = ordered.Count(x => x.D.IsOld), linked = ordered.Count(x => x.Crashes > 0);
            _lblDriversSummary.Text = Localizer.L(
                string.Format("{0} pilotes tiers installés · {1} de plus de deux ans · {2} mis en cause dans un crash.", ordered.Count, old, linked),
                string.Format("{0} third-party drivers installed · {1} older than two years · {2} implicated in a crash.", ordered.Count, old, linked),
                string.Format("{0} controladores de terceros · {1} con más de dos años · {2} implicados en un fallo.", ordered.Count, old, linked),
                string.Format("{0} Fremdtreiber installiert · {1} älter als zwei Jahre · {2} in einen Absturz verwickelt.", ordered.Count, old, linked),
                string.Format("{0} driver di terze parti · {1} con più di due anni · {2} coinvolti in un crash.", ordered.Count, old, linked),
                string.Format("{0} drivers de terceiros · {1} com mais de dois anos · {2} envolvidos em uma falha.", ordered.Count, old, linked));

            if (ordered.Count == 0)
            {
                _driversPanel.Children.Add(MakeEmptyState(Glyph.Component, "TextSecondary",
                    Localizer.L("Inventaire indisponible", "Inventory unavailable", "Inventario no disponible", "Inventar nicht verfügbar", "Inventario non disponibile", "Inventário indisponível"),
                    Localizer.L("Windows n'a pas renvoyé la liste des pilotes. Relancez l'analyse.", "Windows did not return the driver list. Run the scan again.", "Windows no devolvió la lista. Vuelva a analizar.", "Windows lieferte keine Treiberliste. Erneut analysieren.", "Windows non ha restituito l'elenco. Riprova.", "O Windows não retornou a lista. Analise novamente.")));
                return;
            }

            for (int i = 0; i < ordered.Count; i++)
                _driversPanel.Children.Add(BuildDriverRow(ordered[i].D, ordered[i].Crashes, i == 0));
        }

        private UIElement BuildDriverRow(DriverInfo d, int crashes, bool first)
        {
            Grid row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            row.Children.Add(MakeIconTile(DriverClassGlyph(d.DeviceClass), crashes > 0 ? "DangerSoftBg" : "BgSubtle", crashes > 0 ? "DangerFg" : "TextSecondary"));

            StackPanel t = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            t.Children.Add(ResFg(new TextBlock { Text = d.DeviceName, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary"));
            string meta = d.Provider + "  ·  " + d.Version + (d.Date.HasValue ? "  ·  " + d.Date.Value.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentUICulture) : "")
                          + (string.IsNullOrEmpty(d.ImageName) ? "" : "  ·  " + d.ImageName);
            t.Children.Add(ResFg(new TextBlock { Text = meta, FontSize = 11, FontFamily = Ds.MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) }, "TextMuted"));
            Grid.SetColumn(t, 1);
            row.Children.Add(t);

            StackPanel badges = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (crashes > 0)
            {
                Border b = MakePill(Localizer.L(crashes == 1 ? "Mis en cause dans 1 crash" : "Mis en cause dans " + crashes + " crashs", crashes == 1 ? "Blamed for 1 crash" : "Blamed for " + crashes + " crashes", "Culpable de " + crashes + " fallo(s)", "Verursacht " + crashes + " Absturz/Abstürze", "Coinvolto in " + crashes + " crash", "Culpado em " + crashes + " falha(s)"), "DangerSoftBg", "DangerFg");
                b.Margin = new Thickness(0, 0, 6, 0);
                badges.Children.Add(b);
            }
            if (d.IsOld)
            {
                Border b = MakePill(Localizer.L("Ancien · " + (d.AgeDays / 365) + " ans", "Old · " + (d.AgeDays / 365) + " years", "Antiguo · " + (d.AgeDays / 365) + " años", "Alt · " + (d.AgeDays / 365) + " J.", "Vecchio · " + (d.AgeDays / 365) + " anni", "Antigo · " + (d.AgeDays / 365) + " anos"), "WarnSoftBg", "WarnFg");
                b.Margin = new Thickness(0, 0, 6, 0);
                badges.Children.Add(b);
            }
            Grid.SetColumn(badges, 2);
            row.Children.Add(badges);

            Button upd = CreateFlatButton(Localizer.L("🔍 Mise à jour", "🔍 Update", "🔍 Actualizar", "🔍 Update", "🔍 Aggiorna", "🔍 Atualizar"), "BgCard", "AccentText", "BorderCard", new Thickness(10, 4, 10, 4), 12);
            upd.VerticalAlignment = VerticalAlignment.Center;
            upd.ToolTip = Localizer.L("Cherche la dernière version chez le fabricant.", "Searches for the latest version from the vendor.", "Busca la última versión del fabricante.", "Sucht die neueste Version beim Hersteller.", "Cerca l'ultima versione dal produttore.", "Procura a versão mais recente do fabricante.");
            string q = d.Provider + " " + d.DeviceName + " driver download";
            upd.Click += (s, e) => ExecuteQuickAction(null, q);
            Grid.SetColumn(upd, 3);
            row.Children.Add(upd);

            return ResBorder(new Border { BorderThickness = new Thickness(0, first ? 0 : 1, 0, 0), Padding = new Thickness(10, 10, 10, 10), Child = row }, "BorderCard");
        }

        private static string DriverClassGlyph(string cls)
        {
            switch ((cls ?? "").ToUpperInvariant())
            {
                case "DISPLAY": return Glyph.Video;
                case "NET": return Glyph.Globe;
                case "DISKDRIVE":
                case "SCSIADAPTER":
                case "HDC": return Glyph.Save;
                case "USB":
                case "HIDCLASS": return Glyph.Component;
                case "MEDIA":
                case "AUDIOENDPOINT": return Glyph.Video;
                default: return Glyph.Component;
            }
        }

        /// <summary>Outils = Windows launchers + report/export, behind a segmented switch.</summary>
        private void HostReportInTools()
        {
            _svWinTools = _pageWinTools.Children.OfType<ScrollViewer>().FirstOrDefault();

            // The header SFC shortcut duplicated the "SFC / DISM" tool card: the switch takes its place.
            var hdr = _btnQuickSfcAdmin.Parent as Grid;
            _btnQuickSfcAdmin.Visibility = Visibility.Collapsed;

            StackPanel seg = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnToolsSubWindows = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(14, 6, 14, 6), 12);
            _btnToolsSubWindows.Margin = new Thickness(0, 0, 6, 0);
            _btnToolsSubWindows.Click += (s, e) => SwitchToolsSubTab(0);
            _btnToolsSubReport = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(14, 6, 14, 6), 12);
            _btnToolsSubReport.Click += (s, e) => SwitchToolsSubTab(1);
            seg.Children.Add(_btnToolsSubWindows);
            seg.Children.Add(_btnToolsSubReport);
            if (hdr != null)
            {
                Grid.SetColumn(seg, 1);
                hdr.Children.Add(seg);
            }

            DetachFromParent(_reportSubPanel);
            _reportSubPanel.Visibility = Visibility.Collapsed;
            Grid.SetRow(_reportSubPanel, 1);
            _pageWinTools.Children.Add(_reportSubPanel);
            SwitchToolsSubTab(0);
        }

        /// <summary>Used by the --screenshot harness to reach sub-views.</summary>
        public void CaptureSubView(string name)
        {
            switch (name)
            {
                case "crashes-guide": SwitchCrashSubTab(true); break;
                case "crashes-list": SwitchCrashSubTab(false); break;
                case "tools-report": SwitchToolsSubTab(1); break;
                case "tools-windows": SwitchToolsSubTab(0); break;
                case "tools-history": SwitchToolsSubTab(2); break;
                case "optimize-apps": SwitchOptimizeSub(1); break;
                case "optimize-startup": SwitchOptimizeSub(2); break;
                case "optimize-clean": SwitchOptimizeSub(3); break;
                case "optimize-myths": SwitchOptimizeSub(4); break;
                case "conflicts-bottom":
                    SwitchHealthSubTab(1);
                    _svHealthConf.UpdateLayout();
                    _svHealthConf.ScrollToEnd();
                    break;
                case "fixes-bottom":
                    var fsv = FindScrollParent(_fixesListPanel);
                    if (fsv != null) { fsv.UpdateLayout(); fsv.ScrollToEnd(); }
                    break;
                case "crash-drawer-bottom":
                    var g = _crashDrawer.Child as Grid;
                    var sv = g != null ? g.Children.OfType<ScrollViewer>().FirstOrDefault() : null;
                    if (sv != null) { sv.UpdateLayout(); sv.ScrollToEnd(); }
                    break;
            }
        }

        private static ScrollViewer FindScrollParent(DependencyObject d)
        {
            while (d != null && !(d is ScrollViewer)) d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            return d as ScrollViewer;
        }

        private void SwitchToolsSubTab(int sub)
        {
            _toolsSubTab = sub;
            if (_svWinTools != null) _svWinTools.Visibility = sub == 0 ? Visibility.Visible : Visibility.Collapsed;
            _reportSubPanel.Visibility = sub == 1 ? Visibility.Visible : Visibility.Collapsed;
            if (_svHistory != null) _svHistory.Visibility = sub == 2 ? Visibility.Visible : Visibility.Collapsed;
            StyleTogglePill(_btnToolsSubWindows, sub == 0);
            StyleTogglePill(_btnToolsSubReport, sub == 1);
            if (_btnToolsSubHistory != null) StyleTogglePill(_btnToolsSubHistory, sub == 2);
            if (sub == 2) { RenderHistoryTrend(); RenderJournal(); }
            AnimatePageIn(sub == 0 ? (FrameworkElement)_svWinTools : sub == 1 ? (FrameworkElement)_reportSubPanel : _svHistory);
        }

        private void RefreshToolsSubLabels()
        {
            if (_btnToolsSubWindows == null) return;
            _btnToolsSubWindows.Content = Localizer.L("Outils Windows", "Windows tools", "Herramientas Windows", "Windows-Tools", "Strumenti Windows", "Ferramentas Windows");
            _btnToolsSubReport.Content = Localizer.L("Rapport & export", "Report & export", "Informe y exportar", "Bericht & Export", "Rapporto ed esporta", "Relatório e exportar");
            _lblWinToolsMainTitle.Text = Localizer.L("Outils", "Tools", "Herramientas", "Werkzeuge", "Strumenti", "Ferramentas");
            _lblWinToolsMainSub.Text = Localizer.L(
                "Les utilitaires Windows utiles au diagnostic, et le rapport complet à partager.",
                "The Windows utilities that help diagnose, and the full report to share.",
                "Las utilidades de Windows para diagnosticar y el informe completo para compartir.",
                "Die hilfreichen Windows-Werkzeuge und der vollständige Bericht zum Teilen.",
                "Le utility di Windows per la diagnosi e il rapporto completo da condividere.",
                "Os utilitários do Windows para diagnóstico e o relatório completo para compartilhar.");
        }
    }
}
