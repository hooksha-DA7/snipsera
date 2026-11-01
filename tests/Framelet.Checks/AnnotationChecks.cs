using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class AnnotationChecks
{
    private static void Require(bool condition, string why) { if (!condition) throw new Exception(why); }
    private static byte[] Bytes(BitmapSource image) { var bytes = new byte[image.PixelWidth * image.PixelHeight * 4]; image.CopyPixels(bytes, image.PixelWidth * 4, 0); return bytes; }
    public static void Run(string output, Action<string, Action> test, Action<int> pump)
    {
        test("Four arrow styles are distinct and version-three projects preserve their geometry", () =>
        {
            var signatures = new System.Collections.Generic.HashSet<string>();
            foreach (ArrowStyle style in Enum.GetValues<ArrowStyle>())
            {
                var doc = new EditDocument(UiChecks.White()); doc.Add(new Mark(MarkKind.Arrow, [new(25, 65), new(250, 140)], "#EF344D", 5, Arrow: style, Bend: -.25));
                var before = Bytes(EditRenderer.Render(doc)); signatures.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(before)));
                string path = Path.Combine(output, style + ".framelet"); EditFiles.SaveProject(doc, path); var loaded = EditFiles.LoadProject(path);
                Require(loaded.State.Marks[0].Arrow == style && loaded.State.Marks[0].Bend == -.25, "Arrow metadata lost");
                Require(Bytes(EditRenderer.Render(loaded)).SequenceEqual(before), "Arrow pixels changed on round trip"); CaptureService.Save(EditRenderer.Render(doc), Path.Combine(output, "arrow-" + style + ".png"));
                using var zip = ZipFile.OpenRead(path); using var data = zip.GetEntry("project.json")!.Open(); Require(JsonNode.Parse(data)!["Version"]!.GetValue<int>() == 3, "Wrong project version");
            }
            Require(signatures.Count == 4, "Arrow choices render identically");
        });
        test("Curve handle maps to geometry and drag preview does not commit or alter undo history", () =>
        {
            var mark = new Mark(MarkKind.Arrow, [new(30, 60), new(250, 60)], Arrow: ArrowStyle.Curved, Bend: .3);
            Point handle = ArrowDrawing.BendHandle(mark); Require(Math.Abs(ArrowDrawing.BendAt(mark, handle) - .3) < .0001, "Bend mapping wrong");
            Require(mark.Bounds.Contains(handle), "Curve extends beyond selectable bounds");
            var doc = new EditDocument(UiChecks.White()); doc.Add(mark); doc.MarkSaved(); var before = doc.State;
            var changed = mark with { Bend = -.25 }; var preview = EditRenderer.Render(doc, 0, changed);
            Require(doc.State == before && !doc.IsDirty, "Preview modified document"); Require(!Bytes(preview).SequenceEqual(Bytes(EditRenderer.Render(doc))), "Curve preview unchanged");
            doc.Replace(0, changed); doc.Undo(); Require(doc.State.Marks[0] == mark && !doc.IsDirty, "Bend undo failed"); doc.Redo(); Require(doc.State.Marks[0].Bend == -.25, "Bend redo failed");
        });
        test("Version one and two legacy arrows retain their prior render", () =>
        {
            foreach (int version in new[] { 1, 2 })
            {
                var doc = new EditDocument(UiChecks.White()); doc.Add(new Mark(MarkKind.Arrow, [new(20, 80), new(240, 80)], Style: version == 1 ? MarkStyle.Clean : MarkStyle.Sketch)); doc.Add(new Mark(MarkKind.Pen, [new(10, 160), new(60, 190), new(100, 150), new(170, 200)], Style: version == 1 ? MarkStyle.Clean : MarkStyle.Sketch));
                string source = Path.Combine(output, "legacy-source-" + version + ".framelet"), target = Path.Combine(output, "legacy-" + version + ".framelet"); EditFiles.SaveProject(doc, source);
                using (var read = ZipFile.OpenRead(source)) using (var write = ZipFile.Open(target, ZipArchiveMode.Create))
                    foreach (var entry in read.Entries)
                    {
                        using var input = entry.Open(); using var dest = write.CreateEntry(entry.FullName).Open();
                        if (entry.FullName == "project.json") { var n = JsonNode.Parse(input)!; n["Version"] = version; foreach (var mark in n["State"]!["Marks"]!.AsArray()) { var m = mark!.AsObject(); m.Remove("Arrow"); m.Remove("Bend"); m.Remove("Smooth"); if (version == 1) m.Remove("Style"); } using var writer = new StreamWriter(dest); writer.Write(n.ToJsonString()); }
                        else input.CopyTo(dest);
                    }
                Require(Bytes(EditRenderer.Render(EditFiles.LoadProject(target))).SequenceEqual(Bytes(EditRenderer.Render(doc))), "Legacy appearance changed");
            }
        });
        test("Sketch pen smooths sampled corners without changing original pixels", () =>
        {
            var doc = new EditDocument(UiChecks.White()); var original = Bytes(doc.Original);
            var pen = new Mark(MarkKind.Pen, [new(20, 60), new(65, 100), new(110, 30), new(170, 110), new(240, 70)], Width: 5);
            doc.Add(pen); var sharp = Bytes(EditRenderer.Render(doc)); doc.Replace(0, pen with { Style = MarkStyle.Sketch, Smooth = true });
            Require(!sharp.SequenceEqual(Bytes(EditRenderer.Render(doc))), "Smoothed pen unchanged"); Require(original.SequenceEqual(Bytes(doc.Original)), "Source pixels changed");
        });
        test("Floating rounded alpha masks retain antialiased edges at multiple DPI scales", () =>
        {
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            {
                var surface = new Grid { Width = 180, Height = 100, Background = Brushes.Red }; surface.Measure(new Size(180, 100)); surface.Arrange(new Rect(0, 0, 180, 100)); FloatingVisuals.Round(surface, new Size(180, 100));
                var image = new RenderTargetBitmap((int)(180 * scale), (int)(100 * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32); image.Render(surface); var bytes = Bytes(image);
                Require(bytes[3] == 0, "Corner not transparent"); Require(bytes.Where((_, i) => i % 4 == 3).Any(a => a is > 0 and < 255), "Rounded contour is hard clipped");
                CaptureService.Save(image, Path.Combine(output, "rounded-mask-" + scale.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png"));
            }
        });
        test("Capture selection is asynchronous and leaves other windows enabled", () =>
        {
            var parent = new Window { Width = 200, Height = 120 }; parent.Show();
            var picker = new SelectionWindow(UiChecks.White(), new System.Drawing.Rectangle(20, 20, 300, 240));
            var result = picker.PickAsync(); pump(30); Require(!ComponentDispatcher.IsThreadModal && parent.IsEnabled, "Selector disabled other windows");
            picker.Close(); Require(result.IsCompleted && !result.Result, "Selection cancellation did not finish"); parent.Close();
        });
        test("Fluent confirmation uses explicit save discard cancel actions and safe close default", () =>
        {
            var dialog = new ConfirmWindow(null, "Save your edits?", "Test document", "Save project…", "Discard edits"); dialog.Show(); dialog.UpdateLayout();
            Require(dialog.Choice == Confirmation.Cancel && dialog.WindowStyle == WindowStyle.SingleBorderWindow, "Unsafe close default");
            int buttons = 0; void Count(DependencyObject node) { if (node is Button && AutomationProperties.GetAutomationId(node).StartsWith("Dialog")) buttons++; for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) Count(VisualTreeHelper.GetChild(node, i)); } Count(dialog);
            Require(buttons == 3, "Missing semantic confirmation actions"); dialog.Close();
        });
    }
}
