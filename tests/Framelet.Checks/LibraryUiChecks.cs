using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

// Detached production controls and synthetic history only; no desktop capture, HWND or input.
internal static class LibraryUiChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        var app = new CaptureApp(Path.Combine(output, "profile"), _ => throw new InvalidOperationException("No desktop capture in library checks."));
        var entries = new[]
        {
            app.History.Add(UiFixture.Scene(), CaptureKind.Area),
            app.History.Add(UiChecks.White(), CaptureKind.Window)
        };
        var results = new List<object>(); int failures = 0;
        foreach (bool dark in new[] { false, true })
        foreach (double width in new[] { 760d, 880d, 1100d })
        {
            string name = $"library-{(dark ? "dark" : "light")}-{width}";
#pragma warning disable WPF0001
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Appearance.Current.Refresh();
            var home = new HomeWindow(app);
            try
            {
                var root = (FrameworkElement)home.Content; home.Content = null;
                for (int pass = 0; pass < 3; pass++)
                { root.Measure(new Size(width, 700)); root.Arrange(new Rect(0, 0, width, 700)); root.UpdateLayout(); }
                var children = WorkflowChecks.Children(root).ToArray();
                foreach (var entry in entries)
                {
                    var edit = children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "Edit" + entry.Id);
                    Require(edit.IsEnabled && AutomationProperties.GetName(edit) == "Edit capture", "Direct edit must be enabled and labelled.");
                    Require(AutomationProperties.GetHelpText(edit).Contains("saved edits"), "Direct edit must explain that it preserves annotations.");
                    Require(children.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "Open" + entry.Id), "Preview action was removed.");
                    var bounds = edit.TransformToAncestor(root).TransformBounds(new Rect(edit.RenderSize));
                    Require(bounds.Width >= 48 && bounds.Height >= 28 && bounds.Left >= 0 && bounds.Right <= width, "Edit target clipped or too small.");
                    var card = children.OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "History" + entry.Id);
                    Require(ReferenceEquals(card.Background, Ui.CardMaterial), "History does not use the shared window card material.");
                }
                var capture = children.OfType<Border>().Single(b => AutomationProperties.GetAutomationId(b) == "CapturePanel");
                Require(ReferenceEquals(capture.Background, Ui.CardMaterial), "Capture panel does not share the library backdrop.");
                var launcher = children.OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "ShowCaptureBar");
                var launcherTitle = WorkflowChecks.Children(launcher).OfType<TextBlock>().Single(b => b.Text == "All-in-one capture");
                Require(launcherTitle.ActualHeight <= 20 && launcher.ActualHeight <= 88, "Launcher shortcut squeezes the title into a tall, narrow column.");
                if (Appearance.Current.Policy.Frosting)
                    Require(Appearance.Current.Card.A < 255, "Translucent card material became an opaque block.");
                Require(new WindowInteropHelper(home).Handle == IntPtr.Zero, "Library checks created a native window.");
                var image = new RenderTargetBitmap((int)width, 700, 96, 96, PixelFormats.Pbgra32);
                var backdrop = new DrawingVisual(); using (var dc = backdrop.RenderOpen()) dc.DrawRectangle(Ui.Paper, null, new Rect(0, 0, width, 700));
                image.Render(backdrop); image.Render(root); CaptureService.Save(image, Path.Combine(output, name + ".png"));
                results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name);
            }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
            finally { home.Close(); }
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, results,
            scope = "Detached production library controls with synthetic history; native backdrop, focus and actual editor opening remain untested." }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
    private static void Require(bool value, string reason) { if (!value) throw new Exception(reason); }
}
