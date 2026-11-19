using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

// Isolated synthetic files and detached windows only. No Show, clipboard, native drag, dialogs or desktop input.
internal static class PreviewDismissalChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static BitmapSource Pattern()
    {
        const int width = 64, height = 48;
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int offset = (y * width + x) * 4;
            pixels[offset] = (byte)(x * 3); pixels[offset + 1] = (byte)(y * 4); pixels[offset + 2] = 120; pixels[offset + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(bytes, converted.PixelWidth * 4, 0); return bytes;
    }

    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var app = new CaptureApp(Path.Combine(output, "isolated-profile"), presentOverlay: (_, _) => { },
            overlayPlacement: () => (new System.Drawing.Rectangle(0, 0, 1920, 1080), 1));
        var outcomes = new List<object>(); int failures = 0;
        var closed = new HashSet<ImageWindow>();
        var previewField = typeof(CaptureApp).GetField("preview", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pinsField = typeof(CaptureApp).GetField("pins", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var createWindow = typeof(CaptureApp).GetMethod("FloatingWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var pins = (List<ImageWindow>)pinsField.GetValue(app)!;
        ImageWindow Card(CaptureEntry? entry, BitmapSource? image = null, RecordingResult? recording = null)
        {
            // The production factory wires preview/pin cleanup. Do not track presentation or create a native handle.
            var card = (ImageWindow)createWindow.Invoke(app, [image ?? Pattern(), entry, false, null, recording])!;
            card.Closed += (_, _) => closed.Add(card);
            Require(!card.IsVisible && new WindowInteropHelper(card).Handle == IntPtr.Zero, "A detached card acquired a visible/native window");
            return card;
        }
        CaptureEntry Capture() => app.History.Add(Pattern(), CaptureKind.Area);
        void Current(ImageWindow card) => previewField.SetValue(app, card);
        bool IsCurrent(ImageWindow card) => ReferenceEquals(previewField.GetValue(app), card);
        void Check(string name, Action check)
        {
            try { check(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
            finally
            {
                foreach (var card in app.Windows.OfType<ImageWindow>().ToArray()) card.Close();
                foreach (var editor in app.Windows.OfType<EditorWindow>().ToArray()) editor.Close();
            }
        }

        Check("Successful transfer closes its preview and preserves original history bytes after reload", () =>
        {
            var entry = Capture(); var card = Card(entry); Current(card);
            byte[] original = File.ReadAllBytes(app.History.ImagePath(entry)); int count = app.History.Entries.Count;
            card.CompleteTransfer(true);
            Require(closed.Contains(card) && previewField.GetValue(app) is null, "The completed preview was not closed and detached");
            var again = new HistoryStore(app.History.Root);
            Require(again.Entries.Count == count && again.Entries.Any(e => e.Id == entry.Id), "Dismissal removed the history entry");
            Require(original.SequenceEqual(File.ReadAllBytes(again.ImagePath(entry))), "Dismissal changed original image bytes");
            Require(Pixels(Pattern()).SequenceEqual(Pixels(again.LoadImage(entry))), "Reloaded capture pixels changed");
        });
        Check("Dismissal preserves edited output, original pixels and editable annotations after reload", () =>
        {
            var entry = Capture(); var document = app.History.LoadDocument(entry);
            document.Add(new Mark(MarkKind.Rectangle, [new(5, 5), new(45, 32)], "#E64A39", 4));
            document.Resize(48, 36); entry = app.History.SaveEdits(document, entry);
            var edited = EditRenderer.Render(document);
            byte[] original = File.ReadAllBytes(app.History.ImagePath(entry)), project = File.ReadAllBytes(app.History.ProjectPath(entry));
            var card = Card(entry, edited); Current(card); card.CompleteTransfer(true);
            var again = new HistoryStore(app.History.Root); var reloaded = again.LoadDocument(entry);
            Require(closed.Contains(card) && again.Entries.Count(e => e.Id == entry.Id) == 1, "Edited preview dismissal changed history identity");
            Require(original.SequenceEqual(File.ReadAllBytes(again.ImagePath(entry))) && project.SequenceEqual(File.ReadAllBytes(again.ProjectPath(entry))), "Editable history bytes changed");
            Require(reloaded.State.Marks.Length == 1 && reloaded.State.Width == 48 && reloaded.State.Height == 36, "Saved edits were lost");
            Require(Pixels(Pattern()).SequenceEqual(Pixels(reloaded.Original)) && Pixels(edited).SequenceEqual(Pixels(again.LoadImage(entry))), "Original or edited pixels changed");
        });
        Check("Cancelled or failed completion leaves the preview available", () =>
        {
            var entry = Capture(); var card = Card(entry); Current(card); card.CompleteTransfer(false);
            Require(!closed.Contains(card) && IsCurrent(card), "An unsuccessful action dismissed its preview");
            Require(app.History.Entries.Any(e => e.Id == entry.Id), "An unsuccessful action removed history");
        });
        Check("Pinning during a transfer preserves the card; unpinning allows later dismissal", () =>
        {
            var entry = Capture(); var card = Card(entry); Current(card); app.TogglePin(card); card.CompleteTransfer(true);
            Require(card.IsPinned && pins.Contains(card) && !closed.Contains(card) && previewField.GetValue(app) is null, "A newly pinned reference was dismissed");
            app.TogglePin(card); Require(!card.IsPinned && !pins.Contains(card) && IsCurrent(card), "Unpin did not restore the same preview");
            card.CompleteTransfer(true);
            Require(closed.Contains(card) && !pins.Contains(card) && previewField.GetValue(app) is null, "Unpinned transfer did not clean up ownership");
        });
        Check("Late completion dismisses only its source and leaves a newer preview intact", () =>
        {
            var old = Card(Capture()); Current(old); var newer = Card(Capture()); Current(newer);
            old.CompleteTransfer(true); old.CompleteTransfer(true);
            Require(closed.Contains(old) && !closed.Contains(newer) && IsCurrent(newer), "Late completion closed or detached the new preview");
        });
        Check("Completion after a preview was already closed leaves its replacement intact", () =>
        {
            var old = Card(Capture()); Current(old); old.Close(); var newer = Card(Capture()); Current(newer);
            old.CompleteTransfer(true);
            Require(!closed.Contains(newer) && IsCurrent(newer), "A stale closed preview affected its replacement");
        });
        Check("A capture absent from history stays available after successful export", () =>
        {
            var entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Area", 64, 48);
            CaptureService.Save(Pattern(), app.History.ImagePath(entry));
            var card = Card(entry); Current(card); card.CompleteTransfer(true);
            Require(!closed.Contains(card) && IsCurrent(card), "An unindexed capture was dismissed");
        });
        Check("A history entry whose original file is missing stays available", () =>
        {
            var entry = Capture(); var card = Card(entry); Current(card); File.Delete(app.History.ImagePath(entry));
            card.CompleteTransfer(true);
            Require(!closed.Contains(card) && IsCurrent(card), "A capture without a persisted image was dismissed");
        });
        Check("Screenshot completion policy leaves an unpinned recording card unchanged", () =>
        {
            string file = Path.Combine(output, "synthetic.mp4"); File.WriteAllText(file, "synthetic media bytes");
            var result = new RecordingResult(file, RecordingFormat.Mp4, 64, 48, TimeSpan.FromSeconds(6), DateTimeOffset.UtcNow);
            var card = Card(null, recording: result); Current(card); card.CompleteTransfer(true);
            Require(!closed.Contains(card) && IsCurrent(card) && card.Recording == result, "Screenshot handling dismissed a recording");
            Require(File.ReadAllText(file) == "synthetic media bytes", "Recording data changed");
        });
        Check("Cancelled, rejected and non-copy drop effects do not complete a transfer", () =>
        {
            int completed = 0, attempts = 0;
            foreach (var result in new[] { DragDropEffects.None, DragDropEffects.Scroll, DragDropEffects.Move, DragDropEffects.Link })
                DragExport.Drop(() => { attempts++; return result; }, () => completed++);
            Require(attempts == 4 && completed == 0, "An unaccepted Copy drop completed the transfer");
        });
        Check("Accepted copy effects complete once and a failed native drop never completes", () =>
        {
            int completed = 0, attempts = 0;
            foreach (var result in new[] { DragDropEffects.Copy, DragDropEffects.Copy | DragDropEffects.Scroll })
                DragExport.Drop(() => { attempts++; return result; }, () => completed++);
            Require(attempts == 2 && completed == 2, "Accepted drops did not complete exactly once");
            var failure = new System.Runtime.InteropServices.ExternalException("Synthetic drag failure");
            try { DragExport.Drop(() => throw failure, () => completed++); throw new Exception("Drag failure was swallowed"); }
            catch (System.Runtime.InteropServices.ExternalException caught) { Require(ReferenceEquals(caught, failure), "The wrong failure propagated"); }
            Require(completed == 2, "A failed drop completed its transfer");
        });
        Check("Accepted drag closes the preview while staged PNG and history remain readable", () =>
        {
            var entry = Capture(); var card = Card(entry); Current(card); var image = Pattern();
            var data = DragExport.CreateData(app.History.Root, image); string file = data.GetFileDropList().Cast<string>().Single();
            byte[] original = File.ReadAllBytes(file);
            DragExport.Drop(() => DragDropEffects.None, () => card.CompleteTransfer(true));
            Require(!closed.Contains(card), "A cancelled drag dismissed the preview");
            DragExport.Drop(() => DragDropEffects.Copy, () => card.CompleteTransfer(true));
            Require(closed.Contains(card) && original.SequenceEqual(File.ReadAllBytes(file)), "Dismissal removed or changed the staged drag file");
            Require(Pixels(image).SequenceEqual(Pixels(CaptureService.Load(file))) && new HistoryStore(app.History.Root).Entries.Any(e => e.Id == entry.Id), "Drop delivery or history was not preserved");
        });
        Check("Explicit pin close clears pin ownership without removing history", () =>
        {
            var entry = Capture(); var card = Card(entry); Current(card); app.TogglePin(card); card.Close();
            Require(closed.Contains(card) && !pins.Contains(card) && previewField.GetValue(app) is null, "Closed pin remains owned");
            Require(new HistoryStore(app.History.Root).Entries.Any(e => e.Id == entry.Id), "Closing the reference removed history");
        });
        Check("Editor transfer reports the exact state; edit, undo and redo update its current-state status", () =>
        {
            var document = new EditDocument(Pattern()); int completions = 0; EditState? reported = null;
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: _ => { },
                transferCompleted: state => { completions++; reported = state; });
            Require(!editor.HasTransferredCurrentState, "A new editor starts marked as transferred");
            var shared = document.State; editor.CompleteTransfer(shared);
            Require(editor.HasTransferredCurrentState && completions == 1 && ReferenceEquals(reported, shared), "Transfer callback lost the shared state");
            document.Resize(48, 36); Require(!editor.HasTransferredCurrentState, "A later edit remained marked as transferred");
            document.Undo(); Require(editor.HasTransferredCurrentState, "Undo did not restore the transferred state");
            document.Redo(); Require(!editor.HasTransferredCurrentState && completions == 1, "Redo marked a new edit as transferred or repeated the callback");
            Require(!editor.IsVisible && new WindowInteropHelper(editor).Handle == IntPtr.Zero, "The editor acquired a visible/native window");
        });
        Check("Delayed editor transfer completion does not mark newer edits as already shared", () =>
        {
            var document = new EditDocument(Pattern()); EditState? reported = null;
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: _ => { }, transferCompleted: state => reported = state);
            var capturedBeforeAwait = document.State;
            document.Add(new Mark(MarkKind.Line, [new(4, 4), new(50, 34)], "#247AC6", 3));
            editor.CompleteTransfer(capturedBeforeAwait);
            Require(ReferenceEquals(reported, capturedBeforeAwait) && !editor.HasTransferredCurrentState, "Stale success suppressed the newer edited state");
            editor.CompleteTransfer(document.State);
            Require(editor.HasTransferredCurrentState && ReferenceEquals(reported, document.State), "Sharing the latest edits did not update transfer status");
        });
        Check("Cancelled editor drags keep its state unshared; accepted copy marks exactly that state", () =>
        {
            var document = new EditDocument(Pattern()); int completions = 0;
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: _ => { }, transferCompleted: _ => completions++);
            var dragged = document.State;
            foreach (var effect in new[] { DragDropEffects.None, DragDropEffects.Scroll })
                DragExport.Drop(() => effect, () => editor.CompleteTransfer(dragged));
            Require(!editor.HasTransferredCurrentState && completions == 0, "Cancelled/rejected drag marked the editor as shared");
            DragExport.Drop(() => DragDropEffects.Copy, () => editor.CompleteTransfer(dragged));
            Require(editor.HasTransferredCurrentState && completions == 1, "An accepted drag did not report its state once");
        });
        Check("Editor persists edits before transfer callbacks and retains the editable project after close", () =>
        {
            var entry = Capture(); byte[] original = File.ReadAllBytes(app.History.ImagePath(entry));
            var document = app.History.LoadDocument(entry); int saves = 0, callbacks = 0; bool editorClosed = false;
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: edited => { entry = app.History.SaveEdits(edited, entry); saves++; },
                transferCompleted: state =>
                {
                    callbacks++; var persisted = new HistoryStore(app.History.Root);
                    Require(File.Exists(persisted.ProjectPath(entry)) && Pixels(EditRenderer.Render(document)).SequenceEqual(Pixels(persisted.LoadImage(entry))), "Transfer callback ran before edited history was durable");
                    Require(ReferenceEquals(state, document.State), "Callback did not identify the completed state");
                });
            editor.Closed += (_, _) => editorClosed = true;
            document.Add(new Mark(MarkKind.Rectangle, [new(5, 6), new(45, 32)], "#E64A39", 4));
            Require(editor.CompleteTransfer(document.State), "A durable transfer reported failure");
            Require(editor.FlushHistory() && saves == 1 && editor.HasTransferredCurrentState, "Transfer state was lost during history flush");
            document.Resize(48, 36); editor.CompleteTransfer(document.State);
            var expected = EditRenderer.Render(document); editor.Close();
            Require(editorClosed && saves == 2 && callbacks == 2 && !app.Windows.OfType<Window>().Any(window => window.IsVisible), "Closing did not preserve completed transfers without a visible dialog");
            var again = new HistoryStore(app.History.Root); var restored = again.LoadDocument(entry);
            Require(again.Entries.Count(e => e.Id == entry.Id) == 1 && restored.State.Marks.Length == 1 && restored.State.Width == 48 && restored.State.Height == 36, "The transferred edit project was lost or duplicated");
            Require(original.SequenceEqual(File.ReadAllBytes(again.ImagePath(entry))) && Pixels(expected).SequenceEqual(Pixels(again.LoadImage(entry))), "Closing changed original pixels or lost the final edited output");
        });
        Check("Failed editor history flush keeps the current state unshared and suppresses transfer callbacks", () =>
        {
            var entry = Capture(); var document = app.History.LoadDocument(entry); bool fail = true; int callbacks = 0;
            var editor = new EditorWindow(document, _ => { }, () => { }, saveHistory: edited =>
            {
                if (fail) throw new IOException("Synthetic history write failure");
                entry = app.History.SaveEdits(edited, entry);
            }, transferCompleted: _ => callbacks++);
            try
            {
                document.Resize(48, 36);
                Require(!editor.CompleteTransfer(document.State) && !editor.HasTransferredCurrentState && callbacks == 0, "Failed history persistence was marked as transferred");
                Require(document.IsDirty && !File.Exists(app.History.ProjectPath(entry)), "Failure discarded edits or claimed an editable project exists");
                Require(!editor.IsVisible && new WindowInteropHelper(editor).Handle == IntPtr.Zero, "History failure opened a native editor/dialog");
                fail = false;
                Require(editor.CompleteTransfer(document.State) && editor.HasTransferredCurrentState && callbacks == 1, "Retry did not complete once after persistence recovered");
                Require(new HistoryStore(app.History.Root).LoadDocument(entry).State.Width == 48, "Recovered transfer is not durable");
            }
            finally { fail = false; editor.Close(); }
        });

        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown();
        return failures == 0 ? 0 : 1;
    }
}
