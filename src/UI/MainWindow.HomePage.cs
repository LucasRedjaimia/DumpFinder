using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>Accueil: stability score, one-line verdict, 30-day timeline, next actions, recent crashes.</summary>
    public partial class MainWindow
    {
        private Grid _pageHome;
        private TextBlock _lblHomeTitle;
        private TextBlock _lblHomeSub;
        private Button _btnHomeExport;

        private Border _homeHero;
        private ScoreRing _scoreRing;
        private TextBlock _lblScoreValue;
        private TextBlock _lblScoreCaption;
        private Border _homeLevelPill;
        private Border _homeWinPill;
        private Border _homeDeltaPill;
        private TextBlock _lblHomeLevel;
        private TextBlock _heroTitle;
        private TextBlock _heroBody;
        private Button _btnHeroPrimary;
        private Button _btnHeroSecondary;

        private TextBlock _lblTimelineTitle;
        private StackPanel _timelineLegend;
        private StabilityChart _stabilityChart;

        private TextBlock _lblHomeActionsTitle;
        private Button _btnHomeAllFixes;
        private StackPanel _homeActionsList;
        private TextBlock _lblHomeRecentTitle;
        private Button _btnHomeAllCrashes;
        private StackPanel _homeRecentList;

        private Border MakeCard(Thickness padding, Thickness margin)
        {
            return ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(14),
                BorderThickness = new Thickness(1),
                Padding = padding,
                Margin = margin
            }, "BgCard"), "BorderCard");
        }

        private Border MakePill(string text, string bgKey, string fgKey)
        {
            return ResBg(new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(9, 2, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Child = ResFg(new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold }, fgKey)
            }, bgKey);
        }

        private Border MakeIconTile(string glyph, string bgKey, string fgKey, double size = 32)
        {
            return ResBg(new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(9),
                VerticalAlignment = VerticalAlignment.Center,
                Child = ResFg(Ds.Icon(glyph, 14), fgKey)
            }, bgKey);
        }

        private TextBlock MakeSectionTitle()
        {
            return ResFg(new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, "TextPrimary");
        }

        private Button MakeLinkButton(Action onClick)
        {
            Button b = CreateFlatButton("", "BgCard", "AccentText", "BgCard", new Thickness(8, 3, 8, 3), 12);
            b.BorderThickness = new Thickness(0);
            b.Padding = new Thickness(8, 3, 8, 3);
            b.HorizontalAlignment = HorizontalAlignment.Right;
            b.VerticalAlignment = VerticalAlignment.Center;
            b.Click += (s, e) => onClick();
            return b;
        }

        private void BuildHomePage()
        {
            _pageHome = new Grid();
            ScrollViewer sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            StackPanel stack = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            sv.Content = stack;
            _pageHome.Children.Add(sv);

            // ---- Header
            Grid hdr = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel titles = new StackPanel();
            _lblHomeTitle = ResFg(new TextBlock { FontSize = 28, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblHomeSub = ResFg(new TextBlock { FontSize = 13, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary");
            titles.Children.Add(_lblHomeTitle);
            titles.Children.Add(_lblHomeSub);
            hdr.Children.Add(titles);

            StackPanel actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            _btnHomeExport = CreateFlatButton("", "BgCard", "TextPrimary", "BorderCard", new Thickness(14, 7, 14, 7), 13);
            _btnHomeExport.Margin = new Thickness(0, 0, 8, 0);
            _btnHomeExport.Click += (s, e) => ExportHtmlReport();
            actions.Children.Add(_btnHomeExport);
            _btnRescan = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(16, 7, 16, 7), 13);
            _btnRescan.Click += (s, e) => RunScanAsync();
            actions.Children.Add(_btnRescan);
            Grid.SetColumn(actions, 1);
            hdr.Children.Add(actions);
            stack.Children.Add(hdr);

            // ---- Hero: score + verdict
            _homeHero = MakeCard(new Thickness(24, 22, 24, 22), new Thickness(0, 0, 0, 16));
            Grid hero = new Grid();
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hero.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Grid ringHost = new Grid { Width = 128, Height = 128, Margin = new Thickness(0, 0, 26, 0), VerticalAlignment = VerticalAlignment.Center };
            _scoreRing = new ScoreRing { StrokeWidth = 11 };
            _scoreRing.SetResourceReference(ScoreRing.TrackBrushProperty, "BgSubtle");
            _scoreRing.SetResourceReference(ScoreRing.FillBrushProperty, "Accent");
            ringHost.Children.Add(_scoreRing);
            StackPanel ringText = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _lblScoreValue = ResFg(new TextBlock { Text = "—", FontSize = 36, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, HorizontalAlignment = HorizontalAlignment.Center }, "TextPrimary");
            _lblScoreCaption = ResFg(new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, -2, 0, 0) }, "TextMuted");
            ringText.Children.Add(_lblScoreValue);
            ringText.Children.Add(_lblScoreCaption);
            ringHost.Children.Add(ringText);
            hero.Children.Add(ringHost);

            StackPanel verdict = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _homeLevelPill = MakePill("", "BgSubtle", "TextSecondary");
            _lblHomeLevel = (TextBlock)_homeLevelPill.Child;
            StackPanel pills = new StackPanel { Orientation = Orientation.Horizontal };
            pills.Children.Add(_homeLevelPill);
            _homeWinPill = MakePill("", "BgSubtle", "TextSecondary");
            _homeWinPill.Margin = new Thickness(6, 0, 0, 0);
            _homeWinPill.Visibility = Visibility.Collapsed;
            pills.Children.Add(_homeWinPill);
            _homeDeltaPill = MakePill("", "BgSubtle", "TextSecondary");
            _homeDeltaPill.Margin = new Thickness(6, 0, 0, 0);
            _homeDeltaPill.Visibility = Visibility.Collapsed;
            pills.Children.Add(_homeDeltaPill);
            verdict.Children.Add(pills);
            _heroTitle = ResFg(new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 9, 0, 5) }, "TextPrimary");
            _heroBody = ResFg(new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxHeight = 58, TextTrimming = TextTrimming.WordEllipsis, LineHeight = 19 }, "TextSecondary");
            verdict.Children.Add(_heroTitle);
            verdict.Children.Add(_heroBody);
            StackPanel heroBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            _btnHeroPrimary = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(16, 7, 16, 7), 13);
            _btnHeroPrimary.Margin = new Thickness(0, 0, 8, 0);
            _btnHeroPrimary.Click += OnHeroPrimaryClick;
            _btnHeroSecondary = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnHeroSecondary.Click += OnHeroSecondaryClick;
            heroBtns.Children.Add(_btnHeroPrimary);
            heroBtns.Children.Add(_btnHeroSecondary);
            verdict.Children.Add(heroBtns);
            Grid.SetColumn(verdict, 1);
            hero.Children.Add(verdict);
            _homeHero.Child = hero;
            stack.Children.Add(_homeHero);

            // ---- Timeline
            Border tlCard = MakeCard(new Thickness(20, 16, 20, 14), new Thickness(0, 0, 0, 16));
            StackPanel tl = new StackPanel();
            Grid tlHdr = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            tlHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            tlHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblTimelineTitle = MakeSectionTitle();
            tlHdr.Children.Add(_lblTimelineTitle);
            _timelineLegend = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(_timelineLegend, 1);
            tlHdr.Children.Add(_timelineLegend);
            tl.Children.Add(tlHdr);
            _stabilityChart = new StabilityChart { Height = 120 };
            _stabilityChart.SetResourceReference(StabilityChart.LineBrushProperty, "AccentText");
            _stabilityChart.SetResourceReference(StabilityChart.GridBrushProperty, "BorderCard");
            _stabilityChart.SetResourceReference(StabilityChart.LabelBrushProperty, "TextMuted");
            _stabilityChart.SetResourceReference(StabilityChart.SevereBrushProperty, "DangerFg");
            _stabilityChart.SetResourceReference(StabilityChart.WarnBrushProperty, "WarnFg");
            _stabilityChart.SetResourceReference(StabilityChart.ReliabilityBrushProperty, "TextMuted");
            tl.Children.Add(_stabilityChart);
            tlCard.Child = tl;
            stack.Children.Add(tlCard);

            // ---- Lower: actions + recent crashes
            Grid lower = new Grid();
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            lower.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border actCard = MakeCard(new Thickness(18, 14, 18, 10), new Thickness(0));
            StackPanel actStack = new StackPanel();
            Grid actHdr = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            actHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            actHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblHomeActionsTitle = MakeSectionTitle();
            actHdr.Children.Add(_lblHomeActionsTitle);
            _btnHomeAllFixes = MakeLinkButton(() => SwitchTab(TabFixes));
            Grid.SetColumn(_btnHomeAllFixes, 1);
            actHdr.Children.Add(_btnHomeAllFixes);
            actStack.Children.Add(actHdr);
            _homeActionsList = new StackPanel();
            actStack.Children.Add(_homeActionsList);
            actCard.Child = actStack;
            lower.Children.Add(actCard);

            Border recCard = MakeCard(new Thickness(18, 14, 18, 10), new Thickness(0));
            StackPanel recStack = new StackPanel();
            Grid recHdr = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            recHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            recHdr.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblHomeRecentTitle = MakeSectionTitle();
            recHdr.Children.Add(_lblHomeRecentTitle);
            _btnHomeAllCrashes = MakeLinkButton(() => SwitchTab(TabCrashes));
            Grid.SetColumn(_btnHomeAllCrashes, 1);
            recHdr.Children.Add(_btnHomeAllCrashes);
            recStack.Children.Add(recHdr);
            _homeRecentList = new StackPanel();
            recStack.Children.Add(_homeRecentList);
            recCard.Child = recStack;
            Grid.SetColumn(recCard, 2);
            lower.Children.Add(recCard);
            stack.Children.Add(lower);
        }

        private void RefreshHomeLabels()
        {
            if (_lblHomeTitle == null) return;
            int h = DateTime.Now.Hour;
            _lblHomeTitle.Text = h >= 18 || h < 5
                ? Localizer.L("Bonsoir", "Good evening", "Buenas noches", "Guten Abend", "Buonasera", "Boa noite")
                : Localizer.L("Bonjour", "Hello", "Hola", "Hallo", "Buongiorno", "Olá");
            _btnHomeExport.Content = Localizer.L("📄 Rapport", "📄 Report", "📄 Informe", "📄 Bericht", "📄 Rapporto", "📄 Relatório");
            _btnRescan.Content = Localizer.L("⟳ Analyser", "⟳ Scan", "⟳ Analizar", "⟳ Analysieren", "⟳ Analizza", "⟳ Analisar");
            _lblScoreCaption.Text = Localizer.L("/ 100 stabilité", "/ 100 stability", "/ 100 estabilidad", "/ 100 Stabilität", "/ 100 stabilità", "/ 100 estabilidade");
            _lblTimelineTitle.Text = Localizer.L("Stabilité sur 30 jours", "Stability over 30 days", "Estabilidad en 30 días", "Stabilität über 30 Tage", "Stabilità su 30 giorni", "Estabilidade em 30 dias");
            _lblHomeActionsTitle.Text = Localizer.L("Actions recommandées", "Recommended actions", "Acciones recomendadas", "Empfohlene Aktionen", "Azioni consigliate", "Ações recomendadas");
            _btnHomeAllFixes.Content = Localizer.L("Tout voir", "See all", "Ver todo", "Alle", "Vedi tutto", "Ver tudo");
            _lblHomeRecentTitle.Text = Localizer.L("Crashs récents", "Recent crashes", "Fallos recientes", "Letzte Abstürze", "Crash recenti", "Falhas recentes");
            _btnHomeAllCrashes.Content = Localizer.L("Historique", "History", "Historial", "Verlauf", "Cronologia", "Histórico");

            _timelineLegend.Children.Clear();
            _timelineLegend.Children.Add(LegendItem("DangerFg", Localizer.L("Écran bleu / reboot", "BSOD / reboot", "Pantalla azul", "Bluescreen", "Schermata blu", "Tela azul")));
            _timelineLegend.Children.Add(LegendItem("WarnFg", Localizer.L("Autre crash", "Other crash", "Otro fallo", "Anderer Absturz", "Altro crash", "Outra falha")));
            _timelineLegend.Children.Add(LegendItem("TextMuted", Localizer.L("Indice Windows", "Windows index", "Índice Windows", "Windows-Index", "Indice Windows", "Índice Windows")));
        }

        private UIElement LegendItem(string brushKey, string text)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0) };
            sp.Children.Add(ResBg(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 1, 6, 0), VerticalAlignment = VerticalAlignment.Center }, brushKey));
            sp.Children.Add(ResFg(new TextBlock { Text = text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center }, "TextSecondary"));
            return sp;
        }

        private int _homeScore;
        private List<RegistryFixItem> _homeRecommended = new List<RegistryFixItem>();
        private CrashEntry _homeFocusCrash;

        private void RenderHome(ScanResult res)
        {
            if (_pageHome == null || res == null) return;
            RefreshHomeLabels();

            DateTime now = DateTime.Now;
            _lblHomeSub.Text = Localizer.L(
                string.Format("Dernière analyse à {0:HH:mm} · 30 derniers jours · {1}", now, res.Hardware.GetProfileBadge()),
                string.Format("Last scan {0:HH:mm} · last 30 days · {1}", now, res.Hardware.GetProfileBadge()),
                string.Format("Último análisis {0:HH:mm} · 30 días · {1}", now, res.Hardware.GetProfileBadge()),
                string.Format("Letzte Prüfung {0:HH:mm} · 30 Tage · {1}", now, res.Hardware.GetProfileBadge()),
                string.Format("Ultima analisi {0:HH:mm} · 30 giorni · {1}", now, res.Hardware.GetProfileBadge()),
                string.Format("Última análise {0:HH:mm} · 30 dias · {1}", now, res.Hardware.GetProfileBadge()));

            var window = (res.Events ?? new List<CrashEventItem>()).Where(e => (now - e.Timestamp).TotalDays <= Stability.WindowDays).ToList();
            int ownScore = Stability.Score(window, now);
            double? winIndex = Reliability.Latest(res.Reliability);
            // Blend with Windows' own reliability index when available (60 % crash analysis, 40 % Windows).
            _homeScore = Stability.Blend(ownScore, winIndex);
            if (winIndex.HasValue)
            {
                ((TextBlock)_homeWinPill.Child).Text = Localizer.L("Indice Windows ", "Windows index ", "Índice Windows ", "Windows-Index ", "Indice Windows ", "Índice Windows ") + winIndex.Value.ToString("0.0") + "/10";
                _homeWinPill.ToolTip = Localizer.L(
                    "Indice de fiabilité calculé par Windows (Moniteur de fiabilité). Le score combine 60 % d'analyse des crashs et 40 % de cet indice.",
                    "Reliability index computed by Windows (Reliability Monitor). The score blends 60 % crash analysis and 40 % this index.",
                    "Índice de fiabilidad de Windows. La puntuación combina 60 % de análisis de fallos y 40 % de este índice.",
                    "Zuverlässigkeitsindex von Windows. Der Wert kombiniert 60 % Absturzanalyse und 40 % dieses Index.",
                    "Indice di affidabilità di Windows. Il punteggio combina 60 % di analisi dei crash e 40 % di questo indice.",
                    "Índice de confiabilidade do Windows. A pontuação combina 60 % de análise de falhas e 40 % deste índice.");
                _homeWinPill.Visibility = Visibility.Visible;
            }
            else _homeWinPill.Visibility = Visibility.Collapsed;
            StabilityLevel level = Stability.LevelOf(_homeScore);
            _lblScoreValue.Text = _homeScore.ToString();
            var prev = History.PreviousDay(now);
            if (prev != null && prev.Score != _homeScore)
            {
                int d = _homeScore - prev.Score;
                ((TextBlock)_homeDeltaPill.Child).Text = (d > 0 ? "↑ +" : "↓ ") + d + Localizer.L(" depuis le ", " since ", " desde el ", " seit ", " dal ", " desde ") + prev.Time.ToString("d MMM", System.Globalization.CultureInfo.CurrentUICulture);
                _homeDeltaPill.SetResourceReference(Border.BackgroundProperty, d > 0 ? "SuccessSoftBg" : "DangerSoftBg");
                ((TextBlock)_homeDeltaPill.Child).SetResourceReference(TextBlock.ForegroundProperty, d > 0 ? "SuccessFg" : "DangerFg");
                _homeDeltaPill.Visibility = Visibility.Visible;
            }
            else _homeDeltaPill.Visibility = Visibility.Collapsed;
            _scoreRing.Value = _homeScore;

            string fg, soft, label;
            switch (level)
            {
                case StabilityLevel.Stable:
                    fg = "SuccessFg"; soft = "SuccessSoftBg";
                    label = Localizer.L("● Stable", "● Stable", "● Estable", "● Stabil", "● Stabile", "● Estável");
                    break;
                case StabilityLevel.Fragile:
                    fg = "WarnFg"; soft = "WarnSoftBg";
                    label = Localizer.L("● Fragile", "● Fragile", "● Frágil", "● Anfällig", "● Fragile", "● Frágil");
                    break;
                default:
                    fg = "DangerFg"; soft = "DangerSoftBg";
                    label = Localizer.L("● Instable", "● Unstable", "● Inestable", "● Instabil", "● Instabile", "● Instável");
                    break;
            }
            _scoreRing.SetResourceReference(ScoreRing.FillBrushProperty, fg);
            _homeLevelPill.SetResourceReference(Border.BackgroundProperty, soft);
            _lblHomeLevel.SetResourceReference(TextBlock.ForegroundProperty, fg);
            _lblHomeLevel.Text = label;

            _heroTitle.Text = res.DiagnosisTitle;
            _heroBody.Text = res.DiagnosisBody;
            _heroBody.ToolTip = res.DiagnosisBody;

            // Primary action: apply what is recommended, otherwise validate stability.
            _homeRecommended = (res.Fixes ?? new List<RegistryFixItem>())
                .Where(f => f.Status == FixStatus.Recommended && (!_onlyHardwareRelevantFixes || f.IsApplicableTo(res.Hardware)))
                .ToList();
            int n = _homeRecommended.Count;
            _btnHeroPrimary.Content = n > 0
                ? Localizer.L(
                    n == 1 ? "⚡ Appliquer le correctif recommandé" : string.Format("⚡ Appliquer les {0} correctifs", n),
                    n == 1 ? "⚡ Apply the recommended fix" : string.Format("⚡ Apply {0} fixes", n),
                    string.Format("⚡ Aplicar {0} correcciones", n),
                    string.Format("⚡ {0} Korrekturen anwenden", n),
                    string.Format("⚡ Applica {0} correzioni", n),
                    string.Format("⚡ Aplicar {0} correções", n))
                : Localizer.L("⚡ Tester la stabilité", "⚡ Run a stress test", "⚡ Probar estabilidad", "⚡ Stabilität testen", "⚡ Testa stabilità", "⚡ Testar estabilidade");

            _homeFocusCrash = _crashEntries.FirstOrDefault(c => (now - c.Time).TotalDays <= Stability.WindowDays && Stability.IsSevere(c.Category))
                              ?? _crashEntries.FirstOrDefault(c => (now - c.Time).TotalDays <= Stability.WindowDays);
            _btnHeroSecondary.Content = _homeFocusCrash != null
                ? Localizer.L("Voir le crash", "View crash", "Ver fallo", "Absturz ansehen", "Vedi crash", "Ver falha")
                : Localizer.L("🧠 Bilan IA", "🧠 AI check-up", "🧠 Análisis IA", "🧠 KI-Check", "🧠 Analisi IA", "🧠 Análise IA");

            _stabilityChart.SetDays(Stability.Timeline(window, now), res.Reliability);
            RenderHomeActions();
            RenderHomeRecent(now);
        }

        private void RenderHomeActions()
        {
            _homeActionsList.Children.Clear();
            var top = _homeRecommended.Take(3).ToList();
            if (top.Count == 0)
            {
                _homeActionsList.Children.Add(MakeEmptyLine(Glyph.Completed, "SuccessFg",
                    Localizer.L("Toutes les protections recommandées sont actives.", "All recommended protections are active.", "Todas las protecciones recomendadas están activas.", "Alle empfohlenen Schutzmaßnahmen sind aktiv.", "Tutte le protezioni consigliate sono attive.", "Todas as proteções recomendadas estão ativas.")));
                return;
            }
            for (int i = 0; i < top.Count; i++)
            {
                var fix = top[i];
                string fixId = fix.Id;
                Grid row = new Grid { Margin = new Thickness(0, i == 0 ? 2 : 0, 0, 0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                row.Children.Add(MakeIconTile(GroupGlyph(fix.Group), "AccentSoftBg", "AccentText"));
                StackPanel t = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(ResFg(new TextBlock { Text = fix.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary"));
                t.Children.Add(ResFg(new TextBlock { Text = fix.VendorTag + (fix.CanRestore ? Localizer.L(" · réversible", " · reversible", " · reversible", " · umkehrbar", " · reversibile", " · reversível") : ""), FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) }, "TextMuted"));
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                Button apply = CreateFlatButton(Localizer.L("Appliquer", "Apply", "Aplicar", "Anwenden", "Applica", "Aplicar"), "BgCard", "AccentText", "BorderCard", new Thickness(12, 5, 12, 5), 12);
                apply.VerticalAlignment = VerticalAlignment.Center;
                apply.Click += (s, e) => ExecuteSingleFix(fixId + "_apply", fixId);
                Grid.SetColumn(apply, 2);
                row.Children.Add(apply);

                Border wrap = ResBorder(new Border { BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Padding = new Thickness(0, 10, 0, 10), Child = row }, "BorderCard");
                _homeActionsList.Children.Add(wrap);
            }
        }

        private void RenderHomeRecent(DateTime now)
        {
            _homeRecentList.Children.Clear();
            var recent = _crashEntries.Take(4).ToList();
            if (recent.Count == 0)
            {
                _homeRecentList.Children.Add(MakeEmptyLine(Glyph.Completed, "SuccessFg",
                    Localizer.L("Aucun crash enregistré. Votre PC tient bon.", "No crash recorded. Your PC is holding up.", "Ningún fallo registrado.", "Keine Abstürze aufgezeichnet.", "Nessun crash registrato.", "Nenhuma falha registrada.")));
                return;
            }
            for (int i = 0; i < recent.Count; i++)
            {
                CrashEntry c = recent[i];
                string bg, fg;
                CategoryColors(c.Category, out bg, out fg);
                Grid row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.Children.Add(MakeIconTile(CategoryGlyph(c.Category), bg, fg));
                StackPanel t = new StackPanel { Margin = new Thickness(12, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                t.Children.Add(ResFg(new TextBlock { Text = c.Title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary"));
                t.Children.Add(ResFg(new TextBlock { Text = c.TechLine, FontSize = 11, FontFamily = Ds.MonoFont, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0) }, "TextMuted"));
                Grid.SetColumn(t, 1);
                row.Children.Add(t);
                TextBlock when = ResFg(new TextBlock { Text = RelativeTime(c.Time, now), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }, "TextMuted");
                Grid.SetColumn(when, 2);
                row.Children.Add(when);

                Border wrap = ResBorder(new Border
                {
                    BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0),
                    Padding = new Thickness(0, 10, 0, 10),
                    Background = Brushes.Transparent,
                    Cursor = Cursors.Hand,
                    Child = row
                }, "BorderCard");
                string key = c.Key;
                wrap.MouseLeftButtonUp += (s, e) => OpenCrash(key);
                _homeRecentList.Children.Add(wrap);
            }
        }

        private UIElement MakeEmptyLine(string glyph, string fgKey, string text)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 12) };
            TextBlock ic = ResFg(Ds.Icon(glyph, 16), fgKey);
            ic.Margin = new Thickness(0, 0, 10, 0);
            sp.Children.Add(ic);
            sp.Children.Add(ResFg(new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center }, "TextSecondary"));
            return sp;
        }

        private void OnHeroPrimaryClick(object sender, RoutedEventArgs e)
        {
            if (_homeRecommended.Count > 0)
                OnApplyAllRecommendedClick(sender, e);
            else
            {
                SwitchTab(TabHardware);
                SwitchHealthSubTab(2);
            }
        }

        private void OnHeroSecondaryClick(object sender, RoutedEventArgs e)
        {
            if (_homeFocusCrash != null)
                OpenCrash(_homeFocusCrash.Key);
            else
                OpenSystemAiReview();
        }

        // ---- Shared category styling
        private static void CategoryColors(CrashCategory c, out string bgKey, out string fgKey)
        {
            switch (c)
            {
                case CrashCategory.BSOD:
                case CrashCategory.UnexpectedReboot:
                case CrashCategory.HardwareWhea:
                    bgKey = "DangerSoftBg"; fgKey = "DangerFg"; return;
                case CrashCategory.GpuDriver:
                case CrashCategory.StorageNvme:
                case CrashCategory.MemoryExhaustion:
                case CrashCategory.BrowserGpu:
                    bgKey = "WarnSoftBg"; fgKey = "WarnFg"; return;
                default:
                    bgKey = "AccentSoftBg"; fgKey = "AccentText"; return;
            }
        }

        private static string CategoryGlyph(CrashCategory c)
        {
            switch (c)
            {
                case CrashCategory.BSOD: return Glyph.Warning;
                case CrashCategory.UnexpectedReboot: return Glyph.Refresh;
                case CrashCategory.HardwareWhea: return Glyph.Component;
                case CrashCategory.GpuDriver: return Glyph.Video;
                case CrashCategory.StorageNvme: return Glyph.Save;
                case CrashCategory.MemoryExhaustion: return Glyph.Diagnostic;
                case CrashCategory.BrowserGpu: return Glyph.Globe;
                default: return Glyph.ErrorBadge;
            }
        }

        private static string GroupGlyph(FixGroup g)
        {
            switch (g)
            {
                case FixGroup.Gpu: return Glyph.Video;
                case FixGroup.BrowserApps: return Glyph.Globe;
                case FixGroup.StoragePcie: return Glyph.Save;
                case FixGroup.SystemMaint: return Glyph.Repair;
                default: return Glyph.Lightning;
            }
        }

        private static string RelativeTime(DateTime t, DateTime now)
        {
            var d = now - t;
            if (d.TotalMinutes < 60) return Localizer.L("il y a " + Math.Max(1, (int)d.TotalMinutes) + " min", Math.Max(1, (int)d.TotalMinutes) + " min ago", "hace " + Math.Max(1, (int)d.TotalMinutes) + " min", "vor " + Math.Max(1, (int)d.TotalMinutes) + " Min.", Math.Max(1, (int)d.TotalMinutes) + " min fa", "há " + Math.Max(1, (int)d.TotalMinutes) + " min");
            if (t.Date == now.Date) return t.ToString("HH:mm");
            if (t.Date == now.Date.AddDays(-1)) return Localizer.L("hier ", "yesterday ", "ayer ", "gestern ", "ieri ", "ontem ") + t.ToString("HH:mm");
            return t.ToString("d MMM HH:mm", System.Globalization.CultureInfo.CurrentUICulture);
        }
    }
}
