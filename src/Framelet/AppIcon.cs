using System.Drawing;
using System.Drawing.Drawing2D;

namespace Framelet;

public static class AppIcon
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.FromArgb(32, 95, 219));
        using var pen = new Pen(Color.White, 2.8f);
        graphics.DrawLines(pen, [new Point(7, 13), new Point(7, 7), new Point(13, 7)]);
        graphics.DrawLines(pen, [new Point(19, 7), new Point(25, 7), new Point(25, 13)]);
        graphics.DrawLines(pen, [new Point(25, 19), new Point(25, 25), new Point(19, 25)]);
        graphics.DrawLines(pen, [new Point(13, 25), new Point(7, 25), new Point(7, 19)]);
        var handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }
}
