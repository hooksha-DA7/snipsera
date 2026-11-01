using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public sealed class ImageWindow : Window
{
    public BitmapSource CaptureImage { get; }
    private readonly TextBlock status;
    public ImageWindow(CaptureApp app, BitmapSource image, CaptureEntry entry, bool pinned, string? warning = null)
    {
        CaptureImage = image;
        Ui.Setup(this);
        Title = pinned ? "Framelet — pinned capture" : "Framelet — preview";
        Width = pinned ? Math.Clamp(image.PixelWidth + 32, 400, 900) : 446;
        Height = pinned ? Math.Clamp(image.PixelHeight + 90, 220, 720) : 300;
        MinWidth = pinned ? 400 : 446; MinHeight = 180; ShowInTaskbar = true; Topmost = true;
        ShowActivated = pinned; ResizeMode = pinned ? ResizeMode.CanResizeWithGrip : ResizeMode.NoResize;
        WindowStyle = WindowStyle.SingleBorderWindow;
        var dock = new DockPanel { Margin = new Thickness(12) };
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var caption = Ui.Text(pinned ? "PINNED CAPTURE" : "CAPTURED", 11, Ui.Blue, true);
        top.Children.Add(caption); DockPanel.SetDock(top, Dock.Top); dock.Children.Add(top);
        var bottom = new StackPanel();
        status = Ui.Text(warning ?? entry.Label, 11, warning is null ? Ui.Muted : Ui.Brush("#9A450E"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        actions.Children.Add(Ui.Button("Copy", "CopyCapture", async () =>
        { try { await CaptureService.Copy(image); status.Text = "Copied to clipboard"; } catch (Exception ex) { app.Report(ex); } }, true));
        actions.Children.Add(Ui.Button("Save…", "SaveCapture", () => app.SaveImage(image, this)));
        if (!pinned) actions.Children.Add(Ui.Button("Pin", "PinCapture", () => app.Pin(image, entry)));
        actions.Children.Add(Ui.Button("Edit", "EditCapture", () =>
        { try { app.EditImage(image); } catch (Exception ex) { app.Report(ex); } }));
        actions.Children.Add(Ui.Button("History", "OpenHistory", app.ShowHome));
        status.Margin = new Thickness(0, 8, 0, 0);
        bottom.Children.Add(actions); bottom.Children.Add(status);
        DockPanel.SetDock(bottom, Dock.Bottom); dock.Children.Add(bottom);
        dock.Children.Add(new Border { Background = Ui.Brush("#E5EBF4"), CornerRadius = new CornerRadius(6), ClipToBounds = true,
            Child = new Image { Source = image, Stretch = Stretch.Uniform, ToolTip = entry.Label } });
        Content = dock;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C) actions.Children[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) app.SaveImage(image, this);
        };
        Loaded += (_, _) =>
        {
            var work = Native.MonitorAtCursor(true);
            var dpi = VisualTreeHelper.GetDpi(this);
            int width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX), height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
            int x = pinned ? work.Left + 32 : work.Right - width - 20;
            int y = pinned ? work.Top + 32 : work.Bottom - height - 20;
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), Math.Max(work.Left, x), Math.Max(work.Top, y),
                Math.Min(width, work.Width), Math.Min(height, work.Height), 0x0010);
        };
    }
}
