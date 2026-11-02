using System;
using System.Drawing;

namespace Framelet;

public enum RecordingTargetKind { Area, Window, Display, Custom }
public enum RecordingFormat { Mp4, Gif }
public enum RecordingQuality { Compact, Balanced, High }
public enum RecordingResolution { Original, FullHd, Hd, Half }
public enum RecordingState { Idle, Selecting, Ready, Starting, Recording, Paused, Stopping, Converting, Completed, Failed }

public sealed record RecordingOptions
{
    public RecordingFormat Format { get; set; } = RecordingFormat.Mp4;
    public RecordingTargetKind TargetKind { get; set; } = RecordingTargetKind.Area;
    public RecordingQuality Quality { get; set; } = RecordingQuality.Balanced;
    public RecordingResolution Resolution { get; set; } = RecordingResolution.Original;
    public int FramesPerSecond { get; set; } = 30;
    public int CustomWidth { get; set; } = 1280;
    public int CustomHeight { get; set; } = 720;
    public bool Microphone { get; set; }
    public bool ComputerAudio { get; set; }
    public bool ShowCursor { get; set; } = true;
    public bool HighlightClicks { get; set; }
    public bool DoNotDisturb { get; set; }

    public void Validate()
    {
        if (!Enum.IsDefined(Format) || !Enum.IsDefined(TargetKind) || !Enum.IsDefined(Quality) || !Enum.IsDefined(Resolution))
            throw new InvalidOperationException("Choose a supported recording option.");
        if (FramesPerSecond is not (5 or 10 or 15 or 24 or 30 or 60) || Format == RecordingFormat.Gif && FramesPerSecond > 30)
            throw new InvalidOperationException("Choose 5, 10, 15, 24, 30 or 60 FPS. GIF supports up to 30 FPS.");
        if (CustomWidth is < 16 or > 7680 || CustomHeight is < 16 or > 4320)
            throw new InvalidOperationException("Custom dimensions must be between 16 × 16 and 7680 × 4320 pixels.");
        if (TargetKind == RecordingTargetKind.Custom && (CustomWidth % 2 != 0 || CustomHeight % 2 != 0))
            throw new InvalidOperationException("Use even custom dimensions so the H.264 encoder can preserve the exact frame size.");
        if (Format == RecordingFormat.Gif && (Microphone || ComputerAudio))
            throw new InvalidOperationException("GIF does not contain audio. Choose MP4 to record a microphone or computer audio.");
    }

    public Size OutputSize(Rectangle source)
    {
        if (source.Width < 16 || source.Height < 16) throw new InvalidOperationException("Select a recording area at least 16 × 16 pixels.");
        double scale = Resolution switch
        {
            RecordingResolution.Half => .5,
            RecordingResolution.FullHd => Math.Min(1, Math.Min(1920d / source.Width, 1080d / source.Height)),
            RecordingResolution.Hd => Math.Min(1, Math.Min(1280d / source.Width, 720d / source.Height)),
            _ => 1
        };
        var size = new Size(Math.Max(2, (int)(source.Width * scale) / 2 * 2), Math.Max(2, (int)(source.Height * scale) / 2 * 2));
        if (size.Width > 4096 || size.Height > 4096 || (long)size.Width * size.Height > 4096L * 2304)
            throw new InvalidOperationException("Choose 1080p, 720p or Half resolution for this large capture.");
        return size;
    }

    public int EncoderQuality => Quality switch { RecordingQuality.Compact => 45, RecordingQuality.High => 90, _ => 70 };
}

public sealed record RecordingTarget(RecordingTargetKind Kind, Rectangle Bounds, IntPtr WindowHandle, string Label);
public sealed record RecordingResult(string FilePath, RecordingFormat Format, int Width, int Height, TimeSpan Duration, DateTimeOffset Created);
