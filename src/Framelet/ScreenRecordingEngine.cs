using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ScreenRecorderLib;
using Forms = System.Windows.Forms;

namespace Framelet;

internal interface IRecordingEngine : IDisposable
{
    Task Completion { get; }
    Task StartAsync(RecordingTarget target, RecordingOptions options, string path);
    Task StopAsync();
    void Pause();
    void Resume();
}

internal sealed class ScreenRecordingEngine : IRecordingEngine
{
    private Recorder? recorder;
    private RecordingClickHighlight? clicks;
    private IReadOnlyList<string>? clickFrames;
    private string? clickFolder;
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Completion => finished.Task;

    internal static RecorderOptions Configure(RecordingTarget target, RecordingOptions options)
    {
        options.Validate(); var size = options.OutputSize(target.Bounds);
        RecordingSourceBase source;
        if (target.Kind == RecordingTargetKind.Window)
        {
            if (target.WindowHandle == IntPtr.Zero || !Native.IsWindowVisible(target.WindowHandle) || Native.IsIconic(target.WindowHandle))
                throw new InvalidOperationException("That window is no longer available. Choose a visible window again.");
            source = new WindowRecordingSource(target.WindowHandle) { IsCursorCaptureEnabled = options.ShowCursor, IsBorderRequired = true };
        }
        else
        {
            var monitor = Forms.Screen.FromRectangle(target.Bounds);
            if (!monitor.Bounds.Contains(target.Bounds)) throw new InvalidOperationException("Keep the recording area within one display.");
            source = new DisplayRecordingSource(monitor.DeviceName)
            {
                RecorderApi = RecorderApi.WindowsGraphicsCapture,
                IsCursorCaptureEnabled = options.ShowCursor,
                SourceRect = new ScreenRect(target.Bounds.X - monitor.Bounds.X, target.Bounds.Y - monitor.Bounds.Y, target.Bounds.Width, target.Bounds.Height)
            };
        }
        var audio = new List<AudioSourceBase>();
        if (options.Microphone) audio.Add(CaptureAudioSource.Default ?? throw new InvalidOperationException("No default microphone is available. Connect one or turn Microphone off."));
        if (options.ComputerAudio) audio.Add(LoopbackAudioSource.Default ?? throw new InvalidOperationException("No default playback device is available. Connect one or turn Computer audio off."));
        return new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = new List<RecordingSourceBase> { source } },
            OutputOptions = new OutputOptions { RecorderMode = RecorderMode.Video, OutputFrameSize = new ScreenSize(size.Width, size.Height), Stretch = StretchMode.Uniform },
            AudioOptions = new AudioOptions { IsAudioEnabled = audio.Count > 0, AudioSources = audio, Bitrate = AudioBitrate.bitrate_128kbps, Channels = AudioChannels.Stereo },
            VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder { BitrateMode = H264BitrateControlMode.Quality, EncoderProfile = H264Profile.High },
                Quality = options.EncoderQuality, Framerate = options.FramesPerSecond, IsFixedFramerate = true,
                IsHardwareEncodingEnabled = false, IsThrottlingDisabled = false, IsLowLatencyEnabled = false,
                IsFragmentedMp4Enabled = false, IsMp4FastStartEnabled = true
            },
            MouseOptions = new MouseOptions
            {
                IsMousePointerEnabled = options.ShowCursor, IsMouseClicksDetected = false
            },
            LogOptions = new LogOptions { IsLogEnabled = false }
        };
    }

    public async Task StartAsync(RecordingTarget target, RecordingOptions options, string path)
    {
        var configuration = Configure(target, options);
        ImageOverlay? clickOverlay = null;
        if (options.HighlightClicks)
        {
            clickFolder = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".click-" + Guid.NewGuid().ToString("N"));
            clickFrames = RecordingClickVisual.CreateFrames(clickFolder);
            clickOverlay = new ImageOverlay(clickFrames[0]) { AnchorPoint = Anchor.TopLeft, Offset = new ScreenSize(0, 0), Size = new ScreenSize(64, 64) };
            configuration.OverlayOptions = new OverLayOptions { Overlays = new List<RecordingOverlayBase> { clickOverlay } };
        }
        recorder = Recorder.CreateRecorder(configuration);
        recorder.OnStatusChanged += (_, e) => { if (e.Status == RecorderStatus.Recording) started.TrySetResult(); };
        recorder.OnRecordingComplete += (_, _) => finished.TrySetResult();
        recorder.OnRecordingFailed += (_, e) =>
        {
            var error = new InvalidOperationException("Recording stopped: " + e.Error);
            started.TrySetException(error); finished.TrySetException(error);
        };
        recorder.Record(path);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (clickOverlay is not null) clicks = new RecordingClickHighlight(recorder, clickOverlay, clickFrames!, target, options.OutputSize(target.Bounds), ex => finished.TrySetException(ex));
        }
        catch { recorder.Dispose(); recorder = null; throw; }
    }
    public async Task StopAsync()
    {
        if (recorder is null) return;
        clicks?.Dispose(); clicks = null; recorder.Stop(); await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
    public void Pause() { clicks?.SetPaused(true); recorder?.Pause(); }
    public void Resume() { recorder?.Resume(); clicks?.SetPaused(false); }
    internal void PreviewClick(System.Drawing.Point point) => clicks?.Pulse(point);
    public void Dispose()
    {
        clicks?.Dispose(); clicks = null; recorder?.Dispose(); recorder = null; started.TrySetCanceled(); finished.TrySetCanceled();
        if (clickFrames is not null)
        {
            foreach (string file in clickFrames) if (Path.GetDirectoryName(Path.GetFullPath(file)) == clickFolder && File.Exists(file)) File.Delete(file);
            if (clickFolder is not null && Directory.Exists(clickFolder) && Directory.GetFileSystemEntries(clickFolder).Length == 0) Directory.Delete(clickFolder);
            clickFrames = null;
        }
    }
}
