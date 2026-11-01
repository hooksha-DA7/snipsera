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
using System.Windows.Threading;
using Framelet;

internal static class ScrollingUiChecks
{
    public static void Run(string output, Action<string, Action> test, Action<int> pump)
    {
        test("Rendered scrolling buttons remain inside their bar in ready, capture and finishing states", () =>
        {
            var work = Native.MonitorAtCursor(true);
            var area = new System.Drawing.Rectangle(work.Left + work.Width / 3, work.Top + work.Height / 4, work.Width / 2, work.Height / 2);
            var image = ScrollingChecks.Page(area.Width, area.Height);
            using var session = new ScrollCaptureSession(area, _ => image, false);
            var window = new ScrollCaptureWindow(session);
            try
            {
                window.Show(); pump(120); Check("ready");
                session.Start(); pump(120); Check("capturing");
                session.Finish(); window.UpdateLayout(); Check("finishing");
                for (int i = 0; i < 30 && !session.Completion.IsCompleted; i++) pump(50);
                if (!session.Completion.IsCompletedSuccessfully) throw new Exception("Scrolling view did not finish");
            }
            finally { window.Close(); }
            void Check(string phase)
            {
                window.UpdateLayout();
                var bar = FloatingOverlayChecks.Descendants(window).OfType<Border>().Single(b => AutomationProperties.GetName(b) == "Scrolling capture controls");
                var dpi = VisualTreeHelper.GetDpi(window); var full = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(window.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32); full.Render(window);
                var r = bar.TransformToAncestor(window).TransformBounds(new Rect(0, 0, bar.ActualWidth, bar.ActualHeight));
                int x = Math.Clamp((int)Math.Floor(r.Left * dpi.DpiScaleX) - 8, 0, full.PixelWidth - 1), y = Math.Clamp((int)Math.Floor(r.Top * dpi.DpiScaleY) - 8, 0, full.PixelHeight - 1);
                var crop = new CroppedBitmap(full, new Int32Rect(x, y, Math.Min(full.PixelWidth - x, (int)Math.Ceiling(r.Width * dpi.DpiScaleX) + 16), Math.Min(full.PixelHeight - y, (int)Math.Ceiling(r.Height * dpi.DpiScaleY) + 16)));
                CaptureService.Save(crop, Path.Combine(output, "scroll-" + phase + "-bar.png"));
                if (!window.HoleBounds.Contains(area) || window.ObstructionBounds.Any(b => b.IntersectsWith(area))) throw new Exception("Scrolling UI obstructs selected pixels");
                foreach (var button in FloatingOverlayChecks.Descendants(bar).OfType<Button>().Where(b => b.IsVisible))
                {
                    var bounds = button.TransformToAncestor(bar).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                    if (bounds.Left < -.5 || bounds.Top < -.5 || bounds.Right > bar.ActualWidth + .5 || bounds.Bottom > bar.ActualHeight + .5)
                        throw new Exception(phase + " clipped " + AutomationProperties.GetAutomationId(button) + ": " + bounds + " inside " + bar.ActualWidth + "x" + bar.ActualHeight);
                }
            }
        });
    }
    public static int RunOnly(string output)
    {
        Directory.CreateDirectory(output); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(app);
        int failures = 0; var results = new List<object>();
        void Test(string name, Action action) { try { action(); results.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; results.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
        void Pump(int ms) { var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) }; timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame); }
        Run(output, Test, Pump); app.Shutdown(); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true })); return failures == 0 ? 0 : 1;
    }
}
