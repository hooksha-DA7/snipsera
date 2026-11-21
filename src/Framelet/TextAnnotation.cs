using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Framelet;

public enum TextPreset { Plain, Outline, Shadow, SolidLabel, LightLabel, Callout, Highlight }

public static class TextAnnotation
{
    // Plain keeps the original project's font and weight. The styled presets are session-era additions.
    public static Typeface Typeface(Mark mark) => new(new FontFamily("Segoe UI"), FontStyles.Normal,
        mark.TextPreset == TextPreset.Plain ? FontWeights.Normal : FontWeights.SemiBold, FontStretches.Normal);
    public static FormattedText Format(Mark mark, Brush? brush = null) => new(mark.Text, CultureInfo.InvariantCulture,
        FlowDirection.LeftToRight, Typeface(mark), mark.FontSize, brush ?? Foreground(mark), 1) { MaxTextWidth = 20000 };

    public static double Padding(TextPreset preset) => preset switch
    {
        TextPreset.SolidLabel or TextPreset.LightLabel or TextPreset.Callout or TextPreset.Highlight => 8,
        TextPreset.Outline => 3,
        TextPreset.Shadow => 2,
        _ => 0
    };

    public static Thickness Insets(Mark mark)
    {
        double p = Math.Max(2, Math.Round(mark.FontSize * .3));
        return mark.TextPreset switch
        {
            TextPreset.SolidLabel or TextPreset.LightLabel or TextPreset.Callout => new Thickness(p * 1.4, p * .8, p * 1.4, p * .8),
            TextPreset.Highlight => new Thickness(p * .7, 0, p * .7, 0),
            TextPreset.Outline => new Thickness(Math.Ceiling(Math.Max(2, mark.FontSize / 8) / 2)),
            TextPreset.Shadow => new Thickness(2, 0, 2, Math.Max(3, mark.FontSize / 16 + 2)),
            _ => new Thickness(0)
        };
    }

    public static Rect Bounds(Mark mark)
    {
        var text = Format(mark); var inset = Insets(mark);
        return new Rect(mark.Points[0].Point, new Size(Math.Max(1, text.WidthIncludingTrailingWhitespace) + inset.Left + inset.Right,
            Math.Max(mark.FontSize, text.Height) + inset.Top + inset.Bottom + (mark.TextPreset == TextPreset.Callout ? 10 : 0)));
    }

    public static SolidColorBrush Foreground(Mark mark)
    {
        Color color = AnnotationColor(mark);
        Color ink = mark.TextPreset switch
        {
            TextPreset.SolidLabel or TextPreset.Callout => ContrastInk(color),
            TextPreset.LightLabel => DarkenForContrast(color, Tint(color, .14)),
            TextPreset.Highlight => ContrastInk(Tint(color, .45)),
            _ => color
        };
        return new SolidColorBrush(ink);
    }

    public static void Draw(DrawingContext dc, Mark mark)
    {
        DrawDecoration(dc, mark);
        var inset = Insets(mark); var start = mark.Points[0].Point;
        dc.DrawText(Format(mark), new Point(start.X + inset.Left, start.Y + inset.Top));
    }

    // This shared decoration path also backs live text editing on the canvas.
    public static void DrawDecoration(DrawingContext dc, Mark mark)
    {
        Color color = AnnotationColor(mark); var fill = new SolidColorBrush(color);
        var bounds = Bounds(mark); var inset = Insets(mark);
        var origin = new Point(bounds.X + inset.Left, bounds.Y + inset.Top);
        double radius = Math.Max(2, mark.FontSize * .3);
        switch (mark.TextPreset)
        {
            case TextPreset.Outline:
                var outline = new SolidColorBrush(ContrastInk(color));
                dc.DrawGeometry(outline, new Pen(outline, Math.Max(2, mark.FontSize / 8)) { LineJoin = PenLineJoin.Round }, Format(mark).BuildGeometry(origin));
                break;
            case TextPreset.Shadow:
                double offset = Math.Max(1, mark.FontSize / 16);
                dc.DrawText(Format(mark, new SolidColorBrush(Color.FromArgb(45, 0, 0, 0))), origin + new Vector(1, offset + 1));
                dc.DrawText(Format(mark, new SolidColorBrush(Color.FromArgb(65, 0, 0, 0))), origin + new Vector(0, offset));
                break;
            case TextPreset.SolidLabel:
                dc.DrawRoundedRectangle(fill, null, bounds, radius, radius); break;
            case TextPreset.LightLabel:
                dc.DrawRoundedRectangle(new SolidColorBrush(Tint(color, .14)),
                    new Pen(new SolidColorBrush(Tint(color, .6)), 1), bounds, radius, radius); break;
            case TextPreset.Callout:
                dc.DrawRoundedRectangle(fill, null, new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height - 10), radius, radius);
                var tail = new StreamGeometry();
                double left = bounds.X + Math.Min(12, bounds.Width * .25), span = Math.Min(12, bounds.Width * .35);
                using (var g = tail.Open()) { g.BeginFigure(new Point(left, bounds.Bottom - 11), true, true); g.LineTo(new Point(left, bounds.Bottom), true, false); g.LineTo(new Point(left + span, bounds.Bottom - 11), true, false); }
                dc.DrawGeometry(fill, null, tail); break;
            case TextPreset.Highlight:
                // A solid pale tint keeps the marker legible on both light and dark captured content.
                var band = new SolidColorBrush(Tint(color, .45));
                int lines = Math.Max(1, mark.Text.Split('\n').Length);
                double lineHeight = Math.Max(mark.FontSize, Format(mark).Height / lines);
                for (int line = 0; line < lines; line++)
                    dc.DrawRoundedRectangle(band, null, new Rect(bounds.X, bounds.Y + line * lineHeight + lineHeight * .15, bounds.Width, lineHeight * .85), 2, 2);
                break;
        }
    }

    private static Color AnnotationColor(Mark mark) => (Color)ColorConverter.ConvertFromString(mark.Color);
    private static readonly Color DarkInk = Color.FromRgb(27, 27, 27);
    private static Color Tint(Color color, double amount) => Color.FromRgb(
        (byte)Math.Round(255 + (color.R - 255) * amount), (byte)Math.Round(255 + (color.G - 255) * amount), (byte)Math.Round(255 + (color.B - 255) * amount));
    private static double Luminance(Color color)
    {
        static double Linear(byte value) { double channel = value / 255.0; return channel <= .04045 ? channel / 12.92 : Math.Pow((channel + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    private static double Contrast(Color a, Color b)
    { double first = Luminance(a), second = Luminance(b); return (Math.Max(first, second) + .05) / (Math.Min(first, second) + .05); }
    private static Color ContrastInk(Color fill) => Contrast(Colors.White, fill) > Contrast(DarkInk, fill) ? Colors.White : DarkInk;
    private static Color DarkenForContrast(Color color, Color fill)
    {
        var ink = Color.FromRgb((byte)(color.R * .75), (byte)(color.G * .75), (byte)(color.B * .75));
        for (int i = 0; i < 20 && Contrast(ink, fill) < 4.5; i++)
            ink = Color.FromRgb((byte)(ink.R * .9), (byte)(ink.G * .9), (byte)(ink.B * .9));
        return ink;
    }
}
