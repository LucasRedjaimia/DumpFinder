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
        private readonly ToolTip _tip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Custom };

        /// <summary>Day under the mouse (-1: none). The tooltip sits above that day's point, not where the mouse came in.</summary>
        private int _hover = -1;

        public StabilityChart()
        {
            _tip.PlacementTarget = this;
            _tip.CustomPopupPlacementCallback = PlaceTip;
            ToolTip = _tip;
            ToolTipService.SetInitialShowDelay(this, 0);
            Background = Brushes.Transparent;
        }

        /// <summary>The hovered day's point on the line, in the chart's coordinates.</summary>
        public Point HoverPoint
        {
            get { return _hover < 0 || _hover >= _days.Count ? new Point(double.NaN, double.NaN) : new Point(X(_hover, ActualWidth), Y(_days[_hover].Score, ActualHeight)); }
        }

        /// <summary>
        /// Centred above the hovered point, else below it when there is no room above (top of the screen).
        /// WPF gives the sizes in device pixels: the ratio between the target size and ActualWidth converts our
        /// coordinates whatever the display scaling.
        /// </summary>
        private System.Windows.Controls.Primitives.CustomPopupPlacement[] PlaceTip(Size popupSize, Size targetSize, Point offset)
        {
            Point p = HoverPoint;
            if (double.IsNaN(p.X) || ActualWidth <= 0) return new System.Windows.Controls.Primitives.CustomPopupPlacement[0];
            double sx = targetSize.Width / ActualWidth, sy = ActualHeight > 0 ? targetSize.Height / ActualHeight : sx;
            const double gap = 12;
            double left = p.X * sx - popupSize.Width / 2;
            var above = new Point(left, (p.Y - gap) * sy - popupSize.Height);
            var below = new Point(left, (p.Y + gap) * sy);
            return new[]
            {
                new System.Windows.Controls.Primitives.CustomPopupPlacement(above, System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
                new System.Windows.Controls.Primitives.CustomPopupPlacement(below, System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
            };
        }

        /// <summary>Transparent background so the whole area is hit-testable for the tooltip.</summary>
        public Brush Background { get; set; }

        public void SetDays(List<StabilityDay> days, List<ReliabilityPoint> reliability = null)
        {
            _days = days ?? new List<StabilityDay>();
            _reliability = (reliability ?? new List<ReliabilityPoint>()).GroupBy(p => p.Date.Date).ToDictionary(g => g.Key, g => g.Last().Index);
            _hover = -1;   // new scan: the next mouse move refills the tooltip with the new data
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

            // Hovered day: a guide line down to the axis and a ring on its point, so the tooltip clearly belongs to it
            if (_hover >= 0 && _hover < _days.Count)
            {
                var hp = new Point(X(_hover, w), Y(_days[_hover].Score, h));
                var guide = new Pen(LabelBrush, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                guide.Freeze();
                double gx = Math.Round(hp.X) + 0.5;
                dc.DrawLine(guide, new Point(gx, TopPad), new Point(gx, h - LabelBand));
                var ring = new Pen(LineBrush, 1.5);
                ring.Freeze();
                dc.DrawEllipse(null, ring, hp, 12, 12);   // around the crash marker too, without hiding its colour
            }

            // Date labels (every 7 days + today)
            var culture = Localizer.Culture; // axis dates in the app language, not the Windows one
            var tf = new Typeface(Ds.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int i = 0; i < _days.Count - 4; i += 7)
                DrawLabel(dc, Localizer.DayMonth(_days[i].Date), X(i, w), h, tf, dpi, i == 0 ? 0 : 0.5);
            DrawLabel(dc, Localizer.S("charts.today"), X(_days.Count - 1, w), h, tf, dpi, 1);
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
            SetHover(Math.Max(0, Math.Min(_days.Count - 1, i)));
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            InvalidateVisual();
        }

        /// <summary>Shows day <paramref name="i"/> in the tooltip and moves the tooltip over its point (also used by the UI self-test).</summary>
        public void SetHover(int i)
        {
            if (i == _hover || i < 0 || i >= _days.Count) return;
            _hover = i;
            InvalidateVisual();
            var d = _days[i];
            int n = d.Events != null ? d.Events.Count : 0;
            string head = d.Date.ToString("dddd d MMMM", Localizer.Culture);
            string body = n == 0
                ? Localizer.S("charts.no_crash")
                : string.Join("\n", d.Events.Take(4).Select(ev => "• " + Localizer.CategoryName(ev.Category) + "  " + ev.Timestamp.ToString("HH:mm")));
            double widx;
            if (_reliability.TryGetValue(d.Date.Date, out widx))
                body += "\n" + Localizer.S("charts.windows_index") + widx.ToString("0.0") + " / 10";
            _tip.Content = head + "\n" + body;
            // An open popup only re-runs its placement when an offset changes: nudge it so it follows the new point.
            _tip.HorizontalOffset = _tip.HorizontalOffset == 0 ? 0.01 : 0;
        }
    }

    /// <summary>
    /// GPU voltage/frequency curve (Hardware › GPU curve): stock curve (dashed), the chosen profile (solid) and the
    /// suggested safer curve (dotted), frequency against voltage, with a hover guide and a tooltip over the point.
    /// </summary>
    public class VfCurveChart : FrameworkElement
    {
        public static readonly DependencyProperty StockBrushProperty = Reg("StockBrush");
        public static readonly DependencyProperty CurveBrushProperty = Reg("CurveBrush");
        public static readonly DependencyProperty SuggestBrushProperty = Reg("SuggestBrush");
        public static readonly DependencyProperty GridBrushProperty = Reg("GridBrush");
        public static readonly DependencyProperty LabelBrushProperty = Reg("LabelBrush");

        private static DependencyProperty Reg(string name)
        {
            return DependencyProperty.Register(name, typeof(Brush), typeof(VfCurveChart),
                new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));
        }

        public Brush StockBrush { get { return (Brush)GetValue(StockBrushProperty); } set { SetValue(StockBrushProperty, value); } }
        public Brush CurveBrush { get { return (Brush)GetValue(CurveBrushProperty); } set { SetValue(CurveBrushProperty, value); } }
        public Brush SuggestBrush { get { return (Brush)GetValue(SuggestBrushProperty); } set { SetValue(SuggestBrushProperty, value); } }
        public Brush GridBrush { get { return (Brush)GetValue(GridBrushProperty); } set { SetValue(GridBrushProperty, value); } }
        public Brush LabelBrush { get { return (Brush)GetValue(LabelBrushProperty); } set { SetValue(LabelBrushProperty, value); } }

        private double[] _mv = new double[0], _stock = new double[0], _curve = new double[0], _suggest;
        private string _curveName = "", _stockName = "", _suggestName = "";
        private int _first, _hover = -1;
        private double _yMin, _yMax;
        private const double Left = 44, Bottom = 22, Top = 10, Right = 10;
        private readonly ToolTip _tip = new ToolTip { Placement = System.Windows.Controls.Primitives.PlacementMode.Custom };

        public VfCurveChart()
        {
            _tip.PlacementTarget = this;
            _tip.CustomPopupPlacementCallback = (popup, target, offset) =>
            {
                if (_hover < 0 || ActualWidth <= 0) return new System.Windows.Controls.Primitives.CustomPopupPlacement[0];
                double sx = target.Width / ActualWidth, sy = ActualHeight > 0 ? target.Height / ActualHeight : sx;
                var p = new Point(X(_hover), Y(_curve[_hover]));
                return new[]
                {
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(p.X * sx - popup.Width / 2, (p.Y - 14) * sy - popup.Height), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal),
                    new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(p.X * sx - popup.Width / 2, (p.Y + 14) * sy), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal)
                };
            };
            ToolTip = _tip;
            ToolTipService.SetInitialShowDelay(this, 0);
        }

        /// <summary>Same voltages for every series; <paramref name="suggest"/> may be null. The view starts a little before the first change.</summary>
        public void SetData(double[] mv, double[] stock, double[] curve, double[] suggest, string stockName, string curveName, string suggestName)
        {
            _mv = mv ?? new double[0];
            _stock = stock ?? new double[0];
            _curve = curve ?? new double[0];
            _suggest = suggest;
            _stockName = stockName; _curveName = curveName; _suggestName = suggestName;
            _hover = -1;
            // Low voltages sit at idle clocks and only flatten the chart: start 100 MHz under the first changed point,
            // and no lower than 60 % of the top clock.
            double top = _curve.Concat(_stock).DefaultIfEmpty(1).Max();
            int changed = Enumerable.Range(0, _curve.Length).FirstOrDefault(i => Math.Abs(_curve[i] - _stock[i]) >= 1 || (_suggest != null && Math.Abs(_suggest[i] - _stock[i]) >= 1));
            double floor = Math.Max(top * 0.6, changed > 0 ? Math.Min(_curve[changed], _stock[changed]) - 100 : top * 0.6);
            _first = Math.Max(0, Enumerable.Range(0, _curve.Length).FirstOrDefault(i => _stock[i] >= floor || _curve[i] >= floor) - 1);
            _yMin = Math.Floor(Math.Min(floor, Math.Min(_stock[_first], _curve[_first])) / 100) * 100;
            _yMax = Math.Ceiling((top + 40) / 100) * 100;
            InvalidateVisual();
        }

        public int Count { get { return _mv.Length; } }
        public double HoverMv { get { return _hover >= 0 ? _mv[_hover] : double.NaN; } }
        public Point HoverPoint { get { return _hover >= 0 ? new Point(X(_hover), Y(_curve[_hover])) : new Point(double.NaN, double.NaN); } }

        private double X(int i)
        {
            double span = _mv[_mv.Length - 1] - _mv[_first];
            return Left + (span > 0 ? (_mv[i] - _mv[_first]) / span : 0) * (ActualWidth - Left - Right);
        }

        private double Y(double mhz)
        {
            double plot = ActualHeight - Top - Bottom;
            return Top + (1 - (mhz - _yMin) / Math.Max(1, _yMax - _yMin)) * plot;
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));
            if (_mv.Length < 2 || w < 80 || h < 60) return;
            var tf = new Typeface(Ds.UiFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
            double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;

            var grid = new Pen(GridBrush, 1);
            grid.Freeze();
            double step = (_yMax - _yMin) > 1200 ? 500 : 250;
            for (double v = Math.Ceiling(_yMin / step) * step; v <= _yMax; v += step)
            {
                double y = Math.Round(Y(v)) + 0.5;
                dc.DrawLine(grid, new Point(Left, y), new Point(w - Right, y));
                var ft = new FormattedText(v.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, LabelBrush, dpi);
                dc.DrawText(ft, new Point(Left - 6 - ft.Width, y - ft.Height / 2));
            }
            for (double mv = Math.Ceiling(_mv[_first] / 50) * 50; mv <= _mv[_mv.Length - 1]; mv += 50)
            {
                int i = Nearest(mv);
                var ft = new FormattedText(mv.ToString("0", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, LabelBrush, dpi);
                double x = X(i);
                if (x - ft.Width / 2 < Left - 4 || x + ft.Width / 2 > w) continue;
                dc.DrawText(ft, new Point(x - ft.Width / 2, h - ft.Height));
            }
            var unitV = new FormattedText("mV", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, LabelBrush, dpi);
            dc.DrawText(unitV, new Point(w - Right - unitV.Width, h - Bottom - unitV.Height - 2));
            var unitF = new FormattedText("MHz", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10.5, LabelBrush, dpi);
            dc.DrawText(unitF, new Point(Left + 4, Top));

            dc.PushClip(new RectangleGeometry(new Rect(Left, 0, w - Left, h - Bottom)));
            DrawSeries(dc, _stock, StockBrush, 1.5, new double[] { 4, 3 });
            DrawSeries(dc, _curve, CurveBrush, 2, null);
            if (_suggest != null) DrawSeries(dc, _suggest, SuggestBrush, 2.2, new double[] { 1.2, 2 });   // on top: it often runs close to the curve
            if (_hover >= _first)
            {
                var guide = new Pen(LabelBrush, 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
                guide.Freeze();
                double gx = Math.Round(X(_hover)) + 0.5;
                dc.DrawLine(guide, new Point(gx, Top), new Point(gx, h - Bottom));
                dc.DrawEllipse(CurveBrush, null, new Point(X(_hover), Y(_curve[_hover])), 4, 4);
                if (_suggest != null) dc.DrawEllipse(SuggestBrush, null, new Point(X(_hover), Y(_suggest[_hover])), 3, 3);
            }
            dc.Pop();
        }

        private void DrawSeries(DrawingContext dc, double[] v, Brush b, double width, double[] dash)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(X(_first), Y(v[_first])), false, false);
                for (int i = _first + 1; i < v.Length; i++) c.LineTo(new Point(X(i), Y(v[i])), true, true);
            }
            g.Freeze();
            var pen = new Pen(b, width) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (dash != null) pen.DashStyle = new DashStyle(dash, 0);
            pen.Freeze();
            dc.DrawGeometry(null, pen, g);
        }

        private int Nearest(double mv)
        {
            int best = 0;
            for (int i = 1; i < _mv.Length; i++) if (Math.Abs(_mv[i] - mv) < Math.Abs(_mv[best] - mv)) best = i;
            return best;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_mv.Length < 2 || ActualWidth <= Left + Right) return;
            double x = e.GetPosition(this).X;
            int best = _first;
            for (int i = _first; i < _mv.Length; i++) if (Math.Abs(X(i) - x) < Math.Abs(X(best) - x)) best = i;
            SetHover(best);
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = -1;
            InvalidateVisual();
        }

        /// <summary>Shows point <paramref name="i"/> in the tooltip, placed over it (also used by the UI self-test).</summary>
        public void SetHover(int i)
        {
            if (i == _hover || i < _first || i >= _mv.Length) return;
            _hover = i;
            InvalidateVisual();
            string text = GpuCurve.Mv(_mv[i])
                          + "\n" + Localizer.F("gpu_curve.series_value", _curveName, GpuCurve.Mhz(_curve[i]))
                          + "\n" + Localizer.F("gpu_curve.series_value", _stockName, GpuCurve.Mhz(_stock[i]));
            if (_suggest != null) text += "\n" + Localizer.F("gpu_curve.series_value", _suggestName, GpuCurve.Mhz(_suggest[i]));
            _tip.Content = text;
            _tip.HorizontalOffset = _tip.HorizontalOffset == 0 ? 0.01 : 0;
        }
    }
}
