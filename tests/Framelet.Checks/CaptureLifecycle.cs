using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
                var entry = app.History.Add(pixels, CaptureKind.Area); app.OpenEntry(entry); app.Pin(pixels, entry);
                var first = app.CaptureAsync(CaptureKind.Screen); await app.CaptureAsync(CaptureKind.Screen); await first;
                await Until(() => app.History.Entries.Count == 3);
                if (!editor.IsVisible || !doc.IsDirty || doc.State.Marks.Length != 1) throw new Exception("Existing editor was lost or disabled");
                if (!app.Windows.Cast<Window>().OfType<ImageWindow>().Any(w => w.IsPinned && w.IsVisible)) throw new Exception("Pin not restored");
                var modal = new ConfirmWindow(editor, "Test confirmation", "Capture should wait for this dialog without hiding it.", "Continue");
                bool waited = false; var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                timer.Tick += async (_, _) => { timer.Stop(); await app.CaptureAsync(CaptureKind.Screen); waited = app.History.Entries.Count == 3 && modal.IsVisible; modal.Close(); };
                timer.Start(); modal.ShowDialog(); await Until(() => app.History.Entries.Count == 4);
                if (!waited || !editor.IsEnabled || !editor.IsVisible) throw new Exception("Modal capture queue did not recover");
                app.ShowTrayMenu(); await Task.Delay(250);
                var tray = PresentationSource.CurrentSources.OfType<HwndSource>().Select(s => FindMenu(s.RootVisual)).FirstOrDefault(m => m?.IsOpen == true);
                if (tray is null) throw new Exception("Tray menu did not remain open");
                var traySource = (HwndSource)PresentationSource.FromVisual(tray);
                CaptureService.Save(CaptureService.Capture(Native.Bounds(traySource.Handle)), Path.Combine(output, "tray-native.png"));
                ((MenuItem)tray.Items[5]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); await Task.Delay(80);
                if (tray.IsOpen || app.MainWindow?.IsVisible != true) throw new Exception("Tray history command did not dismiss the menu and show history");
                File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "passed", capturesAfterRapidRequests = 3, capturesAfterModal = 4, originalPreviewOpenAtStart = true, dirtyEditorPreserved = true, pinPreserved = true, trayRemainedOpenAndHistoryCommandWorked = true, captureBackend = "synthetic pixels; native capture pixels are separately tested" }, new JsonSerializerOptions { WriteIndented = true }));
                Console.WriteLine("PASS Repeated capture lifecycle preserves preview workflow, dirty editor and pin; modal queue resumes");
            }
            catch (Exception ex) { failure = 1; File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { status = "failed", error = ex.ToString() })); Console.WriteLine("FAIL Capture lifecycle: " + ex.Message); }
            finally { doc.MarkSaved(); app.Quit(); }
        };
        app.Run(); return failure;
    }
    private static async Task Until(Func<bool> ready)
    { var clock = Stopwatch.StartNew(); while (!ready()) { if (clock.ElapsedMilliseconds > 5000) throw new TimeoutException("Queued capture did not complete"); await Task.Delay(30); } }
    private static ContextMenu? FindMenu(DependencyObject? node)
    {
        if (node is ContextMenu menu) return menu;
        if (node is null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) if (FindMenu(VisualTreeHelper.GetChild(node, i)) is ContextMenu found) return found;
        return null;
    }
}
