using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class EditorRenderCacheChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static BitmapSource Pattern(int width = 320, int height = 240, int seed = 0)
    {
        var pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        {
            int p = (y * width + x) * 4;
            pixels[p] = (byte)(x * 7 + seed); pixels[p + 1] = (byte)(y * 11 + seed * 3);
            pixels[p + 2] = (byte)((x ^ y) + seed); pixels[p + 3] = 255;
        }
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4); image.Freeze(); return image;
    }
    private static byte[] Pixels(BitmapSource image)
    {
        var converted = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var result = new byte[converted.PixelWidth * converted.PixelHeight * 4]; converted.CopyPixels(result, converted.PixelWidth * 4, 0); return result;
    }
    private static void MatchesFresh(EditDocument document, BitmapSource cached, int replacementIndex = -1, Mark? replacement = null)
    {
        var state = document.State;
        if (replacement is not null) state = state with { Marks = state.Marks.SetItem(replacementIndex, replacement) };
        var fresh = new EditDocument(document.Original, state, document.ImageAssets);
        Require(Pixels(cached).SequenceEqual(Pixels(EditRenderer.Render(fresh))), "Cached mask changed exported pixels");
    }
    private static void Draw(EditDocument document, int index, Mark replacement)
    {
        var visual = new DrawingVisual(); using var dc = visual.RenderOpen();
        EditRenderer.DrawDocument(dc, document, replacementIndex: index, replacement: replacement);
    }
    public static void Run(Action<string, Action> test)
    {
        test("Cached blur and pixelation preserve fresh-render pixels", () =>
        {
            foreach (var kind in new[] { MarkKind.Blur, MarkKind.Pixelate })
            {
                var doc = new EditDocument(Pattern()); doc.Add(new Mark(kind, [new(15, 20), new(290, 220)]));
                var first = EditRenderer.Render(doc); var cached = EditRenderer.Render(doc);
                Require(Pixels(first).SequenceEqual(Pixels(cached)), "Second render changed pixels");
                Require(EditRenderer.ObscureCacheMetrics(doc) is { Hits: 1, Misses: 1 }, "Unchanged mask was recomputed");
                MatchesFresh(doc, cached);
            }
        });
        test("Moving a later annotation reuses unchanged masks without stale output", () =>
        {
            var doc = new EditDocument(Pattern()); doc.Add(new Mark(MarkKind.Blur, [new(0, 0), new(300, 225)]));
            doc.Add(new Mark(MarkKind.Text, [new(25, 35)], Text: "Move me")); _ = EditRenderer.Render(doc);
            var moved = doc.State.Marks[1].Offset(90, 60); var actual = EditRenderer.Render(doc, 1, moved);
            Require(EditRenderer.ObscureCacheMetrics(doc) is { Hits: 1, Misses: 1 }, "Later annotation invalidated its earlier blur");
            MatchesFresh(doc, actual, 1, moved);
        });
        test("Changed preceding marks and mask geometry invalidate dependent cached effects", () =>
        {
            var doc = new EditDocument(Pattern()); doc.Add(new Mark(MarkKind.Redact, [new(30, 30), new(80, 90)]));
            doc.Add(new Mark(MarkKind.Blur, [new(10, 10), new(260, 210)]));
            doc.Add(new Mark(MarkKind.Pixelate, [new(40, 40), new(290, 220)])); _ = EditRenderer.Render(doc);
            var moved = doc.State.Marks[0].Offset(100, 40); var actual = EditRenderer.Render(doc, 0, moved);
            Require(EditRenderer.ObscureCacheMetrics(doc).Misses == 4, "Prefix edits reused old effect pixels"); MatchesFresh(doc, actual, 0, moved);
            var mask = doc.State.Marks[1].Offset(20, 0); actual = EditRenderer.Render(doc, 1, mask);
            Require(EditRenderer.ObscureCacheMetrics(doc).Misses == 6, "Mask movement reused stale geometry or its dependent effect"); MatchesFresh(doc, actual, 1, mask);
        });
        test("Image order positions and rounded crop invalidate cached masks", () =>
        {
            var doc = new EditDocument(Pattern()); doc.AddImage(Pattern(100, 100, 7), new Point(20, 20)); doc.AddImage(Pattern(100, 100, 17), new Point(50, 50));
            doc.Add(new Mark(MarkKind.Pixelate, [new(0, 0), new(280, 210)])); _ = EditRenderer.Render(doc);
            doc.MoveImage(0, 1); var result = EditRenderer.Render(doc); MatchesFresh(doc, result);
            Require(EditRenderer.ObscureCacheMetrics(doc).Misses == 2, "Layer order did not invalidate the mask");
            doc.ReplaceImage(0, doc.State.Images[0].Offset(80, 0)); result = EditRenderer.Render(doc); MatchesFresh(doc, result);
            Require(EditRenderer.ObscureCacheMetrics(doc).Misses == 3, "Moved layer did not invalidate the mask");
            doc.SetBackground(12, "#FFFFFF", new BackgroundOptions(CornerRadius: 30)); result = EditRenderer.Render(doc); MatchesFresh(doc, result);
            Require(EditRenderer.ObscureCacheMetrics(doc).Misses == 4, "Rounded clipping did not invalidate the mask");
        });
        test("Mask cache respects a bounded memory budget across many edits", () =>
        {
            var doc = new EditDocument(Pattern(2048, 1100)); doc.Add(new Mark(MarkKind.Pixelate, [new(0, 0), new(1536, 1024)]));
            for (int i = 0; i < 8; i++)
            {
                doc.Replace(0, new Mark(MarkKind.Pixelate, [new(i * 50, 0), new(i * 50 + 1536, 1024)]));
                _ = EditRenderer.Render(doc);
                Require(EditRenderer.ObscureCacheMetrics(doc).Bytes <= EditRenderer.ObscureCacheByteLimit, "Retained masks exceeded the byte limit");
            }
            var metrics = EditRenderer.ObscureCacheMetrics(doc);
            Require(metrics.Entries < 8 && metrics.Entries > 0 && metrics.Bytes > 0, "Cache did not evict old mask bitmaps");
            _ = EditRenderer.Render(doc); Require(EditRenderer.ObscureCacheMetrics(doc).Hits == 1, "Newest retained mask was not reused");
        });
        test("Mask caches remain isolated between documents and reduce repeated gesture work", () =>
        {
            var doc = new EditDocument(Pattern(1200, 900)); doc.Add(new Mark(MarkKind.Blur, [new(0, 0), new(1100, 800)]));
            doc.Add(new Mark(MarkKind.Text, [new(25, 35)], Text: "Keep moving"));
            var clock = Stopwatch.StartNew(); Draw(doc, 1, doc.State.Marks[1]); double first = clock.Elapsed.TotalMilliseconds;
            var timings = new double[5];
            for (int i = 0; i < timings.Length; i++) { clock.Restart(); Draw(doc, 1, doc.State.Marks[1].Offset(i * 10, 0)); timings[i] = clock.Elapsed.TotalMilliseconds; }
            Require(EditRenderer.ObscureCacheMetrics(doc) is { Hits: 5, Misses: 1 }, "Repeated gesture rebuilt its invariant blur");
            Console.WriteLine($"Blur gesture drawing 1100x800: cold {first:F2} ms; cached median {timings.Order().ElementAt(2):F2} ms");
            var other = new EditDocument(Pattern(1200, 900, 41), doc.State); var actual = EditRenderer.Render(other);
            Require(EditRenderer.ObscureCacheMetrics(other) is { Hits: 0, Misses: 1 }, "Document reused another document's bitmap"); MatchesFresh(other, actual);
        });
    }
}
