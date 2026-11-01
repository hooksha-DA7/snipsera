using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace Framelet;

// Application functionality invoked only by the user's Auto-scroll control; it never sends clicks or keys.
internal sealed class ScrollCaptureInput : IDisposable
{
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wp, IntPtr lp);
    private readonly IntPtr target, root;
    private readonly Rectangle windowBounds, area;
    private HwndSource? escape;
    private readonly ScrollAutomation? automation;
    public bool Available => target != IntPtr.Zero;
    public bool TargetMoved => root != IntPtr.Zero && (!Native.IsWindowVisible(root) || Native.IsIconic(root) || Native.Bounds(root) != windowBounds);
    public ScrollCaptureInput(Rectangle area, Action cancel)
    {
        this.area = area;
        var point = new Native.Point { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 };
        var candidate = WindowFromPoint(point); Native.GetWindowThreadProcessId(candidate, out uint process);
        if (candidate != IntPtr.Zero && process != Environment.ProcessId)
        { target = candidate; root = Native.GetAncestor(candidate, 2); windowBounds = Native.Bounds(root); automation = new ScrollAutomation(root, new System.Windows.Point(point.X, point.Y)); }
        escape = new HwndSource(new HwndSourceParameters("Framelet scrolling escape") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        escape.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        { if (msg == 0x0312 && wp.ToInt32() == 3901) { handled = true; escape?.Dispatcher.BeginInvoke(cancel); } return IntPtr.Zero; });
        if (!Native.RegisterHotKey(escape.Handle, 3901, 0x4000, 0x1B)) { escape.Dispose(); escape = null; }
    }
    public async Task<bool> ScrollAsync(CancellationToken token)
    {
        if (!Available || TargetMoved) return false;
        if (automation is not null && await automation.ScrollAsync(token)) return true;
        if (token.IsCancellationRequested || TargetMoved) return false;
        int x = area.Left + area.Width / 2, y = area.Top + area.Height / 2;
        long coordinates = (ushort)(short)x | ((long)(ushort)(short)y << 16);
        return PostMessage(target, 0x020A, new IntPtr(unchecked(-120 << 16)), new IntPtr(coordinates));
    }
    public void Dispose() { automation?.Dispose(); if (escape is not null) { Native.UnregisterHotKey(escape.Handle, 3901); escape.Dispose(); escape = null; } }
}
