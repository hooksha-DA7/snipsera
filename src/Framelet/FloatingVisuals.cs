using System.Windows;
using System.Windows.Media;

namespace Framelet;

public static class FloatingVisuals
{
    public const double Radius = 12;
    public static void Round(UIElement surface, Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return;
        // An alpha mask antialiases the contour; a WPF geometry clip cuts it with a hard edge.
        var rect = new Rect(size);
        var drawing = new GeometryDrawing(Brushes.White, null, new RectangleGeometry(rect, Radius, Radius)); drawing.Freeze();
        var mask = new DrawingBrush(drawing) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = rect, Stretch = Stretch.Fill }; mask.Freeze();
        surface.Clip = null; surface.OpacityMask = mask; surface.SnapsToDevicePixels = false;
    }
}
