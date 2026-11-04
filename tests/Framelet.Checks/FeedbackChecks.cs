using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet;

// Isolated file and WPF control checks: no visible windows, global shortcuts or desktop input.
internal static class FeedbackChecks
{
    private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    private static void Require(bool condition, string message = "Feedback assertion failed") { if (!condition) throw new Exception(message); }
    private static BitmapSource Image()
    {
        byte[] pixels = Enumerable.Repeat((byte)255, 160 * 120 * 4).ToArray();
        var image = BitmapSource.Create(160, 120, 96, 96, PixelFormats.Bgra32, null, pixels, 160 * 4); image.Freeze(); return image;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var bitmap = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        byte[] data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0); return data;
    }
    private static Mark Arrow() => new(MarkKind.Arrow, [new(10, 20), new(120, 80)], "#EB4D3D", 4);
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
    }
    public static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(app);
        int failures = 0; var outcomes = new List<object>();
        void Test(string name, Action check)
        {
            try { check(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        HistoryStore Store() => new(Path.Combine(output, Guid.NewGuid().ToString("N")));
        Test("Print Screen aliases map to the same native shortcut", () =>
        {
            foreach (string alias in new[] { "PrintScreen", "PrintScrn", "PrtSc", "PrtScn", "Snapshot" })
                Require(Hotkey.Parse(alias) == new Hotkey(0, 0x2C));
            Require(Hotkey.Parse("Ctrl+Shift+PrintScreen") == new Hotkey(6, 0x2C));
        });
        Test("Keyboard chords format canonically without accepting modifier-only input", () =>
        {
            Require(Hotkey.FromKey(Key.PrintScreen, ModifierKeys.None) == "PrintScreen");
            Require(Hotkey.FromKey(Key.D5, ModifierKeys.Control | ModifierKeys.Alt) == "Ctrl+Alt+5");
            Require(Hotkey.FromKey(Key.LeftCtrl, ModifierKeys.Control) is null);
            Require(Hotkey.FromKey(Key.A, ModifierKeys.None) is null);
            Require(Hotkey.FromKey(Key.F12, ModifierKeys.Control) is null);
            Require(Hotkey.FromKey(Key.PrintScreen, ModifierKeys.Windows) is null);
        });
        Test("Print Screen shortcut settings survive restart", () =>
        {
            var store = Store(); var settings = new AppSettings(Area: "PrintScreen"); store.SaveSettings(settings);
            Require(new HistoryStore(store.Root).LoadSettings() == settings);
        });
        Test("Shortcut recorder reads pressed chords and preserves Escape and Tab navigation", () =>
        {
            var recorder = new ShortcutRecorder("Area", "Ctrl+Alt+1", () => new Scope(() => { }), _ => { });
            recorder.Press(Key.LeftCtrl, ModifierKeys.Control, false);
            Require(recorder.Shortcut == "Ctrl+Alt+1" && recorder.Text.Contains("Ctrl+"));
            recorder.Press(Key.D5, ModifierKeys.Control | ModifierKeys.Alt, false);
            Require(recorder.Shortcut == "Ctrl+Alt+5" && recorder.IsPending);
            recorder.Press(Key.A, ModifierKeys.None, false); Require(recorder.Shortcut == "Ctrl+Alt+5");
            Require(!recorder.Press(Key.Tab, ModifierKeys.None, false) && !recorder.Press(Key.Tab, ModifierKeys.Shift, false));
            recorder.Press(Key.Escape, ModifierKeys.None, false); Require(recorder.Shortcut == recorder.Saved && !recorder.IsPending);
            Require(recorder.IsReadOnly && System.Windows.Automation.AutomationProperties.GetName(recorder) == "Area shortcut");
        });
        Test("Print Screen records key-up-only keyboards and preserves modifiers across two events", () =>
        {
            var recorder = new ShortcutRecorder("Area", "Ctrl+Alt+1", () => new Scope(() => { }), _ => { }); int changes = 0;
            recorder.Changed += _ => changes++;
            recorder.Press(Key.PrintScreen, ModifierKeys.Control, false);
            recorder.Press(Key.LeftCtrl, ModifierKeys.None, true); recorder.Press(Key.PrintScreen, ModifierKeys.None, true);
            Require(recorder.Shortcut == "Ctrl+PrintScreen" && changes == 1);
            recorder.Press(Key.PrintScreen, ModifierKeys.None, true); Require(recorder.Shortcut == "PrintScreen");
            recorder.MarkSaved(recorder.Shortcut); Require(!recorder.IsPending);
        });
        Test("Recorder focus suspension releases once and can be reacquired without global input", () =>
        {
            int acquired = 0, released = 0;
            var recorder = new ShortcutRecorder("Area", "Ctrl+Alt+1", () => { acquired++; return new Scope(() => released++); }, _ => { });
            void Focus() => recorder.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, 0, null, recorder) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });
            Focus(); Focus(); Require(acquired == 1 && recorder.IsSuspending);
            recorder.Release(); recorder.Release(); Require(released == 1 && !recorder.IsSuspending);
            Focus(); Require(acquired == 2 && recorder.IsSuspending); recorder.Release(); Require(released == 2);
        });
        Test("History edits retain original pixels, editable marks and rendered results across restart", () =>
        {
            var store = Store(); var entry = store.Add(Image(), CaptureKind.Window); byte[] original = File.ReadAllBytes(store.ImagePath(entry));
            var document = store.LoadDocument(entry); document.Add(Arrow()); document.Resize(240, 180); document.Background(8, "#FFFFFF");
            var saved = store.SaveEdits(document, entry); var again = new HistoryStore(store.Root); var reopened = again.LoadDocument(again.Entries.Single());
            Require(saved.Id == entry.Id && saved.Mode == "Window" && saved.Created == entry.Created);
            Require(saved.Width == 256 && saved.Height == 196);
            Require(File.ReadAllBytes(store.ImagePath(entry)).SequenceEqual(original));
            Require(JsonSerializer.Serialize(reopened.State) == JsonSerializer.Serialize(document.State));
            Require(reopened.State.Width == 240 && reopened.State.Padding == 8 && reopened.State.Marks.Length == 1);
            Require(Pixels(again.LoadImage(saved)).SequenceEqual(Pixels(EditRenderer.Render(document))));
            Require(again.LoadImage(saved, 80).PixelWidth == 80);
        });
        Test("Editor close silently flushes pending edits into the same history entry", () =>
        {
            var store = Store(); CaptureEntry entry = store.Add(Image(), CaptureKind.Area); var document = store.LoadDocument(entry); int saves = 0;
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: doc => { entry = store.SaveEdits(doc, entry); saves++; });
            document.Add(Arrow()); Require(editor.ConfirmClose()); Require(saves == 1 && store.Entries.Count == 1 && !document.IsDirty);
            Require(store.LoadDocument(entry).State.Marks.Length == 1); Require(editor.ConfirmClose() && saves == 1); editor.Close();
        });
        Test("Background history save runs after editing without a close prompt", () =>
        {
            var store = Store(); CaptureEntry? entry = null; var document = new EditDocument(Image());
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: doc => entry = store.SaveEdits(doc, entry));
            document.Add(Arrow()); Pump(750); Require(entry is not null && store.Entries.Count == 1);
            document.Resize(80, 60); Require(editor.ConfirmClose()); Require(store.Entries.Count == 1 && store.Entries[0].Width == 80); editor.Close();
        });
        Test("A locked history project prevents close and preserves the previous saved edits", () =>
        {
            var store = Store(); CaptureEntry entry = store.Add(Image(), CaptureKind.Area); var document = store.LoadDocument(entry); document.Add(Arrow()); store.SaveEdits(document, entry);
            byte[] before = File.ReadAllBytes(store.ProjectPath(entry)); var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: doc => entry = store.SaveEdits(doc, entry));
            document.Resize(80, 60);
            using (File.Open(store.ProjectPath(entry), FileMode.Open, FileAccess.Read, FileShare.None)) Require(!editor.ConfirmClose() && document.IsDirty);
            Require(File.ReadAllBytes(store.ProjectPath(entry)).SequenceEqual(before)); Require(editor.ConfirmClose());
            Require(store.LoadDocument(entry).State.Width == 80 && store.Entries.Count == 1); editor.Close();
        });
        Test("An index write failure keeps edits recoverable and does not overwrite the original", () =>
        {
            var store = Store(); var entry = store.Add(Image(), CaptureKind.Area); var document = store.LoadDocument(entry); document.Add(Arrow());
            byte[] original = File.ReadAllBytes(store.ImagePath(entry));
            using (File.Open(Path.Combine(store.Root, "history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                bool failed = false; try { store.SaveEdits(document, entry); } catch (IOException) { failed = true; }
                Require(failed && document.IsDirty);
            }
            Require(store.LoadDocument(entry).State.Marks.Length == 1 && File.ReadAllBytes(store.ImagePath(entry)).SequenceEqual(original));
            store.SaveEdits(document, entry); Require(store.Entries.Count == 1);
        });
        Test("Removing an edited capture removes its history project and preserves other entries", () =>
        {
            var store = Store(); var entry = store.Add(Image(), CaptureKind.Area); var other = store.Add(Image(), CaptureKind.Window);
            var document = store.LoadDocument(entry); document.Add(Arrow()); store.SaveEdits(document, entry); store.Remove(entry);
            Require(!File.Exists(store.ProjectPath(entry)) && !File.Exists(store.ImagePath(entry)));
            Require(new HistoryStore(store.Root).Entries.Single().Id == other.Id);
        });
        Test("Autosaved imported images retain one history item after multiple edits", () =>
        {
            var store = Store(); var document = new EditDocument(Image()); document.Add(Arrow());
            var entry = store.SaveEdits(document); document.Resize(80, 60); store.SaveEdits(document, entry);
            Require(new HistoryStore(store.Root).Entries.Count == 1 && store.LoadDocument(entry).State.Width == 80);
        });
        Test("History project paths refuse capture identifiers outside the history folder", () =>
        {
            bool rejected = false; try { Store().ProjectPath(new CaptureEntry("../outside", DateTimeOffset.UtcNow, "Area", 10, 10)); } catch (InvalidDataException) { rejected = true; }
            Require(rejected);
        });
        Test("History recovery restores editable layers and dimensions after both indexes are damaged", () =>
        {
            var store = Store(); var entry = store.Add(Image(), CaptureKind.Area); var document = store.LoadDocument(entry);
            document.Add(Arrow()); document.Resize(80, 60); store.SaveEdits(document, entry);
            File.WriteAllText(Path.Combine(store.Root, "history.json"), "broken"); File.WriteAllText(Path.Combine(store.Root, "history.json.bak"), "broken");
            var recovered = new HistoryStore(store.Root); Require(recovered.Entries.Single().Id == entry.Id && recovered.Entries[0].Width == 80);
            Require(recovered.LoadDocument(entry).State.Marks.Length == 1);
        });
        Test("Retention removes expired automatic projects without touching unrelated files", () =>
        {
            var store = Store(); var entry = store.Add(Image(), CaptureKind.Area); var document = store.LoadDocument(entry); document.Add(Arrow()); store.SaveEdits(document, entry);
            string unrelated = Path.Combine(store.Images, "personal-note.snipsera"); File.WriteAllText(unrelated, "keep");
            for (int i = 0; i < HistoryStore.Capacity; i++) store.Add(Image(), CaptureKind.Area);
            Require(store.Entries.Count == HistoryStore.Capacity && !File.Exists(store.ProjectPath(entry)) && !File.Exists(store.ImagePath(entry)) && File.Exists(unrelated));
        });
        UiChecks.Layout(Test);
        app.Shutdown();
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
