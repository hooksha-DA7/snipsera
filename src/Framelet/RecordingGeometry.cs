using System;
using System.Drawing;
using ScreenRecorderLib;

namespace Framelet;

internal static class RecordingGeometry
{
    // Recording uses physical pointer coordinates; WPF DIPs can span monitors with different scales.
    internal static Rectangle FromDrag(int startX, int startY, int endX, int endY, Rectangle desktop)
    {
        // Pointer endpoints identify pixels, including the display's last row/column. Trim the crop itself to H.264's even
        // dimensions, rather than resampling an odd crop into an even frame and introducing a thin letterbox strip.
        var selected = Rectangle.Intersect(desktop, Rectangle.FromLTRB(Math.Min(startX, endX), Math.Min(startY, endY), Math.Max(startX, endX) + 1, Math.Max(startY, endY) + 1));
        return new(selected.Left, selected.Top, selected.Width / 2 * 2, selected.Height / 2 * 2);
    }

    internal static ScreenRect SourceCrop(Rectangle selected, Rectangle monitor)
    {
        if (selected.Width < 16 || selected.Height < 16 || !monitor.Contains(selected))
            throw new InvalidOperationException("Keep the recording area at least 16 × 16 pixels and entirely inside one display.");
        return new ScreenRect(selected.Left - monitor.Left, selected.Top - monitor.Top, selected.Width / 2 * 2, selected.Height / 2 * 2);
    }
}
