using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Framelet;

internal static class GifConverter
{
    internal static string EncoderPath => Path.Combine(AppContext.BaseDirectory, "encoder", "ffmpeg.exe");
    internal static void RequireEncoder()
    {
        if (!File.Exists(EncoderPath)) throw new FileNotFoundException("The GIF converter is missing. Extract the complete Framelet download, including its encoder folder.");
    }
    internal static async Task ConvertAsync(string input, string output, RecordingOptions options, CancellationToken token)
    {
        RequireEncoder();
        string palette = options.Quality switch { RecordingQuality.Compact => "96", RecordingQuality.High => "256", _ => "192" };
        string paletteFile = output + ".palette-" + Guid.NewGuid().ToString("N") + ".png";
        try
        {
            // Two passes keep decoded video frames out of an unbounded filter queue.
            await RunAsync(new[] { "-i", input, "-vf", $"fps={options.FramesPerSecond},palettegen=max_colors={palette}:stats_mode=diff", "-frames:v", "1", paletteFile }, token);
            await RunAsync(new[] { "-i", input, "-i", paletteFile, "-filter_complex_threads", "1", "-lavfi", $"fps={options.FramesPerSecond}[v];[v][1:v]paletteuse=dither=sierra2_4a", "-loop", "0", "-f", "gif", output }, token);
            if (!File.Exists(output) || new FileInfo(output).Length == 0) throw new IOException("The GIF converter did not produce an image.");
        }
        finally { if (File.Exists(paletteFile)) File.Delete(paletteFile); }
    }
    private static async Task RunAsync(string[] arguments, CancellationToken token)
    {
        var start = new ProcessStartInfo(EncoderPath) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (string argument in new[] { "-hide_banner", "-nostdin", "-n" }) start.ArgumentList.Add(argument);
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The GIF converter could not start.");
        var error = process.StandardError.ReadToEndAsync(token);
        var text = process.StandardOutput.ReadToEndAsync(token);
        using var cancel = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        await process.WaitForExitAsync(CancellationToken.None);
        token.ThrowIfCancellationRequested();
        string details = await error; await text;
        if (process.ExitCode != 0)
            throw new InvalidOperationException("GIF conversion failed. The temporary MP4 is retained for recovery. " + details[^Math.Min(details.Length, 1000)..]);
    }
}
