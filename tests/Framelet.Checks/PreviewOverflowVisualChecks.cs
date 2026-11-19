using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;
using PixelRect = System.Drawing.Rectangle;

// Detached production visuals and synthetic recovery thumbnails. Never Show, Present(true), open Popup or acquire an HWND.
internal static class PreviewOverflowVisualChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static IEnumerable<DependencyObject> Children(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    private static BitmapSource Sample()
    {
        const int width = 64, height = 48; var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { int p = (y * width + x) * 4; pixels[p] = (byte)(100 + x); pixels[p + 1] = (byte)(120 + y); pixels[p + 2] = 35; pixels[p + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static PendingScreenshot[] Items(int count)
    {
        var image = Sample();
        return Enumerable.Range(1, count).Select(id => new PendingScreenshot(new CaptureEntry(id.ToString("x32"), DateTimeOffset.UnixEpoch.AddMinutes(id), "Area", 1920 + id, 1080), image)).ToArray();
    }
    private static void Render(FrameworkElement root, double width, double height, double dpi, string path)
    {
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(width * dpi), (int)Math.Ceiling(height * dpi), 96 * dpi, 96 * dpi, PixelFormats.Pbgra32);
        image.Render(root); CaptureService.Save(image, path);
    }

    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new CaptureApp(Path.Combine(output, "isolated-profile"));
        int failures = 0; var outcomes = new List<object>();
        void Check(string name, Action check)
        {
            try { check(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        foreach (bool dark in new[] { false, true })
        {
#pragma warning disable WPF0001
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Appearance.Current.Refresh();
            foreach (double dpi in new[] { 1d, 1.5, 2 })
            {
                string profile = (dark ? "dark" : "light") + "-" + dpi;
                Check("Pending overflow pill " + profile + " stays bounded, labelled and detached", () =>
                {
                    var window = new PreviewOverflowWindow(app);
                    try
                    {
                        window.Configure(Items(12), new PixelRect(-800, 400, (int)(216 * dpi), (int)(48 * dpi)), dpi);
                        var root = (FrameworkElement)window.Content; window.Content = null;
                        var host = new Grid(); host.Children.Add(root);
                        Render(host, 216, 48, dpi, Path.Combine(output, "pill-" + profile + ".png"));
                        var pill = Children(root).OfType<Button>().Single();
                        Require(AutomationProperties.GetName(pill) == "12 more pending screenshots" && AutomationProperties.GetAutomationId(pill) == "PendingCaptureOverflow", "Overflow button lacks its count or accessible identity");
                        Require(AutomationProperties.GetLiveSetting(pill) == AutomationLiveSetting.Polite, "Count changes are not a polite live region");
                        Require(pill.Width <= FloatingLayout.CardWidth && pill.Height == 32 && pill.FontFamily == Ui.Font, "Pill size or system font changed");
                        Require(new Rect(0, 0, 216, 48).Contains(pill.TransformToAncestor(host).TransformBounds(new Rect(pill.RenderSize))), "Overflow pill is clipped");
                        Require(!window.IsVisible && !window.IsFlyoutOpen && new WindowInteropHelper(window).Handle == IntPtr.Zero, "Configuration or rendering created native UI");
                        window.Present(false); Require(!window.IsFlyoutOpen, "Hiding did not dismiss its flyout");
                    }
                    finally { window.Close(); }
                });
                foreach (double width in new[] { 200d, 280 })
                Check("Pending overflow list " + profile + " at " + width + " remains scrollable and accessible", () =>
                {
                    var window = new PreviewOverflowWindow(app);
                    try
                    {
                        window.Configure(Items(14), new PixelRect(12, 12, 216, 48), 1);
                        var body = window.BuildFlyout(width, 340);
                        Render(body, width, 340, dpi, Path.Combine(output, "list-" + profile + "-" + width + ".png"));
                        var buttons = Children(body).OfType<Button>().ToArray();
                        var rows = buttons.Where(button => AutomationProperties.GetAutomationId(button).StartsWith("ShowPendingCapture-", StringComparison.Ordinal)).ToArray();
                        Require(rows.Length == 14 && rows.All(button => AutomationProperties.GetName(button).StartsWith("Screenshot,", StringComparison.Ordinal) && AutomationProperties.GetName(button).Contains("by")), "Hidden captures lost their labels or actions");
                        Require(buttons.Any(button => AutomationProperties.GetAutomationId(button) == "ClearPendingPreviews") && buttons.Any(button => AutomationProperties.GetAutomationId(button) == "ReopenDismissedCapture"), "Preview management footer is missing");
                        Require(Children(body).OfType<TextBlock>().Any(label => label.Text == "Captures stay in History."), "History reassurance caption is missing");
                        var scroll = Children(body).OfType<ScrollViewer>().First(view => AutomationProperties.GetAutomationId(view) == "PendingCaptureList");
                        Require(scroll.ScrollableHeight > 0 && scroll.HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled, "A long list is not vertically scrollable");
                        foreach (var row in rows)
                        {
                            var box = row.TransformToAncestor(body).TransformBounds(new Rect(row.RenderSize));
                            Require(box.Width > 0 && box.Left >= -.5 && box.Right <= width + .5, "Capture row exceeds flyout width");
                        }
                        Require(body.ActualWidth <= width && body.ActualHeight <= 340 && !window.IsFlyoutOpen && new WindowInteropHelper(window).Handle == IntPtr.Zero, "Detached flyout exceeds bounds or created native UI");
                    }
                    finally { window.Close(); }
                });
            }
        }
        Check("Updating an overflow count or configuring an empty stack never opens native UI", () =>
        {
            var window = new PreviewOverflowWindow(app);
            try
            {
                var items = Items(2); var bounds = new PixelRect(10, 10, 216, 48);
                window.Configure(items, bounds, 1); window.Configure(items, bounds, 1); window.Configure([], bounds, 1); window.Present(false);
                Require(!window.IsVisible && !window.IsFlyoutOpen && new WindowInteropHelper(window).Handle == IntPtr.Zero, "Passive updates opened a window or popup");
            }
            finally { window.Close(); }
        });
        Check("Popup close cleanup releases its thumbnail tree and footer reference without opening native UI", () =>
        {
            var window = new PreviewOverflowWindow(app);
            try
            {
                var popup = (Popup)typeof(PreviewOverflowWindow).GetField("flyout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var footer = typeof(PreviewOverflowWindow).GetField("reopen", BindingFlags.Instance | BindingFlags.NonPublic)!;
                window.Configure(Items(4), new PixelRect(10, 10, 216, 48), 1);
                popup.Child = window.BuildFlyout(280, 340);
                Require(popup.Child is not null && footer.GetValue(window) is Button, "Synthetic popup content was not attached");
                window.ReleaseFlyoutContent(); // The same cleanup callback used by Popup.Closed, including outside-click closure.
                Require(popup.Child is null && footer.GetValue(window) is null, "Closing retained the thumbnail tree or a footer parent chain");
                window.ReleaseFlyoutContent();
                Require(!window.IsFlyoutOpen && new WindowInteropHelper(window).Handle == IntPtr.Zero && PresentationSource.FromVisual(popup) is null, "Cleanup created native UI");
            }
            finally { window.Close(); }
        });
        Check("Present false releases prepared popup content even when the overflow was never shown", () =>
        {
            var window = new PreviewOverflowWindow(app);
            try
            {
                var popup = (Popup)typeof(PreviewOverflowWindow).GetField("flyout", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                var footer = typeof(PreviewOverflowWindow).GetField("reopen", BindingFlags.Instance | BindingFlags.NonPublic)!;
                window.Configure(Items(2), new PixelRect(10, 10, 216, 48), 1);
                popup.Child = window.BuildFlyout(280, 340);
                window.Present(false);
                Require(popup.Child is null && footer.GetValue(window) is null && !window.IsVisible && !window.IsFlyoutOpen, "Passive hiding retained popup visuals");
                Require(new WindowInteropHelper(window).Handle == IntPtr.Zero && PresentationSource.FromVisual(popup) is null, "Passive cleanup created an HWND");
            }
            finally { window.Close(); }
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes, scope = "Detached WPF visuals only; native popup, capture exclusion, focus and Narrator acceptance remain pending" }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
}
