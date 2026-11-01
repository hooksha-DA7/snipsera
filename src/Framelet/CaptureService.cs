using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Buffers.Binary;

namespace Framelet;

public static class CaptureService
{
    public static BitmapSource Capture(Rectangle physicalBounds)
    {
        Rectangle bounds = CaptureGeometry.Clip(physicalBounds, Native.Desktop);
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            IntPtr screen = Native.GetDC(IntPtr.Zero);
            if (screen == IntPtr.Zero) throw new System.ComponentModel.Win32Exception();
            IntPtr target = graphics.GetHdc();
            try
            {
                if (!Native.BitBlt(target, 0, 0, bounds.Width, bounds.Height, screen, bounds.Left, bounds.Top, 0x40CC0020))
                    throw new System.ComponentModel.Win32Exception();
            }
            finally { graphics.ReleaseHdc(target); Native.ReleaseDC(IntPtr.Zero, screen); }
        }
        IntPtr handle = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally { Native.DeleteObject(handle); }
    }

    public static BitmapSource Crop(BitmapSource image, Rectangle bounds, Rectangle screen)
    {
        Rectangle clipped = CaptureGeometry.Clip(bounds, screen);
        var result = new CroppedBitmap(image, new Int32Rect(clipped.Left - screen.Left, clipped.Top - screen.Top, clipped.Width, clipped.Height));
        result.Freeze();
        return result;
    }

    public static void WritePng(BitmapSource image, Stream stream)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(stream);
    }

    public static BitmapSource Load(string path, int decodeWidth = 0)
    {
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[24];
        stream.ReadExactly(header);
        if (!header[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) throw new InvalidDataException("This capture is not a PNG image.");
        uint width = BinaryPrimitives.ReadUInt32BigEndian(header[16..20]);
        uint height = BinaryPrimitives.ReadUInt32BigEndian(header[20..24]);
        if (width == 0 || height == 0 || (ulong)width * height > 100_000_000) throw new InvalidDataException("This capture has unsupported dimensions.");
        stream.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public static async Task Copy(BitmapSource image)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { Clipboard.SetImage(image); return; }
            catch (COMException) when (attempt < 4) { await Task.Delay(70); }
        }
    }

    public static void Save(BitmapSource image, string path)
    {
        string full = Path.GetFullPath(path);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { WritePng(image, stream); stream.Flush(true); }
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
