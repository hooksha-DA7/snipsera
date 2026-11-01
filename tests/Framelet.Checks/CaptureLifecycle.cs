using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Framelet;

internal static class CaptureLifecycle
{
    public static int Run(string output, bool waitForInput = false)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); int failure = 0;
        var pixels = UiChecks.White();
        CaptureApp? activeApp = null;
        bool inspectHiddenCapture = false, hiddenCaptureExcludedWindows = false;
        var app = new CaptureApp(Path.Combine(output, "data"), _ =>
        {
            if (inspectHiddenCapture)
            {
                var floating = activeApp!.Windows.Cast<Window>().Where(w => w is ImageWindow or OverlayShelfWindow);
                hiddenCaptureExcludedWindows = !floating.Any(w => Native.IsWindowVisible(new WindowInteropHelper(w).Handle));
            }
            return pixels;
        });
        activeApp = app;
        app.Startup += async (_, _) =>
        {
            var doc = new EditDocument(pixels); doc.Add(new Mark(MarkKind.Line, [new(10, 10), new(100, 40)]));
            var editor = new EditorWindow(doc, _ => { }, () => { });
            try
            {
                if (waitForInput)
                {
                    app.MainWindow.Title = "Framelet lifecycle check - press F9 to run";
                    var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    KeyEventHandler start = (_, e) => { if (e.Key == Key.F9) { e.Handled = true; ready.TrySetResult(); } };
                    app.MainWindow.PreviewKeyDown += start;
                    try { await ready.Task.WaitAsync(TimeSpan.FromSeconds(90)); }
                    finally { app.MainWindow.PreviewKeyDown -= start; }
                }
                editor.Show();
                var overlayProfiles = await FloatingOverlayChecks.Run(app);
                var entry = app.History.Add(pixels, CaptureKind.Area); app.OpenEntry(entry); app.Pin(pixels, entry);
                var preview = app.Windows.Cast<Window>().OfType<ImageWindow>().Single(w => !w.IsPinned);
                var handle = new WindowInteropHelper(preview).Handle; var size = Native.Bounds(handle).Size;
                preview.RevealActions(true); await Task.Delay(140);
                var bar = (FrameworkElement)LogicalTreeHelper.FindLogicalNode(preview, "CaptureActionBar");
                var buttons = FloatingOverlayChecks.Descendants(bar).OfType<Button>().Where(b => b.IsVisible).ToArray();
                var pinButton = buttons.Single(b => AutomationProperties.GetAutomationId(b) == "PinCapture");
                if (buttons.Length != 6 || buttons.Any(b => b.ActualWidth < 28 || b.ActualHeight < 28)) throw new Exception("Normal capture overlay actions are missing or clipped");
                if (copyLabel(buttons, "CopyCapture") != "Copy" || copyLabel(buttons, "SaveCapture") != "Save") throw new Exception("Normal capture lost labeled Copy and Save actions");
                pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (!preview.IsPinned || new WindowInteropHelper(preview).Handle != handle || Native.Bounds(handle).Size != size) throw new Exception("Pin replaced or resized the preview");
                if (AutomationProperties.GetName(pinButton) != "Unpin") throw new Exception("Pin button did not become Unpin");
                pinButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (preview.IsPinned || !preview.IsVisible || Native.Bounds(handle).Size != size) throw new Exception("Unpin failed to keep the preview");
                if (app.Windows.Cast<Window>().OfType<ImageWindow>().Where(w => w.IsPinned).Any(w => Native.Bounds(new WindowInteropHelper(w).Handle).IntersectsWith(Native.Bounds(handle)))) throw new Exception("Unpinned preview overlaps the reflowed stack");
                preview.RevealActions(true); preview.Hide();
                if (buttons.Any(b => b.IsVisible)) throw new Exception("On-image actions survived hidden capture window");
                preview.Show();
                var first = app.CaptureAsync(CaptureKind.Screen); await app.CaptureAsync(CaptureKind.Screen); await first;
                await Until(() => app.History.Entries.Count == 3);
                if (!editor.IsVisible || !doc.IsDirty || doc.State.Marks.Length != 1) throw new Exception("Existing editor was lost or disabled");
                if (!app.Windows.Cast<Window>().OfType<ImageWindow>().Any(w => w.IsPinned && w.IsVisible)) throw new Exception("Pin not restored");
                var modal = new ConfirmWindow(editor, "Test confirmation", "Capture should wait for this dialog without hiding it.", "Continue");
                bool waited = false; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                timer.Tick += async (_, _) => { timer.Stop(); await app.CaptureAsync(CaptureKind.Screen); waited = app.History.Entries.Count == 3 && modal.IsVisible; modal.Close(); };
                timer.Start(); modal.ShowDialog(); await Until(() => app.History.Entries.Count == 4);
                if (!waited || !editor.IsEnabled || !editor.IsVisible) throw new Exception("Modal capture queue did not recover");
                var currentPreview = app.Windows.Cast<Window>().OfType<ImageWindow>().Single(w => !w.IsPinned);
                currentPreview.RevealActions(true);
                object Foreground()
                {
                    var foreground = Native.GetForegroundWindow();
                    Native.GetWindowThreadProcessId(foreground, out uint process);
                    return new { handle = foreground.ToInt64(), process, testProcess = Environment.ProcessId };
                }
                var foregroundBeforeTray = Foreground();
                app.ShowTrayMenu(); await Task.Delay(250);
                var tray = PresentationSource.CurrentSources.OfType<HwndSource>().Select(s => FindMenu(s.RootVisual)).FirstOrDefault(m => m?.IsOpen == true);
                File.WriteAllText(Path.Combine(output, "tray-focus.json"), JsonSerializer.Serialize(new
                {
                    before = foregroundBeforeTray, after = Foreground(),
                    open = tray?.IsOpen == true, keyboardFocus = tray?.IsKeyboardFocusWithin == true
                }, new JsonSerializerOptions { WriteIndented = true }));
                if (tray is null || !tray.IsKeyboardFocusWithin) throw new Exception("Tray menu did not remain open with keyboard focus");
                var traySource = (HwndSource)PresentationSource.FromVisual(tray);
                CaptureService.Save(CaptureService.Capture(Native.Bounds(traySource.Handle)), Path.Combine(output, "tray-native.png"));
                ((MenuItem)tray.Items[5]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(80);
                if (tray.IsOpen || app.MainWindow?.IsVisible != true) throw new Exception("Tray history command did not dismiss the menu and show history");
                app.Pin(pixels, entry); await Task.Delay(80);
                var retainedPins = app.Windows.Cast<Window>().OfType<ImageWindow>().Where(w => w.IsPinned).Select(w =>
                    (Window: w, Handle: new WindowInteropHelper(w).Handle, Bounds: Native.Bounds(new WindowInteropHelper(w).Handle))).ToArray();
                if (retainedPins.Length != 2) throw new Exception("Expected two native pins for hidden-group integration");
                app.HideOverlays(currentPreview);
                await Task.Delay(240);
                var shelf = app.Windows.Cast<Window>().OfType<OverlayShelfWindow>().Single();
                if (!app.OverlaysHidden || app.HiddenOverlayCount != 3 || !shelf.IsVisible ||
                    app.Windows.Cast<Window>().OfType<ImageWindow>().Any(w => Native.IsWindowVisible(new WindowInteropHelper(w).Handle)))
                    throw new Exception("Hidden group left a floating native window visible or lost its restore shelf");
                if (!editor.IsVisible || !doc.IsDirty) throw new Exception("Temporarily hiding overlays affected the editor");
                inspectHiddenCapture = true;
                await app.CaptureAsync(CaptureKind.Screen);
                inspectHiddenCapture = false;
                if (!hiddenCaptureExcludedWindows || !app.OverlaysHidden || app.History.Entries.Count != 5)
                    throw new Exception("Hidden-group capture failed to hide native overlays before the capture callback");
                var hiddenPreview = app.Windows.Cast<Window>().OfType<ImageWindow>().Single(w => !w.IsPinned);
                if (hiddenPreview.IsVisible || app.HiddenOverlayCount != 3 || !shelf.IsVisible)
                    throw new Exception("A new capture escaped the hidden group or lost restoration");
                app.RestoreOverlays(); await Task.Delay(240);
                if (app.OverlaysHidden || shelf.IsVisible || !hiddenPreview.IsVisible)
                    throw new Exception("Restoring the native group did not restore the new preview");
                foreach (var pin in retainedPins)
                    if (!pin.Window.IsVisible || !pin.Window.IsPinned || new WindowInteropHelper(pin.Window).Handle != pin.Handle || Native.Bounds(pin.Handle) != pin.Bounds)
                        throw new Exception("Hide/restore changed a pin's native identity or geometry");
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "passed", overlayProfiles, capturesAfterRapidRequests = 3, capturesAfterModal = 4, capturesAfterHiddenGroup = 5, originalPreviewOpenAtStart = true, dirtyEditorPreserved = true, pinPreserved = true, pinUnpinPreservedWindowAndSize = true, sixOnImageActionsUnclipped = true, labeledCopySave = true, overlayHidWithWindow = true, trayRemainedOpenAndHistoryCommandWorked = true, nativeHiddenGroupAndShelf = true, hiddenCaptureExcludedWindows, newCaptureStayedHidden = true, restoredTwoPinsWithSameHandlesAndBounds = true, captureBackend = "synthetic pixels; native window visibility checked at the capture callback; native capture pixels are separately tested" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("PASS Repeated capture lifecycle preserves preview workflow, dirty editor and pin; modal queue resumes");
            }
            catch (Exception ex) { failure = 1; File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); Console.WriteLine("FAIL Capture lifecycle: " + ex.Message); }
            finally { doc.MarkSaved(); app.Quit(); }
        };
        app.Run(); return failure;
    }
    private static async Task Until(Func<bool> ready)
    { var clock = Stopwatch.StartNew(); while (!ready()) { if (clock.ElapsedMilliseconds > 5000) throw new TimeoutException("Queued capture did not complete"); await Task.Delay(30); } }
    private static string? copyLabel(Button[] buttons, string id) => buttons.Single(b => AutomationProperties.GetAutomationId(b) == id).Content as string;
    private static ContextMenu? FindMenu(DependencyObject? node)
    {
        if (node is ContextMenu menu) return menu;
        if (node is null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (FindMenu(VisualTreeHelper.GetChild(node, i)) is ContextMenu found) return found;
        return null;
    }
}
