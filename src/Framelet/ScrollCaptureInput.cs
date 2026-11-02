using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;

namespace Framelet;

internal interface IScrollCaptureInput : IDisposable
{
    bool Available { get; }
    bool TargetMoved { get; }
    Task<bool> ScrollAsync(CancellationToken token, bool useWheel);
    void StopAutoScroll();
}

// Product functionality invoked by the user's Auto-scroll control. No clicks or keys are generated.
internal sealed class ScrollCaptureInput : IScrollCaptureInput
{
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(Native.Point point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int count);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] WheelInput[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr ExtraInfo; }
    // MOUSEINPUT is the largest INPUT union member on both Windows architectures.
    [StructLayout(LayoutKind.Sequential)] internal struct WheelInput { public uint Type; public MouseInput Mouse; }
    private readonly IntPtr target, root;
    private readonly Rectangle windowBounds, area;
    private HwndSource? escape;
    private readonly ScrollAutomation? automation;
    private readonly bool browser;
    private bool wheelActive;
    public bool Available => target != IntPtr.Zero;
    public bool TargetMoved => root != IntPtr.Zero && (!Native.IsWindowVisible(root) || Native.IsIconic(root) || Native.Bounds(root) != windowBounds);
    public ScrollCaptureInput(Rectangle area, Action cancel)
    {
        this.area = area;
        var point = new Native.Point { X = area.Left + area.Width / 2, Y = area.Top + area.Height / 2 };
        var candidate = WindowFromPoint(point); Native.GetWindowThreadProcessId(candidate, out uint process);
        if (candidate != IntPtr.Zero && process != Environment.ProcessId)
        {
            target = candidate; root = Native.GetAncestor(candidate, 2); windowBounds = Native.Bounds(root);
            var name = new StringBuilder(256); GetClassName(root, name, name.Capacity); browser = name.ToString().StartsWith("Chrome_WidgetWin", StringComparison.Ordinal);
            if (!browser) automation = new ScrollAutomation(root, new System.Windows.Point(point.X, point.Y));
        }
        escape = new HwndSource(new HwndSourceParameters("Snipsera scrolling escape") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        escape.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
        { if (msg == 0x0312 && wp.ToInt32() == 3901) { handled = true; escape?.Dispatcher.BeginInvoke(cancel); } return IntPtr.Zero; });
        if (!Native.RegisterHotKey(escape.Handle, 3901, 0x4000, 0x1B)) { escape.Dispose(); escape = null; }
    }
    public async Task<bool> ScrollAsync(CancellationToken token, bool useWheel)
    {
        if (!Available || TargetMoved) return false;
        if (!browser && !useWheel && automation is not null)
        {
            using var provider = CancellationTokenSource.CreateLinkedTokenSource(token); provider.CancelAfter(600);
            if (await automation.ScrollAsync(provider.Token)) return true;
        }
        if (token.IsCancellationRequested || TargetMoved) return false;
        return Wheel();
    }
    private bool Wheel()
    {
        // Stop if the user switches apps or holds modifiers; never turn Ctrl+wheel into a zoom operation.
        foreach (int key in new[] { 0x01, 0x02, 0x04, 0x10, 0x11, 0x12, 0x5B, 0x5C }) if (GetAsyncKeyState(key) < 0) return false;
        var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out uint process);
        if (foreground != root && (process != Environment.ProcessId || !Native.SetForegroundWindow(root))) return false;
        if (!Native.GetCursorPos(out var cursor)) return false;
        if (!area.Contains(cursor.X, cursor.Y))
        {
            var underPointer = WindowFromPoint(cursor); Native.GetWindowThreadProcessId(underPointer, out uint pointerProcess);
            // Move once from Snipsera's opt-in control. Later pointer movement out of the region pauses Auto.
            if (wheelActive || pointerProcess != Environment.ProcessId || !SetCursorPos(area.Left + area.Width / 2, area.Top + area.Height / 2)) return false;
            if (!Native.GetCursorPos(out cursor) || !area.Contains(cursor.X, cursor.Y)) return false;
        }
        if (Native.GetAncestor(WindowFromPoint(cursor), 2) != root) return false;
        wheelActive = true;
        var input = new WheelInput { Mouse = new MouseInput { Data = unchecked((uint)-40), Flags = 0x0800 } };
        return SendInput(1, [input], Marshal.SizeOf<WheelInput>()) == 1;
    }
    public void StopAutoScroll() { wheelActive = false; }
    public void Dispose() { automation?.Dispose(); if (escape is not null) { Native.UnregisterHotKey(escape.Handle, 3901); escape.Dispose(); escape = null; } }
}
