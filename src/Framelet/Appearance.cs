using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;

namespace Framelet;

public readonly record struct SurfacePolicy(bool Frosting, bool Animate)
{
    public static SurfacePolicy Resolve(bool highContrast, bool transparency, bool animation) => new(!highContrast && transparency, !highContrast && animation);
}

// Mutable, bound brush colors let existing windows follow Windows preferences without rebuilding documents.
public sealed class Appearance : INotifyPropertyChanged
{
    public static Appearance Current { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action? Changed;
    public Color Ink { get; private set; } = Colors.Black;
    public Color Muted { get; private set; } = Colors.Gray;
    public Color Accent { get; private set; } = Colors.DodgerBlue;
    public Color OnAccent { get; private set; } = Colors.White;
    public Color Paper { get; private set; } = Colors.WhiteSmoke;
    public Color Surface { get; private set; } = Colors.White;
    public Color Chrome { get; private set; } = Colors.WhiteSmoke;
    public Color Line { get; private set; } = Colors.LightGray;
    public Color Canvas { get; private set; } = Colors.LightGray;
    public Color Glass { get; private set; } = Colors.WhiteSmoke;
    public bool Dark { get; private set; }
    public SurfacePolicy Policy { get; private set; }
    public static Brush Token(string property)
    {
        var brush = new SolidColorBrush();
        BindingOperations.SetBinding(brush, SolidColorBrush.ColorProperty, new Binding(property) { Source = Current });
        return brush;
    }
    public void Start(Application app)
    {
        Refresh(); SystemEvents.UserPreferenceChanged += PreferencesChanged; SystemParameters.StaticPropertyChanged += ParametersChanged;
        app.Exit += (_, _) => { SystemEvents.UserPreferenceChanged -= PreferencesChanged; SystemParameters.StaticPropertyChanged -= ParametersChanged; };
    }
    private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();
    private void ParametersChanged(object? sender, PropertyChangedEventArgs e) => QueueRefresh();
    private void QueueRefresh() { var dispatcher = Application.Current?.Dispatcher; if (dispatcher is not null && !dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(Refresh); }
    private static bool Preference(string name, bool fallback)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue(name) is int value ? value != 0 : fallback;
    }
    public void Refresh()
    {
#pragma warning disable WPF0001 // .NET's built-in Fluent theme API is currently marked experimental.
        var mode = Application.Current?.ThemeMode ?? ThemeMode.System;
        Dark = mode == ThemeMode.Dark || mode != ThemeMode.Light && !Preference("AppsUseLightTheme", true);
#pragma warning restore WPF0001
        bool hc = SystemParameters.HighContrast;
        Policy = SurfacePolicy.Resolve(hc, Preference("EnableTransparency", true), SystemParameters.ClientAreaAnimation);
        Color C(string value) => (Color)ColorConverter.ConvertFromString(value);
        Ink = hc ? SystemColors.WindowTextColor : C(Dark ? "#F4F4F4" : "#1B1B1B");
        Muted = hc ? SystemColors.WindowTextColor : C(Dark ? "#B6B6B6" : "#616161");
        Paper = hc ? SystemColors.WindowColor : C(Dark ? "#202020" : "#F3F3F3");
        Surface = hc ? SystemColors.WindowColor : C(Dark ? "#2D2D2D" : "#FFFFFF");
        Chrome = hc ? SystemColors.WindowColor : C(Dark ? "#902B2B2B" : "#A6FFFFFF");
        Line = hc ? SystemColors.WindowTextColor : C(Dark ? "#28FFFFFF" : "#16000000");
        Canvas = hc ? SystemColors.WindowColor : C(Dark ? "#181818" : "#E9E9E9");
        Glass = Policy.Frosting ? C(Dark ? "#70242424" : "#A6F7F7F7") : Surface;
        var accent = Application.Current?.TryFindResource("AccentFillColorDefaultBrush") as SolidColorBrush;
        var onAccent = Application.Current?.TryFindResource("TextOnAccentFillColorPrimaryBrush") as SolidColorBrush;
        Accent = hc ? SystemColors.HighlightColor : accent?.Color ?? C(Dark ? "#60CDFF" : "#0067C0");
        OnAccent = hc ? SystemColors.HighlightTextColor : onAccent?.Color ?? (Dark ? Colors.Black : Colors.White);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); Changed?.Invoke();
    }
}
