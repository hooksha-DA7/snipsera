using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

// Renders actual detached card content with synthetic pixels; never shows a window or reads the desktop.
internal static class FeedbackVisualChecks
{
    private static BitmapSource Pattern(int width, int height, int style)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4; bool stripe = (x / 35 + y / 35) % 2 == 0;
            pixels[i] = (byte)(style == 0 ? 160 + x * 70 / width : style == 1 ? 100 + y * 120 / height : stripe ? 225 : 190);
            pixels[i + 1] = (byte)(style == 0 ? 90 + y * 90 / height : style == 1 ? 130 : stripe ? 190 : 150);
            pixels[i + 2] = (byte)(style == 0 ? 60 : style == 1 ? 45 + x * 70 / width : stripe ? 90 : 65);
            pixels[i + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    public static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var app = new CaptureApp(Path.Combine(output, "isolated-history")); var outcomes = new List<object>(); int failures = 0;
        var work = new System.Drawing.Rectangle(0, 0, 290, 600);
        var images = new[] { Pattern(1600, 900, 0), Pattern(320, 1800, 1), Pattern(800, 800, 2) };
        var bounds = FloatingLayout.Pins(work, images.Skip(1).Select(i => new Size(i.PixelWidth, i.PixelHeight)).ToArray(), 1).ToList();
        bounds.Insert(0, FloatingLayout.Preview(work, new Size(images[0].PixelWidth, images[0].PixelHeight), 1));
        var scene = new DrawingVisual();
        using (var dc = scene.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(39, 58, 76), Color.FromRgb(90, 122, 134), 25), null, new Rect(0, 0, work.Width, work.Height));
            for (int index = 0; index < images.Length; index++)
            {
                var image = images[index]; var entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Area", image.PixelWidth, image.PixelHeight);
                var window = new ImageWindow(app, image, entry, index != 0);
                try
                {
                    var root = (FrameworkElement)window.Content; window.Content = null;
                    var rect = bounds[index]; root.Measure(new Size(rect.Width, rect.Height)); root.Arrange(new Rect(0, 0, rect.Width, rect.Height)); root.UpdateLayout();
                    var controls = Children(root).OfType<Grid>().Single(g => AutomationProperties.GetName(g) == "Capture actions");
                    controls.Visibility = Visibility.Visible; controls.Opacity = index == 0 ? 1 : 0;
                    root.UpdateLayout();
                    var actions = Children(root).OfType<Button>().Where(b => b.Visibility == Visibility.Visible && AutomationProperties.GetAutomationId(b).EndsWith("Capture", StringComparison.Ordinal)).ToArray();
                    if (actions.Length != 6) throw new Exception("A normal compact card must expose all six on-image actions.");
                    var actionBounds = actions.Select(b => b.TransformToAncestor(root).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight))).ToArray();
                    for (int i = 0; i < actionBounds.Length; i++)
                    {
                        var action = actionBounds[i];
                        if (action.Width < 1 || action.Height < 1 || action.Left < 0 || action.Top < 0 || action.Right > rect.Width || action.Bottom > rect.Height) throw new Exception("An action lies outside the card.");
                        for (int j = 0; j < i; j++)
                        {
                            var overlap = Rect.Intersect(action, actionBounds[j]);
                            if (!overlap.IsEmpty && overlap.Width > .5 && overlap.Height > .5) throw new Exception("On-image actions overlap.");
                        }
                    }
                    var thumbnail = Children(root).OfType<Image>().Single();
                    if (thumbnail.Stretch != Stretch.UniformToFill || !ReferenceEquals(thumbnail.Source, image)) throw new Exception("Thumbnail layout changed the source image.");
                    var rendered = new RenderTargetBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Pbgra32); rendered.Render(root);
                    CaptureService.Save(rendered, Path.Combine(output, "card-" + index + ".png"));
                    dc.DrawImage(rendered, new Rect(rect.X, rect.Y, rect.Width, rect.Height));
                    outcomes.Add(new { sourceWidth = image.PixelWidth, sourceHeight = image.PixelHeight, cardWidth = rect.Width, cardHeight = rect.Height, actionsWithinCard = true, actionsDoNotOverlap = true, fullSourceRetained = true });
                    Console.WriteLine($"PASS Offscreen card {image.PixelWidth}x{image.PixelHeight}: {rect.Width}x{rect.Height}, six reachable actions");
                }
                catch (Exception ex) { failures++; outcomes.Add(new { status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + ex.Message); }
                finally { window.Close(); }
            }
        }
        var final = new RenderTargetBitmap(work.Width, work.Height, 96, 96, PixelFormats.Pbgra32); final.Render(scene);
        CaptureService.Save(final, Path.Combine(output, "compact-stack.png"));
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
}
