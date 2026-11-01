using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace Framelet;

public static class Ui
{
    public static readonly FontFamily Font = new("Segoe UI Variable Text, Segoe UI");
    public static readonly Brush Ink = Appearance.Token(nameof(Appearance.Ink)), Muted = Appearance.Token(nameof(Appearance.Muted)), Blue = Appearance.Token(nameof(Appearance.Accent)),
        OnAccent = Appearance.Token(nameof(Appearance.OnAccent)), Paper = Appearance.Token(nameof(Appearance.Paper)), Surface = Appearance.Token(nameof(Appearance.Surface)),
        Line = Appearance.Token(nameof(Appearance.Line)), Chrome = Appearance.Token(nameof(Appearance.Chrome)), Canvas = Appearance.Token(nameof(Appearance.Canvas)), Glass = Appearance.Token(nameof(Appearance.Glass));
    public static Brush Brush(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;

    public static void Install(Application application)
    {
#pragma warning disable WPF0001 // Built-in Fluent controls and Mica; no third-party UI package.
        application.ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001
        Appearance.Current.Start(application);
        var button = new Style(typeof(Button), application.TryFindResource(typeof(Button)) as Style);
        button.Setters.Add(new Setter(Control.FontFamilyProperty, Font)); button.Setters.Add(new Setter(Control.FontSizeProperty, 13d));
        button.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 5, 12, 5)));
        button.Setters.Add(new Setter(Control.ForegroundProperty, Ink));
        application.Resources[typeof(Button)] = button;
    }
    public static TextBlock Text(string text, double size = 13, Brush? foreground = null, bool bold = false) => new()
    {
        Text = text, FontSize = size, Foreground = foreground ?? Ink, FontFamily = Font, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap
    };
    public static Button Button(string label, string id, Action action, bool accent = false)
    {
        var button = new Button { Content = label, Margin = new Thickness(0, 0, 6, 0), MinHeight = 32, HorizontalContentAlignment = HorizontalAlignment.Center };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label);
        if (accent) { button.Background = Blue; button.Foreground = OnAccent; button.BorderBrush = Line; }
        button.Click += (_, _) => action(); return button;
    }
    public static Border Card(UIElement child, Thickness? padding = null) => new()
    {
        Background = Surface, BorderBrush = Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = padding ?? new Thickness(16), Child = child
    };
    public static void Setup(Window window)
    {
        window.FontFamily = Font; window.FontSize = 14; window.UseLayoutRounding = true; window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display); window.Foreground = Ink; window.Background = Paper;
        if (Application.Current?.MainWindow?.Icon is ImageSource icon) window.Icon = icon;
    }
    public static Button IconButton(string symbol, string label, string id, Action action, bool accent = false)
    {
        var button = Button(label, id, action, accent); button.Content = new UiIcon(symbol); button.ToolTip = label;
        button.Padding = new Thickness(7); button.MinWidth = 34; button.Height = 34; button.Margin = new Thickness(1);
        if (!accent) { button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; } return button;
    }
    public static void UseSystemChrome(Window window)
    {
        // Keep the real Windows caption, system menu, resize borders and Snap flyout.
        window.WindowStyle = WindowStyle.SingleBorderWindow;
        void Background() => window.Background = Appearance.Current.Policy.Frosting ? Brushes.Transparent : Paper;
        Background(); Appearance.Current.Changed += Background; window.Closed += (_, _) => Appearance.Current.Changed -= Background;
    }
}
