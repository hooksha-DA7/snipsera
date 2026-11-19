using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelRect = System.Drawing.Rectangle;

namespace Framelet;

public sealed partial class CaptureApp
{
    private readonly ScreenshotStack screenshotStack = new();
    private readonly Dictionary<string, ImageWindow> screenshotWindows = [];
    private readonly Dictionary<string, int> editingScreenshots = [];
    private readonly HashSet<string> dismissedWhileEditing = [];
    private readonly HashSet<ImageWindow> retiringPreviews = [];
    private readonly Func<(PixelRect WorkArea, double Scale)> overlayPlacement;
    private readonly bool presentNativeOverlays;
    private bool refreshingPreviews;
    private PixelRect previewWorkArea;
    private double previewScale = 1;
    private ImageWindow? recordingPreview;
    private PreviewOverflowWindow? previewOverflow;
    private MenuItem? reopenPreviewMenu, clearPreviewMenu;
    private PixelRect? overflowBounds;
    internal IReadOnlyList<PendingScreenshot> PendingScreenshots => screenshotStack.Pending;
    internal IReadOnlyList<ImageWindow> VisibleScreenshotPreviews => screenshotStack.Pending.Where(p => screenshotWindows.ContainsKey(p.Entry.Id)).Select(p => screenshotWindows[p.Entry.Id]).ToArray();
    internal int PendingOverflowCount => Math.Max(0, screenshotStack.Pending.Count(p => !editingScreenshots.ContainsKey(p.Entry.Id)) - screenshotWindows.Count);
    internal bool CanReopenLastDismissed => screenshotStack.Dismissed.Any(p => Available(p) && !pins.Any(w => w.Entry?.Id == p.Entry.Id));

    private bool HasHistory(CaptureEntry entry) => History.Entries.Any(e => e.Id == entry.Id) && File.Exists(History.ImagePath(entry));
    private bool Available(PendingScreenshot item) => item.RecoveryImage is not null || HasHistory(item.Entry);
    private CaptureEntry CurrentEntry(PendingScreenshot item) => History.Entries.FirstOrDefault(entry => entry.Id == item.Entry.Id) ?? item.Entry;
    internal bool OwnsPending(PendingScreenshot item) => ReferenceEquals(screenshotStack.Find(item.Entry.Id), item);
    private BitmapSource PendingImage(PendingScreenshot item) => HasHistory(item.Entry) ? History.LoadImage(item.Entry) : item.RecoveryImage
        ?? throw new FileNotFoundException("This capture is no longer in history.");
    internal BitmapSource PendingThumbnail(PendingScreenshot item)
    {
        BitmapSource image = HasHistory(item.Entry) ? History.LoadImage(item.Entry, 120) : PendingImage(item);
        double scale = Math.Min(1, 120d / Math.Max(image.PixelWidth, image.PixelHeight));
        int width = Math.Max(1, (int)Math.Round(image.PixelWidth * scale)), height = Math.Max(1, (int)Math.Round(image.PixelHeight * scale));
        var visual = new DrawingVisual();
        using (var drawing = visual.RenderOpen()) drawing.DrawImage(image, new System.Windows.Rect(0, 0, width, height));
        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
        // Copy the small pixels so an edited thumbnail cannot keep a full-resolution render alive through its Source chain.
        int stride = width * 4;
        var pixels = new byte[stride * height]; rendered.CopyPixels(pixels, stride, 0);
        var thumbnail = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
        thumbnail.Freeze(); return thumbnail;
    }

    private int VisibleScreenshotSlots(int eligible)
    {
        int recordingSlots = recordingPreview is null ? 0 : 1;
        double height = previewWorkArea.Height / previewScale - 2 * (FloatingLayout.Edge + FloatingLayout.Margin);
        for (int slots = Math.Min(3 - recordingSlots, eligible); slots > 1; slots--)
        {
            double required = FloatingLayout.CardHeight + (slots - 1) * FloatingLayout.DeckPeek
                + recordingSlots * (FloatingLayout.CardHeight + FloatingLayout.Gap)
                + (eligible > slots ? FloatingLayout.OverflowHeight + FloatingLayout.Gap : 0);
            if (height >= required) return slots;
        }
        return Math.Min(1, eligible);
    }

    internal PendingScreenshot AddScreenshotPreview(BitmapSource image, CaptureEntry entry, string? warning = null, bool promote = true)
    {
        using var update = overlays.BeginUpdate();
        var placement = overlayPlacement(); previewWorkArea = placement.WorkArea; previewScale = placement.Scale;
        var item = new PendingScreenshot(entry, HasHistory(entry) ? null : image, warning);
        screenshotStack.Add(item, promote);
        if (screenshotWindows.TryGetValue(entry.Id, out var old)) RetirePreview(old);
        if (OverlaysHidden) OverlayShelfImage = image;
        RefreshPreviewStack(entry.Id, image);
        return item;
    }

    private void AdoptScreenshotPreview(ImageWindow window)
    {
        using var update = overlays.BeginUpdate();
        var entry = window.Entry!;
        if (previewWorkArea.IsEmpty) { var placement = overlayPlacement(); previewWorkArea = placement.WorkArea; previewScale = placement.Scale; }
        if (screenshotWindows.TryGetValue(entry.Id, out var old) && old != window) RetirePreview(old);
        var item = new PendingScreenshot(entry, HasHistory(entry) ? null : window.CaptureImage);
        screenshotStack.Add(item); window.PendingOwner = item;
        screenshotWindows[entry.Id] = window; preview = window;
        RefreshPreviewStack();
    }

    private void RetirePreview(ImageWindow window)
    {
        retiringPreviews.Add(window);
        try { window.Close(); }
        finally { retiringPreviews.Remove(window); }
    }

    private void PreviewClosed(ImageWindow window)
    {
        using var update = overlays.BeginUpdate();
        bool retired = retiringPreviews.Contains(window);
        bool managed = pins.Contains(window) || recordingPreview == window;
        if (window.Entry is { } entry && screenshotWindows.TryGetValue(entry.Id, out var current) && current == window)
        {
            managed = true;
            screenshotWindows.Remove(entry.Id);
            if (!retired && !Quitting)
            {
                if (editingScreenshots.ContainsKey(entry.Id)) dismissedWhileEditing.Add(entry.Id);
                screenshotStack.Dismiss(entry.Id);
            }
        }
        if (preview == window) preview = null;
        if (recordingPreview == window) recordingPreview = null;
        pins.Remove(window); overlays.Forget(window);
        if (managed && !retired && !Quitting) RefreshPreviewStack();
    }

    internal void DismissSharedPreview(ImageWindow window)
    {
        if (!PreviewPreferences.DismissAfterSharing || window.IsPinned || window.Recording is not null) return;
        if (window.PendingOwner is { } owner && OwnsPending(owner)) DismissPendingScreenshot(owner.Entry.Id, manual: false);
        else window.Close();
    }

    private void DismissPendingScreenshot(string id, bool manual)
    {
        using var update = overlays.BeginUpdate();
        if (manual && editingScreenshots.ContainsKey(id)) dismissedWhileEditing.Add(id);
        screenshotStack.Dismiss(id, manual);
        if (screenshotWindows.TryGetValue(id, out var window)) RetirePreview(window);
        RefreshPreviewStack();
    }

    public void ClearUnpinnedPreviews()
    {
        if (Quitting || capturing || Recording.IsActive || ComponentDispatcher.IsThreadModal) return;
        using (overlays.BeginUpdate())
        {
            foreach (string id in editingScreenshots.Keys) dismissedWhileEditing.Add(id);
            screenshotStack.Clear();
            foreach (var window in screenshotWindows.Values.ToArray()) RetirePreview(window);
            if (recordingPreview is not null) RetirePreview(recordingPreview);
        }
        RefreshPreviewStack();
        home?.SetStatus("Previews cleared. Pins and history are unchanged.");
    }

    public void ReopenLastDismissed()
    {
        if (Quitting || capturing || Recording.IsActive || ComponentDispatcher.IsThreadModal) return;
        var item = screenshotStack.TakeLastDismissed(p => Available(p) && !pins.Any(w => w.Entry?.Id == p.Entry.Id));
        if (item is null) { home?.SetStatus("No dismissed capture to reopen."); RefreshPreviewActions(); return; }
        try { AddScreenshotPreview(PendingImage(item), CurrentEntry(item), item.Warning); RestoreOverlays(); }
        catch (Exception ex) { screenshotStack.Add(item); screenshotStack.Dismiss(item.Entry.Id); Report(ex); }
    }

    internal void ShowPendingScreenshot(string id)
    {
        if (Quitting || capturing || Recording.IsActive || ComponentDispatcher.IsThreadModal) return;
        if (screenshotStack.Find(id) is not { } item) return;
        try
        {
            bool restoreFocus = screenshotWindows.Values.Any(window => window.IsKeyboardFocusWithin);
            AddScreenshotPreview(PendingImage(item), CurrentEntry(item), item.Warning);
            if (restoreFocus && presentNativeOverlays && screenshotWindows.TryGetValue(id, out var front) && front.IsVisible)
            { front.Activate(); front.RevealActions(true); front.MoveFocus(new TraversalRequest(FocusNavigationDirection.First)); }
        }
        catch (Exception ex) { Report(ex); }
    }

    internal void SelectDeckLayer(int layer)
    {
        var visible = VisibleScreenshotPreviews;
        if (layer > 0 && layer < visible.Count && visible[layer].Entry is { } entry) ShowPendingScreenshot(entry.Id);
    }

    private void RestackPreviewDeck()
    {
        if (!presentNativeOverlays || Quitting || capturing || OverlaysHidden || overlays.Suspended) return;
        foreach (var window in VisibleScreenshotPreviews.Reverse()) window.RaiseInDeck();
    }

    private void RefreshPreviewStack(string? readyId = null, BitmapSource? readyImage = null)
    {
        if (refreshingPreviews || Quitting) return;
        refreshingPreviews = true;
        try
        {
            using var update = overlays.BeginUpdate();
            // Overflow retains history references, not a full-resolution bitmap/window per capture.
            foreach (var item in screenshotStack.Pending.ToArray())
                if (!Available(item) && !editingScreenshots.ContainsKey(item.Entry.Id) && !screenshotWindows.ContainsKey(item.Entry.Id)) screenshotStack.Remove(item.Entry.Id);
            var eligible = screenshotStack.Pending.Where(p => !editingScreenshots.ContainsKey(p.Entry.Id)).ToArray();
            var visible = eligible.Take(VisibleScreenshotSlots(eligible.Length)).ToArray();
            var ids = visible.Select(p => p.Entry.Id).ToHashSet();
            foreach (var pair in screenshotWindows.ToArray())
                if (!ids.Contains(pair.Key))
                {
                    // Preserve an in-memory recovery image if the original vanished while its card was open.
                    if (screenshotStack.Find(pair.Key) is { } item && !Available(item))
                        screenshotStack.Add(new PendingScreenshot(item.Entry, pair.Value.CaptureImage, item.Warning), promote: false);
                    RetirePreview(pair.Value);
                }
            foreach (var item in visible)
            {
                if (!screenshotWindows.TryGetValue(item.Entry.Id, out var window))
                {
                    try
                    {
                        var image = item.Entry.Id == readyId && readyImage is not null ? readyImage : PendingImage(item);
                        window = FloatingWindow(image, item.Entry, false, item.Warning); window.PendingOwner = item; screenshotWindows.Add(item.Entry.Id, window);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or NotSupportedException)
                    { screenshotStack.Remove(item.Entry.Id); home?.SetStatus("A preview could not be opened. Check its entry in History.", true); continue; }
                }
                window.ConfigureDock(previewWorkArea, previewScale);
                overlays.Track(window);
            }
            var pending = visible.Where(p => screenshotWindows.ContainsKey(p.Entry.Id)).Select(p => screenshotWindows[p.Entry.Id]).ToList();
            for (int depth = 0; depth < pending.Count; depth++)
            {
                var window = pending[depth]; var entry = window.Entry!;
                window.ConfigureDeck(depth, () => ShowPendingScreenshot(entry.Id), $"Screenshot, {entry.TimeLabel}, {depth + 1} of {eligible.Length}. Bring to front.");
                window.SelectDeckLayer = SelectDeckLayer;
            }
            if (recordingPreview is not null) { recordingPreview.ConfigureDock(previewWorkArea, previewScale); pending.Insert(0, recordingPreview); }
            preview = pending.FirstOrDefault();
            overflowBounds = null;
            var groups = pins.Where(p => p.IsDocked && !p.DockWorkArea.IsEmpty).Reverse().GroupBy(p => p.DockWorkArea).ToDictionary(g => g.Key, g => g.ToArray());
            if (!previewWorkArea.IsEmpty && !groups.ContainsKey(previewWorkArea)) groups.Add(previewWorkArea, []);
            foreach (var group in groups)
            {
                bool shelf = group.Key == previewWorkArea;
                double scale = shelf ? previewScale : group.Value[0].DockScale;
                var layout = FloatingLayout.Stack(group.Key, shelf ? pending.Count : 0, group.Value.Length, shelf && PendingOverflowCount > 0, scale, separateRecording: shelf && recordingPreview is not null);
                for (int i = 0; i < layout.Pins.Count; i++) group.Value[i].Place(layout.Pins[i], scale);
                if (!shelf) continue;
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    bool fits = i < layout.Previews.Count; overlays.SetDesired(pending[i], fits);
                    if (fits) pending[i].Place(layout.Previews[i], scale);
                }
                overflowBounds = layout.Overflow;
            }
        }
        finally { refreshingPreviews = false; RestackPreviewDeck(); RefreshPreviewActions(); }
    }

    private void RefreshPreviewActions()
    {
        bool available = !Quitting && !capturing && !Recording.IsActive && !overlays.Suspended;
        if (reopenPreviewMenu is not null) reopenPreviewMenu.IsEnabled = available && CanReopenLastDismissed;
        if (clearPreviewMenu is not null) clearPreviewMenu.IsEnabled = available && (screenshotStack.Pending.Count > 0 || recordingPreview is not null);
        if (!presentNativeOverlays) return;
        if (PendingOverflowCount > 0 && overflowBounds is { } bounds)
        {
            previewOverflow ??= new PreviewOverflowWindow(this);
            var hidden = screenshotStack.Pending.Where(p => !editingScreenshots.ContainsKey(p.Entry.Id) && !screenshotWindows.ContainsKey(p.Entry.Id)).ToArray();
            previewOverflow.Configure(hidden, bounds, previewScale);
        }
        previewOverflow?.Present(available && !OverlaysHidden && PendingOverflowCount > 0 && overflowBounds is not null);
    }
}
