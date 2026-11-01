using System;
using Framelet;
using Rect = System.Drawing.Rectangle;

internal static class OverlayShelfChecks
{
    private static void Require(bool value, string reason = "Restore shelf layout assertion failed") { if (!value) throw new Exception(reason); }
    internal static void Run(Action<string, Action> test)
    {
        test("Restore shelf stays above taskbars and inside positive and negative-origin monitors across scales", () =>
        {
            Rect[] areas = [new(0, 0, 2560, 1380), new(-1920, 0, 1920, 1040), new(64, 0, 1856, 1080), new(0, 48, 1920, 1032), new(-1600, -1000, 1600, 950)];
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 }) foreach (var area in areas)
            {
                var result = OverlayShelfLayout.Place(area, areas, scale);
                Require(result is not null); var placed = result!.Value;
                Require(placed.WorkArea == area && area.Contains(placed.Window), "Window escapes at " + scale + " " + area);
                Require(placed.Window.Contains(placed.Body) && placed.Body.Contains(placed.Resting));
                Require(placed.Body.Bottom == area.Bottom && placed.Resting.Bottom == area.Bottom && placed.Window.Bottom == area.Bottom);
                Require(placed.Resting.Height <= (int)Math.Ceiling(OverlayShelfLayout.RestingHeight * scale) && placed.Body.Width <= (int)Math.Ceiling(OverlayShelfLayout.Width * scale));
            }
        });
        test("Restore shelf safely chooses an available work area after monitor or taskbar changes", () =>
        {
            var old = new Rect(-1920, 0, 1920, 1040); var updated = new Rect(-1920, 48, 1920, 1032); var primary = new Rect(0, 0, 2560, 1380);
            Require(OverlayShelfLayout.Choose(old, [updated, primary]) == updated);
            Require(OverlayShelfLayout.Choose(old, [primary]) == primary);
            Require(OverlayShelfLayout.Choose(Rect.Empty, [primary, updated]) == primary);
            Require(OverlayShelfLayout.Place(old, [primary], 1.25) is { } placed && primary.Contains(placed.Window));
        });
        test("Restore shelf never spills out of tiny work areas and accepts invalid-scale fallback", () =>
        {
            foreach (int size in new[] { 1, 10, 20, 32, 64, 160 }) foreach (double scale in new[] { 1d, 1.25, 2, double.NaN, 0, -1 })
            {
                var area = new Rect(-50, -70, size, size); var result = OverlayShelfLayout.Place(area, [area], scale);
                Require(result is null || area.Contains(result.Value.Window), "Tiny work area overflow: " + size + " at " + scale);
            }
            Require(OverlayShelfLayout.Place(Rect.Empty, [], 1) is null);
        });
    }
}
