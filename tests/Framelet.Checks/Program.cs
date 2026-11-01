using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet;
using Rect = System.Drawing.Rectangle;

internal static class Program
{
    private static readonly List<object> outcomes = [];
    private static string output = "";
    private static int failures;
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}, got {actual}"); }
    private static void True(bool value, string label = "Assertion failed") { if (!value) throw new Exception(label); }
    private static void Throws<T>(Action action) where T : Exception { try { action(); } catch (T) { return; } throw new Exception("Expected " + typeof(T).Name); }
    private static void FileFailure(Action action) { try { action(); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; } throw new Exception("Expected file failure"); }
    private static void Test(string label, Action action)
    {
        try { action(); outcomes.Add(new { name = label, status = "passed" }); Console.WriteLine("PASS " + label); }
        catch (Exception ex) { failures++; outcomes.Add(new { name = label, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + label + ": " + ex.Message); }
    }
    private static string Folder() { string path = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static BitmapSource Pattern(int width = 64, int height = 48)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { int i = (y * width + x) * 4; pixels[i] = (byte)x; pixels[i + 1] = (byte)y; pixels[i + 2] = 120; pixels[i + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
    }
    private static void Pump(int milliseconds = 220)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--scroll-fixture") return ScrollingFixture.Run(args[1]);
        if (args.Length == 2 && args[0] == "--scroll-ui-check") return ScrollingUiChecks.RunOnly(args[1]);
        if (args.Length == 2 && args[0] == "--capture-stress") return CaptureLifecycle.Run(args[1]);
        if (args.Length == 2 && args[0] == "--ui-fixture") return UiFixture.Run(args[1]);
        if (args.Length == 2 && args[0] == "--ui-fixture-dark") return UiFixture.Run(args[1], "dark");
        if (args.Length == 2 && args[0] == "--ui-fixture-light") return UiFixture.Run(args[1], "light");
        if (args.Length == 2 && args[0] == "--floating-fixture") return UiFixture.Run(args[1], "dark", true);
        if (args.Length == 2 && args[0] == "--floating-fixture-light") return UiFixture.Run(args[1], "light", true);
        output = Path.GetFullPath(args.Length > 0 ? args[0] : "check-results"); Directory.CreateDirectory(output);
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(application);
        Test("Default shortcuts map to four distinct native keys", () =>
        { var keys = new AppSettings().Bindings.Select(Hotkey.Parse).ToArray(); Equal(4, keys.Distinct().Count()); Equal(7u, keys[0].Modifiers); Equal(49u, keys[0].VirtualKey); });
        foreach (string invalid in new[] { "A", "Shift+2", "Ctrl", "Win+1", "Ctrl+Alt+A+B", "Ctrl+F12", "Ctrl+NotAKey" })
            Test("Reject invalid shortcut " + invalid, () => Throws<FormatException>(() => Hotkey.Parse(invalid)));
        Test("Shortcut parsing accepts spacing and case", () => Equal(Hotkey.Parse("Ctrl+Alt+Shift+1"), Hotkey.Parse(" alt + CTRL + shift + D1 ")));
        Test("Area selection maps scaled DIPs to physical pixels with negative origin", () =>
        { var r = CaptureGeometry.FromDrag(100, 50, 300, 150, 1920, 1080, new Rect(-2880, -200, 2880, 1620)); Equal(new Rect(-2730, -125, 300, 150), r); });
        Test("Area selection supports reverse drags and clamps to virtual screen", () =>
        { Equal(new Rect(-1920, 0, 1920, 1080), CaptureGeometry.FromDrag(9000, 1080, -50, -20, 1920, 1080, new Rect(-1920, 0, 1920, 1080))); });
        Test("Degenerate selections are refused", () => Throws<InvalidOperationException>(() => CaptureGeometry.Clip(new Rect(4, 4, 1, 1), new Rect(0, 0, 100, 100))));
        Test("Offscreen selections are refused", () => Throws<InvalidOperationException>(() => CaptureGeometry.Clip(new Rect(400, 400, 20, 20), new Rect(0, 0, 100, 100))));
        Test("Physical crop preserves exact pixel values", () =>
        { var crop = CaptureService.Crop(Pattern(), new Rect(-90, 25, 10, 8), new Rect(-100, 20, 64, 48)); Equal(10, crop.PixelWidth); Equal(8, crop.PixelHeight); var p = Pixels(crop); Equal((byte)10, p[0]); Equal((byte)5, p[1]); Equal((byte)120, p[2]); });
        Test("PNG save and reload preserve pixels and release file handle", () =>
        { string file = Path.Combine(Folder(), "export.png"); var image = Pattern(); CaptureService.Save(image, file); True(Pixels(image).SequenceEqual(Pixels(CaptureService.Load(file)))); using var exclusive = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        Test("Atomic export leaves previous PNG intact when destination is locked", () =>
        { string file = Path.Combine(Folder(), "export.png"); CaptureService.Save(Pattern(), file); byte[] before = File.ReadAllBytes(file); using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None)) FileFailure(() => CaptureService.Save(Pattern(12, 10), file)); True(before.SequenceEqual(File.ReadAllBytes(file))); True(!Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*.tmp").Any()); });
        Test("Capture reader rejects malformed image", () =>
        { string file = Path.Combine(Folder(), "broken.png"); File.WriteAllBytes(file, new byte[24]); Throws<InvalidDataException>(() => CaptureService.Load(file)); });
        Test("History persists image dimensions, mode and identifier across restart", () =>
        { string root = Folder(); var store = new HistoryStore(root); var entry = store.Add(Pattern(), CaptureKind.Window); var again = new HistoryStore(root); Equal(entry.Id, again.Entries.Single().Id); Equal(64, again.Entries[0].Width); Equal("Window", again.Entries[0].Mode); True(File.Exists(store.ImagePath(entry))); });
        Test("Corrupt primary and backup recover PNGs and preserve damaged index", () =>
        { string root = Folder(); var store = new HistoryStore(root); var entry = store.Add(Pattern(), CaptureKind.Area); File.WriteAllText(Path.Combine(root, "history.json"), "broken"); File.WriteAllText(Path.Combine(root, "history.json.bak"), "broken backup"); var recovered = new HistoryStore(root); Equal(entry.Id, recovered.Entries.Single().Id); True(recovered.Notice is not null); Equal(1, Directory.GetFiles(root, "history.json.damaged-*").Length); });
        Test("Orphan PNG after interrupted index commit is recovered", () =>
        { string root = Folder(); var store = new HistoryStore(root); store.Add(Pattern(), CaptureKind.Area); string id = Guid.NewGuid().ToString("N"); CaptureService.Save(Pattern(), Path.Combine(store.Images, id + ".png")); Equal(2, new HistoryStore(root).Entries.Count); });
        Test("History never follows a metadata path traversal", () =>
        { var store = new HistoryStore(Folder()); Throws<InvalidDataException>(() => store.ImagePath(new CaptureEntry("../outside", DateTimeOffset.UtcNow, "Area", 10, 10))); });
        Test("History commit failure preserves existing entries and images", () =>
        { string root = Folder(); var store = new HistoryStore(root); store.Add(Pattern(), CaptureKind.Area); using (File.Open(Path.Combine(root, "history.json"), FileMode.Open, FileAccess.Read, FileShare.None)) Throws<IOException>(() => store.Add(Pattern(), CaptureKind.Window)); Equal(1, store.Entries.Count); Equal(1, Directory.GetFiles(store.Images, "*.png").Length); });
        Test("Retention keeps latest 100 and does not delete unrelated files", () =>
        { var store = new HistoryStore(Folder()); string unrelated = Path.Combine(store.Images, "my-own-note.txt"); File.WriteAllText(unrelated, "keep"); for (int i = 0; i < 102; i++) store.Add(Pattern(2, 2), CaptureKind.Area); Equal(100, store.Entries.Count); Equal(100, Directory.GetFiles(store.Images, "*.png").Length); True(File.Exists(unrelated)); });
        Test("Removing history entry persists without affecting another capture", () =>
        { var store = new HistoryStore(Folder()); var first = store.Add(Pattern(), CaptureKind.Area); var second = store.Add(Pattern(), CaptureKind.Window); store.Remove(first); Equal(second.Id, new HistoryStore(store.Root).Entries.Single().Id); True(!File.Exists(store.ImagePath(first))); });
        Test("Missing history image is skipped on reload", () =>
        { var store = new HistoryStore(Folder()); var entry = store.Add(Pattern(), CaptureKind.Area); File.Delete(store.ImagePath(entry)); Equal(0, new HistoryStore(store.Root).Entries.Count); });
        Test("Invalid settings use defaults with an explicit recovery notice", () =>
        { var store = new HistoryStore(Folder()); File.WriteAllText(Path.Combine(store.Root, "settings.json"), "{broken"); Equal(new AppSettings(), store.LoadSettings()); True(store.Notice is not null); });
        Test("Valid shortcut settings survive restart", () =>
        { var store = new HistoryStore(Folder()); var settings = new AppSettings("Ctrl+Alt+Q", "Ctrl+Alt+W", "Ctrl+Alt+E", "Ctrl+Alt+R"); store.SaveSettings(settings); Equal(settings, new HistoryStore(store.Root).LoadSettings()); });

        EditorChecks.Run(output, Test, Pump);
        UiChecks.Run(output, Test);
        AnnotationChecks.Run(output, Test, Pump);
        ScrollingChecks.Run(Test, Pump);
        ScrollingUiChecks.Run(output, Test, Pump);

        var fixture = new FixtureWindow { Topmost = true }; fixture.Show(); fixture.Activate(); Pump(350); Native.DwmFlush();
        Test("Live native capture returns the synthetic window pixels", () =>
        {
            var bounds = Native.Bounds(new WindowInteropHelper(fixture).Handle);
            var frame = CaptureService.Capture(bounds);
            Equal(bounds.Width, frame.PixelWidth); Equal(bounds.Height, frame.PixelHeight);
            var bytes = Pixels(frame); int blue = 0, yellow = 0;
            for (int i = 0; i < bytes.Length; i += 4)
            { if (bytes[i] > 170 && bytes[i+1] is > 60 and < 130 && bytes[i+2] < 70) blue++; if (bytes[i] < 130 && bytes[i+1] > 170 && bytes[i+2] > 220) yellow++; }
            True(blue > frame.PixelWidth * frame.PixelHeight / 8, "Blue fixture region not captured");
            True(yellow > frame.PixelWidth * frame.PixelHeight / 10, "Yellow fixture region not captured");
            CaptureService.Save(frame, Path.Combine(output, "live-synthetic-window.png"));
        });
        Test("Clipboard image round trip preserves synthetic pixels", () =>
        {
            var original = Clipboard.GetDataObject();
            try
            {
                var image = Pattern(); var copy = CaptureService.Copy(image);
                while (!copy.IsCompleted) Pump(80); copy.GetAwaiter().GetResult();
                var received = Clipboard.GetImage() ?? throw new Exception("Clipboard image missing");
                True(Pixels(image).SequenceEqual(Pixels(received)));
            }
            finally { if (original is not null) try { Clipboard.SetDataObject(original, true); } catch (ExternalException) { } }
        });
        Test("Native shortcut conflict is reported and prior shortcuts restored", () =>
        {
            var handle = new WindowInteropHelper(fixture).Handle;
            using var service = new HotkeyService(handle, _ => { });
            var free = new List<string>();
            for (char letter = 'A'; letter <= 'Z' && free.Count < 5; letter++)
            {
                string binding = "Ctrl+Alt+Shift+" + letter; var key = Hotkey.Parse(binding);
                if (Native.RegisterHotKey(handle, 9902, key.Modifiers, key.VirtualKey)) { Native.UnregisterHotKey(handle, 9902); free.Add(binding); }
            }
            True(free.Count == 5, "Five unused test shortcuts were not available");
            var initial = new AppSettings(free[0], free[1], free[2], free[3]); service.Apply(initial);
            using var ready = new ManualResetEventSlim(); using var release = new ManualResetEventSlim();
            bool reserved = false; var blocked = Hotkey.Parse(free[4]);
            var blocker = new Thread(() => { reserved = Native.RegisterHotKey(IntPtr.Zero, 9900, blocked.Modifiers, blocked.VirtualKey); ready.Set(); release.Wait(); if (reserved) Native.UnregisterHotKey(IntPtr.Zero, 9900); }) { IsBackground = true };
            blocker.Start(); True(ready.Wait(5000));
            try
            {
                True(reserved); Throws<InvalidOperationException>(() => service.Apply(initial with { Area = free[4] }));
                bool acquiredOld = false; var old = Hotkey.Parse(initial.Area);
                var probe = new Thread(() => { acquiredOld = Native.RegisterHotKey(IntPtr.Zero, 9901, old.Modifiers, old.VirtualKey); if (acquiredOld) Native.UnregisterHotKey(IntPtr.Zero, 9901); });
                probe.Start(); True(probe.Join(5000)); True(!acquiredOld, "Prior shortcut was not restored");
            }
            finally { release.Set(); True(blocker.Join(5000)); }
        });
        Test("Repeated captures do not leak GDI bitmap handles", () =>
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            int before = GetGuiResources(Process.GetCurrentProcess().Handle, 0);
            True(before > 0, "GDI resource counter unavailable");
            var bounds = Native.Bounds(new WindowInteropHelper(fixture).Handle);
            for (int i = 0; i < 80; i++) _ = CaptureService.Capture(new Rect(bounds.Left + 20, bounds.Top + 80, 80, 60));
            GC.Collect(); GC.WaitForPendingFinalizers();
            int after = GetGuiResources(Process.GetCurrentProcess().Handle, 0); True(after - before < 8, $"GDI handle increase: {after-before}");
        });
        fixture.Close(); application.Shutdown();
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { passed = outcomes.Count - failures, failed = failures, checks = outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"{outcomes.Count - failures} passed, {failures} failed"); return failures == 0 ? 0 : 1;
    }
    [DllImport("user32.dll")] private static extern int GetGuiResources(IntPtr process, int flags);
}
