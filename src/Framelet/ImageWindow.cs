using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingRect = System.Drawing.Rectangle;

namespace Framelet;

public sealed class ImageWindow : Window
{
    public BitmapSource CaptureImage { get; }
    public bool IsPinned => pinned;
    public bool IsDocked { get; private set; } = true;
    public DrawingRect DockWorkArea { get; private set; }
    public double DockScale { get; private set; } = 1;
    private readonly bool pinned;
    private readonly TextBlock status = Ui.Text("", 11, Brushes.White);
    private readonly DispatcherTimer toast = new() { Interval = TimeSpan.FromSeconds(2) };

    public ImageWindow(CaptureApp app, BitmapSource image, CaptureEntry entry, bool pinned, string? warning = null)
    {
        CaptureImage = image; this.pinned = pinned;
        Ui.Setup(this); Title = pinned ? "Framelet — pinned capture" : "Framelet — preview";
        Width = 240; Height = 166; MinWidth = 100; MinHeight = 60;
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true; Topmost = true; ShowActivated = false;
        var root = new Grid { Margin = new Thickness(8) };
        var card = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(9),
            Effect = new DropShadowEffect { BlurRadius = 13, ShadowDepth = 2, Opacity = .24 } };
        var surface = new Grid { Background = Brushes.White };
        surface.SizeChanged += (_, _) => surface.Clip = new RectangleGeometry(new Rect(surface.RenderSize), 9, 9);
        surface.Children.Add(new Image { Source = image, Stretch = Stretch.Uniform, ToolTip = entry.Label });
        var chrome = new Grid { Opacity = 0, Background = Brushes.Transparent };
        var top = new DockPanel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(4) };
        var close = Control("Close", "Close capture", "CloseCapture", Close); close.Background = Ui.Brush("#99242429"); DockPanel.SetDock(close, Dock.Right); top.Children.Add(close);
        var history = Control("History", "History", "OpenHistory", app.ShowHome); history.HorizontalAlignment = HorizontalAlignment.Left; history.Background = Ui.Brush("#99242429"); top.Children.Add(history); chrome.Children.Add(top);
        var actions = new StackPanel { Orientation = Orientation.Horizontal };
        var message = new Border { Background = Ui.Brush("#DD24242A"), CornerRadius = new CornerRadius(5), Padding = new Thickness(7, 3, 7, 3),
            Child = status, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
        async void Copy() { try { await CaptureService.Copy(image); Notice("Copied"); } catch (Exception ex) { app.Report(ex); } }
        actions.Children.Add(Control("Copy", "Copy", "CopyCapture", Copy));
        actions.Children.Add(Control("Save", "Save…", "SaveCapture", () => app.SaveImage(image, this)));
        if (!pinned) actions.Children.Add(Control("Pin", "Pin", "PinCapture", () => { app.Pin(image, entry); Hide(); }));
        actions.Children.Add(Control("Pen", "Annotate", "EditCapture", () => app.EditImage(image)));
        chrome.Children.Add(new Border { Child = actions, Background = Ui.Brush("#D9242429"), CornerRadius = new CornerRadius(8), Padding = new Thickness(2), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) }); surface.Children.Add(chrome);
        surface.Children.Add(message);
        toast.Tick += (_, _) => { toast.Stop(); message.Visibility = Visibility.Collapsed; };
        void ShowChrome(bool show) => chrome.Opacity = show ? 1 : 0;
        surface.MouseEnter += (_, _) => ShowChrome(true); surface.MouseLeave += (_, _) => ShowChrome(false);
        surface.GotKeyboardFocus += (_, _) => ShowChrome(true); surface.LostKeyboardFocus += (_, _) => { if (!surface.IsMouseOver) ShowChrome(false); };
        card.Child = surface; root.Children.Add(card);
        if (pinned)
        {
            var grip = new Thumb { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom, Cursor = Cursors.SizeNWSE, Opacity = 0, ToolTip = "Drag to resize; double-click image to enlarge" };
            grip.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Thumb'><Border Background='Transparent'><Path Stroke='White' StrokeThickness='2' Data='M 5,13 L 13,5 M 10,13 L 13,10' /></Border></ControlTemplate>");
            grip.DragDelta += (_, e) => ResizeCard(Math.Max(100, Width + e.HorizontalChange));
            root.MouseEnter += (_, _) => grip.Opacity = 1; root.MouseLeave += (_, _) => grip.Opacity = 0; root.Children.Add(grip);
        }
        Content = root;
        surface.MouseLeftButtonDown += (_, e) =>
        {
            if (FromControl(e.OriginalSource as DependencyObject)) return;
            if (e.ClickCount == 2) { if (pinned) ResizeCard(Width < 350 ? Math.Max(360, Math.Min(600, image.PixelWidth / DockScale + 16)) : 240); else app.EditImage(image); e.Handled = true; return; }
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var handle = new WindowInteropHelper(this).Handle; Native.GetWindowRect(handle, out var before);
                DragMove(); Native.GetWindowRect(handle, out var after);
                if (pinned && (before.Left != after.Left || before.Top != after.Top)) IsDocked = false;
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) Copy();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) app.SaveImage(image, this);
        };
        Loaded += (_, _) =>
        {
            DockWorkArea = Native.MonitorAtCursor(true);
            var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, new IntPtr(-1), DockWorkArea.Left + 12, DockWorkArea.Top + 12, 0, 0, 0x0011);
            DockScale = Native.GetDpiForWindow(handle) / 96d;
            if (pinned) app.ReflowPins();
            else Place(FloatingLayout.Preview(DockWorkArea, new Size(image.PixelWidth, image.PixelHeight), DockScale), DockScale);
            if (warning is not null) Notice(warning);
        };
        Closed += (_, _) => toast.Stop();
        void Notice(string value) { status.Text = value; message.Visibility = Visibility.Visible; toast.Stop(); toast.Start(); }
    }

    public void Place(DrawingRect bounds, double scale)
    {
        Width = bounds.Width / scale; Height = bounds.Height / scale;
        Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), bounds.X, bounds.Y, bounds.Width, bounds.Height, 0x0010);
    }
    private void ResizeCard(double width)
    {
        IsDocked = false;
        var work = Native.MonitorAtCursor(true); var handle = new WindowInteropHelper(this).Handle;
        double scale = Native.GetDpiForWindow(handle) / 96d;
        double ratio = (double)CaptureImage.PixelHeight / CaptureImage.PixelWidth;
        width = Math.Clamp(width, 100, Math.Max(100, work.Width / scale - 24));
        double height = (width - 16) * ratio + 16;
        if (height > work.Height / scale - 24) { height = work.Height / scale - 24; width = (height - 16) / ratio + 16; }
        Native.GetWindowRect(handle, out var current);
        int w = (int)Math.Round(width * scale), h = (int)Math.Round(height * scale);
        Place(new DrawingRect(Math.Clamp(current.Left, work.Left, work.Right - w), Math.Clamp(current.Top, work.Top, work.Bottom - h), w, h), scale);
    }
    private static Button Control(string symbol, string label, string id, Action action)
    {
        var button = Ui.IconButton(symbol, label, id, action); button.Foreground = Brushes.White; button.Width = button.Height = 29; button.MinHeight = button.MinWidth = 29; button.Padding = new Thickness(5); return button;
    }
    private static bool FromControl(DependencyObject? source)
    {
        while (source is not null) { if (source is ButtonBase) return true; source = VisualTreeHelper.GetParent(source); } return false;
    }
}
