using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Framelet;

public sealed class RecordingController : IDisposable
{
    private readonly CaptureApp app;
    private readonly RecordingStore store;
    private readonly Func<IRecordingEngine> createEngine;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timer;
    private readonly Stopwatch elapsed = new();
    private IRecordingEngine? engine;
    private IDisposable? presentation;
    private CancellationTokenSource? conversion;
    private Task? stopping;
    private string? temporary, destination;
    private bool cancelRequested, disposed;
    private RecordingOptions options;
    public RecordingOptions Options => options with { };
    public RecordingState State { get; private set; }
    public RecordingTarget? Target { get; private set; }
    public RecordingResult? LastResult { get; private set; }
    public IReadOnlyList<RecordingResult> RecentRecordings => store.Entries;
    public TimeSpan Elapsed => elapsed.Elapsed;
    public string Status { get; private set; } = "Choose what to record.";
    public bool IsActive => State is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused or RecordingState.Stopping or RecordingState.Converting;
    public bool CanStart => Target is not null && !IsActive && State != RecordingState.Selecting;
    public bool DoNotDisturbAvailable => false;
    public string DoNotDisturbExplanation => "Automatic Do not disturb needs Microsoft approval for its Focus API. Open Windows notification settings to enable it manually.";
    public event Action? Changed;

    internal RecordingController(CaptureApp app, string historyRoot, Func<IRecordingEngine>? createEngine = null)
    {
        this.app = app; store = new(historyRoot); this.createEngine = createEngine ?? (() => new ScreenRecordingEngine());
        options = store.LoadOptions(); LastResult = store.Entries.Count > 0 ? store.Entries[0] : null;
        timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += (_, _) =>
        {
            if (State is RecordingState.Recording or RecordingState.Paused && Target is { Kind: RecordingTargetKind.Window } target &&
                (!Native.IsWindowVisible(target.WindowHandle) || Native.IsIconic(target.WindowHandle))) _ = StopUnavailableTargetAsync();
            Changed?.Invoke();
        };
    }
    public void UpdateOptions(RecordingOptions options)
    {
        CheckDispatcher();
        if (IsActive || State == RecordingState.Selecting) throw new InvalidOperationException("Stop the current recording before changing its settings.");
        options.Validate();
        if (options.DoNotDisturb && !DoNotDisturbAvailable) throw new InvalidOperationException(DoNotDisturbExplanation);
        if (options.TargetKind != Options.TargetKind || options.CustomWidth != Options.CustomWidth || options.CustomHeight != Options.CustomHeight)
            Target = null;
        store.SaveOptions(options); this.options = options with { }; Notify(Target is null ? RecordingState.Idle : RecordingState.Ready, Target?.Label ?? "Choose what to record.");
    }
    public async Task SelectTargetAsync(RecordingTargetKind kind)
    {
        CheckDispatcher(); if (IsActive) throw new InvalidOperationException("Stop the current recording first.");
        var previous = Target; Notify(RecordingState.Selecting, "Select the content to record. Esc cancels.");
        try { Target = await app.SelectRecordingTargetAsync(kind, Options.CustomWidth, Options.CustomHeight) ?? previous; }
        catch { Target = previous; throw; }
        finally { Notify(Target is null ? RecordingState.Idle : RecordingState.Ready, Target?.Label ?? "Selection canceled."); }
    }
    public async Task StartAsync()
    {
        CheckDispatcher(); if (!CanStart) throw new InvalidOperationException("Choose a recording source first.");
        Options.Validate(); if (Options.DoNotDisturb) throw new InvalidOperationException(DoNotDisturbExplanation);
        if (Options.Format == RecordingFormat.Gif) GifConverter.RequireEncoder();
        var size = Options.OutputSize(Target!.Bounds);
        cancelRequested = false; stopping = null; elapsed.Reset();
        string id = DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8];
        temporary = Path.Combine(store.Root, "." + id + ".mp4");
        destination = Path.Combine(store.Root, "Snipsera-" + id + (Options.Format == RecordingFormat.Gif ? ".gif" : ".mp4"));
        Notify(RecordingState.Starting, "Preparing recording…");
        try
        {
            presentation = app.SuspendForRecording();
            await Task.Delay(220); Native.DwmFlush();
            engine = createEngine(); await engine.StartAsync(Target, Options with { }, temporary);
            elapsed.Start(); timer.Start(); Notify(RecordingState.Recording, "Recording");
            app.ShowRecordingBar();
            _ = ObserveCompletionAsync(engine);
        }
        catch (Exception ex)
        {
            Cleanup(); Notify(RecordingState.Failed, ex.Message); throw;
        }
    }
    internal void UseTarget(RecordingTarget target)
    {
        CheckDispatcher();
        if (IsActive) throw new InvalidOperationException("Stop the current recording before changing its source.");
        _ = Options.OutputSize(target.Bounds); Target = target;
        Notify(RecordingState.Ready, target.Label);
    }
    private async Task ObserveCompletionAsync(IRecordingEngine observed)
    {
        try
        {
            await observed.Completion;
            if (!disposed && engine == observed && State is RecordingState.Recording or RecordingState.Paused) await StopAsync();
        }
        catch (Exception ex)
        {
            if (!disposed && engine == observed && State is RecordingState.Recording or RecordingState.Paused)
            { Notify(RecordingState.Failed, ex.Message); Cleanup(); }
        }
    }
    private async Task StopUnavailableTargetAsync()
    {
        try { await StopAsync(); }
        catch { /* Finalization retains the error and recovery path in Status. */ }
    }
    public Task StopAsync()
    {
        CheckDispatcher();
        if (stopping is not null) return stopping;
        if (State is not (RecordingState.Recording or RecordingState.Paused)) return Task.CompletedTask;
        stopping = FinishAsync(); return stopping;
    }
    private async Task FinishAsync()
    {
        elapsed.Stop(); Notify(RecordingState.Stopping, "Finishing recording…");
        try
        {
            await engine!.StopAsync(); engine.Dispose(); engine = null;
            if (cancelRequested) { DeleteOwnedTemporary(); Notify(RecordingState.Idle, "Recording discarded."); return; }
            if (!File.Exists(temporary) || new FileInfo(temporary).Length == 0) throw new IOException("No recording data was produced.");
            if (Options.Format == RecordingFormat.Gif)
            {
                Notify(RecordingState.Converting, "Creating GIF…"); conversion = new();
                string workingGif = Path.ChangeExtension(temporary, ".gif");
                try { await GifConverter.ConvertAsync(temporary!, workingGif, Options, conversion.Token); File.Move(workingGif, destination!, false); }
                finally { if (File.Exists(workingGif) && store.Owns(workingGif)) File.Delete(workingGif); }
                DeleteOwnedTemporary();
            }
            else File.Move(temporary!, destination!, false);
            var size = Options.OutputSize(Target!.Bounds);
            LastResult = new(destination!, Options.Format, size.Width, size.Height, elapsed.Elapsed, DateTimeOffset.UtcNow);
            try { store.Add(LastResult); Notify(RecordingState.Completed, "Recording saved."); }
            catch (IOException) { Notify(RecordingState.Completed, "Recording saved, but recent-recording history could not be updated."); }
        }
        catch (OperationCanceledException) when (cancelRequested) { DeleteOwnedTemporary(); Notify(RecordingState.Idle, "Recording discarded."); }
        catch (Exception ex) { Notify(RecordingState.Failed, ex.Message + (File.Exists(temporary) ? " Recovery file: " + temporary : "")); throw; }
        finally { Cleanup(); }
    }
    public void TogglePause()
    {
        CheckDispatcher();
        if (State == RecordingState.Recording) { engine!.Pause(); elapsed.Stop(); Notify(RecordingState.Paused, "Paused"); }
        else if (State == RecordingState.Paused) { engine!.Resume(); elapsed.Start(); Notify(RecordingState.Recording, "Recording"); }
    }
    public async Task CancelAsync()
    {
        CheckDispatcher();
        if (!IsActive) return;
        if (State == RecordingState.Starting) throw new InvalidOperationException("Wait for recording to start, then discard it.");
        cancelRequested = true; conversion?.Cancel(); await StopAsync();
    }
    private void Notify(RecordingState state, string message) { State = state; Status = message; Changed?.Invoke(); }
    private void Cleanup()
    {
        timer.Stop(); elapsed.Stop(); engine?.Dispose(); engine = null; conversion?.Dispose(); conversion = null;
        presentation?.Dispose(); presentation = null; app.CloseRecordingBar(); Changed?.Invoke();
    }
    private void DeleteOwnedTemporary() { if (temporary is not null && store.Owns(temporary) && File.Exists(temporary)) File.Delete(temporary); }
    private void CheckDispatcher() { dispatcher.VerifyAccess(); ObjectDisposedException.ThrowIf(disposed, this); }
    public void OpenLastRecording()
    {
        if (LastResult is not null && store.IsRecording(LastResult) && File.Exists(LastResult.FilePath)) Open(LastResult.FilePath);
    }
    public void OpenOutputFolder() => Open(store.Root);
    public void OpenNotificationSettings() => Open("ms-settings:notifications");
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public void Dispose() { if (disposed) return; disposed = true; State = RecordingState.Idle; Cleanup(); }
}
