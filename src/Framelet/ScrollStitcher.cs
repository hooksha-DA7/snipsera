using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public enum ScrollAppendStatus { Added, Unchanged, Backwards, NeedsOverlap, LimitReached }

// Pixel-only alignment: no website text, DOM access or knowledge of the destination application.
public sealed class ScrollFrame
{
    public int Width { get; }
    public int Height { get; }
    internal byte[] Pixels { get; }
    public ScrollFrame(BitmapSource image)
    {
        Width = image.PixelWidth; Height = image.PixelHeight;
        if (Width < 2 || Height < 2 || (long)Width * Height > 40_000_000) throw new InvalidOperationException("Choose a smaller scrolling area.");
        var converted = image.Format == PixelFormats.Bgra32 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        Pixels = new byte[checked(Width * Height * 4)]; converted.CopyPixels(Pixels, Width * 4, 0);
    }
    internal double Distance(ScrollFrame other, int offset = 0)
    {
        if (Width != other.Width || Height != other.Height) return double.PositiveInfinity;
        int previousY = Math.Max(offset, 0), nextY = Math.Max(-offset, 0), overlap = Height - Math.Abs(offset);
        int margin = Math.Max(2, Width / 40), stepX = Math.Max(1, (Width - 2 * margin) / 40), stepY = Math.Max(1, overlap / 64);
        long error = 0; int count = 0;
        for (int y = 0; y < overlap; y += stepY)
            for (int x = margin; x < Width - margin; x += stepX)
            {
                int a = ((previousY + y) * Width + x) * 4, b = ((nextY + y) * Width + x) * 4;
                error += Math.Abs(Pixels[a] - other.Pixels[b]) + Math.Abs(Pixels[a + 1] - other.Pixels[b + 1]) + Math.Abs(Pixels[a + 2] - other.Pixels[b + 2]); count += 3;
            }
        return count == 0 ? double.PositiveInfinity : (double)error / count;
    }
    internal BitmapSource Bitmap()
    {
        var image = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, Pixels, Width * 4); image.Freeze(); return image;
    }
}

public sealed class ScrollStitcher
{
    public const int MaxSide = 20_000;
    public const long MaxPixels = 40_000_000;
    private ScrollFrame last;
    private readonly List<(byte[] Pixels, int Height)> strips = [];
    public int Width => last.Width;
    public int Height { get; private set; }
    public int Frames { get; private set; } = 1;
    public ScrollStitcher(ScrollFrame first)
    {
        if (first.Width > MaxSide || first.Height > MaxSide) throw new InvalidOperationException("The scrolling area exceeds the image limit.");
        last = first; Height = first.Height; strips.Add((first.Pixels, first.Height));
    }
    public ScrollAppendStatus Add(ScrollFrame frame)
    {
        if (frame.Width != Width || frame.Height != last.Height) throw new InvalidOperationException("The scrolling area changed size. Start a new capture.");
        double zero = last.Distance(frame);
        if (zero <= .65) return ScrollAppendStatus.Unchanged;
        if (Height >= MaxSide || (long)Width * Height >= MaxPixels || Frames >= 1000) return ScrollAppendStatus.LimitReached;
        int limit = last.Height - Math.Max(32, last.Height / 4), best = 0;
        double bestError = double.PositiveInfinity;
        var errors = new double[2 * limit + 1];
        for (int shift = -limit; shift <= limit; shift++)
        {
            double error = last.Distance(frame, shift); errors[shift + limit] = error;
            if (error < bestError) { bestError = error; best = shift; }
        }
        double alternative = double.PositiveInfinity;
        for (int shift = -limit; shift <= limit; shift++)
            if (Math.Abs(shift - best) > 2) alternative = Math.Min(alternative, errors[shift + limit]);
        // Repeated patterns and blank areas cannot prove how far the page moved. Keep the last good image.
        if (best == 0 || bestError > 4 || bestError > zero * .45 || alternative - bestError < Math.Max(.6, bestError * .45)) return ScrollAppendStatus.NeedsOverlap;
        if (best < 0) return ScrollAppendStatus.Backwards;
        if (Height + best > MaxSide || (long)Width * (Height + best) > MaxPixels || Frames >= 1000) return ScrollAppendStatus.LimitReached;
        var added = new byte[checked(Width * best * 4)];
        Buffer.BlockCopy(frame.Pixels, (frame.Height - best) * Width * 4, added, 0, added.Length);
        strips.Add((added, best)); Height += best; Frames++; last = frame;
        return ScrollAppendStatus.Added;
    }
    public BitmapSource Image()
    {
        var pixels = new byte[checked(Width * Height * 4)]; int offset = 0;
        foreach (var strip in strips) { Buffer.BlockCopy(strip.Pixels, 0, pixels, offset, strip.Pixels.Length); offset += strip.Pixels.Length; }
        var image = BitmapSource.Create(Width, Height, 96, 96, PixelFormats.Bgra32, null, pixels, Width * 4); image.Freeze(); return image;
    }
    public BitmapSource Thumbnail()
    {
        double scale = Math.Min(1, Math.Min(180d / Width, 320d / Height));
        int width = Math.Max(1, (int)Math.Ceiling(Width * scale)), height = Math.Max(1, (int)Math.Ceiling(Height * scale));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            double y = 0;
            foreach (var strip in strips)
            {
                var source = BitmapSource.Create(Width, strip.Height, 96, 96, PixelFormats.Bgra32, null, strip.Pixels, Width * 4); source.Freeze();
                drawing.DrawImage(source, new Rect(0, y * scale, Width * scale, strip.Height * scale)); y += strip.Height;
            }
        }
        var thumbnail = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); thumbnail.Render(visual); thumbnail.Freeze(); return thumbnail;
    }
}
