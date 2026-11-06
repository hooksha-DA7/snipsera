using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Framelet;

// Windows 11 transient Acrylic, not a blurred screenshot. The empty, non-layered HWND supplies only the backdrop; the
// layered WPF host above it supplies all content. Neither foreground pixels nor user input pass through this surface.
internal sealed class AcrylicPlate : IDisposable
{
    private readonly HwndSource source;
    private readonly IntPtr host;
    private Rect previous = Rect.Empty;
    private bool visible;
    internal AcrylicPlate(IntPtr host)
    {
        if (host == IntPtr.Zero) throw new InvalidOperationException("The toolbar is not ready.");
        this.host = host;
        source = new HwndSource(new HwndSourceParameters("")
        {
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP, initially hidden
            ExtendedWindowStyle = 0x08000000 | 0x00000080 | 0x00000020, // NOACTIVATE | TOOLWINDOW | TRANSPARENT
            Width = 1, Height = 1, UsesPerPixelOpacity = false
        });
        try
        {
            source.AddHook(Hook); source.RootVisual = new DrawingVisual();
            source.CompositionTarget.BackgroundColor = Colors.Transparent;
            var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
            Marshal.ThrowExceptionForHR(DwmExtendFrameIntoClientArea(source.Handle, ref margins));
            SetAttribute(33, 2); // DWMWCP_ROUND
            SetAttribute(34, unchecked((int)0xFFFFFFFE)); // DWMWA_COLOR_NONE: the WPF panel owns its border
            SetAttribute(38, 3); // DWMSBT_TRANSIENTWINDOW (Desktop Acrylic), build 22621+
            // Fail closed: a helper must never appear inside a user's screenshot or recording.
            if (!SetWindowDisplayAffinity(source.Handle, 0x11)) throw new InvalidOperationException("Windows could not exclude the backdrop from capture.");
        }
        catch { source.Dispose(); throw; }
    }

    private static IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x84) { handled = true; return new IntPtr(-1); } // HTTRANSPARENT
        if (message == 0x21) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE
        return IntPtr.Zero;
    }
    private void SetAttribute(int attribute, int value) => Marshal.ThrowExceptionForHR(DwmSetWindowAttribute(source.Handle, attribute, ref value, sizeof(int)));
    internal void Theme(bool dark) => SetAttribute(20, dark ? 1 : 0);
    internal void Place(Point a, Point b, bool force = false)
    {
        int x = (int)Math.Round(a.X), y = (int)Math.Round(a.Y), w = Math.Max(1, (int)Math.Round(b.X) - x), h = Math.Max(1, (int)Math.Round(b.Y) - y);
        var bounds = new Rect(x, y, w, h);
        if (bounds == previous && visible && !force) return;
        // HWND ownership would force the plate above its host. Insert it directly below instead; never activate it.
        if (!Native.SetWindowPos(source.Handle, host, x, y, w, h, 0x0010 | 0x0040)) throw new InvalidOperationException("The backdrop could not be positioned.");
        previous = bounds; visible = true;
    }
    internal void Hide() { ShowWindow(source.Handle, 0); visible = false; }
    public void Dispose() { if (!source.IsDisposed) { Hide(); source.Dispose(); } }
    [StructLayout(LayoutKind.Sequential)] private struct Margins { internal int Left, Right, Top, Bottom; }
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int command);
}
