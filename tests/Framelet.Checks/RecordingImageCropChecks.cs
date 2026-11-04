using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;
using ScreenRecorderLib;

// Exercises the native recorder's crop/scale/encoder using only a generated image source, never the desktop.
internal static class RecordingImageCropChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        try { RunAsync(output).GetAwaiter().GetResult(); return 0; }
        catch (Exception ex)
        {
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = false, error = ex.ToString() }));
            Console.WriteLine("FAIL Synthetic native image crop: " + ex.Message); return 1;
        }
    }
    private static async Task RunAsync(string output)
    {
        const int width = 1280, height = 720;
        var monitor = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        var target = new RecordingTarget(RecordingTargetKind.Area, new(monitor.Left + 120, monitor.Top + 80, 641, 361), IntPtr.Zero, "Synthetic odd crop");
        var options = ScreenRecordingEngine.Configure(target, new() { FramesPerSecond = 10, ShowCursor = false, Resolution = RecordingResolution.Original });
        var display = options.SourceOptions.RecordingSources.OfType<DisplayRecordingSource>().Single();
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int i = (y * width + x) * 4;
            bool selected = x >= 120 && x < 760 && y >= 80 && y < 440;
            bool center = x >= 360 && x < 520 && y >= 200 && y < 320;
            pixels[i] = (byte)(center ? 220 : selected ? 50 : 30);
            pixels[i + 1] = (byte)(center ? 50 : selected ? 200 : 30);
            pixels[i + 2] = (byte)(selected ? 30 : 220); pixels[i + 3] = 255;
        }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze();
        string image = Path.GetFullPath(Path.Combine(output, "generated-source.png")); CaptureService.Save(bitmap, image);
        options.SourceOptions.RecordingSources = new List<RecordingSourceBase> { new ImageRecordingSource(image)
        {
            SourceRect = display.SourceRect, OutputSize = display.OutputSize, Position = display.Position, AnchorPoint = display.AnchorPoint, Stretch = display.Stretch
        } };
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string video = Path.GetFullPath(Path.Combine(output, "crop-640x360.mp4"));
        using (var recorder = Recorder.CreateRecorder(options))
        {
            recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(); };
            recorder.OnRecordingComplete += (_, _) => finished.TrySetResult();
            recorder.OnRecordingFailed += (_, e) => { var ex = new Exception(e.Error); started.TrySetException(ex); finished.TrySetException(ex); };
            recorder.Record(video); await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            await Task.Delay(600); recorder.Stop(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        var start = new ProcessStartInfo(GifConverter.EncoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string arg in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-i", video, "-frames:v", "1", "-pix_fmt", "rgb24", "-f", "rawvideo", "pipe:1" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!; using var frame = new MemoryStream();
        var read = process.StandardOutput.BaseStream.CopyToAsync(frame); var error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(); await read;
        if (process.ExitCode != 0) throw new Exception(await error);
        byte[] decoded = frame.ToArray();
        if (decoded.Length != 640 * 360 * 3) throw new Exception("Native output did not use the selected dimensions");
        foreach (var (x, y) in new[] { (0, 0), (639, 0), (0, 359), (639, 359) })
        {
            int i = (y * 640 + x) * 3;
            if (decoded[i] > 80 || decoded[i + 1] < 140 || decoded[i + 2] > 110) throw new Exception("Encoded corner includes pixels outside the selected green region");
        }
        int middle = (180 * 640 + 320) * 3;
        if (decoded[middle + 2] < 150 || decoded[middle] > 80) throw new Exception("Encoded center does not match the selected blue marker");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = true, nativeEncodedCrop = true, selected = "120,80 640x360", encodedWidth = 640, encodedHeight = 360, corners = "selected green region only", center = "blue marker preserved", source = "generated image; no desktop or audio capture", limitation = "WindowsGraphicsCapture and live pointer selection not exercised" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS Native synthetic image crop: all four corners and center match the selected region at 640x360");
    }
}
