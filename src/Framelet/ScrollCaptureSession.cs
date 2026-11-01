using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DrawingRect = System.Drawing.Rectangle;

namespace Framelet;

public enum ScrollCapturePhase { Ready, Capturing, Finishing, Completed, Cancelled, Failed }

public sealed class ScrollCaptureSession : IDisposable
{
    public DrawingRect Bounds { get; }
    public ScrollCapturePhase Phase { get; private set; } = ScrollCapturePhase.Ready;
    public bool AutoScroll { get; private set; }
    public bool CanAutoScroll => input?.Available == true;
    public string Status { get; private set; } = "Select only scrolling content. Leave fixed headers and scrollbars outside the area.";
    public string? Warning { get; private set; }
    public BitmapSource? Preview { get; private set; }
    public int Width => Bounds.Width;
    public int Height => stitcher?.Height ?? Bounds.Height;
    public int Frames => stitcher?.Frames ?? 0;
    public Task<BitmapSource?> Completion => completion.Task;
    public event Action? Changed;
    private readonly Func<DrawingRect, BitmapSource> capture;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly IScrollCaptureInput? input;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<BitmapSource?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScrollStitcher? stitcher;
    private CancellationTokenSource? autoStep;
    private bool finishing, limited, disposed;
    private bool autoPending, wheelFallback;
    private int noMovement, autoFrames, autoRevision;
    private long autoIssued, settledSince, mismatchSince;
    private (ScrollCapturePhase, bool, string, string?, int, int, BitmapSource?)? notified;

    public ScrollCaptureSession(DrawingRect bounds, Func<DrawingRect, BitmapSource>? capture = null, bool nativeInput = true)
    {
        if (bounds.Width < 80 || bounds.Height < 80 || bounds.Width > ScrollStitcher.MaxSide || bounds.Height > ScrollStitcher.MaxSide || (long)bounds.Width * bounds.Height > ScrollStitcher.MaxPixels)
            throw new InvalidOperationException("Select a scrolling area at least 80 × 80 pixels, with room around it for controls.");
        Bounds = bounds; this.capture = capture ?? CaptureService.Capture;
        if (nativeInput) input = new ScrollCaptureInput(bounds, Cancel);
    }
    internal ScrollCaptureSession(DrawingRect bounds, Func<DrawingRect, BitmapSource> capture, IScrollCaptureInput input) : this(bounds, capture, false) { this.input = input; }
    public void Start()
    {
        if (Phase != ScrollCapturePhase.Ready || disposed) return;
        Phase = ScrollCapturePhase.Capturing; Status = "Scroll down slowly. Keep part of the previous view visible."; Notify(); _ = dispatcher.InvokeAsync(Run);
    }
    public void Finish()
    {
        if (Phase != ScrollCapturePhase.Capturing) return;
        finishing = true; AutoScroll = false; autoStep?.Cancel(); input?.StopAutoScroll(); Phase = ScrollCapturePhase.Finishing; Status = "Finishing capture…"; Notify();
    }
    public void Cancel()
    {
        if (completion.Task.IsCompleted) return;
        cancellation.Cancel(); AutoScroll = false; Phase = ScrollCapturePhase.Cancelled; Status = "Capture cancelled"; completion.TrySetResult(null); input?.Dispose(); Notify();
    }
    public void SetAutoScroll(bool enabled)
    {
        if (Phase is not (ScrollCapturePhase.Ready or ScrollCapturePhase.Capturing) || disposed) return;
        AutoScroll = enabled && CanAutoScroll && !limited; noMovement = 0; autoPending = false; wheelFallback = false; settledSince = 0; autoRevision++;
        autoStep?.Cancel(); input?.StopAutoScroll();
        if (enabled && !AutoScroll) Status = "Auto-scroll isn't available here. Scroll down manually.";
        Notify();
    }
    private async Task Run()
    {
        if (disposed || completion.Task.IsCompleted) return;
        var token = cancellation.Token; var elapsed = Stopwatch.StartNew();
        try
        {
            var previousSample = new ScrollFrame(capture(Bounds)); stitcher = new ScrollStitcher(previousSample); Preview = stitcher.Thumbnail(); Notify();
            long finishingSince = 0;
            long previewUpdated = 0;
            while (!token.IsCancellationRequested)
            {
                if (AutoScroll && !autoPending && !finishing && !limited)
                {
                    autoPending = true; autoFrames = Frames; autoIssued = elapsed.ElapsedMilliseconds; settledSince = 0;
                    int revision = autoRevision;
                    using var step = CancellationTokenSource.CreateLinkedTokenSource(token); step.CancelAfter(TimeSpan.FromSeconds(2)); autoStep = step;
                    bool sent;
                    try { sent = await input!.ScrollAsync(step.Token, wheelFallback); }
                    catch (OperationCanceledException) when (step.IsCancellationRequested) { sent = false; }
                    autoStep = null;
                    if (AutoScroll && revision == autoRevision && !sent) PauseAuto("Auto-scroll paused. Keep the pointer over the selected content and the target app active, or scroll manually.");
                }
                await Task.Delay(80, token);
                long now = elapsed.ElapsedMilliseconds;
                if (finishing && finishingSince == 0) finishingSince = now;
                if (input?.TargetMoved == true)
                { PauseAuto("Window moved. Choose Done to keep the captured portion, or Cancel to start again."); Warning = "The target window moved. The image ends at the last aligned view."; }
                else if (!limited)
                {
                    var frame = new ScrollFrame(capture(Bounds)); bool stable = previousSample.IsStableComparedTo(frame); previousSample = frame;
                    if (stable) { if (settledSince == 0) settledSince = now; } else settledSince = 0;
                    // Collect identifiable intermediate positions too. Waiting for all motion to stop can lose an entire viewport during manual scrolling.
                    var result = await Task.Run(() => stitcher.Add(frame), token); token.ThrowIfCancellationRequested();
                    switch (result)
                    {
                        case ScrollAppendStatus.Added:
                            noMovement = 0; mismatchSince = 0; Warning = null;
                            if (now - previewUpdated >= 350) { Preview = stitcher.Thumbnail(); previewUpdated = now; }
                            Status = "Scroll down to capture more, or choose Done."; break;
                        case ScrollAppendStatus.Unchanged:
                            mismatchSince = 0;
                            if (Warning is not null) Status = "Scroll down to capture more, or choose Done.";
                            Warning = null;
                            if (finishing && stable) stitcher.RefreshLastView(frame);
                            break;
                        case ScrollAppendStatus.Backwards:
                            mismatchSince = 0; Warning = null; PauseAuto("Scrolled back. Return to the last captured position, then continue down."); break;
                        case ScrollAppendStatus.NeedsOverlap:
                            if (mismatchSince == 0) mismatchSince = now;
                            if ((stable && now - mismatchSince >= 750) || now - mismatchSince >= 1500 || (finishing && now - finishingSince >= 1200))
                            { Warning = "The last view could not be aligned. The image ends at the last matched portion."; PauseAuto("Couldn't align this view. Scroll back slightly, then down slowly."); }
                            break;
                        case ScrollAppendStatus.LimitReached:
                            StopAtLimit(); break;
                    }
                    if (AutoScroll && autoPending)
                    {
                        long age = now - autoIssued;
                        if (settledSince > 0 && now - settledSince >= 160 && age >= 350 && result is (ScrollAppendStatus.Added or ScrollAppendStatus.Unchanged))
                        {
                            if (Frames > autoFrames) autoPending = false;
                            else if (age >= 900)
                            {
                                // A driver result proves only that input was submitted. Require pixels to move before treating it as progress.
                                wheelFallback = true; autoPending = false;
                                if (++noMovement >= 4) PauseAuto("No page movement detected. The page may be at its end. Choose Done, or scroll manually.");
                            }
                        }
                        else if (age > 3000) PauseAuto("Auto-scroll paused while the page is changing. Wait for it to settle, or scroll manually.");
                    }
                    if (finishing && (!stable || result == ScrollAppendStatus.NeedsOverlap) && now - finishingSince < 1200) { Notify(); continue; }
                    if (finishing && !stable) Warning ??= "The final view was still changing. Check the captured portion before using it.";
                }
                Notify();
                if (finishing) break;
                if (elapsed.Elapsed > TimeSpan.FromMinutes(10)) StopAtLimit();
            }
            token.ThrowIfCancellationRequested();
            var image = await Task.Run(stitcher.Image, token); token.ThrowIfCancellationRequested();
            Phase = ScrollCapturePhase.Completed; Status = "Scrolling capture ready"; completion.TrySetResult(image); Notify();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Phase = ScrollCapturePhase.Failed; AutoScroll = false; Status = ex.Message; completion.TrySetException(ex); Notify(); }
        finally { input?.Dispose(); }
    }
    private void StopAtLimit()
    { limited = true; PauseAuto("Capture limit reached. Choose Done to keep it, or Cancel."); Warning = "Scrolling capture reached its length or time limit. The saved image contains the captured portion."; Notify(); }
    private void PauseAuto(string status) { AutoScroll = false; autoPending = false; autoStep?.Cancel(); input?.StopAutoScroll(); Status = status; }
    private void Notify()
    {
        var state = (Phase, AutoScroll, Status, Warning, Height, Frames, Preview);
        if (disposed || notified == state) return;
        notified = state; Changed?.Invoke();
    }
    public void Dispose() { if (disposed) return; Cancel(); disposed = true; input?.Dispose(); }
}
