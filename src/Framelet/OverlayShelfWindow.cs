using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Framelet;

// Restore tab shown while floating captures are temporarily hidden. A small top-rounded tab sits flush on the bottom-left of the
// saved work area, above the taskbar. Hover rises it to show a frosted hint of the hidden image; a short dwell, a click, or
// Enter/Space restores the original windows. It never activates itself. See design/temporary-hide-2026-11-01/DESIGN.md.
public sealed class OverlayShelfWindow : Window
{
    private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(380), Rise = TimeSpan.FromMilliseconds(140), Entrance = TimeSpan.FromMilliseconds(180);
    private readonly CaptureApp app;
    private readonly Button tab;
    private readonly Grid content = new();
    private readonly Image image = new() { Stretch = Stretch.UniformToFill, IsHitTestVisible = false };
    private readonly Border hint, badge;
    private readonly TextBlock count = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink };
    private readonly ToolTip tip = new() { Placement = PlacementMode.Top };
    private readonly TranslateTransform slide = new();
    private readonly DispatcherTimer dwell = new() { Interval = Dwell };
    // While the pointer is still where the tab appeared, hover must not restore: wait until it has left the tab's area.
    private readonly DispatcherTimer arming = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private double restingHeight = OverlayShelfLayout.RestingHeight, expandedHeight = OverlayShelfLayout.ExpandedHeight;
    private bool expanded;
    private readonly OverlayRestoreHover hover = new();

    // Physical placement of the last layout, for non-desktop geometry checks.
    public OverlayShelfPlacement? Placement { get; private set; }
    public bool IsExpanded => expanded;

    public OverlayShelfWindow(CaptureApp app)
    {
        this.app = app; Ui.Setup(this);
        Title = "Snipsera — hidden captures";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual; SizeToContent = SizeToContent.Manual;

        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        hint = new Border { Child = image, Opacity = 0, IsHitTestVisible = false };
        var chevron = new UiIcon("ChevronUp", 16, 2.2) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
        badge = new Border { Child = count, Background = Ui.Floating, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7), Opacity = 0, IsHitTestVisible = false };
        content.Children.Add(hint); content.Children.Add(chevron); content.Children.Add(badge);
        content.SizeChanged += (_, e) => content.OpacityMask = TopRounded(e.NewSize);

        tab = new Button { Content = content, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, RenderTransform = slide, ToolTip = tip };
        AutomationProperties.SetAutomationId(tab, "RestoreOverlays");
        AutomationProperties.SetHelpText(tab, "Hover, click, or press Enter to bring the hidden captures back.");
        ToolTipService.SetInitialShowDelay(tab, 600);
        var root = new Grid(); root.Children.Add(tab); Content = root;
        Restyle();

        tab.Click += (_, _) => Restore();
        // Hover rises the tab only once armed; a click always restores immediately.
        tab.MouseEnter += (_, _) => { if (!hover.CanRestore(true, Pressed(), app.CanShowOverlayShelf)) return; Expand(true); dwell.Stop(); dwell.Start(); };
        tab.MouseLeave += (_, _) => { dwell.Stop(); if (!tab.IsKeyboardFocused) Expand(false); };
        tab.GotKeyboardFocus += (_, _) => Expand(true);
        tab.LostKeyboardFocus += (_, _) => { if (!tab.IsMouseOver) Expand(false); };
        // Hover restores only after a short, deliberate dwell, and never while a button is held (e.g. dragging a file across it).
        dwell.Tick += (_, _) => { dwell.Stop(); if (hover.CanRestore(tab.IsMouseOver, Pressed(), app.CanShowOverlayShelf)) Restore(); };
        arming.Tick += (_, _) => { bool known = TryPointerInTabArea(out bool inside); hover.Observe(known, inside); if (hover.Armed) arming.Stop(); };
        // If the window is ever activated (not relied on as a keyboard route), focus lands on the tab.
        Activated += (_, _) => tab.Focus();

        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Hook);
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (IsVisible) Place(); });
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Appearance.Current.Changed += Restyle;
        Closing += (_, e) => { if (!app.Quitting) { e.Cancel = true; app.RestoreOverlays(); } };
        Closed += (_, _) => { dwell.Stop(); arming.Stop(); tip.IsOpen = false; SystemEvents.DisplaySettingsChanged -= DisplaysChanged; Appearance.Current.Changed -= Restyle; };
    }

    // Called by CaptureApp on every hide/restore/capture state change.
    public void Refresh()
    {
        if (!app.CanShowOverlayShelf) { Dismiss(); return; }
        int hidden = Math.Max(1, app.HiddenOverlayCount);
        string name = hidden == 1 ? "Show 1 hidden capture" : $"Show {hidden:N0} hidden captures";
        AutomationProperties.SetName(tab, name); tip.Content = name;
        count.Text = hidden.ToString("N0"); badge.Visibility = hidden > 1 ? Visibility.Visible : Visibility.Collapsed;
        image.Source = app.OverlayShelfImage; hint.Visibility = image.Source is null ? Visibility.Collapsed : Visibility.Visible;
        if (!Place()) { Dismiss(); return; }
        if (IsVisible) return;
        Expand(false, true);
        // The tab may appear right under the pointer that just scrolled a capture away. Don't let that hover undo the hide.
        bool known = TryPointerInTabArea(out bool inside); hover.Reset(known, inside);
        if (!hover.Armed) arming.Start();
        Show(); Arrive();
    }

    private void Dismiss()
    {
        dwell.Stop(); arming.Stop(); hover.Reset(); tip.IsOpen = false;
        tab.BeginAnimation(HeightProperty, null); hint.BeginAnimation(OpacityProperty, null); badge.BeginAnimation(OpacityProperty, null); slide.BeginAnimation(TranslateTransform.YProperty, null);
        Expand(false, true); slide.Y = 0;
        if (IsVisible) Hide();
    }

    private void Restore()
    {
        dwell.Stop(); tip.IsOpen = false;
        if (app.CanShowOverlayShelf) app.RestoreOverlays();
    }

    private bool Place()
    {
        var handle = new WindowInteropHelper(this).EnsureHandle();
        var areas = ScrollCaptureLayout.WorkAreas();
        // Live DPI of the monitor the tab will occupy; the saved scale is only a fallback if the monitor can't be queried.
        var work = OverlayShelfLayout.Choose(app.OverlayShelfWorkArea, areas);
        double scale = work.IsEmpty ? app.OverlayShelfScale : ScrollCaptureLayout.ScaleFor(work);
        if (!(scale > 0)) scale = app.OverlayShelfScale > 0 ? app.OverlayShelfScale : 1;
        if (OverlayShelfLayout.Place(app.OverlayShelfWorkArea, areas, scale) is not OverlayShelfPlacement placed) return false;
        Placement = placed;
        var window = placed.Window; var body = placed.Body;
        Width = window.Width / scale; Height = window.Height / scale;
        tab.Width = body.Width / scale; tab.Margin = new Thickness((body.Left - window.Left) / scale, 0, 0, 0);
        restingHeight = placed.Resting.Height / scale; expandedHeight = body.Height / scale;
        if (tab.HasAnimatedProperties) tab.BeginAnimation(HeightProperty, null);
        tab.Height = expanded ? expandedHeight : restingHeight;
        // HWND_TOPMOST, SWP_NOACTIVATE: position without taking focus from the user's app.
        Native.SetWindowPos(handle, new IntPtr(-1), window.Left, window.Top, window.Width, window.Height, 0x0010);
        return true;
    }

    private void Expand(bool on, bool instant = false)
    {
        expanded = on;
        bool hc = SystemParameters.HighContrast, motion = !instant && Appearance.Current.Policy.Animate && IsVisible;
        double height = on ? expandedHeight : restingHeight;
        // High contrast: no image over text-bearing chrome; the count badge carries the information.
        double hintOpacity = on && !hc ? (Appearance.Current.Policy.Frosting ? .62 : 1) : 0, badgeOpacity = on ? 1 : 0;
        if (!motion)
        {
            tab.BeginAnimation(HeightProperty, null); hint.BeginAnimation(OpacityProperty, null); badge.BeginAnimation(OpacityProperty, null);
            tab.Height = height; hint.Opacity = hintOpacity; badge.Opacity = badgeOpacity; return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        tab.BeginAnimation(HeightProperty, new DoubleAnimation(height, Rise) { EasingFunction = ease });
        hint.BeginAnimation(OpacityProperty, new DoubleAnimation(hintOpacity, Rise));
        badge.BeginAnimation(OpacityProperty, new DoubleAnimation(badgeOpacity, Rise));
    }

    // The tab slides up out of the taskbar edge, echoing the stack sliding down. Instant under reduced motion.
    private void Arrive()
    {
        if (!Appearance.Current.Policy.Animate) { slide.Y = 0; return; }
        slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(restingHeight + 4, 0, Entrance) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    private void Restyle()
    {
        bool hc = SystemParameters.HighContrast;
        tab.Style = TabStyle(hc);
        tab.Effect = hc ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Direction = 90, Opacity = .2 };
        hint.Effect = hc || !Appearance.Current.Policy.Frosting ? null : new BlurEffect { Radius = 4 };
        if (IsVisible) Expand(expanded, true);
    }

    // Physical test against the full (expanded) tab body, so a pointer resting just above the short tab still counts as "there".
    private bool TryPointerInTabArea(out bool inside)
    {
        inside = true;
        if (Placement is not OverlayShelfPlacement placed || !Native.GetCursorPos(out var cursor)) return false;
        inside = placed.Body.Contains(cursor.X, cursor.Y); return true;
    }

    private static bool Pressed() => Mouse.LeftButton == MouseButtonState.Pressed || Mouse.RightButton == MouseButtonState.Pressed || Mouse.MiddleButton == MouseButtonState.Pressed;

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_MOUSEACTIVATE → MA_NOACTIVATE: clicking the tab restores without stealing focus from the user's app.
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        // WM_SETTINGCHANGE includes work-area/taskbar changes that need not change display resolution.
        if (message == 0x001A) Dispatcher.BeginInvoke(() => { if (IsVisible && !Place()) Dismiss(); });
        return IntPtr.Zero;
    }

    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (IsVisible && !Place()) Dismiss(); });

    // Rounded top corners only; the bottom edge meets the taskbar edge squarely.
    private static Brush? TopRounded(Size size)
    {
        if (size.Width <= 0 || size.Height <= 0) return null;
        double r = Math.Min(OverlayShelfLayout.Radius, Math.Min(size.Width, size.Height) / 2);
        var shape = new GeometryGroup { FillRule = FillRule.Nonzero };
        shape.Children.Add(new RectangleGeometry(new Rect(size), r, r)); shape.Children.Add(new RectangleGeometry(new Rect(0, r, size.Width, Math.Max(0, size.Height - r))));
        var drawing = new GeometryDrawing(Brushes.White, null, shape); drawing.Freeze();
        var mask = new DrawingBrush(drawing) { ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(size), Stretch = Stretch.Fill }; mask.Freeze();
        return mask;
    }

    private static Style? standard, contrast;
    // Internal for off-screen template checks: parses and styles without any window or native call.
    internal static Style TabStyle(bool highContrast) => highContrast ? (contrast ??= Create(true)) : (standard ??= Create(false));
    private static Style Create(bool hc)
    {
        const string Radius = "8,8,0,0";
        // Same material/veil approach as FloatingButtons, but top-rounded and without a bottom border.
        string template = hc
            ? $@"<ControlTemplate TargetType='Button'><Grid>
                <Border Background='{{TemplateBinding Background}}' CornerRadius='{Radius}'/>
                <ContentPresenter Margin='{{TemplateBinding Padding}}'/>
                <Border x:Name='Outline' BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='1,1,1,0' CornerRadius='{Radius}'/>
              </Grid><ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Outline' Property='BorderThickness' Value='2,2,2,0'/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate>"
            : $@"<ControlTemplate TargetType='Button'><Grid>
                <Border Background='{{TemplateBinding Background}}' CornerRadius='{Radius}'/>
                <ContentPresenter Margin='{{TemplateBinding Padding}}'/>
                <Border x:Name='State' Background='{{TemplateBinding Foreground}}' Opacity='0' CornerRadius='{Radius}'/>
                <Border BorderBrush='{{TemplateBinding BorderBrush}}' BorderThickness='1,1,1,0' CornerRadius='{Radius}'/>
              </Grid><ControlTemplate.Triggers>
                <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='State' Property='Opacity' Value='0.06'/></Trigger>
                <Trigger Property='IsPressed' Value='True'><Setter TargetName='State' Property='Opacity' Value='0.14'/></Trigger>
              </ControlTemplate.Triggers></ControlTemplate>";
        // Two-tone keyboard ring inset from the edges, so the flush bottom never clips it.
        const string focus = @"<ControlTemplate TargetType='Control'><Grid>
                <Border Margin='2' BorderThickness='2' BorderBrush='{TemplateBinding Foreground}' CornerRadius='6,6,0,0'/>
                <Border Margin='4' BorderThickness='1' BorderBrush='{TemplateBinding Background}' CornerRadius='4,4,0,0'/>
              </Grid></ControlTemplate>";
        var ring = new Style(typeof(Control));
        ring.Setters.Add(new Setter(Control.TemplateProperty, Parse<ControlTemplate>(focus)));
        ring.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink)); ring.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Surface));
        var style = new Style(typeof(Button));
        style.Setters.Add(new Setter(Control.TemplateProperty, Parse<ControlTemplate>(template)));
        style.Setters.Add(new Setter(FrameworkElement.FocusVisualStyleProperty, ring));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Floating));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Ink));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, hc ? Ui.Ink : Ui.Line));
        return style;
    }
    private static T Parse<T>(string xaml)
    {
        var context = new ParserContext();
        context.XmlnsDictionary.Add("", "http://schemas.microsoft.com/winfx/2006/xaml/presentation");
        context.XmlnsDictionary.Add("x", "http://schemas.microsoft.com/winfx/2006/xaml");
        return (T)XamlReader.Parse(xaml, context);
    }
}
