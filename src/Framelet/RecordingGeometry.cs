using System;
using System.Drawing;
using ScreenRecorderLib;

namespace Framelet;

internal static class RecordingGeometry
{
    // Recording uses physical pointer coordinates; WPF DIPs can span monitors with different scales.
    internal static Rectangle FromDrag(int startX, int startY, int endX, int endY, Rectangle desktop) =>
        Rectangle.Intersect(desktop, Rectangle.FromLTRB(Math.Min(startX, endX), Math.Min(startY, endY), Math.Max(startX, endX), Math.Max(startY, endY)));

    internal static ScreenRect SourceCrop(Rectangle selected, Rectangle monitor)
    {
        if (selected.Width < 16 || selected.Height < 16 || !monitor.Contains(selected))
            throw new InvalidOperationException("Keep the recording area at least 16 × 16 pixels and entirely inside one display.");
        return new ScreenRect(selected.Left - monitor.Left, selected.Top - monitor.Top, selected.Width, selected.Height);
    }
}
