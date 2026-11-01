using System;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;

namespace Framelet;

// The app's opt-in Auto-scroll driver. UIA stays on one background MTA thread; no document text is read.
internal sealed class ScrollAutomation : IDisposable
{
    private sealed record Request(CancellationToken Token, TaskCompletionSource<bool> Result);
    private readonly BlockingCollection<Request> requests = new(1);
    private readonly Point point;
    private readonly IntPtr target;
    private volatile bool disposed;
    public ScrollAutomation(IntPtr target, Point point)
    {
        this.target = target; this.point = point;
        var thread = new Thread(Work) { IsBackground = true, Name = "Framelet Auto-scroll" }; thread.SetApartmentState(ApartmentState.MTA); thread.Start();
    }
    public async Task<bool> ScrollAsync(CancellationToken token)
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            if (disposed || token.IsCancellationRequested || !requests.TryAdd(new Request(token, result))) return false;
            return await result.Task.WaitAsync(token);
        }
        catch (InvalidOperationException) { return false; }
        catch (OperationCanceledException) { return false; }
    }
    private void Work()
    {
        ScrollPattern? pattern = null; bool searched = false;
        foreach (var request in requests.GetConsumingEnumerable())
        {
            bool moved = false;
            try
            {
                if (!disposed && !request.Token.IsCancellationRequested)
                {
                    if (!searched) { searched = true; pattern = FindPattern(); }
                    if (pattern is not null && !disposed && !request.Token.IsCancellationRequested)
                    { pattern.Scroll(ScrollAmount.NoAmount, ScrollAmount.SmallIncrement); moved = true; }
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or ElementNotEnabledException or InvalidOperationException or ArgumentException or COMException or TimeoutException or UnauthorizedAccessException)
            { pattern = null; }
            request.Result.TrySetResult(moved);
        }
    }
    private ScrollPattern? FindPattern()
    {
        // Resolve from the recorded external HWND. An accessibility hit-test can select our overlay even where its alpha is zero.
        var root = AutomationElement.FromHandle(target);
        if (root.Current.ProcessId == Environment.ProcessId) return null;
        var elements = root.FindAll(TreeScope.Element | TreeScope.Descendants, new PropertyCondition(AutomationElement.IsScrollPatternAvailableProperty, true));
        ScrollPattern? best = null; double smallest = double.PositiveInfinity;
        for (int index = 0; index < Math.Min(elements.Count, 128); index++)
        {
            var element = elements[index];
            int process = element.Current.ProcessId;
            if (process == Environment.ProcessId || process <= 0) continue;
            var bounds = element.Current.BoundingRectangle;
            if (!bounds.Contains(point) || bounds.Width * bounds.Height >= smallest) continue;
            if (element.TryGetCurrentPattern(ScrollPattern.Pattern, out object value) && value is ScrollPattern scroll && scroll.Current.VerticallyScrollable)
            { best = scroll; smallest = bounds.Width * bounds.Height; }
        }
        return best;
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; requests.CompleteAdding();
        while (requests.TryTake(out var request)) request.Result.TrySetResult(false);
    }
}
