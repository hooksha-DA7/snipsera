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
    private readonly ScrollCaptureInput? input;
    private readonly CancellationTokenSource cancellation = new();
    private readonly TaskCompletionSource<BitmapSource?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private ScrollStitcher? stitcher;
    private CancellationTokenSource? autoStep;
    private bool finishing, limited, disposed;
    private int unchanged;

    public ScrollCaptureSession(DrawingRect bounds, Func<DrawingRect, BitmapSource>? capture = null, bool nativeInput = true)
    {
        if (bounds.Width < 80 || bounds.Height < 80 || bounds.Width > ScrollStitcher.MaxSide || bounds.Height > ScrollStitcher.MaxSide || (long)bounds.Width * bounds.Height > ScrollStitcher.MaxPixels)
            throw new InvalidOperationException("Select a scrolling area at least 80 × 80 pixels, with room around it for controls.");
        Bounds = bounds; this.capture = capture ?? CaptureService.Capture;
        if (nativeInput) input = new ScrollCaptureInput(bounds, Cancel);
    }
    public void Start()
    {
        if (Phase != ScrollCapturePhase.Ready || disposed) return;
        Phase = ScrollCapturePhase.Capturing; Status = "Scroll down slowly. Keep part of the previous view visible."; Notify(); _ = dispatcher.InvokeAsync(Run);
    }
    public void Finish()
    {
        if (Phase != ScrollCapturePhase.Capturing) return;
        finishing = true; AutoScroll = false; autoStep?.Cancel(); Phase = ScrollCapturePhase.Finishing; Status = "Finishing capture…"; Notify();
    }
    public void Cancel()
    {
        if (completion.Task.IsCompleted) return;
        cancellation.Cancel(); AutoScroll = false; Phase = ScrollCapturePhase.Cancelled; Status = "Capture cancelled"; completion.TrySetResult(null); input?.Dispose(); Notify();
    }
    public void SetAutoScroll(bool enabled)
    {
        if (Phase is not (ScrollCapturePhase.Ready or ScrollCapturePhase.Capturing) || disposed) return;
        AutoScroll = enabled && CanAutoScroll && !limited; unchanged = 0;
        if (!AutoScroll) autoStep?.Cancel();
        if (enabled && !AutoScroll) Status = "Auto-scroll isn't available here. Scroll down manually.";
        Notify();
    }
    private async Task Run()
    {
        if (disposed || completion.Task.IsCompleted) return;
        var token = cancellation.Token; var elapsed = Stopwatch.StartNew();
        try
        {
            Native.DwmFlush(); stitcher = new ScrollStitcher(new ScrollFrame(capture(Bounds))); Preview = stitcher.Thumbnail(); Notify();
            while (!token.IsCancellationRequested)
            {
                if (AutoScroll)
                {
                    using var step = CancellationTokenSource.CreateLinkedTokenSource(token); step.CancelAfter(TimeSpan.FromSeconds(2)); autoStep = step;
                    bool scrolled = await input!.ScrollAsync(step.Token); autoStep = null;
                    if (AutoScroll && !scrolled) { AutoScroll = false; Status = "Auto-scroll stopped. You can scroll manually or choose Done."; Notify(); }
                }
                await Task.Delay(210, token);
                if (input?.TargetMoved == true)
                { AutoScroll = false; Warning = "The target window moved. The image ends at the last aligned view."; Status = "Window moved. Choose Done to keep the captured portion, or Cancel to start again."; Notify(); }
                else if (!limited)
                {
                    var first = new ScrollFrame(capture(Bounds)); await Task.Delay(90, token); var frame = new ScrollFrame(capture(Bounds));
                    if (first.Distance(frame) > 1.1)
                    {
                        Status = "Waiting for scrolling to settle…"; Notify();
                        if (!finishing) continue;
                        await Task.Delay(220, token); first = new ScrollFrame(capture(Bounds)); await Task.Delay(90, token); frame = new ScrollFrame(capture(Bounds));
                    }
                    if (first.Distance(frame) <= 1.1)
                    {
                        var result = await Task.Run(() => stitcher.Add(frame), token); token.ThrowIfCancellationRequested();
                        switch (result)
                        {
                            case ScrollAppendStatus.Added:
                                unchanged = 0; Warning = null; Preview = stitcher.Thumbnail(); Status = "Scroll down to capture more, or choose Done."; break;
                            case ScrollAppendStatus.Unchanged:
                                if (Warning is not null) Status = "Scroll down to capture more, or choose Done.";
                                Warning = null;
                                if (AutoScroll && ++unchanged >= 4) { AutoScroll = false; Status = "No further movement detected. Choose Done, or continue scrolling manually."; }
                                break;
                            case ScrollAppendStatus.Backwards:
                                AutoScroll = false; Warning = null; Status = "Scrolled back. Return to the last captured position, then continue down."; break;
                            case ScrollAppendStatus.NeedsOverlap:
                                AutoScroll = false; Warning = "The last view could not be aligned. The image ends at the last matched portion."; Status = "Couldn't align this view. Scroll back slightly, then down slowly."; break;
                            case ScrollAppendStatus.LimitReached:
                                StopAtLimit(); break;
                        }
                    }
                    else { Warning = "The final view was still moving. The image ends at the last stable portion."; Status = "Waiting for a stable view…"; }
                    Notify();
                }
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
    { limited = true; AutoScroll = false; Warning = "Scrolling capture reached its length or time limit. The saved image contains the captured portion."; Status = "Capture limit reached. Choose Done to keep it, or Cancel."; Notify(); }
    private void Notify() { if (!disposed) Changed?.Invoke(); }
    public void Dispose() { if (disposed) return; Cancel(); disposed = true; input?.Dispose(); }
}
