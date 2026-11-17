using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using DumpFinder.Mac.Core;
using WinCrashDoctor;

namespace DumpFinder.Mac;

/// <summary>Circular score gauge 0–100 with rounded caps (Windows UI/Shell/Charts.cs ScoreRing).</summary>
public sealed class ScoreRing : Control
{
    public static readonly StyledProperty<double> ValueProperty = AvaloniaProperty.Register<ScoreRing, double>(nameof(Value));
    public static readonly StyledProperty<double> StrokeWidthProperty = AvaloniaProperty.Register<ScoreRing, double>(nameof(StrokeWidth), 11);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty = AvaloniaProperty.Register<ScoreRing, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> FillBrushProperty = AvaloniaProperty.Register<ScoreRing, IBrush?>(nameof(FillBrush));

    static ScoreRing() => AffectsRender<ScoreRing>(ValueProperty, StrokeWidthProperty, TrackBrushProperty, FillBrushProperty);

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double StrokeWidth { get => GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }

    public override void Render(DrawingContext dc)
    {
        double size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        double r = (size - StrokeWidth) / 2;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        dc.DrawEllipse(null, new Pen(TrackBrush, StrokeWidth), c, r, r);

        double v = Math.Clamp(Value, 0, 100) / 100.0;
        if (v <= 0) return;
        var pen = new Pen(FillBrush, StrokeWidth, lineCap: PenLineCap.Round);
        if (v >= 0.999) { dc.DrawEllipse(null, pen, c, r, r); return; }
        double a0 = -Math.PI / 2, a1 = a0 + v * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0)), false);
            ctx.ArcTo(new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1)), new Size(r, r), 0, v > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>
/// 30-day stability timeline (Windows StabilityChart): area + line of the daily value, one dot per crash day
/// (red = severe, amber = other), date labels every 7 days + today, tooltip per day.
/// </summary>
public sealed class StabilityChart : Control
{
    public static readonly StyledProperty<List<StabilityDay>?> DaysProperty = AvaloniaProperty.Register<StabilityChart, List<StabilityDay>?>(nameof(Days));
    public static readonly StyledProperty<IBrush?> LineBrushProperty = AvaloniaProperty.Register<StabilityChart, IBrush?>(nameof(LineBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<StabilityChart, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<StabilityChart, IBrush?>(nameof(LabelBrush));
    public static readonly StyledProperty<IBrush?> SevereBrushProperty = AvaloniaProperty.Register<StabilityChart, IBrush?>(nameof(SevereBrush));
    public static readonly StyledProperty<IBrush?> WarnBrushProperty = AvaloniaProperty.Register<StabilityChart, IBrush?>(nameof(WarnBrush));

    static StabilityChart() => AffectsRender<StabilityChart>(DaysProperty, LineBrushProperty, GridBrushProperty, LabelBrushProperty, SevereBrushProperty, WarnBrushProperty);

    public List<StabilityDay>? Days { get => GetValue(DaysProperty); set => SetValue(DaysProperty, value); }
    public IBrush? LineBrush { get => GetValue(LineBrushProperty); set => SetValue(LineBrushProperty, value); }
    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }
    public IBrush? SevereBrush { get => GetValue(SevereBrushProperty); set => SetValue(SevereBrushProperty, value); }
    public IBrush? WarnBrush { get => GetValue(WarnBrushProperty); set => SetValue(WarnBrushProperty, value); }

    const double LabelBand = 20, TopPad = 8;

    List<StabilityDay> D => Days ?? new List<StabilityDay>();
    double X(int i, double w) => 6 + i * (w - 12) / Math.Max(1, D.Count - 1);
    static double Y(double score, double h) => TopPad + (1 - score / 100.0) * (h - LabelBand - TopPad);

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        dc.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h)); // hit-testable for the tooltip
        var days = D;
        if (w <= 20 || h <= 40 || days.Count == 0) return;

        var grid = new Pen(GridBrush, 1);
        foreach (var s in new[] { 100.0, 75.0, 50.0 })
        {
            double y = Math.Round(Y(s, h)) + 0.5;
            dc.DrawLine(grid, new Point(0, y), new Point(w, y));
        }

        var line = new StreamGeometry();
        var area = new StreamGeometry();
        double baseY = h - LabelBand;
        using (var lc = line.Open())
        {
            lc.BeginFigure(new Point(X(0, w), Y(days[0].Score, h)), false);
            for (int i = 1; i < days.Count; i++) lc.LineTo(new Point(X(i, w), Y(days[i].Score, h)));
            lc.EndFigure(false);
        }
        using (var ac = area.Open())
        {
            ac.BeginFigure(new Point(X(0, w), baseY), true);
            for (int i = 0; i < days.Count; i++) ac.LineTo(new Point(X(i, w), Y(days[i].Score, h)));
            ac.LineTo(new Point(X(days.Count - 1, w), baseY));
            ac.EndFigure(true);
        }
        var lineColor = (LineBrush as ISolidColorBrush)?.Color ?? Colors.SlateBlue;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(70, lineColor.R, lineColor.G, lineColor.B), 0), new GradientStop(Color.FromArgb(0, lineColor.R, lineColor.G, lineColor.B), 1) },
        };
        dc.DrawGeometry(fill, null, area);
        dc.DrawGeometry(null, new Pen(LineBrush, 2, lineJoin: PenLineJoin.Round), line);

        for (int i = 0; i < days.Count; i++)
        {
            var evs = days[i].Events?.Where(e => Stability.IsMarkerWorthy(e.Category)).ToList();
            if (evs is null || evs.Count == 0) continue;
            bool severe = evs.Any(e => Stability.IsSevere(e.Category));
            var b = severe ? SevereBrush : WarnBrush;
            var p = new Point(X(i, w), Y(days[i].Score, h));
            double r = severe ? 5.5 : 4.5;
            var bc = (b as ISolidColorBrush)?.Color ?? Colors.OrangeRed;
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(55, bc.R, bc.G, bc.B)), null, p, r + 4, r + 4);
            dc.DrawEllipse(b, null, p, r, r);
        }

        for (int i = 0; i < days.Count - 4; i += 7) DrawLabel(dc, Loc.DayMonth(days[i].Date), X(i, w), h, i == 0 ? 0 : 0.5);
        DrawLabel(dc, Loc.S("charts.today"), X(days.Count - 1, w), h, 1);
    }

    void DrawLabel(DrawingContext dc, string text, double x, double h, double align)
    {
        var ft = new FormattedText(text, Loc.Culture, FlowDirection.LeftToRight, Typeface.Default, 11, LabelBrush);
        double left = Math.Max(0, Math.Min(Bounds.Width - ft.Width, x - ft.Width * align));
        dc.DrawText(ft, new Point(left, h - ft.Height));
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var days = D;
        if (days.Count == 0 || Bounds.Width <= 12) return;
        double x = e.GetPosition(this).X;
        int i = Math.Clamp((int)Math.Round((x - 6) / ((Bounds.Width - 12) / Math.Max(1, days.Count - 1))), 0, days.Count - 1);
        var d = days[i];
        int n = d.Events?.Count ?? 0;
        string head = d.Date.ToString("dddd d MMMM", Loc.Culture);
        string body = n == 0 ? Loc.S("charts.no_crash")
            : string.Join("\n", d.Events!.Take(4).Select(ev => "• " + CrashTimeline.CategoryName(ev.Category) + "  " + ev.Timestamp.ToString("HH:mm")));
        ToolTip.SetTip(this, head + "\n" + body);
        ToolTip.SetIsOpen(this, true);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ToolTip.SetIsOpen(this, false);
    }
}
