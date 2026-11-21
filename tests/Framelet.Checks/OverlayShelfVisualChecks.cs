using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

// Render the production button templates off-screen. No Window, native input or desktop pixels.
internal static class OverlayShelfVisualChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); int failures = 0; var results = new List<object>();
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(app);
        foreach (string profile in new[] { "light", "dark", "contrast-template" })
        {
#pragma warning disable WPF0001
            app.ThemeMode = profile == "dark" ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
            Appearance.Current.Refresh();
            foreach (bool expanded in new[] { false, true })
            foreach (int hidden in new[] { 1, 3, 12 })
            {
                string name = profile + (expanded ? "-expanded" : "-resting") + "-" + hidden;
                try
                {
                    double width = OverlayShelfLayout.Width, height = expanded ? OverlayShelfLayout.ExpandedHeight : OverlayShelfLayout.RestingHeight;
                    var count = new TextBlock { Text = hidden.ToString(), Visibility = hidden > 1 ? Visibility.Visible : Visibility.Collapsed };
                    var content = OverlayShelfWindow.CreateContent(count);
                    var chevron = (UiIcon)((StackPanel)content.Children[0]).Children[0];
                    var button = new Button { Width = width, Height = height, Padding = new Thickness(0), Style = OverlayShelfWindow.TabStyle(profile == "contrast-template"), Content = content,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
                    button.ApplyTemplate(); button.Measure(new Size(width, height)); button.Arrange(new Rect(0, 0, width, height)); button.UpdateLayout();
                    if (button.ActualWidth != width || button.ActualHeight != height || VisualTreeHelper.GetChildrenCount(button) == 0) throw new Exception("Production shelf template has invalid bounds");
                    Rect icon = chevron.TransformToAncestor(button).TransformBounds(new Rect(chevron.RenderSize));
                    if (!new Rect(0, 0, width, height).Contains(icon)) throw new Exception("Restore chevron is clipped");
                    if (hidden > 1)
                    {
                        Rect label = count.TransformToAncestor(button).TransformBounds(new Rect(count.RenderSize));
                        if (!new Rect(0, 0, width, height).Contains(label) || label.Left < icon.Right + 3.5) throw new Exception("Hidden capture count is clipped or overlaps the chevron");
                    }
                    var image = new RenderTargetBitmap((int)width * 2, (int)height * 2, 192, 192, PixelFormats.Pbgra32); image.Render(button); image.Freeze();
                    CaptureService.Save(image, Path.Combine(output, name + ".png"));
                    results.Add(new { name, status = "passed", logicalWidth = width, logicalHeight = height }); Console.WriteLine("PASS Shelf template " + name);
                }
                catch (Exception ex) { failures++; results.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL Shelf template " + name + ": " + ex.Message); }
            }
        }
        app.Shutdown(); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, results, scope = "Off-screen production templates with synthetic sample content; native window/hover behavior is not tested" }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
