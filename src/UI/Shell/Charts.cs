using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace WinCrashDoctor
{
    /// <summary>Circular score gauge (0–100) with rounded caps.</summary>
    public class ScoreRing : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(double), typeof(ScoreRing), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(
            "StrokeWidth", typeof(double), typeof(ScoreRing), new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            "TrackBrush", typeof(Brush), typeof(ScoreRing), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
            "FillBrush", typeof(Brush), typeof(ScoreRing), new FrameworkPropertyMetadata(Brushes.SlateBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Value { get { return (double)GetValue(ValueProperty); } set { SetValue(ValueProperty, value); } }
        public double StrokeWidth { get { return (double)GetValue(StrokeWidthProperty); } set { SetValue(StrokeWidthProperty, value); } }
        public Brush TrackBrush { get { return (Brush)GetValue(TrackBrushProperty); } set { SetValue(TrackBrushProperty, value); } }
        public Brush FillBrush { get { return (Brush)GetValue(FillBrushProperty); } set { SetValue(FillBrushProperty, value); } }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= 0) return;
            double r = (size - StrokeWidth) / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);

            var track = new Pen(TrackBrush, StrokeWidth);
            dc.DrawEllipse(null, track, c, r, r);

            double v = Math.Max(0, Math.Min(100, Value)) / 100.0;
            if (v <= 0) return;
            var pen = new Pen(FillBrush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (v >= 0.999)
            {
                dc.DrawEllipse(null, pen, c, r, r);
                return;
            }
            double a0 = -Math.PI / 2;
            double a1 = a0 + v * 2 * Math.PI;
            var p0 = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
            var p1 = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(p0, false, false);
                ctx.ArcTo(p1, new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise, true, true);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    /// <summary>
    /// 30-day stability timeline: area + line of the daily value, one dot per crash day
    /// (red = severe, amber = other), weekly date labels, hover tooltip.
    /// </summary>
    public class StabilityChart : FrameworkElement
    {
        public static readonly DependencyProperty LineBrushProperty = Reg("LineBrush");
        public static readonly DependencyProperty GridBrushProperty = Reg("GridBrush");
        public static readonly DependencyProperty LabelBrushProperty = Reg("LabelBrush");
        public static readonly DependencyProperty SevereBrushProperty = Reg("SevereBrush");
        public static readonly DependencyProperty WarnBrushProperty = Reg("WarnBrush");
        public static readonly DependencyProperty ReliabilityBrushProperty = Reg("ReliabilityBrush");

        private static DependencyProperty Reg(string name)
        {
            return DependencyProperty.Register(name, typeof(Brush), typeof(StabilityChart),
                new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        }

        public Brush LineBrush { get { return (Brush)GetValue(LineBrushProperty); } set { SetValue(LineBrushProperty, value); } }
        public Brush GridBrush { get { return (Brush)GetValue(GridBrushProperty); } set { SetValue(GridBrushProperty, value); } }
        public Brush LabelBrush { get { return (Brush)GetValue(LabelBrushProperty); } set { SetValue(LabelBrushProperty, value); } }
        public Brush SevereBrush { get { return (Brush)GetValue(SevereBrushProperty); } set { SetValue(SevereBrushProperty, value); } }
        public Brush WarnBrush { get { return (Brush)GetValue(WarnBrushProperty); } set { SetValue(WarnBrushProperty, value); } }
        public Brush ReliabilityBrush { get { return (Brush)GetValue(ReliabilityBrushProperty); } set { SetValue(ReliabilityBrushProperty, value); } }

        private Dictionary<DateTime, double> _reliability = new Dictionary<DateTime, double>();

        private List<StabilityDay> _days = new List<StabilityDay>();
        private const double LabelBand = 20;
        private const double TopPad = 8;
        private readonly ToolTip _tip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Mouse };

        public StabilityChart()
        {
            ToolTip = _tip;
            ToolTipService.SetInitialShowDelay(this, 0);
            Background = Brushes.Transparent;
        }

        /// <summary>Transparent background so the whole area is hit-testable for the tooltip.</summary>
        public Brush Background { get; set; }

        public void SetDays(List<StabilityDay> days, List<ReliabilityPoint> reliability = null)
        {
            _days = days ?? new List<StabilityDay>();
            _reliability = (reliability ?? new List<ReliabilityPoint>()).GroupBy(p => p.Date.Date).ToDictionary(g => g.Key, g => g.Last().Index);
            InvalidateVisual();
        }

        private double X(int i, double w)
        {
            int n = Math.Max(1, _days.Count - 1);
            return 6 + i * (w - 12) / n;
        }

        private double Y(double score, double h)
        {
            double plotH = h - LabelBand - TopPad;
            return TopPad + (1 - score / 100.0) * plotH;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Background, null, new Rect(0, 0, w, h));
            if (w <= 20 || h <= 40 || _days.Count == 0) return;

            var grid = new Pen(GridBrush, 1);
            grid.Freeze();
            foreach (double s in new[] { 100.0, 75.0, 50.0 })
            {
                double y = Math.Round(Y(s, h)) + 0.5;
                dc.DrawLine(grid, new Point(0, y), new Point(w, y));
            }

            // Area + line
            var line = new StreamGeometry();
            var area = new StreamGeometry();
            using (var lc = line.Open())
            using (var ac = area.Open())
            {
                double baseY = h - LabelBand;
                lc.BeginFigure(new Point(X(0, w), Y(_days[0].Score, h)), false, false);
                ac.BeginFigure(new Point(X(0, w), baseY), true, true);
                ac.LineTo(new Point(X(0, w), Y(_days[0].Score, h)), false, false);
                for (int i = 1; i < _days.Count; i++)
                {
                    var p = new Point(X(i, w), Y(_days[i].Score, h));
                    lc.LineTo(p, true, true);
                    ac.LineTo(p, false, false);
                }
                ac.LineTo(new Point(X(_days.Count - 1, w), baseY), false, false);
            }
            line.Freeze();
            area.Freeze();

            Color lineColor = (LineBrush as SolidColorBrush) != null ? ((SolidColorBrush)LineBrush).Color : Colors.SlateBlue;
            var fill = new LinearGradientBrush(
                Color.FromArgb(70, lineColor.R, lineColor.G, lineColor.B),
                Color.FromArgb(0, lineColor.R, lineColor.G, lineColor.B), 90);
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
            var lp = new Pen(LineBrush, 2) { LineJoin = PenLineJoin.Round };
            lp.Freeze();
            dc.DrawGeometry(null, lp, line);

            // Windows Reliability Monitor index (1–10, drawn on the same 0–100 scale), dashed
            if (_reliability.Count > 1)
            {
                var rel = new StreamGeometry();
                bool started = false;
                using (var rc = rel.Open())
                {
                    for (int i = 0; i < _days.Count; i++)
                    {
                        double idx;
                        if (!_reliability.TryGetValue(_days[i].Date.Date, out idx)) continue;
                        var pt = new Point(X(i, w), Y(idx * 10, h));
                        if (!started) { rc.BeginFigure(pt, false, false); started = true; }
                        else rc.LineTo(pt, true, true);
                    }
                }
                rel.Freeze();
                var rp = new Pen(ReliabilityBrush, 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0), LineJoin = PenLineJoin.Round };
                rp.Freeze();
                dc.DrawGeometry(null, rp, rel);
            }

            // Crash markers
            for (int i = 0; i < _days.Count; i++)
            {
                var evs = _days[i].Events == null ? null : _days[i].Events.Where(e => Stability.IsMarkerWorthy(e.Category)).ToList();
                if (evs == null || evs.Count == 0) continue;
                bool severe = evs.Any(e => Stability.IsSevere(e.Category));
                Brush b = severe ? SevereBrush : WarnBrush;
                var p = new Point(X(i, w), Y(_days[i].Score, h));
                double r = severe ? 5.5 : 4.5;
                Color bc = (b as SolidColorBrush) != null ? ((SolidColorBrush)b).Color : Colors.OrangeRed;
                var halo = new SolidColorBrush(Color.FromArgb(55, bc.R, bc.G, bc.B));
                halo.Freeze();
                dc.DrawEllipse(halo, null, p, r + 4, r + 4);
                dc.DrawEllipse(b, null, p, r, r);
            }

            // Date labels (every 7 days + today)
            var culture = CultureInfo.CurrentUICulture;
            var tf = new Typeface(Ds.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int i = 0; i < _days.Count - 4; i += 7)
                DrawLabel(dc, _days[i].Date.ToString("d MMM", culture), X(i, w), h, tf, dpi, i == 0 ? 0 : 0.5);
            DrawLabel(dc, Localizer.L("Auj.", "Today", "Hoy", "Heute", "Oggi", "Hoje"), X(_days.Count - 1, w), h, tf, dpi, 1);
        }

        private void DrawLabel(DrawingContext dc, string text, double x, double h, Typeface tf, double dpi, double align)
        {
            var ft = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, tf, 11, LabelBrush, dpi);
            double left = Math.Max(0, Math.Min(ActualWidth - ft.Width, x - ft.Width * align));
            dc.DrawText(ft, new Point(left, h - ft.Height));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_days.Count == 0 || ActualWidth <= 12) return;
            double x = e.GetPosition(this).X;
            int i = (int)Math.Round((x - 6) / ((ActualWidth - 12) / Math.Max(1, _days.Count - 1)));
            i = Math.Max(0, Math.Min(_days.Count - 1, i));
            var d = _days[i];
            int n = d.Events != null ? d.Events.Count : 0;
            string head = d.Date.ToString("dddd d MMMM", Localizer.Culture);
            string body = n == 0
                ? Localizer.L("Aucun crash", "No crash", "Sin fallos", "Kein Absturz", "Nessun crash", "Sem falhas")
                : string.Join("\n", d.Events.Take(4).Select(ev => "• " + Localizer.CategoryName(ev.Category) + "  " + ev.Timestamp.ToString("HH:mm")));
            double widx;
            if (_reliability.TryGetValue(d.Date.Date, out widx))
                body += "\n" + Localizer.L("Indice Windows : ", "Windows index: ", "Índice de Windows: ", "Windows-Index: ", "Indice Windows: ", "Índice do Windows: ") + widx.ToString("0.0") + " / 10";
            _tip.Content = head + "\n" + body;
        }
    }
}
