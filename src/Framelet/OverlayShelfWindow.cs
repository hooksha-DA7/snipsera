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
// saved work area, above the taskbar. Hover lifts the small chevron/count handle; a short dwell, a click, or
// Enter/Space restores the original windows. It never activates itself. See design/temporary-hide-2026-11-01/DESIGN.md.
public sealed class OverlayShelfWindow : Window
{
    private static readonly TimeSpan Dwell = TimeSpan.FromMilliseconds(320), Rise = TimeSpan.FromMilliseconds(120), Entrance = TimeSpan.FromMilliseconds(160);
    private readonly CaptureApp app;
    private readonly Button tab;
    private readonly Border hitTarget;
    private readonly TextBlock count = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.Ink };
    private readonly ToolTip tip = new() { Placement = PlacementMode.Top };
    private readonly TranslateTransform slide = new();
    private readonly DispatcherTimer dwell = new() { Interval = Dwell };
    // While the pointer is still where the tab appeared, hover must not restore: wait until it has left the tab's area.
    private readonly DispatcherTimer arming = new() { Interval = TimeSpan.FromMilliseconds(120) };
    private double restingHeight = OverlayShelfLayout.RestingHeight, expandedHeight = OverlayShelfLayout.ExpandedHeight;
    private bool expanded;
    private bool pressedOnTarget;
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

        tab = new Button { Content = CreateContent(count), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch, RenderTransform = slide };
        // A very faint hit surface gives the small visible handle a 28 px target, without a large expanding thumbnail.
        hitTarget = new Border { Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)), Child = tab,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, ToolTip = tip };
        AutomationProperties.SetAutomationId(hitTarget, "RestoreOverlaysTarget");
        AutomationProperties.SetAutomationId(tab, "RestoreOverlays");
        AutomationProperties.SetHelpText(tab, "Hover, click, or press Enter to bring the hidden captures back.");
        ToolTipService.SetInitialShowDelay(hitTarget, 600);
        var root = new Grid(); root.Children.Add(hitTarget); Content = root;
        Restyle();

        tab.Click += (_, _) => Restore();
        hitTarget.MouseLeftButtonDown += (_, e) => { pressedOnTarget = hitTarget.CaptureMouse(); e.Handled = true; };
        hitTarget.MouseLeftButtonUp += (_, e) =>
        {
            bool restore = pressedOnTarget && hitTarget.IsMouseOver; pressedOnTarget = false;
            if (hitTarget.IsMouseCaptured) hitTarget.ReleaseMouseCapture();
            if (restore) Restore(); e.Handled = true;
        };
        hitTarget.LostMouseCapture += (_, _) => pressedOnTarget = false;
        // Hover rises the tab only once armed; a click always restores immediately.
        hitTarget.MouseEnter += (_, _) => { if (!hover.CanRestore(true, Pressed(), app.CanShowOverlayShelf)) return; Expand(true); dwell.Stop(); dwell.Start(); };
        hitTarget.MouseLeave += (_, _) => { dwell.Stop(); if (!tab.IsKeyboardFocused) Expand(false); };
        tab.GotKeyboardFocus += (_, _) => Expand(true);
        tab.LostKeyboardFocus += (_, _) => { if (!hitTarget.IsMouseOver) Expand(false); };
        // Hover restores only after a short, deliberate dwell, and never while a button is held (e.g. dragging a file across it).
        dwell.Tick += (_, _) => { dwell.Stop(); if (hover.CanRestore(hitTarget.IsMouseOver, Pressed(), app.CanShowOverlayShelf)) Restore(); };
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
        count.Text = hidden.ToString("N0"); count.Visibility = hidden > 1 ? Visibility.Visible : Visibility.Collapsed;
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
        pressedOnTarget = false; if (hitTarget.IsMouseCaptured) hitTarget.ReleaseMouseCapture();
        dwell.Stop(); arming.Stop(); hover.Reset(); tip.IsOpen = false;
        tab.BeginAnimation(HeightProperty, null); slide.BeginAnimation(TranslateTransform.YProperty, null);
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
        hitTarget.Width = tab.Width = body.Width / scale; hitTarget.Height = body.Height / scale;
        hitTarget.Margin = new Thickness((body.Left - window.Left) / scale, 0, 0, 0);
        restingHeight = placed.Resting.Height / scale;
        expandedHeight = Math.Min(body.Height / scale, OverlayShelfLayout.ExpandedHeight);
        if (tab.HasAnimatedProperties) tab.BeginAnimation(HeightProperty, null);
        tab.Height = expanded ? expandedHeight : restingHeight;
        // HWND_TOPMOST, SWP_NOACTIVATE: position without taking focus from the user's app.
        Native.SetWindowPos(handle, new IntPtr(-1), window.Left, window.Top, window.Width, window.Height, 0x0010);
        return true;
    }

    private void Expand(bool on, bool instant = false)
    {
        expanded = on;
        bool motion = !instant && Appearance.Current.Policy.Animate && IsVisible;
        double height = on ? expandedHeight : restingHeight;
        if (!motion)
        {
            tab.BeginAnimation(HeightProperty, null); tab.Height = height; return;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        tab.BeginAnimation(HeightProperty, new DoubleAnimation(height, Rise) { EasingFunction = ease });
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
        tab.Effect = hc ? null : new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Direction = 90, Opacity = .18 };
        if (IsVisible) Expand(expanded, true);
    }

    // Include the pointer target above the visible handle when arming hover restoration.
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

    // Shared with detached rendering checks so they exercise the production content, including count alignment.
    internal static Grid CreateContent(TextBlock count)
    {
        count.FontSize = 11; count.FontWeight = FontWeights.SemiBold; count.Foreground = Ui.Ink;
        count.VerticalAlignment = VerticalAlignment.Center; count.Margin = new Thickness(4, 0, 0, 0);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(new UiIcon("ChevronUp", 12, 2) { VerticalAlignment = VerticalAlignment.Center }); row.Children.Add(count);
        var content = new Grid(); content.Children.Add(row); return content;
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
                <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Outline' Property='BorderThickness' Value='2,2,2,0'/></Trigger>
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
