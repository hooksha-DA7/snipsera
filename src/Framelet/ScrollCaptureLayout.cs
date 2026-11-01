using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using LogicalSize = System.Windows.Size;
using PixelRect = System.Drawing.Rectangle;
using PixelSize = System.Drawing.Size;

namespace Framelet;

public enum ScrollGutter { Left, Right }
public readonly record struct ScrollBarPlacement(PixelRect Body, PixelRect Outer, bool Compact, string Edge);
public readonly record struct ScrollPreviewSlot(PixelRect Outer, ScrollGutter Side);

// Scrolling-capture geometry in physical desktop pixels. The alpha-zero hole is the selection plus a 1 px guard; the ring sits
// outside the hole; every "outer" rectangle (a panel plus its shadow) stays inside one work area and clear of the ring by Clearance.
// Nothing returned here may intersect the selection. See design/scrolling-2026-11-01/DESIGN.md.
public static class ScrollCaptureLayout
{
    // Logical (96 dpi) metrics, scaled by the selection monitor's DPI.
    public const double Pad = 14, Clearance = 4, PreviewMaxWidth = 160, PreviewMinWidth = 64, PreviewMinHeight = 120, PreviewChromeX = 10, PreviewChromeY = 28;
    // Conservative bar sizes for a pre-check before the overlay exists; the window measures its real bar.
    public static readonly LogicalSize NominalWide = new(640, 46), NominalCompact = new(350, 84);
    public const string NoRoom = "There's no room for the scrolling controls outside this selection. Select an area that leaves space above, below or beside it.";

    public static int Px(double logical, double scale) => (int)Math.Ceiling(logical * scale - 1e-6);
    public static int RingWidth(double scale) => Math.Max(2, (int)Math.Round(2 * scale));
    public static PixelRect Hole(PixelRect bounds) => PixelRect.Inflate(bounds, 1, 1);
    public static PixelRect Frame(PixelRect bounds, double scale) { int ring = RingWidth(scale); return PixelRect.Inflate(Hole(bounds), ring, ring); }
    public static PixelRect KeepOut(PixelRect bounds, double scale) { int gap = Px(Clearance, scale); return PixelRect.Inflate(Frame(bounds, scale), gap, gap); }
    public static IReadOnlyList<PixelRect> Ring(PixelRect bounds, double scale)
    {
        PixelRect hole = Hole(bounds), frame = Frame(bounds, scale);
        return [PixelRect.FromLTRB(frame.Left, frame.Top, frame.Right, hole.Top), PixelRect.FromLTRB(frame.Left, hole.Bottom, frame.Right, frame.Bottom),
                PixelRect.FromLTRB(frame.Left, hole.Top, hole.Left, hole.Bottom), PixelRect.FromLTRB(hole.Right, hole.Top, frame.Right, hole.Bottom)];
    }

    // Below, then above (CleanShot order), then the side gutters. Wide bars first above/below; compact first beside.
    private static readonly (string Edge, bool Compact)[] Candidates =
        [("Below", false), ("Below", true), ("Above", false), ("Above", true), ("Right", true), ("Right", false), ("Left", true), ("Left", false)];
    public static ScrollBarPlacement? PlaceBar(PixelRect bounds, IReadOnlyList<PixelRect> work, LogicalSize wide, LogicalSize compact, double scale)
    {
        if (!Valid(bounds, scale)) return null;
        int pad = Px(Pad, scale), cx = bounds.Left + bounds.Width / 2; var keep = KeepOut(bounds, scale);
        var areas = work.Where(a => a.Width > 0 && a.Height > 0).OrderBy(a => Distance(a, bounds)).ThenByDescending(a => Overlap(a, bounds)).ToArray();
        foreach (var (edge, small) in Candidates)
        {
            var size = small ? compact : wide;
            int w = Px(size.Width, scale) + 2 * pad, h = Px(size.Height, scale) + 2 * pad;
            foreach (var area in areas)
            {
                if (w > area.Width || h > area.Height) continue;
                int x = edge switch { "Right" => keep.Right, "Left" => keep.Left - w, _ => Math.Clamp(cx - w / 2, area.Left, area.Right - w) };
                // Beside the selection the bar's body bottom lines up with the selection bottom.
                int y = edge switch { "Below" => keep.Bottom, "Above" => keep.Top - h, _ => Math.Clamp(bounds.Bottom + pad - h, area.Top, area.Bottom - h) };
                var outer = new PixelRect(x, y, w, h);
                if (area.Contains(outer) && !outer.IntersectsWith(keep)) return new ScrollBarPlacement(PixelRect.Inflate(outer, -pad, -pad), outer, small, edge);
            }
        }
        return null;
    }

    // The preview grows upward from the selection's bottom edge, in the left gutter when it fits, else the right.
    public static ScrollPreviewSlot? PreviewSlot(PixelRect bounds, IReadOnlyList<PixelRect> work, PixelRect? bar, double scale)
    {
        if (!Valid(bounds, scale)) return null;
        int pad = Px(Pad, scale), maxW = Px(PreviewMaxWidth, scale) + 2 * pad, minW = Px(PreviewMinWidth, scale) + 2 * pad, minH = Px(PreviewMinHeight, scale) + 2 * pad;
        var keep = KeepOut(bounds, scale);
        foreach (var side in new[] { ScrollGutter.Left, ScrollGutter.Right })
        {
            int edge = side == ScrollGutter.Left ? keep.Left : keep.Right, probe = side == ScrollGutter.Left ? edge - 1 : edge;
            var area = work.FirstOrDefault(a => a.Contains(probe, bounds.Bottom - 1));
            if (area.IsEmpty) area = work.FirstOrDefault(a => a.Contains(probe, bounds.Top + bounds.Height / 2));
            if (area.IsEmpty) continue;
            int left = side == ScrollGutter.Left ? Math.Max(area.Left, edge - maxW) : edge, right = side == ScrollGutter.Left ? edge : Math.Min(area.Right, edge + maxW);
            int top = area.Top, bottom = Math.Min(area.Bottom, bounds.Bottom + pad);
            if (bar is PixelRect b && b.IntersectsWith(PixelRect.FromLTRB(left, top, right, bottom)))
            {
                if (b.Top - top >= minH) bottom = Math.Min(bottom, b.Top);
                else if (bottom - b.Bottom >= minH) top = Math.Max(top, b.Bottom);
                else continue;
            }
            var outer = PixelRect.FromLTRB(left, top, right, bottom);
            if (outer.Width < minW || outer.Height < minH || !area.Contains(outer) || outer.IntersectsWith(keep) || bar is PixelRect c && outer.IntersectsWith(c)) continue;
            return new ScrollPreviewSlot(outer, side);
        }
        return null;
    }

    // Card body for the current image: never upscaled, anchored to the selection side and bottom of the slot.
    public static PixelRect FitPreview(ScrollPreviewSlot slot, PixelSize image, double scale)
    {
        if (image.Width <= 0 || image.Height <= 0 || !(scale > 0) || !double.IsFinite(scale)) return PixelRect.Empty;
        int pad = Px(Pad, scale); var body = PixelRect.Inflate(slot.Outer, -pad, -pad);
        double chromeX = PreviewChromeX * scale, chromeY = PreviewChromeY * scale;
        double k = Math.Min(1, Math.Min((body.Width - chromeX) / image.Width, (body.Height - chromeY) / image.Height));
        if (!(k > 0)) return PixelRect.Empty;
        int width = Math.Min(body.Width, Math.Max(Math.Min(body.Width, Px(PreviewMinWidth, scale)), (int)Math.Ceiling(image.Width * k + chromeX)));
        int height = Math.Min(body.Height, (int)Math.Ceiling(image.Height * k + chromeY));
        int x = slot.Side == ScrollGutter.Left ? body.Right - width : body.Left;
        return new PixelRect(x, body.Bottom - height, width, height);
    }

    // Pre-check for CaptureApp after selection: null when the controls can be placed, otherwise a user-facing reason.
    public static string? Check(PixelRect bounds)
    {
        double scale = ScaleFor(bounds);
        return PlaceBar(bounds, WorkAreas(), NominalWide, NominalCompact, scale) is null ? NoRoom : null;
    }

    public static IReadOnlyList<PixelRect> WorkAreas()
    {
        var areas = new List<PixelRect>();
        MonitorEnum callback = (IntPtr monitor, IntPtr dc, ref Native.Rect rect, IntPtr data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            if (Native.GetMonitorInfo(monitor, ref info)) areas.Add(info.Work.Rectangle);
            return true;
        };
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        return areas;
    }

    // Effective DPI of the monitor holding most of the selection; the floating controls are sized for it.
    public static double ScaleFor(PixelRect bounds)
    {
        var rect = new Native.Rect { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
        IntPtr monitor = MonitorFromRect(ref rect, 2);
        return monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpi, out _) == 0 && dpi > 0 ? dpi / 96d : 1;
    }

    private static bool Valid(PixelRect bounds, double scale) => bounds.Width > 0 && bounds.Height > 0 && scale > 0 && double.IsFinite(scale);
    private static long Distance(PixelRect a, PixelRect b)
    {
        long dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right)), dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return dx * dx + dy * dy;
    }
    private static long Overlap(PixelRect a, PixelRect b) { var i = PixelRect.Intersect(a, b); return (long)i.Width * i.Height; }

    private delegate bool MonitorEnum(IntPtr monitor, IntPtr dc, ref Native.Rect rect, IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr clip, MonitorEnum callback, IntPtr data);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Native.Rect rect, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
