using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class CompositionChecks
{
    private static void Require(bool value, string message = "Composition assertion failed") { if (!value) throw new Exception(message); }
    private static void Reject(Action action)
    {
        try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException) { return; }
        throw new Exception("Invalid composition was accepted");
    }
    private static BitmapSource Image(int width = 120, int height = 80, Color? color = null)
    {
        Color fill = color ?? Colors.White; var bytes = new byte[width * height * 4];
        for (int i = 0; i < bytes.Length; i += 4) { bytes[i] = fill.B; bytes[i + 1] = fill.G; bytes[i + 2] = fill.R; bytes[i + 3] = fill.A; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bytes, width * 4); bitmap.Freeze(); return bitmap;
    }
    private static byte[] Pixels(BitmapSource source)
    { var image = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); var pixels = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels; }
    private static byte[] Pixel(BitmapSource source, int x, int y) => Pixels(source).Skip((y * source.PixelWidth + x) * 4).Take(4).ToArray();
    private static string FilePath(string root, string name)
    { string folder = Path.Combine(root, "composition"); Directory.CreateDirectory(folder); return Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + name); }
    private static void Alter(string input, string output, Action<JsonObject> change, string? extra = null, string? skip = null)
    {
        using var source = ZipFile.OpenRead(input); using var destination = ZipFile.Open(output, ZipArchiveMode.Create);
        foreach (var entry in source.Entries)
        {
            if (entry.FullName == skip) continue;
            using var read = entry.Open(); using var write = destination.CreateEntry(entry.FullName).Open();
            if (entry.FullName == "project.json") { var metadata = JsonNode.Parse(read)!.AsObject(); change(metadata); using var writer = new StreamWriter(write); writer.Write(metadata.ToJsonString()); }
            else read.CopyTo(write);
        }
        if (extra is not null) { using var writer = new StreamWriter(destination.CreateEntry(extra).Open()); writer.Write("unexpected"); }
    }
    public static void Run(string output, Action<string, Action> test)
    {
        test("Seven text presets render distinct output and use font metrics", () =>
        {
            Require(Enum.GetValues<TextPreset>().Length == 7);
            var hashes = new HashSet<string>();
            foreach (var preset in Enum.GetValues<TextPreset>())
            {
                var document = new EditDocument(Image(240, 100));
                var mark = new Mark(MarkKind.Text, [new(15, 15)], Text: "Caption", FontSize: 26, TextPreset: preset);
                document.Add(mark); hashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Pixels(EditRenderer.Render(document)))));
                Require(mark.Bounds.Width > 50 && mark.Bounds.Height >= 26);
            }
            Require(hashes.Count == 7, "Text styles are not visually distinct");
            var narrow = new Mark(MarkKind.Text, [new(0, 0)], Text: "iii", FontSize: 24);
            Require((narrow with { Text = "WWW" }).Bounds.Width > narrow.Bounds.Width * 2, "Text bounds still use a length heuristic");
        });
        test("Check, tick and cross symbols are rendered and persist as editable text", () =>
        {
            foreach (string symbol in new[] { "√", "✓", "×" })
            {
                var document = new EditDocument(Image()); document.Add(new Mark(MarkKind.Text, [new(20, 20)], Text: symbol, FontSize: 32));
                Require(!Pixels(document.Original).SequenceEqual(Pixels(EditRenderer.Render(document))));
                string path = FilePath(output, "symbol.snipsera"); EditFiles.SaveProject(document, path);
                Require(EditFiles.LoadProject(path).State.Marks.Single().Text == symbol);
            }
        });
        test("White and yellow labels and callouts retain readable contrasting text", () =>
        {
            foreach (string color in new[] { "#FFFFFF", "#FFD561" })
            foreach (var preset in new[] { TextPreset.SolidLabel, TextPreset.Callout, TextPreset.LightLabel, TextPreset.Highlight })
            {
                var mark = new Mark(MarkKind.Text, [new(12, 12)], color, Text: "Readable", FontSize: 24, TextPreset: preset);
                Color ink = TextAnnotation.Foreground(mark).Color;
                Require(ink.R < 140 && ink.G < 140, $"Light {preset} uses pale text");
                var document = new EditDocument(Image(240, 100)); document.Add(mark);
                byte[] pixels = Pixels(EditRenderer.Render(document));
                int darkPixels = Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] < 140 && pixels[i * 4 + 1] < 140 && pixels[i * 4 + 2] < 140);
                Require(darkPixels > 60, $"{preset} has no readable dark glyphs");
            }
        });
        test("Light outlined text uses a dark outline and Plain retains legacy metrics", () =>
        {
            foreach (string color in new[] { "#FFFFFF", "#FFD561" })
            {
                var document = new EditDocument(Image(240, 100));
                document.Add(new Mark(MarkKind.Text, [new(10, 10)], color, Text: "Outline", FontSize: 26, TextPreset: TextPreset.Outline));
                byte[] pixels = Pixels(EditRenderer.Render(document));
                Require(Enumerable.Range(0, pixels.Length / 4).Count(i => pixels[i * 4] < 100 && pixels[i * 4 + 1] < 100 && pixels[i * 4 + 2] < 100) > 50);
            }
            var plain = new Mark(MarkKind.Text, [new(0, 0)], Text: "Legacy", FontSize: 24);
            var legacy = new FormattedText(plain.Text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), plain.FontSize, Brushes.Black, 1);
            Require(TextAnnotation.Typeface(plain).Weight == FontWeights.Normal && TextAnnotation.Insets(plain) == new Thickness(0));
            Require(Math.Abs(TextAnnotation.Format(plain).Width - legacy.Width) < .01);
        });
        test("Negative image placement expands composition and coordinate transforms round trip", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(20, 30, Colors.Blue), new Point(-30, -10));
            Require(document.State.IsComposition && document.State.ContentBounds == new Rect(-30, -10, 150, 90));
            Require(document.State.CanvasWidth == 150 && document.State.CanvasHeight == 90);
            var source = new Point(-10, 15); Require(document.State.ToOriginal(document.State.ToCanvas(source)) == source);
            var rendered = EditRenderer.Render(document);
            Require(Pixel(rendered, 5, 5)[0] == 255 && Pixel(rendered, 5, 5)[2] == 0);
            Require(Pixel(rendered, 35, 20).Take(3).All(b => b == 255));
        });
        test("Image layer move resize undo and redo retain original pixels", () =>
        {
            var original = Image(); var document = new EditDocument(original);
            int index = document.AddImage(Image(20, 10, Colors.Red), new Point(15, 15));
            var initial = Pixels(EditRenderer.Render(document));
            document.ReplaceImage(index, document.State.Images[index] with { X = 40, Y = 20, Width = 40, Height = 20 });
            var moved = Pixels(EditRenderer.Render(document)); Require(!moved.SequenceEqual(initial));
            document.Undo(); Require(Pixels(EditRenderer.Render(document)).SequenceEqual(initial));
            document.Redo(); Require(Pixels(EditRenderer.Render(document)).SequenceEqual(moved));
            Require(Pixels(original).SequenceEqual(Pixels(document.Original)));
        });
        test("Composition supports annotations over imported images after the image is removed", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(50, 50, Colors.Blue), new Point(-60, 0));
            document.Add(new Mark(MarkKind.Text, [new(-50, 10)], Text: "Keep", FontSize: 12));
            document.RemoveImage(0); Require(document.State.Images.Length == 0 && document.State.ContentBounds.Left <= -50);
            string path = FilePath(output, "remaining-text.snipsera"); EditFiles.SaveProject(document, path);
            var loaded = EditFiles.LoadProject(path); Require(loaded.State.IsComposition && loaded.State.Marks.Single().Text == "Keep");
            Require(Pixels(EditRenderer.Render(document)).SequenceEqual(Pixels(EditRenderer.Render(loaded))));
        });
        test("Layer source replacement is rejected without changing state", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(10, 10), new Point(5, 5)); var state = document.State;
            Reject(() => document.ReplaceImage(0, state.Images[0] with { Id = new string('a', 32) })); Require(document.State == state);
            Reject(() => document.ReplaceImage(0, state.Images[0] with { Width = double.NaN })); Require(document.State == state);
            Reject(() => document.AddImage(Image(10, 10), new Point(double.PositiveInfinity, 0))); Require(document.State == state);
        });
        test("Image ordering changes overlap and is undoable", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(20, 20, Colors.Red), new Point(10, 10));
            document.AddImage(Image(20, 20, Colors.Blue), new Point(10, 10));
            Require(Pixel(EditRenderer.Render(document), 15, 15)[0] == 255);
            document.MoveImage(0, 1); Require(Pixel(EditRenderer.Render(document), 15, 15)[2] == 255);
            document.Undo(); Require(Pixel(EditRenderer.Render(document), 15, 15)[0] == 255);
            var state = document.State; Reject(() => document.MoveImage(0, 3)); Require(document.State == state);
        });
        test("Added image count is bounded and redo keeps embedded sources", () =>
        {
            var document = new EditDocument(Image()); var small = Image(2, 2);
            for (int i = 0; i < EditDocument.MaximumImages; i++) document.AddImage(small, new Point(i, i));
            Reject(() => document.AddImage(small, new Point())); Require(document.State.Images.Length == EditDocument.MaximumImages);
            string id = document.State.Images[^1].Id; document.Undo(); document.Redo();
            Require(document.GetImage(id).PixelWidth == 2 && document.State.Images[^1].Id == id);
        });
        test("Embedded images and text styles survive a self contained project round trip", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(30, 40, Colors.Blue), new Point(130, 5));
            document.Add(new Mark(MarkKind.Text, [new(130, 10)], Text: "A", TextPreset: TextPreset.Callout));
            document.SetBackground(12, "#FFFFFF", new BackgroundOptions("Aurora", 16.0 / 9, CornerRadius: 4, Shadow: true));
            string path = FilePath(output, "combined.snipsera"); EditFiles.SaveProject(document, path); var loaded = EditFiles.LoadProject(path);
            Require(Pixels(EditRenderer.Render(document)).SequenceEqual(Pixels(EditRenderer.Render(loaded))));
            using var archive = ZipFile.OpenRead(path); Require(archive.Entries.Count == 3 && archive.Entries.Any(e => e.FullName.StartsWith("images/")));
            string png = FilePath(output, "combined.png"); EditFiles.Export(loaded, png, false);
            Require(Pixels(EditFiles.LoadImage(png)).SequenceEqual(Pixels(EditRenderer.Render(loaded))));
        });
        test("Project loader rejects missing embedded images and path bearing extras", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(30, 40), new Point(10, 10));
            string path = FilePath(output, "valid.snipsera"); EditFiles.SaveProject(document, path);
            string missing = FilePath(output, "missing.snipsera"); Alter(path, missing, _ => { }, skip: "images/" + document.State.Images[0].Id + ".png"); Reject(() => EditFiles.LoadProject(missing));
            string traversal = FilePath(output, "path.snipsera"); Alter(path, traversal, _ => { }, extra: "../outside.png"); Reject(() => EditFiles.LoadProject(traversal));
            string id = FilePath(output, "id.snipsera"); Alter(path, id, j => j["State"]!["Layers"]![0]!["Id"] = "../../outside"); Reject(() => EditFiles.LoadProject(id));
        });
        test("Version three projects without new properties still load", () =>
        {
            var document = new EditDocument(Image()); document.Add(new Mark(MarkKind.Text, [new(10, 10)], Text: "Legacy"));
            string original = FilePath(output, "modern.snipsera"), old = FilePath(output, "legacy.framelet"); EditFiles.SaveProject(document, original);
            Alter(original, old, j => { j["Version"] = 3; var state = j["State"]!.AsObject(); state.Remove("Layers"); state.Remove("Backdrop"); state.Remove("IsComposition"); foreach (var mark in state["Marks"]!.AsArray()) mark!.AsObject().Remove("TextPreset"); });
            var loaded = EditFiles.LoadProject(old); Require(!loaded.State.IsComposition && loaded.State.Images.Length == 0 && loaded.State.Marks[0].TextPreset == TextPreset.Plain);
            Require(Pixels(EditRenderer.Render(loaded)).SequenceEqual(Pixels(EditRenderer.Render(document))));
        });
        test("Background aspect ratios add space without stretching screenshots", () =>
        {
            var document = new EditDocument(Image(120, 80)); document.SetBackground(10, "#112233", new BackgroundOptions("Solid", 1));
            Require(document.State.CanvasWidth == 140 && document.State.CanvasHeight == 140);
            Require(document.State.ContentRect == new Rect(10, 30, 120, 80));
            Require(document.State.ToOriginal(document.State.ToCanvas(new Point(20, 30))) == new Point(20, 30));
            document.SetBackground(10, "#112233", new BackgroundOptions("Solid", 1, AlignmentY: 1));
            Require(document.State.ContentRect.Y == 50 && document.State.ContentRect.Height == 80);
        });
        test("Background gradients vary across canvas and preserve content pixels", () =>
        {
            Require(CompositionLayout.Presets.Length == 16 && CompositionLayout.Presets.Distinct().Count() == 16);
            foreach (string preset in CompositionLayout.Presets.Where(p => p != "Solid"))
            {
                var document = new EditDocument(Image()); document.SetBackground(20, "#FFFFFF", new BackgroundOptions(preset));
                var image = EditRenderer.Render(document); Require(!Pixel(image, 2, 2).SequenceEqual(Pixel(image, image.PixelWidth - 3, image.PixelHeight - 3)));
                Require(Pixel(image, 50, 50).Take(3).All(b => b == 255));
            }
            var nebula = CompositionLayout.BackgroundBrush(new EditDocument(Image()).State with { Backdrop = new BackgroundOptions("Nebula") }) as LinearGradientBrush;
            Require(nebula?.GradientStops.Count == 3 && nebula.GradientStops[1].Offset == .5);
        });
        test("Auto balance trims uniform margins reversibly and keeps annotations", () =>
        {
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 120, 80)); dc.DrawRectangle(Brushes.Black, null, new Rect(30, 20, 50, 30)); }
            var original = new RenderTargetBitmap(120, 80, 96, 96, PixelFormats.Pbgra32); original.Render(visual); original.Freeze();
            var document = new EditDocument(original); document.Add(new Mark(MarkKind.Rectangle, [new(8, 8), new(20, 16)], Width: 2));
            var before = document.State; Require(document.AutoBalance()); Require(document.State.Crop.Left <= 6 && document.State.Crop.Top <= 6 && document.State.Crop.Right >= 80);
            document.Undo(); Require(document.State == before);
            var blank = new EditDocument(Image()); Require(!blank.AutoBalance());
        });
        test("Auto balance centers an already tight screenshot", () =>
        {
            var document = new EditDocument(Image()); document.SetBackground(10, "#FFFFFF", new BackgroundOptions("Solid", 1, AlignmentY: 0));
            Require(document.AutoBalance()); Require(document.State.Backdrop?.AlignmentY == .5 && document.State.ContentRect.Y == 30);
        });
        test("Invalid background input is rejected atomically", () =>
        {
            var document = new EditDocument(Image()); var initial = document.State;
            Reject(() => document.SetBackground(10, "#FFFFFF", new BackgroundOptions("Unknown")));
            Reject(() => document.SetBackground(10, "#FFFFFF", new BackgroundOptions(AspectRatio: double.NaN)));
            Reject(() => document.SetBackground(10, "#FFFFFF", new BackgroundOptions(AlignmentX: 2)));
            Reject(() => document.SetBackground(10, "#FFFFFF", new BackgroundOptions(CornerRadius: -1)));
            Require(document.State == initial && !document.CanUndo);
        });
        test("Vector drag preview keeps the current layout without committing geometry", () =>
        {
            var document = new EditDocument(Image()); document.AddImage(Image(10, 10, Colors.Blue), new Point(10, 10));
            var state = document.State; var changed = state with { Layers = state.Images.SetItem(0, state.Images[0].Offset(-15, 0)) };
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen()) EditRenderer.DrawDocument(dc, document, changed, layoutState: state);
            Require(document.State == state && changed.ContentBounds.Left == -5 && state.ContentBounds.Left == 0);
            var bitmap = new RenderTargetBitmap(120, 80, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual);
            Require(Pixel(bitmap, 2, 15)[0] == 255 && Pixel(bitmap, 2, 15)[2] == 0);
        });
    }
}
