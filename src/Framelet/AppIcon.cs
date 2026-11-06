using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace Framelet;

public static class AppIcon
{
    private static readonly int[] TraySizes = [16, 20, 24, 32, 40, 48, 64];
    private static readonly Lazy<BitmapFrame> WindowFrame = new(() =>
    {
        using var stream = Open("App");
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze(); return frame;
    });
    public static BitmapFrame WindowImage => WindowFrame.Value;

    // The notification area has its own pixel-sized artwork resource.
    public static Icon CreateTray(uint dpi = 96) => Load("Tray", TrayPixels(dpi));
    internal static int TrayPixels(uint dpi)
    {
        double desired = 16 * (dpi == 0 ? 96 : dpi) / 96d;
        foreach (int size in TraySizes) if (size >= desired) return size;
        return TraySizes[^1];
    }
    internal static uint CurrentTrayDpi()
    {
        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        uint dpi = taskbar == IntPtr.Zero ? 0 : Native.GetDpiForWindow(taskbar);
        return dpi > 0 ? dpi : 96;
    }
    private static Icon Load(string name, int size)
    {
        if (size is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(size));
        using var stream = Open(name);
        using var icon = new Icon(stream, size, size);
        return (Icon)icon.Clone();
    }
    internal static Stream Open(string name) => typeof(AppIcon).Assembly.GetManifestResourceStream("Snipsera.Icon." + name)
        ?? throw new InvalidOperationException("The application icon resource is missing.");
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string? title);
}
