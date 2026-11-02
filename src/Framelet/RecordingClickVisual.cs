using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

// Click highlight for recordings: a thin blue ring that expands and fades around the pointer.
// Frames are drawn in code at a fixed size, so the same inputs always give the same images. Frame 0 is empty (hidden state);
// frames 1-12 grow the ring from radius 14 to 26 px with an ease-out, holding full strength for the first half and then
// fading. The ring's interior and exterior stay fully transparent, so recorded pixels show through everywhere else.
// Create on a WPF dispatcher thread; DrawingVisual and RenderTargetBitmap need one.
internal static class RecordingClickVisual
{
    internal const int CanvasSize = 64;
    internal const int FrameCount = 13;
    private const double Stroke = 3, StartRadius = 14, EndRadius = 26, HoldUntil = .5, FinalOpacity = .12;
    private static readonly Color Blue = Color.FromRgb(0x4D, 0xA8, 0xFF);

    // Writes click-00.png … click-12.png (64 × 64, 96 dpi, alpha) into folder, replacing earlier copies, and returns their paths in order.
    internal static IReadOnlyList<string> CreateFrames(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        Directory.CreateDirectory(folder);
        var paths = new string[FrameCount];
        for (int index = 0; index < FrameCount; index++)
        {
            string path = Path.Combine(folder, $"click-{index:00}.png");
            CaptureService.Save(Render(index), path);
            paths[index] = path;
        }
        return paths;
    }

    internal static BitmapSource Render(int index)
    {
        if (index < 0 || index >= FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen())
        {
            if (index > 0)
            {
                var (radius, opacity) = Ring(index);
                // Alpha lives in the brush colour, so only the stroke carries colour; there is no fill.
                var brush = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * opacity), Blue.R, Blue.G, Blue.B)); brush.Freeze();
                var pen = new Pen(brush, Stroke); pen.Freeze();
                drawing.DrawEllipse(null, pen, new Point(CanvasSize / 2d, CanvasSize / 2d), radius, radius);
            }
        }
        var bitmap = new RenderTargetBitmap(CanvasSize, CanvasSize, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    // Radius to the stroke centre and stroke opacity for frames 1-12. At most 26 + 1.5 px from the centre, inside the 32 px half-canvas.
    internal static (double Radius, double Opacity) Ring(int index)
    {
        if (index < 1 || index >= FrameCount) throw new ArgumentOutOfRangeException(nameof(index));
        double t = (index - 1) / (double)(FrameCount - 2);
        double grown = 1 - (1 - t) * (1 - t);
        double opacity = t <= HoldUntil ? 1 : 1 - (1 - FinalOpacity) * ((t - HoldUntil) / (1 - HoldUntil));
        return (StartRadius + (EndRadius - StartRadius) * grown, opacity);
    }
}
