using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace Framelet;

public sealed class EditorWindow : Window
{
    public EditDocument Document { get; }
    public EditCanvas Canvas { get; }
    private readonly string name;
    private string? projectPath;
    private readonly TextBlock status = Ui.Text("Drag on the image to add an arrow.", 12, Ui.Muted);
    private readonly TextBlock dimensions = Ui.Text("", 11, Ui.Muted);
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Brush("#DFE7F1") };
    private readonly ListBox objects = new() { MinHeight = 100, MaxHeight = 220, Margin = new Thickness(0, 6, 0, 12) };
    private readonly TextBox text = new() { Text = "Add a note", AcceptsReturn = true, Height = 66, MaxLength = 2000, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox width = Field("ResizeWidth"), height = Field("ResizeHeight"), padding = Field("BackgroundPadding");
    private readonly CheckBox aspect = new() { Content = "Keep aspect ratio", IsChecked = true, Margin = new Thickness(0, 5, 0, 8) };
    private readonly ComboBox colors = Choice("AnnotationColor"), strokes = Choice("StrokeWidth"), fonts = Choice("FontSize"), background = Choice("BackgroundColor"), zoom = Choice("CanvasZoom");
    private readonly Dictionary<string, Button> tools = [];
    private readonly Button undo, redo, remove;
    private bool updating;
    private double resizeRatio;
    private sealed record Swatch(string Name, string Value);

    public EditorWindow(EditDocument document, Action<System.Windows.Media.Imaging.BitmapSource> pin, Action open, string name = "Capture", string? projectPath = null)
    {
        Document = document; this.name = name; this.projectPath = projectPath;
        Ui.Setup(this); Width = 1280; Height = 850; MinWidth = 1040; MinHeight = 640; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Canvas = new EditCanvas(document) { Report = ex => SetStatus(ex.Message, true) };
        Canvas.MakeMark = (kind, points) => new Mark(kind, points, SelectedColor(colors), Number(strokes, 4), text.Text, Number(fonts, 24),
            Math.Min(999, document.State.Marks.Where(m => m.Kind == MarkKind.Counter).Select(m => m.Number).DefaultIfEmpty(0).Max() + 1));
        var dock = new DockPanel { Margin = new Thickness(20) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(Action("Open…", "EditorOpen", open));
        actions.Children.Add(Action("Save project…", "SaveProject", () => SaveProject()));
        actions.Children.Add(Action("PNG…", "ExportPng", () => Export(false), true));
        actions.Children.Add(Action("JPEG…", "ExportJpeg", () => Export(true)));
        actions.Children.Add(Action("Copy", "EditorCopy", async () =>
        { try { await CaptureService.Copy(Canvas.Image); SetStatus("Edited image copied to clipboard"); } catch (Exception ex) { SetStatus(ex.Message, true); } }));
        actions.Children.Add(Action("Pin", "EditorPin", () => pin(Canvas.Image)));
        DockPanel.SetDock(actions, Dock.Right); header.Children.Add(actions);
        var title = new StackPanel(); var heading = Ui.Text("Framelet", 26, Ui.Ink, true); heading.FontFamily = new FontFamily("Bahnschrift");
        title.Children.Add(heading); title.Children.Add(Ui.Text("IMAGE EDITOR", 10, Ui.Blue, true)); header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        status.Margin = new Thickness(0, 12, 0, 0); DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(192) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(218) });
        var left = new StackPanel(); left.Children.Add(Ui.Text("TOOLS", 11, Ui.Blue, true));
        var toolGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 7, 0, 12) };
        (string tool, string label)[] definitions = [("Select", "Move / select"), ("Crop", "Crop"), ("Arrow", "Arrow"), ("Line", "Line"), ("Rectangle", "Rectangle"), ("Ellipse", "Ellipse"), ("Text", "Text"), ("Counter", "Counter"), ("Highlight", "Highlight"), ("Pen", "Freehand"), ("Blur", "Blur"), ("Pixelate", "Pixelate"), ("Redact", "Solid mask")];
        foreach (var (tool, label) in definitions)
        {
            var button = Action(label, "Tool" + tool, () => ChooseTool(tool)); button.FontSize = 11; button.Padding = new Thickness(5, 8, 5, 8); button.Margin = new Thickness(0, 0, 5, 5);
            tools.Add(tool, button); toolGrid.Children.Add(button);
        }
        left.Children.Add(toolGrid); left.Children.Add(Ui.Text("Color", 11));
        AddColors(colors, false); left.Children.Add(colors);
        left.Children.Add(Ui.Text("Stroke width / text size", 11));
        foreach (double value in new[] { 2d, 4, 8, 12, 20 }) strokes.Items.Add(value); strokes.SelectedItem = 4d;
        foreach (double value in new[] { 12d, 18, 24, 32, 48, 64 }) fonts.Items.Add(value); fonts.SelectedItem = 24d;
        var sizes = new UniformGrid { Columns = 2 }; sizes.Children.Add(strokes); sizes.Children.Add(fonts); left.Children.Add(sizes);
        left.Children.Add(Ui.Text("Text for your note", 11)); AutomationProperties.SetAutomationId(text, "AnnotationText"); left.Children.Add(text);
        var apply = Action("Update selected", "UpdateAnnotation", UpdateSelected); apply.Margin = new Thickness(0, 8, 0, 8); left.Children.Add(apply);
        left.Children.Add(Ui.Text("Text/counter: click to place. Other tools: drag. Select an object to move, restyle or remove it.", 11, Ui.Muted));
        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 0, 14, 0) }; grid.Children.Add(leftScroll);

        var center = new DockPanel { Margin = new Thickness(0, 0, 16, 0) };
        var canvasBar = new DockPanel { Margin = new Thickness(0, 0, 0, 9) };
        var editActions = new StackPanel { Orientation = Orientation.Horizontal };
        undo = Action("Undo", "EditorUndo", () => { Canvas.CancelGesture(); document.Undo(); });
        redo = Action("Redo", "EditorRedo", () => { Canvas.CancelGesture(); document.Redo(); });
        remove = Action("Remove", "RemoveAnnotation", RemoveSelected);
        foreach (var button in new[] { undo, redo, remove }) { button.FontSize = 11; button.Padding = new Thickness(9, 8, 9, 8); editActions.Children.Add(button); }
        canvasBar.Children.Add(editActions);
        foreach (string value in new[] { "Fit", "25%", "50%", "100%", "150%", "200%", "400%" }) zoom.Items.Add(value);
        zoom.Width = 76; zoom.SelectedIndex = 0; zoom.Margin = new Thickness(0); DockPanel.SetDock(zoom, Dock.Right);
        canvasBar.Children.Add(zoom); DockPanel.SetDock(canvasBar, Dock.Top); center.Children.Add(canvasBar);
        dimensions.FontFamily = new FontFamily("Cascadia Mono, Consolas"); dimensions.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(dimensions, Dock.Top); center.Children.Add(dimensions);
        var paper = new Border { Child = Canvas, Background = Checkerboard(), BorderBrush = Ui.Brush("#8DA9D3"), BorderThickness = new Thickness(1), Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        viewport.Content = paper; center.Children.Add(viewport); Grid.SetColumn(center, 1); grid.Children.Add(center);

        var right = new StackPanel(); right.Children.Add(Ui.Text("OUTPUT SIZE", 11, Ui.Blue, true));
        right.Children.Add(Ui.Text("Image width × height in pixels", 11, Ui.Muted));
        var resize = new UniformGrid { Columns = 2 }; resize.Children.Add(width); resize.Children.Add(height); right.Children.Add(resize); right.Children.Add(aspect);
        right.Children.Add(Action("Apply resize", "ApplyResize", () => Try(() => document.Resize(Parse(width, 1, 20000), Parse(height, 1, 20000)))));
        var bgLabel = Ui.Text("BACKGROUND", 11, Ui.Blue, true); bgLabel.Margin = new Thickness(0, 18, 0, 0); right.Children.Add(bgLabel);
        AddColors(background, true); right.Children.Add(background);
        right.Children.Add(Ui.Text("Padding on each side (pixels)", 11, Ui.Muted)); right.Children.Add(padding);
        right.Children.Add(Action("Apply background", "ApplyBackground", () => Try(() => document.Background(Parse(padding, 0, 500), SelectedColor(background)))));
        var objectLabel = Ui.Text("OBJECTS", 11, Ui.Blue, true); objectLabel.Margin = new Thickness(0, 18, 0, 0); right.Children.Add(objectLabel);
        AutomationProperties.SetAutomationId(objects, "AnnotationObjects"); right.Children.Add(objects);
        right.Children.Add(Ui.Text("Projects keep the original image and editable layers, including pixels hidden by masks. Share a PNG or JPEG for a flattened result.", 11, Ui.Muted));
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; Grid.SetColumn(rightScroll, 2); grid.Children.Add(rightScroll);
        dock.Children.Add(grid); Content = dock;

        objects.SelectionChanged += (_, _) => { if (!updating) Canvas.Select(objects.SelectedIndex); };
        Canvas.SelectionChanged += index =>
        {
            if (objects.SelectedIndex != index) objects.SelectedIndex = index;
            remove.IsEnabled = index >= 0;
            if (index < 0) return;
            var mark = document.State.Marks[index]; SelectColor(colors, mark.Color); strokes.SelectedItem = mark.Width; fonts.SelectedItem = mark.FontSize;
            if (mark.Kind == MarkKind.Text) text.Text = mark.Text;
        };
        width.TextChanged += (_, _) => SyncRatio(true); height.TextChanged += (_, _) => SyncRatio(false);
        aspect.Checked += (_, _) => { if (int.TryParse(width.Text, out int w) && int.TryParse(height.Text, out int h) && h > 0) resizeRatio = (double)w / h; };
        zoom.SelectionChanged += (_, _) => UpdateZoom(); viewport.SizeChanged += (_, _) => UpdateZoom();
        document.Changed += Refresh;
        Closing += (_, e) => { if (Application.Current is not CaptureApp { Quitting: true } && !ConfirmClose()) e.Cancel = true; };
        Closed += (_, _) => { Canvas.Dispose(); document.Changed -= Refresh; };
        KeyDown += HandleKey;
        Refresh(); ChooseTool("Arrow");
    }

    private static Button Action(string label, string id, Action action, bool accent = false) => Ui.Button(label, id, action, accent);
    private static TextBox Field(string id)
    { var box = new TextBox { FontSize = 12, Padding = new Thickness(7), Margin = new Thickness(0, 6, 5, 6) }; AutomationProperties.SetAutomationId(box, id); return box; }
    private static ComboBox Choice(string id)
    { var box = new ComboBox { FontSize = 12, Padding = new Thickness(5), Margin = new Thickness(0, 6, 5, 8), MinHeight = 30 }; AutomationProperties.SetAutomationId(box, id); return box; }
    private static void AddColors(ComboBox combo, bool transparent)
    {
        combo.DisplayMemberPath = "Name";
        if (transparent) combo.Items.Add(new Swatch("Transparent", "Transparent"));
        foreach (var item in new[] { new Swatch("Coral", "#EB4D3D"), new Swatch("Blue", "#205FDB"), new Swatch("Amber", "#FFD561"), new Swatch("Green", "#16875C"), new Swatch("Slate", "#182A43"), new Swatch("Black", "#000000"), new Swatch("White", "#FFFFFF") }) combo.Items.Add(item);
        combo.SelectedIndex = 0;
    }
    private static void SelectColor(ComboBox combo, string color)
    {
        var choice = combo.Items.Cast<Swatch>().FirstOrDefault(s => s.Value == color);
        if (choice is null) { choice = new Swatch(color, color); combo.Items.Add(choice); }
        combo.SelectedItem = choice;
    }
    private static string SelectedColor(ComboBox combo) => (combo.SelectedItem as Swatch)?.Value ?? "#EB4D3D";
    private static double Number(ComboBox combo, double fallback) => combo.SelectedItem is double value ? value : fallback;
    private static int Parse(TextBox box, int min, int max)
    { if (!int.TryParse(box.Text, out int value) || value < min || value > max) throw new ArgumentException($"Enter a whole number from {min} to {max}."); return value; }
    private static DrawingBrush Checkerboard()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(Ui.Brush("#E8EDF3"), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(Ui.Brush("#E8EDF3"), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, 0, 16, 16) };
    }
    private void Refresh()
    {
        updating = true;
        int selected = Canvas.Selected;
        var state = Document.State; objects.Items.Clear();
        for (int i = 0; i < state.Marks.Length; i++)
        { var mark = state.Marks[i]; objects.Items.Add($"{i + 1}. {mark.Kind}" + (mark.Kind == MarkKind.Text ? " · " + mark.Text.Replace('\n', ' ')[..Math.Min(mark.Text.Length, 30)] : mark.Kind == MarkKind.Counter ? " " + mark.Number : "")); }
        objects.SelectedIndex = selected < state.Marks.Length ? selected : -1;
        width.Text = state.Width.ToString(); height.Text = state.Height.ToString(); resizeRatio = (double)state.Width / state.Height;
        padding.Text = state.Padding.ToString(); SelectColor(background, state.Background);
        undo.IsEnabled = Document.CanUndo; redo.IsEnabled = Document.CanRedo; remove.IsEnabled = Canvas.Selected >= 0;
        dimensions.Text = $"{state.CanvasWidth:N0} × {state.CanvasHeight:N0} px · original {Document.Original.PixelWidth:N0} × {Document.Original.PixelHeight:N0}";
        Title = "Framelet — editor · " + name + (Document.IsDirty ? " *" : ""); updating = false; UpdateZoom();
    }
    private void SyncRatio(bool fromWidth)
    {
        if (updating || aspect.IsChecked != true || resizeRatio <= 0) return;
        updating = true;
        if (fromWidth && int.TryParse(width.Text, out int w) && w > 0) height.Text = Math.Max(1, Math.Round(w / resizeRatio)).ToString();
        else if (!fromWidth && int.TryParse(height.Text, out int h) && h > 0) width.Text = Math.Max(1, Math.Round(h * resizeRatio)).ToString();
        updating = false;
    }
    private void UpdateZoom()
    {
        string mode = zoom.SelectedItem as string ?? "Fit";
        double scale = mode == "Fit" ? Math.Min((Math.Max(70, viewport.ActualWidth) - 50) / Document.State.CanvasWidth, (Math.Max(70, viewport.ActualHeight) - 50) / Document.State.CanvasHeight) : double.Parse(mode.TrimEnd('%')) / 100;
        Canvas.SetZoom(Math.Min(mode == "Fit" ? 1 : 4, scale));
    }
    private void ChooseTool(string tool)
    {
        Canvas.CancelGesture(); Canvas.Tool = tool; Canvas.Cursor = tool == "Select" ? Cursors.SizeAll : Cursors.Cross;
        foreach (var (key, button) in tools) { button.Background = key == tool ? Ui.Blue : Brushes.White; button.Foreground = key == tool ? Brushes.White : Ui.Ink; }
        SetStatus(tool switch { "Text" => "Type a note on the left, then click the image to place it.", "Counter" => "Click to place the next numbered counter.", "Select" => "Click an annotation to select it, then drag to move. Remove is undoable.", "Crop" => "Drag the part of the image to keep. Undo restores the full crop.", "Redact" => "Drag a solid black mask. Projects still contain the original; share a flattened export.", "Blur" or "Pixelate" => "Drag to obscure an area. Use Solid mask for sensitive text; blur and pixelation are not secure redaction.", _ => "Drag on the image to add " + tool.ToLowerInvariant() + ". Esc cancels the current stroke." });
    }
    private void UpdateSelected()
    {
        if (Canvas.Selected < 0) { SetStatus("Select an annotation in the image or Objects list first."); return; }
        Try(() => { var mark = Document.State.Marks[Canvas.Selected]; Document.Replace(Canvas.Selected, mark with { Color = SelectedColor(colors), Width = Number(strokes, mark.Width), FontSize = Number(fonts, mark.FontSize), Text = mark.Kind == MarkKind.Text ? text.Text : mark.Text }); });
    }
    private void RemoveSelected() { if (Canvas.Selected >= 0) Try(() => { Document.Remove(Canvas.Selected); Canvas.Select(-1); }); }
    private void SetStatus(string value, bool error = false) { status.Text = value; status.Foreground = error ? Ui.Brush("#A33E16") : Ui.Muted; }
    private void Try(Action action) { try { Canvas.CancelGesture(); action(); SetStatus("Edit applied"); } catch (Exception ex) { SetStatus(ex.Message, true); } }

    public bool SaveProject()
    {
        var dialog = new SaveFileDialog { Title = "Save editable project — includes original image", Filter = "Framelet project (*.framelet)|*.framelet", DefaultExt = ".framelet", AddExtension = true,
            FileName = projectPath is null ? name + ".framelet" : Path.GetFileName(projectPath), InitialDirectory = projectPath is null ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : Path.GetDirectoryName(projectPath) };
        if (dialog.ShowDialog(this) != true) return false;
        try { EditFiles.SaveProject(Document, dialog.FileName); projectPath = dialog.FileName; SetStatus("Editable project saved, including the original image"); return true; }
        catch (Exception ex) { SetStatus(ex.Message, true); return false; }
    }
    private void Export(bool jpeg)
    {
        var dialog = new SaveFileDialog { Title = jpeg ? "Export JPEG" : "Export PNG", Filter = jpeg ? "JPEG image (*.jpg)|*.jpg" : "PNG image (*.png)|*.png", DefaultExt = jpeg ? ".jpg" : ".png", AddExtension = true,
            FileName = name + "-edited" + (jpeg ? ".jpg" : ".png"), InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        if (dialog.ShowDialog(this) != true) return;
        try { EditFiles.Export(Document, dialog.FileName, jpeg); SetStatus("Flattened " + (jpeg ? "JPEG" : "PNG") + " exported"); }
        catch (Exception ex) { SetStatus(ex.Message, true); }
    }
    public bool ConfirmClose()
    {
        if (!Document.IsDirty) return true;
        var choice = MessageBox.Show(this, "Save changes to an editable project before closing? PNG/JPEG exports do not retain editable layers.", "Unsaved edits", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return choice == MessageBoxResult.No || choice == MessageBoxResult.Yes && SaveProject();
    }
    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Canvas.CancelGesture(); e.Handled = true; return; }
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S) { SaveProject(); e.Handled = true; return; }
        if (Keyboard.FocusedElement is TextBoxBase or ComboBox) return;
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z) { Canvas.CancelGesture(); Document.Undo(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y) { Canvas.CancelGesture(); Document.Redo(); e.Handled = true; }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
        { _ = CopyImage(); e.Handled = true; }
        else if (e.Key == Key.Delete) { RemoveSelected(); e.Handled = true; }
    }
    private async System.Threading.Tasks.Task CopyImage()
    { try { await CaptureService.Copy(Canvas.Image); SetStatus("Edited image copied to clipboard"); } catch (Exception ex) { SetStatus(ex.Message, true); } }
}
