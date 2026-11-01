using System;
using System.Collections.Generic;
using System.Linq;
using PixelRect = System.Drawing.Rectangle;

namespace Framelet;

// Physical-pixel window rectangle for the restore tab, kept pure so it can be checked without a desktop.
// Window = tab body (expanded height) + shadow pad on the left, right and top. The bottom edge sits flush on the work-area
// bottom, i.e. directly above the taskbar, never on it or on another monitor. See design/temporary-hide-2026-11-01/DESIGN.md.
public readonly record struct OverlayShelfPlacement(PixelRect Window, PixelRect Body, PixelRect Resting, PixelRect WorkArea, double Scale);

public static class OverlayShelfLayout
{
    // Logical (96 dpi) metrics. Left matches the preview card's image edge (FloatingLayout margin 12 + window shadow margin 8).
    public const double Width = 112, MinWidth = 48, RestingHeight = 22, ExpandedHeight = 68, Pad = 12, Left = 20, Radius = 8;

    public static OverlayShelfPlacement? Place(PixelRect saved, IReadOnlyList<PixelRect> current, double scale)
    {
        if (!(scale > 0) || !double.IsFinite(scale)) scale = 1;
        var work = Choose(saved, current);
        if (work.Width <= 0 || work.Height <= 0) return null;
        int pad = Px(Pad, scale), width = Px(Width, scale), height = Px(ExpandedHeight, scale), resting = Px(RestingHeight, scale), narrowest = Px(MinWidth, scale);
        // The shadow pad plus a usable resting tab must fit; otherwise there is no safe tab (CaptureApp keeps tray/Home/menu routes).
        if (work.Width < 2 * pad + narrowest || work.Height < pad + resting) return null;
        // Small work areas: shrink the body (never below the minimum) rather than spill outside.
        width = Math.Min(width, work.Width - 2 * pad); height = Math.Min(height, work.Height - pad);
        int left = Math.Clamp(work.Left + Px(Left, scale), work.Left + pad, Math.Max(work.Left + pad, work.Right - pad - width));
        var body = new PixelRect(left, work.Bottom - height, width, height);
        var window = PixelRect.FromLTRB(body.Left - pad, body.Top - pad, body.Right + pad, body.Bottom);
        return new OverlayShelfPlacement(window, body, new PixelRect(body.Left, body.Bottom - resting, width, resting), work, scale);
    }

    // The saved work area if it still exists; otherwise the current work area that overlaps it most, else the nearest, else the first.
    public static PixelRect Choose(PixelRect saved, IReadOnlyList<PixelRect> current)
    {
        var areas = current.Where(a => a.Width > 0 && a.Height > 0).ToArray();
        if (areas.Length == 0) return saved;
        if (areas.Contains(saved)) return saved;
        var overlapping = areas.OrderByDescending(a => { var i = PixelRect.Intersect(a, saved); return (long)i.Width * i.Height; }).First();
        if (overlapping.IntersectsWith(saved)) return overlapping;
        if (saved.IsEmpty) return areas[0];
        // Distance from the saved bottom-left corner, where the tab lived, to each area.
        int px = saved.Left, py = saved.Bottom - 1;
        long Distance(PixelRect a) { long dx = Math.Max(0, Math.Max(a.Left - px, px - (a.Right - 1))), dy = Math.Max(0, Math.Max(a.Top - py, py - (a.Bottom - 1))); return dx * dx + dy * dy; }
        return areas.OrderBy(Distance).First();
    }

    public static int Px(double logical, double scale) => (int)Math.Ceiling(logical * scale - 1e-6);
}
