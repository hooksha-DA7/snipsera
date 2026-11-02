using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Framelet;

public static class RecordingPrivacy
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    public static bool Exclude(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        return handle != IntPtr.Zero && SetWindowDisplayAffinity(handle, 0x11);
    }
}
