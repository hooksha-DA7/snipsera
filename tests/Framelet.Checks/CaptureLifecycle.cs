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
using System.Windows.Media;
using System.Windows.Threading;
using Framelet;

internal static class CaptureLifecycle
{
    public static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output); int failure = 0;
        var pixels = UiChecks.White();
        var app = new CaptureApp(Path.Combine(output, "data"), _ => pixels);
        app.Startup += async (_, _) =>
        {
            var doc = new EditDocument(pixels); doc.Add(new Mark(MarkKind.Line, [new(10, 10), new(100, 40)]));
            var editor = new EditorWindow(doc, _ => { }, () => { }); editor.Show();
            try
            {
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
                app.ShowTrayMenu(); await Task.Delay(250);
                var tray = PresentationSource.CurrentSources.OfType<HwndSource>().Select(s => FindMenu(s.RootVisual)).FirstOrDefault(m => m?.IsOpen == true);
                if (tray is null || !tray.IsKeyboardFocusWithin) throw new Exception("Tray menu did not remain open with keyboard focus");
                var traySource = (HwndSource)PresentationSource.FromVisual(tray);
                CaptureService.Save(CaptureService.Capture(Native.Bounds(traySource.Handle)), Path.Combine(output, "tray-native.png"));
                ((MenuItem)tray.Items[5]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(80);
                if (tray.IsOpen || app.MainWindow?.IsVisible != true) throw new Exception("Tray history command did not dismiss the menu and show history");
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "passed", overlayProfiles, capturesAfterRapidRequests = 3, capturesAfterModal = 4, originalPreviewOpenAtStart = true, dirtyEditorPreserved = true, pinPreserved = true, pinUnpinPreservedWindowAndSize = true, sixOnImageActionsUnclipped = true, labeledCopySave = true, overlayHidWithWindow = true, trayRemainedOpenAndHistoryCommandWorked = true, captureBackend = "synthetic pixels; native capture pixels are separately tested" }, new JsonSerializerOptions { WriteIndented = true }));
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
