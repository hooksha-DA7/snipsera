using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PixelRect = System.Drawing.Rectangle;

namespace Framelet;

public static class FloatingLayout
{
    public const double MaxWidth = 240, MaxHeight = 166;
    public static PixelRect Preview(PixelRect work, Size image, double dpi)
    {
        var size = Fit(image, Math.Min(MaxWidth * dpi, work.Width - 24 * dpi), Math.Min(MaxHeight * dpi, work.Height - 24 * dpi), dpi);
        int margin = (int)Math.Round(12 * dpi);
        return new PixelRect(work.Left + margin, work.Bottom - margin - (int)size.Height, (int)size.Width, (int)size.Height);
    }
    public static IReadOnlyList<PixelRect> Pins(PixelRect work, IReadOnlyList<Size> images, double dpi)
    {
        if (images.Count == 0) return [];
        double margin = 12 * dpi, gap = 10 * dpi;
        double reserve = Math.Min(178 * dpi, work.Height - margin * 2);
        double availableHeight = Math.Max(64 * dpi, work.Height - margin * 2 - reserve);
        double availableWidth = Math.Min(520 * dpi, work.Width - margin * 2);
        int rows = images.Count; double cellWidth = availableWidth, cellHeight = availableHeight / rows, best = -1;
        for (int cols = 1; cols <= Math.Min(4, images.Count); cols++)
        {
            int candidateRows = (int)Math.Ceiling((double)images.Count / cols);
            double w = Math.Min(MaxWidth * dpi, (availableWidth - gap * (cols - 1)) / cols);
            double h = Math.Min(MaxHeight * dpi, (availableHeight - gap * (candidateRows - 1)) / candidateRows);
            if (w < 72 * dpi || h < 48 * dpi) continue;
            double score = images.Sum(image => { var size = Fit(image, w, h, dpi); return size.Width * size.Height; }) / (1 + cols * 0.01);
            if (score <= best) continue;
            best = score; rows = candidateRows; cellWidth = w; cellHeight = h;
        }
        var result = new List<PixelRect>();
        for (int i = 0; i < images.Count; i++)
        {
            var size = Fit(images[i], cellWidth, cellHeight, dpi);
            int col = i / rows, row = i % rows;
            int x = work.Left + (int)Math.Round(margin + col * (cellWidth + gap));
            int y = work.Bottom - (int)Math.Round(margin + reserve + row * (cellHeight + gap) + size.Height);
            result.Add(new PixelRect(x, y, (int)size.Width, (int)size.Height));
        }
        return result;
    }
    private static Size Fit(Size image, double maxWidth, double maxHeight, double dpi)
    {
        if (dpi <= 0 || !double.IsFinite(dpi) || image.Width <= 0 || image.Height <= 0) throw new ArgumentException("Invalid floating image dimensions.");
        double border = 16 * dpi;
        double width = Math.Max(1, maxWidth - border), height = Math.Max(1, maxHeight - border);
        double scale = Math.Min(width / image.Width, height / image.Height);
        return new Size(Math.Floor(image.Width * scale + border), Math.Floor(image.Height * scale + border));
    }
}
