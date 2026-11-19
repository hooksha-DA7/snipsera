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
using PixelRect = System.Drawing.Rectangle;

// Synthetic history and fake presentation/monitor placement only. No Show, native handles, clipboard, capture or physical input.
internal static class PreviewStackIntegrationChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static BitmapSource Image(byte shade)
    {
        const int width = 80, height = 60; var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = shade; pixels[i + 1] = 70; pixels[i + 2] = 160; pixels[i + 3] = 255; }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }

    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var presented = new HashSet<ImageWindow>(); var observed = new HashSet<ImageWindow>();
        var app = new CaptureApp(Path.Combine(output, "profile"),
            presentOverlay: (window, show) =>
            {
                Require(!window.IsVisible && new WindowInteropHelper(window).Handle == IntPtr.Zero, "Synthetic presentation acquired a native window");
                if (observed.Add(window)) window.Closed += (_, _) => presented.Remove(window);
                if (show) presented.Add(window); else presented.Remove(window);
            },
            overlayPlacement: () => (new PixelRect(-1920, 0, 1920, 1040), 1d));
        app.Storage.SetAutomaticImageSaving(false);
        app.Storage.SetImageFolder(Path.Combine(output, "automatic-images"));
        var overlays = (OverlayVisibility<ImageWindow>)typeof(CaptureApp).GetField("overlays", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var stack = (ScreenshotStack)typeof(CaptureApp).GetField("screenshotStack", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var pins = (List<ImageWindow>)typeof(CaptureApp).GetField("pins", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(app)!;
        var outcomes = new List<object>(); int failures = 0, color = 0;
        PendingScreenshot Capture()
        {
            var image = Image((byte)++color); var entry = app.History.Add(image, CaptureKind.Area);
            return app.AddScreenshotPreview(image, entry);
        }
        ImageWindow Card(PendingScreenshot item) => app.VisibleScreenshotPreviews.Single(window => window.Entry?.Id == item.Entry.Id);
        string[] PendingIds() => app.PendingScreenshots.Select(item => item.Entry.Id).ToArray();
        string[] VisibleIds() => app.VisibleScreenshotPreviews.Select(window => window.Entry!.Id).OrderBy(id => id).ToArray();
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
            finally
            {
                foreach (var editor in app.Windows.OfType<EditorWindow>().ToArray()) editor.Close();
                app.ClearUnpinnedPreviews(); foreach (var pin in pins.ToArray()) pin.Close();
                stack.TakeLastDismissed(_ => false); overlays.Restore(); app.PreviewPreferences.SetDismissAfterSharing(true);
            }
        }

        Check("Unpinning a recording before any screenshot obtains a valid work area and keeps its window", () =>
        {
            Require(app.PendingScreenshots.Count == 0, "Recording-only fixture has screenshots");
            var work = new PixelRect(-1920, 0, 1920, 1040);
            var recording = new RecordingResult(Path.Combine(output, "synthetic-recording.mp4"), RecordingFormat.Mp4, 80, 60, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow);
            var factory = typeof(CaptureApp).GetMethod("FloatingWindow", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var window = (ImageWindow)factory.Invoke(app, [Image(25), null, true, null, recording])!;
            window.ConfigureDock(work, 1); pins.Add(window); overlays.Track(window);
            app.TogglePin(window);
            var bounds = (PixelRect?)typeof(ImageWindow).GetField("dockBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window);
            Require(!window.IsPinned && !pins.Contains(window) && window.DockWorkArea == work && bounds is { } box && work.Contains(box), "Unpinned recording has no valid placement");
            Require(presented.Contains(window) && app.PendingScreenshots.Count == 0 && window.Recording == recording, "Unpin replaced the recording or added a screenshot");
            app.TogglePin(window); Require(window.IsPinned && pins.Contains(window), "Recording could not be repinned in place");
        });
        Check("Five captures retain newest-first metadata with only three live screenshot cards", () =>
        {
            var captures = Enumerable.Range(0, 5).Select(_ => Capture()).ToArray();
            Require(PendingIds().SequenceEqual(captures.Reverse().Select(item => item.Entry.Id)), "Pending order is not newest-first");
            Require(app.VisibleScreenshotPreviews.Count == 3 && app.PendingOverflowCount == 2, "Visible/overflow counts are wrong");
            Require(VisibleIds().SequenceEqual(captures.Skip(2).Select(item => item.Entry.Id).OrderBy(id => id)), "Wrong cards occupy the visible stack");
            Require(app.PendingScreenshots.All(item => item.RecoveryImage is null), "Saved overflow retained full-resolution images");
            Require(app.Windows.OfType<ImageWindow>().Count() == 3 && presented.Count == 3, "Overflow created excess windows");
        });
        Check("Selecting an overflow capture promotes it once without duplicate entries", () =>
        {
            var captures = Enumerable.Range(0, 5).Select(_ => Capture()).ToArray();
            app.ShowPendingScreenshot(captures[0].Entry.Id); app.ShowPendingScreenshot(captures[0].Entry.Id);
            Require(app.PendingScreenshots.Count == 5 && app.PendingScreenshots[0].Entry.Id == captures[0].Entry.Id, "Selection lost or duplicated a capture");
            Require(PendingIds().Distinct().Count() == 5 && app.VisibleScreenshotPreviews.Count == 3 && app.PendingOverflowCount == 2, "Selection changed stack capacity");
            Require(app.VisibleScreenshotPreviews.Any(window => window.Entry?.Id == captures[0].Entry.Id), "Selected overflow capture is not visible");
        });
        Check("Pending screenshots form front and rear layers while Pin and Unpin reset the selected layer", () =>
        {
            var captures = Enumerable.Range(0, 3).Select(_ => Capture()).ToArray();
            Require(app.VisibleScreenshotPreviews.Select(window => window.DeckDepth).SequenceEqual(new[] { 0, 1, 2 }), "Deck depths do not follow pending order");
            var card = Card(captures[0]); app.TogglePin(card);
            Require(card.IsPinned && !card.IsDeckBack && card.DeckDepth == 0, "Pin retained rear-card interaction limits");
            app.TogglePin(card);
            Require(ReferenceEquals(app.VisibleScreenshotPreviews[0], card) && !card.IsDeckBack && app.PendingScreenshots.Count == 3, "Unpin did not return the same window to the front");
        });
        Check("Keyboard layer selection brings the chosen capture forward without sharing or removing its neighbours", () =>
        {
            var captures = Enumerable.Range(0, 4).Select(_ => Capture()).ToArray();
            var oldFront = app.VisibleScreenshotPreviews[0]; int count = app.History.Entries.Count;
            oldFront.SelectDeckLayer?.Invoke(2);
            Require(app.PendingScreenshots[0].Entry.Id == captures[1].Entry.Id && app.VisibleScreenshotPreviews[0].DeckDepth == 0, "Layer selection brought forward the wrong capture");
            Require(app.PendingScreenshots.Count == 4 && app.PendingOverflowCount == 1 && !app.CanReopenLastDismissed && app.History.Entries.Count == count, "Deck selection dismissed, shared or removed history");
            Require(app.VisibleScreenshotPreviews.Contains(oldFront) && oldFront.IsDeckBack, "Selecting a layer replaced the former front unnecessarily");
            var order = PendingIds(); app.SelectDeckLayer(-1); app.SelectDeckLayer(3);
            Require(PendingIds().SequenceEqual(order), "An unavailable layer changed the deck");
        });
        Check("Successful sharing dismisses one card, refills the stack and preserves all history", () =>
        {
            var captures = Enumerable.Range(0, 5).Select(_ => Capture()).ToArray(); int historyCount = app.History.Entries.Count;
            var shared = Card(captures[4]); shared.CompleteTransfer(true);
            Require(!PendingIds().Contains(captures[4].Entry.Id) && app.PendingScreenshots.Count == 4, "Shared card remains pending");
            Require(app.VisibleScreenshotPreviews.Count == 3 && app.PendingOverflowCount == 1 && app.VisibleScreenshotPreviews.Any(window => window.Entry?.Id == captures[1].Entry.Id), "Dismissal did not refill from overflow");
            var history = new HistoryStore(app.History.Root);
            Require(history.Entries.Count == historyCount && captures.All(item => history.Entries.Any(entry => entry.Id == item.Entry.Id)), "Dismissal removed history");
            Require(app.CanReopenLastDismissed, "A shared capture cannot be reopened");
        });
        Check("Delayed transfer dismisses its still-owned pending capture after its window moves into overflow", () =>
        {
            var capture = Capture(); var source = Card(capture); bool retired = false; source.Closed += (_, _) => retired = true;
            Capture(); Capture(); Capture();
            Require(retired && app.OwnsPending(capture) && app.PendingOverflowCount == 1, "Fixture did not retire the source into overflow");
            source.CompleteTransfer(true);
            Require(!PendingIds().Contains(capture.Entry.Id) && app.PendingScreenshots.Count == 3 && app.PendingOverflowCount == 0, "Delayed Copy left its completed capture pending");
            Require(new HistoryStore(app.History.Root).Entries.Any(entry => entry.Id == capture.Entry.Id), "Delayed completion removed history");
        });
        Check("A stale transfer cannot dismiss a replacement node or an explicitly reopened capture", () =>
        {
            var capture = Capture(); var source = Card(capture); Capture(); Capture(); Capture();
            app.ShowPendingScreenshot(capture.Entry.Id); var replacement = Card(capture);
            Require(!app.OwnsPending(capture) && !ReferenceEquals(source, replacement), "Overflow selection did not replace the source node");
            source.CompleteTransfer(true); Require(ReferenceEquals(Card(capture), replacement), "Stale transfer dismissed a selected replacement");
            replacement.Close(); app.ReopenLastDismissed(); var reopened = Card(capture);
            replacement.CompleteTransfer(true);
            Require(ReferenceEquals(Card(capture), reopened) && PendingIds().Count(id => id == capture.Entry.Id) == 1, "Late completion dismissed or duplicated an explicitly reopened capture");
        });
        Check("Dismiss preference changes affect subsequent transfers without changing pending order", () =>
        {
            var first = Capture(); var second = Capture(); var card = Card(first); var before = PendingIds();
            app.PreviewPreferences.SetDismissAfterSharing(false); card.CompleteTransfer(true);
            Require(PendingIds().SequenceEqual(before) && ReferenceEquals(Card(first), card), "Off preference dismissed or recreated a card");
            app.PreviewPreferences.SetDismissAfterSharing(true); card.CompleteTransfer(false);
            Require(PendingIds().SequenceEqual(before), "A failed transfer dismissed a card");
            card.CompleteTransfer(true); Require(PendingIds().SequenceEqual(new[] { second.Entry.Id }), "Enabled dismissal removed the wrong card");
        });
        Check("Pin and Unpin reuse one window while preserving other pending screenshots", () =>
        {
            var first = Capture(); var second = Capture(); var third = Capture(); var card = Card(second); var neighbor = Card(third);
            app.TogglePin(card);
            Require(card.IsPinned && pins.Contains(card) && !PendingIds().Contains(second.Entry.Id) && app.PendingScreenshots.Count == 2, "Pin did not transfer ownership");
            Require(ReferenceEquals(Card(third), neighbor), "Pin unnecessarily replaced another preview");
            app.TogglePin(card);
            Require(!card.IsPinned && !pins.Contains(card) && ReferenceEquals(Card(second), card) && app.PendingScreenshots.Count == 3, "Unpin replaced the source window or another capture");
            Require(PendingIds().Distinct().Count() == 3 && PendingIds().Contains(first.Entry.Id), "Unpin lost or duplicated another capture");
        });
        Check("A moved or resized pin redocks when unpinned and remains docked on repin", () =>
        {
            var capture = Capture(); var card = Card(capture); app.TogglePin(card);
            typeof(ImageWindow).GetProperty(nameof(ImageWindow.IsDocked))!.SetValue(card, false); card.Width = 500;
            app.TogglePin(card);
            Require(card.IsDocked && !card.IsPinned && ReferenceEquals(Card(capture), card) && card.Width <= FloatingLayout.MaxWidth, "Unpin did not restore compact docked ownership");
            app.TogglePin(card);
            var bounds = (PixelRect?)typeof(ImageWindow).GetField("dockBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(card);
            Require(card.IsDocked && card.IsPinned && pins.Contains(card) && bounds is { } box && card.DockWorkArea.Contains(box), "Repinned window remained detached or unplaced");
        });
        Check("Clear unpinned keeps pins and history and Reopen restores the newest cleared screenshot", () =>
        {
            var pinned = Capture(); var pin = Card(pinned); app.TogglePin(pin);
            var older = Capture(); var newest = Capture(); int count = app.History.Entries.Count;
            app.ClearUnpinnedPreviews();
            Require(app.PendingScreenshots.Count == 0 && app.VisibleScreenshotPreviews.Count == 0 && app.PendingOverflowCount == 0, "Clear left a pending card");
            Require(pins.Contains(pin) && pin.IsPinned && presented.Contains(pin), "Clear dismissed a pinned reference");
            Require(new HistoryStore(app.History.Root).Entries.Count == count && app.CanReopenLastDismissed, "Clear removed history or recovery");
            app.ReopenLastDismissed(); Require(PendingIds().SequenceEqual(new[] { newest.Entry.Id }), "Reopen chose the wrong cleared screenshot");
            app.ReopenLastDismissed(); Require(PendingIds().SequenceEqual(new[] { older.Entry.Id, newest.Entry.Id }) && pins.Contains(pin), "Repeated reopen lost a restored capture or pin");
        });
        Check("Edited and recovery overflow thumbnails own only small frozen pixels", () =>
        {
            var capture = Capture(); var document = app.History.LoadDocument(capture.Entry);
            document.Resize(1200, 900); var entry = app.History.SaveEdits(document, capture.Entry);
            var edited = app.PendingThumbnail(new PendingScreenshot(entry));
            var recovery = app.PendingThumbnail(new PendingScreenshot(entry with { Id = Guid.NewGuid().ToString("N") }, app.History.LoadImage(entry)));
            foreach (var thumbnail in new[] { edited, recovery })
            {
                Require(thumbnail.IsFrozen && thumbnail.PixelWidth == 120 && thumbnail.PixelHeight == 90, "Thumbnail exceeds its small pixel bounds");
                Require(thumbnail is not TransformedBitmap && thumbnail is not RenderTargetBitmap && thumbnail is not FormatConvertedBitmap && thumbnail is not CroppedBitmap, "Thumbnail retains a full-size source/render");
                var pixels = new byte[thumbnail.PixelWidth * thumbnail.PixelHeight * 4]; thumbnail.CopyPixels(pixels, thumbnail.PixelWidth * 4, 0);
                Require(pixels.Any(value => value != 0), "Independent thumbnail lost the rendered image");
            }
            var slender = BitmapSource.Create(1, 1000, 96, 96, PixelFormats.Bgra32, null, new byte[4000], 4); slender.Freeze();
            var tiny = app.PendingThumbnail(new PendingScreenshot(entry with { Id = Guid.NewGuid().ToString("N") }, slender));
            Require(tiny.PixelWidth == 1 && tiny.PixelHeight == 120, "Very narrow recovery image has invalid thumbnail dimensions");
        });
        Check("Temporary hide and capture suspension keep newly added previews hidden until restoration", () =>
        {
            Capture(); Capture(); app.HideOverlays(); Require(app.OverlaysHidden && presented.Count == 0, "Hide failed");
            using (overlays.Suspend())
            {
                Capture(); Capture(); Require(presented.Count == 0 && app.PendingScreenshots.Count == 4 && app.PendingOverflowCount == 1, "Capture presented a pending screenshot while hidden/suspended");
            }
            Require(app.OverlaysHidden && presented.Count == 0, "Capture completion undid temporary hiding");
            app.RestoreOverlays(); Require(!app.OverlaysHidden && presented.Count == 3, "Restore did not present the visible stack");
            using (overlays.Suspend()) { Require(presented.Count == 0, "Visible stack escaped capture suspension"); Capture(); Require(presented.Count == 0, "New capture appeared during suspension"); }
            Require(presented.Count == 3 && app.PendingScreenshots.Count == 5, "Normal capture suspension did not restore the stack");
        });
        Check("Editing an older card preserves other window identities and its pending position", () =>
        {
            var first = Capture(); var second = Capture(); var third = Capture(); var neighbor = Card(third);
            var document = app.History.LoadDocument(first.Entry); var editor = app.CreateEditor(document, historyEntry: first.Entry, editingItem: first);
            Require(!app.VisibleScreenshotPreviews.Any(window => window.Entry?.Id == first.Entry.Id) && ReferenceEquals(Card(third), neighbor), "Editor hid or replaced the wrong preview");
            var newest = Capture(); document.Resize(40, 30); editor.Close();
            Require(PendingIds().SequenceEqual(new[] { newest.Entry.Id, third.Entry.Id, second.Entry.Id, first.Entry.Id }), "Editor close reordered or replaced another capture");
            Require(ReferenceEquals(Card(third), neighbor) && new HistoryStore(app.History.Root).LoadDocument(first.Entry).State.Width == 40, "Editor lost neighbor identity or edits");
        });
        Check("Closing an editor after Clear cannot resurrect its dismissed preview", () =>
        {
            var capture = Capture(); var document = app.History.LoadDocument(capture.Entry);
            var editor = app.CreateEditor(document, historyEntry: capture.Entry, editingItem: capture);
            app.ClearUnpinnedPreviews(); var newest = Capture(); var neighbor = Card(newest);
            document.Resize(40, 30); editor.Close();
            Require(PendingIds().SequenceEqual(new[] { newest.Entry.Id }) && ReferenceEquals(Card(newest), neighbor), "Editor resurrected a cleared preview");
            Require(new HistoryStore(app.History.Root).LoadDocument(capture.Entry).State.Width == 40, "Clear prevented editable history persistence");
            app.ReopenLastDismissed();
            Require(Card(capture).Entry?.Width == 40 && Card(capture).Entry?.Height == 30 && Card(capture).CaptureImage.PixelWidth == 40, "Reopen retained stale edited dimensions or pixels");
        });
        Check("Clear after replacing the editor's pending node suppresses restoration by capture identity", () =>
        {
            var capture = Capture(); var document = app.History.LoadDocument(capture.Entry); var editor = app.CreateEditor(document, historyEntry: capture.Entry, editingItem: capture);
            app.OpenEntry(capture.Entry); Require(!app.OwnsPending(capture), "Fixture did not replace the pending node");
            app.ClearUnpinnedPreviews(); var newer = Capture(); var newerWindow = Card(newer);
            document.Resize(40, 30); editor.Close();
            Require(PendingIds().SequenceEqual(new[] { newer.Entry.Id }) && ReferenceEquals(Card(newer), newerWindow), "An old editor resurrected a replaced-and-cleared pending capture");
            Require(new HistoryStore(app.History.Root).LoadDocument(capture.Entry).State.Width == 40, "Suppression discarded saved edits");
        });
        Check("An already-shared edit stays dismissed when its editor closes", () =>
        {
            var capture = Capture(); var neighbor = Capture(); var neighborWindow = Card(neighbor); var document = app.History.LoadDocument(capture.Entry);
            var editor = app.CreateEditor(document, historyEntry: capture.Entry, editingItem: capture);
            document.Resize(40, 30); Require(editor.CompleteTransfer(document.State), "Transfer was not persisted"); editor.Close();
            Require(PendingIds().SequenceEqual(new[] { neighbor.Entry.Id }) && ReferenceEquals(Card(neighbor), neighborWindow), "Shared editor result returned or changed another preview");
            Require(new HistoryStore(app.History.Root).LoadDocument(capture.Entry).State.Width == 40 && app.CanReopenLastDismissed, "Shared editor result is not durable/reopenable");
        });
        Check("Further edits after sharing return one updated preview without replacing newer captures", () =>
        {
            var capture = Capture(); var document = app.History.LoadDocument(capture.Entry); var editor = app.CreateEditor(document, historyEntry: capture.Entry, editingItem: capture);
            document.Resize(40, 30); editor.CompleteTransfer(document.State); var newer = Capture(); var newerWindow = Card(newer);
            document.Resize(24, 18); editor.Close();
            Require(app.PendingScreenshots.Count == 2 && PendingIds().Distinct().Count() == 2 && PendingIds().Contains(capture.Entry.Id), "Later edits did not return once");
            Require(ReferenceEquals(Card(newer), newerWindow) && Card(capture).CaptureImage.PixelWidth == 24, "Later edits replaced another card or show stale pixels");
        });
        Check("A delayed editor completion does not dismiss newer edits or another screenshot", () =>
        {
            var capture = Capture(); var document = app.History.LoadDocument(capture.Entry); var editor = app.CreateEditor(document, historyEntry: capture.Entry, editingItem: capture);
            document.Resize(40, 30); var sharedState = document.State; document.Resize(24, 18); var newer = Capture(); var newerWindow = Card(newer);
            editor.CompleteTransfer(sharedState); editor.Close();
            Require(PendingIds().Contains(capture.Entry.Id) && ReferenceEquals(Card(newer), newerWindow), "Stale transfer dismissed newer work");
            Require(Card(capture).CaptureImage.PixelWidth == 24, "Stale transfer replaced the latest edit pixels");
        });
        Check("With dismissal off an editor transfer remains in the pending stack", () =>
        {
            var capture = Capture(); var document = app.History.LoadDocument(capture.Entry); var editor = app.CreateEditor(document, historyEntry: capture.Entry, editingItem: capture);
            app.PreviewPreferences.SetDismissAfterSharing(false); document.Resize(40, 30); editor.CompleteTransfer(document.State); editor.Close();
            Require(PendingIds().SequenceEqual(new[] { capture.Entry.Id }) && Card(capture).CaptureImage.PixelWidth == 40, "Editor ignored the dismissal preference");
        });
        foreach (bool edited in new[] { false, true })
        Check($"Sharing an {(edited ? "edited" : "unchanged")} recovery screenshot rebinds its pending ID to durable history", () =>
        {
            var original = Image(210); var missing = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Area", 80, 60);
            var pending = app.AddScreenshotPreview(original, missing, "Synthetic failed history write"); var before = app.History.Entries.Select(entry => entry.Id).ToHashSet();
            var document = new EditDocument(original); var editor = app.CreateEditor(document, editingItem: pending);
            if (edited) document.Resize(40, 30);
            Require(editor.CompleteTransfer(document.State), "Recovery screenshot could not be persisted before dismissal"); editor.Close();
            var saved = app.History.Entries.Single(entry => !before.Contains(entry.Id));
            Require(saved.Id != missing.Id && app.PendingScreenshots.Count == 0 && app.CanReopenLastDismissed, "Recovery retained a stale pending ID or lost reopen ownership");
            app.ReopenLastDismissed();
            Require(PendingIds().SequenceEqual(new[] { saved.Id }) && app.VisibleScreenshotPreviews.Single().CaptureImage.PixelWidth == (edited ? 40 : 80), "Reopen used stale recovery pixels or identity");
            var history = new HistoryStore(app.History.Root);
            Require(history.LoadDocument(saved).State.Width == (edited ? 40 : 80) && File.Exists(history.ProjectPath(saved)), "Reopened recovery lacks durable editable history");
        });
        Check("Clearing a recovery screenshot during editing keeps its saved result dismissed and reopens the new history ID", () =>
        {
            var original = Image(211); var missing = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Area", 80, 60);
            var pending = app.AddScreenshotPreview(original, missing, "Synthetic failed history write"); var before = app.History.Entries.Select(entry => entry.Id).ToHashSet();
            var document = new EditDocument(original); var editor = app.CreateEditor(document, editingItem: pending);
            app.ClearUnpinnedPreviews(); document.Resize(36, 27); editor.Close();
            var saved = app.History.Entries.Single(entry => !before.Contains(entry.Id));
            Require(saved.Id != missing.Id && app.PendingScreenshots.Count == 0 && app.VisibleScreenshotPreviews.Count == 0 && app.CanReopenLastDismissed, "Saving resurrected a cleared recovery screenshot or lost reopen ownership");
            app.ReopenLastDismissed();
            Require(PendingIds().SequenceEqual(new[] { saved.Id }) && app.VisibleScreenshotPreviews.Single().CaptureImage.PixelWidth == 36, "Reopen restored the old recovery identity or stale pixels");
            var history = new HistoryStore(app.History.Root);
            Require(history.LoadDocument(saved).State.Width == 36 && File.Exists(history.ProjectPath(saved)), "Cleared recovery edits are not durable");
        });
        Check("Unavailable dismissed history is skipped and stale overflow is removed safely", () =>
        {
            var older = Capture(); var unavailable = Capture(); Card(unavailable).Close(); File.Delete(app.History.ImagePath(unavailable.Entry));
            Require(!app.CanReopenLastDismissed, "Missing history was advertised as reopenable"); app.ReopenLastDismissed();
            Require(PendingIds().SequenceEqual(new[] { older.Entry.Id }), "Missing history changed the current preview");
            var captures = Enumerable.Range(0, 4).Select(_ => Capture()).ToArray(); File.Delete(app.History.ImagePath(older.Entry)); Capture();
            Require(!PendingIds().Contains(older.Entry.Id) && app.VisibleScreenshotPreviews.Count == 3, "Unavailable overflow kept a stale entry or broke visible previews");
        });
        Check("Unsaved screenshots remain recoverable in overflow and cannot auto-dismiss", () =>
        {
            var entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Area", 80, 60);
            var capture = app.AddScreenshotPreview(Image(222), entry, "Synthetic history failure"); Card(capture).CompleteTransfer(true);
            Require(PendingIds().Contains(entry.Id), "Unsaved screenshot was dismissed after sharing");
            Capture(); Capture(); Capture(); Require(app.PendingScreenshots.Single(item => item.Entry.Id == entry.Id).RecoveryImage is not null, "Overflow discarded unsaved recovery pixels");
            app.ShowPendingScreenshot(entry.Id); Require(Card(capture).CaptureImage.PixelWidth == 80, "Unsaved overflow could not be restored");
            app.ClearUnpinnedPreviews(); Require(app.CanReopenLastDismissed, "Clear discarded the last recovery reference");
        });

        Require(!app.Windows.Cast<Window>().Any(window => window.IsVisible || new WindowInteropHelper(window).Handle != IntPtr.Zero), "Integration suite created a native window");
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes, desktop = "not used" }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
}
