using System;
using Framelet;

internal static class OverlayHideGestureChecks
{
    private static void Require(bool value) { if (!value) throw new Exception("Temporary hide wheel gesture assertion failed"); }
    internal static void Run(Action<string, Action> test)
    {
        test("Hide gesture requires a complete downward notch and accumulates precision-wheel deltas", () =>
        {
            var gesture = new OverlayHideGesture(); Require(gesture.Push(-120, 1000, true));
            Require(!gesture.Push(-30, 2000, true) && !gesture.Push(-30, 2100, true) && !gesture.Push(-30, 2200, true) && gesture.Push(-30, 2300, true));
            Require(!gesture.Push(-30, 2350, true));
        });
        test("Hide gesture resets on upward or blocked modifier/control/drag input", () =>
        {
            var gesture = new OverlayHideGesture(); Require(!gesture.Push(-60, 1000, true)); Require(!gesture.Push(120, 1100, true)); Require(!gesture.Push(-60, 1200, true));
            Require(!gesture.Push(-120, 1250, false)); Require(!gesture.Push(-60, 1300, true));
        });
        test("Hide gesture expires idle partial gestures and handles a full notch after the timeout", () =>
        {
            var gesture = new OverlayHideGesture(); Require(!gesture.Push(-60, 1000, true)); Require(!gesture.Push(-60, 1500, true)); Require(gesture.Push(-60, 1550, true));
            Require(gesture.Push(-120, 3000, true));
        });
        test("Opacity adjustments clear a partial hide gesture before plain scrolling resumes", () =>
        {
            var gesture = new OverlayHideGesture(); Require(!gesture.Push(-60, 1000, true)); gesture.Reset();
            Require(!gesture.Push(-60, 1100, true)); Require(gesture.Push(-60, 1200, true));
        });
        test("Restore hover cannot undo hiding when the tab appears under the pointer", () =>
        {
            var hover = new OverlayRestoreHover(); hover.Reset(true, true);
            Require(!hover.CanRestore(true, false, true)); hover.Observe(true, true); Require(!hover.Armed);
            hover.Observe(true, false); Require(hover.CanRestore(true, false, true));
        });
        test("Restore hover arms immediately when the pointer starts outside the tab", () =>
        {
            var hover = new OverlayRestoreHover(); hover.Reset(true, false); Require(hover.CanRestore(true, false, true));
        });
        test("Restore hover never treats an unreadable pointer as having left the tab", () =>
        {
            var hover = new OverlayRestoreHover(); hover.Reset(false, false); hover.Observe(false, false);
            Require(!hover.Armed && !hover.CanRestore(true, false, true));
        });
        test("Restore hover blocks button-held, pointer-exit and capture-suspended restoration", () =>
        {
            var hover = new OverlayRestoreHover(); hover.Reset(true, false);
            Require(!hover.CanRestore(true, true, true) && !hover.CanRestore(false, false, true) && !hover.CanRestore(true, false, false));
            hover.Reset(); Require(!hover.CanRestore(true, false, true));
        });
    }
}
