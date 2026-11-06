using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class WorkflowVisualChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new CaptureApp(Path.Combine(output, "profile"));
        var results = new List<object>(); int failures = 0;
        foreach (bool dark in new[] { true, false })
        {
#pragma warning disable WPF0001
            app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Appearance.Current.Refresh();
            foreach (var page in Enum.GetValues<SettingsPage>())
            foreach (double width in new[] { 400d, 544, 780 })
            {
                string name = $"settings-{(dark ? "dark" : "light")}-{page}-{width}";
                var window = new PreferencesWindow(app.Preferences, app); window.ShowPage(page);
                try
                {
                    var root = (FrameworkElement)window.Content; window.Content = null;
                    for (int pass = 0; pass < 3; pass++) { root.Measure(new Size(width, 620)); root.Arrange(new Rect(0, 0, width, 620)); root.UpdateLayout(); }
                    var controls = WorkflowChecks.Children(root).OfType<Control>().Where(c => c is Button or CheckBox &&
                        !string.IsNullOrWhiteSpace(AutomationProperties.GetAutomationId(c)) && Shown(c, root)).ToArray();
                    foreach (var control in controls)
                    {
                        var box = control.TransformToAncestor(root).TransformBounds(new Rect(0, 0, control.ActualWidth, control.ActualHeight));
                        if (box.Width < 1 || box.Left < -.5 || box.Right > width + .5)
                            throw new Exception($"Horizontal clipping: {AutomationProperties.GetAutomationId(control)} at {box} in {width}");
                        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(control))) throw new Exception("Unlabelled action");
                    }
                    foreach (double scale in new[] { 1d, 1.5, 2 })
                    {
                        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(620 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                        var scene = new DrawingVisual(); using (var dc = scene.RenderOpen()) dc.DrawRectangle(Ui.Paper, null, new Rect(0, 0, width, 620));
                        bitmap.Render(scene); bitmap.Render(root); CaptureService.Save(bitmap, Path.Combine(output, name + "-" + scale + ".png"));
                    }
                    Console.WriteLine("PASS " + name); results.Add(new { name, passed = true, controls = controls.Length });
                }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); results.Add(new { name, passed = false, error = ex.ToString() }); }
                finally { window.Close(); }
            }
            foreach (var format in Enum.GetValues<RecordingFormat>())
            foreach (var size in new[] { new Size(240, 166), new Size(110, 90), new Size(45, 45) })
            foreach (bool actions in new[] { false, true })
            {
                string name = $"recording-card-{(dark ? "dark" : "light")}-{format}-{size.Width}-{actions}";
                var result = new RecordingResult(Path.Combine(output, "sample." + (format == RecordingFormat.Gif ? "gif" : "mp4")), format, 320, 180, TimeSpan.FromSeconds(6), DateTimeOffset.UtcNow);
                var card = new ImageWindow(app, RecordingPreview.Placeholder(), null, true, recording: result);
                try
                {
                    var root = (FrameworkElement)card.Content; card.Content = null;
                    var controls = WorkflowChecks.Children(root).OfType<Grid>().Single(c => AutomationProperties.GetName(c) == "Capture actions");
                    controls.Visibility = actions ? Visibility.Visible : Visibility.Hidden; controls.Opacity = actions ? 1 : 0;
                    typeof(ImageWindow).GetField("revealed", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(card, actions);
                    root.Measure(size); root.Arrange(new Rect(size)); root.UpdateLayout();
                    typeof(ImageWindow).GetMethod("FitBadge", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(card, null);
                    root.UpdateLayout();
                    var badge = WorkflowChecks.Children(root).OfType<Border>().Single(c => AutomationProperties.GetAutomationId(c) == "RecordingBadge");
                    if (badge.IsHitTestVisible || !AutomationProperties.GetName(badge).Contains(format == RecordingFormat.Gif ? "GIF recording" : "MP4 recording")) throw new Exception("Recording badge intercepts input or lacks a label");
                    if (badge.Visibility == Visibility.Visible)
                    {
                        var bounds = badge.TransformToAncestor(root).TransformBounds(new Rect(badge.RenderSize));
                        if (!new Rect(size).Contains(bounds)) throw new Exception("Media badge is clipped");
                        foreach (var button in WorkflowChecks.Children(controls).OfType<Button>().Where(b => Shown(b, root)))
                        {
                            var box = button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
                            var overlap = Rect.Intersect(bounds, box);
                            if (!overlap.IsEmpty && overlap.Width > .5 && overlap.Height > .5) throw new Exception("Media badge overlaps an action");
                        }
                    }
                    var bitmap = new RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(root);
                    CaptureService.Save(bitmap, Path.Combine(output, name + ".png")); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true });
                }
                catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); results.Add(new { name, passed = false, error = ex.ToString() }); }
                finally { card.Close(); }
            }
        }
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
    private static bool Shown(DependencyObject element, DependencyObject root)
    {
        for (DependencyObject? current = element; current is not null; current = VisualTreeHelper.GetParent(current))
        { if (current is UIElement { Visibility: not Visibility.Visible }) return false; if (ReferenceEquals(current, root)) return true; }
        return false;
    }
}
