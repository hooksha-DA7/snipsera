using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet;

internal static class UiFixture
{
    // A separate process with synthetic data for repeatable native UI inspection.
    public static int Run(string output, string mode = "system")
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app = new CaptureApp(Path.Combine(output, "data"));
#pragma warning disable WPF0001
        app.ThemeMode = mode == "dark" ? ThemeMode.Dark : mode == "light" ? ThemeMode.Light : ThemeMode.System;
#pragma warning restore WPF0001
        Appearance.Current.Refresh();
        app.Startup += (_, _) =>
        {
            var backdrop = new DropPad(output); backdrop.Show();
            var image = Scene(); CaptureService.Save(image, Path.Combine(output, "sample.png"));
            var entry = app.History.Add(image, CaptureKind.Window);
            var doc = new EditDocument(image);
            doc.Add(new Mark(MarkKind.Arrow, [new(620, 105), new(505, 275)], "#F34461", 5, Style: MarkStyle.Sketch, Arrow: ArrowStyle.Curved));
            doc.Add(new Mark(MarkKind.Ellipse, [new(72, 76), new(410, 145)], "#F34461", 4, Style: MarkStyle.Sketch));
            doc.Add(new Mark(MarkKind.Text, [new(572, 70)], "#F34461", Text: "A little more focus", FontSize: 23, Style: MarkStyle.Sketch));
            EditFiles.SaveProject(doc, Path.Combine(output, "sample.framelet"));
            var editor = new EditorWindow(doc, bitmap => app.Pin(bitmap, entry), app.OpenImageOrProject, "Design review"); editor.Show();
            // Harness-only keys exercise the production tray path and save native evidence.
            InputManager.Current.PreProcessInput += (_, e) =>
            {
                if (e.StagingItem.Input is not KeyEventArgs key || key.RoutedEvent != Keyboard.KeyDownEvent) return;
                var pressed = key.Key == Key.System ? key.SystemKey : key.Key;
                if (pressed == Key.F11)
                {
                    app.Dispatcher.BeginInvoke(app.ShowTrayMenu); key.Handled = true;
                    var proof = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                    proof.Tick += (_, _) => { proof.Stop(); SaveProof(); }; proof.Start();
                }
                else if (pressed == Key.F9) SaveProof();
            };
            void SaveProof()
            {
                foreach (var window in app.Windows.Cast<Window>().Where(w => w.IsVisible && w is not DropPad))
                {
                    var handle = new WindowInteropHelper(window).Handle;
                    CaptureService.Save(CaptureService.Capture(Native.Bounds(handle)), Path.Combine(output, "proof-" + handle + ".png"));
                }
                foreach (var source in PresentationSource.CurrentSources.OfType<HwndSource>().Where(s => s.RootVisual?.GetType().Name == "PopupRoot"))
                    CaptureService.Save(CaptureService.Capture(Native.Bounds(source.Handle)), Path.Combine(output, "popup-" + source.Handle + ".png"));
            }
            app.MainWindow.Hide(); app.OpenEntry(entry);
            app.Pin(image, entry); app.Pin(EditRenderer.Render(doc), entry); app.Pin(new CroppedBitmap(image, new Int32Rect(60, 65, 540, 310)), entry);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) =>
            {
                timer.Stop(); editor.UpdateLayout();
                SaveVisual(editor, Path.Combine(output, "editor-ui.png"));
                var editorHandle = new WindowInteropHelper(editor).Handle;
                if (Native.GetForegroundWindow() == editorHandle)
                {
                    CaptureService.Save(CaptureService.Capture(Native.Bounds(editorHandle)), Path.Combine(output, "editor-native.png"));
                    // The full-work-area synthetic backdrop prevents unrelated desktop content entering this proof.
                    CaptureService.Save(CaptureService.Capture(Native.WorkArea(editorHandle)), Path.Combine(output, "workspace-native.png"));
                }
                foreach (var window in app.Windows.Cast<Window>().OfType<ImageWindow>()) SaveVisual(window, Path.Combine(output, (window.Title.Contains("preview") ? "preview" : "pin-" + new WindowInteropHelper(window).Handle) + ".png"));
                var windows = app.Windows.Cast<Window>().Where(w => w.IsVisible).Select(w =>
                {
                    var handle = new WindowInteropHelper(w).Handle; Native.GetWindowRect(handle, out var r);
                    Native.DwmGetWindowAttribute(handle, 38, out int material, sizeof(int));
                    return new { w.Title, x = r.Left, y = r.Top, width = r.Right - r.Left, height = r.Bottom - r.Top, dpi = Native.GetDpiForWindow(handle), backdropType = material, pinned = w is ImageWindow { IsPinned: true }, sourceWidth = (w as ImageWindow)?.CaptureImage.PixelWidth };
                });
                File.WriteAllText(Path.Combine(output, "native-windows.json"), JsonSerializer.Serialize(windows, new JsonSerializerOptions { WriteIndented = true }));
                var preview = app.Windows.Cast<Window>().OfType<ImageWindow>().Single(w => !w.IsPinned);
                preview.RevealActions(true);
                var hover = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                hover.Tick += (_, _) => { hover.Stop(); SaveVisual(preview, Path.Combine(output, "preview-hover.png")); };
                hover.Start();
            }; timer.Start(); editor.Activate();
        };
        return app.Run();
    }
    private static void SaveVisual(Window window, string path)
    {
        var content = (FrameworkElement)window.Content; var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        if (window is EditorWindow)
        { var baseLayer = new DrawingVisual(); using (var dc = baseLayer.RenderOpen()) dc.DrawRectangle(Ui.Paper, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight)); bitmap.Render(baseLayer); }
        bitmap.Render(content); CaptureService.Save(bitmap, path);
    }
    private static BitmapSource Scene()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            void Box(string color, double x, double y, double w, double h, double r = 0) => dc.DrawRoundedRectangle(Ui.Brush(color), null, new Rect(x, y, w, h), r, r);
            void Text(string value, double x, double y, double size, string color, bool bold = false) => dc.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(Ui.Font, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal), size, Ui.Brush(color), 1), new Point(x, y));
            Box("#D8E5DD", 0, 0, 900, 560); Box("#FFFFFF", 38, 32, 824, 496, 14);
            Text("STUDIO NOTES", 74, 51, 11, "#8D9691", true); Text("Make room for good ideas.", 74, 91, 26, "#263F31", true);
            Text("A quieter workspace, a clearer point of view.", 76, 151, 14, "#77837B");
            Box("#F1F3ED", 74, 202, 356, 242, 9); Box("#BBCDB7", 94, 222, 316, 126, 7);
            Text("01  /  Exploration", 96, 365, 18, "#334A36", true); Text("Collect the details that matter.", 96, 398, 13, "#7A857B");
            Box("#F5EFE6", 450, 202, 336, 242, 9); Box("#DAC7AB", 470, 222, 296, 126, 7);
            Text("02  /  Direction", 472, 365, 18, "#5B4B35", true); Text("Turn a small observation into a useful next step.", 472, 398, 12, "#8B7A65");
            Text("All notes", 76, 477, 12, "#7A857B"); Box("#263F31", 688, 467, 98, 30, 6); Text("New note", 711, 474, 12, "#FFFFFF");
        }
        var bitmap = new RenderTargetBitmap(900, 560, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
}
