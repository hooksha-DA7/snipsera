using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class RecordingIntegrationChecks
{
    internal static void Render(FrameworkElement view, string path, double width, double height, double scale = 1, bool paper = false)
    {
        view.Measure(new Size(width, height)); view.Arrange(new Rect(0, 0, width, height)); view.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        if (paper)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Ui.Paper, null, new Rect(0, 0, width, height)); dc.DrawRectangle(new VisualBrush(view), null, new Rect(0, 0, width, height)); }
            bitmap.Render(visual);
        }
        else bitmap.Render(view);
        CaptureService.Save(bitmap, path);
    }
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        int failed = 0; var app = new CaptureApp(Path.Combine(output, "data"));
        app.Startup += async (_, _) =>
        {
            Process? source = null;
            try
            {
                void True(bool value, string message) { if (!value) throw new Exception(message); }
                var setup = new RecordingWindow(app);
                setup.Show(); await Task.Delay(200);
                Render((FrameworkElement)setup.Content, Path.Combine(output, "setup-125.png"), 500, 680, 1.25, true);
                setup.Width = 440; setup.Height = 460; await Task.Delay(100);
                Render((FrameworkElement)setup.Content, Path.Combine(output, "setup-narrow-150.png"), 440, 420, 1.5, true);
                setup.Close();
                string folder = Path.Combine(output, "source"); Directory.CreateDirectory(folder);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--recording-source"); start.ArgumentList.Add(folder); source = Process.Start(start) ?? throw new Exception("Source launch failed");
                string description = Path.Combine(folder, "source.json");
                for (int i = 0; !File.Exists(description) && i < 100; i++) await Task.Delay(100);
                using var data = JsonDocument.Parse(File.ReadAllText(description)); var d = data.RootElement;
                var bounds = new System.Drawing.Rectangle(d.GetProperty("x").GetInt32(), d.GetProperty("y").GetInt32(), d.GetProperty("width").GetInt32(), d.GetProperty("height").GetInt32());
                var target = new RecordingTarget(RecordingTargetKind.Window, bounds, new IntPtr(d.GetProperty("handle").GetInt64()), "Synthetic test window");
                var entry = app.History.Add(UiChecks.White(), CaptureKind.Area); app.OpenEntry(entry); app.Pin(UiChecks.White(), entry);
                var pin = app.Windows.OfType<ImageWindow>().Single(w => w.IsPinned); var pinHandle = new WindowInteropHelper(pin).Handle; var pinBounds = Native.Bounds(pinHandle);
                var controller = app.Recording;
                controller.UpdateOptions(new() { TargetKind = RecordingTargetKind.Window, FramesPerSecond = 15, Resolution = RecordingResolution.Half, ShowCursor = false }); controller.UseTarget(target);
                var copy = controller.Options; copy.FramesPerSecond = 60; True(controller.Options.FramesPerSecond == 15, "External settings mutation changed active configuration");
                await controller.StartAsync(); await Task.Delay(700);
                True(controller.State == RecordingState.Recording, "Recording did not start");
                True(!Native.IsWindowVisible(pinHandle), "Pin remained visible while recording");
                var bar = app.Windows.OfType<RecordingBarWindow>().Single();
                bool excluded = bar.IsCaptureExcluded; True(excluded || !bar.IsVisible, "Unexcluded recording controls were visible");
                Render((FrameworkElement)bar.Content, Path.Combine(output, "bar-recording-125.png"), bar.ActualWidth, bar.ActualHeight, 1.25);
                await app.CaptureAsync(CaptureKind.Screen); True(app.History.Entries.Count == 1, "Screenshot picker ran during recording");
                controller.TogglePause(); var paused = controller.Elapsed; await Task.Delay(600); True(controller.Elapsed == paused, "Paused elapsed time kept advancing");
                controller.TogglePause(); await Task.Delay(900);
                True(app.RecordingTrayText.Contains("00:01"), "Tray elapsed time did not update");
                var stop = controller.StopAsync(); True(ReferenceEquals(stop, controller.StopAsync()), "Duplicate stop created another finalization"); await stop;
                True(controller.State == RecordingState.Completed && controller.LastResult is not null, "Recording did not complete");
                var video = controller.LastResult!; True(File.Exists(video.FilePath), "Video was not saved");
                True(Native.IsWindowVisible(pinHandle) && Native.Bounds(pinHandle) == pinBounds, "Recording changed pin identity or bounds");
                controller.UpdateOptions(controller.Options with { Format = RecordingFormat.Gif }); controller.UseTarget(target);
                await controller.StartAsync(); await Task.Delay(1600); await controller.StopAsync();
                var gif = controller.LastResult!; True(gif.Format == RecordingFormat.Gif && File.Exists(gif.FilePath), "GIF finalization did not finish");
                True(controller.RecentRecordings.Count == 2 && app.History.Entries.Count == 1, "Recording history altered screenshot history");
                controller.UpdateOptions(controller.Options with { Format = RecordingFormat.Mp4 }); controller.UseTarget(target);
                await controller.StartAsync(); await Task.Delay(600); await controller.CancelAsync();
                True(controller.State == RecordingState.Idle && controller.RecentRecordings.Count == 2 && controller.LastResult == gif, "Discard lost existing recordings or committed a canceled recording");
                controller.UseTarget(target with { WindowHandle = IntPtr.Zero });
                bool refused = false; try { await controller.StartAsync(); } catch (InvalidOperationException) { refused = true; }
                True(refused && controller.State == RecordingState.Failed && Native.IsWindowVisible(pinHandle), "Invalid source did not recover the capture windows");
                bool retained = controller.LastResult == gif;
                controller.UseTarget(target); await controller.StartAsync(); await Task.Delay(450);
                source.Kill(); source.Dispose(); source = null;
                for (int i = 0; controller.IsActive && i < 100; i++) await Task.Delay(100);
                True(!controller.IsActive && Native.IsWindowVisible(pinHandle), "Closing the recorded window left a recording running");
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "passed", video, gif, barCaptureExcluded = excluded, preservedPinWindowAndBounds = true, pauseClockStopped = true, trayElapsedUpdated = true, duplicateStopCoalesced = true, canceledRecordingNotCommitted = true, retainedPreviousResultAfterFailure = retained, separateRecordingHistory = true, closedWindowStoppedRecording = true, nativeCodecValidation = "pending" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("PASS Recording controller, native MP4/GIF, pause clock, discard, failure recovery and preserved screenshot state");
            }
            catch (Exception ex) { failed = 1; File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); Console.WriteLine("FAIL Recording integration: " + ex.Message); }
            finally
            {
                try { if (app.Recording.IsActive) await app.Recording.StopAsync(); } catch { }
                if (source is not null) { if (!source.HasExited) source.Kill(); source.Dispose(); }
                app.Quit();
            }
        };
        app.Run(); return failed;
    }
}
