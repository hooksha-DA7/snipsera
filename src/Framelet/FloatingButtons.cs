using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;

namespace Framelet;

// Scoped look for the controls laid over a capture. The app-wide Fluent button style is untouched;
// its pointer-over state replaces the fill with a near-transparent brush, which is unreadable over images.
public static class FloatingButtons
{
    public static readonly IValueConverter Radius = new RadiusConverter();
    private const string Round = "{Binding ActualHeight, RelativeSource={RelativeSource TemplatedParent}, Converter={x:Static f:FloatingButtons.Radius}";
    // Material stays put; a Foreground-coloured veil lightens dark controls and darkens light ones, and tints the pinned accent.
    private const string Standard = $@"<ControlTemplate TargetType='Button'><Grid>
        <Border x:Name='Fill' Background='{{TemplateBinding Background}}' CornerRadius='{Round}}}'/>
        <Border x:Name='State' Background='{{TemplateBinding Foreground}}' Opacity='0' CornerRadius='{Round}}}'/>
        <Border BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='{{TemplateBinding BorderThickness}}' CornerRadius='{Round}}}'/>
        <ContentPresenter Margin='{{TemplateBinding Padding}}' HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}' VerticalAlignment='{{TemplateBinding VerticalContentAlignment}}' RecognizesAccessKey='False'/>
      </Grid><ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='State' Property='Opacity' Value='0.1'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='State' Property='Opacity' Value='0.2'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate>";
    // High contrast: no translucency; system Highlight/HighlightText/GrayText and a thicker outline on pointer-over.
    private const string Contrast = $@"<ControlTemplate TargetType='Button'><Grid>
        <Border x:Name='Fill' Background='{{TemplateBinding Background}}' CornerRadius='{Round}}}'/>
        <Border x:Name='Outline' BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='{{TemplateBinding BorderThickness}}' CornerRadius='{Round}}}'/>
        <ContentPresenter Margin='{{TemplateBinding Padding}}' HorizontalAlignment='{{TemplateBinding HorizontalContentAlignment}}' VerticalAlignment='{{TemplateBinding VerticalContentAlignment}}' RecognizesAccessKey='False'/>
      </Grid><ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Outline' Property='BorderThickness' Value='2'/><Setter TargetName='Outline' Property='BorderBrush' Value='{{Binding Foreground, RelativeSource={{RelativeSource TemplatedParent}}}}'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Fill' Property='Background' Value='{{DynamicResource {{x:Static SystemColors.HighlightBrushKey}}}}'/><Setter Property='Foreground' Value='{{DynamicResource {{x:Static SystemColors.HighlightTextBrushKey}}}}'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{{DynamicResource {{x:Static SystemColors.GrayTextBrushKey}}}}'/></Trigger>
      </ControlTemplate.Triggers></ControlTemplate>";
    // Windows 11 style two-tone keyboard focus ring that follows the circle or pill shape. Keyboard only, never on click.
    private const string Focus = $@"<ControlTemplate TargetType='Control'><Grid>
        <Border Margin='-3' BorderThickness='2' BorderBrush='{{TemplateBinding Foreground}}' CornerRadius='{Round}, ConverterParameter=3}}'/>
        <Border Margin='-1' BorderThickness='1' BorderBrush='{{TemplateBinding Background}}' CornerRadius='{Round}, ConverterParameter=1}}'/>
      </Grid></ControlTemplate>";
    private static readonly Dictionary<(bool Accent, bool Contrast), Style> styles = new();
    private static ControlTemplate? standard, contrast;
    private static Style? focus;

    public static void Apply(Button button, bool accent = false)
    {
        bool hc = SystemParameters.HighContrast;
        if (!styles.TryGetValue((accent, hc), out var style)) styles[(accent, hc)] = style = Create(accent, hc);
        button.Style = style;
    }
    private static Style Create(bool accent, bool hc)
    {
        var template = hc ? (contrast ??= Parse<ControlTemplate>(Contrast)) : (standard ??= Parse<ControlTemplate>(Standard));
        var ring = focus;
        if (ring is null)
        {
            ring = focus = new Style(typeof(Control));
            ring.Setters.Add(new Setter(Control.TemplateProperty, Parse<ControlTemplate>(Focus)));
            ring.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink)); ring.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Surface));
        }
        // Style setters rather than local values, so template state triggers can still override them.
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, ring));
        style.Setters.Add(new Setter(Control.BackgroundProperty, accent ? Ui.Blue : Ui.Floating));
        style.Setters.Add(new Setter(Control.ForegroundProperty, accent ? Ui.OnAccent : Ui.Ink));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, accent && !hc ? Brushes.Transparent : hc ? Ui.Ink : Ui.Line));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        return style;
    }
    private static T Parse<T>(string xaml)
    {
        var context = new ParserContext();
        context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        context.XmlnsDictionary.Add("f", "clr-namespace:Framelet;assembly=" + typeof(FloatingButtons).Assembly.GetName().Name);
        return (T)XamlReader.Parse(xaml, context);
    }
    private sealed class RadiusConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double extra = parameter is string text ? double.Parse(text, CultureInfo.InvariantCulture) : 0;
            return new CornerRadius(Math.Max(0, (value is double height ? height : 0) / 2 + extra));
        }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
