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
    internal bool IsStableComparedTo(ScrollFrame other)
    {
        if (Width != other.Width || Height != other.Height) return false;
        int movingTiles = 0;
        for (int row = 0; row < 8; row++) for (int column = 0; column < 8; column++)
        {
            int left = column * Width / 8, right = (column + 1) * Width / 8;
            int top = row * Height / 8, bottom = (row + 1) * Height / 8;
            long error = 0; int count = 0;
            for (int y = top; y < bottom; y += Math.Max(1, (bottom - top) / 12))
                for (int x = left; x < right; x += Math.Max(1, (right - left) / 12))
                {
                    int p = (y * Width + x) * 4;
                    error += Math.Abs(Pixels[p] - other.Pixels[p]) + Math.Abs(Pixels[p + 1] - other.Pixels[p + 1]) + Math.Abs(Pixels[p + 2] - other.Pixels[p + 2]); count += 3;
                }
            if (count > 0 && (double)error / count > 1.1 && ++movingTiles > 8) return false;
        }
        return true;
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
    private double fractionalRows;
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
        if (last.Pixels.AsSpan().SequenceEqual(frame.Pixels)) return ScrollAppendStatus.Unchanged;
        if (Height >= MaxSide || (long)Width * Height >= MaxPixels || Frames >= 1000) return ScrollAppendStatus.LimitReached;
        double? match = ScrollAlignment.Find(last, frame);
        if (match is null) return ScrollAppendStatus.NeedsOverlap;
        // Refresh only a certified zero-offset match; an ambiguous repeated page must never advance the anchor.
        if (match.Value == 0) { last = frame; return ScrollAppendStatus.Unchanged; }
        if (match.Value < 0) return ScrollAppendStatus.Backwards;
        double movement = match.Value + fractionalRows;
        int best = (int)Math.Round(movement, MidpointRounding.AwayFromZero);
        if (best == 0) { fractionalRows = movement; last = frame; return ScrollAppendStatus.Unchanged; }
        if (Height + best > MaxSide || (long)Width * (Height + best) > MaxPixels || Frames >= 1000) return ScrollAppendStatus.LimitReached;
        var added = new byte[checked(Width * best * 4)];
        Buffer.BlockCopy(frame.Pixels, (frame.Height - best) * Width * 4, added, 0, added.Length);
        strips.Add((added, best)); Height += best; Frames++; last = frame; fractionalRows = movement - best;
        RefreshLastView(frame);
        return ScrollAppendStatus.Added;
    }
    internal void RefreshLastView(ScrollFrame frame)
    {
        int start = Height - frame.Height, top = 0;
        foreach (var strip in strips)
        {
            int from = Math.Max(start, top), to = Math.Min(Height, top + strip.Height);
            if (to > from) Buffer.BlockCopy(frame.Pixels, (from - start) * Width * 4, strip.Pixels, (from - top) * Width * 4, (to - from) * Width * 4);
            top += strip.Height;
        }
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
