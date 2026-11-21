using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;

internal static class EditorRefinementChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    internal static BitmapSource Fixture()
    {
        var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 640, 400));
            dc.DrawRoundedRectangle(Ui.Brush("#EFF5F8"), null, new Rect(36, 36, 568, 328), 14, 14);
            EditRenderer.DrawMark(dc, new Mark(MarkKind.Text, [new(64, 58)], "#203348", Text: "A clearer picture", FontSize: 30));
            EditRenderer.DrawMark(dc, new Mark(MarkKind.Text, [new(64, 103)], "#526578", Text: "Capture. Explain. Share.", FontSize: 18));
            for (int i = 0; i < 7; i++) dc.DrawRoundedRectangle(Ui.Brush(i == 5 ? "#F34461" : "#54B8B3"), null, new Rect(70 + i * 70, 302 - i * 18, 36, 30 + i * 18), 5, 5);
        }
        var image = new RenderTargetBitmap(640, 400, 96, 96, PixelFormats.Pbgra32); image.Render(visual); image.Freeze(); return image;
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is Window { Content: DependencyObject content }) { if (content is T match) yield return match; foreach (var nested in Descendants<T>(content)) yield return nested; yield break; }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        { var child = VisualTreeHelper.GetChild(root, i); if (child is T result) yield return result; foreach (var nested in Descendants<T>(child)) yield return nested; }
    }
    private static Button Button(Window window, string id) => Descendants<Button>(window).Single(b => AutomationProperties.GetAutomationId(b) == id);
    private static void Click(Window window, string id) => Button(window, id).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    private static void Layout(EditorWindow window, double width = 1120, double height = 780)
    { window.Width = width; window.Height = height; ((FrameworkElement)window.Content).Measure(new Size(width, height)); ((FrameworkElement)window.Content).Arrange(new Rect(0, 0, width, height)); window.UpdateLayout(); }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(app);
        int failures = 0; var results = new List<object>();
        void Test(string name, Action body) { try { body(); results.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); } catch (Exception ex) { failures++; results.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); } }
        EditorChecks.Run(output, (name, body) => { if (!name.Contains("clipboard", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("Editor canvas")) Test(name, body); }, _ => { });
        CompositionChecks.Run(output, Test); EditorRenderCacheChecks.Run(Test);
        Test("New arrows select themselves and their shaft moves immediately with one undo", () =>
        {
            var doc = new EditDocument(Fixture()); var canvas = new EditCanvas(doc) { MakeMark = (k, p) => new(k, p, Arrow: ArrowStyle.Classic) };
            canvas.PointerDown(new Point(40, 80)); canvas.PointerUp(new Point(160, 80));
            Require(canvas.Tool == "Select" && canvas.Selected == 0, "New arrow was not selected");
            canvas.PointerDown(new Point(100, 80)); canvas.PointerMove(new Point(120, 110)); canvas.PointerUp(new Point(120, 110));
            Require(doc.State.Marks[0].Points[0] == new EditPoint(60, 110), "Shaft did not move"); doc.Undo();
            Require(doc.State.Marks[0].Points[0] == new EditPoint(40, 80), "Move did not undo as one operation"); canvas.Dispose();
        });
        Test("Arrow endpoints and curve bends remain editable at reduced zoom", () =>
        {
            var doc = new EditDocument(Fixture()); doc.Add(new Mark(MarkKind.Arrow, [new(40, 80), new(220, 80)], Arrow: ArrowStyle.Curved));
            var canvas = new EditCanvas(doc); canvas.SetZoom(.5); canvas.Select(0); canvas.PointerDown(new Point(220, 80)); canvas.PointerUp(new Point(240, 130));
            Require(doc.State.Marks[0].Points[1] == new EditPoint(240, 130), "Endpoint stayed fixed");
            var at = ArrowDrawing.BendHandle(doc.State.Marks[0]); canvas.PointerDown(at); canvas.PointerUp(at + new Vector(0, 35));
            Require(Math.Abs(doc.State.Marks[0].Bend - .2) > .01, "Curve stayed fixed"); canvas.Dispose();
        });
        Test("Shape hit testing respects hollow interiors and supports move and corner resize", () =>
        {
            var doc = new EditDocument(Fixture()); doc.Add(new Mark(MarkKind.Rectangle, [new(50, 50), new(200, 180)]));
            Require(EditInteraction.HitMark(doc.State, new Point(120, 110), 5) == -1, "Empty rectangle interior stole hit");
            var canvas = new EditCanvas(doc) { Tool = "Select" }; canvas.PointerDown(new Point(120, 50)); canvas.PointerUp(new Point(140, 70));
            Require(doc.State.Marks[0].Points[0] == new EditPoint(70, 70), "Rectangle did not move");
            canvas.PointerDown(new Point(220, 200)); canvas.PointerUp(new Point(250, 220));
            Require(doc.State.Marks[0].Bounds.Width == 180, "Rectangle did not resize"); canvas.Dispose();
        });
        Test("Text is typed on image, can be edited again and moved without rewriting", () =>
        {
            var doc = new EditDocument(Fixture()); var canvas = new EditCanvas(doc) { Tool = "Text" }; canvas.PointerDown(new Point(80, 100));
            Require(canvas.IsEditingText && doc.State.Marks.Length == 0, "Text was committed before typing");
            canvas.Children.OfType<TextBox>().Single().Text = "A movable label"; Require(canvas.CommitText(), "Inline commit failed");
            Require(doc.State.Marks[0].Text == "A movable label" && canvas.Tool == "Select", "Inline text missing");
            canvas.BeginText(0, new Point(90, 110)); canvas.Children.OfType<TextBox>().Single().Text = "Updated label"; canvas.CommitText();
            var center = doc.State.Marks[0].Bounds; var point = new Point(center.X + center.Width / 2, center.Y + center.Height / 2);
            canvas.PointerDown(point); canvas.PointerUp(point + new Vector(30, 20));
            Require(doc.State.Marks[0].Text == "Updated label" && doc.State.Marks[0].Points[0] == new EditPoint(110, 120), "Text failed to move"); canvas.Dispose();
        });
        Test("Esc cancels new text and existing text without changing history", () =>
        {
            var doc = new EditDocument(Fixture()); var canvas = new EditCanvas(doc); canvas.BeginText(-1, new Point(40, 40)); canvas.Children.OfType<TextBox>().Single().Text = "Cancel me"; canvas.CancelText();
            Require(!doc.CanUndo && doc.State.Marks.Length == 0, "Cancelled text changed document"); doc.Add(new Mark(MarkKind.Text, [new(40, 40)], Text: "Keep"));
            canvas.BeginText(0, new Point(40, 40)); canvas.Children.OfType<TextBox>().Single().Text = "Discard"; canvas.CancelText();
            Require(doc.State.Marks[0].Text == "Keep", "Original text was lost"); canvas.Dispose();
        });
        Test("Dragging away then returning to origin does not commit stale geometry", () =>
        {
            var doc = new EditDocument(Fixture()); doc.Add(new Mark(MarkKind.Line, [new(40, 80), new(160, 80)])); var canvas = new EditCanvas(doc) { Tool = "Select" };
            var state = doc.State; canvas.PointerDown(new Point(100, 80)); canvas.PointerMove(new Point(140, 120)); canvas.PointerUp(new Point(100, 80));
            Require(doc.State == state, "Returned drag committed stale mark"); doc.AddImage(Fixture(), new Point(700, 0), 200); canvas.SelectImage(0); state = doc.State;
            canvas.PointerDown(new Point(750, 60)); canvas.PointerMove(new Point(780, 100)); canvas.PointerUp(new Point(750, 60));
            Require(doc.State == state, "Returned image drag committed stale position"); canvas.Dispose();
        });
        Test("Undo reorder preserves selected image identity", () =>
        {
            var doc = new EditDocument(Fixture()); doc.AddImage(Fixture(), new Point(700, 0), 200); doc.AddImage(Fixture(), new Point(950, 0), 200);
            var canvas = new EditCanvas(doc); canvas.SelectImage(0); string id = doc.State.Images[0].Id; doc.MoveImage(0, 1);
            Require(doc.State.Images[canvas.SelectedImage].Id == id, "Reorder changed selected image"); doc.Undo();
            Require(doc.State.Images[canvas.SelectedImage].Id == id, "Undo selected a different image"); canvas.Dispose();
        });
        Test("Rejected endpoint resize and unchanged text do not dirty the document", () =>
        {
            var doc = new EditDocument(Fixture()); doc.Add(new Mark(MarkKind.Arrow, [new(40, 80), new(220, 80)])); doc.MarkSaved();
            var canvas = new EditCanvas(doc); canvas.Select(0); var state = doc.State;
            canvas.PointerDown(new Point(220, 80)); canvas.PointerUp(new Point(40.5, 80));
            Require(doc.State == state && !doc.IsDirty, "Rejected near-zero arrow resize changed state");
            doc.Add(new Mark(MarkKind.Text, [new(40, 120)], Text: "Unchanged")); doc.MarkSaved(); state = doc.State;
            canvas.BeginText(1, new Point(40, 120)); canvas.CommitText();
            Require(doc.State == state && !doc.IsDirty, "Unchanged text created an undo step"); canvas.Dispose();
        });
        Test("Crop is restricted to original image even when added images extend the canvas", () =>
        {
            var doc = new EditDocument(Fixture()); doc.AddImage(Fixture(), new Point(700, 0), 300); var canvas = new EditCanvas(doc) { Tool = "Crop" }; var state = doc.State;
            canvas.PointerDown(new Point(750, 50)); canvas.PointerUp(new Point(850, 130));
            Require(doc.State == state, "Crop accepted a misleading added-image selection");
            canvas.PointerDown(new Point(50, 50)); canvas.PointerUp(new Point(900, 300));
            Require(doc.State.Crop.Right == 640 && doc.State.Crop.X == 50 && doc.State.Images[0] == state.Images[0], "Crop escaped original or changed added layer"); canvas.Dispose();
        });
        Test("Undo text style resyncs controls before the next color edit", () =>
        {
            var doc = new EditDocument(Fixture()); doc.Add(new Mark(MarkKind.Text, [new(30, 40)], Text: "Label")); var editor = new EditorWindow(doc, _ => { }, () => { }, saveHistory: _ => { }); Layout(editor); editor.Canvas.Select(0);
            doc.Replace(0, doc.State.Marks[0] with { TextPreset = TextPreset.SolidLabel }); doc.Undo(); Click(editor, "ColorBlue");
            Require(doc.State.Marks[0].TextPreset == TextPreset.Plain && doc.State.Marks[0].Color == "#087FF5", "Color edit reapplied undone text style"); editor.Close();
        });
        Test("Drag preparation commits the text that will be exported", () =>
        {
            var editor = new EditorWindow(new EditDocument(Fixture()), _ => { }, () => { }, saveHistory: _ => { });
            editor.Canvas.BeginText(-1, new Point(40, 50)); editor.Canvas.Children.OfType<TextBox>().Single().Text = "Include in drag";
            Require(editor.PrepareImageTransfer() && editor.Document.State.Marks.Single().Text == "Include in drag", "Pending text omitted from transfer"); editor.Close();
        });
        Test("Added images move outside original bounds and resize proportionally", () =>
        {
            var doc = new EditDocument(Fixture()); doc.AddImage(Fixture(), new Point(650, 0), 320); var canvas = new EditCanvas(doc) { Tool = "Select" };
            canvas.PointerDown(new Point(750, 70)); canvas.PointerUp(new Point(720, 130));
            Require(doc.State.Images[0].X == 620 && doc.State.Images[0].Y == 60, "Image position unchanged");
            canvas.PointerDown(new Point(940, 260)); canvas.PointerUp(new Point(1100, 360));
            Require(Math.Abs(doc.State.Images[0].Width / doc.State.Images[0].Height - 1.6) < .001, "Imported image distorted"); canvas.Dispose();
        });
        Test("Editor exposes seven styles, symbol stamps, named panels and file drop", () =>
        {
            var editor = new EditorWindow(new EditDocument(Fixture()), _ => { }, () => { }, saveHistory: _ => { }); Layout(editor);
            Click(editor, "ToolText"); Layout(editor); var styles = Descendants<ComboBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "TextPreset");
            Require(styles.Items.Count == 7, "Text preset count wrong"); Require(editor.AllowDrop, "Drop not enabled");
            Click(editor, "ToolStamp"); editor.Canvas.PointerDown(new Point(120, 110)); Require(editor.Document.State.Marks[^1].Text == "✓", "Stamp not placed");
            Click(editor, "EditorBackground"); Layout(editor); Click(editor, "BackgroundAurora");
            Require(editor.Document.State.Backdrop?.Preset == "Aurora" && editor.Document.State.Padding == 48, "Background preset not applied");
            Click(editor, "ToggleInspector"); Layout(editor); Require(Button(editor, "AddCompositionImage").ActualWidth > 0, "Size and layers panel missing"); editor.Close();
        });
        Test("Imported images and pending inline text are saved when editor closes", () =>
        {
            string imagePath = Path.Combine(output, "import.png"); CaptureService.Save(Fixture(), imagePath); var store = new HistoryStore(Path.Combine(output, "history")); CaptureEntry? entry = null;
            var editor = new EditorWindow(new EditDocument(Fixture()), _ => { }, () => { }, saveHistory: doc => entry = store.SaveEdits(doc, entry));
            editor.ImportFiles([imagePath]); editor.Canvas.BeginText(-1, new Point(30, 40)); editor.Canvas.Children.OfType<TextBox>().Single().Text = "Kept in history";
            Require(editor.ConfirmClose() && entry is not null, "Close did not save"); var restored = store.LoadDocument(entry!);
            Require(restored.State.Images.Length == 1 && restored.State.Marks[^1].Text == "Kept in history", "History discarded composition or pending text"); editor.Close();
        });
        Test("Failed image drops are rejected; accepted drops preserve position and text", () =>
        {
            var editor = new EditorWindow(new EditDocument(Fixture()), _ => { }, () => { }, saveHistory: _ => { });
            var invalid = new DataObject(DataFormats.FileDrop, new[] { Path.Combine(output, "does-not-exist.png") }); var before = editor.Document.State;
            Require(!editor.ImportDroppedImage(invalid, new Point(200, 150)) && editor.Document.State == before, "Failed drop was accepted");
            Require(!editor.ImportDroppedImage(new DataObject(DataFormats.Text, "not an image"), new Point(200, 150)), "Unsupported drop was accepted");
            editor.Canvas.BeginText(-1, new Point(30, 40)); editor.Canvas.Children.OfType<TextBox>().Single().Text = "Keep before drop";
            Require(editor.ImportDroppedImage(new DataObject(DataFormats.Bitmap, Fixture()), new Point(800, 100)), "Bitmap drop rejected");
            var layer = editor.Document.State.Images.Single();
            Require(Math.Abs(layer.X + layer.Width / 2 - 800) < .01 && editor.Document.State.Marks.Single().Text == "Keep before drop", "Drop lost position or inline text"); editor.Close();
        });
        Test("Background alignment shows selection and disables choices without extra space", () =>
        {
            var editor = new EditorWindow(new EditDocument(Fixture()), _ => { }, () => { }, saveHistory: _ => { }); Layout(editor); Click(editor, "EditorBackground"); Layout(editor);
            Require(!Button(editor, "BackgroundAlign4").IsEnabled && AutomationProperties.GetItemStatus(Button(editor, "BackgroundAlign4")) == "Selected", "Automatic alignment state misleading");
            var ratio = Descendants<ComboBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "BackgroundRatio"); ratio.SelectedIndex = 1;
            Require(Button(editor, "BackgroundAlign0").IsEnabled, "Square alignment not enabled"); Click(editor, "BackgroundAlign0");
            Require(AutomationProperties.GetItemStatus(Button(editor, "BackgroundAlign0")) == "Selected" && AutomationProperties.GetItemStatus(Button(editor, "BackgroundAlign4")) == "", "New alignment not indicated");
            ratio.SelectedIndex = 0; Require(!Button(editor, "BackgroundAlign0").IsEnabled, "Automatic grid stayed active"); editor.Close();
        });
        Test("Context controls and seven text styles render with meaningful labels", () =>
        {
            var editor = new EditorWindow(new EditDocument(Fixture()), _ => { }, () => { }, saveHistory: _ => { }); Layout(editor);
            var font = Descendants<ComboBox>(editor).Single(c => AutomationProperties.GetAutomationId(c) == "FontSize");
            Require(((FrameworkElement)font.Parent).Visibility == Visibility.Collapsed, "Text size shown for arrow tool"); Click(editor, "ToolText"); Layout(editor); Require(((FrameworkElement)font.Parent).Visibility == Visibility.Visible, "Text size hidden for text tool");
            var visual = new DrawingVisual(); using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Ui.Brush("#EDF1F7"), null, new Rect(0, 0, 760, 500));
                string[] names = ["Plain", "Outline", "Shadow", "Label", "Soft label", "Callout", "Marker"];
                for (int i = 0; i < 7; i++)
                {
                    TextAnnotation.Draw(dc, new Mark(MarkKind.Text, [new(24, 22 + i * 65)], "#536174", Text: names[i], FontSize: 17));
                    TextAnnotation.Draw(dc, new Mark(MarkKind.Text, [new(180, 15 + i * 65)], "#B83861", Text: "A useful detail", FontSize: 26, TextPreset: (TextPreset)i));
                }
                TextAnnotation.Draw(dc, new Mark(MarkKind.Text, [new(480, 30)], "#16875C", Text: "✓  √  ×  ✕  x", FontSize: 28));
            }
            var bitmap = new RenderTargetBitmap(760, 500, 96, 96, PixelFormats.Pbgra32); bitmap.Render(visual); CaptureService.Save(bitmap, Path.Combine(output, "text-styles.png")); editor.Close();
        });
        foreach (string theme in new[] { "dark", "light" }) foreach (int size in new[] { 980, 1280 })
            Test($"Detached editor render {theme} {size}", () =>
            {
#pragma warning disable WPF0001
                app.ThemeMode = theme == "dark" ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
                Appearance.Current.Refresh(); var doc = new EditDocument(Fixture());
                doc.Add(new Mark(MarkKind.Arrow, [new(360, 170), new(455, 210)], "#F34461", 5, Arrow: ArrowStyle.Curved, Bend: -.3));
                doc.Add(new Mark(MarkKind.Text, [new(300, 130)], "#A52C58", Text: "A useful detail", FontSize: 22, TextPreset: TextPreset.LightLabel));
                var editor = new EditorWindow(doc, _ => { }, () => { }, saveHistory: _ => { }); Layout(editor, size); Click(editor, "EditorBackground"); Layout(editor, size); Click(editor, "BackgroundAurora"); Layout(editor, size);
                var content = (FrameworkElement)editor.Content; var bitmap = new RenderTargetBitmap(size, 780, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                CaptureService.Save(bitmap, Path.Combine(output, $"editor-{theme}-{size}.png"));
                Require(new System.Windows.Interop.WindowInteropHelper(editor).Handle == IntPtr.Zero, "Render created a native window");
                var bg = Button(editor, "EditorBackground").TransformToAncestor(content).TransformBounds(new Rect(Button(editor, "EditorBackground").RenderSize));
                Require(bg.Right <= size && bg.Width >= 32, "Background button clipped"); editor.Close();
            });
        app.Shutdown(); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, results }, new JsonSerializerOptions { WriteIndented = true })); return failures == 0 ? 0 : 1;
    }
}
