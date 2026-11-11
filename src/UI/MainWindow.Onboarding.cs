using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WinCrashDoctor
{
    /// <summary>
    /// First-run welcome (3 screens) + one contextual tip the first time each section is opened.
    /// Replaces the former 23-step spotlight tour.
    /// </summary>
    public partial class MainWindow : Window
    {
        private Grid _rootGrid;
        private Grid _onboardingOverlay;
        private Button _btnTourGuide;
        private bool _initialTourTriggered = false;
        private AppSettings _appSettings;

        private bool _onboardingActive;
        private int _onboardingStepIndex;
        private StackPanel _onbDots;
        private TextBlock _lblOnbKicker;
        private TextBlock _lblOnbTitle;
        private TextBlock _lblOnbBody;
        private Button _btnOnbSkip;
        private Button _btnOnbBack;
        private Button _btnOnbNext;
        private Border _onbIllustration;

        private const int OnboardingSteps = 3;

        private void BuildOnboardingOverlay()
        {
            _onboardingOverlay = new Grid { Visibility = Visibility.Collapsed };
            Border scrim = new Border();
            scrim.SetResourceReference(Border.BackgroundProperty, "ScrimBg");
            _onboardingOverlay.Children.Add(scrim);

            Border card = ResBorder(ResBg(new Border
            {
                Width = 520,
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(32, 30, 32, 24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Effect = new DropShadowEffect { BlurRadius = 48, ShadowDepth = 14, Direction = 270, Opacity = 0.4, Color = Colors.Black }
            }, "BgCard"), "BorderStrong");

            StackPanel sp = new StackPanel();
            _onbIllustration = new Border { Height = 88, Margin = new Thickness(0, 0, 0, 22) };
            sp.Children.Add(_onbIllustration);

            _lblOnbKicker = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold }, "AccentText");
            _lblOnbTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 10) }, "TextPrimary");
            _lblOnbBody = ResFg(new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21, MinHeight = 84 }, "TextSecondary");
            sp.Children.Add(_lblOnbKicker);
            sp.Children.Add(_lblOnbTitle);
            sp.Children.Add(_lblOnbBody);

            Grid foot = new Grid { Margin = new Thickness(0, 26, 0, 0) };
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _onbDots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foot.Children.Add(_onbDots);

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal };
            _btnOnbSkip = CreateFlatButton("", "BgCard", "TextSecondary", "BgCard", new Thickness(12, 7, 12, 7), 13);
            _btnOnbSkip.BorderThickness = new Thickness(0);
            _btnOnbSkip.Click += (s, e) => EndOnboarding();
            _btnOnbBack = CreateFlatButton("", "BgCard", "TextPrimary", "BorderStrong", new Thickness(14, 7, 14, 7), 13);
            _btnOnbBack.Margin = new Thickness(6, 0, 0, 0);
            _btnOnbBack.Click += (s, e) => PrevOnboardingStep();
            _btnOnbNext = CreatePrimaryButton("", "Accent", "OnAccent", new Thickness(18, 7, 18, 7), 13);
            _btnOnbNext.Margin = new Thickness(8, 0, 0, 0);
            _btnOnbNext.Click += (s, e) => NextOnboardingStep();
            btns.Children.Add(_btnOnbSkip);
            btns.Children.Add(_btnOnbBack);
            btns.Children.Add(_btnOnbNext);
            Grid.SetColumn(btns, 2);
            foot.Children.Add(btns);
            sp.Children.Add(foot);

            card.Child = sp;
            _onboardingOverlay.Children.Add(card);

            _onboardingOverlay.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape) { EndOnboarding(); e.Handled = true; }
                else if (e.Key == Key.Right || e.Key == Key.Enter) { NextOnboardingStep(); e.Handled = true; }
                else if (e.Key == Key.Left) { PrevOnboardingStep(); e.Handled = true; }
            };
        }

        public void StartOnboarding()
        {
            _onboardingActive = true;
            _onboardingStepIndex = 0;
            _onboardingOverlay.Visibility = Visibility.Visible;
            ShowCurrentOnboardingStep();
            _btnOnbNext.Focus();
        }

        public void EndOnboarding()
        {
            _onboardingActive = false;
            _onboardingOverlay.Visibility = Visibility.Collapsed;
            if (_appSettings != null)
            {
                _appSettings.HasCompletedTour = true;
                Localizer.SaveFullSettings(_appSettings);
            }
            if (_pendingUpdate != null) { var u = _pendingUpdate; _pendingUpdate = null; ShowUpdate(u); }
        }

        public void NextOnboardingStep()
        {
            if (!_onboardingActive) return;
            if (_onboardingStepIndex >= OnboardingSteps - 1) { EndOnboarding(); return; }
            _onboardingStepIndex++;
            ShowCurrentOnboardingStep();
        }

        public void PrevOnboardingStep()
        {
            if (!_onboardingActive || _onboardingStepIndex == 0) return;
            _onboardingStepIndex--;
            ShowCurrentOnboardingStep();
        }

        /// <summary>Replays the welcome and re-arms the per-section tips (Settings › Help).</summary>
        private void ReplayIntroduction()
        {
            if (_appSettings != null)
            {
                _appSettings.SeenTips = "";
                Localizer.SaveFullSettings(_appSettings);
            }
            SwitchTab(TabHome);
            StartOnboarding();
        }

        private void ShowCurrentOnboardingStep()
        {
            int i = _onboardingStepIndex;
            switch (i)
            {
                case 0:
                    _lblOnbKicker.Text = Localizer.L("BIENVENUE", "WELCOME", "BIENVENIDO", "WILLKOMMEN", "BENVENUTO", "BEM-VINDO");
                    _lblOnbTitle.Text = Localizer.L("Comprendre pourquoi votre PC plante", "Understand why your PC crashes", "Entender por qué falla su PC", "Verstehen, warum Ihr PC abstürzt", "Capire perché il PC va in crash", "Entender por que seu PC trava");
                    _lblOnbBody.Text = Localizer.L(
                        "Quand Windows plante, il laisse des traces : des événements dans ses journaux et des fichiers .dmp. Dump Finder les lit pour vous et les traduit en langage clair.",
                        "When Windows crashes it leaves traces: events in its logs and .dmp files. Dump Finder reads them for you and puts them in plain words.",
                        "Cuando Windows falla deja rastros: eventos y archivos .dmp. Dump Finder los lee por usted y los explica con claridad.",
                        "Wenn Windows abstürzt, hinterlässt es Spuren: Ereignisse und .dmp-Dateien. Dump Finder liest sie und erklärt sie verständlich.",
                        "Quando Windows va in crash lascia tracce: eventi e file .dmp. Dump Finder li legge e li spiega in modo semplice.",
                        "Quando o Windows trava, deixa rastros: eventos e arquivos .dmp. O Dump Finder os lê e explica de forma simples.");
                    _onbIllustration.Child = IllustrationLogo();
                    break;
                case 1:
                    _lblOnbKicker.Text = Localizer.L("ÉTAPE 2 SUR 3", "STEP 2 OF 3", "PASO 2 DE 3", "SCHRITT 2 VON 3", "PASSO 2 DI 3", "PASSO 2 DE 3");
                    _lblOnbTitle.Text = Localizer.L("L'analyse est déjà lancée", "The scan is already running", "El análisis ya está en marcha", "Die Analyse läuft bereits", "L'analisi è già partita", "A análise já começou");
                    _lblOnbBody.Text = Localizer.L(
                        "Journaux des 30 derniers jours, fichiers .dmp, BIOS, mémoire, SSD : tout est lu en lecture seule. Rien n'est modifié sur votre PC sans votre accord, et chaque correctif s'annule en un clic.",
                        "30 days of logs, .dmp files, BIOS, memory, SSD: everything is read-only. Nothing changes on your PC without your approval, and every fix can be undone in one click.",
                        "30 días de registros, .dmp, BIOS, memoria, SSD: todo en solo lectura. Nada cambia sin su permiso y cada corrección se deshace en un clic.",
                        "30 Tage Protokolle, .dmp, BIOS, Speicher, SSD: alles nur gelesen. Nichts ändert sich ohne Ihre Zustimmung, jede Korrektur ist umkehrbar.",
                        "30 giorni di log, .dmp, BIOS, memoria, SSD: tutto in sola lettura. Nulla cambia senza il tuo consenso e ogni correzione si annulla in un clic.",
                        "30 dias de logs, .dmp, BIOS, memória, SSD: tudo somente leitura. Nada muda sem sua permissão e cada correção se desfaz em um clique.");
                    _onbIllustration.Child = IllustrationIcons(new[] { Glyph.Document, Glyph.Diagnostic, Glyph.Component, Glyph.Shield });
                    break;
                default:
                    _lblOnbKicker.Text = Localizer.L("ÉTAPE 3 SUR 3", "STEP 3 OF 3", "PASO 3 DE 3", "SCHRITT 3 VON 3", "PASSO 3 DI 3", "PASSO 3 DE 3");
                    _lblOnbTitle.Text = Localizer.L("Votre tableau de bord", "Your dashboard", "Su panel", "Ihr Dashboard", "La tua dashboard", "Seu painel");
                    _lblOnbBody.Text = Localizer.L(
                        "L'Accueil donne un score de stabilité sur 30 jours et les actions utiles. Chaque crash a sa fiche dans Crashs, avec l'explication, le fichier .dmp et les correctifs liés.",
                        "Home shows a 30-day stability score and the actions that help. Every crash has its own page in Crashes, with the explanation, the .dmp file and related fixes.",
                        "Inicio muestra una puntuación de estabilidad de 30 días y las acciones útiles. Cada fallo tiene su ficha en Fallos.",
                        "Start zeigt einen 30-Tage-Stabilitätswert und hilfreiche Aktionen. Jeder Absturz hat seine Seite unter Abstürze.",
                        "Home mostra un punteggio di stabilità a 30 giorni e le azioni utili. Ogni crash ha la sua scheda in Crash.",
                        "Início mostra uma pontuação de estabilidade de 30 dias e as ações úteis. Cada falha tem sua ficha em Falhas.");
                    _onbIllustration.Child = IllustrationScore();
                    break;
            }

            _btnOnbSkip.Content = Localizer.L("Passer", "Skip", "Omitir", "Überspringen", "Salta", "Pular");
            _btnOnbBack.Content = Localizer.L("Retour", "Back", "Atrás", "Zurück", "Indietro", "Voltar");
            _btnOnbBack.Visibility = i == 0 ? Visibility.Collapsed : Visibility.Visible;
            _btnOnbSkip.Visibility = i == OnboardingSteps - 1 ? Visibility.Collapsed : Visibility.Visible;
            _btnOnbNext.Content = i == OnboardingSteps - 1
                ? Localizer.L("Commencer", "Get started", "Empezar", "Loslegen", "Inizia", "Começar")
                : Localizer.L("Suivant", "Next", "Siguiente", "Weiter", "Avanti", "Próximo");

            _onbDots.Children.Clear();
            for (int d = 0; d < OnboardingSteps; d++)
            {
                Border dot = ResBg(new Border { Height = 6, Width = d == i ? 20 : 6, CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 6, 0) }, d == i ? "Accent" : "BorderStrong");
                _onbDots.Children.Add(dot);
            }
            AnimatePageIn(_onbIllustration);
        }

        private UIElement IllustrationLogo()
        {
            Image logo = new Image { Width = 80, Height = 80, HorizontalAlignment = HorizontalAlignment.Left };
            logo.SetResourceReference(Image.SourceProperty, "BrandLogo");
            return logo;
        }

        private UIElement IllustrationIcons(string[] glyphs)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var g in glyphs)
            {
                Border tile = MakeIconTile(g, "AccentSoftBg", "AccentText", 56);
                tile.CornerRadius = new CornerRadius(16);
                ((TextBlock)tile.Child).FontSize = 22;
                tile.Margin = new Thickness(0, 0, 12, 0);
                sp.Children.Add(tile);
            }
            return sp;
        }

        private UIElement IllustrationScore()
        {
            Grid g = new Grid { Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Left };
            ScoreRing ring = new ScoreRing { Value = 86, StrokeWidth = 8 };
            ring.SetResourceReference(ScoreRing.TrackBrushProperty, "BgSubtle");
            ring.SetResourceReference(ScoreRing.FillBrushProperty, "SuccessFg");
            g.Children.Add(ring);
            g.Children.Add(ResFg(new TextBlock { Text = "86", FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, "TextPrimary"));
            return g;
        }

        // ==================== CONTEXTUAL TIPS (once per section) ====================
        private void OnUserSwitchedTab(int tabIdx)
        {
            if (_onboardingActive || _appSettings == null || !_appSettings.HasCompletedTour) return;
            string id = "tab" + tabIdx;
            var seen = new HashSet<string>((_appSettings.SeenTips ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
            if (seen.Contains(id)) return;

            string tip = SectionTip(tabIdx);
            if (tip == null) return;
            seen.Add(id);
            _appSettings.SeenTips = string.Join(",", seen);
            Localizer.SaveFullSettings(_appSettings);
            ShowToast(tip, ToastKind.Info, true);
        }

        private static string SectionTip(int tabIdx)
        {
            switch (tabIdx)
            {
                case TabCrashes:
                    return Localizer.L(
                        "Astuce : cliquez sur un crash pour ouvrir sa fiche. « Copier pour forum » prépare un résumé prêt à poster.",
                        "Tip: click a crash to open its details. \"Copy for forum\" prepares a summary ready to post.",
                        "Consejo: haga clic en un fallo para ver su ficha.",
                        "Tipp: Klicken Sie auf einen Absturz, um die Details zu öffnen.",
                        "Suggerimento: clicca su un crash per aprire la scheda.",
                        "Dica: clique em uma falha para abrir a ficha.");
                case TabHardware:
                    return Localizer.L(
                        "Astuce : le sous-onglet « Test de stabilité » vérifie le GPU, la RAM et le SSD sous charge après un correctif.",
                        "Tip: the \"Stability test\" tab checks GPU, RAM and SSD under load after a fix.",
                        "Consejo: la pestaña «Prueba de estabilidad» comprueba GPU, RAM y SSD bajo carga.",
                        "Tipp: Der Reiter „Stabilitätstest“ prüft GPU, RAM und SSD unter Last.",
                        "Suggerimento: la scheda «Test di stabilità» verifica GPU, RAM e SSD sotto carico.",
                        "Dica: a aba «Teste de estabilidade» verifica GPU, RAM e SSD sob carga.");
                case TabFixes:
                    return Localizer.L(
                        "Astuce : le Registre est sauvegardé avant chaque correctif. « Annuler les modifications » revient à l'état d'origine.",
                        "Tip: the Registry is backed up before every fix. \"Rollback fixes\" returns to the original state.",
                        "Consejo: el Registro se guarda antes de cada corrección.",
                        "Tipp: Die Registry wird vor jeder Korrektur gesichert.",
                        "Suggerimento: il Registro viene salvato prima di ogni correzione.",
                        "Dica: o Registro é salvo antes de cada correção.");
                case TabTools:
                    return Localizer.L(
                        "Astuce : « Rapport & export » regroupe tout le diagnostic en un texte à joindre à un ticket de support.",
                        "Tip: \"Report & export\" gathers the whole diagnosis into one text to attach to a support ticket.",
                        "Consejo: «Informe y exportar» reúne todo el diagnóstico en un texto.",
                        "Tipp: „Bericht & Export“ fasst die Diagnose in einem Text zusammen.",
                        "Suggerimento: «Rapporto ed esporta» raccoglie tutta la diagnosi in un testo.",
                        "Dica: «Relatório e exportar» reúne todo o diagnóstico em um texto.");
                default:
                    return null;
            }
        }
    }
}
