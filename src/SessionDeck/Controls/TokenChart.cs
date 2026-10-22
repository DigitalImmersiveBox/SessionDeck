using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeSessions;

public enum TokenChartKind { Donut, Bars, Treemap, Timeline }

/// <summary>One coloured entity in the token chart and its legend: a project, a session, or "Other".</summary>
public sealed class TokenSlice
{
    public TokenSlice(string key, string name, string? detail, long value, long[] buckets, object? tag)
    {
        Key = key;
        Name = name;
        Detail = detail;
        Value = value;
        Buckets = buckets;
        Tag = tag;
    }

    public string Key { get; }
    public string Name { get; }
    /// <summary>The project of a session, or how many sessions a project or "Other" holds.</summary>
    public string? Detail { get; }
    public long Value { get; }
    public long[] Buckets { get; }
    /// <summary>A project path or a <see cref="TokenEntry"/>; null for "Other", which can't be clicked.</summary>
    public object? Tag { get; }
    public double Share { get; set; }
    public Brush Brush { get; set; } = Brushes.Gray;
    /// <summary>Text drawn on the slice's own colour (treemap tiles).</summary>
    public Brush OnBrush { get; set; } = Brushes.Black;

    public string ValueText => Tokens.Short(Value);
    public string ShareText => TokenChart.Percent(Share);
    public string Tooltip => Detail != null ? $"{Name}\n{Detail}" : Name;
}

/// <summary>
/// Token use drawn four ways: a donut, ranked bars, a treemap, or stacked columns over time.
/// Hovering a mark dims the others and reports a line of detail; clicking one raises <see cref="SliceClicked"/>.
/// </summary>
public sealed class TokenChart : FrameworkElement
{
    static readonly Brush Primary = Ink.Of(0xE6, 0xE7, 0xEA);
    static readonly Brush Secondary = Ink.Of(0xA9, 0xAC, 0xB4);
    static readonly Brush Muted = Ink.Of(0x7D, 0x80, 0x8A);
    static readonly Brush HoverRow = Ink.Of(0x1D, 0x1F, 0x24);
    static readonly Pen GridPen = Frozen(new Pen(Ink.Of(0x26, 0x29, 0x2F), 1));
    static readonly Pen AxisPen = Frozen(new Pen(Ink.Of(0x38, 0x3B, 0x42), 1));
    static readonly Typeface Regular = new("Segoe UI");
    static readonly Typeface Semibold = new(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    static readonly Typeface Light = new("Segoe UI Light");

    /// <summary>The background the chart sits on: the 2 px gaps between marks are drawn in it.</summary>
    public static readonly Brush Surface = Ink.Of(0x14, 0x15, 0x18);
    static readonly Pen GapPen = Frozen(new Pen(Surface, 2));

    TokenChartKind kind;
    IReadOnlyList<TokenSlice> slices = Array.Empty<TokenSlice>();
    long total;
    DateTime from;
    TimeSpan step;
    int bucketCount;
    bool longRange;

    readonly List<(Geometry Area, TokenSlice? Slice, int Bucket)> hits = new();
    TokenSlice? hover;
    int hoverBucket = -1;

    /// <summary>A line about what's under the pointer; null when it leaves.</summary>
    public event Action<string?>? HoverTextChanged;
    public event Action<TokenSlice>? SliceClicked;

    public void Show(TokenChartKind kind, IReadOnlyList<TokenSlice> slices, long total, TokenStats stats)
    {
        this.kind = kind;
        this.slices = slices;
        this.total = total;
        from = stats.From;
        step = stats.Step;
        bucketCount = slices.FirstOrDefault()?.Buckets.Length ?? 0;
        longRange = stats.Window > TimeSpan.FromHours(24);
        if (hover != null) hover = slices.FirstOrDefault(s => s.Key == hover.Key);
        InvalidateVisual();
    }

    /// <summary>The slice drawn at full strength while the others dim (set from the legend too).</summary>
    public TokenSlice? Highlight
    {
        get => hover;
        set
        {
            if (hover == value && hoverBucket < 0) return;
            hover = value;
            hoverBucket = -1;
            InvalidateVisual();
        }
    }

    // ---- Pointer -------------------------------------------------------------

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);
        (Geometry, TokenSlice?, int)? hit = null;
        for (int i = hits.Count - 1; i >= 0; i--)
            if (hits[i].Area.FillContains(p)) { hit = hits[i]; break; }
        var slice = hit?.Item2;
        int bucket = hit?.Item3 ?? -1;
        Cursor = slice?.Tag != null ? Cursors.Hand : null;
        if (slice == hover && bucket == hoverBucket) return;
        hover = slice;
        hoverBucket = bucket;
        InvalidateVisual();
        HoverTextChanged?.Invoke(hit == null ? null : Describe(slice, bucket));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        Cursor = null;
        if (hover == null && hoverBucket < 0) return;
        hover = null;
        hoverBucket = -1;
        InvalidateVisual();
        HoverTextChanged?.Invoke(null);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (hover is { Tag: not null } slice) SliceClicked?.Invoke(slice);
    }

    public string Describe(TokenSlice? slice, int bucket)
    {
        if (bucket >= 0)
        {
            long column = slices.Sum(s => s.Buckets[bucket]);
            string when = BucketText(bucket);
            if (slice != null) return $"{when}  ·  {slice.Name}: {Tokens.Short(slice.Buckets[bucket])} of {Tokens.Short(column)}";
            var top = slices.Where(s => s.Buckets[bucket] > 0).OrderByDescending(s => s.Buckets[bucket]).Take(3)
                .Select(s => $"{s.Name} {Tokens.Short(s.Buckets[bucket])}");
            return $"{when}  ·  {Tokens.Short(column)} tokens" + (column > 0 ? "  ·  " + string.Join(", ", top) : "");
        }
        if (slice == null) return "";
        return $"{slice.Name}  ·  {Tokens.Short(slice.Value)} tokens, {Percent(slice.Share)}"
               + (slice.Detail != null ? $"  ·  {slice.Detail}" : "");
    }

    string BucketText(int bucket)
    {
        var start = (from + step * bucket).ToLocalTime();
        var end = start + step;
        string format = longRange ? "ddd HH:mm" : "HH:mm";
        return $"{start.ToString(format, CultureInfo.CurrentCulture)}–{end:HH:mm}";
    }

    public static string Percent(double share) =>
        share >= 0.995 ? "100 %" : share < 0.01 ? "<1 %" : $"{share * 100:0} %";

    // ---- Drawing -------------------------------------------------------------

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        hits.Clear();
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h)); // hit-testable everywhere
        if (total <= 0 || slices.Count == 0) return;
        switch (kind)
        {
            case TokenChartKind.Donut: DrawDonut(dc, w, h); break;
            case TokenChartKind.Bars: DrawBars(dc, w, h); break;
            case TokenChartKind.Treemap: DrawTreemap(dc, w, h); break;
            case TokenChartKind.Timeline: DrawTimeline(dc, w, h); break;
        }
    }

    Brush Fill(TokenSlice s) => hover == null || hover == s ? s.Brush : Dimmed(s.Brush);

    static Brush Dimmed(Brush brush)
    {
        var dim = brush.Clone();
        dim.Opacity = 0.32;
        dim.Freeze();
        return dim;
    }

    void DrawDonut(DrawingContext dc, double w, double h)
    {
        var c = new Point(w / 2, h / 2);
        double outer = Math.Min(w, h) / 2 - 8, inner = outer * 0.62;
        if (inner < 20) return;
        double angle = -90;
        foreach (var s in slices)
        {
            double sweep = s.Share * 360;
            double r = s == hover ? outer + 5 : outer;
            Geometry area = sweep >= 359.9
                ? new CombinedGeometry(GeometryCombineMode.Exclude, new EllipseGeometry(c, r, r), new EllipseGeometry(c, inner, inner))
                : Segment(c, inner, r, angle, angle + sweep);
            area.Freeze();
            dc.DrawGeometry(Fill(s), sweep >= 359.9 ? null : GapPen, area);
            hits.Add((area, s, -1));
            angle += sweep;
        }

        // The centre: the total, or the slice under the pointer
        double room = inner * 1.6;
        var big = Text(hover != null ? hover.ValueText : Tokens.Short(total), 30, Primary, Light, room);
        var small = Text(hover != null ? hover.Name : "tokens", 12, Secondary, hover != null ? Semibold : Regular, room);
        var note = hover != null ? Text(Percent(hover.Share), 11.5, Muted, Regular, room) : null;
        double blockH = big.Height + small.Height + (note?.Height ?? 0);
        double y = c.Y - blockH / 2;
        dc.DrawText(small, new Point(c.X - small.Width / 2, y));
        y += small.Height;
        dc.DrawText(big, new Point(c.X - big.Width / 2, y - 2));
        y += big.Height;
        if (note != null) dc.DrawText(note, new Point(c.X - note.Width / 2, y - 4));
    }

    static Geometry Segment(Point c, double r, double outer, double a0, double a1)
    {
        Point At(double radius, double deg) =>
            new(c.X + radius * Math.Cos(deg * Math.PI / 180), c.Y + radius * Math.Sin(deg * Math.PI / 180));
        bool large = a1 - a0 > 180;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(At(outer, a0), true, true);
            ctx.ArcTo(At(outer, a1), new Size(outer, outer), 0, large, SweepDirection.Clockwise, true, false);
            ctx.LineTo(At(r, a1), true, false);
            ctx.ArcTo(At(r, a0), new Size(r, r), 0, large, SweepDirection.Counterclockwise, true, false);
        }
        return g;
    }

    void DrawBars(DrawingContext dc, double w, double h)
    {
        double rowH = Math.Min(40, h / slices.Count);
        double labelW = Math.Clamp(w * 0.32, 90, 240), valueW = 92;
        double barRoom = Math.Max(10, w - labelW - valueW - 8);
        long max = slices.Max(s => s.Value);
        for (int i = 0; i < slices.Count; i++)
        {
            var s = slices[i];
            var row = new Rect(0, i * rowH, w, rowH);
            if (s == hover) dc.DrawRoundedRectangle(HoverRow, null, row, 6, 6);
            hits.Add((new RectangleGeometry(row), s, -1));

            // Name, and under it (when there's room) the project or session count
            var name = Text(s.Name, 12, s == hover ? Primary : Secondary, s == hover ? Semibold : Regular, labelW - 18);
            var detail = s.Detail != null && rowH >= 32 ? Text(s.Detail, 10.5, Muted, Regular, labelW - 18) : null;
            double textH = name.Height + (detail?.Height ?? 0);
            double ty = row.Y + (rowH - textH) / 2;
            dc.DrawText(name, new Point(8, ty));
            if (detail != null) dc.DrawText(detail, new Point(8, ty + name.Height - 1));

            double thick = Math.Min(14, rowH * 0.5);
            double len = Math.Max(3, barRoom * s.Value / max);
            var bar = new Rect(labelW, row.Y + (rowH - thick) / 2, len, thick);
            var fill = Fill(s);
            dc.DrawRoundedRectangle(fill, null, bar, Math.Min(4, len / 2), Math.Min(4, thick / 2)); // rounded data end…
            dc.DrawRectangle(fill, null, new Rect(bar.X, bar.Y, Math.Min(4, len / 2), thick));       // …square at the baseline

            var value = Text($"{s.ValueText}  {s.ShareText}", 11.5, s == hover ? Primary : Secondary, Regular, valueW);
            dc.DrawText(value, new Point(bar.Right + 8, row.Y + (rowH - value.Height) / 2));
        }
        dc.DrawLine(AxisPen, new Point(labelW - 0.5, 0), new Point(labelW - 0.5, slices.Count * rowH));
    }

    void DrawTreemap(DrawingContext dc, double w, double h)
    {
        var order = slices.Where(s => s.Value > 0).OrderByDescending(s => s.Value).ToList();
        var rects = Squarify(order.Select(s => (double)s.Value).ToArray(), new Rect(0, 0, w, h));
        for (int i = 0; i < order.Count; i++)
        {
            var s = order[i];
            var r = rects[i];
            r.Inflate(-1, -1); // the 2 px gap
            if (r.Width <= 0 || r.Height <= 0) continue;
            dc.DrawRoundedRectangle(Fill(s), null, r, 4, 4);
            hits.Add((new RectangleGeometry(r), s, -1));
            if (r.Width < 48 || r.Height < 24) continue;
            double room = r.Width - 16;
            var ink = hover == null || hover == s ? s.OnBrush : Muted;
            var name = Text(s.Name, 12, ink, Semibold, room);
            dc.DrawText(name, new Point(r.X + 8, r.Y + 6));
            if (r.Height < 44) continue;
            var value = Text($"{s.ValueText} · {s.ShareText}", 11.5, ink, Regular, room);
            dc.DrawText(value, new Point(r.X + 8, r.Y + 6 + name.Height));
            if (s.Detail != null && r.Height >= 66)
            {
                var detail = Text(s.Detail, 10.5, ink, Regular, room);
                dc.PushOpacity(0.75);
                dc.DrawText(detail, new Point(r.X + 8, r.Y + 8 + name.Height + value.Height));
                dc.Pop();
            }
        }
    }

    /// <summary>Squarified treemap (Bruls et al.): rows along the shorter side, values largest first.</summary>
    static Rect[] Squarify(double[] values, Rect bounds)
    {
        var rects = new Rect[values.Length];
        double sumAll = values.Sum();
        if (sumAll <= 0 || bounds.Width <= 0 || bounds.Height <= 0) return rects;
        double scale = bounds.Width * bounds.Height / sumAll;
        var areas = values.Select(v => v * scale).ToArray();
        var rest = bounds;
        int i = 0;
        while (i < areas.Length)
        {
            double side = Math.Min(rest.Width, rest.Height);
            int j = i;
            double sum = 0, worst = double.MaxValue;
            while (j < areas.Length)
            {
                double next = sum + areas[j];
                double ratio = Worst(areas, i, j, next, side);
                if (j > i && ratio > worst) break;
                worst = ratio;
                sum = next;
                j++;
            }
            double thickness = side > 0 ? sum / side : 0;
            bool column = rest.Width >= rest.Height; // a column on the left, else a row on top
            double offset = 0;
            for (int k = i; k < j; k++)
            {
                double len = thickness > 0 ? areas[k] / thickness : 0;
                rects[k] = column ? new Rect(rest.X, rest.Y + offset, thickness, len) : new Rect(rest.X + offset, rest.Y, len, thickness);
                offset += len;
            }
            rest = column
                ? new Rect(rest.X + thickness, rest.Y, Math.Max(0, rest.Width - thickness), rest.Height)
                : new Rect(rest.X, rest.Y + thickness, rest.Width, Math.Max(0, rest.Height - thickness));
            i = j;
        }
        return rects;
    }

    static double Worst(double[] areas, int from, int to, double sum, double side)
    {
        double max = 0, min = double.MaxValue;
        for (int k = from; k <= to; k++)
        {
            max = Math.Max(max, areas[k]);
            min = Math.Min(min, areas[k]);
        }
        double s2 = side * side, sum2 = sum * sum;
        return min <= 0 ? double.MaxValue : Math.Max(s2 * max / sum2, sum2 / (s2 * min));
    }

    void DrawTimeline(DrawingContext dc, double w, double h)
    {
        if (bucketCount == 0) return;
        const double axisW = 46, axisH = 22;
        var plot = new Rect(axisW, 8, Math.Max(1, w - axisW - 4), Math.Max(1, h - axisH - 8));
        var columns = Enumerable.Range(0, bucketCount).Select(b => slices.Sum(s => s.Buckets[b])).ToArray();
        double top = NiceCeiling(columns.Max());

        foreach (double f in new[] { 0, 0.5, 1 })
        {
            double y = Math.Round(plot.Bottom - f * plot.Height) + 0.5;
            dc.DrawLine(f == 0 ? AxisPen : GridPen, new Point(plot.X, y), new Point(plot.Right, y));
            var label = Text(Tokens.Short((long)(top * f)), 10.5, Muted, Regular, axisW - 8);
            dc.DrawText(label, new Point(axisW - 8 - label.Width, y - label.Height / 2));
        }

        double slot = plot.Width / bucketCount;
        double barW = Math.Max(1, slot * 0.72);
        for (int b = 0; b < bucketCount; b++)
        {
            double x = plot.X + b * slot + (slot - barW) / 2;
            hits.Add((new RectangleGeometry(new Rect(plot.X + b * slot, plot.Y, slot, plot.Height)), null, b));
            if (b == hoverBucket && hover == null)
                dc.DrawRectangle(HoverRow, null, new Rect(plot.X + b * slot, plot.Y, slot, plot.Height - 1));
            double y = plot.Bottom - 1;
            foreach (var s in slices)
            {
                double hgt = top > 0 ? s.Buckets[b] / top * plot.Height : 0;
                if (hgt <= 0) continue;
                var seg = new Rect(x, y - hgt, barW, hgt);
                // 1 px of surface between stacked segments, once a segment is tall enough to keep its colour
                var drawn = hgt > 3 ? new Rect(seg.X, seg.Y + 1, seg.Width, seg.Height - 1) : seg;
                dc.DrawRectangle(Fill(s), null, drawn);
                hits.Add((new RectangleGeometry(new Rect(plot.X + b * slot, seg.Y, slot, Math.Max(seg.Height, 3))), s, b));
                y -= hgt;
            }
        }

        // About six time labels along the bottom, on bucket starts
        int every = Math.Max(1, (int)Math.Ceiling(bucketCount / 6.0));
        string format = longRange ? "ddd HH:mm" : "HH:mm";
        for (int b = 0; b < bucketCount; b += every)
        {
            var label = Text((from + step * b).ToLocalTime().ToString(format, CultureInfo.CurrentCulture), 10.5, Muted, Regular, 120);
            double x = Math.Clamp(plot.X + b * slot - label.Width / 2, plot.X - 4, w - label.Width);
            dc.DrawText(label, new Point(x, plot.Bottom + 5));
        }
    }

    static double NiceCeiling(double max)
    {
        if (max <= 0) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(max)));
        foreach (double m in new[] { 1, 2, 2.5, 5, 10 })
            if (m * mag >= max) return m * mag;
        return 10 * mag;
    }

    FormattedText Text(string text, double size, Brush brush, Typeface face, double maxWidth)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, face, size, brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };
        return ft;
    }

    static Pen Frozen(Pen pen)
    {
        pen.Freeze();
        return pen;
    }
}
