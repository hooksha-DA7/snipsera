using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

internal static class RecordingPreview
{
    internal static async Task<BitmapSource> ThumbnailAsync(string path, string dataRoot)
    {
        string folder = Path.Combine(dataRoot, "recording-thumbnails");
        Directory.CreateDirectory(folder);
        string thumbnail = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        try
        {
            var start = new ProcessStartInfo(GifConverter.EncoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-threads", "2", "-i", path,
                "-frames:v", "1", "-vf", "scale=480:-2", "-update", "1", thumbnail }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException("Could not create a recording thumbnail.");
            var error = process.StandardError.ReadToEndAsync(); var output = process.StandardOutput.ReadToEndAsync();
            using var canceled = timeout.Token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
            await process.WaitForExitAsync(); await error; await output;
            timeout.Token.ThrowIfCancellationRequested();
            if (process.ExitCode != 0) throw new IOException("Could not read the recording thumbnail.");
            return CaptureService.Load(thumbnail);
        }
        finally { if (File.Exists(thumbnail)) File.Delete(thumbnail); }
    }

    internal static BitmapSource Placeholder()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(38, 45, 56)), null, new Rect(0, 0, 288, 180));
            dc.DrawGeometry(Brushes.White, null, Geometry.Parse("M 134,72 L 160,90 L 134,108 Z"));
        }
        var bitmap = new RenderTargetBitmap(288, 180, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); bitmap.Freeze(); return bitmap;
    }

    internal static async Task CopyAsync(RecordingResult result)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetDataObject(DragExport.CreateFileData(result.FilePath), true); return; }
            catch (System.Runtime.InteropServices.ExternalException) when (attempt < 5) { await Task.Delay(60); }
        }
    }

    internal static void Open(RecordingResult result)
    {
        if (!File.Exists(result.FilePath)) throw new FileNotFoundException("This recording has been moved or removed.");
        Process.Start(new ProcessStartInfo(result.FilePath) { UseShellExecute = true });
    }
}
