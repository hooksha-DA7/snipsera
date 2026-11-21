using System;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Framelet;

internal static class EditInteraction
{
    public static Point[] Corners(Rect r) => [r.TopLeft, r.TopRight, r.BottomRight, r.BottomLeft];
    public static Point[] Handles(Mark mark)
    {
        if (mark.Kind is MarkKind.Arrow or MarkKind.Line)
            return mark.Arrow == ArrowStyle.Curved ? [mark.Points[0].Point, mark.Points[1].Point, ArrowDrawing.BendHandle(mark)] : [mark.Points[0].Point, mark.Points[^1].Point];
        return Corners(mark.Bounds);
    }
    public static int HitMark(EditState state, Point point, double tolerance)
    {
        for (int i = state.Marks.Length - 1; i >= 0; i--)
        {
            var mark = state.Marks[i]; var bounds = mark.Bounds; bounds.Inflate(tolerance, tolerance);
            if (!bounds.Contains(point)) continue;
            if (mark.Kind is MarkKind.Text or MarkKind.Counter or MarkKind.Highlight or MarkKind.Blur or MarkKind.Pixelate or MarkKind.Redact) return i;
            var drawing = new DrawingGroup(); using (var dc = drawing.Open()) EditRenderer.DrawMark(dc, mark);
            if (HitDrawing(drawing, point, tolerance)) return i;
        }
        return -1;
    }
    private static bool HitDrawing(Drawing drawing, Point point, double tolerance)
    {
        if (drawing is DrawingGroup group) return group.Children.Any(child => HitDrawing(child, point, tolerance));
        if (drawing is not GeometryDrawing geometry) return drawing.Bounds.Contains(point);
        if (geometry.Brush is not null && geometry.Geometry.FillContains(point)) return true;
        return geometry.Geometry.StrokeContains(new Pen(Brushes.Black, Math.Max(tolerance * 2, geometry.Pen?.Thickness ?? 0)), point);
    }
    public static Mark Move(Mark mark, Vector delta, Rect area)
    {
        double left = mark.Points.Min(p => p.X), right = mark.Points.Max(p => p.X), top = mark.Points.Min(p => p.Y), bottom = mark.Points.Max(p => p.Y);
        return mark.Offset(Math.Clamp(delta.X, area.Left - left, area.Right - right), Math.Clamp(delta.Y, area.Top - top, area.Bottom - bottom));
    }
    public static Mark Resize(Mark mark, int handle, Point point)
    {
        if (mark.Kind is MarkKind.Arrow or MarkKind.Line)
        {
            if (handle == 2) return mark with { Bend = ArrowDrawing.BendAt(mark, point) };
            var changed = mark with { Points = mark.Points.SetItem(handle == 0 ? 0 : mark.Points.Length - 1, new(point.X, point.Y)) };
            return (changed.Points[^1].Point - changed.Points[0].Point).Length >= 2 ? changed : mark;
        }
        var bounds = mark.Bounds; var anchor = Corners(bounds)[(handle + 2) % 4];
        var next = new Rect(anchor, point);
        if (next.Width < 2 || next.Height < 2 || bounds.Width < .01 || bounds.Height < .01) return mark;
        double sx = next.Width / bounds.Width, sy = next.Height / bounds.Height;
        if (mark.Kind is MarkKind.Text or MarkKind.Counter)
        {
            double size = Math.Clamp(mark.FontSize * Math.Max(sx, sy), 8, 200);
            var scaled = mark with { FontSize = size };
            var location = handle switch { 0 => anchor - new Vector(scaled.Bounds.Width, scaled.Bounds.Height), 1 => new Point(anchor.X, anchor.Y - scaled.Bounds.Height), 2 => anchor, _ => new Point(anchor.X - scaled.Bounds.Width, anchor.Y) };
            return scaled.Offset(location.X - scaled.Bounds.X, location.Y - scaled.Bounds.Y);
        }
        return mark with { Points = mark.Points.Select(p => new EditPoint(next.X + (p.X - bounds.X) * sx, next.Y + (p.Y - bounds.Y) * sy)).ToImmutableArray() };
    }
    public static ImageLayer ResizeImage(ImageLayer image, int handle, Point point)
    {
        var anchor = Corners(new Rect(image.X, image.Y, image.Width, image.Height))[(handle + 2) % 4];
        double ratio = image.Width / image.Height;
        double maximum = Math.Min(20000, 20000 * ratio);
        double width = Math.Clamp(Math.Max(Math.Abs(point.X - anchor.X), Math.Abs(point.Y - anchor.Y) * ratio), Math.Min(8, maximum), maximum);
        double height = width / ratio;
        return image with { X = handle is 0 or 3 ? anchor.X - width : anchor.X, Y = handle is 0 or 1 ? anchor.Y - height : anchor.Y, Width = width, Height = height };
    }
}
