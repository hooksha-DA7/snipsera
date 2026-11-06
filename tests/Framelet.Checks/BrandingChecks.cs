using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using Framelet;

internal static class BrandingChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var outcomes = new List<object>(); int failures = 0;
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        static void Require(bool condition, string message = "Icon verification failed") { if (!condition) throw new Exception(message); }
        Check("Application resource contains sculpted artwork at every required resolution", () =>
        {
            var frame = AppIcon.WindowImage;
            Require(frame.IsFrozen && frame.Decoder is not null);
            Require(frame.Decoder!.Frames.Select(f => f.PixelWidth).SequenceEqual(new[] { 16, 20, 24, 32, 48, 64, 128, 256 }));
            CaptureService.Save(frame.Decoder.Frames[^1], Path.Combine(output, "compiled-app-256.png"));
        });
        Check("Tray DPI selection uses exact 100,125,150,200 percent artwork", () =>
        {
            foreach (var pair in new[] { (96u, 16), (120u, 20), (144u, 24), (192u, 32), (240u, 40), (288u, 48), (384u, 64), (0u, 16), (999u, 64) })
                Require(AppIcon.TrayPixels(pair.Item1) == pair.Item2);
        });
        Check("Each embedded tray frame loads at its intended size after the resource stream closes", () =>
        {
            foreach (uint dpi in new uint[] { 96, 120, 144, 192, 240, 288, 384 })
            {
                using var icon = AppIcon.CreateTray(dpi); int size = AppIcon.TrayPixels(dpi);
                Require(icon.Width == size && icon.Height == size);
                using var image = icon.ToBitmap(); image.Save(Path.Combine(output, "compiled-tray-" + size + ".png"));
                Require(image.GetPixel(0, 0).A == 0 && image.GetPixel(size - 1, size - 1).A == 0);
                Require(Colours(image).Any(c => c.A == 255 && c.R == 255 && c.G == 106 && c.B == 85), "Coral pin was lost");
                Require(Colours(image).Any(c => c.A == 255 && c.R == 19 && c.G == 166 && c.B == 184), "Teal card was lost");
                Require(Colours(image).Any(c => c.A == 255 && c.R == 100 && c.G == 128 && c.B == 159), "Slate rear card was lost");
            }
        });
        Check("Main and tray ICO directories contain safe complete PNG blocks", () =>
        {
            foreach (string name in new[] { "App", "Tray" })
            {
                using var stream = AppIcon.Open(name); using var reader = new BinaryReader(stream);
                Require(reader.ReadUInt16() == 0 && reader.ReadUInt16() == 1);
                int count = reader.ReadUInt16(); Require(count == (name == "App" ? 8 : 7));
                var entries = new List<(int Size, uint Bytes, uint Offset)>();
                for (int i = 0; i < count; i++)
                {
                    int w = reader.ReadByte(), h = reader.ReadByte(); Require(w == h);
                    Require(reader.ReadByte() == 0 && reader.ReadByte() == 0 && reader.ReadUInt16() == 1 && reader.ReadUInt16() == 32);
                    entries.Add((w == 0 ? 256 : w, reader.ReadUInt32(), reader.ReadUInt32()));
                }
                long end = 6 + 16 * count;
                foreach (var entry in entries)
                {
                    Require(entry.Offset == end && entry.Bytes > 32 && entry.Offset + entry.Bytes <= stream.Length);
                    stream.Position = entry.Offset; byte[] png = reader.ReadBytes((int)entry.Bytes);
                    Require(png.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
                    using var bytes = new MemoryStream(png); var frame = BitmapFrame.Create(bytes, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    Require(frame.PixelWidth == entry.Size && frame.PixelHeight == entry.Size); end += entry.Bytes;
                }
                Require(end == stream.Length);
            }
        });
        Check("Large app icon is transparent and retains the selected teal and coral identity", () =>
        {
            using var bytes = new MemoryStream(); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(AppIcon.WindowImage.Decoder!.Frames[^1]); encoder.Save(bytes); bytes.Position = 0;
            using var image = new Bitmap(bytes); Require(image.Width == 256);
            Require(image.GetPixel(0, 0).A == 0 && image.GetPixel(255, 255).A == 0);
            Require(Colours(image).Count(c => c.A > 240 && c.G > c.R + 50 && c.B > c.R + 40) > 5000);
            Require(Colours(image).Count(c => c.A > 240 && c.R > 210 && c.G is > 50 and < 180 && c.B < 160) > 300);
        });
        Check("Window icon pixels stay available after resource decoding and collection", () =>
        {
            var frame = AppIcon.WindowImage; GC.Collect(); GC.WaitForPendingFinalizers();
            Require(ReferenceEquals(frame, AppIcon.WindowImage));
            foreach (var item in frame.Decoder!.Frames) { var pixels = new byte[item.PixelHeight * ((item.PixelWidth * item.Format.BitsPerPixel + 7) / 8)]; item.CopyPixels(pixels, pixels.Length / item.PixelHeight, 0); Require(pixels.Any(p => p != 0)); }
        });
        Check("Repeated tray icon replacement releases GDI handles", () =>
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); uint before = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
            for (int i = 0; i < 80; i++) { using var icon = AppIcon.CreateTray((uint)(96 + i % 4 * 24)); using var image = icon.ToBitmap(); }
            GC.Collect(); GC.WaitForPendingFinalizers(); uint after = GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle, 0);
            Require(after <= before + 4, $"GDI handles increased from {before} to {after}");
        });
        Check("Small application and tray frames retain their distinct approved roles", () =>
        {
            using var appStream = AppIcon.Open("App"); using var trayStream = AppIcon.Open("Tray");
            var appFrames = new IconBitmapDecoder(appStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            var trayFrames = new IconBitmapDecoder(trayStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames;
            for (int i = 0; i < 4; i++)
            {
                var a = new PngBitmapEncoder(); a.Frames.Add(appFrames[i]); using var ab = new MemoryStream(); a.Save(ab);
                var t = new PngBitmapEncoder(); t.Frames.Add(trayFrames[i]); using var tb = new MemoryStream(); t.Save(tb);
                Require(!ab.ToArray().SequenceEqual(tb.ToArray()), "Application frame was replaced with the flat tray design");
            }
        });
        var app = new CaptureApp(Path.Combine(output, "profile"));
        Check("Home and secondary window setup use the application identity instead of tray artwork", () =>
        {
            var home = new HomeWindow(app); Require(ReferenceEquals(home.Icon, AppIcon.WindowImage));
            var settings = new PreferencesWindow(app.Preferences, app); Require(ReferenceEquals(settings.Icon, AppIcon.WindowImage));
            settings.Close(); home.Close();
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, nativeTrayDisplay = "not tested; offscreen resource checks only", outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
    private static IEnumerable<Color> Colours(Bitmap image) { for (int y = 0; y < image.Height; y++) for (int x = 0; x < image.Width; x++) yield return image.GetPixel(x, y); }
    [DllImport("user32.dll")] private static extern uint GetGuiResources(IntPtr process, uint flags);
}
