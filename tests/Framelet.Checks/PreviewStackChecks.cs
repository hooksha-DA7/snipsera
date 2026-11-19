using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

// Pure ownership and synthetic image data. No application, visible windows, files from user history or desktop input.
internal static class PreviewStackChecks
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static PendingScreenshot Item(int id) => new(new CaptureEntry(id.ToString("x32"), DateTimeOffset.UnixEpoch.AddSeconds(id), "Area", 64, 48));
    private static string[] Ids(IReadOnlyList<PendingScreenshot> items) => items.Select(item => item.Entry.Id).ToArray();

    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); int failures = 0; var outcomes = new List<object>();
        void Check(string name, Action check)
        {
            try { check(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        Check("Pending captures retain newest-first identity and bounded visible overflow counts", () =>
        {
            var stack = new ScreenshotStack(); var first = Item(1); var second = Item(2); var third = Item(3);
            stack.Add(first); stack.Add(second); stack.Add(third);
            Require(Ids(stack.Pending).SequenceEqual(new[] { third.Entry.Id, second.Entry.Id, first.Entry.Id }), "New captures are not newest-first");
            Require(ReferenceEquals(stack.Find(second.Entry.Id), second) && stack.Find("absent") is null, "Lookup changed capture identity");
            Require(stack.OverflowCount(0) == 3 && stack.OverflowCount(1) == 2 && stack.OverflowCount(3) == 0 && stack.OverflowCount(10) == 0, "Overflow count is incorrect");
        });
        Check("Replacing duplicate captures preserves or promotes their position without duplicating them", () =>
        {
            var stack = new ScreenshotStack(); var first = Item(1); var second = Item(2); var third = Item(3);
            stack.Add(first); stack.Add(second); stack.Add(third);
            var replacement = new PendingScreenshot(second.Entry with { Width = 99 }); stack.Add(replacement, promote: false);
            Require(stack.Pending.Count == 3 && ReferenceEquals(stack.Pending[1], replacement) && stack.Pending[1].Entry.Width == 99, "Nonpromoting replacement moved or duplicated a capture");
            stack.Add(first);
            Require(Ids(stack.Pending).SequenceEqual(new[] { first.Entry.Id, third.Entry.Id, second.Entry.Id }), "Promoting replacement did not move exactly one capture");
        });
        Check("Removing a pin candidate neither remembers dismissal nor suppresses editor restoration", () =>
        {
            var stack = new ScreenshotStack(); var item = Item(1); stack.Add(item);
            Require(ReferenceEquals(stack.Remove(item.Entry.Id), item), "Pin removal lost identity");
            Require(stack.Pending.Count == 0 && stack.Dismissed.Count == 0 && !item.SuppressEditorRestore, "Pin removal was treated as manual dismissal");
            Require(stack.Remove("missing") is null && stack.Dismiss("missing") is null, "Missing removal manufactured an item");
        });
        Check("Manual and successful-sharing dismissals retain distinct editor restoration intent", () =>
        {
            var stack = new ScreenshotStack(); var shared = Item(1); var manual = Item(2);
            stack.Add(shared); stack.Add(manual);
            Require(ReferenceEquals(stack.Dismiss(shared.Entry.Id, manual: false), shared) && !shared.SuppressEditorRestore, "Sharing suppressed future editor updates");
            Require(ReferenceEquals(stack.Dismiss(manual.Entry.Id), manual) && manual.SuppressEditorRestore, "Manual dismissal did not suppress editor resurrection");
            Require(Ids(stack.Dismissed).SequenceEqual(new[] { manual.Entry.Id, shared.Entry.Id }), "Dismissed captures are not LIFO");
        });
        Check("Clear dismisses oldest first so reopen returns newest without replacing pending captures", () =>
        {
            var stack = new ScreenshotStack(); var items = new[] { Item(1), Item(2), Item(3) };
            foreach (var item in items) stack.Add(item); stack.Clear();
            Require(stack.Pending.Count == 0 && items.All(item => item.SuppressEditorRestore), "Clear left pending captures or editor resurrection intent");
            var current = Item(4); stack.Add(current);
            var restored = stack.TakeLastDismissed(_ => true);
            Require(ReferenceEquals(restored, items[2]) && restored!.SuppressEditorRestore, "Reopen changed identity or manual intent");
            Require(stack.Pending.Count == 1 && ReferenceEquals(stack.Pending[0], current), "Taking a dismissed item replaced current pending work");
            stack.Add(restored!);
            Require(stack.Pending.Count == 2 && ReferenceEquals(stack.Pending[0], restored) && ReferenceEquals(stack.Pending[1], current), "Reopening dropped current work");
            Require(ReferenceEquals(stack.TakeLastDismissed(_ => true), items[1]), "Clear did not retain chronological reopen order");
        });
        Check("Reopening skips unavailable captures and exhausts stale entries without touching pending work", () =>
        {
            var stack = new ScreenshotStack(); var available = Item(1); var stale = Item(2); var current = Item(3);
            stack.Add(available); stack.Dismiss(available.Entry.Id); stack.Add(stale); stack.Dismiss(stale.Entry.Id); stack.Add(current);
            var visited = new List<string>();
            var restored = stack.TakeLastDismissed(item => { visited.Add(item.Entry.Id); return item == available; });
            Require(ReferenceEquals(restored, available) && visited.SequenceEqual(new[] { stale.Entry.Id, available.Entry.Id }), "Stale captures prevented reaching an available one");
            Require(stack.Dismissed.Count == 0 && ReferenceEquals(stack.Pending.Single(), current), "Stale cleanup changed current pending work");
            stack.Add(stale); stack.Dismiss(stale.Entry.Id);
            Require(stack.TakeLastDismissed(_ => false) is null && stack.Dismissed.Count == 0, "Unavailable captures were not skipped");
        });
        Check("Adding a dismissed identity removes it from reopen history and repeated dismissal stays unique", () =>
        {
            var stack = new ScreenshotStack(); var item = Item(1); stack.Add(item); stack.Dismiss(item.Entry.Id);
            var replacement = new PendingScreenshot(item.Entry with { Height = 72 }); stack.Add(replacement, promote: false);
            Require(stack.Dismissed.Count == 0 && ReferenceEquals(stack.Pending.Single(), replacement), "Re-added identity remained duplicated in dismissed history");
            stack.Dismiss(replacement.Entry.Id); stack.Add(replacement); stack.Dismiss(replacement.Entry.Id);
            Require(stack.Dismissed.Count == 1 && ReferenceEquals(stack.TakeLastDismissed(_ => true), replacement), "Repeated dismissal duplicated or changed identity");
        });
        Check("Dismissed history is bounded while pending data is not silently evicted", () =>
        {
            var stack = new ScreenshotStack(); int count = HistoryStore.Capacity + 25;
            for (int id = 1; id <= count; id++) stack.Add(Item(id));
            Require(stack.Pending.Count == count, "Adding captures silently discarded pending data");
            stack.Clear();
            Require(stack.Dismissed.Count == HistoryStore.Capacity && stack.Dismissed[0].Entry.Id == Item(count).Entry.Id && stack.Dismissed[^1].Entry.Id == Item(26).Entry.Id, "Dismissed history did not retain the latest bounded captures");
            Require(stack.TakeLastDismissed(_ => true)!.Entry.Id == Item(count).Entry.Id && stack.Dismissed.Count == HistoryStore.Capacity - 1, "LIFO pop did not remove exactly one item");
        });
        Check("Recovery pixels and warnings survive dismissal and reopen without copying or losing their identity", () =>
        {
            var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[] { 10, 20, 30, 255 }, 4); image.Freeze();
            var item = new PendingScreenshot(Item(1).Entry, image, "History save failed"); var stack = new ScreenshotStack();
            stack.Add(item); stack.Dismiss(item.Entry.Id, manual: false);
            var restored = stack.TakeLastDismissed(candidate => candidate.RecoveryImage is not null);
            Require(ReferenceEquals(restored, item) && ReferenceEquals(restored!.RecoveryImage, image) && restored.Warning == "History save failed" && !restored.SuppressEditorRestore, "Recovery data or editor intent changed");
        });
        Check("An availability-check failure leaves the dismissed item available for retry", () =>
        {
            var stack = new ScreenshotStack(); var item = Item(1); stack.Add(item); stack.Dismiss(item.Entry.Id);
            try { stack.TakeLastDismissed(_ => throw new IOException("Synthetic lookup failure")); throw new Exception("Lookup failure was swallowed"); }
            catch (IOException) { }
            Require(ReferenceEquals(stack.Dismissed.Single(), item) && ReferenceEquals(stack.TakeLastDismissed(_ => true), item), "Failed lookup discarded the last dismissed capture");
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
