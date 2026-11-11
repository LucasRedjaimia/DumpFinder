using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using WinCrashDoctor.Stress;

namespace WinCrashDoctor
{
    /// <summary>
    /// Hardware › Stability test: verified CPU / GPU / VRAM / RAM / SSD tests, run alone or as
    /// a Quick (~20 min) or Thorough (~3 h) plan, with a verdict and the matching fixes at the end.
    /// </summary>
    public partial class MainWindow
    {
        private readonly StressRunner _stressRunner = new StressRunner();
        private StressPlanKind _stressPlan = StressPlanKind.Quick;
        private readonly List<StressSample> _stressSeries = new List<StressSample>();
        private int _stressStageShown = -1;
        private int _stressLastLogSec = -100;
        private StressCapabilities _stressCaps;

        private TextBlock _lblStressTitle, _lblStressSub;
        private TextBlock _lblStressPlanCaption, _lblStressPlanHint, _lblStressCapsCaption, _lblStressCaps, _lblStressLast;
        private ComboBox _cbStressPlan;
        private StackPanel _stressDurationRow;
        private TextBlock _lblStressDuration, _valStressDuration;
        private Slider _slStressDuration;
        private CheckBox _chkStressCpuTemp;
        private TextBlock _lblStressCpuTempHint;
        private Button _btnStartStress, _btnStopStress;

        private TextBlock _lblStressPhase, _lblStressStatus, _lblStressTimer;
        private ProgressBar _pbStress;
        private WrapPanel _stressSteps;
        private readonly TextBlock[] _stTileTitles = new TextBlock[4];
        private readonly TextBlock[] _stTileVals = new TextBlock[4];
        private readonly TextBlock[] _stTileSubs = new TextBlock[4];
        private Border _stressChartCard, _stressResultsCard;
        private TextBlock _lblStressLegend;
        private Canvas _stressCanvas;
        private StackPanel _stressResultsPanel;
        private TextBox _txtStressLog;

        private static string SL(string fr, string en, string es, string de, string it, string pt) { return Localizer.L(fr, en, es, de, it, pt); }

        // ==================== BUILD ====================
        private void BuildStressPage()
        {
            _pageStress = new Grid { Visibility = Visibility.Collapsed };
            _pageStress.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _pageStress.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var hdr = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            _lblStressTitle = ResFg(new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, FontFamily = Ds.DisplayFont }, "TextPrimary");
            _lblStressSub = ResFg(new TextBlock { FontSize = 13, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary");
            hdr.Children.Add(_lblStressTitle);
            hdr.Children.Add(_lblStressSub);
            _pageStress.Children.Add(hdr);

            var body = new Grid();
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(315) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
            body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(body, 1);
            _pageStress.Children.Add(body);

            body.Children.Add(BuildStressConfigCard());
            var right = BuildStressLiveArea();
            Grid.SetColumn(right, 2);
            body.Children.Add(right);

            Loaded += (s, e) => OnStressPageLoaded();
        }

        private Border StressCard(Thickness margin, Thickness padding)
        {
            return ResBorder(ResBg(new Border
            {
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1),
                Padding = padding,
                Margin = margin
            }, "BgCard"), "BorderCard");
        }

        private UIElement BuildStressConfigCard()
        {
            Border card = StressCard(new Thickness(0), new Thickness(18, 16, 18, 16));
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var stack = new StackPanel();

            _lblStressPlanCaption = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }, "TextSecondary");
            stack.Children.Add(_lblStressPlanCaption);

            _cbStressPlan = new ComboBox { Margin = new Thickness(0, 0, 0, 8) };
            _cbStressPlan.SelectionChanged += (s, e) =>
            {
                if (_suppressDropdownEvents || _cbStressPlan.SelectedIndex < 0) return;
                _stressPlan = (StressPlanKind)_cbStressPlan.SelectedIndex;
                UpdateStressPlanHint();
            };
            stack.Children.Add(_cbStressPlan);

            _lblStressPlanHint = ResFg(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14), LineHeight = 17 }, "TextMuted");
            stack.Children.Add(_lblStressPlanHint);

            _stressDurationRow = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblStressDuration = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold }, "TextSecondary");
            _valStressDuration = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold }, "AccentText");
            Grid.SetColumn(_valStressDuration, 1);
            row.Children.Add(_lblStressDuration);
            row.Children.Add(_valStressDuration);
            _stressDurationRow.Children.Add(row);
            _slStressDuration = new Slider { Minimum = 1, Maximum = 120, Value = 10, TickFrequency = 1, IsSnapToTickEnabled = true, Margin = new Thickness(0, 6, 0, 0) };
            _slStressDuration.ValueChanged += (s, e) => { UpdateStressDurationLabel(); UpdateStressPlanHint(); };
            _stressDurationRow.Children.Add(_slStressDuration);
            stack.Children.Add(_stressDurationRow);

            stack.Children.Add(ResBg(new Border { Height = 1, Margin = new Thickness(0, 2, 0, 12) }, "BorderCard"));
            _lblStressCapsCaption = ResFg(new TextBlock { FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) }, "TextSecondary");
            stack.Children.Add(_lblStressCapsCaption);
            _lblStressCaps = ResFg(new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 18 }, "TextMuted");
            stack.Children.Add(_lblStressCaps);

            _chkStressCpuTemp = new CheckBox { IsChecked = true, Margin = new Thickness(0, 12, 0, 0), FontSize = 12.5 };
            _chkStressCpuTemp.SetResourceReference(Control.ForegroundProperty, "TextPrimary");
            stack.Children.Add(_chkStressCpuTemp);
            _lblStressCpuTempHint = ResFg(new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(22, 2, 0, 0), LineHeight = 16 }, "TextMuted");
            stack.Children.Add(_lblStressCpuTempHint);

            sv.Content = stack;
            layout.Children.Add(sv);

            var bottom = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            var btnRow = new Grid();
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            btnRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            _btnStartStress = CreateFlatButton("", "Accent", "OnAccent", "Accent", new Thickness(12, 9, 12, 9), 13);
            _btnStartStress.FontWeight = FontWeights.SemiBold;
            _btnStartStress.Click += async (s, e) => await StartStressRun();
            btnRow.Children.Add(_btnStartStress);
            _btnStopStress = CreateFlatButton("", "BgSubtle", "TextPrimary", "BorderCard", new Thickness(10, 9, 10, 9), 12.5);
            _btnStopStress.IsEnabled = false;
            _btnStopStress.Click += (s, e) =>
            {
                _stressRunner.RequestStop();
                _btnStopStress.IsEnabled = false;
                _lblStressStatus.Text = SL("Arrêt en cours…", "Stopping…", "Deteniendo…", "Wird gestoppt…", "Arresto in corso…", "Parando…");
            };
            Grid.SetColumn(_btnStopStress, 2);
            btnRow.Children.Add(_btnStopStress);
            bottom.Children.Add(btnRow);
            _lblStressLast = ResFg(new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) }, "TextMuted");
            bottom.Children.Add(_lblStressLast);
            Grid.SetRow(bottom, 1);
            layout.Children.Add(bottom);

            card.Child = layout;
            return card;
        }

        private Grid BuildStressLiveArea()
        {
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(86) });

            // Stage + progress
            Border phase = StressCard(new Thickness(0, 0, 0, 12), new Thickness(16, 12, 16, 12));
            var ps = new StackPanel();
            var top = new Grid();
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _lblStressPhase = ResFg(new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary");
            _lblStressTimer = ResFg(new TextBlock { FontSize = 13, FontFamily = Ds.MonoFont, Margin = new Thickness(12, 0, 0, 0) }, "TextSecondary");
            Grid.SetColumn(_lblStressTimer, 1);
            top.Children.Add(_lblStressPhase);
            top.Children.Add(_lblStressTimer);
            ps.Children.Add(top);
            _lblStressStatus = ResFg(new TextBlock { FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis }, "TextMuted");
            ps.Children.Add(_lblStressStatus);
            _pbStress = ResBg(new ProgressBar { Height = 6, Minimum = 0, Maximum = 100, BorderThickness = new Thickness(0), Margin = new Thickness(0, 8, 0, 0) }, "BgSubtle");
            _pbStress.SetResourceReference(ProgressBar.ForegroundProperty, "Accent");
            ps.Children.Add(_pbStress);
            _stressSteps = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            ps.Children.Add(_stressSteps);
            phase.Child = ps;
            right.Children.Add(phase);

            // Tiles
            var tiles = new UniformGrid { Columns = 4, Rows = 1, Margin = new Thickness(0, 0, 0, 12) };
            for (int i = 0; i < 4; i++)
            {
                Border t = StressCard(new Thickness(i == 0 ? 0 : 5, 0, i == 3 ? 0 : 5, 0), new Thickness(13, 10, 13, 10));
                var st = new StackPanel();
                _stTileTitles[i] = ResFg(new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis }, "TextSecondary");
                _stTileVals[i] = ResFg(new TextBlock { FontSize = 18, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 3, 0, 1), TextTrimming = TextTrimming.CharacterEllipsis }, "TextPrimary");
                _stTileSubs[i] = ResFg(new TextBlock { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis }, "TextMuted");
                st.Children.Add(_stTileTitles[i]);
                st.Children.Add(_stTileVals[i]);
                st.Children.Add(_stTileSubs[i]);
                t.Child = st;
                tiles.Children.Add(t);
            }
            Grid.SetRow(tiles, 1);
            right.Children.Add(tiles);

            // Chart (while running) / results (after)
            _stressChartCard = StressCard(new Thickness(0, 0, 0, 12), new Thickness(14, 10, 14, 12));
            var cg = new Grid();
            cg.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cg.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            _lblStressLegend = ResFg(new TextBlock { FontSize = 12, Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis }, "TextSecondary");
            cg.Children.Add(_lblStressLegend);
            _stressCanvas = ResBg(new Canvas { ClipToBounds = true }, "ChartBg");
            _stressCanvas.SizeChanged += (s, e) => RedrawStressChart();
            Grid.SetRow(_stressCanvas, 1);
            cg.Children.Add(_stressCanvas);
            _stressChartCard.Child = cg;
            Grid.SetRow(_stressChartCard, 2);
            right.Children.Add(_stressChartCard);

            _stressResultsCard = StressCard(new Thickness(0, 0, 0, 12), new Thickness(16, 12, 10, 12));
            _stressResultsCard.Visibility = Visibility.Collapsed;
            _stressResultsPanel = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
            _stressResultsCard.Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _stressResultsPanel };
            Grid.SetRow(_stressResultsCard, 2);
            right.Children.Add(_stressResultsCard);

            _txtStressLog = ResFg(ResBg(new TextBox
            {
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7),
                FontSize = 12,
                FontFamily = Ds.MonoFont,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            }, "BgCard"), "TextSecondary");
            _txtStressLog.SetResourceReference(TextBox.BorderBrushProperty, "BorderCard");
            Grid.SetRow(_txtStressLog, 3);
            right.Children.Add(_txtStressLog);
            return right;
        }

        // ==================== LABELS ====================
        private void RefreshStressPageLabels()
        {
            _lblStressTitle.Text = SL("Test de stabilité", "Stability test", "Prueba de estabilidad", "Stabilitätstest", "Test di stabilità", "Teste de estabilidade");
            _lblStressSub.Text = SL("Chaque test vérifie ses propres résultats : une erreur est détectée avant même un plantage.",
                                    "Every test checks its own results: an error is caught before it turns into a crash.",
                                    "Cada prueba verifica sus propios resultados: un error se detecta antes de un bloqueo.",
                                    "Jeder Test prüft seine eigenen Ergebnisse: Fehler werden erkannt, bevor es abstürzt.",
                                    "Ogni test verifica i propri risultati: un errore viene rilevato prima di un crash.",
                                    "Cada teste verifica os próprios resultados: um erro é detectado antes de um travamento.");
            _lblStressPlanCaption.Text = SL("Que tester ?", "What to test?", "¿Qué probar?", "Was testen?", "Cosa testare?", "O que testar?");
            _lblStressCapsCaption.Text = SL("Ce PC", "This PC", "Este PC", "Dieser PC", "Questo PC", "Este PC");
            _btnStartStress.Content = SL("Démarrer", "Start", "Iniciar", "Starten", "Avvia", "Iniciar");
            _btnStopStress.Content = SL("Arrêter", "Stop", "Detener", "Stopp", "Ferma", "Parar");

            bool old = _suppressDropdownEvents;
            _suppressDropdownEvents = true;
            int sel = (int)_stressPlan;
            _cbStressPlan.Items.Clear();
            foreach (StressPlanKind k in Enum.GetValues(typeof(StressPlanKind)))
            {
                string name = StressText.PlanName(k);
                if (StressPlanner.IsSuite(k))
                {
                    double sec = Math.Round(StressPlanner.Build(k, 10).TotalSec / 300.0) * 300; // rounded to 5 min
                    name += " (≈ " + FormatStressDuration(sec)
                          + (k == StressPlanKind.Thorough ? SL(", la nuit", ", overnight", ", de noche", ", über Nacht", ", di notte", ", à noite") : "") + ")";
                }
                _cbStressPlan.Items.Add(name);
            }
            _cbStressPlan.SelectedIndex = sel;
            _suppressDropdownEvents = old;

            UpdateStressPlanHint();
            RefreshStressCaps();
            RefreshStressLast();
            if (!_stressRunner.IsRunning && _stressStageShown < 0)
            {
                _lblStressPhase.Text = StressText.Ready;
                _lblStressStatus.Text = "";
                ResetStressTiles();
            }
        }

        private void UpdateStressDurationLabel()
        {
            int v = (int)_slStressDuration.Value;
            _valStressDuration.Text = v + " min";
        }

        private void UpdateStressPlanHint()
        {
            if (_lblStressPlanHint == null) return;
            bool suite = StressPlanner.IsSuite(_stressPlan);
            _stressDurationRow.Visibility = suite ? Visibility.Collapsed : Visibility.Visible;
            if (_stressPlan == StressPlanKind.CpuPerCore)
            {
                _lblStressDuration.Text = SL("Durée par cœur", "Time per core", "Duración por núcleo", "Dauer pro Kern", "Durata per core", "Duração por núcleo");
                _slStressDuration.Maximum = 30;
            }
            else
            {
                _lblStressDuration.Text = SL("Durée", "Duration", "Duración", "Dauer", "Durata", "Duração");
                _slStressDuration.Maximum = 120;
            }
            UpdateStressDurationLabel();

            StressPlan plan = StressPlanner.Build(_stressPlan, (int)_slStressDuration.Value);
            string dur = FormatStressDuration(plan.TotalSec);
            string gb = StressText.Gb(plan.DiskBytes);
            string drive = StressPlanner.TestDrive;
            string t;
            switch (_stressPlan)
            {
                case StressPlanKind.Quick:
                    t = SL("Enchaîne 7 tests vérifiés en {0} : processeur (tous les cœurs puis cœur par cœur), RAM, carte graphique, mémoire vidéo, pics de charge et SSD ({1} écrits puis relus). Un bon premier diagnostic.",
                           "Runs 7 verified tests in {0}: CPU (all cores, then core by core), RAM, graphics card, video memory, load spikes and SSD ({1} written then read back). A good first check.",
                           "Encadena 7 pruebas verificadas en {0}: CPU (todos los núcleos y uno a uno), RAM, gráfica, memoria de vídeo, picos de carga y SSD ({1} escritos y releídos). Un buen primer diagnóstico.",
                           "Führt 7 geprüfte Tests in {0} aus: CPU (alle Kerne, dann einzeln), RAM, Grafikkarte, Grafikspeicher, Lastspitzen und SSD ({1} geschrieben und zurückgelesen). Eine gute erste Prüfung.",
                           "Esegue 7 test verificati in {0}: CPU (tutti i core, poi uno per uno), RAM, scheda video, memoria video, picchi di carico e SSD ({1} scritti e riletti). Un buon primo controllo.",
                           "Executa 7 testes verificados em {0}: CPU (todos os núcleos, depois um a um), RAM, placa de vídeo, memória de vídeo, picos de carga e SSD ({1} gravados e relidos). Um bom primeiro diagnóstico.");
                    break;
                case StressPlanKind.Thorough:
                    t = SL("Les mêmes 7 tests en version longue ({0}) : à lancer le soir. La RAM est couverte de nombreuses fois, chaque cœur plusieurs minutes, et le SSD au-delà de son cache ({1}). Pour valider un overclock ou un undervolt.",
                           "The same 7 tests, long version ({0}): start it in the evening. RAM is covered many times, each core for several minutes, and the SSD past its cache ({1}). To validate an overclock or undervolt.",
                           "Las mismas 7 pruebas en versión larga ({0}): lánzala por la noche. La RAM se cubre muchas veces, cada núcleo varios minutos y el SSD más allá de su caché ({1}). Para validar un overclock o undervolt.",
                           "Dieselben 7 Tests in Langfassung ({0}): abends starten. RAM wird vielfach abgedeckt, jeder Kern mehrere Minuten, die SSD über ihren Cache hinaus ({1}). Zum Absichern von Übertaktung oder Undervolting.",
                           "Gli stessi 7 test in versione lunga ({0}): da avviare la sera. La RAM viene coperta molte volte, ogni core per diversi minuti e l'SSD oltre la sua cache ({1}). Per validare overclock o undervolt.",
                           "Os mesmos 7 testes na versão longa ({0}): inicie à noite. A RAM é coberta muitas vezes, cada núcleo por vários minutos e o SSD além do cache ({1}). Para validar overclock ou undervolt.");
                    break;
                case StressPlanKind.CpuAllCore:
                    t = SL("Tous les threads calculent en boucle des résultats connus d'avance (entier + " + CpuWork.SimdLabel + ") : la moindre erreur de calcul est repérée, avant même un plantage.",
                           "Every thread loops over computations whose results are known in advance (integer + " + CpuWork.SimdLabel + "): any miscalculation is caught, even before a crash.",
                           "Todos los hilos calculan en bucle resultados conocidos de antemano (entero + " + CpuWork.SimdLabel + "): cualquier error se detecta, incluso antes de un bloqueo.",
                           "Alle Threads berechnen fortlaufend vorab bekannte Ergebnisse (Ganzzahl + " + CpuWork.SimdLabel + "): Jeder Rechenfehler wird erkannt, noch vor einem Absturz.",
                           "Tutti i thread calcolano in ciclo risultati noti in anticipo (interi + " + CpuWork.SimdLabel + "): ogni errore viene rilevato, anche prima di un crash.",
                           "Todas as threads calculam em loop resultados conhecidos (inteiro + " + CpuWork.SimdLabel + "): qualquer erro é detectado, antes mesmo de um travamento.");
                    break;
                case StressPlanKind.CpuPerCore:
                    t = SL("Teste chaque cœur seul, à sa fréquence maximale (méthode CoreCycler) : idéal pour régler un Curve Optimizer ou un undervolt par cœur. Durée totale : {0}.",
                           "Tests each core on its own at its highest boost (CoreCycler method): ideal to tune Curve Optimizer or a per-core undervolt. Total time: {0}.",
                           "Prueba cada núcleo solo a su frecuencia máxima (método CoreCycler): ideal para ajustar Curve Optimizer o un undervolt por núcleo. Duración total: {0}.",
                           "Testet jeden Kern einzeln bei maximalem Boost (CoreCycler-Methode): ideal für Curve Optimizer oder Undervolting pro Kern. Gesamtdauer: {0}.",
                           "Testa ogni core da solo alla massima frequenza (metodo CoreCycler): ideale per regolare Curve Optimizer o undervolt per core. Durata totale: {0}.",
                           "Testa cada núcleo sozinho na frequência máxima (método CoreCycler): ideal para ajustar Curve Optimizer ou undervolt por núcleo. Duração total: {0}.");
                    break;
                case StressPlanKind.GpuCompute:
                    t = SL("Charge la carte graphique avec un calcul vérifié à chaque passe, découpé pour ne jamais provoquer lui-même de reset du pilote (TDR).",
                           "Loads the graphics card with a computation verified on every launch, split so that it never causes a driver reset (TDR) itself.",
                           "Carga la gráfica con un cálculo verificado en cada pasada, dividido para no provocar nunca un reinicio del controlador (TDR).",
                           "Lastet die Grafikkarte mit einer bei jedem Durchlauf geprüften Berechnung aus, so aufgeteilt, dass sie selbst nie einen Treiber-Reset (TDR) auslöst.",
                           "Carica la scheda video con un calcolo verificato a ogni passaggio, suddiviso per non causare mai da solo un reset del driver (TDR).",
                           "Carrega a placa de vídeo com um cálculo verificado a cada passagem, dividido para nunca causar por si só um reset do driver (TDR).");
                    break;
                case StressPlanKind.Vram:
                    t = SL("Remplit environ 80 % de la mémoire vidéo puis la relit dans le désordre, avec des motifs et leurs inverses : révèle un overclock mémoire GPU instable ou une puce défaillante.",
                           "Fills about 80 % of video memory, then reads it back in scrambled order with patterns and their inverses: exposes an unstable GPU memory overclock or a failing chip.",
                           "Llena cerca del 80 % de la memoria de vídeo y la relee en desorden con patrones y sus inversos: revela un overclock de memoria GPU inestable o un chip defectuoso.",
                           "Füllt etwa 80 % des Grafikspeichers und liest ihn ungeordnet mit Mustern und deren Inversen zurück: zeigt instabile Speicherübertaktung oder einen defekten Chip.",
                           "Riempie circa l'80 % della memoria video e la rilegge in ordine sparso con schemi e loro inversi: rivela un overclock instabile della memoria GPU o un chip guasto.",
                           "Preenche cerca de 80 % da memória de vídeo e a relê fora de ordem com padrões e seus inversos: revela overclock de memória da GPU instável ou chip com defeito.");
                    break;
                case StressPlanKind.Transient:
                    t = SL("Fait passer CPU et GPU ensemble de 0 à 100 % en créneaux de plus en plus rapides (2 s → 100 ms) : révèle une alimentation trop juste ou un câble 12V-2x6 / PCIe mal enfiché.",
                           "Swings CPU and GPU together from 0 to 100 % in ever faster steps (2 s → 100 ms): exposes a marginal power supply or a badly seated 12V-2x6 / PCIe cable.",
                           "Lleva CPU y GPU juntas de 0 a 100 % en escalones cada vez más rápidos (2 s → 100 ms): revela una fuente justa o un cable 12V-2x6 / PCIe mal conectado.",
                           "Schaltet CPU und GPU gemeinsam immer schneller zwischen 0 und 100 % (2 s → 100 ms): deckt ein knappes Netzteil oder ein schlecht sitzendes 12V-2x6-/PCIe-Kabel auf.",
                           "Porta CPU e GPU insieme da 0 a 100 % a gradini sempre più rapidi (2 s → 100 ms): rivela un alimentatore al limite o un cavo 12V-2x6 / PCIe inserito male.",
                           "Leva CPU e GPU juntas de 0 a 100 % em degraus cada vez mais rápidos (2 s → 100 ms): revela uma fonte no limite ou um cabo 12V-2x6 / PCIe mal encaixado.");
                    break;
                case StressPlanKind.Ram:
                    t = SL("Teste environ 85 % de la mémoire libre (pas seulement le cache) avec trois méthodes, chaque mot relu et comparé : révèle un profil EXPO/XMP instable ou une barrette défaillante.",
                           "Tests about 85 % of free memory (not just the cache) with three methods, every word read back and compared: exposes an unstable EXPO/XMP profile or a failing stick.",
                           "Prueba cerca del 85 % de la memoria libre (no solo la caché) con tres métodos, cada palabra releída y comparada: revela un perfil EXPO/XMP inestable o un módulo defectuoso.",
                           "Testet etwa 85 % des freien Speichers (nicht nur den Cache) mit drei Methoden, jedes Wort zurückgelesen und verglichen: zeigt ein instabiles EXPO/XMP-Profil oder ein defektes Modul.",
                           "Testa circa l'85 % della memoria libera (non solo la cache) con tre metodi, ogni parola riletta e confrontata: rivela un profilo EXPO/XMP instabile o un modulo guasto.",
                           "Testa cerca de 85 % da memória livre (não só o cache) com três métodos, cada palavra relida e comparada: revela um perfil EXPO/XMP instável ou um pente com defeito.");
                    break;
                default:
                    t = SL("Écrit {1} de données connues sur " + drive + ", les relit directement depuis le disque (sans le cache de Windows) et compare tout, puis enchaîne des lectures aléatoires 4K vérifiées. Le fichier est supprimé à la fin.",
                           "Writes {1} of known data to " + drive + ", reads it back straight from the drive (bypassing the Windows cache) and compares everything, then runs verified random 4K reads. The file is deleted at the end.",
                           "Escribe {1} de datos conocidos en " + drive + ", los relee directamente del disco (sin la caché de Windows) y lo compara todo; luego lecturas aleatorias 4K verificadas. El archivo se borra al final.",
                           "Schreibt {1} bekannter Daten auf " + drive + ", liest sie direkt vom Laufwerk zurück (ohne Windows-Cache) und vergleicht alles, danach geprüfte zufällige 4K-Lesezugriffe. Die Datei wird am Ende gelöscht.",
                           "Scrive {1} di dati noti su " + drive + ", li rilegge direttamente dal disco (senza la cache di Windows) e confronta tutto, poi letture casuali 4K verificate. Il file viene eliminato alla fine.",
                           "Grava {1} de dados conhecidos em " + drive + ", relê direto do disco (sem o cache do Windows) e compara tudo, depois leituras aleatórias 4K verificadas. O arquivo é apagado no fim.");
                    break;
            }
            _lblStressPlanHint.Text = string.Format(t, dur, gb);
        }

        private static string FormatStressDuration(double sec)
        {
            var ts = TimeSpan.FromSeconds(sec);
            if (ts.TotalSeconds < 60) return Math.Max(1, (int)Math.Round(ts.TotalSeconds)) + " s";
            if (ts.TotalHours >= 1) return string.Format("{0} h {1:00}", (int)ts.TotalHours, ts.Minutes);
            return Math.Max(1, (int)Math.Round(ts.TotalMinutes)) + " min";
        }

        private static string Clock(double sec)
        {
            var ts = TimeSpan.FromSeconds(Math.Max(0, sec));
            return ts.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds) : string.Format("{0:00}:{1:00}", ts.Minutes, ts.Seconds);
        }

        private void RefreshStressCaps()
        {
            var c = _stressCaps;
            if (c == null)
            {
                _lblStressCaps.Text = SL("Détection du matériel…", "Detecting hardware…", "Detectando hardware…", "Hardware wird erkannt…", "Rilevamento hardware…", "Detectando hardware…");
                RefreshStressCpuTempOption();
                return;
            }
            var lines = new List<string>();
            lines.Add(string.Format(SL("Processeur : {0} cœurs / {1} threads · {2}", "CPU: {0} cores / {1} threads · {2}", "CPU: {0} núcleos / {1} hilos · {2}", "CPU: {0} Kerne / {1} Threads · {2}", "CPU: {0} core / {1} thread · {2}", "CPU: {0} núcleos / {1} threads · {2}"), c.Cores, c.Threads, c.Simd));
            if (!string.IsNullOrEmpty(c.GpuName))
                lines.Add(string.Format(SL("Carte graphique : {0} · {1}", "Graphics: {0} · {1}", "Gráfica: {0} · {1}", "Grafik: {0} · {1}", "Scheda video: {0} · {1}", "Placa de vídeo: {0} · {1}"), c.GpuName, StressText.Gb(c.GpuMemBytes))
                          + (c.GpuSensor != null ? SL(" (capteurs : ", " (sensors: ", " (sensores: ", " (Sensoren: ", " (sensori: ", " (sensores: ") + c.GpuSensor + ")"
                                                 : SL(" (sans capteurs)", " (no sensors)", " (sin sensores)", " (ohne Sensoren)", " (senza sensori)", " (sem sensores)")));
            else
                lines.Add(SL("Carte graphique : aucune compatible OpenCL (tests GPU sautés)", "Graphics: no OpenCL-capable GPU (GPU tests skipped)", "Gráfica: ninguna compatible con OpenCL (pruebas GPU omitidas)", "Grafik: keine OpenCL-fähige GPU (GPU-Tests übersprungen)", "Scheda video: nessuna compatibile OpenCL (test GPU saltati)", "Placa de vídeo: nenhuma compatível com OpenCL (testes de GPU ignorados)"));
            lines.Add(string.Format(SL("Mémoire libre : {0} sur {1}", "Free memory: {0} of {1}", "Memoria libre: {0} de {1}", "Freier Speicher: {0} von {1}", "Memoria libera: {0} su {1}", "Memória livre: {0} de {1}"), StressText.Gb(c.RamAvailBytes), StressText.Gb(c.RamTotalBytes)));
            lines.Add(string.Format(SL("Disque de test : {0} · {1} libres", "Test drive: {0} · {1} free", "Disco de prueba: {0} · {1} libres", "Testlaufwerk: {0} · {1} frei", "Disco di test: {0} · {1} liberi", "Disco de teste: {0} · {1} livres"), c.Drive, StressText.Gb(c.DiskFreeBytes)));
            _lblStressCaps.Text = string.Join("\n", lines);
            RefreshStressCpuTempOption();
        }

        /// <summary>CPU temperature needs the signed PawnIO driver, plus admin rights (direct, or through one UAC prompt).</summary>
        private void RefreshStressCpuTempOption()
        {
            if (_chkStressCpuTemp == null) return;
            _chkStressCpuTemp.Content = SL("Afficher la température du processeur", "Show the CPU temperature", "Mostrar la temperatura de la CPU", "CPU-Temperatur anzeigen", "Mostra la temperatura della CPU", "Mostrar a temperatura da CPU");
            var c = _stressCaps;
            bool pawn = c != null ? c.PawnIo : CpuTempReader.PawnIoInstalled;
            bool admin = c != null ? c.IsAdmin : Elevation.IsAdministrator();
            _chkStressCpuTemp.IsEnabled = pawn && !_stressRunner.IsRunning;
            if (!pawn)
            {
                _chkStressCpuTemp.IsChecked = false;
                _lblStressCpuTempHint.Text = SL("Nécessite le pilote PawnIO (gratuit, signé, utilisé par LibreHardwareMonitor et FanControl). Installe-le avec « winget install PawnIO » ou depuis pawnio.eu, puis rouvre cette page.",
                                                "Needs the PawnIO driver (free, signed, used by LibreHardwareMonitor and FanControl). Install it with 'winget install PawnIO' or from pawnio.eu, then reopen this page.",
                                                "Requiere el controlador PawnIO (gratuito, firmado). Instálalo con «winget install PawnIO» o desde pawnio.eu.",
                                                "Benötigt den PawnIO-Treiber (kostenlos, signiert). Mit „winget install PawnIO“ oder von pawnio.eu installieren.",
                                                "Richiede il driver PawnIO (gratuito, firmato). Installalo con «winget install PawnIO» o da pawnio.eu.",
                                                "Requer o driver PawnIO (gratuito, assinado). Instale com «winget install PawnIO» ou em pawnio.eu.");
            }
            else if (admin)
                _lblStressCpuTempHint.Text = SL("Lue directement via PawnIO (mode administrateur).", "Read directly through PawnIO (administrator mode).", "Leída directamente vía PawnIO (modo administrador).", "Direkt über PawnIO gelesen (Administratormodus).", "Letta direttamente tramite PawnIO (modalità amministratore).", "Lida diretamente via PawnIO (modo administrador).");
            else
                _lblStressCpuTempHint.Text = SL("Lue via PawnIO : Windows demandera une autorisation au démarrage du test, pour un petit assistant qui s'arrête avec lui.",
                                                "Read through PawnIO: Windows will ask for permission when the test starts, for a small helper that stops with it.",
                                                "Leída vía PawnIO: Windows pedirá permiso al iniciar la prueba, para un pequeño asistente que se detiene con ella.",
                                                "Über PawnIO gelesen: Windows fragt beim Teststart nach einer Freigabe für einen kleinen Helfer, der mit dem Test endet.",
                                                "Letta tramite PawnIO: Windows chiederà un'autorizzazione all'avvio del test, per un piccolo assistente che si ferma con esso.",
                                                "Lida via PawnIO: o Windows pedirá permissão ao iniciar o teste, para um pequeno assistente que para junto com ele.");
        }

        private void RefreshStressLast()
        {
            var last = History.StressRuns().FirstOrDefault();
            if (last == null) { _lblStressLast.Text = SL("Aucun test lancé pour l'instant.", "No test run yet.", "Ninguna prueba realizada aún.", "Noch kein Test durchgeführt.", "Nessun test eseguito finora.", "Nenhum teste executado ainda."); return; }
            int ok = last.Stages.Count(s => s.Outcome == StressOutcome.Passed);
            _lblStressLast.Text = string.Format(SL("Dernier test : {0:g} · {1} · {2}/{3} réussi(s)", "Last test: {0:g} · {1} · {2}/{3} passed", "Última prueba: {0:g} · {1} · {2}/{3} superada(s)", "Letzter Test: {0:g} · {1} · {2}/{3} bestanden", "Ultimo test: {0:g} · {1} · {2}/{3} superato/i", "Último teste: {0:g} · {1} · {2}/{3} aprovado(s)"),
                last.Time.ToString("g", Localizer.Culture), StressText.PlanName(last.Plan), ok, last.Stages.Count);
        }

        private void ResetStressTiles()
        {
            string[] titles =
            {
                SL("Fréquence", "Clock", "Frecuencia", "Takt", "Frequenza", "Frequência"),
                SL("Charge", "Load", "Carga", "Last", "Carico", "Carga"),
                StressText.Verified,
                StressText.Errors
            };
            for (int i = 0; i < 4; i++)
            {
                _stTileTitles[i].Text = titles[i];
                _stTileVals[i].Text = "—";
                _stTileVals[i].SetResourceReference(TextBlock.ForegroundProperty, "TextPrimary");
                _stTileSubs[i].Text = "";
            }
            _lblStressLegend.Text = "";
        }

        // ==================== STARTUP ====================
        private void OnStressPageLoaded()
        {
            // A test that was running when the PC went down leaves its marker behind.
            StressRunRecord rec = null;
            try { rec = StressRunner.RecoverInterrupted(); } catch (Exception ex) { Log.Warn(ex); }
            if (rec != null)
            {
                RenderStressResults(rec, true);
                RefreshStressLast();
                var bad = rec.Stages.LastOrDefault();
                if (bad != null && bad.Outcome == StressOutcome.Interrupted)
                    SetToast(string.Format(SL("Le dernier test de stabilité ne s'est pas terminé : le PC a planté pendant « {0} ». Détails dans Matériel › Test de stabilité.",
                                              "The last stability test did not finish: the PC crashed during “{0}”. Details under Hardware › Stability test.",
                                              "La última prueba de estabilidad no terminó: el PC se colgó durante «{0}». Detalles en Hardware › Prueba de estabilidad.",
                                              "Der letzte Stabilitätstest wurde nicht beendet: Der PC stürzte bei „{0}“ ab. Details unter Hardware › Stabilitätstest.",
                                              "L'ultimo test di stabilità non è terminato: il PC si è bloccato durante «{0}». Dettagli in Hardware › Test di stabilità.",
                                              "O último teste de estabilidade não terminou: o PC travou durante «{0}». Detalhes em Hardware › Teste de estabilidade."), bad.Name), "DangerFg");
            }
            else
            {
                var last = History.StressRuns().FirstOrDefault();
                if (last != null) RenderStressResults(last, true);
            }

            Task.Run(() => StressRunner.Probe()).ContinueWith(t =>
            {
                if (t.Status == TaskStatus.RanToCompletion) { _stressCaps = t.Result; RefreshStressCaps(); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ==================== RUN ====================
        /// <summary>Screenshot harness only: a short real run (CPU, 1 GB of RAM, GPU) without the confirmation dialog.</summary>
        public Task RunStressDemoForCapture()
        {
            RamStress.MaxTestBytes = 1L << 30;
            return StartStressRun(new StressPlan
            {
                Kind = StressPlanKind.Quick,
                Stages = new List<StressStage>
                {
                    new StressStage { Kind = StressStageKind.CpuAllCore, DurationSec = 6 },
                    new StressStage { Kind = StressStageKind.Ram, DurationSec = 6 },
                    new StressStage { Kind = StressStageKind.GpuCompute, DurationSec = 6 }
                }
            });
        }

        private async Task StartStressRun(StressPlan forced = null)
        {
            if (_stressRunner.IsRunning) return;
            StressPlan plan = forced ?? StressPlanner.Build(_stressPlan, (int)_slStressDuration.Value);
            // Read before the controls are locked for the run (locking disables the checkbox).
            bool cpuTemp = _chkStressCpuTemp.IsEnabled && _chkStressCpuTemp.IsChecked == true;

            string diskLine = plan.DiskBytes > 0
                ? string.Format(SL(" et environ {0} seront écrits puis effacés sur {1}", " and about {0} will be written then deleted on {1}", " y se escribirán y borrarán unos {0} en {1}", " und etwa {0} werden auf {1} geschrieben und wieder gelöscht", " e circa {0} verranno scritti e poi cancellati su {1}", " e cerca de {0} serão gravados e depois apagados em {1}"), StressText.Gb(plan.DiskBytes), StressPlanner.TestDrive)
                : "";
            string msg = string.Format(SL(
                "{0} — durée prévue : {1}.\n\nLe PC va être fortement sollicité{2}. Ferme les jeux et applications lourdes et enregistre ton travail.\n\nSi le PC plante ou redémarre pendant le test, c'est justement le signe recherché : relance Dump Finder, il indiquera l'étape en cause.\n\nDémarrer ?",
                "{0} — expected duration: {1}.\n\nThe PC will be heavily loaded{2}. Close games and heavy apps and save your work.\n\nIf the PC crashes or reboots during the test, that is exactly the sign we are looking for: start Dump Finder again and it will name the stage at fault.\n\nStart?",
                "{0} — duración prevista: {1}.\n\nEl PC estará muy cargado{2}. Cierra juegos y aplicaciones pesadas y guarda tu trabajo.\n\nSi el PC se cuelga o reinicia durante la prueba, es justo la señal buscada: vuelve a abrir Dump Finder y te dirá qué etapa falló.\n\n¿Iniciar?",
                "{0} — voraussichtliche Dauer: {1}.\n\nDer PC wird stark belastet{2}. Spiele und schwere Anwendungen schließen und Arbeit speichern.\n\nStürzt der PC während des Tests ab oder startet neu, ist genau das der gesuchte Hinweis: Dump Finder erneut öffnen, er nennt den betroffenen Schritt.\n\nStarten?",
                "{0} — durata prevista: {1}.\n\nIl PC sarà molto sollecitato{2}. Chiudi giochi e app pesanti e salva il tuo lavoro.\n\nSe il PC si blocca o si riavvia durante il test, è proprio il segnale cercato: riapri Dump Finder e indicherà la fase in causa.\n\nAvviare?",
                "{0} — duração prevista: {1}.\n\nO PC será muito exigido{2}. Feche jogos e aplicativos pesados e salve seu trabalho.\n\nSe o PC travar ou reiniciar durante o teste, é exatamente o sinal procurado: abra o Dump Finder de novo e ele indicará a etapa responsável.\n\nIniciar?"),
                StressText.PlanName(plan.Kind), FormatStressDuration(plan.TotalSec), diskLine);
            if (forced == null && MessageBox.Show(this, msg, "Dump Finder", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes) return;

            _stressSeries.Clear();
            _stressStageShown = -1;
            _stressLastLogSec = -100;
            _txtStressLog.Clear();
            ResetStressTiles();
            _pbStress.Value = 0;
            _stressResultsCard.Visibility = Visibility.Collapsed;
            _stressChartCard.Visibility = Visibility.Visible;
            BuildStressSteps(plan, -1, null);
            _btnStartStress.IsEnabled = false;
            _btnStopStress.IsEnabled = true;
            _cbStressPlan.IsEnabled = false;
            _slStressDuration.IsEnabled = false;
            _chkStressCpuTemp.IsEnabled = false;
            _lblStressPhase.Text = StressText.PlanName(plan.Kind);
            _lblStressStatus.Text = StressText.Calibrating;
            AppendStressLog(string.Format("[{0:HH:mm:ss}] {1} · {2}", DateTime.Now, StressText.PlanName(plan.Kind), FormatStressDuration(plan.TotalSec)));

            StressRunRecord rec = null;
            try
            {
                rec = await _stressRunner.RunAsync(plan, s => Dispatcher.BeginInvoke(new Action(() => OnStressSample(plan, s))), cpuTemp);
            }
            catch (Exception ex) { Log.Warn(ex); }

            _btnStartStress.IsEnabled = true;
            _btnStopStress.IsEnabled = false;
            _cbStressPlan.IsEnabled = true;
            _slStressDuration.IsEnabled = true;
            RefreshStressCpuTempOption();
            _stressStageShown = -1;
            if (rec == null) return;

            BuildStressSteps(plan, -1, rec);
            _pbStress.Value = rec.Completed ? 100 : _pbStress.Value;
            RenderStressResults(rec, false);
            RefreshStressLast();
            string verdict = StressVerdict(rec);
            _lblStressPhase.Text = verdict;
            _lblStressStatus.Text = "";
            AppendStressLog(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, verdict));
            SetToast(verdict, rec.AnyFailure ? "DangerFg" : (rec.Stages.Any(s => s.Outcome == StressOutcome.Warning) || !rec.Completed ? "WarnFg" : "SuccessFg"));
        }

        private void OnStressSample(StressPlan plan, StressSample s)
        {
            if (s.LogLine != null)
            {
                AppendStressLog(string.Format("[{0:HH:mm:ss}] {1}", DateTime.Now, s.LogLine));
                if (s.Tiles == null) return;
            }
            if (s.StageIndex != _stressStageShown)
            {
                _stressStageShown = s.StageIndex;
                _stressSeries.Clear();
                BuildStressSteps(plan, s.StageIndex, null);
                AppendStressLog(string.Format("[{0:HH:mm:ss}] ▶ {1}", DateTime.Now, s.StageName));
            }
            if (s.Tiles == null) return;

            _lblStressPhase.Text = s.StageCount > 1
                ? string.Format(SL("Étape {0}/{1} · {2}", "Stage {0}/{1} · {2}", "Etapa {0}/{1} · {2}", "Schritt {0}/{1} · {2}", "Fase {0}/{1} · {2}", "Etapa {0}/{1} · {2}"), s.StageIndex + 1, s.StageCount, s.StageName)
                : s.StageName;
            _lblStressStatus.Text = s.Status ?? "";
            _lblStressTimer.Text = Clock(s.RunElapsedSec) + " / " + Clock(s.RunTotalSec);
            _pbStress.Value = Math.Min(100, s.RunElapsedSec * 100.0 / Math.Max(1, s.RunTotalSec));

            for (int i = 0; i < 4 && i < s.Tiles.Length; i++)
            {
                StressTile t = s.Tiles[i];
                _stTileTitles[i].Text = t.Title;
                _stTileVals[i].Text = t.Value;
                _stTileSubs[i].Text = t.Sub ?? "";
                _stTileVals[i].SetResourceReference(TextBlock.ForegroundProperty, t.Tone == 2 ? "DangerFg" : (t.Tone == 1 ? "SuccessFg" : "TextPrimary"));
            }
            if (!string.IsNullOrEmpty(s.Legend)) _lblStressLegend.Text = s.Legend;
            if (!double.IsNaN(s.Series1) || !double.IsNaN(s.Series2))
            {
                _stressSeries.Add(s);
                if (_stressSeries.Count > 180) _stressSeries.RemoveAt(0);
                RedrawStressChart();
            }

            int sec = (int)s.RunElapsedSec;
            if (sec - _stressLastLogSec >= 15)
            {
                _stressLastLogSec = sec;
                AppendStressLog(string.Format("[+{0}] {1} | {2}", Clock(s.RunElapsedSec), s.Status,
                    string.Join(" | ", s.Tiles.Select(t => t.Title + " " + t.Value))));
            }
        }

        private void AppendStressLog(string line)
        {
            _txtStressLog.AppendText(line + Environment.NewLine);
            _txtStressLog.ScrollToEnd();
        }

        // ==================== STEPS / RESULTS ====================
        private void BuildStressSteps(StressPlan plan, int current, StressRunRecord rec)
        {
            _stressSteps.Children.Clear();
            if (plan.Stages.Count < 2) return;
            for (int i = 0; i < plan.Stages.Count; i++)
            {
                string bg = "BgSubtle", fg = "TextMuted";
                if (rec != null && i < rec.Stages.Count)
                    OutcomeColors(rec.Stages[i].Outcome, out fg, out bg);
                else if (i == current) { bg = "AccentSoftBg"; fg = "AccentText"; }
                else if (i < current) { bg = "BgSubtle"; fg = "TextSecondary"; }
                var chip = ResBg(new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 4, 6, 0) }, bg);
                chip.Child = ResFg(new TextBlock { Text = (i + 1) + ". " + StressText.StageName(plan.Stages[i].Kind), FontSize = 11 }, fg);
                _stressSteps.Children.Add(chip);
            }
        }

        private static void OutcomeColors(StressOutcome o, out string fg, out string bg)
        {
            switch (o)
            {
                case StressOutcome.Passed: fg = "SuccessFg"; bg = "SuccessSoftBg"; break;
                case StressOutcome.Warning: fg = "WarnFg"; bg = "WarnSoftBg"; break;
                case StressOutcome.Failed:
                case StressOutcome.Interrupted: fg = "DangerFg"; bg = "DangerSoftBg"; break;
                default: fg = "TextMuted"; bg = "BgSubtle"; break;
            }
        }

        private static string OutcomeGlyph(StressOutcome o)
        {
            switch (o)
            {
                case StressOutcome.Passed: return Glyph.Completed;
                case StressOutcome.Warning: return Glyph.Warning;
                case StressOutcome.Failed:
                case StressOutcome.Interrupted: return Glyph.ErrorBadge;
                default: return Glyph.Info;
            }
        }

        private static string OutcomeLabel(StressOutcome o)
        {
            switch (o)
            {
                case StressOutcome.Passed: return SL("Réussi", "Passed", "Superada", "Bestanden", "Superato", "Aprovado");
                case StressOutcome.Warning: return SL("À surveiller", "Watch out", "A vigilar", "Beobachten", "Da tenere d'occhio", "Atenção");
                case StressOutcome.Failed: return SL("Échec", "Failed", "Fallo", "Fehlgeschlagen", "Fallito", "Falhou");
                case StressOutcome.Interrupted: return SL("PC planté", "PC crashed", "PC colgado", "PC abgestürzt", "PC bloccato", "PC travou");
                case StressOutcome.Stopped: return SL("Arrêté", "Stopped", "Detenida", "Gestoppt", "Interrotto", "Parado");
                default: return SL("Non testé", "Not tested", "No probada", "Nicht getestet", "Non testato", "Não testado");
            }
        }

        private static string StressVerdict(StressRunRecord rec)
        {
            if (rec.AnyFailure)
                return SL("Instabilité détectée : voir les étapes en rouge.", "Instability found: see the stages in red.", "Inestabilidad detectada: mira las etapas en rojo.", "Instabilität gefunden: siehe rote Schritte.", "Instabilità rilevata: vedi le fasi in rosso.", "Instabilidade detectada: veja as etapas em vermelho.");
            if (!rec.Completed)
                return SL("Test arrêté avant la fin : aucune erreur jusque-là.", "Test stopped early: no errors up to that point.", "Prueba detenida antes del final: ningún error hasta entonces.", "Test vorzeitig gestoppt: bis dahin keine Fehler.", "Test interrotto prima della fine: nessun errore fino ad allora.", "Teste parado antes do fim: nenhum erro até então.");
            if (rec.Stages.Any(s => s.Outcome == StressOutcome.Warning))
                return SL("Terminé, avec des points à surveiller.", "Finished, with points to watch.", "Terminada, con puntos a vigilar.", "Beendet, mit Auffälligkeiten.", "Terminato, con punti da tenere d'occhio.", "Concluído, com pontos de atenção.");
            return SL("Aucune erreur détectée.", "No errors found.", "Ningún error detectado.", "Keine Fehler gefunden.", "Nessun errore rilevato.", "Nenhum erro detectado.");
        }

        private StressRunRecord _stressShownRecord;
        private bool _stressShownPrevious;

        private void RefreshStressResultsIfShown()
        {
            if (_stressShownRecord != null && _stressResultsCard != null && _stressResultsCard.Visibility == Visibility.Visible && !_stressRunner.IsRunning)
                Dispatcher.BeginInvoke(new Action(() => RenderStressResults(_stressShownRecord, _stressShownPrevious)));
        }

        private void RenderStressResults(StressRunRecord rec, bool previous)
        {
            _stressShownRecord = rec;
            _stressShownPrevious = previous;
            _stressResultsPanel.Children.Clear();
            _stressChartCard.Visibility = Visibility.Collapsed;
            _stressResultsCard.Visibility = Visibility.Visible;

            var head = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            string fgKey = rec.AnyFailure ? "DangerFg" : (rec.Stages.Any(s => s.Outcome == StressOutcome.Warning) || !rec.Completed ? "WarnFg" : "SuccessFg");
            head.Children.Add(ResFg(new TextBlock { Text = StressVerdict(rec), FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, fgKey));
            head.Children.Add(ResFg(new TextBlock
            {
                Text = (previous ? SL("Dernier test", "Last test", "Última prueba", "Letzter Test", "Ultimo test", "Último teste") + " · " : "")
                       + StressText.PlanName(rec.Plan) + " · " + rec.Time.ToString("g", Localizer.Culture),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            }, "TextMuted"));
            if (!rec.AnyFailure && rec.Completed && rec.Plan == StressPlanKind.Quick)
                head.Children.Add(ResFg(new TextBlock
                {
                    Text = SL("Un test rapide réussi est bon signe, pas une garantie : pour valider un overclock ou un undervolt, lance le test approfondi.",
                              "A passed quick test is a good sign, not a guarantee: to validate an overclock or undervolt, run the thorough test.",
                              "Una prueba rápida superada es buena señal, no una garantía: para validar un overclock o undervolt, lanza la prueba a fondo.",
                              "Ein bestandener Schnelltest ist ein gutes Zeichen, keine Garantie: Für Übertaktung oder Undervolting den gründlichen Test starten.",
                              "Un test rapido superato è un buon segno, non una garanzia: per validare overclock o undervolt, avvia il test approfondito.",
                              "Um teste rápido aprovado é um bom sinal, não uma garantia: para validar overclock ou undervolt, rode o teste completo."),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                }, "TextSecondary"));
            _stressResultsPanel.Children.Add(head);

            foreach (StressStageResult r in rec.Stages)
                _stressResultsPanel.Children.Add(BuildStressResultRow(r));
        }

        private UIElement BuildStressResultRow(StressStageResult r)
        {
            string fg, bg;
            OutcomeColors(r.Outcome, out fg, out bg);
            var row = new Grid { Margin = new Thickness(0, 8, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = ResFg(Ds.Icon(OutcomeGlyph(r.Outcome), 16), fg);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 0, 0);
            row.Children.Add(icon);

            var col = new StackPanel();
            Grid.SetColumn(col, 1);
            var title = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.Wrap };
            var name = new System.Windows.Documents.Run(r.Name ?? StressText.StageName(r.Kind)) { FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "TextPrimary");
            var state = new System.Windows.Documents.Run("  ·  " + OutcomeLabel(r.Outcome) + (r.DurationSec > 0 ? "  ·  " + FormatStressDuration(r.DurationSec) : ""));
            state.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, fg);
            title.Inlines.Add(name);
            title.Inlines.Add(state);
            col.Children.Add(title);
            if (!string.IsNullOrEmpty(r.Summary))
                col.Children.Add(ResFg(new TextBlock { Text = r.Summary, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) }, "TextSecondary"));
            if (!string.IsNullOrEmpty(r.Advice))
            {
                var box = ResBg(new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 8), Margin = new Thickness(0, 6, 0, 0) }, bg);
                box.Child = ResFg(new TextBlock { Text = r.Advice, FontSize = 12, TextWrapping = TextWrapping.Wrap, LineHeight = 17 }, "TextPrimary");
                col.Children.Add(box);
            }
            if (r.FixIds != null && r.FixIds.Count > 0)
            {
                var fixes = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
                foreach (string id in r.FixIds)
                {
                    var fix = _lastResult != null && _lastResult.Fixes != null ? _lastResult.Fixes.FirstOrDefault(f => f.Id == id) : null;
                    var b = CreateFlatButton(SL("Correctif : ", "Fix: ", "Corrección: ", "Korrektur: ", "Correzione: ", "Correção: ") + (fix != null ? fix.Title : id),
                                             "BgSubtle", "AccentText", "BorderCard", new Thickness(10, 5, 10, 5), 12);
                    b.Margin = new Thickness(0, 0, 6, 4);
                    b.Click += (s, e) => SwitchTab(TabFixes);
                    fixes.Children.Add(b);
                }
                col.Children.Add(fixes);
            }
            row.Children.Add(col);
            return row;
        }

        // ==================== CHART ====================
        private void RedrawStressChart()
        {
            if (_stressCanvas == null) return;
            _stressCanvas.Children.Clear();
            double w = _stressCanvas.ActualWidth, h = _stressCanvas.ActualHeight;
            if (w < 40 || h < 40) return;

            Brush grid = GetBrush("ChartGrid");
            for (int g = 1; g <= 3; g++)
                _stressCanvas.Children.Add(new Line { X1 = 0, Y1 = h * g / 4.0, X2 = w, Y2 = h * g / 4.0, Stroke = grid, StrokeThickness = 1 });
            if (_stressSeries.Count < 2) return;

            AddStressLine(s => s.Series1, "ChartPower", 2.2, w, h);
            AddStressLine(s => s.Series2, "ChartTemp", 1.8, w, h);
        }

        private void AddStressLine(Func<StressSample, double> pick, string brushKey, double thickness, double w, double h)
        {
            var vals = _stressSeries.Select(pick).ToList();
            var valid = vals.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToList();
            if (valid.Count < 2) return;
            double max = Math.Max(1e-9, valid.Max() * 1.15);
            var line = new Polyline { Stroke = GetBrush(brushKey), StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round };
            int n = vals.Count;
            for (int i = 0; i < n; i++)
            {
                double v = vals[i];
                if (double.IsNaN(v) || double.IsInfinity(v)) continue;
                double x = i * w / Math.Max(1, Math.Max(n - 1, 59));
                double y = h - Math.Min(h - 4, Math.Max(4, v / max * (h - 12)));
                line.Points.Add(new Point(x, y));
            }
            _stressCanvas.Children.Add(line);
        }
    }
}
