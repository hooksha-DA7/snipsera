using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Framelet;

// Stroke glyphs for the recording setup panel, drawn on the same 24-unit grid as UiIcon. Ink follows the nearest control's
// Foreground, so the icons work inside toggle and radio buttons as well as plain buttons.
public sealed class RecordingGlyph : FrameworkElement
{
    private static readonly Dictionary<string, Geometry> Paths = new()
    {
        ["Area"] = Parse("M3 8 L3 3 L8 3 M16 3 L21 3 L21 8 M21 16 L21 21 L16 21 M8 21 L3 21 L3 16 M8 8 L16 8 L16 16 L8 16 Z"),
        ["Window"] = Parse("M3 4 L21 4 L21 20 L3 20 Z M3 9 L21 9 M6 6.5 L6.1 6.5 M9 6.5 L9.1 6.5"),
        ["Display"] = Parse("M3 4 L21 4 L21 17 L3 17 Z M12 17 L12 21 M7 21 L17 21"),
        ["Custom"] = Parse("M3 6 L21 6 L21 18 L3 18 Z M7 12 L17 12 M9.5 9.5 L7 12 L9.5 14.5 M14.5 9.5 L17 12 L14.5 14.5"),
        ["Cursor"] = Parse("M6 3 L7 19 L10.5 15 L13.5 21 L16 19.8 L13 14 L18.5 13.5 Z"),
        ["Click"] = Parse("M10 10 L11 21 L13.5 18 L16 22 L18 21 L15.5 17 L19.5 16.5 Z M10 2.5 L10 5.5 M4.7 4.7 L6.8 6.8 M2.5 10 L5.5 10 M15.3 4.7 L13.2 6.8 M4.7 15.3 L6.8 13.2"),
        ["Microphone"] = Parse("M9 6 A3 3 0 0 1 15 6 L15 11 A3 3 0 0 1 9 11 Z M5.5 11 A6.5 6.5 0 0 0 18.5 11 M12 17.5 L12 21 M8.5 21 L15.5 21"),
        ["Speaker"] = Parse("M3.5 9 L7.5 9 L12.5 5 L12.5 19 L7.5 15 L3.5 15 Z M16 9.5 A3.5 3.5 0 0 1 16 14.5 M18.5 6.5 A7.5 7.5 0 0 1 18.5 17.5"),
        ["Settings"] = Parse("M4 6 L20 6 M4 12 L20 12 M4 18 L20 18 M8 3 L8 9 M16 9 L16 15 M10 15 L10 21"),
        ["Close"] = Parse("M6 6 L18 18 M18 6 L6 18"),
        ["Gif"] = Parse("M4 4 L20 4 L20 20 L4 20 Z M4 8 L20 8 M4 16 L20 16 M8 4 L8 8 M12 4 L12 8 M16 4 L16 8 M8 16 L8 20 M12 16 L12 20 M16 16 L16 20 M10.5 10.2 L14 12 L10.5 13.8 Z"),
        ["Video"] = Parse("M3 6.5 L15 6.5 L15 17.5 L3 17.5 Z M15 10.5 L21 7 L21 17 L15 13.5")
    };
    public static readonly DependencyProperty InkProperty = DependencyProperty.Register(nameof(Ink), typeof(Brush), typeof(RecordingGlyph), new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
    public Brush Ink { get => (Brush)GetValue(InkProperty); set => SetValue(InkProperty, value); }
    public string Symbol { get; }

    public RecordingGlyph(string symbol, double size = 16)
    {
        Symbol = symbol; Width = Height = size; IsHitTestVisible = false; Focusable = false;
        SetBinding(InkProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Control), 1) });
    }
    protected override void OnRender(DrawingContext dc)
    {
        if (!Paths.TryGetValue(Symbol, out var path)) return;
        dc.PushTransform(new ScaleTransform(ActualWidth / 24, ActualHeight / 24));
        dc.DrawGeometry(null, new Pen(Ink, 2) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, path);
        dc.Pop();
    }
    private static Geometry Parse(string data) { var geometry = Geometry.Parse(data); geometry.Freeze(); return geometry; }
}

// Look and placement shared by the compact recording setup panel and the recording bar. Panels use the floating material and
// hairline of the capture controls; buttons are small rounded rectangles with the same Foreground veil, two-tone keyboard ring
// and high-contrast fallback as FloatingButtons.
public static class RecordingSetupVisuals
{
    public const double PanelWidth = 312, Tool = 32, ActionHeight = 36, Gap = 8, Shadow = 12;
    private const string Corner = "6";
    private const string Standard = $@"<ControlTemplate TargetType='ButtonBase'><Grid>
        <Border Background='{{TemplateBinding Background}}' CornerRadius='{Corner}'/>
        <Border x:Name='State' Background='{{TemplateBinding Foreground}}' Opacity='0' CornerRadius='{Corner}'/>
        <Border BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='{{TemplateBinding BorderThickness}}' CornerRadius='{Corner}'/>
        <ContentPresenter Margin='{{TemplateBinding Padding}}' HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}' VerticalAlignment='{{TemplateBinding VerticalContentAlignment}}' RecognizesAccessKey='False'/>
      </Grid><ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='State' Property='Opacity' Value='0.08'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='State' Property='Opacity' Value='0.16'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate>";
    // High contrast: solid system colours, a thicker Highlight outline on pointer-over, GrayText when disabled (style trigger).
    private const string Contrast = $@"<ControlTemplate TargetType='ButtonBase'><Grid>
        <Border Background='{{TemplateBinding Background}}' CornerRadius='{Corner}'/>
        <Border x:Name='Outline' BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='{{TemplateBinding BorderThickness}}' CornerRadius='{Corner}'/>
        <ContentPresenter Margin='{{TemplateBinding Padding}}' HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}' VerticalAlignment='{{TemplateBinding VerticalContentAlignment}}' RecognizesAccessKey='False'/>
      </Grid><ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Outline' Property='BorderThickness' Value='2'/><Setter TargetName='Outline' Property='BorderBrush' Value='{{DynamicResource {{x:Static SystemColors.HighlightBrushKey}}}}'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate>";
    private const string Focus = @"<ControlTemplate TargetType='Control'><Grid>
        <Border Margin='-3' BorderThickness='2' BorderBrush='{TemplateBinding Foreground}' CornerRadius='9'/>
        <Border Margin='-1' BorderThickness='1' BorderBrush='{TemplateBinding Background}' CornerRadius='7'/>
      </Grid></ControlTemplate>";
    private static readonly Brush LightError = Frozen(0xC4, 0x2B, 0x1C), DarkError = Frozen(0xFF, 0x99, 0xA4);
    private static readonly Dictionary<(string Kind, bool Contrast), Style> styles = new();
    private static ControlTemplate? standard, contrast;
    private static Style? ring;

    // Windows critical text colour for the current theme; plain window text in high contrast.
    public static Brush ErrorBrush => SystemParameters.HighContrast ? Ui.Ink : Appearance.Current.Dark ? DarkError : LightError;

    public static Border Panel(UIElement child, string name)
    {
        var panel = new Border { Child = child, Background = Ui.Floating, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(FloatingVisuals.Radius), Padding = new Thickness(6) };
        AutomationName(panel, name); Restyle(panel); return panel;
    }
    public static void Restyle(Border panel)
    {
        bool hc = SystemParameters.HighContrast;
        panel.BorderBrush = hc ? Ui.Ink : Ui.Line;
        panel.Effect = hc ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Direction = 270, Opacity = .22 };
    }
    public static Border Rule(bool vertical) => vertical
        ? new Border { Width = 1, Height = 20, Margin = new Thickness(7, 0, 7, 0), Background = Ui.Line, VerticalAlignment = VerticalAlignment.Center }
        : new Border { Height = 1, Margin = new Thickness(-6, 6, -6, 6), Background = Ui.Line };

    // "Toggle": on/off capture options, accent fill when on. "Segment": the selected source, subtle fill and accent ink.
    // "Tool": plain icon buttons. "Action": full-width Record rows.
    public static void Apply(ButtonBase button, string kind)
    {
        bool hc = SystemParameters.HighContrast;
        if (!styles.TryGetValue((kind, hc), out var style)) styles[(kind, hc)] = style = Create(kind, hc);
        button.Style = style;
    }
    private static Style Create(string kind, bool hc)
    {
        if (ring is null)
        {
            ring = new Style(typeof(Control));
            ring.Setters.Add(new Setter(Control.TemplateProperty, Parse<ControlTemplate>(Focus)));
            ring.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink)); ring.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Surface));
        }
        bool toggle = kind is "Toggle" or "Segment";
        var style = new Style(toggle ? typeof(ToggleButton) : typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, hc ? (contrast ??= Parse<ControlTemplate>(Contrast)) : (standard ??= Parse<ControlTemplate>(Standard))));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, ring));
        style.Setters.Add(new Setter(Control.BackgroundProperty, hc ? Ui.Surface : Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, hc ? Ui.Ink : Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(Control.FontFamilyProperty, Ui.Font));
        if (toggle)
        {
            var on = new Trigger { Property = ToggleButton.IsCheckedProperty, Value = true };
            // In high contrast the accent tokens resolve to Highlight / HighlightText.
            bool accent = kind == "Toggle" || hc;
            on.Setters.Add(new Setter(Control.BackgroundProperty, accent ? Ui.Blue : Ui.Line));
            on.Setters.Add(new Setter(Control.ForegroundProperty, accent ? Ui.OnAccent : Ui.Blue));
            style.Triggers.Add(on);
        }
        if (hc)
        {
            var off = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            off.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(SystemColors.GrayTextBrushKey)));
            off.Setters.Add(new Setter(Control.BorderBrushProperty, new DynamicResourceExtension(SystemColors.GrayTextBrushKey)));
            style.Triggers.Add(off);
        }
        return style;
    }

    // Puts a floating window's panel just outside the target (below, above, then beside) inside the target's own work area, using
    // the scrolling-capture keep-out so it never overlaps the captured pixels. Only that work area is offered, so the controls never
    // jump to a neighbouring display (e.g. beside a full-display target). False when no side has room.
    internal static bool PlaceOutside(Window window, System.Drawing.Rectangle target, double shadow)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || target.IsEmpty || !Native.GetWindowRect(handle, out var rect)) return false;
        double self = VisualTreeHelper.GetDpi(window).DpiScaleX, scale = ScrollCaptureLayout.ScaleFor(target);
        int own = (int)Math.Round(shadow * self);
        var body = new Size(Math.Max(1, rect.Right - rect.Left - 2 * own) / self, Math.Max(1, rect.Bottom - rect.Top - 2 * own) / self);
        if (ScrollCaptureLayout.PlaceBar(target, [WorkAreaFor(target)], body, body, scale) is not ScrollBarPlacement placed) return false;
        int margin = (int)Math.Round(shadow * scale);
        // HWND_TOPMOST, SWP_NOSIZE | SWP_NOACTIVATE.
        Native.SetWindowPos(handle, new IntPtr(-1), placed.Body.Left - margin, placed.Body.Top - margin, 0, 0, 0x0011);
        return true;
    }

    // Bottom centre of a work area, lifted by gap (logical px); the panel grows upward when settings expand.
    internal static void PlaceBottom(Window window, System.Drawing.Rectangle work, double shadow, double gap)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || work.IsEmpty || !Native.GetWindowRect(handle, out var rect)) return;
        double scale = ScrollCaptureLayout.ScaleFor(work);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = work.Left + Math.Max(0, (work.Width - width) / 2);
        int y = Math.Max(work.Top, work.Bottom - height + (int)Math.Round(shadow * scale) - (int)Math.Round(gap * scale));
        Native.SetWindowPos(handle, new IntPtr(-1), x, y, 0, 0, 0x0011);
    }

    internal static System.Drawing.Rectangle WorkAreaFor(System.Drawing.Rectangle target)
    {
        var center = new System.Drawing.Point(target.Left + target.Width / 2, target.Top + target.Height / 2);
        var area = ScrollCaptureLayout.WorkAreas().FirstOrDefault(a => a.Contains(center));
        return area.IsEmpty ? Native.MonitorAtCursor(true) : area;
    }

    private static void AutomationName(DependencyObject element, string name) => System.Windows.Automation.AutomationProperties.SetName(element, name);
    private static Brush Frozen(byte r, byte g, byte b) { var brush = new SolidColorBrush(Color.FromRgb(r, g, b)); brush.Freeze(); return brush; }
    private static T Parse<T>(string xaml)
    {
        var context = new ParserContext();
        context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        return (T)XamlReader.Parse(xaml, context);
    }
}
