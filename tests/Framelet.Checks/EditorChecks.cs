using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class EditorChecks
{
    private static void Require(bool condition, string message = "Editor assertion failed") { if (!condition) throw new Exception(message); }
    private static void Reject(Action action) { try { action(); } catch (Exception ex) when (ex is ArgumentException or InvalidDataException or System.Text.Json.JsonException) { return; } throw new Exception("Invalid editor input was accepted"); }
    private static BitmapSource Pattern(bool white = false, bool alpha = false)
    {
        const int width = 160, height = 120; byte[] pixels = new byte[width * height * 4];
        for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
        { int i = (y * width + x) * 4; pixels[i] = white ? (byte)255 : (byte)x; pixels[i + 1] = white ? (byte)255 : (byte)y; pixels[i + 2] = white ? (byte)255 : (byte)120; pixels[i + 3] = alpha ? (byte)0 : (byte)255; }
        var bitmap = BitmapSource.Create(width, height, 144, 144, PixelFormats.Bgra32, null, pixels, width * 4); bitmap.Freeze(); return bitmap;
    }
    private static byte[] Pixels(BitmapSource source)
    { var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0); byte[] data = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4]; bitmap.CopyPixels(data, bitmap.PixelWidth * 4, 0); return data; }
    private static byte[] Pixel(BitmapSource image, int x, int y) => Pixels(image).Skip((y * image.PixelWidth + x) * 4).Take(4).ToArray();
    private static Mark Box(MarkKind kind, double x = 20, double y = 20, double right = 100, double bottom = 80) => new(kind, [new(x, y), new(right, bottom)], "#EB4D3D", 4);
    private static string Folder(string root) { string path = Path.Combine(root, "editor-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }
    private static void AlterProject(string source, string target, Action<JsonObject> change, string? extra = null)
    {
        using var read = ZipFile.OpenRead(source); using var write = ZipFile.Open(target, ZipArchiveMode.Create);
        foreach (var entry in read.Entries)
        {
            using var input = entry.Open(); using var output = write.CreateEntry(entry.FullName).Open();
            if (entry.FullName == "project.json")
            { var node = JsonNode.Parse(input)!.AsObject(); change(node); using var writer = new StreamWriter(output); writer.Write(node.ToJsonString()); }
            else input.CopyTo(output);
        }
        if (extra is not null) { using var writer = new StreamWriter(write.CreateEntry(extra).Open()); writer.Write("untrusted"); }
    }
    public static void Run(string output, Action<string, Action> test, Action<int> pump)
    {
        test("Editor preserves original pixels and normalizes output DPI", () =>
        { var image = Pattern(); var document = new EditDocument(image); var result = EditRenderer.Render(document); Require(Pixels(image).SequenceEqual(Pixels(result))); Require(result.DpiX == 96 && result.PixelWidth == 160 && !document.IsDirty); });
        test("Arrow shaft and arrowhead are rendered in image coordinates", () =>
        { var doc = new EditDocument(Pattern(true)); doc.Add(new Mark(MarkKind.Arrow, [new(10, 30), new(90, 30)], "#EB4D3D", 4)); var image = EditRenderer.Render(doc); Require(Pixel(image, 50, 30)[2] > 200 && Pixel(image, 50, 30)[1] < 100, "Arrow shaft missing"); Require(Pixel(image, 78, 34)[1] < 150, "Arrowhead interior missing"); });
        test("Rectangle and ellipse retain unfilled interiors", () =>
        { foreach (var kind in new[] { MarkKind.Rectangle, MarkKind.Ellipse }) { var doc = new EditDocument(Pattern(true)); doc.Add(Box(kind)); var image = EditRenderer.Render(doc); Require(Pixel(image, 60, 20)[1] < 150); Require(Pixel(image, 60, 50).Take(3).All(b => b == 255)); } });
        test("Text and numbered counters render without changing the original", () =>
        { var original = Pattern(true); var before = Pixels(original); var doc = new EditDocument(original); doc.Add(new Mark(MarkKind.Text, [new(8, 8)], Text: "Note", FontSize: 20)); doc.Add(new Mark(MarkKind.Counter, [new(120, 80)], Number: 2, FontSize: 16)); Require(!Pixels(EditRenderer.Render(doc)).SequenceEqual(before)); Require(Pixels(doc.Original).SequenceEqual(before)); Require(Pixel(EditRenderer.Render(doc), 120, 66)[1] < 160); });
        test("Highlight stays translucent and leaves outside pixels unchanged", () =>
        { var doc = new EditDocument(Pattern(true)); doc.Add(Box(MarkKind.Highlight) with { Color = "#FFD561" }); var image = EditRenderer.Render(doc); var pixel = Pixel(image, 50, 50); Require(pixel[0] is > 140 and < 250 && pixel[1] > 200 && pixel[3] == 255); Require(Pixel(image, 5, 5).Take(3).All(b => b == 255)); });
        test("Freehand paths and single-point strokes render", () =>
        { var doc = new EditDocument(Pattern(true)); doc.Add(new Mark(MarkKind.Pen, [new(10, 10), new(30, 30), new(50, 10)], "#205FDB", 5)); doc.Add(new Mark(MarkKind.Pen, [new(75, 20)], "#205FDB", 5)); var image = EditRenderer.Render(doc); Require(Pixel(image, 30, 29)[0] > 170 && Pixel(image, 75, 20)[2] < 100); });
        test("Solid redaction covers original pixels and earlier annotations opaquely", () =>
        { var doc = new EditDocument(Pattern()); doc.Add(new Mark(MarkKind.Text, [new(25, 30)], Text: "Sensitive")); doc.Add(Box(MarkKind.Redact)); var image = EditRenderer.Render(doc); var bytes = Pixels(image); for (int y = 21; y < 79; y++) for (int x = 21; x < 99; x++) { int i = (y * image.PixelWidth + x) * 4; Require(bytes[i] == 0 && bytes[i + 1] == 0 && bytes[i + 2] == 0 && bytes[i + 3] == 255); } Require(Pixel(image, 5, 5).SequenceEqual(Pixel(doc.Original, 5, 5))); });
        test("Pixelation flattens blocks including earlier drawing content", () =>
        { var doc = new EditDocument(Pattern(true)); doc.Add(new Mark(MarkKind.Line, [new(20, 25), new(100, 25)], "#000000", 6)); doc.Add(Box(MarkKind.Pixelate)); var image = EditRenderer.Render(doc); Require(Pixel(image, 22, 22).SequenceEqual(Pixel(image, 30, 30))); Require(Pixel(image, 22, 22)[0] < 250); Require(Pixel(image, 5, 5)[0] == 255); });
        test("Blur changes the selected region and keeps outside pixels", () =>
        { var doc = new EditDocument(Pattern(true)); doc.Add(new Mark(MarkKind.Line, [new(20, 45), new(100, 45)], "#000000", 5)); var sharp = EditRenderer.Render(doc); doc.Add(Box(MarkKind.Blur)); var blurred = EditRenderer.Render(doc); Require(Pixel(blurred, 50, 45)[0] > Pixel(sharp, 50, 45)[0] + 30); Require(Pixel(blurred, 5, 5).SequenceEqual(Pixel(sharp, 5, 5))); });
        test("Crop is pixel exact and reversible", () =>
        { var original = Pattern(); var doc = new EditDocument(original); doc.Crop(new Rect(10, 5, 40, 30)); var expected = new CroppedBitmap(original, new Int32Rect(10, 5, 40, 30)); Require(Pixels(expected).SequenceEqual(Pixels(EditRenderer.Render(doc)))); doc.Undo(); Require(Pixels(original).SequenceEqual(Pixels(EditRenderer.Render(doc))) && !doc.IsDirty); });
        test("Resize and background padding preserve annotation alignment", () =>
        { var doc = new EditDocument(Pattern(true)); doc.Add(new Mark(MarkKind.Line, [new(20, 20), new(80, 20)], "#EB4D3D", 4)); doc.Crop(new Rect(10, 10, 80, 60)); doc.Resize(160, 120); doc.Background(5, "#182A43"); var image = EditRenderer.Render(doc); Require(image.PixelWidth == 170 && image.PixelHeight == 130); Require(Pixel(image, 50, 25)[2] > 200 && Pixel(image, 50, 25)[1] < 100); Require(Pixel(image, 2, 2)[0] == 67); Point mapped = doc.State.ToCanvas(new Point(20, 20)); Require(mapped == new Point(25, 25) && doc.State.ToOriginal(mapped) == new Point(20, 20)); });
        test("Undo redo includes style, move, removal, geometry and background", () =>
        { var doc = new EditDocument(Pattern()); doc.Add(Box(MarkKind.Rectangle)); doc.Replace(0, doc.State.Marks[0].Offset(2, 3) with { Color = "#205FDB" }); doc.Crop(new Rect(5, 5, 110, 90)); doc.Resize(220, 180); doc.Background(10, "#FFFFFF"); doc.Remove(0); var final = Pixels(EditRenderer.Render(doc)); for (int i = 0; i < 6; i++) doc.Undo(); Require(!doc.IsDirty && !doc.CanUndo && doc.State.Marks.Length == 0); for (int i = 0; i < 6; i++) doc.Redo(); Require(Pixels(EditRenderer.Render(doc)).SequenceEqual(final)); doc.Undo(); doc.Add(Box(MarkKind.Ellipse)); Require(!doc.CanRedo); });
        test("PNG export equals rendered pixels and JPEG flattens transparent padding", () =>
        { string folder = Folder(output); var doc = new EditDocument(Pattern()); doc.Add(Box(MarkKind.Redact)); doc.Background(8, "Transparent"); string png = Path.Combine(folder, "edited.png"), jpg = Path.Combine(folder, "edited.jpg"); EditFiles.Export(doc, png, false); EditFiles.Export(doc, jpg, true); Require(Pixels(EditFiles.LoadImage(png)).SequenceEqual(Pixels(EditRenderer.Render(doc)))); var jpeg = EditFiles.LoadImage(jpg); Require(jpeg.PixelWidth == 176 && jpeg.PixelHeight == 136 && Pixel(jpeg, 2, 2).Take(3).All(b => b > 240)); Require(Pixel(jpeg, 58, 58).Take(3).All(b => b < 12)); });
        test("Project is self contained and preserves every annotation kind and rendered pixels", () =>
        {
            string path = Path.Combine(Folder(output), "all-tools.framelet"); var doc = new EditDocument(Pattern());
            foreach (MarkKind kind in Enum.GetValues<MarkKind>()) doc.Add(kind is MarkKind.Text or MarkKind.Counter ? new Mark(kind, [new(30, 30)], Text: "A\nNote", Number: 3) : Box(kind));
            doc.Crop(new Rect(5, 8, 120, 90)); doc.Resize(240, 180); doc.Background(12, "#FFFFFF"); var expected = Pixels(EditRenderer.Render(doc)); EditFiles.SaveProject(doc, path);
            var loaded = EditFiles.LoadProject(path); Require(!doc.IsDirty && !loaded.IsDirty); Require(loaded.State.Marks.Length == Enum.GetValues<MarkKind>().Length); Require(Pixels(loaded.Original).SequenceEqual(Pixels(doc.Original))); Require(expected.SequenceEqual(Pixels(EditRenderer.Render(loaded))));
            using var archive = ZipFile.OpenRead(path); Require(archive.Entries.Select(e => e.FullName).Order().SequenceEqual(new[] { "original.png", "project.json" }));
        });
        test("Locked project save preserves previous file and unsaved edits", () =>
        {
            string folder = Folder(output), path = Path.Combine(folder, "locked.framelet"); var doc = new EditDocument(Pattern()); EditFiles.SaveProject(doc, path); var original = File.ReadAllBytes(path); doc.Add(Box(MarkKind.Arrow));
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { bool failed = false; try { EditFiles.SaveProject(doc, path); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; } Require(failed); }
            Require(doc.IsDirty && original.SequenceEqual(File.ReadAllBytes(path)) && !Directory.GetFiles(folder, "*.tmp").Any());
        });
        test("Editor refuses invalid coordinates, geometry and mark data before mutation", () =>
        {
            var doc = new EditDocument(Pattern()); Reject(() => doc.Add(Box(MarkKind.Arrow) with { Width = double.NaN })); Reject(() => doc.Add(new Mark(MarkKind.Text, [new(-1, 0)]))); Reject(() => doc.Add(new Mark((MarkKind)99, [new(2, 2)]))); Reject(() => doc.Add(Box(MarkKind.Rectangle) with { Color = "file:///x" })); Reject(() => doc.Resize(20000, 20000)); Reject(() => doc.Background(501, "#FFFFFF")); Require(!doc.IsDirty && !doc.CanUndo);
        });
        test("Malformed, future and path-bearing project archives are rejected", () =>
        {
            string folder = Folder(output), good = Path.Combine(folder, "good.framelet"); EditFiles.SaveProject(new EditDocument(Pattern()), good);
            int index = 0;
            foreach (var alter in new Action<JsonObject>[] { n => n["Version"] = 99, n => n["OriginalWidth"] = 42, n => n["State"]!["CropX"] = -1, n => n["State"]!["Width"] = int.MaxValue, n => n["ExtraPath"] = "../../file.png" })
            { string bad = Path.Combine(folder, $"bad{index++}.framelet"); AlterProject(good, bad, alter); Reject(() => EditFiles.LoadProject(bad)); }
            string traversal = Path.Combine(folder, "traversal.framelet"); AlterProject(good, traversal, _ => { }, "../outside.txt"); Reject(() => EditFiles.LoadProject(traversal)); Require(!File.Exists(Path.Combine(folder, "outside.txt")));
        });
        test("Compressed oversized project JSON is rejected before parsing", () =>
        { string path = Path.Combine(Folder(output), "oversized.framelet"); using (var zip = ZipFile.Open(path, ZipArchiveMode.Create)) { using (var writer = new StreamWriter(zip.CreateEntry("project.json").Open())) writer.Write(new string(' ', 4 * 1024 * 1024 + 1)); using var image = zip.CreateEntry("original.png").Open(); } Reject(() => EditFiles.LoadProject(path)); });
        test("PNG and JPEG import release handles and honor JPEG rotation metadata", () =>
        { string folder = Folder(output), path = Path.Combine(folder, "rotated.jpg"); var metadata = new BitmapMetadata("jpg"); metadata.SetQuery("/app1/ifd/{ushort=274}", (ushort)6); var encoder = new JpegBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Pattern(), null, metadata, null)); using (var stream = File.Create(path)) encoder.Save(stream); var rotated = EditFiles.LoadImage(path); Require(rotated.PixelWidth == 120 && rotated.PixelHeight == 160); using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None); });
        test("Unsupported image formats are rejected", () =>
        { string path = Path.Combine(Folder(output), "pretend.png"); var encoder = new GifBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(Pattern())); using (var file = File.Create(path)) encoder.Save(file); Reject(() => EditFiles.LoadImage(path)); });
        test("Edited clipboard image equals the flattened render including redaction and padding", () =>
        {
            var previous = Clipboard.GetDataObject();
            try
            { var doc = new EditDocument(Pattern()); doc.Add(Box(MarkKind.Redact)); doc.Background(6, "#FFFFFF"); var rendered = EditRenderer.Render(doc); var copy = CaptureService.Copy(rendered); while (!copy.IsCompleted) pump(80); copy.GetAwaiter().GetResult(); Require(Pixels(Clipboard.GetImage()!).SequenceEqual(Pixels(rendered))); }
            finally { if (previous is not null) try { Clipboard.SetDataObject(previous, true); } catch (System.Runtime.InteropServices.ExternalException) { } }
        });
        test("Editing and exporting a history capture preserve its original PNG and metadata", () =>
        { string folder = Folder(output); var store = new HistoryStore(folder); var entry = store.Add(Pattern(), CaptureKind.Window); var original = File.ReadAllBytes(store.ImagePath(entry)); var doc = new EditDocument(CaptureService.Load(store.ImagePath(entry))); doc.Add(Box(MarkKind.Arrow)); doc.Crop(new Rect(5, 5, 80, 60)); EditFiles.SaveProject(doc, Path.Combine(folder, "edits.framelet")); EditFiles.Export(doc, Path.Combine(folder, "export.png"), false); Require(original.SequenceEqual(File.ReadAllBytes(store.ImagePath(entry)))); var reloaded = new HistoryStore(folder); Require(reloaded.Entries.Single() == entry); });
        test("Failed edited PNG and JPEG exports preserve the destination", () =>
        { string folder = Folder(output); var doc = new EditDocument(Pattern()); foreach (bool jpeg in new[] { false, true }) { string path = Path.Combine(folder, jpeg ? "old.jpg" : "old.png"); EditFiles.Export(doc, path, jpeg); byte[] before = File.ReadAllBytes(path); doc.Add(Box(MarkKind.Rectangle)); using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { bool failed = false; try { EditFiles.Export(doc, path, jpeg); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { failed = true; } Require(failed); } Require(before.SequenceEqual(File.ReadAllBytes(path)) && !Directory.GetFiles(folder, "*.tmp").Any()); } });
        test("Editor canvas follows document edits and releases subscriptions on close", () =>
        { var doc = new EditDocument(Pattern()); var window = new EditorWindow(doc, _ => { }, () => { }); window.Show(); doc.Resize(80, 60); Require(window.Canvas.Image.PixelWidth == 80); doc.MarkSaved(); Require(window.ConfirmClose()); window.Close(); doc.Resize(40, 30); Require(window.Canvas.Image.PixelWidth == 80); });
    }
}
