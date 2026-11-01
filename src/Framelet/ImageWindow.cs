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
    public bool IsPinned { get; }
    public bool IsDocked { get; private set; } = true;
    public DrawingRect DockWorkArea { get; private set; }
    public double DockScale { get; private set; } = 1;
    public double ImageOpacity { get => thumbnail.Opacity; private set { thumbnail.Opacity = Math.Clamp(value, .2, 1); edge.Opacity = thumbnail.Opacity; } }
    private readonly Image thumbnail;
    private readonly Grid chrome;
    private readonly Border edge = new();
    private readonly TextBlock status = Ui.Text("", 12, Ui.Ink);
    private readonly DispatcherTimer toast = new() { Interval = TimeSpan.FromSeconds(2) };

    public ImageWindow(CaptureApp app, BitmapSource image, CaptureEntry entry, bool pinned, string? warning = null)
    {
        CaptureImage = image; IsPinned = pinned;
        Ui.Setup(this); Title = pinned ? "Framelet — pinned capture" : "Framelet — preview";
        Width = 240; Height = 166; MinWidth = 100; MinHeight = 60;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = true; ShowActivated = false;
        var root = new Grid { Margin = new Thickness(8) };
        var card = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(FloatingVisuals.Radius),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = .18 } };
        var surface = new Grid { Background = Brushes.Transparent };
        surface.SizeChanged += (_, _) => FloatingVisuals.Round(surface, surface.RenderSize);
        thumbnail = new Image { Source = image, Stretch = Stretch.Uniform, ToolTip = pinned ? "Drag to move · double-click to enlarge · Ctrl+wheel for opacity" : "Drag the image to another app · double-click to annotate" };
        RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
        surface.Children.Add(thumbnail);
        chrome = new Grid { Opacity = 0, Background = pinned ? Brushes.Transparent : Ui.Glass };
        var message = new Border { Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 4, 8, 4),
            Child = status, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        var close = Corner("Close", "Close capture", "CloseCapture", Close, HorizontalAlignment.Right, VerticalAlignment.Top);
        var menu = new ContextMenu();
        void Menu(string label, Action action) { var item = new MenuItem { Header = label }; item.Click += (_, _) => { menu.IsOpen = false; Dispatcher.BeginInvoke(action); }; menu.Items.Add(item); }
        Menu("Capture area", () => _ = app.CaptureAsync(CaptureKind.Area)); Menu("Capture window", () => _ = app.CaptureAsync(CaptureKind.Window)); Menu("Capture display", () => _ = app.CaptureAsync(CaptureKind.Screen)); menu.Items.Add(new Separator());
        if (!pinned) Menu("Pin", () => { if (app.Pin(image, entry)) Hide(); });
        Menu("History", app.ShowHome); Menu("Annotate", () => app.EditImage(image));
        var opacity = new Slider { Minimum = .2, Maximum = 1, Value = 1, Width = 150, TickFrequency = .05, Margin = new Thickness(8, 5, 8, 8), ToolTip = "Pinned image opacity" };
        AutomationProperties.SetAutomationId(opacity, "PinOpacity"); opacity.ValueChanged += (_, _) => ImageOpacity = opacity.Value;
        if (pinned) { menu.Items.Add(new Separator()); menu.Items.Add(new MenuItem { Header = Ui.Text("Image opacity · Ctrl+wheel", 12, Ui.Muted), Focusable = false, IsHitTestVisible = false }); menu.Items.Add(new MenuItem { Header = opacity, StaysOpenOnClick = true }); }
        Menu("Close", Close); surface.ContextMenu = menu;
        menu.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { menu.IsOpen = false; e.Handled = true; } };
        var pin = Corner("Pin", pinned ? "Pin options" : "Pin", "PinCapture", () => { if (pinned) menu.IsOpen = true; else if (app.Pin(image, entry)) Hide(); }, HorizontalAlignment.Left, VerticalAlignment.Top);
        var annotate = Corner("Pen", "Annotate", "EditCapture", () => app.EditImage(image), HorizontalAlignment.Left, VerticalAlignment.Bottom);
        var drag = Corner("Drag", "Drag image to another app", "DragCapture", () => Notice("Drag this handle to another app"), HorizontalAlignment.Right, VerticalAlignment.Bottom);
        DragExport.Attach(drag, app.History.Root, () => image, app.Report);
        var actions = new StackPanel { VerticalAlignment = pinned ? VerticalAlignment.Bottom : VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Orientation = pinned ? Orientation.Horizontal : Orientation.Vertical, Margin = new Thickness(0, 0, 0, pinned ? 5 : 0) };
        async void Copy() { try { await CaptureService.Copy(image); Notice("Copied"); } catch (Exception ex) { app.Report(ex); } }
        var copy = Ui.Button("Copy", "CopyCapture", Copy); var save = Ui.Button("Save…", "SaveCapture", () => app.SaveImage(image, this));
        foreach (var button in new[] { copy, save }) { button.Width = pinned ? 28 : 76; button.MinHeight = 28; button.Height = 28; button.Padding = pinned ? new Thickness(5) : new Thickness(8, 2, 8, 2); button.Margin = new Thickness(2); button.Background = Ui.Surface; button.Foreground = Ui.Ink; button.Resources["ControlCornerRadius"] = new CornerRadius(14); actions.Children.Add(button); }
        if (pinned) { copy.Content = new UiIcon("Copy"); save.Content = new UiIcon("Save"); copy.ToolTip = "Copy · Ctrl+C"; save.ToolTip = "Save… · Ctrl+S"; }
        chrome.Children.Add(actions); chrome.Children.Add(pin); chrome.Children.Add(close); chrome.Children.Add(annotate); chrome.Children.Add(drag); surface.Children.Add(chrome);
        edge.BorderBrush = Ui.Line; edge.BorderThickness = new Thickness(.7); edge.CornerRadius = new CornerRadius(FloatingVisuals.Radius); edge.IsHitTestVisible = false; surface.Children.Add(edge);
        surface.Children.Add(message); toast.Tick += (_, _) => { toast.Stop(); message.Visibility = Visibility.Collapsed; };
        void ShowChrome() => RevealActions(surface.IsMouseOver || surface.IsKeyboardFocusWithin || menu.IsOpen);
        surface.MouseEnter += (_, _) => ShowChrome(); surface.MouseLeave += (_, _) => ShowChrome();
        surface.GotKeyboardFocus += (_, _) => ShowChrome(); surface.LostKeyboardFocus += (_, _) => Dispatcher.BeginInvoke(ShowChrome);
        menu.Opened += (_, _) => ShowChrome(); menu.Closed += (_, _) => ShowChrome();
        Appearance.Current.Changed += ShowChrome;
        card.Child = surface; root.Children.Add(card);
        if (pinned)
        {
            var grip = new Thumb { Width = 15, Height = 15, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Opacity = 0, ToolTip = "Resize pinned image", Background = Ui.Chrome };
            grip.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'><Path Stroke='Gray' StrokeThickness='1.5' Data='M 4,12 L 12,4 M 9,12 L 12,9' /></Border></ControlTemplate>");
            grip.DragDelta += (_, e) => ResizeCard(Math.Max(100, Width + e.HorizontalChange));
            root.MouseEnter += (_, _) => grip.Opacity = 1; root.MouseLeave += (_, _) => grip.Opacity = 0; root.Children.Add(grip);
            MouseWheel += (_, e) => { if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { opacity.Value = ImageOpacity + Math.Sign(e.Delta) * .05; e.Handled = true; } };
        }
        else DragExport.Attach(surface, app.History.Root, () => image, app.Report, true);
        Content = root;
        surface.MouseLeftButtonDown += (_, e) =>
        {
            if (DragExport.IsControl(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) { if (pinned) ResizeCard(Width < 350 ? Math.Max(360, Math.Min(600, image.PixelWidth / DockScale + 16)) : 240); else app.EditImage(image); e.Handled = true; return; }
            if (pinned && e.LeftButton == MouseButtonState.Pressed)
            {
                var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var before);
                DragMove(); Native.GetWindowRect(handle, out var after);
                if (before.Left != after.Left || before.Top != after.Top) IsDocked = false;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) Copy();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) app.SaveImage(image, this);
            else if (pinned && e.Key is Key.Left or Key.Right or Key.Up or Key.Down && Keyboard.FocusedElement is not Slider)
            { Nudge(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1); e.Handled = true; }
        };
        Loaded += (_, _) =>
        {
            DockWorkArea = Native.MonitorAtCursor(true); var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, new IntPtr(-1), DockWorkArea.Left + 12, DockWorkArea.Top + 12, 0, 0, 0x0011);
            DockScale = Native.GetDpiForWindow(handle) / 96d;
            if (pinned) app.ReflowPins(); else Place(FloatingLayout.Preview(DockWorkArea, new Size(image.PixelWidth, image.PixelHeight), DockScale), DockScale);
            if (warning is not null) Notice(warning);
        };
        Closed += (_, _) => { toast.Stop(); Appearance.Current.Changed -= ShowChrome; };
        void Notice(string value) { status.Text = value; message.Visibility = Visibility.Visible; toast.Stop(); toast.Start(); }
    }
    public void RevealActions(bool reveal)
    {
        thumbnail.Effect = !IsPinned && reveal && Appearance.Current.Policy.Frosting ? new BlurEffect { Radius = 16, RenderingBias = RenderingBias.Performance } : null;
        var duration = Appearance.Current.Policy.Animate ? TimeSpan.FromMilliseconds(120) : TimeSpan.Zero;
        chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(reveal ? 1 : 0, duration));
    }
    public void Place(DrawingRect bounds, double scale)
    {
        Width = bounds.Width / scale; Height = bounds.Height / scale;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
    }
    private void Nudge(Key key, int step)
    {
        IsDocked = false; var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var current);
        double scale = Native.GetDpiForWindow(handle) / 96d; int amount = (int)Math.Round(step * scale);
        int x = current.Left + (key == Key.Left ? -amount : key == Key.Right ? amount : 0), y = current.Top + (key == Key.Up ? -amount : key == Key.Down ? amount : 0);
        // Use this window's monitor, not the current pointer's monitor, for keyboard movement.
        var work = Native.WorkArea(handle);
        Native.SetWindowPos(handle, new IntPtr(-1), Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - (current.Right - current.Left))), Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - (current.Bottom - current.Top))), 0, 0, 0x0011);
    }
    private void ResizeCard(double width)
    {
        IsDocked = false; var handle = new WindowInteropHelper(this).Handle; var work = Native.WorkArea(handle);
        double scale = Native.GetDpiForWindow(handle) / 96d, ratio = (double)CaptureImage.PixelHeight / CaptureImage.PixelWidth;
        width = Math.Clamp(width, 100, Math.Max(100, work.Width / scale - 24)); double height = (width - 16) * ratio + 16;
        if (height > work.Height / scale - 24) { height = work.Height / scale - 24; width = (height - 16) / ratio + 16; }
        Native.GetWindowRect(handle, out var current); int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        Place(new DrawingRect(Math.Clamp(current.Left, work.Left, work.Right - w), Math.Clamp(current.Top, work.Top, work.Bottom - h), w, h), scale);
    }
    private static Button Corner(string symbol, string label, string id, Action action, HorizontalAlignment horizontal, VerticalAlignment vertical)
    {
        var button = Ui.IconButton(symbol, label, id, action); button.Width = button.Height = 28; button.MinHeight = button.MinWidth = 28; button.Padding = new Thickness(5); button.Margin = new Thickness(5);
        button.HorizontalAlignment = horizontal; button.VerticalAlignment = vertical; button.Background = Ui.Surface; button.Foreground = Ui.Ink; button.Resources["ControlCornerRadius"] = new CornerRadius(14); return button;
    }
}
