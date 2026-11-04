using System;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace Framelet;

internal static class GifConverter
{
    internal static string EncoderPath => Path.Combine(AppContext.BaseDirectory, "encoder", "ffmpeg.exe");
    internal static void RequireEncoder()
    {
        if (!File.Exists(EncoderPath)) throw new FileNotFoundException("The GIF converter is missing. Extract the complete Snipsera download, including its encoder folder.");
    }
    internal static async Task ConvertAsync(string input, string output, RecordingOptions options, CancellationToken token,
        IProgress<double>? progress = null, TimeSpan? duration = null)
    {
        RequireEncoder();
        token.ThrowIfCancellationRequested();
        if (File.Exists(output)) throw new IOException("The GIF output already exists. Existing recordings are never overwritten.");
        string palette = options.Quality switch { RecordingQuality.Compact => "96", RecordingQuality.High => "256", _ => "192" };
        string paletteFile = output + ".palette-" + Guid.NewGuid().ToString("N") + ".png";
        try
        {
            // Sample only the palette for faster modes; output keeps its selected resolution and every frame.
            // Separate passes bound decoded-frame memory even for long recordings.
            bool high = options.Quality == RecordingQuality.High;
            string sample = high ? $"fps={options.FramesPerSecond}" : $"fps={Math.Min(5, options.FramesPerSecond)},scale=w='min(iw,640)':h='min(ih,640)':force_original_aspect_ratio=decrease:flags=fast_bilinear";
            string dither = high ? "sierra2_4a" : "bayer:bayer_scale=3";
            progress?.Report(0);
            await RunAsync(new[] { "-i", input, "-vf", $"{sample},palettegen=max_colors={palette}:stats_mode=diff", "-frames:v", "1", paletteFile }, token, duration, value => progress?.Report(value * .2));
            await RunAsync(new[] { "-i", input, "-i", paletteFile, "-filter_complex_threads", "1", "-lavfi", $"fps={options.FramesPerSecond}[v];[v][1:v]paletteuse=dither={dither}:diff_mode=rectangle", "-loop", "0", "-f", "gif", output }, token, duration, value => progress?.Report(.2 + value * .8));
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("The GIF converter did not produce an image.");
        }
        finally { if (File.Exists(paletteFile)) File.Delete(paletteFile); }
    }
    private static async Task RunAsync(string[] arguments, CancellationToken token, TimeSpan? duration, Action<double> progress)
    {
        var start = new ProcessStartInfo(EncoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        token.ThrowIfCancellationRequested();
        foreach (string argument in new[] { "-hide_banner", "-loglevel", "error", "-nostdin", "-n", "-progress", "pipe:1", "-nostats" }) start.ArgumentList.Add(argument);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The GIF converter could not start.");
        var error = process.StandardError.ReadToEndAsync();
        var text = ReadProgressAsync(process.StandardOutput, duration, progress);
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(CancellationToken.None);
        string details = await error; await text;
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0)
            throw new InvalidOperationException("GIF conversion failed. The temporary MP4 is retained for recovery. " + details[^Math.Min(details.Length, 1000)..]);
        progress(1);
    }

    private static async Task ReadProgressAsync(StreamReader reader, TimeSpan? duration, Action<double> progress)
    {
        double last = 0;
        while (await reader.ReadLineAsync() is string line)
        {
            if (duration is { TotalSeconds: > 0 } length && line.StartsWith("out_time_us=", StringComparison.Ordinal) &&
                long.TryParse(line.AsSpan(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out long micros))
            {
                last = Math.Max(last, Math.Clamp(micros / 1_000_000d / length.TotalSeconds, 0, 1));
                progress(last);
            }
        }
    }
}
