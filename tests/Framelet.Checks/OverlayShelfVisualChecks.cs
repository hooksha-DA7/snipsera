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
            {
                string name = profile + (expanded ? "-expanded" : "-resting");
                try
                {
                    double width = OverlayShelfLayout.Width, height = expanded ? OverlayShelfLayout.ExpandedHeight : OverlayShelfLayout.RestingHeight;
                    var content = new Grid();
                    var chevron = new UiIcon("ChevronUp", 16, 2.2) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 3, 0, 0) };
                    if (expanded && profile != "contrast-template") content.Children.Add(new Border { Background = new LinearGradientBrush(Colors.SlateBlue, Colors.SandyBrown, 25), Opacity = .62, Margin = new Thickness(0, 22, 0, 0) });
                    content.Children.Add(chevron);
                    if (expanded) content.Children.Add(new Border { Background = Ui.Floating, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(7, 1, 7, 1), HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 7), Child = Ui.Text("3", 11, Ui.Ink, true) });
                    var button = new Button { Width = width, Height = height, Padding = new Thickness(0), Style = OverlayShelfWindow.TabStyle(profile == "contrast-template"), Content = content,
                        HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
                    button.ApplyTemplate(); button.Measure(new Size(width, height)); button.Arrange(new Rect(0, 0, width, height)); button.UpdateLayout();
                    if (button.ActualWidth != width || button.ActualHeight != height || VisualTreeHelper.GetChildrenCount(button) == 0) throw new Exception("Production shelf template has invalid bounds");
                    Rect icon = chevron.TransformToAncestor(button).TransformBounds(new Rect(chevron.RenderSize));
                    if (!new Rect(0, 0, width, height).Contains(icon)) throw new Exception("Restore chevron is clipped");
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
