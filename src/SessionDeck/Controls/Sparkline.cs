using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ClaudeSessions;

/// <param name="X">0..1 across the span.</param>
/// <param name="Y">0..1, where 1 is 100 %.</param>
/// <param name="Gap">No line from the previous point (the app wasn't running).</param>
public sealed record SparkPoint(double X, double Y, bool Gap);

/// <summary>A tiny line chart: a faint 100 % line, a soft fill under the line, and the line itself.</summary>
public sealed class Sparkline : FrameworkElement
{
    static readonly Pen Guide = Frozen(new Pen(new SolidColorBrush(Color.FromRgb(0x33, 0x36, 0x3D)), 1) { DashStyle = DashStyles.Dash });

    IReadOnlyList<SparkPoint> points = new List<SparkPoint>();
    Brush stroke = Brushes.White;

    public void Show(IReadOnlyList<SparkPoint> points, Brush stroke)
    {
        this.points = points;
        this.stroke = stroke;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-testable for the tooltip
        dc.DrawLine(Guide, new Point(0, 0.5), new Point(w, 0.5));
        dc.DrawLine(Guide, new Point(0, h - 0.5), new Point(w, h - 0.5));
        if (points.Count == 0) return;

        Point At(SparkPoint p) => new(p.X * w, h - p.Y * (h - 1) - 0.5);
        var line = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var l = line.Open())
        using (var f = fill.Open())
        {
            for (int i = 0; i < points.Count; i++)
            {
                var pt = At(points[i]);
                bool start = i == 0 || points[i].Gap;
                if (start)
                {
                    if (i > 0) f.LineTo(new Point(At(points[i - 1]).X, h), false, false);
                    l.BeginFigure(pt, false, false);
                    f.BeginFigure(new Point(pt.X, h), true, true);
                    f.LineTo(pt, false, false);
                }
                else
                {
                    l.LineTo(pt, true, true);
                    f.LineTo(pt, false, false);
                }
            }
            f.LineTo(new Point(At(points[^1]).X, h), false, false);
        }
        line.Freeze();
        fill.Freeze();

        var area = stroke.Clone();
        area.Opacity = 0.18;
        dc.DrawGeometry(area, null, fill);
        dc.DrawGeometry(null, new Pen(stroke, 1.5) { LineJoin = PenLineJoin.Round }, line);
        if (points.Count == 1 || points[^1].Gap) dc.DrawEllipse(stroke, null, At(points[^1]), 1.8, 1.8);
    }

    static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
