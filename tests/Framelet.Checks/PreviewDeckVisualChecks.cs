using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;
using PixelRect = System.Drawing.Rectangle;

// Production card visuals rendered detached with generated sample captures. No HWND, input, clipboard or desktop capture.
internal static class PreviewDeckVisualChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static void Text(DrawingContext drawing, string value, double size, Brush ink, double x, double y, bool bold = false)
    {
        var type = new Typeface(Ui.Font, FontStyles.Normal, bold ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal);
        drawing.DrawText(new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, type, size, ink, 1), new Point(x, y));
    }
    private static BitmapSource Sample(int kind)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (kind == 0)
            {
                dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(83, 144, 170), Color.FromRgb(228, 214, 181), 90), null, new Rect(0, 0, 800, 600));
                dc.DrawEllipse(Ui.Brush("#F5E8B7"), null, new Point(612, 130), 58, 58);
                dc.DrawGeometry(Ui.Brush("#586A7D"), null, Geometry.Parse("M 0,420 L 160,190 340,420 460,278 650,445 800,335 800,600 0,600 Z"));
                dc.DrawGeometry(Ui.Brush("#D4D8D8"), null, Geometry.Parse("M 113,262 L 160,190 227,277 182,257 153,268 140,250 Z"));
                dc.DrawGeometry(Ui.Brush("#28575D"), null, Geometry.Parse("M 0,440 C 210,375 350,510 800,417 L 800,600 0,600 Z"));
                dc.DrawRectangle(new LinearGradientBrush(Color.FromArgb(0, 0, 0, 0), Color.FromArgb(170, 5, 25, 34), 90), null, new Rect(0, 300, 800, 300));
                Text(dc, "North coast", 46, Brushes.White, 44, 435, true);
                Text(dc, "A quiet afternoon", 23, Ui.Brush("#D6E5E8"), 46, 495);
            }
            else if (kind == 1)
            {
                dc.DrawRectangle(Ui.Brush("#F3EDE4"), null, new Rect(0, 0, 800, 600));
                dc.DrawRectangle(Ui.Brush("#E0B79B"), null, new Rect(0, 0, 800, 64));
                Text(dc, "FIELD NOTES", 19, Ui.Brush("#645047"), 35, 23, true);
                Text(dc, "Ideas worth keeping", 43, Ui.Brush("#383D39"), 45, 110, true);
                Text(dc, "A little space for the next good idea.", 24, Ui.Brush("#7A7C72"), 48, 177);
                for (int row = 0; row < 3; row++)
                {
                    dc.DrawRoundedRectangle(Brushes.White, null, new Rect(45, 245 + row * 96, 710, 72), 12, 12);
                    dc.DrawEllipse(Ui.Brush(new[] { "#D19E80", "#81A694", "#9CABC3" }[row]), null, new Point(78, 280 + row * 96), 10, 10);
                    Text(dc, new[] { "Collect references", "Find the simplest form", "Make room for details" }[row], 25, Ui.Brush("#454B46"), 110, 262 + row * 96);
                }
            }
            else
            {
                dc.DrawRectangle(Ui.Brush("#173A46"), null, new Rect(0, 0, 800, 600));
                dc.DrawRectangle(Ui.Brush("#2D6872"), null, new Rect(0, 0, 800, 64));
                Text(dc, "STUDIO / OVERVIEW", 19, Ui.Brush("#D9F1E9"), 35, 23, true);
                Text(dc, "A week in focus", 43, Brushes.White, 45, 120, true);
                for (int column = 0; column < 7; column++)
                {
                    double height = new[] { 115, 165, 140, 240, 190, 270, 220 }[column];
                    dc.DrawRoundedRectangle(Ui.Brush(column == 5 ? "#E5B59C" : "#72BDB3"), null, new Rect(48 + column * 100, 510 - height, 54, height), 8, 8);
                }
            }
        }
        var bitmap = new RenderTargetBitmap(800, 600, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }
    private static byte[] Pixels(BitmapSource source)
    {
        int stride = (source.PixelWidth * source.Format.BitsPerPixel + 7) / 8;
        var pixels = new byte[stride * source.PixelHeight]; source.CopyPixels(pixels, stride, 0); return pixels;
    }
    private static BitmapSource Render(FrameworkElement root, double width, double height, double dpi)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi), (int)Math.Ceiling(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        bitmap.Render(root); bitmap.Freeze(); return bitmap;
    }
    private static ImageWindow Card(CaptureApp app, BitmapSource source, int depth, Action? promote = null)
    {
        var time = new DateTimeOffset(2026, 11, 19, 12, 14 - depth, 0, TimeSpan.Zero);
        var entry = new CaptureEntry(Guid.NewGuid().ToString("N"), time, "Area", source.PixelWidth, source.PixelHeight);
        var window = new ImageWindow(app, source, entry, false);
        window.ConfigureDeck(depth, promote, $"Screenshot {depth + 1}, behind. Bring to front."); return window;
    }
    private static BitmapSource Scene(CaptureApp app, BitmapSource[] images, int count, bool actions, bool dark, double dpi)
    {
        var drawing = new DrawingVisual(); var work = new PixelRect(0, 0, 300, 300);
        var layout = FloatingLayout.Stack(work, count, 0, false, 1);
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(dark ? Color.FromRgb(36, 55, 64) : Color.FromRgb(221, 232, 235), dark ? Color.FromRgb(69, 86, 90) : Color.FromRgb(239, 229, 218), 35), null, new Rect(0, 0, 300, 320));
            Text(dc, actions ? "On hover" : "At rest", 15, dark ? Ui.Brush("#E5ECEE") : Ui.Brush("#344B51"), 28, 22, true);
            double shift = (300 - FloatingLayout.MaxWidth) / 2 - layout.Previews[0].X;
            for (int depth = count - 1; depth >= 0; depth--)
            {
                var window = Card(app, images[depth], depth, () => { });
                try
                {
                    var root = (FrameworkElement)window.Content; window.Content = null; var bounds = layout.Previews[depth];
                    var bitmap = Render(root, bounds.Width, bounds.Height, dpi);
                    if (depth == 0 && actions)
                    {
                        var controls = Children(root).OfType<Grid>().Single(grid => AutomationProperties.GetName(grid) == "Capture actions");
                        controls.Visibility = Visibility.Visible; controls.Opacity = 1;
                        ((Border)typeof(ImageWindow).GetField("scrim", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!).Opacity = 1;
                        bitmap = Render(root, bounds.Width, bounds.Height, dpi);
                    }
                    dc.DrawImage(bitmap, new Rect(bounds.X + shift, bounds.Y - 8, bounds.Width, bounds.Height));
                    Require(new WindowInteropHelper(window).Handle == IntPtr.Zero && !window.IsVisible, "Rendering created native UI");
                }
                finally { window.Close(); }
            }
        }
        var scene = new RenderTargetBitmap((int)(300 * dpi), (int)(320 * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32); scene.Render(drawing); scene.Freeze(); return scene;
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new CaptureApp(Path.Combine(output, "isolated-profile"));
        var outcomes = new List<object>(); int failures = 0; var images = new[] { Sample(0), Sample(1), Sample(2) };
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        Check("Rear cards expose one accessible edge, block hidden actions and preserve full source pixels", () =>
        {
            var source = images[0]; var original = Pixels(source); int promoted = 0; var window = Card(app, source, 1, () => promoted++);
            try
            {
                var root = (FrameworkElement)window.Content; Render(root, 200, 166, 1);
                var controls = Children(root).OfType<Grid>().Single(grid => AutomationProperties.GetName(grid) == "Capture actions");
                var selector = Children(root).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "BringDeckCaptureForward");
                window.RevealActions(true);
                Require(window.IsDeckBack && window.DeckDepth == 1 && !window.ShowInTaskbar && !controls.IsEnabled && !controls.IsHitTestVisible && controls.Visibility != Visibility.Visible, "Rear capture actions are reachable");
                Require(selector.Visibility == Visibility.Visible && selector.IsEnabled && selector.Focusable && selector.ActualHeight == FloatingLayout.DeckPeek && AutomationProperties.GetName(selector).Contains("Bring to front"), "Rear edge lacks an accessible action");
                Require(promoted == 0, "Passive rendering promoted a capture");
                selector.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Require(promoted == 1, "Click did not promote exactly once");
                window.HandleDeckKey(Key.Enter, ModifierKeys.None); window.HandleDeckKey(Key.Space, ModifierKeys.None); Require(promoted == 3, "Keyboard promotion failed");
                foreach (var key in new[] { Key.Escape, Key.C, Key.S }) Require(window.HandleDeckKey(key, key == Key.Escape ? ModifierKeys.None : ModifierKeys.Control), "Rear export or close key was not intercepted");
                Require(promoted == 3 && ReferenceEquals(source, window.CaptureImage) && Pixels(source).SequenceEqual(original), "Deck presentation changed image pixels or invoked another action");
                window.RaiseInDeck(); Require(new WindowInteropHelper(window).Handle == IntPtr.Zero && !window.IsVisible, "Restacking created native UI");
            }
            finally { window.Close(); }
        });
        Check("Pinning a rear card restores its full on-image controls and taskbar role", () =>
        {
            var window = Card(app, images[1], 2, () => { });
            try
            {
                window.SetPinned(true);
                var root = (FrameworkElement)window.Content; Render(root, 216, 166, 1);
                var controls = Children(root).OfType<Grid>().Single(grid => AutomationProperties.GetName(grid) == "Capture actions");
                var selector = Children(root).OfType<Button>().Single(button => AutomationProperties.GetAutomationId(button) == "BringDeckCaptureForward");
                Require(window.IsPinned && !window.IsDeckBack && window.DeckDepth == 0 && window.ShowInTaskbar && controls.IsEnabled && controls.IsHitTestVisible && selector.Visibility == Visibility.Collapsed, "Pin retained the restricted rear-card role");
                window.SetPinned(false); Require(!window.IsDeckBack && !window.IsPinned, "Unpin introduced an unmanaged deck depth");
            }
            finally { window.Close(); }
        });
        Check("Front card layer shortcuts select older cards without taking over ordinary capture shortcuts", () =>
        {
            var window = Card(app, images[0], 0); var selected = new List<int>(); window.SelectDeckLayer = selected.Add;
            try
            {
                Require(window.HandleDeckKey(Key.D2, ModifierKeys.Alt) && window.HandleDeckKey(Key.D3, ModifierKeys.Alt) && selected.SequenceEqual(new[] { 1, 2 }), "Layer shortcuts select the wrong depth");
                Require(!window.HandleDeckKey(Key.C, ModifierKeys.Control) && !window.HandleDeckKey(Key.Escape, ModifierKeys.None) && !window.HandleDeckKey(Key.D2, ModifierKeys.Control), "Front capture keys were swallowed");
                window.SetPinned(true); Require(!window.HandleDeckKey(Key.D2, ModifierKeys.Alt), "Pinned reference still changes the pending deck");
            }
            finally { window.Close(); }
        });
        foreach (bool dark in new[] { false, true })
        {
#pragma warning disable WPF0001
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Appearance.Current.Refresh();
            foreach (double dpi in new[] { 1d, 1.5, 2 }) foreach (int count in new[] { 1, 3 }) foreach (bool actions in new[] { false, true })
            {
                string name = $"deck-{(dark ? "dark" : "light")}-{count}-{(actions ? "actions" : "rest")}-{dpi}";
                Check(name, () => CaptureService.Save(Scene(app, images, count, actions, dark, dpi), Path.Combine(output, name + ".png")));
            }
            Check("Shareable production deck preview " + (dark ? "dark" : "light"), () =>
            {
                var visual = new DrawingVisual();
                using (var dc = visual.RenderOpen())
                {
                    dc.DrawRectangle(dark ? Ui.Brush("#20313A") : Ui.Brush("#EDF1F2"), null, new Rect(0, 0, 660, 402));
                    Text(dc, "Layered captures", 20, dark ? Brushes.White : Ui.Brush("#273D45"), 20, 16, true);
                    dc.DrawImage(Scene(app, images, 3, false, dark, 2), new Rect(20, 52, 300, 320));
                    dc.DrawImage(Scene(app, images, 3, true, dark, 2), new Rect(340, 52, 300, 320));
                    Text(dc, "Click a rear edge to bring that capture forward.", 12, dark ? Ui.Brush("#C1D4DB") : Ui.Brush("#516871"), 20, 382);
                }
                var preview = new RenderTargetBitmap(1320, 804, 192, 192, PixelFormats.Pbgra32); preview.Render(visual);
                CaptureService.Save(preview, Path.Combine(output, "deck-preview-" + (dark ? "dark" : "light") + ".png"));
            });
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes, scope = "Detached production visuals and input routing only. Native stacking, pointer targets, focus and capture exclusion remain pending." }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
}
