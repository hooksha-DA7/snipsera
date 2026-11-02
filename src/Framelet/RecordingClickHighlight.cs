using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using ScreenRecorderLib;

namespace Framelet;

internal sealed class RecordingClickHighlight : IDisposable
{
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public Native.Point Point; public uint Data, Flags, Time; public UIntPtr Extra; }
    private delegate IntPtr MouseCallback(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, MouseCallback callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? module);
    private readonly Recorder recorder;
    private readonly ImageOverlay overlay;
    private readonly IReadOnlyList<string> frames;
    private readonly RecordingTarget target;
    private readonly Size output;
    private readonly Action<Exception> failed;
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly MouseCallback callback;
    private IntPtr hook;
    private int frame;
    private bool paused, disposed;

    internal RecordingClickHighlight(Recorder recorder, ImageOverlay overlay, IReadOnlyList<string> frames, RecordingTarget target, Size output, Action<Exception> failed)
    {
        this.recorder = recorder; this.overlay = overlay; this.frames = frames; this.target = target; this.output = output; this.failed = failed;
        callback = ObserveMouse;
        hook = SetWindowsHookEx(14, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Windows could not enable click highlighting.");
        timer.Tick += (_, _) =>
        {
            if (++frame >= frames.Count) { timer.Stop(); frame = 0; }
            ApplyFrame(frame);
        };
    }
    private IntPtr ObserveMouse(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && !paused && !disposed && message.ToInt64() is 0x0201 or 0x0204)
        {
            try
            {
                var input = Marshal.PtrToStructure<MouseData>(data);
                Native.GetWindowThreadProcessId(WindowFromPoint(input.Point), out uint process);
                if (process != Environment.ProcessId)
                {
                    var point = input.Point;
                    dispatcher.BeginInvoke(() => Pulse(new Point(point.X, point.Y)));
                }
            }
            catch (Exception ex) { dispatcher.BeginInvoke(() => failed(ex)); }
        }
        return CallNextHookEx(hook, code, message, data);
    }
    internal static Rectangle? Place(Rectangle source, Size output, Point point)
    {
        if (!source.Contains(point) || source.Width < 1 || source.Height < 1) return null;
        double scale = Math.Min((double)output.Width / source.Width, (double)output.Height / source.Height);
        int size = Math.Max(8, (int)Math.Round(RecordingClickVisual.CanvasSize * scale));
        double x = (output.Width - source.Width * scale) / 2 + (point.X - source.Left) * scale;
        double y = (output.Height - source.Height * scale) / 2 + (point.Y - source.Top) * scale;
        return new((int)Math.Round(x - size / 2d), (int)Math.Round(y - size / 2d), size, size);
    }
    internal void Pulse(Point point)
    {
        if (paused || disposed) return;
        var source = target.Kind == RecordingTargetKind.Window ? Native.Bounds(target.WindowHandle) : target.Bounds;
        if (Place(source, output, point) is not Rectangle bounds) return;
        overlay.Offset = new ScreenSize(bounds.X, bounds.Y); overlay.Size = new ScreenSize(bounds.Width, bounds.Height);
        frame = 1; ApplyFrame(frame); timer.Stop(); timer.Start();
    }
    private void ApplyFrame(int index)
    {
        if (disposed) return;
        try { overlay.SourcePath = frames[index]; recorder.GetDynamicOptionsBuilder().SetUpdatedOverlay(overlay).Apply(); }
        catch (Exception ex) { timer.Stop(); failed(new InvalidOperationException("Click highlighting stopped: " + ex.Message, ex)); }
    }
    internal void SetPaused(bool value) { paused = value; timer.Stop(); ApplyFrame(0); }
    public void Dispose()
    {
        if (disposed) return; disposed = true; timer.Stop();
        if (hook != IntPtr.Zero) { UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
        GC.KeepAlive(callback);
    }
}
