using System;
using System.Collections.Generic;
using System.Windows;
using PixelRect = System.Drawing.Rectangle;

namespace Framelet;

public sealed record FloatingStackLayout(IReadOnlyList<PixelRect> Previews, IReadOnlyList<PixelRect> Pins, PixelRect? Overflow);

public static class FloatingLayout
{
    // Logical (96 dpi) metrics. Front previews and pins share one compact 4:3 card whatever the capture's shape; the thumbnail
    // fills it and may crop at the edges, while the window keeps the full bitmap. Each window adds a transparent shadow Margin on
    // every side, so neighbours overlap by their margins and the visible Gap between images stays tight.
    public const double CardWidth = 200, CardHeight = 150, Margin = 8, Edge = 12, Gap = 8;
    public const double MaxWidth = CardWidth + 2 * Margin, MaxHeight = CardHeight + 2 * Margin;
    public const double OverflowHeight = 32;
    public const double DeckPeek = 20, DeckInset = 8;

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

    // Screenshots form an overlapping deck, newest in front. A recording stays below that deck as a separate full card.
    // References use the space above the group first, then neighbouring columns. Front cards and references share one scale.
    // Empty geometry means the work area cannot hold positive content; the caller can defer showing the cards there.
    public static FloatingStackLayout Stack(PixelRect work, int previewCount, int pinCount, bool overflow, double dpi, bool separateRecording = false)
    {
        Validate(new Size(CardWidth, CardHeight), dpi);
        if (previewCount < 0 || previewCount > 3) throw new ArgumentOutOfRangeException(nameof(previewCount));
        if (pinCount < 0 || pinCount > 8) throw new ArgumentOutOfRangeException(nameof(pinCount));
        if (separateRecording && previewCount == 0) throw new ArgumentException("A separate recording needs a preview slot.", nameof(separateRecording));
        var empty = new FloatingStackLayout([], [], null);
        if (work.Width <= 0 || work.Height <= 0 || previewCount + pinCount == 0 && !overflow) return empty;
        int margin = Px(Margin, dpi), edge = Px(Edge, dpi), gap = Px(Gap, dpi);
        double availableWidth = work.Width - 2d * (edge + margin), availableHeight = work.Height - 2d * (edge + margin);
        if (availableWidth <= 0 || availableHeight <= 0) return empty;

        // Keep one vacant capture slot when there are only pins, so the next capture has a predictable position.
        int screenshots = previewCount - (separateRecording ? 1 : 0), firstPins = 0, sideRows = 0;
        double groupHeight = (separateRecording ? CardHeight : 0) + (screenshots > 0 ? CardHeight + (screenshots - 1) * DeckPeek : 0);
        if (previewCount == 0) groupHeight = CardHeight;
        int recordingGap = separateRecording && screenshots > 0 ? gap : 0;
        double best = -1;
        for (int sideColumns = 0; sideColumns <= pinCount; sideColumns++)
        for (int above = pinCount; above >= 0; above--)
        {
            int remaining = pinCount - above;
            if (sideColumns == 0 ? remaining != 0 : remaining < sideColumns) continue;
            int rows = sideColumns == 0 ? 0 : (int)Math.Ceiling((double)remaining / sideColumns);
            int firstGaps = recordingGap + (above + (overflow ? 1 : 0)) * gap;
            double firstHeight = (groupHeight + above * CardHeight + (overflow ? OverflowHeight : 0)) * dpi;
            double fit = Math.Min(1, Math.Min((availableWidth - sideColumns * gap) / ((sideColumns + 1) * CardWidth * dpi),
                (availableHeight - firstGaps) / firstHeight));
            if (rows > 0) fit = Math.Min(fit, (availableHeight - (rows - 1) * gap) / (rows * CardHeight * dpi));
            if (fit <= best + 1e-9 || Math.Floor(CardWidth * dpi * fit) < 1 || Math.Floor(CardHeight * dpi * fit) < 1
                || screenshots > 1 && Math.Floor(DeckPeek * dpi * fit) < 1
                || overflow && Math.Floor(OverflowHeight * dpi * fit) < 1) continue;
            best = fit; firstPins = above; sideRows = rows;
        }
        if (best <= 0) return empty;

        int width = (int)Math.Floor(CardWidth * dpi * best), height = (int)Math.Floor(CardHeight * dpi * best);
        int peek = (int)Math.Floor(DeckPeek * dpi * best), inset = (int)Math.Floor(DeckInset * dpi * best);
        int chipHeight = (int)Math.Floor(OverflowHeight * dpi * best);
        int left = work.Left + edge + margin, baseline = work.Bottom - edge - margin;
        PixelRect Window(int x, int bottom, int bodyHeight, int bodyWidth) => new(x - margin, bottom - bodyHeight - margin, bodyWidth + 2 * margin, bodyHeight + 2 * margin);
        var previews = new List<PixelRect>(previewCount); var pins = new List<PixelRect>(pinCount);
        int bottom = baseline;
        if (separateRecording)
        {
            previews.Add(Window(left, bottom, height, width));
            bottom -= height + gap;
        }
        if (screenshots > 0)
        {
            for (int depth = 0; depth < screenshots; depth++)
                previews.Add(Window(left + depth * inset, bottom - depth * peek, height, width - 2 * depth * inset));
            bottom -= height + (screenshots - 1) * peek + gap;
        }
        else if (!separateRecording) bottom -= height + gap;
        PixelRect? chip = null;
        if (overflow) { chip = Window(left, bottom, chipHeight, width); bottom -= chipHeight + gap; }
        for (int i = 0; i < firstPins; i++) { pins.Add(Window(left, bottom, height, width)); bottom -= height + gap; }
        for (int i = 0; i < pinCount - firstPins; i++)
        {
            int column = 1 + i / sideRows, row = i % sideRows;
            pins.Add(Window(left + column * (width + gap), baseline - row * (height + gap), height, width));
        }
        return new(previews, pins, chip);
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
