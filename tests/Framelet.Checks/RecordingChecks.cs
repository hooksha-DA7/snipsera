using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using Framelet;
using ScreenRecorderLib;

internal static class RecordingChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var results = new List<object>(); int failed = 0;
        void Check(string name, Action action)
        {
            try { action(); results.Add(new { name, passed = true }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failed++; results.Add(new { name, passed = false, error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        void True(bool test) { if (!test) throw new Exception("Assertion failed"); }
        void Refuses(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new Exception("Invalid settings were accepted"); }
        Check("Recording defaults are video with no ambient audio capture", () => { var o = new RecordingOptions(); o.Validate(); True(o.Format == RecordingFormat.Mp4 && !o.Microphone && !o.ComputerAudio && !o.DoNotDisturb); });
        Check("GIF cannot silently claim to include audio", () => Refuses(() => new RecordingOptions { Format = RecordingFormat.Gif, Microphone = true }.Validate()));
        Check("Unsupported and excessive GIF frame rates are refused", () => { Refuses(() => new RecordingOptions { FramesPerSecond = 120 }.Validate()); Refuses(() => new RecordingOptions { Format = RecordingFormat.Gif, FramesPerSecond = 60 }.Validate()); });
        Check("H264 output uses even dimensions and respects portrait aspect", () => { var o = new RecordingOptions { Resolution = RecordingResolution.FullHd }; True(o.OutputSize(new(0, 0, 1001, 2001)) == new Size(540, 1080)); });
        Check("Half resolution and tiny source rejection are explicit", () => { True(new RecordingOptions { Resolution = RecordingResolution.Half }.OutputSize(new(0, 0, 1280, 720)) == new Size(640, 360)); Refuses(() => new RecordingOptions().OutputSize(new(0, 0, 1, 10))); });
        Check("Oversized original output requires a smaller resolution", () => Refuses(() => new RecordingOptions().OutputSize(new(0, 0, 7680, 4320))));
        Check("Click rings map physical window coordinates into resized output", () =>
        {
            var rect = RecordingClickHighlight.Place(new(-1200, 100, 1000, 500), new(500, 250), new(-700, 350));
            True(rect == new Rectangle(234, 109, 32, 32));
            True(RecordingClickHighlight.Place(new(100, 100, 500, 300), new(500, 300), new(20, 20)) is null);
        });
        Check("Invalid custom dimensions and enum values are rejected", () => { Refuses(() => new RecordingOptions { CustomWidth = -1 }.Validate()); Refuses(() => new RecordingOptions { Format = (RecordingFormat)100 }.Validate()); });
        Check("Recording settings persist separately from screenshot shortcuts", () =>
        {
            var root = Path.Combine(output, "settings"); var store = new RecordingStore(root); store.SaveOptions(new() { FramesPerSecond = 15, ShowCursor = false });
            var loaded = new RecordingStore(root).LoadOptions(); True(loaded.FramesPerSecond == 15 && !loaded.ShowCursor && !File.Exists(Path.Combine(root, "settings.json")));
        });
        Check("Recording history rejects paths outside its output folder", () =>
        {
            var store = new RecordingStore(Path.Combine(output, "history"));
            Refuses(() => store.Add(new(Path.Combine(output, "unrelated.mp4"), RecordingFormat.Mp4, 640, 360, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow)));
        });
        Check("Pinned native recorder creates H264 options and loads in this runtime", () =>
        {
            var bounds = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            var target = new RecordingTarget(RecordingTargetKind.Area, new(bounds.Left + 20, bounds.Top + 20, 640, 360), IntPtr.Zero, "Synthetic region");
            var options = ScreenRecordingEngine.Configure(target, new() { FramesPerSecond = 15, ShowCursor = false });
            True(options.VideoEncoderOptions.Framerate == 15 && options.VideoEncoderOptions.Encoder is H264VideoEncoder && options.AudioOptions.IsAudioEnabled == false && options.MouseOptions.IsMousePointerEnabled == false);
            using var recorder = Recorder.CreateRecorder(options);
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = results.Count - failed, failed, results }, new JsonSerializerOptions { WriteIndented = true }));
        return failed == 0 ? 0 : 1;
    }
}
