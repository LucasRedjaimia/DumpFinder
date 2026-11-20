using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinCrashDoctor.Stress;

namespace WinCrashDoctor
{
    /// <summary>
    /// Hardware › GPU curve: the NVIDIA voltage/frequency curve read from MSI Afterburner (saved profiles and, when it
    /// runs, the curve applied right now), over the stock curve, with a safer suggested version and tips. Read-only:
    /// the values are shown for the user to set in Afterburner's curve editor, never written.
    /// </summary>
    public partial class MainWindow
    {
        private const int HealthSubGpuCurve = 4;

        private Button _btnHealthSubCurve;
        private ScrollViewer _svHealthCurve;
        private StackPanel _curvePanel;
        private VfCurveChart _curveChart;
        private GpuCurveInfo _curveInfo;
        private string _curveSelected;              // profile id shown ("Live", "Startup", "Profile1"…)
        private bool _curveLoading;

        private void HostGpuCurveInHardware()
        {
            var seg = _btnHealthSubHw.Parent as StackPanel;
            _btnHealthSubCurve = CreateFlatButton("", "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 6, 12, 6), 12);
            _btnHealthSubCurve.Margin = new Thickness(0, 0, 6, 0);
            _btnHealthSubCurve.Visibility = Visibility.Collapsed;           // shown once an Afterburner NVIDIA curve is found
            _btnHealthSubCurve.Click += (s, e) => SwitchHealthSubTab(HealthSubGpuCurve);
            if (seg != null) seg.Children.Insert(Math.Max(0, seg.Children.IndexOf(_btnHealthSubStress)), _btnHealthSubCurve);

            _svHealthCurve = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Visibility = Visibility.Collapsed };
            _curvePanel = new StackPanel { Margin = new Thickness(0, 0, 6, 12) };
            _svHealthCurve.Content = _curvePanel;
            Grid.SetRow(_svHealthCurve, 1);
            _pageHealth.Children.Add(_svHealthCurve);
            _ = LoadGpuCurveAsync();
        }

        private void RefreshGpuCurveLabels()
        {
            if (_btnHealthSubCurve == null) return;
            _btnHealthSubCurve.Content = Localizer.S("gpu_curve.tab");
            if (_curveInfo != null) RenderGpuCurve();
        }

        /// <summary>Reads Afterburner's files and live curve off the UI thread; shows the tab only when there is a curve.</summary>
        private async Task LoadGpuCurveAsync()
        {
            if (_curveLoading) return;
            _curveLoading = true;
            try
            {
                GpuCurveInfo info = await Task.Run(() =>
                {
                    try { return GpuCurve.Load(); }
                    catch (Exception ex) { Log.Warn(ex, "GPU curve"); return null; }
                });
                _curveInfo = info;
                if (_btnHealthSubCurve != null) _btnHealthSubCurve.Visibility = info != null ? Visibility.Visible : Visibility.Collapsed;
                if (info != null && (_curveSelected == null || (_curveSelected == "Live" && info.Live == null) || (_curveSelected != "Live" && info.Profiles.All(p => p.Id != _curveSelected))))
                    _curveSelected = info.Live != null ? "Live" : (info.Profiles.FirstOrDefault(p => p.Modified) ?? info.Profiles.First()).Id;
                RenderGpuCurve();
            }
            finally { _curveLoading = false; }
        }

        private VfProfile SelectedCurve()
        {
            if (_curveInfo == null) return null;
            if (_curveSelected == "Live" && _curveInfo.Live != null) return _curveInfo.Live;
            return _curveInfo.Profiles.FirstOrDefault(p => p.Id == _curveSelected) ?? _curveInfo.Live ?? _curveInfo.Profiles.FirstOrDefault();
        }

        private void RenderGpuCurve()
        {
            if (_curvePanel == null) return;
            _curvePanel.Children.Clear();
            GpuCurveInfo info = _curveInfo;
            if (info == null)
            {
                _curvePanel.Children.Add(OptNote(Localizer.S("gpu_curve.none")));
                return;
            }
            VfProfile curve = SelectedCurve();
            CurveAdvice advice = GpuCurve.Analyze(curve, info.Stock);

            // ---- Source and refresh
            var head = new Grid();
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var headText = new StackPanel { Margin = new Thickness(0, 0, 14, 0) };
            headText.Children.Add(ResFg(new TextBlock { Text = Localizer.S("gpu_curve.title"), FontSize = 14, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, "TextPrimary"));
            string source;
            if (info.Live == null) source = Localizer.S("gpu_curve.source_saved_only");
            else if (info.ActiveIds.Contains("Stock")) source = Localizer.S("gpu_curve.source_live_stock");
            else if (info.LiveIsCustom) source = Localizer.S("gpu_curve.source_live_custom");
            else source = Localizer.F("gpu_curve.source_live_profile", string.Join(", ", info.ActiveIds.Select(GpuCurve.ProfileName)));
            headText.Children.Add(ResFg(new TextBlock { Text = source, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0) }, "TextSecondary"));
            head.Children.Add(headText);
            var refresh = CreateFlatButton(Localizer.S("gpu_curve.refresh"), "BgCard", "TextPrimary", "BorderStrong", new Thickness(12, 6, 12, 6), 12);
            refresh.VerticalAlignment = VerticalAlignment.Center;
            refresh.Click += (s, e) => { _ = LoadGpuCurveAsync(); };
            Grid.SetColumn(refresh, 1);
            head.Children.Add(refresh);
            _curvePanel.Children.Add(OptCard(head));

            // ---- Which curve: the applied one, then each saved profile ("applied" badge on those matching it)
            var pills = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
            pills.Children.Add(ResFg(new TextBlock { Text = Localizer.S("gpu_curve.show"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 10, 6) }, "TextSecondary"));
            var ids = new List<string>();
            if (info.Live != null) ids.Add("Live");
            ids.AddRange(info.Profiles.Select(p => p.Id));
            foreach (string id in ids)
            {
                string pid = id;
                VfProfile p = id == "Live" ? info.Live : info.Profiles.First(x => x.Id == id);
                string label = GpuCurve.ProfileName(id);
                if (id != "Live" && info.ActiveIds.Contains(id)) label += "  ·  " + Localizer.S("gpu_curve.applied_badge");
                else if (id != "Live" && !p.Modified) label += "  ·  " + Localizer.S("gpu_curve.stock_badge");
                var b = CreateFlatButton(label, "BgCard", "TextSecondary", "BorderCard", new Thickness(12, 5, 12, 5), 12);
                b.Margin = new Thickness(0, 0, 6, 6);
                StyleTogglePill(b, id == curve.Id);
                b.Click += (s, e) => { _curveSelected = pid; RenderGpuCurve(); };
                pills.Children.Add(b);
            }
            _curvePanel.Children.Add(pills);

            // ---- Chart and legend
            var chartBox = new StackPanel();
            var legend = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            string curveName = GpuCurve.ProfileName(curve.Id);
            legend.Children.Add(LegendItem("TextMuted", true, Localizer.S("gpu_curve.stock")));
            legend.Children.Add(LegendItem("AccentText", false, curveName));
            if (advice.Suggested != null) legend.Children.Add(LegendItem("SuccessFg", true, Localizer.S("gpu_curve.suggested")));
            chartBox.Children.Add(legend);
            _curveChart = new VfCurveChart { Height = 280 };
            _curveChart.SetResourceReference(VfCurveChart.StockBrushProperty, "TextMuted");
            _curveChart.SetResourceReference(VfCurveChart.CurveBrushProperty, "AccentText");
            _curveChart.SetResourceReference(VfCurveChart.SuggestBrushProperty, "SuccessFg");
            _curveChart.SetResourceReference(VfCurveChart.GridBrushProperty, "BorderCard");
            _curveChart.SetResourceReference(VfCurveChart.LabelBrushProperty, "TextMuted");
            AutomationProperties.SetName(_curveChart, Localizer.F("gpu_curve.chart_name", curveName));
            double[] mv = curve.Points.Select(p => p.MilliVolts).ToArray();
            double[] stock = info.Stock.Points.Select(p => p.BaseMHz).ToArray();
            if (stock.Length != mv.Length) stock = curve.Points.Select(p => p.BaseMHz).ToArray();
            _curveChart.SetData(mv, stock, curve.Points.Select(p => p.MHz).ToArray(), advice.Suggested != null ? advice.Suggested.ToArray() : null,
                Localizer.S("gpu_curve.stock"), curveName, Localizer.S("gpu_curve.suggested"));
            chartBox.Children.Add(_curveChart);
            _curvePanel.Children.Add(OptCard(chartBox));

            // ---- Tips
            _curvePanel.Children.Add(OptSectionTitle(Localizer.S("gpu_curve.tips")));
            foreach (CurveTip t in advice.Tips) _curvePanel.Children.Add(TipCard(t));

            // ---- Suggested values to set by hand, and the test
            if (advice.Suggested != null)
            {
                _curvePanel.Children.Add(OptSectionTitle(Localizer.S("gpu_curve.values_title")));
                var vals = new StackPanel();
                vals.Children.Add(OptNote(Localizer.F("gpu_curve.values_note", GpuCurve.Mv(advice.TargetMv), GpuCurve.Mhz(advice.SuggestedMHz))));
                var table = new Grid { Margin = new Thickness(0, 2, 0, 6) };
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
                table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                int row = 0;
                foreach (var kp in advice.KeyPoints)
                {
                    table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    VfPoint now = curve.Points.OrderBy(p => Math.Abs(p.MilliVolts - kp.Key)).First();
                    AddCell(table, row, 0, GpuCurve.Mv(kp.Key), "TextSecondary", false);
                    AddCell(table, row, 1, GpuCurve.Mhz(kp.Value), "TextPrimary", true);
                    AddCell(table, row, 2, Localizer.F("gpu_curve.now_value", GpuCurve.Mhz(now.MHz)), "TextMuted", false);
                    row++;
                }
                vals.Children.Add(table);
                vals.Children.Add(OptNote(Localizer.S("gpu_curve.never_written")));
                var test = CreateFlatButton(Localizer.S("gpu_curve.test"), "Accent", "OnAccent", "Accent", new Thickness(14, 7, 14, 7), 12.5);
                test.HorizontalAlignment = HorizontalAlignment.Left;
                test.Click += (s, e) => OpenStress(StressPlanKind.GpuCompute);
                vals.Children.Add(test);
                _curvePanel.Children.Add(OptCard(vals));
            }
            _curvePanel.Children.Add(OptNote(Localizer.F("gpu_curve.file_note", info.ProfileFile)));
        }

        private UIElement LegendItem(string brushKey, bool dashed, string text)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            var line = new System.Windows.Shapes.Line { X1 = 0, X2 = 16, Y1 = 0, Y2 = 0, StrokeThickness = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
            line.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, brushKey);
            if (dashed) line.StrokeDashArray = new System.Windows.Media.DoubleCollection { 2, 1.5 };
            sp.Children.Add(line);
            sp.Children.Add(ResFg(new TextBlock { Text = text, FontSize = 12 }, "TextSecondary"));
            return sp;
        }

        private Border TipCard(CurveTip t)
        {
            string bg = t.Kind == CurveTipKind.Danger ? "DangerSoftBg" : t.Kind == CurveTipKind.Warning ? "WarnSoftBg" : "BgCard";
            string fg = t.Kind == CurveTipKind.Danger ? "DangerFg" : t.Kind == CurveTipKind.Warning ? "WarnFg" : "TextSecondary";
            string glyph = t.Kind == CurveTipKind.Info ? Glyph.Info : Glyph.Warning;
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icon = ResFg(Ds.Icon(glyph, 14), fg);
            icon.VerticalAlignment = VerticalAlignment.Top;
            icon.Margin = new Thickness(0, 2, 10, 0);
            row.Children.Add(icon);
            var text = ResFg(new TextBlock { Text = t.Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 18 }, t.Kind == CurveTipKind.Info ? "TextPrimary" : fg);
            Grid.SetColumn(text, 1);
            row.Children.Add(text);
            Border card = ResBorder(ResBg(new Border { CornerRadius = new CornerRadius(10), BorderThickness = new Thickness(1), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 8), Child = row }, bg), "BorderCard");
            return card;
        }

        private void AddCell(Grid g, int row, int col, string text, string fgKey, bool bold)
        {
            var t = ResFg(new TextBlock { Text = text, FontSize = 12.5, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Margin = new Thickness(0, 2, 12, 2) }, fgKey);
            Grid.SetRow(t, row);
            Grid.SetColumn(t, col);
            g.Children.Add(t);
        }

        /// <summary>UI self-test / captures: the GPU curve chart once rendered (null when the tab is not available).</summary>
        internal VfCurveChart GpuCurveChartForTest { get { return _curveChart; } }

        internal async Task<bool> OpenGpuCurveForCapture()
        {
            await LoadGpuCurveAsync();
            if (_curveInfo == null) return false;
            SwitchTab(TabHardware);
            SwitchHealthSubTab(HealthSubGpuCurve);
            return true;
        }

        /// <summary>Shows a profile by id, or the first modified saved profile when <paramref name="id"/> is null.</summary>
        internal void ShowGpuCurveProfileForCapture(string id)
        {
            if (_curveInfo == null) return;
            VfProfile p = id != null ? _curveInfo.Profiles.FirstOrDefault(x => x.Id == id) : _curveInfo.Profiles.FirstOrDefault(x => x.Modified);
            if (p == null && id != "Live") return;
            _curveSelected = p != null ? p.Id : id;
            RenderGpuCurve();
        }
    }
}
