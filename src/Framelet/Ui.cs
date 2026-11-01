using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace Framelet;

public static class Ui
{
    public static readonly Brush Ink = Brush("#182A43");
    public static readonly Brush Muted = Brush("#60738A");
    public static readonly Brush Blue = Brush("#205FDB");
    public static readonly Brush Paper = Brush("#F3F6FB");
    public static readonly Brush Line = Brush("#DCE4EF");
    public static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public static void Install(Application application)
    {
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
        button.Setters.Add(new Setter(Control.FontSizeProperty, 14d));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 10, 14, 10)));
        button.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        button.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.White));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, Line));
        button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        button.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse("""
          <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
            <Border x:Name="frame" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True" />
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="frame" Property="Opacity" Value="0.8" /></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="#205FDB" /><Setter TargetName="frame" Property="BorderThickness" Value="2" /></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="frame" Property="Opacity" Value="0.4" /></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
          """)));
        application.Resources.Add(typeof(Button), button);
    }

    public static TextBlock Text(string text, double size = 14, Brush? foreground = null, bool bold = false) =>
        new() { Text = text, FontSize = size, Foreground = foreground ?? Ink, FontFamily = new FontFamily("Segoe UI"),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    public static Button Button(string label, string id, Action action, bool accent = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 8, 0), MinHeight = 36 };
        AutomationProperties.SetAutomationId(button, id);
        if (accent) { button.Background = Blue; button.Foreground = Brushes.White; button.BorderBrush = Blue; }
        button.Click += (_, _) => action();
        return button;
    }

    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = Brushes.White, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
        Padding = padding ?? new Thickness(18), Child = child
    };

    public static void Setup(Window window)
    {
        window.FontFamily = new FontFamily("Segoe UI");
        window.Foreground = Ink;
        window.Background = Paper;
    }
}
