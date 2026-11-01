using System;
using System.Collections.Immutable;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;
using PixelRect = System.Drawing.Rectangle;

internal static class UiChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(string output, Action<string, Action> test)
    {
        test("Floating previews stay small at the left across DPI and negative origins", () =>
        {
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 }) foreach (var work in new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(-1920, -200, 1920, 1040), new PixelRect(0, 0, 1280, 720) })
            foreach (Size image in new[] { new Size(7680, 4320), new Size(400, 2400), new Size(3000, 200), new Size(100, 100) })
            { var r = FloatingLayout.Preview(work, image, dpi); Require(work.Contains(r), "Preview offscreen"); Require(r.Width <= 240 * dpi && r.Height <= 166 * dpi, "Preview too large"); Require(Math.Abs(r.Left - work.Left - 12 * dpi) <= 1, "Preview is not left anchored"); Require(Math.Abs(work.Bottom - r.Bottom - 12 * dpi) <= 1, "Preview is not bottom anchored"); }
        });
        test("One to eight pins form bounded nonoverlapping left stacks at multiple DPI scales", () =>
        {
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 }) foreach (var work in new[] { new PixelRect(0, 0, 1920, 1040), new PixelRect(-1280, -200, 1280, 720), new PixelRect(0, 0, 1024, 768) })
            for (int count = 1; count <= 8; count++)
            {
                var images = Enumerable.Range(0, count).Select(i => i % 2 == 0 ? new Size(3840, 2160) : new Size(600, 1800)).ToArray();
                var rects = FloatingLayout.Pins(work, images, dpi); var preview = FloatingLayout.Preview(work, new Size(3840, 2160), dpi);
                Require(rects.Count == count, "Pin lost");
                for (int i = 0; i < count; i++)
                { Require(work.Contains(rects[i]), "Pin offscreen"); Require(rects[i].Width <= 256 * dpi && rects[i].Height <= 192 * dpi, "Pin too large"); Require(!rects[i].IntersectsWith(preview), "Pin overlaps reserved preview"); for (int j = 0; j < i; j++) Require(!rects[i].IntersectsWith(rects[j]), "Pins overlap"); }
            }
        });
        test("Floating frames preserve wide and tall aspect ratios and one pin matches its preview", () =>
        {
            var work = new PixelRect(0, 0, 2560, 1400);
            foreach (double dpi in new[] { 1d, 1.25, 1.5, 2 })
            foreach (var image in new[] { new Size(1551, 574), new Size(1187, 547), new Size(3000, 200), new Size(400, 2400), new Size(100, 100) })
            {
                var preview = FloatingLayout.Preview(work, image, dpi); var pin = FloatingLayout.Pins(work, [image], dpi)[0];
                Require(preview.Size == pin.Size, "Pinning changed the compact size");
                double width = preview.Width - 16 * dpi, height = preview.Height - 16 * dpi;
                double expectedHeight = width * image.Height / image.Width;
                // Integer physical-pixel bounds may round either image edge by less than one pixel.
                Require(Math.Abs(height - expectedHeight) <= 1 + image.Height / image.Width, "Frame adds empty space instead of fitting the image");
            }
        });
        test("Sketch arrows curve and have open heads; sketch ovals keep their interior clear", () =>
        {
            var image = White(); var doc = new EditDocument(image);
            doc.Add(new Mark(MarkKind.Arrow, [new(30, 50), new(230, 50)], "#F34461", 4, Style: MarkStyle.Sketch));
            var rendered = EditRenderer.Render(doc); var bytes = Pixels(rendered);
            bool Ink(int x, int y) => bytes[(y * rendered.PixelWidth + x) * 4 + 1] < 180;
            Require(Enumerable.Range(50, 30).Any(y => Ink(130, y)), "No curved shaft"); Require(!Ink(130, 50), "Sketch shaft remained straight");
            CaptureService.Save(rendered, Path.Combine(output, "sketch-arrow.png"));
            Require(!Ink(218, 61), "Arrowhead interior filled");
            doc.Add(new Mark(MarkKind.Ellipse, [new(60, 100), new(230, 210)], "#F34461", 4, Style: MarkStyle.Sketch));
            bytes = Pixels(EditRenderer.Render(doc)); Require(bytes[(155 * 300 + 145) * 4 + 1] == 255, "Oval filled");
            CaptureService.Save(EditRenderer.Render(doc), Path.Combine(output, "sketch-annotations.png"));
        });
        test("Style persists in version two projects and version one projects retain clean rendering", () =>
        {
            string path = Path.Combine(output, "sketch.framelet"), old = Path.Combine(output, "legacy-v1.framelet");
            var doc = new EditDocument(White()); doc.Add(new Mark(MarkKind.Arrow, [new(30, 50), new(230, 50)], Style: MarkStyle.Sketch));
            EditFiles.SaveProject(doc, path); var loaded = EditFiles.LoadProject(path);
            Require(loaded.State.Marks[0].Style == MarkStyle.Sketch && Pixels(EditRenderer.Render(loaded)).SequenceEqual(Pixels(EditRenderer.Render(doc))), "Style round trip changed render");
            using (var input = ZipFile.OpenRead(path)) using (var target = ZipFile.Open(old, ZipArchiveMode.Create))
            {
                foreach (var entry in input.Entries)
                {
                    using var source = entry.Open(); using var dest = target.CreateEntry(entry.FullName).Open();
                    if (entry.FullName == "project.json") { var n = JsonNode.Parse(source)!; n["Version"] = 1; foreach (var mark in n["State"]!["Marks"]!.AsArray()) mark!.AsObject().Remove("Style"); using var writer = new StreamWriter(dest); writer.Write(n.ToJsonString()); }
                    else source.CopyTo(dest);
                }
            }
            var legacy = EditFiles.LoadProject(old); Require(legacy.State.Marks[0].Style == MarkStyle.Clean, "Legacy style changed");
            doc.Replace(0, doc.State.Marks[0] with { Style = MarkStyle.Clean }); doc.Undo(); Require(doc.State.Marks[0].Style == MarkStyle.Sketch, "Style undo failed"); doc.Redo(); Require(doc.State.Marks[0].Style == MarkStyle.Clean, "Style redo failed");
            try { doc.Add(new Mark(MarkKind.Arrow, [new(1, 1), new(2, 2)], Style: (MarkStyle)99)); throw new Exception("Unknown style accepted"); } catch (ArgumentException) { }
        });
        test("Editor toolbar defaults to sketch and immediately restyles selected layers with undo", () =>
        {
            var doc = new EditDocument(White()); var window = new EditorWindow(doc, _ => { }, () => { });
            try
            {
                window.Show(); window.UpdateLayout();
                Find<Button>(window, "ToolEllipse").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(window.Canvas.Tool == "Ellipse", "Tool button did not select ellipse");
                var mark = window.Canvas.MakeMark(MarkKind.Ellipse, [new(20, 20), new(200, 150)]); Require(mark.Style == MarkStyle.Sketch, "New marks not hand drawn");
                doc.Add(mark); window.Canvas.Select(0); Find<Button>(window, "ColorBlue").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Require(doc.State.Marks[0].Color == "#087FF5", "Color not applied");
                Find<ComboBox>(window, "AnnotationStyle").SelectedIndex = 1; Require(doc.State.Marks[0].Style == MarkStyle.Clean, "Style not applied"); doc.Undo(); Require(doc.State.Marks[0].Style == MarkStyle.Sketch, "Style undo failed");
                Find<Button>(window, "ToggleInspector").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); window.UpdateLayout(); Require(Find<TextBox>(window, "ResizeWidth").IsVisible, "Inspector not available");
            }
            finally { doc.MarkSaved(); window.Close(); }
        });
        test("Frosting and motion policies honor contrast, transparency and animation preferences", () =>
        {
            Require(!SurfacePolicy.Resolve(true, true, true).Frosting && !SurfacePolicy.Resolve(true, true, true).Animate, "Contrast fallback missing");
            Require(!SurfacePolicy.Resolve(false, false, true).Frosting, "Transparency opt-out ignored");
            Require(!SurfacePolicy.Resolve(false, true, false).Animate, "Reduced motion ignored");
            Require(SurfacePolicy.Resolve(false, true, true).Frosting, "Normal frosting unavailable");
        });
        test("Native drag payload retains original PNG pixels and expires only owned old drag exports", () =>
        {
            string root = Path.Combine(output, "drag-data"), folder = Path.Combine(root, "drag-exports"); Directory.CreateDirectory(folder);
            string expired = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"), unrelated = Path.Combine(folder, "keep.png");
            File.WriteAllText(expired, "old owned cache"); File.WriteAllText(unrelated, "unrelated"); File.SetLastWriteTimeUtc(expired, DateTime.UtcNow.AddDays(-8)); File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddDays(-8));
            var original = White(); var data = DragExport.CreateData(root, original); var files = data.GetFileDropList();
            Require(files.Count == 1 && data.ContainsImage(), "Native formats missing");
            var image = CaptureService.Load(files[0]!); Require(image.PixelWidth == 300 && image.PixelHeight == 240 && Pixels(image).SequenceEqual(Pixels(original)), "Drag changed original pixels");
            Require(File.Exists(files[0]) && !File.Exists(expired) && File.Exists(unrelated), "Drag cache retention incorrect");
        });
        test("Fluent light and dark controls use native window chrome and preserve rendered document pixels", () =>
        {
#pragma warning disable WPF0001
            var app = Application.Current; var previous = app.ThemeMode;
            try
            {
                var doc = new EditDocument(White()); var before = Pixels(EditRenderer.Render(doc));
                foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
                {
                    app.ThemeMode = mode; Appearance.Current.Refresh(); var window = new EditorWindow(doc, _ => { }, () => { });
                    try { window.Show(); window.UpdateLayout(); Require(window.WindowStyle == WindowStyle.SingleBorderWindow && !window.AllowsTransparency, "System caption missing"); Require(Find<Button>(window, "EditorCopy").Template is not null, "Fluent template missing"); Require(Pixels(EditRenderer.Render(doc)).SequenceEqual(before), "Theme altered image pixels"); }
                    finally { doc.MarkSaved(); window.Close(); }
                }
            }
            finally { app.ThemeMode = previous; Appearance.Current.Refresh(); }
#pragma warning restore WPF0001
        });
    }
    public static BitmapSource White()
    { var bytes = Enumerable.Repeat((byte)255, 300 * 240 * 4).ToArray(); var b = BitmapSource.Create(300, 240, 96, 96, PixelFormats.Bgra32, null, bytes, 1200); b.Freeze(); return b; }
    private static byte[] Pixels(BitmapSource source)
    { var bytes = new byte[source.PixelWidth * source.PixelHeight * 4]; source.CopyPixels(bytes, source.PixelWidth * 4, 0); return bytes; }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject
    {
        T? Search(DependencyObject node)
        { if (node is T value && AutomationProperties.GetAutomationId(node) == id) return value; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) { var result = Search(VisualTreeHelper.GetChild(node, i)); if (result is not null) return result; } return null; }
        return Search(root) ?? throw new Exception("Missing control " + id);
    }
}
