using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public sealed record ImageLayer(string Id, double X, double Y, double Width, double Height)
{
    [System.Text.Json.Serialization.JsonIgnore] public Rect Bounds => new(X, Y, Width, Height);
    public ImageLayer Offset(double x, double y) => this with { X = X + x, Y = Y + y };
}

public sealed record BackgroundOptions(string Preset = "Solid", double AspectRatio = 0,
    double AlignmentX = .5, double AlignmentY = .5, double CornerRadius = 0, bool Shadow = false);

public readonly record struct CompositionFrame(int Width, int Height, Rect Content);

public static class CompositionLayout
{
    public static readonly string[] Presets = ["Solid", "Aurora", "Sunset", "Ocean", "Lavender", "Slate",
        "Violet", "Peach", "Mint", "Sky", "Rose", "Ember", "Midnight", "Graphite", "Sand", "Nebula"];

    public static Rect ContentBounds(EditState state)
    {
        var bounds = state.Crop;
        foreach (var image in state.Images) bounds.Union(image.Bounds);
        if (state.IsComposition)
            foreach (var mark in state.Marks)
            {
                Rect annotation = mark.Bounds;
                if (mark.Kind != MarkKind.Text) annotation.Inflate(mark.Width / 2, mark.Width / 2);
                bounds.Union(annotation);
            }
        return bounds;
    }

    public static CompositionFrame Calculate(EditState state)
    {
        Rect bounds = ContentBounds(state);
        double contentWidth = bounds.Width * state.ScaleX, contentHeight = bounds.Height * state.ScaleY;
        double width = Math.Ceiling(contentWidth) + state.Padding * 2, height = Math.Ceiling(contentHeight) + state.Padding * 2;
        var options = state.Backdrop ?? new BackgroundOptions();
        if (options.AspectRatio > 0)
        {
            if (width / height < options.AspectRatio) width = Math.Ceiling(height * options.AspectRatio);
            else height = Math.Ceiling(width / options.AspectRatio);
        }
        if (!double.IsFinite(width) || !double.IsFinite(height) || width > 20000 || height > 20000)
            throw new ArgumentException("This composition exceeds 20,000 pixels on one side.");
        double x = state.Padding + Math.Max(0, width - contentWidth - state.Padding * 2) * options.AlignmentX;
        double y = state.Padding + Math.Max(0, height - contentHeight - state.Padding * 2) * options.AlignmentY;
        return new((int)width, (int)height, new Rect(x, y, contentWidth, contentHeight));
    }

    public static void Validate(EditState state)
    {
        var options = state.Backdrop;
        if (options is not null && (!Presets.Contains(options.Preset, StringComparer.Ordinal) ||
            !double.IsFinite(options.AspectRatio) || options.AspectRatio is < 0 or > 5 || options.AspectRatio is > 0 and < .2 ||
            !double.IsFinite(options.AlignmentX) || options.AlignmentX is < 0 or > 1 ||
            !double.IsFinite(options.AlignmentY) || options.AlignmentY is < 0 or > 1 ||
            !double.IsFinite(options.CornerRadius) || options.CornerRadius is < 0 or > 100))
            throw new ArgumentException("Choose a valid background preset, alignment and aspect ratio.");
        if (state.Images.Length > EditDocument.MaximumImages || state.Images.Select(i => i?.Id).Distinct().Count() != state.Images.Length)
            throw new ArgumentException("A composition supports up to 32 distinct added images.");
        foreach (var layer in state.Images)
            if (layer is null || layer.Id is null || !Regex.IsMatch(layer.Id, "^[a-f0-9]{32}$") ||
                !double.IsFinite(layer.X) || !double.IsFinite(layer.Y) || Math.Abs(layer.X) > 20000 || Math.Abs(layer.Y) > 20000 ||
                !double.IsFinite(layer.Width) || !double.IsFinite(layer.Height) || layer.Width is < 1 or > 20000 || layer.Height is < 1 or > 20000)
                throw new ArgumentException("This project contains an invalid image layer.");
    }

    public static Brush BackgroundBrush(EditState state)
    {
        string preset = state.Backdrop?.Preset ?? "Solid";
        (string start, string end) = preset switch
        {
            "Aurora" => ("#8BD8CF", "#5063B4"),
            "Sunset" => ("#F9C58D", "#D85E86"),
            "Ocean" => ("#72C8E8", "#2465B8"),
            "Lavender" => ("#E6D8F8", "#A794D3"),
            "Slate" => ("#566478", "#202B3C"),
            "Violet" => ("#C471F5", "#6A5AF9"),
            "Peach" => ("#FFD3A5", "#FD6585"),
            "Mint" => ("#D4FC79", "#2FB89A"),
            "Sky" => ("#E0F2FF", "#7AB8F5"),
            "Rose" => ("#FBC2EB", "#E5689B"),
            "Ember" => ("#F6D365", "#E2502C"),
            "Midnight" => ("#3A3F6B", "#0F1222"),
            "Graphite" => ("#8E9AAB", "#3B4250"),
            "Sand" => ("#F5E9D7", "#D8BFA0"),
            "Nebula" => ("#FF6CAB", "#4FC3F7"),
            _ => (state.Background, state.Background)
        };
        if (preset == "Solid") return state.Background == "Transparent" ? Brushes.Transparent : new SolidColorBrush((Color)ColorConverter.ConvertFromString(state.Background));
        var brush = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(start), (Color)ColorConverter.ConvertFromString(end), 45);
        if (preset == "Nebula") brush.GradientStops.Insert(1, new GradientStop((Color)ColorConverter.ConvertFromString("#7366FF"), .5));
        return brush;
    }
}

internal static class ContentBalance
{
    // Remove only a nearly uniform border that agrees at every corner. Original pixels remain in the project.
    public static Rect Find(BitmapSource original, Rect crop)
    {
        var frame = new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        int x0 = (int)crop.X, y0 = (int)crop.Y, width = (int)crop.Width, height = (int)crop.Height;
        int stride = checked(width * 4); var pixels = new byte[checked(stride * height)];
        frame.CopyPixels(new Int32Rect(x0, y0, width, height), pixels, stride, 0);
        bool Background(int x, int y)
        {
            int index = y * stride + x * 4;
            if (pixels[3] < 8 && pixels[index + 3] < 8) return true;
            for (int c = 0; c < 4; c++) if (Math.Abs(pixels[index + c] - pixels[c]) > 10) return false;
            return true;
        }
        if (!Background(width - 1, 0) || !Background(0, height - 1) || !Background(width - 1, height - 1)) return crop;
        int left = width, top = height, right = -1, bottom = -1;
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            if (!Background(x, y)) { left = Math.Min(left, x); right = Math.Max(right, x); top = Math.Min(top, y); bottom = Math.Max(bottom, y); }
        if (right < left || bottom < top) return crop;
        // A two-pixel breathing space keeps antialiased content away from the edge.
        left = Math.Max(0, left - 2); top = Math.Max(0, top - 2); right = Math.Min(width - 1, right + 2); bottom = Math.Min(height - 1, bottom + 2);
        return new Rect(x0 + left, y0 + top, right - left + 1, bottom - top + 1);
    }
}
