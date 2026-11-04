using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Framelet;
using ScreenRecorderLib;

internal static class RecordingRefinementChecks
{
    private static void True(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Refuses(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid area was accepted"); }

    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var results = new List<object>(); int failures = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        Check("Physical area endpoints retain their offset, including reversed drags on negative-origin screens", () =>
        {
            var desktop = new Rectangle(-1920, -200, 4480, 1640);
            var area = RecordingGeometry.FromDrag(-1100, 600, -1740, 240, desktop);
            True(area == new Rectangle(-1740, 240, 640, 360), "Physical endpoints were scaled or translated");
            var crop = RecordingGeometry.SourceCrop(area, new(-1920, 0, 1920, 1080));
            True(crop.Left == 180 && crop.Top == 240 && crop.Width == 640 && crop.Height == 360, "Monitor-local crop differs from physical selection");
        });
        Check("Area selection clips at desktop bounds and rejects a cross-display frame", () =>
        {
            var area = RecordingGeometry.FromDrag(-100, -50, 600, 400, new(0, 0, 1920, 1080));
            True(area == new Rectangle(0, 0, 600, 400), "Desktop clipping lost its origin");
            Refuses(() => RecordingGeometry.SourceCrop(new(-20, 100, 640, 360), new(0, 0, 1920, 1080)));
            Refuses(() => RecordingGeometry.SourceCrop(new(20, 20, 1, 10), new(0, 0, 1920, 1080)));
            True(RecordingGeometry.FromDrag(0, 0, 1919, 1079, new(0, 0, 1920, 1080)) == new Rectangle(0, 0, 1920, 1080), "Last display row or column was omitted");
            True(RecordingGeometry.FromDrag(100, 80, 740, 440, new(0, 0, 1920, 1080)) == new Rectangle(100, 80, 640, 360), "Odd selection was rescaled instead of trimmed");
            var odd = RecordingGeometry.SourceCrop(new(100, 80, 641, 361), new(0, 0, 1920, 1080));
            True(odd.Width == 640 && odd.Height == 360, "Odd crop can introduce a letterbox strip");
        });
        Check("Recorder locks a nonzero area crop and source canvas to the requested output", () =>
        {
            var monitor = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            var area = new Rectangle(monitor.Left + 80, monitor.Top + 100, 640, 360);
            var options = ScreenRecordingEngine.Configure(new(RecordingTargetKind.Area, area, IntPtr.Zero, "Test area"), new() { Resolution = RecordingResolution.Half, ShowCursor = false });
            var source = options.SourceOptions.RecordingSources.OfType<DisplayRecordingSource>().Single();
            True(source.SourceRect.Left == 80 && source.SourceRect.Top == 100 && source.SourceRect.Width == 640 && source.SourceRect.Height == 360, "Area fell back to the whole monitor");
            True(source.OutputSize.Width == 320 && source.OutputSize.Height == 180 && source.Position.Left == 0 && source.Position.Top == 0, "Crop canvas does not match the encoded frame");
            True(options.OutputOptions.OutputFrameSize.Width == 320 && options.OutputOptions.OutputFrameSize.Height == 180 && !source.IsCursorCaptureEnabled, "Recording output settings changed");
        });
        var app = new CaptureApp(Path.Combine(output, "isolated-history"));
        Check("Canceling reselection returns false without replacing the previous target", () =>
        {
            using var controller = new RecordingController(app, Path.Combine(output, "cancel"), selectTarget: (_, _, _) => Task.FromResult<RecordingTarget?>(null));
            var original = new RecordingTarget(RecordingTargetKind.Area, new(80, 100, 640, 360), IntPtr.Zero, "Original");
            controller.UseTarget(original);
            True(!controller.SelectTargetAsync(RecordingTargetKind.Area).GetAwaiter().GetResult(), "Cancel looked like accepted selection");
            True(controller.Target == original && controller.State == RecordingState.Ready, "Cancel discarded the existing target");
        });
        Check("Accepted and failed selections report their actual outcome and preserve recovery", () =>
        {
            var selected = new RecordingTarget(RecordingTargetKind.Area, new(100, 150, 500, 300), IntPtr.Zero, "Selected");
            bool fail = false;
            using var controller = new RecordingController(app, Path.Combine(output, "selection"), selectTarget: (_, _, _) => fail ? Task.FromException<RecordingTarget?>(new IOException("Selection unavailable")) : Task.FromResult<RecordingTarget?>(selected));
            True(controller.SelectTargetAsync(RecordingTargetKind.Area).GetAwaiter().GetResult() && controller.Target == selected, "Accepted area was not retained");
            fail = true;
            try { controller.SelectTargetAsync(RecordingTargetKind.Area).GetAwaiter().GetResult(); throw new Exception("Selection error was hidden"); } catch (IOException) { }
            True(controller.Target == selected && controller.State == RecordingState.Ready, "Selection failure lost the last valid area");
        });
        Check("Video audio preferences survive silent GIF mode for the app session", () =>
        {
            using var controller = new RecordingController(app, Path.Combine(output, "audio-preferences"));
            controller.UpdateOptions(new() { Microphone = true, ComputerAudio = true });
            controller.UpdateOptions(controller.Options with { Format = RecordingFormat.Gif, Microphone = false, ComputerAudio = false });
            True(controller.VideoMicrophonePreference && controller.VideoComputerAudioPreference && !controller.Options.Microphone && !controller.Options.ComputerAudio, "GIF lost video preferences or enabled audio");
            controller.RememberVideoAudio(false, true);
            True(!controller.VideoMicrophonePreference && controller.VideoComputerAudioPreference, "Session preferences did not update independently");
        });
        Check("GIF name collision preserves both the existing GIF and recovery MP4", () =>
        {
            string history = Path.Combine(output, "collision"); var store = new RecordingStore(history);
            using var controller = new RecordingController(app, history);
            controller.UpdateOptions(new() { Format = RecordingFormat.Gif });
            controller.UseTarget(new(RecordingTargetKind.Area, new(80, 100, 640, 360), IntPtr.Zero, "Synthetic area"));
            string temporary = Path.Combine(store.Root, ".collision.mp4"), gif = Path.ChangeExtension(temporary, ".gif");
            File.WriteAllText(temporary, "recovery"); File.WriteAllText(gif, "existing GIF");
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            typeof(RecordingController).GetField("temporary", flags)!.SetValue(controller, temporary);
            typeof(RecordingController).GetField("destination", flags)!.SetValue(controller, Path.Combine(store.Root, "new.gif"));
            typeof(RecordingController).GetField("engine", flags)!.SetValue(controller, new CompletedEngine());
            typeof(RecordingController).GetProperty(nameof(RecordingController.State))!.SetValue(controller, RecordingState.Recording);
            bool refused = false;
            try { controller.StopAsync().GetAwaiter().GetResult(); } catch (IOException) { refused = true; }
            True(refused && File.ReadAllText(gif) == "existing GIF" && File.ReadAllText(temporary) == "recovery", "Collision cleanup deleted a file it did not create");
            True(controller.State == RecordingState.Failed && controller.Status.Contains(temporary, StringComparison.Ordinal), "Recovery path was not reported");
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, desktopCapture = "not performed", results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private sealed class CompletedEngine : IRecordingEngine
    {
        public Task Completion => Task.CompletedTask;
        public Task StartAsync(RecordingTarget target, RecordingOptions options, string path) => throw new Exception("No recording should start in this check");
        public Task StopAsync() => Task.CompletedTask;
        public void Pause() { }
        public void Resume() { }
        public void Dispose() { }
    }

    private sealed class ProgressValues : IProgress<double>
    {
        private readonly List<double> values = [];
        public void Report(double value) { lock (values) values.Add(value); }
        public double[] Snapshot() { lock (values) return values.ToArray(); }
    }

    internal static int GifRun(string output)
    {
        Directory.CreateDirectory(output); var results = new List<object>(); int failures = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        string input = Path.Combine(output, "synthetic-six-seconds.mp4");
        Check("Generate a six-second synthetic H264 fixture without desktop or audio capture", () =>
        {
            var start = new ProcessStartInfo(GifConverter.EncoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            foreach (var arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=30:duration=6", "-an", "-c:v", "libopenh264", "-b:v", "2M", input }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; string error = process.StandardError.ReadToEnd(); process.WaitForExit(); True(process.ExitCode == 0, error);
        });
        if (!File.Exists(input)) return 1;
        string original = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
        foreach (var quality in new[] { RecordingQuality.Compact, RecordingQuality.Balanced, RecordingQuality.High })
            Check($"Actual {quality} GIF conversion preserves resolution, all frames, duration and progress", () =>
            {
                string gif = Path.Combine(output, quality + ".gif"); var progress = new ProgressValues(); var watch = Stopwatch.StartNew();
                GifConverter.ConvertAsync(input, gif, new() { Format = RecordingFormat.Gif, Quality = quality, FramesPerSecond = 30 }, CancellationToken.None, progress, TimeSpan.FromSeconds(6)).GetAwaiter().GetResult();
                watch.Stop(); using var stream = File.OpenRead(gif); var image = new GifBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                True(image.Frames[0].PixelWidth == 640 && image.Frames[0].PixelHeight == 360 && image.Frames.Count == 180, "Dimensions or output frames were reduced");
                double delay = image.Frames.Sum(f => Convert.ToDouble(((BitmapMetadata)f.Metadata).GetQuery("/grctlext/Delay")) / 100);
                True(Math.Abs(delay - 6) < .02, "GIF timing differs from the source");
                var values = progress.Snapshot(); True(values.Length > 2 && values[0] == 0 && values[^1] == 1 && values.All(v => v >= 0 && v <= 1) && values.Zip(values.Skip(1)).All(pair => pair.First <= pair.Second), "Progress was missing or moved backwards");
                File.WriteAllText(Path.Combine(output, quality + "-timing.json"), JsonSerializer.Serialize(new { seconds = watch.Elapsed.TotalSeconds, frames = image.Frames.Count, duration = delay, width = 640, height = 360, bytes = stream.Length }));
            });
        Check("Canceled conversion retains the MP4 and removes only its temporary palette", () =>
        {
            using var token = new CancellationTokenSource(TimeSpan.FromMilliseconds(80)); bool canceled = false;
            try { GifConverter.ConvertAsync(input, Path.Combine(output, "canceled.gif"), new(), token.Token).GetAwaiter().GetResult(); } catch (OperationCanceledException) { canceled = true; }
            True(canceled && !Directory.EnumerateFiles(output, "*.palette-*.png").Any(), "Conversion cancellation left a palette behind");
            True(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) == original, "Cancellation changed the recovery MP4");
        });
        Check("GIF conversion never overwrites an existing output", () =>
        {
            string existing = Path.Combine(output, "existing.gif"); File.WriteAllText(existing, "retained"); bool refused = false;
            try { GifConverter.ConvertAsync(input, existing, new(), CancellationToken.None).GetAwaiter().GetResult(); } catch (IOException) { refused = true; }
            True(refused, "Existing output was reported as a new conversion");
            True(File.ReadAllText(existing) == "retained", "Existing output was replaced");
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = results.Count - failures, failed = failures, input = "generated testsrc2 H264, 6 seconds, no audio", results }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
