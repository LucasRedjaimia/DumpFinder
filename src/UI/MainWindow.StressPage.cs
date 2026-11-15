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
                _lblStressStatus.Text = Localizer.S("main_window.stopping");
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
            _lblStressTitle.Text = Localizer.S("health.stability_test");
            _lblStressSub.Text = Localizer.S("stress.every_test_checks_its_own_results");
            _lblStressPlanCaption.Text = Localizer.S("stress.what_to_test");
            _lblStressCapsCaption.Text = Localizer.S("stress.this_pc");
            _btnStartStress.Content = Localizer.S("stress.start");
            _btnStopStress.Content = Localizer.S("stress.stop");

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
                          + (k == StressPlanKind.Thorough ? Localizer.S("stress.overnight") : "") + ")";
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
                _lblStressDuration.Text = Localizer.S("stress.time_per_core");
                _slStressDuration.Maximum = 30;
            }
            else
            {
                _lblStressDuration.Text = Localizer.S("stress.duration");
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
                    t = Localizer.S("stress.runs_7_verified_tests_in_cpu");
                    break;
                case StressPlanKind.Thorough:
                    t = Localizer.S("stress.the_same_7_tests_long_version");
                    break;
                case StressPlanKind.CpuAllCore:
                    t = Localizer.F("stress.every_thread_loops_over_computations_who", CpuWork.SimdLabel);
                    break;
                case StressPlanKind.CpuPerCore:
                    t = Localizer.S("stress.tests_each_core_on_its_own");
                    break;
                case StressPlanKind.GpuCompute:
                    t = Localizer.S("stress.loads_the_graphics_card_with_a");
                    break;
                case StressPlanKind.Vram:
                    t = Localizer.S("stress.fills_about_80_of_video_memory");
                    break;
                case StressPlanKind.Transient:
                    t = Localizer.S("stress.swings_cpu_and_gpu_together_from");
                    break;
                case StressPlanKind.Ram:
                    t = Localizer.S("stress.tests_about_85_of_free_memory");
                    break;
                default:
                    t = Localizer.S("stress.disk_plan_hint");
                    break;
            }
            // {0} duration, {1} size, {2} drive (each hint uses the ones it needs).
            _lblStressPlanHint.Text = string.Format(t, dur, gb, drive);
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
                _lblStressCaps.Text = Localizer.S("stress.detecting_hardware");
                RefreshStressCpuTempOption();
                return;
            }
            var lines = new List<string>();
            lines.Add(string.Format(Localizer.S("stress.cpu_cores_threads"), c.Cores, c.Threads, c.Simd));
            if (!string.IsNullOrEmpty(c.GpuName))
                lines.Add(string.Format(Localizer.S("stress.graphics"), c.GpuName, StressText.Gb(c.GpuMemBytes))
                          + (c.GpuSensor != null ? Localizer.S("stress.sensors") + c.GpuSensor + ")"
                                                 : Localizer.S("stress.no_sensors")));
            else
                lines.Add(Localizer.S("stress.graphics_no_opencl_capable_gpu_gpu"));
            lines.Add(string.Format(Localizer.S("stress.free_memory_of"), StressText.Gb(c.RamAvailBytes), StressText.Gb(c.RamTotalBytes)));
            lines.Add(string.Format(Localizer.S("stress.test_drive_free"), c.Drive, StressText.Gb(c.DiskFreeBytes)));
            _lblStressCaps.Text = string.Join("\n", lines);
            RefreshStressCpuTempOption();
        }

        /// <summary>CPU temperature needs the signed PawnIO driver, plus admin rights (direct, or through one UAC prompt).</summary>
        private void RefreshStressCpuTempOption()
        {
            if (_chkStressCpuTemp == null) return;
            _chkStressCpuTemp.Content = Localizer.S("stress.show_the_cpu_temperature");
            var c = _stressCaps;
            bool pawn = c != null ? c.PawnIo : CpuTempReader.PawnIoInstalled;
            bool admin = c != null ? c.IsAdmin : Elevation.IsAdministrator();
            _chkStressCpuTemp.IsEnabled = pawn && !_stressRunner.IsRunning;
            if (!pawn)
            {
                _chkStressCpuTemp.IsChecked = false;
                _lblStressCpuTempHint.Text = Localizer.S("stress.needs_the_pawnio_driver_free_signed");
            }
            else if (admin)
                _lblStressCpuTempHint.Text = Localizer.S("stress.read_directly_through_pawnio_administrat");
            else
                _lblStressCpuTempHint.Text = Localizer.S("stress.read_through_pawnio_windows_will_ask");
        }

        private void RefreshStressLast()
        {
            var last = History.StressRuns().FirstOrDefault();
            if (last == null) { _lblStressLast.Text = Localizer.S("stress.no_test_run_yet"); return; }
            int ok = last.Stages.Count(s => s.Outcome == StressOutcome.Passed);
            _lblStressLast.Text = string.Format(Localizer.S("stress.last_test_0_g_passed"),
                last.Time.ToString("g", Localizer.Culture), StressText.PlanName(last.Plan), ok, last.Stages.Count);
        }

        private void ResetStressTiles()
        {
            string[] titles =
            {
                Localizer.S("stress.clock"),
                Localizer.S("stress.load"),
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
                    SetToast(string.Format(Localizer.S("stress.the_last_stability_test_did_not"), bad.Name), "DangerFg");
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
                ? string.Format(Localizer.S("stress.and_about_will_be_written_then"), StressText.Gb(plan.DiskBytes), StressPlanner.TestDrive)
                : "";
            string msg = string.Format(Localizer.S("stress.expected_duration_the_pc_will_be"),
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
                ? string.Format(Localizer.S("stress.stage"), s.StageIndex + 1, s.StageCount, s.StageName)
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
                case StressOutcome.Passed: return Localizer.S("stress.passed");
                case StressOutcome.Warning: return Localizer.S("stress.watch_out");
                case StressOutcome.Failed: return Localizer.S("stress.failed");
                case StressOutcome.Interrupted: return Localizer.S("stress.pc_crashed");
                case StressOutcome.Stopped: return Localizer.S("stress.stopped");
                default: return Localizer.S("stress.not_tested");
            }
        }

        private static string StressVerdict(StressRunRecord rec)
        {
            if (rec.AnyFailure)
                return Localizer.S("stress.instability_found_see_the_stages_in");
            if (!rec.Completed)
                return Localizer.S("stress.test_stopped_early_no_errors_up");
            if (rec.Stages.Any(s => s.Outcome == StressOutcome.Warning))
                return Localizer.S("stress.finished_with_points_to_watch");
            return Localizer.S("stress.no_errors_found");
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
                Text = (previous ? Localizer.S("stress.last_test") + " · " : "")
                       + StressText.PlanName(rec.Plan) + " · " + rec.Time.ToString("g", Localizer.Culture),
                FontSize = 12,
                Margin = new Thickness(0, 2, 0, 0)
            }, "TextMuted"));
            if (!rec.AnyFailure && rec.Completed && rec.Plan == StressPlanKind.Quick)
                head.Children.Add(ResFg(new TextBlock
                {
                    Text = Localizer.S("stress.a_passed_quick_test_is_a"),
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
                    var b = CreateFlatButton(Localizer.S("stress.fix") + (fix != null ? fix.Title : id),
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
