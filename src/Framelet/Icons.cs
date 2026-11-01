using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace Framelet;

public sealed class UiIcon : FrameworkElement
{
    public string Symbol { get; }
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(UiIcon), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    private readonly double weight;
    public UiIcon(string symbol, double size = 18, double weight = 1.7)
    {
        Symbol = symbol; Width = Height = size; IsHitTestVisible = false; this.weight = weight;
        SetBinding(InkProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) });
    }
    protected override void OnRender(DrawingContext dc)
    {
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        var pen = new Pen(Ink, weight) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        string path = Symbol switch
        {
            "Select" => "M5 3 L6 20 L10 15 L14 21 L17 19 L13 13 L20 12 Z",
            "Arrow" => "M4 20 C7 12 12 7 20 4 M11 4 L20 4 L20 13",
            "Line" => "M5 19 L19 5", "Rectangle" => "M4 5 L20 5 L20 19 L4 19 Z",
            "Ellipse" => "M20 11 C22 1 3 1 3 12 C3 22 22 22 20 11",
            "Crop" => "M7 3 L7 17 L21 17 M3 7 L17 7 L17 21",
            "Pen" => "M4 19 L6 13 L16 3 L21 8 L11 18 Z M6 13 L11 18 M3 21 L9 20",
            "Highlight" => "M4 18 L10 12 M7 9 L15 17 L22 10 L14 2 Z M2 22 L11 22",
            "Text" => "M5 5 L19 5 M12 5 L12 20 M8 20 L16 20",
            "Counter" => "M12 2 A10 10 0 1 1 11.9 2 M10 8 L12 6 L12 18 M9 18 L15 18",
            "Blur" => "M12 2 C10 6 5 11 5 15 A7 7 0 0 0 19 15 C19 11 14 6 12 2 M9 15 C9 18 11 19 13 19",
            "Redact" => "M3 7 L21 7 L21 17 L3 17 Z M6 10 L18 10 M6 14 L18 14",
            "Pin" => "M8 3 L18 3 M9 3 L10 10 L7 13 L17 13 L14 10 L15 3 M12 13 L12 22",
            "Unpin" => "M8 3 L18 3 M9 3 L10 10 L7 13 L17 13 L14 10 L15 3 M12 13 L12 22 M3 3 L21 21",
            "Copy" => "M8 8 L20 8 L20 21 L8 21 Z M5 16 L3 16 L3 3 L15 3 L15 5",
            "Save" => "M12 3 L12 15 M7 10 L12 15 L17 10 M4 16 L4 21 L20 21 L20 16",
            "Project" => "M4 3 L17 3 L21 7 L21 21 L4 21 Z M8 3 L8 9 L16 9 L16 3 M8 21 L8 14 L17 14 L17 21",
            "Open" => "M3 7 L3 20 L19 20 L22 10 L8 10 L5 20 M3 7 L9 7 L11 4 L20 4 L20 8",
            "History" => "M4 8 A9 9 0 1 1 3 15 M3 3 L3 9 L9 9 M12 7 L12 13 L16 15",
            "Undo" => "M8 5 L3 10 L8 15 M3 10 L14 10 C22 10 22 20 15 20",
            "Redo" => "M16 5 L21 10 L16 15 M21 10 L10 10 C2 10 2 20 9 20",
            "Close" => "M6 6 L18 18 M18 6 L6 18",
            "Check" => "M4 12 L9 17 L20 6",
            "Drag" => "M9 4 L4 4 L4 20 L20 20 L20 15 M12 3 L21 3 L21 12 M21 3 L10 14",
            "Delete" => "M4 6 L20 6 M9 6 L9 3 L15 3 L15 6 M6 6 L7 21 L17 21 L18 6 M10 10 L10 17 M14 10 L14 17",
            "Settings" => "M4 6 L20 6 M4 12 L20 12 M4 18 L20 18 M8 3 L8 9 M16 9 L16 15 M10 15 L10 21",
            "Screen" => "M3 4 L21 4 L21 17 L3 17 Z M12 17 L12 21 M7 21 L17 21",
            "Window" => "M3 4 L21 4 L21 20 L3 20 Z M3 9 L21 9 M6 6.5 L6.1 6.5 M9 6.5 L9.1 6.5",
            "Area" => "M3 8 L3 3 L8 3 M16 3 L21 3 L21 8 M21 16 L21 21 L16 21 M8 21 L3 21 L3 16 M8 8 L16 8 L16 16 L8 16 Z",
            "More" => "M4 12 L4.1 12 M12 12 L12.1 12 M20 12 L20.1 12",
            _ => "M4 4 L20 4 L20 20 L4 20 Z"
        };
        if (Symbol == "Pixelate")
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) dc.DrawRectangle((x + y) % 2 == 0 ? Ink : null, pen, new Rect(3 + x * 6, 3 + y * 6, 5, 5));
        else dc.DrawGeometry(null, pen, Geometry.Parse(path));
        dc.Pop();
    }
}
