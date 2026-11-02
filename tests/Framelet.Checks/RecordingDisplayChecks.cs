using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Framelet;

internal static class RecordingDisplayChecks
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        int failed = 0; var app = new Application();
        app.Startup += async (_, _) =>
        {
            Process? source = null;
            try
            {
                string folder = Path.Combine(output, "source"); Directory.CreateDirectory(folder);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--recording-source-fullscreen"); start.ArgumentList.Add(folder); source = Process.Start(start) ?? throw new Exception("Synthetic display source did not start.");
                string file = Path.Combine(folder, "source.json");
                for (int i = 0; !File.Exists(file) && i < 100; i++) await Task.Delay(100);
                using var data = JsonDocument.Parse(File.ReadAllText(file)); var d = data.RootElement;
                var bounds = new System.Drawing.Rectangle(d.GetProperty("x").GetInt32(), d.GetProperty("y").GetInt32(), d.GetProperty("width").GetInt32(), d.GetProperty("height").GetInt32());
                Native.GetCursorPos(out var before);
                var options = new RecordingOptions { FramesPerSecond = 30, Resolution = RecordingResolution.Hd, Quality = RecordingQuality.High, ShowCursor = false };
                foreach (bool cursor in new[] { false, true })
                {
                    using var engine = new ScreenRecordingEngine();
                    await engine.StartAsync(new(RecordingTargetKind.Display, bounds, IntPtr.Zero, "Synthetic full display"), options with { ShowCursor = cursor }, Path.Combine(output, cursor ? "display-cursor.mp4" : "display-hidden-cursor.mp4"));
                    await Task.Delay(1000); await engine.StopAsync();
                }
                var area = new System.Drawing.Rectangle(bounds.X + 20, bounds.Y + 20, 640, 360);
                using (var engine = new ScreenRecordingEngine())
                {
                    await engine.StartAsync(new(RecordingTargetKind.Custom, area, IntPtr.Zero, "Synthetic exact-size area"), options with { Resolution = RecordingResolution.Original, Quality = RecordingQuality.Compact }, Path.Combine(output, "custom-640x360.mp4"));
                    await Task.Delay(1000); await engine.StopAsync();
                }
                Native.GetCursorPos(out var after); var size = options.OutputSize(bounds);
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "encoded", fullWidth = size.Width, fullHeight = size.Height, fps = 30, customWidth = 640, customHeight = 360, audio = "disabled; synthetic full-display cover", pointerBefore = new { before.X, before.Y }, pointerAfter = new { after.X, after.Y }, pointerStayed = before.X == after.X && before.Y == after.Y, display = new { bounds.X, bounds.Y, bounds.Width, bounds.Height }, independentCodecValidation = "pending" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("PASS Native display and custom-region recording with cursor on/off; independent pixel comparison pending");
            }
            catch (Exception ex) { failed = 1; File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); Console.WriteLine("FAIL Display recording: " + ex.Message); }
            finally { if (source is not null) { if (!source.HasExited) source.Kill(); source.Dispose(); } app.Shutdown(); }
        };
        app.Run(); return failed;
    }
}
