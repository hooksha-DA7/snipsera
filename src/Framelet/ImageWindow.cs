using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingRect = System.Drawing.Rectangle;

namespace Framelet;

public sealed class ImageWindow : Window
{
    public BitmapSource CaptureImage { get; }
    public bool IsPinned { get; private set; }
    public bool IsDocked { get; private set; } = true;
    public DrawingRect DockWorkArea { get; private set; }
    public double DockScale { get; private set; } = 1;
    public double ImageOpacity { get => thumbnail.Opacity; private set { thumbnail.Opacity = Math.Clamp(value, .2, 1); edge.Opacity = thumbnail.Opacity; } }
    private readonly Image thumbnail;
    private readonly Border edge = new(), toolbar;
    private readonly Popup actions;
    private readonly ContextMenu menu;
    private readonly Button pinButton;
    private readonly MenuItem pinMenu, opacityLabel, opacityItem;
    private readonly Slider opacity;
    private readonly Thumb grip;
    private readonly TextBlock status = Ui.Text("", 12, Ui.Ink);
    private readonly DispatcherTimer toast = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly DispatcherTimer dismiss = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool keyboardActions;

    public ImageWindow(CaptureApp app, BitmapSource image, CaptureEntry entry, bool pinned, string? warning = null)
    {
        CaptureImage = image;
        Ui.Setup(this); Width = 240; Height = 166; MinWidth = 18; MinHeight = 18;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = true; ShowActivated = false;
        var root = new Grid { Margin = new Thickness(8) };
        var card = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(FloatingVisuals.Radius),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = .18 } };
        var surface = new Grid { Background = Brushes.Transparent };
        surface.SizeChanged += (_, _) => FloatingVisuals.Round(surface, surface.RenderSize);
        thumbnail = new Image { Source = image, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
        surface.Children.Add(thumbnail);
        edge.BorderBrush = Ui.Line; edge.BorderThickness = new Thickness(.7); edge.CornerRadius = new CornerRadius(FloatingVisuals.Radius); edge.IsHitTestVisible = false; surface.Children.Add(edge);
        var message = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
            Child = status, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        surface.Children.Add(message); card.Child = surface; root.Children.Add(card);

        // A separate popup never participates in image measurement or clips its controls to a thin capture.
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        toolbar = new Border { Name = "CaptureActionBar", Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(4), Margin = new Thickness(8), Child = row,
            Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Opacity = .2 } };
        AutomationProperties.SetName(toolbar, "Capture actions");
        KeyboardNavigation.SetTabNavigation(toolbar, KeyboardNavigationMode.Cycle);
        actions = new Popup { PlacementTarget = surface, Placement = PlacementMode.Top, VerticalOffset = 4, AllowsTransparency = true, StaysOpen = true, Child = toolbar };
        root.Children.Add(actions);
        Button Action(string symbol, string label, string id, Action action)
        {
            var button = Ui.IconButton(symbol, label, id, action); button.Width = button.Height = 28; button.MinWidth = button.MinHeight = 28; button.Padding = new Thickness(5); button.Margin = new Thickness(1);
            button.Resources["ControlCornerRadius"] = new CornerRadius(14); row.Children.Add(button); return button;
        }
        void Toggle() { RevealActions(false); app.TogglePin(this); }
        pinButton = Action("Pin", "Pin", "PinCapture", Toggle);
        Action("Pen", "Annotate", "EditCapture", () => { RevealActions(false); app.EditImage(image); });
        async void Copy() { try { await CaptureService.Copy(image); Notice("Copied"); } catch (Exception ex) { app.Report(ex); } }
        Action("Copy", "Copy · Ctrl+C", "CopyCapture", Copy);
        Action("Save", "Save… · Ctrl+S", "SaveCapture", () => { RevealActions(false); app.SaveImage(image, this); });
        var drag = Action("Drag", "Drag image to another app", "DragCapture", () => { });
        DragExport.Attach(drag, app.History.Root, () => image, app.Report);
        Action("Close", "Close capture", "CloseCapture", Close);

        menu = new ContextMenu(); surface.ContextMenu = menu;
        MenuItem Menu(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => { menu.IsOpen = false; RevealActions(false); Dispatcher.BeginInvoke(action); }; menu.Items.Add(item); return item; }
        Menu("Capture area", () => _ = app.CaptureAsync(CaptureKind.Area)); Menu("Capture window", () => _ = app.CaptureAsync(CaptureKind.Window)); Menu("Capture display", () => _ = app.CaptureAsync(CaptureKind.Screen)); menu.Items.Add(new Separator());
        pinMenu = Menu("Pin", Toggle); Menu("History", app.ShowHome); Menu("Annotate", () => app.EditImage(image));
        opacity = new Slider { Minimum = .2, Maximum = 1, Value = 1, Width = 150, TickFrequency = .05, Margin = new Thickness(8, 5, 8, 8), ToolTip = "Pinned image opacity" };
        AutomationProperties.SetAutomationId(opacity, "PinOpacity"); opacity.ValueChanged += (_, _) => ImageOpacity = opacity.Value;
        opacityLabel = new MenuItem { Header = Ui.Text("Image opacity · Ctrl+wheel", 12, Ui.Muted), Focusable = false, IsHitTestVisible = false };
        opacityItem = new MenuItem { Header = opacity, StaysOpenOnClick = true }; menu.Items.Add(new Separator()); menu.Items.Add(opacityLabel); menu.Items.Add(opacityItem); Menu("Close", Close);
        menu.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { menu.IsOpen = false; e.Handled = true; } };
        menu.Opened += (_, _) => RevealActions(false); menu.Closed += (_, _) => ScheduleDismiss();

        grip = new Thumb { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Opacity = 0, ToolTip = "Resize pinned image", Background = Ui.Chrome };
        grip.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'><Path Stroke='Gray' StrokeThickness='1.5' Data='M 4,12 L 12,4 M 9,12 L 12,9' /></Border></ControlTemplate>");
        grip.DragStarted += (_, _) => RevealActions(false); grip.DragDelta += (_, e) => ResizeCard(Math.Max(32, Width + e.HorizontalChange)); root.Children.Add(grip);
        Content = root; SetPinned(pinned);
        void Hover() { dismiss.Stop(); if (!menu.IsOpen && !app.TrayMenuOpen) RevealActions(true); }
        surface.MouseEnter += (_, _) => { Hover(); grip.Opacity = 1; }; surface.MouseLeave += (_, _) => { grip.Opacity = 0; ScheduleDismiss(); };
        toolbar.MouseEnter += (_, _) => Hover(); toolbar.MouseLeave += (_, _) => ScheduleDismiss();
        dismiss.Tick += (_, _) => { dismiss.Stop(); if (Mouse.LeftButton == MouseButtonState.Pressed) { dismiss.Start(); return; } if (!surface.IsMouseOver && !toolbar.IsMouseOver && !(keyboardActions && toolbar.IsKeyboardFocusWithin)) RevealActions(false); };
        toolbar.PreviewMouseDown += (_, _) => keyboardActions = false;
        surface.GotKeyboardFocus += (_, _) => Hover(); toolbar.GotKeyboardFocus += (_, _) => Hover(); toolbar.LostKeyboardFocus += (_, _) => ScheduleDismiss();
        IsVisibleChanged += (_, _) => { if (!IsVisible) { menu.IsOpen = false; RevealActions(false); } };
        LocationChanged += (_, _) => RevealActions(false); Deactivated += (_, _) => { keyboardActions = false; ScheduleDismiss(); };
        DragExport.Attach(surface, app.History.Root, () => image, app.Report, true, () => !IsPinned);
        surface.MouseLeftButtonDown += (_, e) =>
        {
            if (DragExport.IsControl(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) { if (IsPinned) ResizeCard(Width < 350 ? Math.Max(360, Math.Min(600, image.PixelWidth / DockScale + 16)) : FloatingLayout.Preview(DockWorkArea, new Size(image.PixelWidth, image.PixelHeight), DockScale).Width / DockScale); else app.EditImage(image); e.Handled = true; return; }
            if (IsPinned && e.LeftButton == MouseButtonState.Pressed)
            {
                RevealActions(false); var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var before);
                DragMove(); Native.GetWindowRect(handle, out var after);
                if (before.Left != after.Left || before.Top != after.Top) IsDocked = false;
            }
        };
        void HandleKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Tab) { keyboardActions = true; if (!actions.IsOpen) { RevealActions(true); toolbar.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); e.Handled = true; } }
            else if (e.Key == Key.Escape) { if (actions.IsOpen) RevealActions(false); else Close(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) { Copy(); e.Handled = true; }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { RevealActions(false); app.SaveImage(image, this); e.Handled = true; }
            else if (IsPinned && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is not Slider) { Nudge(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1); e.Handled = true; }
        }
        KeyDown += HandleKey; toolbar.KeyDown += HandleKey;
        MouseWheel += (_, e) => { if (IsPinned && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { opacity.Value = ImageOpacity + Math.Sign(e.Delta) * .05; e.Handled = true; } };
        Loaded += (_, _) =>
        {
            DockWorkArea = Native.MonitorAtCursor(true); var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, new IntPtr(-1), DockWorkArea.Left + 12, DockWorkArea.Top + 12, 0, 0, 0x0011); DockScale = Native.GetDpiForWindow(handle) / 96d;
            if (IsPinned) app.ReflowPins(); else Place(FloatingLayout.Preview(DockWorkArea, new Size(image.PixelWidth, image.PixelHeight), DockScale), DockScale);
            if (warning is not null) Notice(warning);
        };
        toast.Tick += (_, _) => { toast.Stop(); message.Visibility = Visibility.Collapsed; };
        Closed += (_, _) => { toast.Stop(); dismiss.Stop(); actions.IsOpen = false; menu.IsOpen = false; };
        void Notice(string value) { status.Text = value; message.Visibility = Visibility.Visible; toast.Stop(); toast.Start(); }
    }

    internal void SetPinned(bool pinned)
    {
        IsPinned = pinned; Title = pinned ? "Framelet — pinned capture" : "Framelet — preview";
        string label = pinned ? "Unpin" : "Pin"; pinMenu.Header = label; pinButton.ToolTip = label; AutomationProperties.SetName(pinButton, label); pinButton.Content = new UiIcon(pinned ? "Unpin" : "Pin");
        pinButton.Background = pinned ? Ui.Blue : Brushes.Transparent; pinButton.Foreground = pinned ? Ui.OnAccent : Ui.Ink;
        opacityLabel.Visibility = opacityItem.Visibility = grip.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
        AutomationProperties.SetHelpText(thumbnail, pinned ? "Drag to move · double-click to enlarge · Ctrl+wheel for opacity" : "Drag the image to another app · double-click to annotate");
        if (!pinned)
        {
            opacity.Value = 1;
            if (IsLoaded)
            {
                var bounds = Native.Bounds(new WindowInteropHelper(this).Handle); int margin = (int)Math.Round(12 * DockScale);
                Place(new DrawingRect(DockWorkArea.Left + margin, DockWorkArea.Bottom - margin - bounds.Height, bounds.Width, bounds.Height), DockScale);
            }
        }
    }
    private void ScheduleDismiss() { dismiss.Stop(); dismiss.Start(); }
    internal void DismissActions() { menu.IsOpen = false; RevealActions(false); }
    public void RevealActions(bool reveal)
    {
        if (!reveal) keyboardActions = false;
        dismiss.Stop(); actions.IsOpen = reveal && IsVisible;
        var duration = Appearance.Current.Policy.Animate ? TimeSpan.FromMilliseconds(100) : TimeSpan.Zero;
        toolbar.BeginAnimation(OpacityProperty, new DoubleAnimation(reveal ? 1 : 0, duration));
    }
    public void Place(DrawingRect bounds, double scale)
    {
        RevealActions(false); Width = bounds.Width / scale; Height = bounds.Height / scale;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
    }
    private void Nudge(Key key, int step)
    {
        IsDocked = false; RevealActions(false); var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var current);
        double scale = Native.GetDpiForWindow(handle) / 96d; int amount = (int)Math.Round(step * scale);
        int x = current.Left + (key == Key.Left ? -amount : key == Key.Right ? amount : 0), y = current.Top + (key == Key.Up ? -amount : key == Key.Down ? amount : 0);
        var work = Native.WorkArea(handle);
        Native.SetWindowPos(handle, new IntPtr(-1), Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - (current.Right - current.Left))), Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - (current.Bottom - current.Top))), 0, 0, 0x0011);
    }
    private void ResizeCard(double width)
    {
        IsDocked = false; var handle = new WindowInteropHelper(this).Handle; var work = Native.WorkArea(handle);
        double scale = Native.GetDpiForWindow(handle) / 96d, ratio = (double)CaptureImage.PixelHeight / CaptureImage.PixelWidth;
        width = Math.Clamp(width, 18, Math.Max(18, work.Width / scale - 24)); double height = (width - 16) * ratio + 16;
        if (height > work.Height / scale - 24) { height = work.Height / scale - 24; width = (height - 16) / ratio + 16; }
        Native.GetWindowRect(handle, out var current); int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        Place(new DrawingRect(Math.Clamp(current.Left, work.Left, work.Right - w), Math.Clamp(current.Top, work.Top, work.Bottom - h), w, h), scale);
    }
}
