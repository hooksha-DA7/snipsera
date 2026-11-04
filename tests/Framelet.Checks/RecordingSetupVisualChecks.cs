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
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class RecordingSetupVisualChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static bool Visible(DependencyObject node, FrameworkElement root)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
            if (ReferenceEquals(current, root)) return true;
        }
        return false;
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new CaptureApp(Path.Combine(output, "isolated-history"));
        var results = new List<object>(); int failures = 0;
        foreach (bool dark in new[] { true, false })
        {
#pragma warning disable WPF0001
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Appearance.Current.Refresh();
            foreach (bool settings in new[] { false, true })
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                RecordingWindow? window = null;
                string name = $"setup-{(dark ? "dark" : "light")}-{(settings ? "settings" : "compact")}-{scale}";
                try
                {
                    window = new RecordingWindow(app);
                    app.Recording.UseTarget(new(RecordingTargetKind.Area, new(80, 60, 608, 456), IntPtr.Zero, "Synthetic area"));
                    var root = (FrameworkElement)window.Content; window.Content = null;
                    var settingsToggle = Children(root).OfType<ToggleButton>().Single(b => AutomationProperties.GetAutomationId(b) == "RecordingSettings");
                    settingsToggle.IsChecked = settings;
                    double width = RecordingSetupVisuals.PanelWidth + RecordingSetupVisuals.Shadow * 2;
                    root.Measure(new Size(width, double.PositiveInfinity)); double height = root.DesiredSize.Height;
                    root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                    if (width > 380 || !settings && height > 280) throw new Exception($"Setup is not compact: {width} x {height}");
                    var controls = Children(root).OfType<Control>().Where(c => AutomationProperties.GetAutomationId(c).StartsWith("Recording", StringComparison.Ordinal) && Visible(c, root)).ToArray();
                    var bounds = controls.Select(c => c.TransformToAncestor(root).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight))).ToArray();
                    for (int i = 0; i < controls.Length; i++)
                    {
                        var box = bounds[i];
                        if (box.Width < 1 || box.Height < 1 || box.Left < 0 || box.Top < 0 || box.Right > width + .5 || box.Bottom > height + .5)
                            throw new Exception("A recording control is clipped: " + AutomationProperties.GetAutomationId(controls[i]));
                        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(controls[i]))) throw new Exception("A recording control has no accessible name");
                        for (int j = 0; j < i; j++)
                        {
                            var overlap = Rect.Intersect(box, bounds[j]);
                            if (!overlap.IsEmpty && overlap.Width > .5 && overlap.Height > .5) throw new Exception("Recording controls overlap: " + AutomationProperties.GetAutomationId(controls[i]) + " / " + AutomationProperties.GetAutomationId(controls[j]));
                        }
                    }
                    if (!controls.Any(c => AutomationProperties.GetAutomationId(c) == "RecordingRecordGif") || !controls.Any(c => AutomationProperties.GetAutomationId(c) == "RecordingRecordVideo")) throw new Exception("Both direct record actions are required");
                    var image = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    image.Render(root); CaptureService.Save(image, Path.Combine(output, name + ".png"));
                    results.Add(new { name, passed = true, width, height, controls = controls.Length }); Console.WriteLine("PASS " + name);
                }
                catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
                finally { window?.Close(); }
            }
        }
        foreach (bool converting in new[] { true, false })
        {
            RecordingBarWindow? bar = null;
            string name = converting ? "bar-conversion-stop-pending" : "bar-long-error";
            try
            {
                typeof(RecordingController).GetProperty(nameof(RecordingController.State))!.SetValue(app.Recording, converting ? RecordingState.Converting : RecordingState.Failed);
                typeof(RecordingController).GetProperty(nameof(RecordingController.ConversionProgress))!.SetValue(app.Recording, .5);
                typeof(RecordingController).GetProperty(nameof(RecordingController.Status))!.SetValue(app.Recording, "Recording failed: " + new string('W', 120));
                bar = new RecordingBarWindow(app.Recording);
                if (converting) typeof(RecordingBarWindow).GetField("busy", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(bar, true);
                typeof(RecordingBarWindow).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, null);
                var root = (FrameworkElement)bar.Content; bar.Content = null;
                root.Measure(new Size(520, double.PositiveInfinity)); double width = Math.Min(520, root.DesiredSize.Width), height = root.DesiredSize.Height;
                root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
                var cancel = Children(root).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "RecordingCancel");
                if (converting && !cancel.IsEnabled) throw new Exception("Pending Stop disabled GIF conversion cancellation");
                if (converting && Children(root).OfType<ProgressBar>().Single().Value != .5) throw new Exception("Bar did not display actual conversion progress");
                foreach (var button in Children(root).OfType<Button>())
                {
                    var box = button.TransformToAncestor(root).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    if (box.Left < 0 || box.Right > width + .5 || box.Width < 1) throw new Exception("Recording bar clipped a control: " + AutomationProperties.GetAutomationId(button));
                }
                var image = new RenderTargetBitmap((int)Math.Ceiling(width * 1.5), (int)Math.Ceiling(height * 1.5), 144, 144, PixelFormats.Pbgra32);
                image.Render(root); CaptureService.Save(image, Path.Combine(output, name + ".png"));
                results.Add(new { name, passed = true, width, height }); Console.WriteLine("PASS " + name);
            }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
            finally { typeof(RecordingController).GetProperty(nameof(RecordingController.State))!.SetValue(app.Recording, RecordingState.Idle); bar?.Close(); }
        }
        try
        {
            app.Recording.UpdateOptions(app.Recording.Options with { Format = RecordingFormat.Gif, Microphone = false, ComputerAudio = false });
            app.Recording.RememberVideoAudio(true, true);
            for (int i = 0; i < 2; i++)
            {
                var setup = new RecordingWindow(app);
                try
                {
                    var toggles = Children((FrameworkElement)setup.Content).OfType<ToggleButton>();
                    if (toggles.Single(b => AutomationProperties.GetAutomationId(b) == "RecordingMicrophone").IsChecked != true ||
                        toggles.Single(b => AutomationProperties.GetAutomationId(b) == "RecordingComputerAudio").IsChecked != true)
                        throw new Exception("Reopening GIF setup lost the video audio choices");
                }
                finally { setup.Close(); }
            }
            if (app.Recording.Options.Microphone || app.Recording.Options.ComputerAudio) throw new Exception("Remembering video preferences enabled GIF audio");
            results.Add(new { name = "video-audio-survives-setup-reopen", passed = true }); Console.WriteLine("PASS video-audio-survives-setup-reopen");
        }
        catch (Exception ex) { failures++; results.Add(new { name = "video-audio-survives-setup-reopen", passed = false, error = ex.ToString() }); Console.WriteLine("FAIL video-audio-survives-setup-reopen: " + ex.Message); }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, desktop = "not used; detached controls rendered offscreen", results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
