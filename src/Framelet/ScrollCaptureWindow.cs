using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Microsoft.Win32;
using PixelRect = System.Drawing.Rectangle;
using PixelSize = System.Drawing.Size;

namespace Framelet;

// Scrolling-capture overlay: one layered, non-activating window over the virtual desktop. The selection plus a 1 px guard stays
// alpha 0, so wheel and clicks reach the app underneath and screen capture never sees Framelet pixels there. The surface is laid
// out in physical pixels; the floating bar and preview are scaled to the selection monitor's DPI. No tooltips: their popups could
// land over the selection. See design/scrolling-2026-11-01/DESIGN.md.
public sealed class ScrollCaptureWindow : Window
{
    // Two 12 px lines at this width hold the backend's longest guidance (~90 characters) without trimming.
    private const double StatusWidth = 280;
    private const string Blocked = "Leave space around the selection for the controls, then try again.";
    private static readonly Brush Danger = Frozen(Color.FromRgb(0xC4, 0x2B, 0x1C)), Dim = Frozen(Color.FromArgb(0x59, 0, 0, 0));
    private readonly ScrollCaptureSession session;
    private readonly Canvas surface = new() { Visibility = Visibility.Hidden };
    private readonly Path shade = new() { Fill = Dim }, ring = new() { IsHitTestVisible = false };
    private readonly Border bar, card;
    private readonly StackPanel actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Ellipse dot = new() { Width = 8, Height = 8, Margin = new Thickness(2, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock statusText, caption;
    private readonly Image preview = new() { Stretch = Stretch.Uniform };
    private readonly Button start, done, cancel, auto;
    private readonly TextBlock doneLabel, cancelLabel;
    private readonly List<PixelRect> fixedObstructions = [];
    private PixelRect previewOuter;
    private ScrollPreviewSlot? slot;
    private System.Drawing.Point origin;
    private double scale = 1;
    private string announced = "";
    private double slack;
    private bool fitQueued;

    // Physical rectangles Framelet paints near the selection: ring strips, bar and preview (each including its shadow).
    // Everything outside HoleBounds is painted (the dim layer); nothing inside it is, unless PlacementError is set before Start.
    public IReadOnlyList<PixelRect> ObstructionBounds => previewOuter.IsEmpty ? fixedObstructions.ToArray() : fixedObstructions.Append(previewOuter).ToArray();
    public PixelRect HoleBounds { get; private set; }
    public string? PlacementError { get; private set; }
    // Actual rendered physical bounds after layout, for regression checks: each visible control must lie inside the bar,
    // and neither may intersect the selection. Empty when hidden.
    public PixelRect RenderedBarBounds => bar.IsVisible ? Physical(bar) : PixelRect.Empty;
    public IReadOnlyList<PixelRect> RenderedControlBounds => Buttons.Where(b => b.IsVisible).Select(Physical).ToArray();

    public ScrollCaptureWindow(ScrollCaptureSession session)
    {
        this.session = session;
        Title = "Framelet — scrolling capture";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = true; Topmost = true; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual; SizeToContent = SizeToContent.Manual;
        FontFamily = Ui.Font; FontSize = 12; Foreground = Ui.Ink;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        if (Application.Current?.MainWindow?.Icon is ImageSource icon) Icon = icon;

        // Shade and ring are drawn on whole physical pixels without antialiasing, so no partial alpha bleeds into the hole.
        RenderOptions.SetEdgeMode(shade, EdgeMode.Aliased); RenderOptions.SetEdgeMode(ring, EdgeMode.Aliased);
        surface.Children.Add(shade); surface.Children.Add(ring);

        statusText = new TextBlock { FontSize = 12, Foreground = Ui.Ink, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 16, LineStackingStrategy = LineStackingStrategy.BlockLineHeight, MaxHeight = 32, VerticalAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(statusText, "ScrollStatus"); AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);
        var status = new DockPanel { Width = StatusWidth, Height = 32, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(dot, Dock.Left); status.Children.Add(dot); status.Children.Add(statusText);

        auto = Pill("", "Auto-scroll", "ScrollAuto", () => Run(() => session.SetAutoScroll(!session.AutoScroll)), out _);
        cancel = Pill("Close", "Cancel", "ScrollCancel", () => { if (Live) Run(session.Cancel); else Close(); }, out cancelLabel);
        start = Pill("Scrolling", "Start capture", "ScrollStart", () => Run(session.Start), out _);
        done = Pill("Check", "Done", "ScrollDone", () => Run(session.Finish), out doneLabel);
        AutomationProperties.SetHelpText(auto, "Scrolls the selected window automatically. You can keep scrolling by hand.");
        AutomationProperties.SetHelpText(start, "Starts capturing. Then scroll the selected content down.");
        foreach (var button in new[] { auto, cancel, start, done }) actions.Children.Add(button);
        for (int i = 0; i < actions.Children.Count; i++) ((Button)actions.Children[i]).TabIndex = i;

        var grid = new Grid();
        // The actions column takes any reserved width beyond the current phase's, so buttons stay flush right.
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.Children.Add(status); grid.Children.Add(actions); Orient(false);
        bar = new Border { Child = grid, Background = Ui.Floating, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 6, 6, 6) };
        AutomationProperties.SetName(bar, "Scrolling capture controls"); KeyboardNavigation.SetTabNavigation(bar, KeyboardNavigationMode.Cycle);

        caption = new TextBlock { FontSize = 11, Foreground = Ui.Muted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.HighQuality); AutomationProperties.SetAutomationId(preview, "ScrollPreview");
        var previewGrid = new Grid();
        previewGrid.RowDefinitions.Add(new RowDefinition()); previewGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(18) });
        Grid.SetRow(caption, 1); previewGrid.Children.Add(preview); previewGrid.Children.Add(caption);
        // Chrome is PreviewChromeX/Y in ScrollCaptureLayout: 1 px border + 4 px padding per side, plus the 18 px caption row.
        card = new Border { Child = previewGrid, Background = Ui.Floating, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(4), Visibility = Visibility.Collapsed };
        surface.Children.Add(card); surface.Children.Add(bar);
        Content = surface;
        Restyle();

        SourceInitialized += (_, _) => { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(Hook); Cover(); };
        Loaded += (_, _) => Relayout();
        // WPF may move/resize the window for a new DPI; cover the desktop again and recompute.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(Relayout);
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        session.Changed += Refresh;
        Appearance.Current.Changed += Restyle;
        // Keyboard users reach the overlay with Alt+Tab; start on the primary action.
        Activated += (_, _) => { if (!bar.IsKeyboardFocusWithin) Primary()?.Focus(); };
        Closed += (_, _) =>
        {
            session.Changed -= Refresh; Appearance.Current.Changed -= Restyle; SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            if (session.Phase is not (ScrollCapturePhase.Completed or ScrollCapturePhase.Cancelled))
                try { session.Cancel(); } catch (ObjectDisposedException) { }
        };
    }

    private bool Live => session.Phase is ScrollCapturePhase.Ready or ScrollCapturePhase.Capturing or ScrollCapturePhase.Finishing;

    private static Button Pill(string symbol, string label, string id, Action action, out TextBlock text)
    {
        text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (symbol.Length > 0) content.Children.Add(new UiIcon(symbol, 14, 2) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(text);
        var button = new Button { Content = content, Height = 32, MinWidth = 32, Padding = new Thickness(symbol.Length > 0 ? 11 : 14, 0, 14, 0), Margin = new Thickness(6, 0, 0, 0),
            FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label);
        button.Click += (_, _) => action();
        return button;
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (InvalidOperationException ex) { statusText.Text = ex.Message; dot.Fill = SystemParameters.HighContrast ? Ui.Ink : Danger; }
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // WM_MOUSEACTIVATE → MA_NOACTIVATE: clicking the controls must not take activation (and wheel focus) from the app being scrolled.
        if (message == 0x0021) { handled = true; return new IntPtr(3); }
        return IntPtr.Zero;
    }

    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(Relayout);

    private void Cover()
    {
        var handle = new WindowInteropHelper(this).Handle; if (handle == IntPtr.Zero) return;
        var desktop = Native.Desktop;
        Native.SetWindowPos(handle, new IntPtr(-1), desktop.Left, desktop.Top, desktop.Width, desktop.Height, 0x0010);
    }

    private void Relayout()
    {
        if (!IsLoaded) return;
        Cover(); Place(); Refresh(); EnsureFit();
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !Native.GetWindowRect(handle, out var window)) return;
        // 1 surface unit = 1 physical pixel, relative to the window's physical origin.
        origin = new System.Drawing.Point(window.Left, window.Top);
        double windowScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        surface.RenderTransform = new ScaleTransform(1 / windowScale, 1 / windowScale);
        var bounds = session.Bounds; scale = ScrollCaptureLayout.ScaleFor(bounds);
        var panelScale = new ScaleTransform(scale, scale); panelScale.Freeze();
        bar.RenderTransform = card.RenderTransform = panelScale;

        HoleBounds = ScrollCaptureLayout.Hole(bounds);
        var frame = ScrollCaptureLayout.Frame(bounds, scale);
        shade.Data = Cutout(window.Rectangle, frame);
        ring.Data = Cutout(frame, HoleBounds);
        fixedObstructions.Clear(); fixedObstructions.AddRange(ScrollCaptureLayout.Ring(bounds, scale));
        slot = null; previewOuter = PixelRect.Empty;

        var work = ScrollCaptureLayout.WorkAreas();
        var (wide, compact) = MeasureBar();
        if (ScrollCaptureLayout.PlaceBar(bounds, work, wide, compact, scale) is ScrollBarPlacement placement)
        {
            PlacementError = null;
            Orient(placement.Compact); var size = placement.Compact ? compact : wide;
            bar.Width = size.Width; bar.Height = size.Height; bar.Visibility = Visibility.Visible;
            Put(bar, placement.Body); fixedObstructions.Add(placement.Outer);
            slot = ScrollCaptureLayout.PreviewSlot(bounds, work, placement.Outer, scale);
        }
        else
        {
            PlacementError = ScrollCaptureLayout.NoRoom;
            if (session.Phase is ScrollCapturePhase.Capturing or ScrollCapturePhase.Finishing)
            {
                // Mid-capture (display change): nothing may be drawn over the selection, so stop rather than contaminate.
                bar.Visibility = Visibility.Collapsed;
                try { session.Cancel(); } catch (ObjectDisposedException) { }
            }
            else
            {
                // Before Start nothing is being captured: explain in place, with only Cancel available.
                Orient(true); bar.Width = compact.Width; bar.Height = compact.Height; bar.Visibility = Visibility.Visible;
                int w = ScrollCaptureLayout.Px(compact.Width, scale), h = ScrollCaptureLayout.Px(compact.Height, scale);
                var desktop = window.Rectangle;
                var body = new PixelRect(Math.Clamp(bounds.Left + (bounds.Width - w) / 2, desktop.Left, Math.Max(desktop.Left, desktop.Right - w)),
                                         Math.Clamp(bounds.Top + (bounds.Height - h) / 2, desktop.Top, Math.Max(desktop.Top, desktop.Bottom - h)), w, h);
                Put(bar, body); fixedObstructions.Add(PixelRect.Inflate(body, ScrollCaptureLayout.Px(ScrollCaptureLayout.Pad, scale), ScrollCaptureLayout.Px(ScrollCaptureLayout.Pad, scale)));
            }
        }
        surface.Visibility = Visibility.Visible;
    }

    // Reserve the largest bar any phase needs, so it never moves or grows over the selection during a capture.
    // A manual Measure on a clean element returns its cached DesiredSize, and visibility/cell/text changes only dirty the
    // child or its direct parent, so the whole bar subtree is invalidated before every measurement.
    private (Size Wide, Size Compact) MeasureBar()
    {
        string label = doneLabel.Text, cancelText = cancelLabel.Text; bar.Visibility = Visibility.Visible;
        Size Fit(bool compact)
        {
            Orient(compact); bar.Width = bar.Height = double.NaN; double w = 0, h = 0;
            foreach (bool ready in new[] { true, false })
            {
                start.Visibility = Vis(ready); done.Visibility = auto.Visibility = Vis(!ready); cancel.Visibility = Visibility.Visible;
                doneLabel.Text = "Finishing…"; cancelLabel.Text = "Cancel";
                Invalidate(bar);
                bar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                w = Math.Max(w, bar.DesiredSize.Width); h = Math.Max(h, bar.DesiredSize.Height);
            }
            return new Size(Math.Ceiling(w + slack), Math.Ceiling(h));
        }
        var result = (Fit(false), Fit(true)); doneLabel.Text = label; cancelLabel.Text = cancelText; Invalidate(bar); return result;
    }
    private static void Invalidate(DependencyObject node)
    {
        if (node is UIElement element) element.InvalidateMeasure();
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Invalidate(VisualTreeHelper.GetChild(node, i));
    }

    // Rendered check: every visible action must lie inside the bar's padded interior. If a font or template measures
    // differently at layout time, widen the reservation and place again (bounded), rather than leave a clipped control.
    private bool BarFits()
    {
        if (bar.Visibility != Visibility.Visible || bar.ActualWidth <= 0) return true;
        var inner = new Rect(bar.Padding.Left + bar.BorderThickness.Left, bar.Padding.Top + bar.BorderThickness.Top,
            Math.Max(0, bar.ActualWidth - bar.Padding.Left - bar.Padding.Right - bar.BorderThickness.Left - bar.BorderThickness.Right),
            Math.Max(0, bar.ActualHeight - bar.Padding.Top - bar.Padding.Bottom - bar.BorderThickness.Top - bar.BorderThickness.Bottom));
        inner.Inflate(.5, .5);
        foreach (var button in Buttons.Where(b => b.Visibility == Visibility.Visible))
        {
            var rect = button.TransformToAncestor(bar).TransformBounds(new Rect(button.RenderSize));
            if (!inner.Contains(rect)) return false;
        }
        return true;
    }
    private void EnsureFit()
    {
        fitQueued = false;
        if (!IsLoaded) return;
        UpdateLayout();
        while (!BarFits() && slack < 96) { slack += 24; Place(); Refresh(); UpdateLayout(); }
    }

    private void Orient(bool compact)
    {
        Grid.SetRow(actions, compact ? 1 : 0); Grid.SetColumn(actions, compact ? 0 : 1); Grid.SetColumnSpan(actions, compact ? 2 : 1);
        actions.Margin = compact ? new Thickness(-6, 6, 0, 0) : new Thickness(4, 0, 0, 0);
    }

    private void Refresh()
    {
        var phase = session.Phase;
        bool capturing = phase is ScrollCapturePhase.Capturing or ScrollCapturePhase.Finishing, blocked = PlacementError is not null;
        start.Visibility = Vis(phase == ScrollCapturePhase.Ready && !blocked);
        done.Visibility = Vis(capturing && !blocked);
        auto.Visibility = Vis(capturing && !blocked && session.CanAutoScroll);
        start.IsEnabled = phase == ScrollCapturePhase.Ready; done.IsEnabled = auto.IsEnabled = phase == ScrollCapturePhase.Capturing;
        cancel.IsEnabled = phase != ScrollCapturePhase.Completed;
        doneLabel.Text = phase == ScrollCapturePhase.Finishing ? "Finishing…" : "Done";
        cancelLabel.Text = Live ? "Cancel" : "Close"; AutomationProperties.SetName(cancel, cancelLabel.Text);
        FloatingButtons.Apply(auto, session.AutoScroll); AutomationProperties.SetItemStatus(auto, session.AutoScroll ? "On" : "Off");

        bool hc = SystemParameters.HighContrast, failed = phase == ScrollCapturePhase.Failed || blocked;
        ring.Fill = hc ? SystemColors.HighlightBrush : failed ? Danger : phase switch
        {
            ScrollCapturePhase.Capturing or ScrollCapturePhase.Completed => Ui.Blue,
            ScrollCapturePhase.Finishing or ScrollCapturePhase.Cancelled => Ui.Muted,
            _ => Brushes.White
        };
        dot.Fill = failed ? (hc ? Ui.Ink : Danger) : phase is ScrollCapturePhase.Capturing or ScrollCapturePhase.Completed ? Ui.Blue : Ui.Muted;

        string text = blocked ? Blocked : !string.IsNullOrWhiteSpace(session.Status) ? session.Status : phase switch
        {
            ScrollCapturePhase.Ready => "Start, then scroll the selected content down.",
            ScrollCapturePhase.Capturing => "Scroll down at a steady pace. Click Done when finished.",
            ScrollCapturePhase.Finishing => "Finishing the capture…",
            ScrollCapturePhase.Failed => "Scrolling capture stopped.",
            _ => ""
        };
        statusText.Text = text;
        RefreshPreview();
        // Phase changes swap actions; confirm the rendered bar still holds them once layout has run.
        if (IsLoaded && !fitQueued) { fitQueued = true; Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(EnsureFit)); }
        if (text != announced)
        {
            announced = text;
            if (IsLoaded) UIElementAutomationPeer.CreatePeerForElement(statusText)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void RefreshPreview()
    {
        previewOuter = PixelRect.Empty;
        if (slot is not ScrollPreviewSlot place || session.Preview is not BitmapSource image || PlacementError is not null) { card.Visibility = Visibility.Collapsed; return; }
        var size = session.Width > 0 && session.Height > 0 ? new PixelSize(session.Width, session.Height) : new PixelSize(image.PixelWidth, image.PixelHeight);
        var body = ScrollCaptureLayout.FitPreview(place, size, scale);
        if (body.IsEmpty) { card.Visibility = Visibility.Collapsed; return; }
        preview.Source = image; card.Width = body.Width / scale; card.Height = body.Height / scale; Put(card, body);
        caption.Text = $"{size.Width:N0} × {size.Height:N0}";
        AutomationProperties.SetName(preview, $"Capture preview, {size.Width:N0} by {size.Height:N0} pixels, {session.Frames:N0} frames");
        int pad = ScrollCaptureLayout.Px(ScrollCaptureLayout.Pad, scale); previewOuter = PixelRect.Inflate(body, pad, pad);
        card.Visibility = Visibility.Visible;
    }

    // High contrast swaps the floating button template and drops shadows; theme colours already follow the bound tokens.
    private void Restyle()
    {
        FloatingButtons.Apply(start, true); FloatingButtons.Apply(done, true); FloatingButtons.Apply(cancel); FloatingButtons.Apply(auto, session.AutoScroll);
        // Shadow stays well inside ScrollCaptureLayout.Pad (14): blur 8 + depth 1.
        bar.Effect = SystemParameters.HighContrast ? null : new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Direction = 270, Opacity = .25 };
        card.Effect = SystemParameters.HighContrast ? null : new DropShadowEffect { BlurRadius = 8, ShadowDepth = 1, Direction = 270, Opacity = .25 };
        if (IsLoaded) Refresh();
    }

    private Button? Primary() => new[] { start, done, cancel }.FirstOrDefault(b => b.IsVisible && b.IsEnabled);
    private Button[] Buttons => [auto, cancel, start, done];
    private static PixelRect Physical(FrameworkElement element)
    {
        Point a = element.PointToScreen(new Point(0, 0)), b = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return PixelRect.FromLTRB((int)Math.Floor(Math.Min(a.X, b.X)), (int)Math.Floor(Math.Min(a.Y, b.Y)), (int)Math.Ceiling(Math.Max(a.X, b.X)), (int)Math.Ceiling(Math.Max(a.Y, b.Y)));
    }
    private void Put(UIElement element, PixelRect body) { Canvas.SetLeft(element, body.X - origin.X); Canvas.SetTop(element, body.Y - origin.Y); }
    private Geometry Cutout(PixelRect outer, PixelRect inner)
    {
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Children.Add(new RectangleGeometry(View(outer))); group.Children.Add(new RectangleGeometry(View(inner)));
        group.Freeze(); return group;
    }
    private Rect View(PixelRect r) => new(r.X - origin.X, r.Y - origin.Y, r.Width, r.Height);
    private static Visibility Vis(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
    private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
}
