using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class ScrollingFixture
{
    public static int Run(string output)
    {
        Directory.CreateDirectory(output); Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app = new Application(); Ui.Install(app);
        var backdrop = new Window { Title = "Snipsera scrolling test backdrop", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Background = Ui.Brush("#31475E") };
        backdrop.Loaded += (_, _) => { var area = Native.MonitorAtCursor(true); Native.SetWindowPos(new WindowInteropHelper(backdrop).Handle, IntPtr.Zero, area.X, area.Y, area.Width, area.Height, 0x0010); };
        backdrop.Show();
        var page = new DrawingVisual();
        using (var dc = page.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 760, 5760));
            for (int row = 0; row < 60; row++)
            {
                var tone = new SolidColorBrush(Color.FromRgb((byte)(215 + row * 11 % 39), (byte)(216 + row * 17 % 38), (byte)(218 + row * 23 % 36))); tone.Freeze();
                dc.DrawRectangle(tone, null, new Rect(0, row * 96, 760, 95));
                dc.DrawText(new FormattedText($"{row + 1:00}  Snipsera scrolling test", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Font, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 24, Ui.Brush("#24342D"), 1.25), new Point(26, row * 96 + 15));
                dc.DrawText(new FormattedText($"Source row {row + 1}; every numbered section must appear once.", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Font, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 14, Ui.Brush("#617775"), 1.25), new Point(28, row * 96 + 53));
                for (int x = 0; x < 12; x++)
                {
                    uint code = unchecked((uint)(row + 1) * 2246822519u ^ (uint)x * 2654435761u);
                    var ink = new SolidColorBrush(Color.FromRgb((byte)code, (byte)(code >> 8), (byte)(code >> 16))); ink.Freeze();
                    dc.DrawRectangle(ink, null, new Rect(540 + x * 16, row * 96 + 68, 12, 12));
                }
            }
        }
        var bitmap = new RenderTargetBitmap(760, 5760, 96, 96, PixelFormats.Pbgra32); bitmap.Render(page); bitmap.Freeze();
        CaptureService.Save(bitmap, Path.Combine(output, "source-page.png"));
        var window = new Window { Title = "Snipsera scrolling test target", Width = 860, Height = 740, WindowStartupLocation = WindowStartupLocation.CenterScreen }; Ui.Setup(window); Ui.UseSystemChrome(window);
        app.MainWindow = window; window.Closed += (_, _) => backdrop.Close();
        var root = new DockPanel { Margin = new Thickness(20) };
        var label = Ui.Text("Scroll through 60 numbered sections · synthetic test data", 16, Ui.Ink, true); label.Margin = new Thickness(0, 0, 0, 16); DockPanel.SetDock(label, Dock.Top); root.Children.Add(label);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = new Image { Source = bitmap, Stretch = Stretch.None, HorizontalAlignment = HorizontalAlignment.Left }, Background = Brushes.White };
        root.Children.Add(scroll); window.Content = root;
        void Record()
        {
            var point = scroll.PointToScreen(new Point(0, 0)); var dpi = VisualTreeHelper.GetDpi(scroll);
            File.WriteAllText(Path.Combine(output, "viewport.json"), JsonSerializer.Serialize(new { offset = scroll.VerticalOffset, viewportHeight = scroll.ViewportHeight, x = point.X, y = point.Y, width = scroll.ActualWidth * dpi.DpiScaleX, height = scroll.ActualHeight * dpi.DpiScaleY, dpi = dpi.PixelsPerInchX }, new JsonSerializerOptions { WriteIndented = true }));
        }
        scroll.ScrollChanged += (_, _) => Record(); window.Loaded += (_, _) => Record();
        window.KeyDown += (_, e) =>
        {
            if (e.Key != Key.F9) return;
            Record(); var point = scroll.PointToScreen(new Point(0, 0)); var dpi = VisualTreeHelper.GetDpi(scroll);
            CaptureService.Save(CaptureService.Capture(new System.Drawing.Rectangle((int)Math.Round(point.X), (int)Math.Round(point.Y), (int)Math.Round(scroll.ActualWidth * dpi.DpiScaleX), (int)Math.Round(scroll.ActualHeight * dpi.DpiScaleY))), Path.Combine(output, "native-viewport.png"));
            CaptureService.Save(CaptureService.Capture(Native.WorkArea(new WindowInteropHelper(window).Handle)), Path.Combine(output, "native-workspace.png"));
        };
        return app.Run(window);
    }
}
