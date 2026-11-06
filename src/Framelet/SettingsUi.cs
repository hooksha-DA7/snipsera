using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Framelet;

// Shared building blocks for the Settings pages, so every section uses the same headings, cards, spacing and status text.
internal static class SettingsUi
{
    // The page title at the top of each section.
    public static TextBlock PageTitle(string title)
    {
        var heading = Ui.Text(title, 20, Ui.Ink, true); heading.Margin = new Thickness(2, 0, 0, 4);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level1); return heading;
    }

    // A titled card inside a page.
    public static UIElement Group(string title, UIElement content)
    {
        var group = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
        var heading = Ui.Text(title, 14, Ui.Ink, true); heading.Margin = new Thickness(2, 0, 0, 8);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        group.Children.Add(heading); group.Children.Add(Ui.Card(content));
        AutomationProperties.SetName(group, title); return group;
    }

    public static CheckBox Box(string label, string id, string help, Panel host)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, FontSize = 14 };
        AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, label); AutomationProperties.SetHelpText(box, help);
        var hint = Ui.Text(help, 12, Ui.Muted); hint.Margin = new Thickness(28, 0, 0, 0);
        host.Children.Add(box); host.Children.Add(hint); return box;
    }

    // Buttons that wrap onto another line instead of clipping when the window is narrow.
    public static WrapPanel Actions(params Button[] buttons)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        foreach (var button in buttons) { button.Margin = new Thickness(0, 0, 8, 6); row.Children.Add(button); }
        return row;
    }

    public static Border Rule() => new() { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 12, 0, 12) };
    public static void Describe(TextBlock text, string id, AutomationLiveSetting live) { AutomationProperties.SetAutomationId(text, id); AutomationProperties.SetLiveSetting(text, live); }
    // Status and error lines collapse when empty and always wrap, so long messages and paths are never cut off.
    public static void Show(TextBlock text, string? value) { text.Text = value ?? ""; text.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible; }

    // Line glyphs for the section list. The stroke follows the item's foreground, so selection, themes and high contrast stay legible.
    public static FrameworkElement Glyph(string data)
    {
        var path = new Path { Data = Geometry.Parse(data), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Stretch = Stretch.None, IsHitTestVisible = false };
        path.SetBinding(Shape.StrokeProperty, new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(ListBoxItem), 1) });
        return new Viewbox { Width = 16, Height = 16, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };
    }

    // Section list entries, Windows 11 Settings style: a subtle rounded fill on hover, a stronger one plus a short accent bar on the
    // open section, and a rounded ring for keyboard focus only. High contrast uses the system highlight pair and an outline instead.
    // Background is the selection fill and BorderBrush the accent bar; the content keeps the item's own padding.
    private const string Entry = @"<ControlTemplate TargetType='ListBoxItem'><Grid Background='Transparent'>
        <Border x:Name='Fill' Background='{{TemplateBinding Background}}' CornerRadius='4' Opacity='0'/>
        <Border x:Name='Outline' BorderBrush='{{TemplateBinding Foreground}}' BorderThickness='0' CornerRadius='4'/>
        <Rectangle x:Name='Indicator' Width='3' Height='16' RadiusX='1.5' RadiusY='1.5' Fill='{{TemplateBinding BorderBrush}}' HorizontalAlignment='Left' VerticalAlignment='Center' Visibility='Collapsed'/>
        <ContentPresenter Margin='{{TemplateBinding Padding}}' HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}' VerticalAlignment='Center'/>
      </Grid><ControlTemplate.Triggers>{0}</ControlTemplate.Triggers></ControlTemplate>";
    private const string Subtle = @"
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Fill' Property='Opacity' Value='0.45'/></Trigger>
        <Trigger Property='IsSelected' Value='True'><Setter TargetName='Fill' Property='Opacity' Value='0.75'/><Setter TargetName='Indicator' Property='Visibility' Value='Visible'/></Trigger>";
    private const string Contrast = @"
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Outline' Property='BorderThickness' Value='1'/></Trigger>
        <Trigger Property='IsSelected' Value='True'><Setter TargetName='Fill' Property='Opacity' Value='1'/><Setter TargetName='Indicator' Property='Visibility' Value='Visible'/>
          <Setter Property='Foreground' Value='{DynamicResource {x:Static SystemColors.HighlightTextBrushKey}}'/></Trigger>";
    private const string EntryFocus = @"<ControlTemplate TargetType='Control'>
        <Border Margin='-2' BorderThickness='2' BorderBrush='{TemplateBinding Foreground}' CornerRadius='6'/></ControlTemplate>";
    private static Style? entry, contrastEntry;

    // Call again when high contrast changes; ordinary theme changes already follow the bound color tokens.
    public static Style SectionEntry()
    {
        bool hc = SystemParameters.HighContrast;
        if ((hc ? contrastEntry : entry) is { } cached) return cached;
        var focus = new Style(typeof(Control));
        focus.Setters.Add(new Setter(Control.TemplateProperty, Parse<ControlTemplate>(EntryFocus))); focus.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
        var style = new Style(typeof(ListBoxItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, Parse<ControlTemplate>(string.Format(Entry, hc ? Contrast : Subtle))));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, focus));
        // Line is a light translucent tint in both themes, so the fill stays quiet beside the accent bar.
        style.Setters.Add(new Setter(Control.BackgroundProperty, hc ? Ui.Blue : Ui.Line));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, hc ? Ui.OnAccent : Ui.Blue));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 1, 4, 1)));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        return hc ? contrastEntry = style : entry = style;
    }

    private static T Parse<T>(string xaml)
    {
        var context = new ParserContext();
        context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        return (T)XamlReader.Parse(xaml, context);
    }
}
