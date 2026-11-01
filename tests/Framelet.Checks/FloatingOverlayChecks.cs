using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class FloatingOverlayChecks
{
    public static async Task<object[]> Run(CaptureApp app)
    {
        var results = new List<object>();
        foreach (var size in new[] { (300, 240), (1551, 574), (1187, 547), (3000, 200), (400, 2400), (100, 100) })
        {
            var bytes = Enumerable.Repeat((byte)255, size.Item1 * size.Item2 * 4).ToArray();
            var image = BitmapSource.Create(size.Item1, size.Item2, 96, 96, PixelFormats.Bgra32, null, bytes, size.Item1 * 4); image.Freeze();
            var entry = app.History.Add(image, CaptureKind.Area); app.OpenEntry(entry);
            var window = app.Windows.Cast<Window>().OfType<ImageWindow>().Single(w => !w.IsPinned && w.IsVisible);
            try
            {
                var handle = new WindowInteropHelper(window).Handle; var before = Native.Bounds(handle);
                window.RevealActions(true); await Task.Delay(180); window.UpdateLayout();
                if (Native.Bounds(handle) != before) throw new Exception("Showing overlay resized the image frame");
                var content = window;
                var controls = Descendants(content).OfType<Button>().Where(b => b.IsVisible && AutomationProperties.GetAutomationId(b).EndsWith("Capture", StringComparison.Ordinal)).ToArray();
                if (controls.Length == 0) throw new Exception("No on-image actions available");
                if (!controls.Any(b => AutomationProperties.GetAutomationId(b) == "DragCapture")) throw new Exception("Capture has no reachable drag-export gesture");
                var bounds = controls.Select(b => b.TransformToAncestor(content).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight))).ToArray();
                for (int i = 0; i < bounds.Length; i++)
                {
                    var r = bounds[i];
                    if (r.Width < 1 || r.Height < 1 || r.Left < -.5 || r.Top < -.5 || r.Right > content.ActualWidth + .5 || r.Bottom > content.ActualHeight + .5)
                        throw new Exception("An on-image action lies outside its window: " + AutomationProperties.GetAutomationId(controls[i]));
                    for (int j = 0; j < i; j++)
                    {
                        var overlap = Rect.Intersect(r, bounds[j]);
                        if (!overlap.IsEmpty && overlap.Width > .5 && overlap.Height > .5) throw new Exception("On-image actions overlap");
                    }
                }
                var after = new byte[bytes.Length]; window.CaptureImage.CopyPixels(after, size.Item1 * 4, 0);
                if (!after.SequenceEqual(bytes)) throw new Exception("Hover presentation changed source pixels");
                window.Hide();
                if (controls.Any(b => b.IsVisible)) throw new Exception("Actions remained visible after hiding the capture");
                results.Add(new { width = size.Item1, height = size.Item2, visibleActions = controls.Select(AutomationProperties.GetAutomationId).ToArray(), dragExportAvailable = true, frameUnchanged = true, controlsWithinWindow = true, controlsDoNotOverlap = true, sourcePixelsUnchanged = true });
            }
            finally { window.Close(); app.History.Remove(entry); }
        }
        return results.ToArray();
    }

    public static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Descendants(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
}
