using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Framelet;

public static class ArrowDrawing
{
    public static Point BendHandle(Mark mark)
    {
        Point a = mark.Points[0].Point, b = mark.Points[1].Point; Vector span = b - a;
        return a + span * .5 + new Vector(-span.Y, span.X) * mark.Bend;
    }
    public static double BendAt(Mark mark, Point point)
    {
        Vector span = mark.Points[1].Point - mark.Points[0].Point;
        if (span.LengthSquared < 1) return mark.Bend;
        Vector offset = point - (mark.Points[0].Point + span * .5);
        return Math.Clamp(Vector.Multiply(offset, new Vector(-span.Y, span.X)) / span.LengthSquared, -2, 2);
    }
    public static Rect Bounds(Mark mark)
    {
        var drawing = new DrawingGroup(); using (var dc = drawing.Open()) Draw(dc, mark);
        return drawing.Bounds;
    }
    public static void Draw(DrawingContext dc, Mark mark)
    {
        Point start = mark.Points[0].Point, end = mark.Points[1].Point; Vector span = end - start;
        double distance = span.Length; if (distance < .01) return;
        var brush = Ui.Brush(mark.Color);
        var pen = new Pen(brush, mark.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        Vector direction = span / distance, side = new(-direction.Y, direction.X);
        double head = Math.Min(distance * .42, Math.Max(14, mark.Width * 4.5));
        if (mark.Arrow == ArrowStyle.Classic)
        {
            Point shoulder = end - direction * head;
            var body = new StreamGeometry(); using (var path = body.Open())
            {
                path.BeginFigure(start - side * mark.Width * .08, true, true);
                path.LineTo(shoulder - side * mark.Width * .48, true, false);
                path.LineTo(shoulder - side * head * .48, true, false); path.LineTo(end, true, false);
                path.LineTo(shoulder + side * head * .48, true, false); path.LineTo(shoulder + side * mark.Width * .48, true, false);
                path.LineTo(start + side * mark.Width * .08, true, false);
            }
            dc.DrawGeometry(brush, null, body); return;
        }
        if (mark.Arrow == ArrowStyle.Curved)
        {
            Point middle = BendHandle(mark), control = middle + (middle - (start + span * .5));
            var curve = new StreamGeometry(); using (var path = curve.Open())
            { path.BeginFigure(start, false, false); path.QuadraticBezierTo(control, end, true, true); }
            dc.DrawGeometry(null, pen, curve);
            direction = end - control; if (direction.Length < .01) direction = span; direction.Normalize(); side = new(-direction.Y, direction.X);
        }
        else dc.DrawLine(pen, start, mark.Arrow == ArrowStyle.Filled ? end - direction * head * .4 : end);
        var tip = new StreamGeometry();
        using (var path = tip.Open())
        {
            path.BeginFigure(end - direction * head + side * head * .48, mark.Arrow == ArrowStyle.Filled, mark.Arrow == ArrowStyle.Filled);
            path.LineTo(end, true, true); path.LineTo(end - direction * head - side * head * .48, true, true);
        }
        dc.DrawGeometry(mark.Arrow == ArrowStyle.Filled ? brush : null, mark.Arrow == ArrowStyle.Filled ? null : pen, tip);
    }
}

public sealed class ArrowSample : FrameworkElement
{
    private readonly ArrowStyle style;
    public ArrowSample(ArrowStyle style)
    {
        this.style = style; Width = 46; Height = 22; IsHitTestVisible = false;
        Loaded += (_, _) => { Appearance.Current.Changed += InvalidateVisual; InvalidateVisual(); };
        Unloaded += (_, _) => Appearance.Current.Changed -= InvalidateVisual;
    }
    protected override void OnRender(DrawingContext dc)
    {
        var color = Appearance.Current.Ink;
        ArrowDrawing.Draw(dc, new Mark(MarkKind.Arrow, [new(4, 16), new(41, 6)], $"#{color.R:X2}{color.G:X2}{color.B:X2}", 2, Arrow: style, Bend: -.18));
    }
}
