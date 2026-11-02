using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Framelet;

internal static class RecordingNativeChecks
{
    internal static int Source(string output, bool fullscreen = false)
    {
        Directory.CreateDirectory(output); Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        var app = new Application(); Ui.Install(app);
        var canvas = new Canvas { Background = new SolidColorBrush(Color.FromRgb(19, 38, 64)) };
        var title = new TextBlock { Text = "Snipsera recording test", Foreground = Brushes.White, FontSize = 30, Margin = new Thickness(30) };
        var clock = new TextBlock { Foreground = Brushes.White, FontSize = 25 }; Canvas.SetLeft(clock, 30); Canvas.SetTop(clock, 110);
        var shape = new Rectangle { Width = 100, Height = 100, Fill = Brushes.DeepSkyBlue }; Canvas.SetTop(shape, 220);
        canvas.Children.Add(title); canvas.Children.Add(clock); canvas.Children.Add(shape);
        var click = Ui.Button("Click to test highlighting", "RecordingTestClick", () => { });
        Canvas.SetLeft(click, 450); Canvas.SetTop(click, 315); canvas.Children.Add(click);
        click.PreviewMouseLeftButtonDown += (_, _) => File.WriteAllText(System.IO.Path.Combine(output, "physical-click.json"), JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, kind = "physical mouse down" }));
        var window = new Window { Title = "Snipsera synthetic recording source", Width = 800, Height = 450, Left = 90, Top = 90, WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, Topmost = true, Content = canvas };
        if (fullscreen) window.Loaded += (_, _) =>
        {
            var monitor = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            Native.SetWindowPos(new WindowInteropHelper(window).Handle, new IntPtr(-1), monitor.X, monitor.Y, monitor.Width, monitor.Height, 0x0040);
        };
        int frames = 0; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { frames++; clock.Text = "Synthetic animation · frame " + frames; Canvas.SetLeft(shape, 40 + frames * 7 % 560); };
        window.ContentRendered += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle; var bounds = Native.Bounds(handle);
            File.WriteAllText(System.IO.Path.Combine(output, "source.json"), JsonSerializer.Serialize(new { handle = handle.ToInt64(), x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height }));
            if (fullscreen) { clock.Text = "Synthetic static screen · cursor test"; Canvas.SetLeft(shape, 200); }
            else timer.Start();
        };
        window.Closed += (_, _) => timer.Stop(); return app.Run(window);
    }

    internal static int RunClicks(string output, bool rendererProbe = false)
    {
        output = System.IO.Path.GetFullPath(output); Directory.CreateDirectory(output); Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        int failed = 0; var app = new Application();
        app.Startup += async (_, _) =>
        {
            Process? source = null;
            try
            {
                string folder = System.IO.Path.Combine(output, "source"); Directory.CreateDirectory(folder);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--recording-source"); start.ArgumentList.Add(folder); source = Process.Start(start)!;
                string descriptor = System.IO.Path.Combine(folder, "source.json");
                for (int i = 0; !File.Exists(descriptor) && i < 100; i++) await Task.Delay(100);
                using var document = JsonDocument.Parse(File.ReadAllText(descriptor)); var d = document.RootElement;
                var bounds = new System.Drawing.Rectangle(d.GetProperty("x").GetInt32(), d.GetProperty("y").GetInt32(), d.GetProperty("width").GetInt32(), d.GetProperty("height").GetInt32());
                using var engine = new ScreenRecordingEngine();
                var options = new RecordingOptions { TargetKind = RecordingTargetKind.Window, ShowCursor = true, HighlightClicks = true, FramesPerSecond = 30, Resolution = rendererProbe ? RecordingResolution.Half : RecordingResolution.Original };
                await engine.StartAsync(new(RecordingTargetKind.Window, bounds, new IntPtr(d.GetProperty("handle").GetInt64()), "Synthetic cursor test"), options, System.IO.Path.Combine(output, "cursor-clicks.mp4"));
                File.WriteAllText(System.IO.Path.Combine(output, "ready.json"), "{\"recording\":true}");
                bool observed = false;
                if (rendererProbe)
                {
                    await Task.Delay(500);
                    engine.PreviewClick(new System.Drawing.Point(bounds.X + 600, bounds.Y + 400));
                    await Task.Delay(600);
                }
                else for (int i = 0; i < 400; i++) { if (File.Exists(System.IO.Path.Combine(folder, "physical-click.json"))) { observed = true; await Task.Delay(500); break; } await Task.Delay(100); }
                await engine.StopAsync();
                File.WriteAllText(System.IO.Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "encoded", physicalMouseDownObserved = observed, rendererProbe, sourceWidth = bounds.Width, sourceHeight = bounds.Height, size = options.OutputSize(bounds), output = "cursor-clicks.mp4", audio = false, cursorAndClickPixels = "pending" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(rendererProbe ? "PASS Synthetic ring-rendering signal encoded; source-relative pixel comparison pending" : observed ? "PASS Physical click delivered to synthetic recording source; video inspection pending" : "LIMIT No physical mouse-down was delivered during the recording");
            }
            catch (Exception ex) { failed = 1; File.WriteAllText(System.IO.Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); }
            finally { if (source is not null) { if (!source.HasExited) source.Kill(); source.Dispose(); } app.Shutdown(); }
        };
        app.Run(); return failed;
    }

    internal static int Run(string output, bool audioTest = false)
    {
        output = System.IO.Path.GetFullPath(output); Directory.CreateDirectory(output);
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); int failed = 0;
        var app = new Application();
        app.Startup += async (_, _) =>
        {
            Process? source = null;
            try
            {
                string folder = System.IO.Path.Combine(output, "source"); Directory.CreateDirectory(folder);
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("--recording-source"); start.ArgumentList.Add(folder);
                source = Process.Start(start) ?? throw new Exception("Synthetic source did not start.");
                string descriptor = System.IO.Path.Combine(folder, "source.json");
                for (int i = 0; !File.Exists(descriptor) && i < 100; i++) await Task.Delay(100);
                using var info = JsonDocument.Parse(File.ReadAllText(descriptor)); var root = info.RootElement;
                var bounds = new System.Drawing.Rectangle(root.GetProperty("x").GetInt32(), root.GetProperty("y").GetInt32(), root.GetProperty("width").GetInt32(), root.GetProperty("height").GetInt32());
                var target = new RecordingTarget(RecordingTargetKind.Window, bounds, new IntPtr(root.GetProperty("handle").GetInt64()), "Synthetic animated window");
                var options = new RecordingOptions { TargetKind = RecordingTargetKind.Window, FramesPerSecond = 15, Resolution = RecordingResolution.Half, ShowCursor = false, Microphone = audioTest, ComputerAudio = audioTest };
                string video = System.IO.Path.Combine(output, "animated-window.mp4");
                using (var engine = new ScreenRecordingEngine())
                {
                    await engine.StartAsync(target, options, video);
                    if (audioTest) await Task.Delay(5000);
                    else { await Task.Delay(1400); engine.Pause(); await Task.Delay(600); engine.Resume(); await Task.Delay(1800); }
                    await engine.StopAsync();
                }
                if (!File.Exists(video) || new FileInfo(video).Length < 1000) throw new Exception("No useful MP4 data was saved.");
                string? gif = null;
                if (!audioTest)
                {
                    gif = System.IO.Path.Combine(output, "animated-window.gif");
                    await GifConverter.ConvertAsync(video, gif, options with { Format = RecordingFormat.Gif }, default);
                }
                var size = options.OutputSize(bounds);
                File.WriteAllText(System.IO.Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "encoded", video, gif, width = size.Width, height = size.Height, fps = 15, pausedMilliseconds = audioTest ? 0 : 600, audio = audioTest ? "microphone and computer audio; explicit five-second tester authorization" : "disabled; only synthetic window pixels", independentCodecValidation = "pending" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine(audioTest ? "PASS Five-second native microphone + computer-audio recording; independent probe pending" : "PASS Native window recording, pause/resume, MP4 finalization and GIF conversion; independent probe pending");
            }
            catch (Exception ex)
            {
                failed = 1; File.WriteAllText(System.IO.Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); Console.WriteLine("FAIL Native recording: " + ex.Message);
            }
            finally
            {
                if (source is not null) { if (!source.HasExited) source.Kill(); source.Dispose(); }
                app.Shutdown();
            }
        };
        app.Run(); return failed;
    }
}
