using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace Framelet;

// Look of the all-in-one capture bar: one floating pill in the capture controls' material and hairline, a quiet grip to drag
// it by, and a soft shadow that drops out in high contrast. Buttons reuse the recording setup's "Tool" style.
public static class CaptureBarVisuals
{
    // Logical pixels. Shadow is the transparent margin around the panel that holds the drop shadow.
    public const double ModeWidth = 68, ModeHeight = 54, ModeIcon = 20, CloseSize = 28, Shadow = 16;

    public static Border Panel(UIElement child)
    {
        var panel = new Border { Child = child, Background = Ui.Floating, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(FloatingVisuals.Radius), Padding = new Thickness(6) };
        System.Windows.Automation.AutomationProperties.SetName(panel, "Screenshot modes");
        Restyle(panel); return panel;
    }
    public static void Restyle(Border panel)
    {
        bool hc = SystemParameters.HighContrast;
        panel.BorderBrush = hc ? Ui.Ink : Ui.Line;
        panel.Effect = hc ? null : new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = Appearance.Current.Dark ? .32 : .18 };
    }

    public static Border Divider() => new() { Width = 1, Height = 32, Margin = new Thickness(6, 0, 6, 0), Background = Ui.Line, VerticalAlignment = VerticalAlignment.Center };

    // Two columns of three dots beside the single row, or three columns of two above the wrapped grid; the transparent fill
    // keeps the whole strip draggable.
    public static Border Grip(bool horizontal = false)
    {
        var dots = new GeometryGroup();
        int columns = horizontal ? 3 : 2, rows = horizontal ? 2 : 3;
        for (int column = 0; column < columns; column++) for (int row = 0; row < rows; row++) dots.Children.Add(new EllipseGeometry(new Point(1.25 + column * 5, 1.25 + row * 5), 1.25, 1.25));
        dots.Freeze();
        var path = new Path { Data = dots, Fill = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        var grip = new Border { Child = path, Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.SizeAll, ToolTip = "Drag to move" };
        if (horizontal) { grip.Height = 14; grip.Margin = new Thickness(0, 0, 0, 2); }
        else { grip.Width = 18; grip.Margin = new Thickness(0, 0, 2, 0); }
        return grip;
    }

    // A segmented bar, for the Home launcher. Ink follows the enclosing button's Foreground, like UiIcon.
    public static FrameworkElement LauncherGlyph(double size = 22)
    {
        var data = Geometry.Parse("M5 6 L19 6 A2 2 0 0 1 21 8 L21 16 A2 2 0 0 1 19 18 L5 18 A2 2 0 0 1 3 16 L3 8 A2 2 0 0 1 5 6 Z M9 6 L9 18 M15 6 L15 18"); data.Freeze();
        var path = new Path { Data = data, StrokeThickness = 1.7, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Stretch = Stretch.None, IsHitTestVisible = false };
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
        return new Viewbox { Child = new Canvas { Width = 24, Height = 24, Children = { path } }, Width = size, Height = size, IsHitTestVisible = false };
    }
}
