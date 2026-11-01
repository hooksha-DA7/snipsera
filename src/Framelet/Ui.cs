using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Shell;

namespace Framelet;

public static class Ui
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly Brush Ink = Brush("#29292D");
    public static readonly Brush Muted = Brush("#7A7A82");
    public static readonly Brush Blue = Brush("#087FF5");
    public static readonly Brush Paper = Brush("#F5F5F7");
    public static readonly Brush Line = Brush("#DEDEE3");
    public static readonly Brush Chrome = Brush("#EBEBEF");
    public static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public static void Install(Application application)
    {
        var button = new Style(typeof(Button));
        button.Setters.Add(new Setter(Control.FontFamilyProperty, Font));
        button.Setters.Add(new Setter(Control.FontSizeProperty, 12.5d));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(11, 6, 11, 6)));
        button.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        button.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        button.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        button.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        button.Setters.Add(new Setter(FrameworkElement.CursorProperty, Cursors.Hand));
        button.Setters.Add(new Setter(Control.TemplateProperty, XamlReader.Parse("""
          <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="Button">
            <Border x:Name="frame" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="{TemplateBinding BorderThickness}" CornerRadius="7" Padding="{TemplateBinding Padding}">
              <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalAlignment="Center" RecognizesAccessKey="True" />
            </Border>
            <ControlTemplate.Triggers>
              <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="#B9B9C1" /></Trigger>
              <Trigger Property="IsPressed" Value="True"><Setter TargetName="frame" Property="Opacity" Value="0.65" /></Trigger>
              <Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="frame" Property="BorderBrush" Value="#087FF5" /></Trigger>
              <Trigger Property="IsEnabled" Value="False"><Setter TargetName="frame" Property="Opacity" Value="0.32" /></Trigger>
            </ControlTemplate.Triggers>
          </ControlTemplate>
          """)));
        application.Resources.Add(typeof(Button), button);
    }

    public static TextBlock Text(string text, double size = 13, Brush? foreground = null, bool bold = false) =>
        new() { Text = text, FontSize = size, Foreground = foreground ?? Ink, FontFamily = Font,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    public static Button Button(string label, string id, Action action, bool accent = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 5, 0), MinHeight = 30, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(button, id);
        AutomationProperties.SetName(button, label);
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
        window.FontFamily = Font; window.FontSize = 13; window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        window.Foreground = Ink;
        window.Background = Paper;
    }

    public static Button IconButton(string symbol, string label, string id, Action action, bool accent = false)
    {
        var button = Button(label, id, action, accent); button.Content = new UiIcon(symbol); button.ToolTip = label;
        button.Padding = new Thickness(6); button.MinWidth = 32; button.Height = 32; button.Margin = new Thickness(1); return button;
    }
    public static void CustomChrome(Window window)
    {
        window.WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(window, new WindowChrome { CaptionHeight = 34, ResizeBorderThickness = new Thickness(6), GlassFrameThickness = new Thickness(0), CornerRadius = new CornerRadius(10), UseAeroCaptionButtons = false });
    }
    public static FrameworkElement TitleBar(Window window, string title)
    {
        var grid = new Grid { Height = 34, Background = Chrome };
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        (string color, string name, Action action)[] items = [("#FF6058", "Close", window.Close), ("#FEBC2E", "Minimize", () => window.WindowState = WindowState.Minimized), ("#29C840", "Maximize", () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized)];
        foreach (var (color, label, action) in items)
        {
            var button = Button(label, "Window" + label, action); button.Width = button.Height = 12; button.MinWidth = button.MinHeight = 0; button.Padding = new Thickness(0); button.Margin = new Thickness(0, 0, 7, 0); button.Background = Brush(color); button.ToolTip = label;
            button.Template = (ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'><Ellipse Fill='{TemplateBinding Background}' /></ControlTemplate>");
            WindowChrome.SetIsHitTestVisibleInChrome(button, true); controls.Children.Add(button);
        }
        grid.Children.Add(controls);
        var caption = Text(title, 12, Muted); caption.HorizontalAlignment = HorizontalAlignment.Center; caption.VerticalAlignment = VerticalAlignment.Center; caption.IsHitTestVisible = false; grid.Children.Add(caption);
        return grid;
    }
}
