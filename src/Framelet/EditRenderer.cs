using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public static class EditRenderer
{
    public static BitmapSource Render(EditDocument document)
    {
        var state = document.State;
        var drawing = new DrawingGroup();
        drawing.Children.Add(new ImageDrawing(document.Original, new Rect(0, 0, document.Original.PixelWidth, document.Original.PixelHeight)));
        foreach (var mark in state.Marks)
        {
            if (mark.Kind is MarkKind.Blur or MarkKind.Pixelate)
            {
                var bounds = PixelBounds(mark.Bounds, document.Original.PixelWidth, document.Original.PixelHeight);
                if (bounds.Width == 0 || bounds.Height == 0) continue;
                var region = new DrawingVisual();
                using (var dc = region.RenderOpen()) { dc.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y)); dc.DrawDrawing(drawing); }
                var bitmap = Rasterize(region, bounds.Width, bounds.Height);
                drawing.Children.Add(new ImageDrawing(Obscure(bitmap, mark.Kind == MarkKind.Pixelate), new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height)));
            }
            else
            {
                var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) DrawMark(dc, mark);
                drawing.Children.Add(visual.Drawing);
            }
        }
        var output = new DrawingVisual();
        using (var dc = output.RenderOpen())
        {
            if (state.Background != "Transparent") dc.DrawRectangle(Brush(state.Background), null, new Rect(0, 0, state.CanvasWidth, state.CanvasHeight));
            dc.PushClip(new RectangleGeometry(new Rect(state.Padding, state.Padding, state.Width, state.Height)));
            dc.PushTransform(new TranslateTransform(state.Padding, state.Padding));
            dc.PushTransform(new ScaleTransform((double)state.Width / state.CropWidth, (double)state.Height / state.CropHeight));
            dc.PushTransform(new TranslateTransform(-state.CropX, -state.CropY));
            dc.DrawDrawing(drawing);
        }
        return Rasterize(output, state.CanvasWidth, state.CanvasHeight);
    }

    private static RenderTargetBitmap Rasterize(Visual visual, int width, int height)
    { var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap; }
    private static SolidColorBrush Brush(string hex) => new((Color)ColorConverter.ConvertFromString(hex));
    private static Int32Rect PixelBounds(Rect bounds, int width, int height)
    {
        int x = (int)Math.Clamp(Math.Floor(bounds.Left), 0, width), y = (int)Math.Clamp(Math.Floor(bounds.Top), 0, height);
        int right = (int)Math.Clamp(Math.Ceiling(bounds.Right), x, width), bottom = (int)Math.Clamp(Math.Ceiling(bounds.Bottom), y, height);
        return new Int32Rect(x, y, right - x, bottom - y);
    }

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
                dc.DrawLine(pen, start, end);
                Vector direction = end - start;
                if (direction.Length < 1) break;
                double length = Math.Min(direction.Length, Math.Max(12, mark.Width * 4)); direction.Normalize();
                Vector side = new(-direction.Y, direction.X);
                var arrow = new StreamGeometry();
                using (var path = arrow.Open()) { path.BeginFigure(end, true, true); path.LineTo(end - direction * length + side * length * 0.45, true, false); path.LineTo(end - direction * length - side * length * 0.45, true, false); }
                dc.DrawGeometry(brush, null, arrow); break;
            case MarkKind.Rectangle: dc.DrawRectangle(null, pen, bounds); break;
            case MarkKind.Ellipse: dc.DrawEllipse(null, pen, new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2), bounds.Width / 2, bounds.Height / 2); break;
            case MarkKind.Highlight: brush.Opacity = 0.35; dc.DrawRectangle(brush, null, bounds); break;
            case MarkKind.Redact: dc.DrawRectangle(Brushes.Black, null, bounds); break;
            case MarkKind.Text: dc.DrawText(Text(mark.Text, mark.FontSize, brush), start); break;
            case MarkKind.Counter:
                dc.DrawEllipse(brush, null, start, mark.FontSize, mark.FontSize);
                var number = Text(mark.Number.ToString(CultureInfo.InvariantCulture), mark.FontSize, Brushes.White);
                dc.DrawText(number, new Point(start.X - number.Width / 2, start.Y - number.Height / 2)); break;
            case MarkKind.Pen:
                if (mark.Points.Length == 1) { dc.DrawEllipse(brush, null, start, mark.Width / 2, mark.Width / 2); break; }
                var geometry = new StreamGeometry();
                using (var path = geometry.Open()) { path.BeginFigure(start, false, false); path.PolyLineTo(mark.Points.Skip(1).Select(p => p.Point).ToArray(), true, false); }
                dc.DrawGeometry(null, pen, geometry); break;
            default: dc.DrawRectangle(Ui.Line, new Pen(Ui.Blue, 2), bounds); break;
        }
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
