using System;
using System.Collections.Generic;
using System.Windows;
using PixelRect = System.Drawing.Rectangle;

namespace Framelet;

public static class FloatingLayout
{
    // Logical (96 dpi) metrics. Docked previews and pins share one compact 4:3 card whatever the capture's shape; the thumbnail
    // fills it and may crop at the edges, while the window keeps the full bitmap. Each window adds a transparent shadow Margin on
    // every side, so neighbours overlap by their margins and the visible Gap between images stays tight.
    public const double CardWidth = 200, CardHeight = 150, Margin = 8, Edge = 12, Gap = 8;
    public const double MaxWidth = CardWidth + 2 * Margin, MaxHeight = CardHeight + 2 * Margin;

    public static PixelRect Preview(PixelRect work, Size image, double dpi)
    {
        Validate(image, dpi);
        var (w, h) = PreviewCard(work, dpi); int m = Px(Margin, dpi), edge = Px(Edge, dpi);
        return new PixelRect(work.Left + edge, work.Bottom - edge - h - 2 * m, w + 2 * m, h + 2 * m);
    }

    // Pins stack upward from just above the preview slot (kept free so a new preview never covers a pin), then into more columns.
    public static IReadOnlyList<PixelRect> Pins(PixelRect work, IReadOnlyList<Size> images, double dpi)
    {
        foreach (var image in images) Validate(image, dpi);
        if (images.Count == 0) return [];
        int m = Px(Margin, dpi), edge = Px(Edge, dpi), gap = Px(Gap, dpi), count = images.Count;
        int previewTop = work.Bottom - edge - m - PreviewCard(work, dpi).Height;
        double width = work.Width - 2 * (edge + m), height = previewTop - gap - (work.Top + edge + m);
        int rows = count; double scale = -1;
        // Prefer the arrangement that keeps cards largest; a single column wins ties.
        for (int cols = 1; cols <= count; cols++)
        {
            int candidateRows = (int)Math.Ceiling((double)count / cols);
            double fit = Math.Min(1, Math.Min((width - (cols - 1) * gap) / (cols * CardWidth * dpi), (height - (candidateRows - 1) * gap) / (candidateRows * CardHeight * dpi)));
            if (fit > scale + 1e-9) { scale = fit; rows = candidateRows; }
        }
        int w = Math.Max(1, (int)Math.Floor(CardWidth * dpi * scale)), h = Math.Max(1, (int)Math.Floor(CardHeight * dpi * scale));
        var result = new List<PixelRect>(count);
        for (int i = 0; i < count; i++)
        {
            int col = i / rows, row = i % rows;
            int left = work.Left + edge + m + col * (w + gap), bottom = previewTop - gap - row * (h + gap);
            result.Add(new PixelRect(left - m, bottom - h - m, w + 2 * m, h + 2 * m));
        }
        return result;
    }

    // Visible image size of the preview card in physical pixels, shrunk only when the work area cannot hold the full card.
    private static (int Width, int Height) PreviewCard(PixelRect work, double dpi)
    {
        double inset = 2 * (Px(Edge, dpi) + Px(Margin, dpi));
        double scale = Math.Min(1, Math.Min((work.Width - inset) / (CardWidth * dpi), (work.Height - inset) / (CardHeight * dpi)));
        return (Math.Max(1, (int)Math.Floor(CardWidth * dpi * scale)), Math.Max(1, (int)Math.Floor(CardHeight * dpi * scale)));
    }
    private static int Px(double value, double dpi) => (int)Math.Round(value * dpi);
    private static void Validate(Size image, double dpi)
    {
        if (dpi <= 0 || !double.IsFinite(dpi) || image.Width <= 0 || image.Height <= 0) throw new ArgumentException("Invalid floating image dimensions.");
    }
}
