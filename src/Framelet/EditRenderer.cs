using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public static class EditRenderer
{
    // Weak document ownership releases cached masks when an editor is discarded.
    // A bounded LRU avoids retaining a separate full canvas for every undo state.
    internal const long ObscureCacheByteLimit = 32L * 1024 * 1024;
    private static readonly ConditionalWeakTable<EditDocument, ObscureCache> obscureCaches = new();
    private static ObscureCache CacheFor(EditDocument document) => obscureCaches.GetValue(document, static _ => new ObscureCache());
    internal static (int Entries, long Bytes, long Hits, long Misses) ObscureCacheMetrics(EditDocument document) => CacheFor(document).Metrics;

    public static BitmapSource Render(EditDocument document, int replacementIndex = -1, Mark? replacement = null, EditState? stateOverride = null)
    {
        var state = stateOverride ?? document.State;
        var output = new DrawingVisual();
        using (var dc = output.RenderOpen()) DrawDocument(dc, document, state, replacementIndex, replacement);
        return Rasterize(output, state.CanvasWidth, state.CanvasHeight);
    }

    // Drawing directly avoids allocating a full-size bitmap on every pointer movement.
    public static void DrawDocument(DrawingContext dc, EditDocument document, EditState? stateOverride = null,
        int replacementIndex = -1, Mark? replacement = null, EditState? layoutState = null)
    {
        var state = stateOverride ?? document.State;
        var layout = layoutState ?? state;
        var frame = CompositionLayout.Calculate(layout);
        var contentBounds = layout.ContentBounds;
        var originalBounds = new Rect(0, 0, document.Original.PixelWidth, document.Original.PixelHeight);
        dc.DrawRectangle(CompositionLayout.BackgroundBrush(state), null, new Rect(0, 0, frame.Width, frame.Height));
        if (state.Backdrop?.Shadow == true)
        {
            DrawShadow(dc, Map(state.Crop, layout), state.Backdrop.CornerRadius);
            foreach (var layer in state.Images) DrawShadow(dc, Map(layer.Bounds, layout), state.Backdrop.CornerRadius);
        }
        var drawing = new DrawingGroup();
        using (var imageDrawing = drawing.Open())
        {
            double radius = (state.Backdrop?.CornerRadius ?? 0) / Math.Max(.01, state.ScaleX);
            imageDrawing.PushClip(new RectangleGeometry(state.IsComposition || radius > 0 ? state.Crop : originalBounds, radius, radius));
            imageDrawing.DrawImage(document.Original, originalBounds);
            imageDrawing.Pop();
            foreach (var layer in state.Images)
            {
                imageDrawing.PushClip(new RectangleGeometry(layer.Bounds, radius, radius));
                imageDrawing.DrawImage(document.GetImage(layer.Id), layer.Bounds);
                imageDrawing.Pop();
            }
        }
        for (int index = 0; index < state.Marks.Length; index++)
        {
            var mark = index == replacementIndex && replacement is not null ? replacement : state.Marks[index];
            if (mark.Kind is MarkKind.Blur or MarkKind.Pixelate)
            {
                Rect regionBounds = mark.Bounds; regionBounds.Intersect(state.IsComposition ? contentBounds : originalBounds);
                if (regionBounds.IsEmpty || regionBounds.Width < 1 || regionBounds.Height < 1) continue;
                int x = (int)Math.Floor(regionBounds.X), y = (int)Math.Floor(regionBounds.Y);
                int width = (int)Math.Ceiling(regionBounds.Right) - x, height = (int)Math.Ceiling(regionBounds.Bottom) - y;
                var rectangle = new Rect(x, y, width, height);
                double radius = (state.Backdrop?.CornerRadius ?? 0) / Math.Max(.01, state.ScaleX);
                var prefix = state.Marks.Take(index).ToImmutableArray();
                if (replacement is not null && replacementIndex >= 0 && replacementIndex < index) prefix = prefix.SetItem(replacementIndex, replacement);
                var key = new ObscureKey(rectangle, mark.Kind == MarkKind.Pixelate,
                    state.IsComposition || radius > 0 ? state.Crop : originalBounds,
                    state.IsComposition ? contentBounds : originalBounds, radius, state.Images, prefix);
                var cache = CacheFor(document);
                if (!cache.TryGet(key, out var obscured))
                {
                    var region = new DrawingVisual();
                    using (var regionDc = region.RenderOpen()) { regionDc.PushTransform(new TranslateTransform(-x, -y)); regionDc.DrawDrawing(drawing); }
                    var bitmap = Rasterize(region, width, height);
                    obscured = Obscure(bitmap, mark.Kind == MarkKind.Pixelate); cache.Add(key, obscured);
                }
                drawing.Children.Add(new ImageDrawing(obscured, rectangle));
            }
            else
            {
                var visual = new DrawingVisual(); using (var markDc = visual.RenderOpen()) DrawMark(markDc, mark);
                drawing.Children.Add(visual.Drawing);
            }
        }
        dc.PushClip(new RectangleGeometry(layoutState is null ? frame.Content : new Rect(0, 0, frame.Width, frame.Height)));
        dc.PushTransform(new TranslateTransform(frame.Content.X, frame.Content.Y));
        dc.PushTransform(new ScaleTransform(layout.ScaleX, layout.ScaleY));
        dc.PushTransform(new TranslateTransform(-contentBounds.X, -contentBounds.Y));
        dc.DrawDrawing(drawing);
        dc.Pop(); dc.Pop(); dc.Pop(); dc.Pop();
    }

    private sealed record ObscureKey(Rect Region, bool Pixelate, Rect OriginalClip, Rect EffectClip, double Radius,
        ImmutableArray<ImageLayer> Images, ImmutableArray<Mark> Prefix)
    {
        public bool Matches(ObscureKey other) => Region == other.Region && Pixelate == other.Pixelate &&
            OriginalClip == other.OriginalClip && EffectClip == other.EffectClip && Radius == other.Radius &&
            Images.SequenceEqual(other.Images) && Prefix.SequenceEqual(other.Prefix);
    }
    private sealed class ObscureCache
    {
        private sealed record Entry(ObscureKey Key, BitmapSource Bitmap, long Bytes);
        private readonly LinkedList<Entry> entries = new();
        private readonly object gate = new();
        private long bytes, hits, misses;
        public (int Entries, long Bytes, long Hits, long Misses) Metrics { get { lock (gate) return (entries.Count, bytes, hits, misses); } }
        public bool TryGet(ObscureKey key, out BitmapSource bitmap)
        {
            lock (gate)
            {
                for (var node = entries.First; node is not null; node = node.Next)
                {
                    if (!node.Value.Key.Matches(key)) continue;
                    bitmap = node.Value.Bitmap; entries.Remove(node); entries.AddFirst(node); hits++; return true;
                }
                misses++; bitmap = null!; return false;
            }
        }
        public void Add(ObscureKey key, BitmapSource bitmap)
        {
            long size = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
            if (size > ObscureCacheByteLimit) return;
            lock (gate)
            {
                // Concurrent detached renders can finish the same mask independently.
                for (var node = entries.First; node is not null; node = node.Next)
                    if (node.Value.Key.Matches(key)) return;
                while (entries.Count >= 8 || bytes + size > ObscureCacheByteLimit)
                { bytes -= entries.Last!.Value.Bytes; entries.RemoveLast(); }
                entries.AddFirst(new Entry(key, bitmap, size)); bytes += size;
            }
        }
    }

    private static Rect Map(Rect rectangle, EditState state) => new(state.ToCanvas(rectangle.TopLeft),
        new Size(rectangle.Width * state.ScaleX, rectangle.Height * state.ScaleY));

    private static void DrawShadow(DrawingContext dc, Rect rectangle, double radius)
    {
        for (int i = 8; i >= 1; i--)
        {
            Rect shadow = rectangle; shadow.Inflate(i, i); shadow.Offset(0, 3);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(5, 0, 0, 0)), null, shadow, radius + i, radius + i);
        }
    }

    private static RenderTargetBitmap Rasterize(Visual visual, int width, int height)
    { var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap; }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    public static void DrawMark(DrawingContext dc, Mark mark)
    {
        Point start = mark.Points[0].Point, end = mark.Points[^1].Point;
        var bounds = mark.Bounds;
        var brush = Brush(mark.Color);
        var pen = new Pen(brush, mark.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        switch (mark.Kind)
        {
            case MarkKind.Line: dc.DrawLine(pen, start, end); break;
            case MarkKind.Arrow:
                if (mark.Arrow is not null) { ArrowDrawing.Draw(dc, mark); break; }
                if (mark.Style == MarkStyle.Sketch) { SketchArrow(dc, pen, start, end); break; }
                dc.DrawLine(pen, start, end);
                Vector direction = end - start;
                if (direction.Length < 1) break;
                double length = Math.Min(direction.Length, Math.Max(12, mark.Width * 4)); direction.Normalize();
                Vector side = new(-direction.Y, direction.X);
                var arrow = new StreamGeometry();
                using (var path = arrow.Open()) { path.BeginFigure(end, true, true); path.LineTo(end - direction * length + side * length * 0.45, true, false); path.LineTo(end - direction * length - side * length * 0.45, true, false); }
                dc.DrawGeometry(brush, null, arrow); break;
            case MarkKind.Rectangle: dc.DrawRectangle(null, pen, bounds); break;
            case MarkKind.Ellipse:
                if (mark.Style == MarkStyle.Sketch) SketchEllipse(dc, pen, bounds);
                else dc.DrawEllipse(null, pen, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2);
                break;
            case MarkKind.Highlight: brush.Opacity = 0.35; dc.DrawRectangle(brush, null, bounds); break;
            case MarkKind.Redact: dc.DrawRectangle(Brushes.Black, null, bounds); break;
            case MarkKind.Text: TextAnnotation.Draw(dc, mark); break;
            case MarkKind.Counter:
                dc.DrawEllipse(brush, null, start, mark.FontSize, mark.FontSize);
                var number = Text(mark.Number.ToString(CultureInfo.InvariantCulture), mark.FontSize, Brushes.White);
                dc.DrawText(number, new Point(start.X - number.Width / 2, start.Y - number.Height / 2)); break;
            case MarkKind.Pen:
                if (mark.Points.Length == 1) { dc.DrawEllipse(brush, null, start, mark.Width / 2, mark.Width / 2); break; }
                var geometry = new StreamGeometry();
                using (var path = geometry.Open())
                {
                    path.BeginFigure(start, false, false);
                    if (!mark.Smooth) path.PolyLineTo(mark.Points.Skip(1).Select(p => p.Point).ToArray(), true, false);
                    else for (int i = 0; i < mark.Points.Length - 1; i++)
                    {
                        Point a = mark.Points[Math.Max(0, i - 1)].Point, b = mark.Points[i].Point,
                            c = mark.Points[i + 1].Point, d = mark.Points[Math.Min(mark.Points.Length - 1, i + 2)].Point;
                        path.BezierTo(b + (c - a) / 6, c - (d - b) / 6, c, true, true);
                    }
                }
                dc.DrawGeometry(null, pen, geometry); break;
            default: dc.DrawRectangle(Ui.Line, new Pen(Ui.Blue, 2), bounds); break;
        }
    }

    private static void SketchArrow(DrawingContext dc, Pen pen, Point start, Point end)
    {
        Vector span = end - start;
        if (span.Length < 1) return;
        double distance = span.Length; Vector direction = span; direction.Normalize();
        Vector normal = new(-direction.Y, direction.X);
        double bow = Math.Min(50, distance * .18);
        Point control1 = start + span * .36 + normal * bow;
        Point control2 = start + span * .76 + normal * bow * .65;
        var curve = new StreamGeometry();
        using (var path = curve.Open()) { path.BeginFigure(start, false, false); path.BezierTo(control1, control2, end, true, true); }
        dc.DrawGeometry(null, pen, curve);
        Vector tangent = end - control2; tangent.Normalize(); Vector side = new(-tangent.Y, tangent.X);
        double length = Math.Min(distance * .35, Math.Max(14, pen.Thickness * 4.5));
        var head = new StreamGeometry();
        using (var path = head.Open()) { path.BeginFigure(end - tangent * length + side * length * .5, false, false); path.LineTo(end, true, true); path.LineTo(end - tangent * length - side * length * .5, true, true); }
        dc.DrawGeometry(null, pen, head);
    }
    private static void SketchEllipse(DrawingContext dc, Pen pen, Rect b)
    {
        Point At(double x, double y) => new(b.X + b.Width * x, b.Y + b.Height * y);
        var oval = new StreamGeometry();
        using (var path = oval.Open())
        {
            path.BeginFigure(At(.51, .015), false, true);
            path.BezierTo(At(.83, -.025), At(1.015, .20), At(.985, .50), true, true);
            path.BezierTo(At(1.01, .83), At(.79, 1.015), At(.47, .98), true, true);
            path.BezierTo(At(.17, 1.015), At(-.02, .78), At(.015, .47), true, true);
            path.BezierTo(At(-.005, .17), At(.22, -.01), At(.51, .015), true, true);
        }
        dc.DrawGeometry(null, pen, oval);
    }

    private static FormattedText Text(string value, double size, System.Windows.Media.Brush brush) =>
        new(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, brush, 1) { MaxTextWidth = 20000 };

    private static BitmapSource Obscure(BitmapSource image, bool pixelate)
    {
        int width = image.PixelWidth, height = image.PixelHeight, stride = width * 4;
        byte[] pixels = new byte[stride * height]; image.CopyPixels(pixels, stride, 0);
        if (pixelate)
        {
            const int block = 16;
            for (int y = 0; y < height; y += block) for (int x = 0; x < width; x += block)
            {
                int right = Math.Min(x + block, width), bottom = Math.Min(y + block, height), count = (right - x) * (bottom - y);
                long[] sum = new long[4];
                for (int row = y; row < bottom; row++) for (int col = x; col < right; col++) for (int c = 0; c < 4; c++) sum[c] += pixels[row * stride + col * 4 + c];
                for (int row = y; row < bottom; row++) for (int col = x; col < right; col++) for (int c = 0; c < 4; c++) pixels[row * stride + col * 4 + c] = (byte)(sum[c] / count);
            }
        }
        else
        {
            // Three separable box passes approximate a blur without GPU effects in export.
            for (int pass = 0; pass < 3; pass++)
            {
                pixels = BlurPass(pixels, width, height, true);
                pixels = BlurPass(pixels, width, height, false);
            }
        }
        var result = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride); result.Freeze(); return result;
    }
    private static byte[] BlurPass(byte[] source, int width, int height, bool horizontal)
    {
        const int radius = 7;
        byte[] output = new byte[source.Length];
        int lines = horizontal ? height : width, length = horizontal ? width : height;
        int step = horizontal ? 4 : width * 4;
        for (int line = 0; line < lines; line++) for (int c = 0; c < 4; c++)
        {
            int offset = (horizontal ? line * width * 4 : line * 4) + c;
            int sum = 0, first = 0, last = -1;
            for (int p = 0; p < length; p++)
            {
                int right = Math.Min(length - 1, p + radius), left = Math.Max(0, p - radius);
                while (last < right) { last++; sum += source[offset + last * step]; }
                while (first < left) { sum -= source[offset + first * step]; first++; }
                output[offset + p * step] = (byte)(sum / (last - first + 1));
            }
        }
        return output;
    }
}
