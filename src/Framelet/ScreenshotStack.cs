using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace Framelet;

internal sealed class PendingScreenshot(CaptureEntry entry, BitmapSource? recoveryImage = null, string? warning = null)
{
    internal CaptureEntry Entry { get; } = entry;
    internal BitmapSource? RecoveryImage { get; } = recoveryImage;
    internal string? Warning { get; } = warning;
    internal bool SuppressEditorRestore { get; set; }
}

// Both lists are newest-first. Ordinary pending captures retain metadata only; recovery pixels are reserved for unsaved data.
internal sealed class ScreenshotStack
{
    private readonly List<PendingScreenshot> pending = [];
    private readonly List<PendingScreenshot> dismissed = [];
    internal IReadOnlyList<PendingScreenshot> Pending => pending;
    internal IReadOnlyList<PendingScreenshot> Dismissed => dismissed;

    internal PendingScreenshot? Find(string id) => pending.Find(item => item.Entry.Id == id);

    internal void ReplaceDismissed(PendingScreenshot original, PendingScreenshot replacement)
    {
        int index = dismissed.FindIndex(item => ReferenceEquals(item, original));
        if (index >= 0) dismissed[index] = replacement;
    }

    internal void Add(PendingScreenshot item, bool promote = true)
    {
        int index = pending.FindIndex(existing => existing.Entry.Id == item.Entry.Id);
        if (index >= 0 && !promote) pending[index] = item;
        else
        {
            if (index >= 0) pending.RemoveAt(index);
            pending.Insert(0, item);
        }
        dismissed.RemoveAll(existing => existing.Entry.Id == item.Entry.Id);
    }

    // Pinning and presentation changes remove ownership without recording a dismissal or changing editor intent.
    internal PendingScreenshot? Remove(string id)
    {
        int index = pending.FindIndex(item => item.Entry.Id == id);
        if (index < 0) return null;
        var item = pending[index]; pending.RemoveAt(index); return item;
    }

    internal PendingScreenshot? Dismiss(string id, bool manual = true)
    {
        var item = Remove(id);
        if (item is null) return null;
        if (manual) item.SuppressEditorRestore = true;
        dismissed.RemoveAll(existing => existing.Entry.Id == id);
        dismissed.Insert(0, item);
        if (dismissed.Count > HistoryStore.Capacity) dismissed.RemoveRange(HistoryStore.Capacity, dismissed.Count - HistoryStore.Capacity);
        return item;
    }

    internal void Clear()
    {
        while (pending.Count > 0) Dismiss(pending[^1].Entry.Id);
    }

    // Return ownership to the caller, who can load/reopen it before adding it to Pending. Existing pending cards never move.
    internal PendingScreenshot? TakeLastDismissed(Func<PendingScreenshot, bool> available)
    {
        while (dismissed.Count > 0)
        {
            var item = dismissed[0];
            if (Find(item.Entry.Id) is not null) { dismissed.RemoveAt(0); continue; }
            bool canOpen = available(item); // If checking fails, preserve the item for a later retry.
            dismissed.RemoveAt(0);
            if (canOpen) return item;
        }
        return null;
    }

    internal int OverflowCount(int visibleCount)
    {
        if (visibleCount < 0) throw new ArgumentOutOfRangeException(nameof(visibleCount));
        return Math.Max(0, pending.Count - visibleCount);
    }
}
