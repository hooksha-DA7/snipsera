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
using System.Windows.Threading;
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
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Ui.Canvas };
    private readonly ListBox objects = new() { MinHeight = 100, MaxHeight = 220, Margin = new Thickness(0, 6, 0, 12) };
    private readonly TextBox text = new() { Text = "Add a note", AcceptsReturn = true, Height = 29, MaxLength = 2000, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly TextBox width = Field("ResizeWidth"), height = Field("ResizeHeight"), padding = Field("BackgroundPadding");
    private readonly CheckBox aspect = new() { Content = "Keep aspect ratio", IsChecked = true, Margin = new Thickness(0, 5, 0, 8) };
    private readonly ComboBox colors = Choice("AnnotationColor"), strokes = Choice("StrokeWidth"), fonts = Choice("FontSize"), background = Choice("BackgroundColor"), zoom = Choice("CanvasZoom"), styles = Choice("AnnotationStyle"), arrows = Choice("ArrowStyle");
    private readonly StackPanel textOptions = new() { Orientation = Orientation.Horizontal };
    private readonly Border inspector = new();
    private readonly Dictionary<string, Button> tools = [];
    private readonly Button undo, redo, remove;
    private bool updating;
    private readonly Action<EditDocument>? saveHistory;
    private readonly DispatcherTimer historySave = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private EditState historyState = null!;
    private bool savingHistory;
    private double resizeRatio;
    private sealed record Swatch(string Name, string Value);

    public EditorWindow(EditDocument document, Action<System.Windows.Media.Imaging.BitmapSource> pin, Action open, string name = "Capture", string? projectPath = null, Action<EditDocument>? saveHistory = null)
    {
        Document = document; this.name = name; this.projectPath = projectPath; this.saveHistory = saveHistory; historyState = document.State;
        Ui.Setup(this); Ui.UseSystemChrome(this); Width = 1120; Height = 780; MinWidth = 900; MinHeight = 560; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Canvas = new EditCanvas(document) { Report = ex => SetStatus(ex.Message, true) };
        Canvas.MakeMark = (kind, points) => new Mark(kind, points, SelectedColor(colors), Number(strokes, 4), text.Text, Number(fonts, 24),
            Math.Min(999, document.State.Marks.Where(m => m.Kind == MarkKind.Counter).Select(m => m.Number).DefaultIfEmpty(0).Max() + 1), SelectedStyle, kind == MarkKind.Arrow ? SelectedArrow : null, Smooth: kind == MarkKind.Pen && SelectedStyle == MarkStyle.Sketch);
        var dock = new DockPanel { Background = Brushes.Transparent };
        var toolbar = new DockPanel { Background = Ui.Chrome, LastChildFill = false, Margin = new Thickness(0) };
        var files = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 6, 5, 6) };
        files.Children.Add(Ui.IconButton("Open", "Open image or project", "EditorOpen", open));
        files.Children.Add(Ui.IconButton("Project", "Save editable project", "SaveProject", () => SaveProject()));
        toolbar.Children.Add(files);
        var toolRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5, 6, 0, 6) };
        (string tool, string label)[] definitions = [("Select", "Select / move"), ("Crop", "Crop"), ("Arrow", "Arrow"), ("Line", "Line"), ("Rectangle", "Rectangle"), ("Ellipse", "Ellipse"), ("Text", "Text"), ("Counter", "Counter"), ("Highlight", "Highlight"), ("Pen", "Freehand"), ("Blur", "Blur"), ("Pixelate", "Pixelate"), ("Redact", "Solid mask")];
        foreach (var (tool, label) in definitions) { var button = Ui.IconButton(tool, label, "Tool" + tool, () => ChooseTool(tool)); tools.Add(tool, button); toolRow.Children.Add(button); }
        toolbar.Children.Add(new Border { Child = toolRow, BorderBrush = Ui.Line, BorderThickness = new Thickness(1, 0, 1, 0) });
        var edits = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 6, 0, 6) };
        undo = Ui.IconButton("Undo", "Undo · Ctrl+Z", "EditorUndo", () => { Canvas.CancelGesture(); document.Undo(); });
        redo = Ui.IconButton("Redo", "Redo · Ctrl+Y", "EditorRedo", () => { Canvas.CancelGesture(); document.Redo(); });
        remove = Ui.IconButton("Delete", "Delete selected annotation", "RemoveAnnotation", RemoveSelected);
        edits.Children.Add(undo); edits.Children.Add(redo); edits.Children.Add(remove);
        edits.Children.Add(Ui.IconButton("Settings", "Image size, background and layers", "ToggleInspector", () => inspector.Visibility = inspector.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible));
        toolbar.Children.Add(edits); DockPanel.SetDock(toolbar, Dock.Top); dock.Children.Add(toolbar);

        AddColors(colors, false);
        var context = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(14, 0, 10, 0), Height = 42, VerticalAlignment = VerticalAlignment.Center };
        foreach (Swatch swatch in colors.Items)
        {
            var chip = Ui.Button(swatch.Name, "Color" + swatch.Name, () => colors.SelectedItem = swatch); chip.Content = new System.Windows.Shapes.Ellipse { Width = 14, Height = 14, Fill = Ui.Brush(swatch.Value), Stroke = Ui.Brush("#30000000"), StrokeThickness = 1 };
            chip.Width = chip.Height = 26; chip.MinHeight = 26; chip.Padding = new Thickness(4); chip.Margin = new Thickness(0, 0, 3, 0); chip.ToolTip = swatch.Name;
            void State() => chip.BorderBrush = Equals(colors.SelectedItem, swatch) ? Ui.Ink : Brushes.Transparent;
            colors.SelectionChanged += (_, _) => State(); State(); context.Children.Add(chip);
        }
        foreach (double value in new[] { 2d, 4, 8, 12, 20 }) strokes.Items.Add(value); strokes.SelectedItem = 4d;
        foreach (double value in new[] { 12d, 18, 24, 32, 48, 64 }) fonts.Items.Add(value); fonts.SelectedItem = 24d;
        foreach (ArrowStyle arrow in Enum.GetValues<ArrowStyle>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new ArrowSample(arrow)); var label = Ui.Text(arrow.ToString(), 12); label.Margin = new Thickness(8, 0, 0, 0); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            arrows.Items.Add(new ComboBoxItem { Content = row, Tag = arrow });
        }
        arrows.SelectedIndex = 0; arrows.Width = 160; arrows.ToolTip = "Arrow style"; context.Children.Add(arrows);
        styles.Items.Add("Hand-drawn"); styles.Items.Add("Clean"); styles.SelectedIndex = 0;
        context.Children.Add(Label("Stroke")); strokes.Width = 68; context.Children.Add(strokes);
        styles.Width = 140; styles.ToolTip = "Annotation style"; context.Children.Add(styles);
        context.Children.Add(Label("Text")); fonts.Width = 72; context.Children.Add(fonts);
        AutomationProperties.SetAutomationId(text, "AnnotationText"); text.Width = 160; text.Padding = new Thickness(6, 4, 6, 4); text.Margin = new Thickness(4, 0, 5, 0); text.VerticalContentAlignment = VerticalAlignment.Center;
        textOptions.Children.Add(text); context.Children.Add(textOptions);
        var apply = Ui.IconButton("Check", "Update selected annotation", "UpdateAnnotation", UpdateSelected); context.Children.Add(apply);
        DockPanel.SetDock(context, Dock.Top); dock.Children.Add(context);

        var footer = new DockPanel { Background = Ui.Chrome, Height = 46 };
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (string value in new[] { "Fit", "25%", "50%", "100%", "150%", "200%", "400%" }) zoom.Items.Add(value);
        zoom.Width = 88; zoom.SelectedIndex = 0; zoom.Margin = new Thickness(0); zoomRow.Children.Add(zoom); dimensions.Margin = new Thickness(12, 0, 0, 0); dimensions.VerticalAlignment = VerticalAlignment.Center; zoomRow.Children.Add(dimensions); footer.Children.Add(zoomRow);
        var output = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 10, 0) };
        output.Children.Add(Ui.IconButton("Pin", "Pin edited image", "EditorPin", () => pin(Canvas.Image)));
        output.Children.Add(Ui.IconButton("Copy", "Copy edited image · Ctrl+C", "EditorCopy", async () => await CopyImage()));
        var drag = Ui.IconButton("Drag", "Drag edited PNG to another app", "EditorDrag", () => SetStatus("Drag this handle to another app"));
        DragExport.Attach(drag, (Application.Current as CaptureApp)?.History.Root ?? Path.Combine(Path.GetTempPath(), "Snipsera"), () => Canvas.Image, ex => SetStatus(ex.Message, true)); output.Children.Add(drag);
        output.Children.Add(Action("JPEG…", "ExportJpeg", () => Export(true)));
        output.Children.Add(Action("Save image…", "ExportPng", () => Export(false), true));
        DockPanel.SetDock(output, Dock.Right); footer.Children.Insert(0, output); DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);
        status.Margin = new Thickness(14, 5, 14, 5); status.FontSize = 11; status.TextWrapping = TextWrapping.NoWrap; status.TextTrimming = TextTrimming.CharacterEllipsis; DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        var center = new DockPanel();
        var right = new StackPanel { Margin = new Thickness(14) }; right.Children.Add(Ui.Text("Image size", 13, Ui.Ink, true)); right.Children.Add(Ui.Text("Width × height · pixels", 11, Ui.Muted));
        var resize = new UniformGrid { Columns = 2 }; resize.Children.Add(width); resize.Children.Add(height); right.Children.Add(resize); right.Children.Add(aspect);
        right.Children.Add(Action("Resize", "ApplyResize", () => Try(() => document.Resize(Parse(width, 1, 20000), Parse(height, 1, 20000)))));
        var bgLabel = Ui.Text("Background", 13, Ui.Ink, true); bgLabel.Margin = new Thickness(0, 20, 0, 0); right.Children.Add(bgLabel);
        AddColors(background, true); right.Children.Add(background); right.Children.Add(Ui.Text("Padding · pixels", 11, Ui.Muted)); right.Children.Add(padding);
        right.Children.Add(Action("Apply background", "ApplyBackground", () => Try(() => document.Background(Parse(padding, 0, 500), SelectedColor(background)))));
        var layersLabel = Ui.Text("Layers", 13, Ui.Ink, true); layersLabel.Margin = new Thickness(0, 20, 0, 0); right.Children.Add(layersLabel);
        AutomationProperties.SetAutomationId(objects, "AnnotationObjects"); right.Children.Add(objects);
        right.Children.Add(Ui.Text("Projects retain the original pixels. Share a flattened PNG or JPEG when hiding sensitive content.", 11, Ui.Muted));
        inspector.Width = 218; inspector.Background = Ui.Paper; inspector.BorderBrush = Ui.Line; inspector.BorderThickness = new Thickness(1, 0, 0, 0); inspector.Visibility = Visibility.Collapsed;
        inspector.Child = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; DockPanel.SetDock(inspector, Dock.Right); center.Children.Add(inspector);
        var paper = new Border { Child = Canvas, Background = Checkerboard(), Margin = new Thickness(24), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 18, ShadowDepth = 3, Opacity = .15 } };
        viewport.Content = paper; center.Children.Add(viewport); dock.Children.Add(center); Content = dock;

        objects.SelectionChanged += (_, _) => { if (!updating) Canvas.Select(objects.SelectedIndex); };
        Canvas.SelectionChanged += index =>
        {
            if (objects.SelectedIndex != index) objects.SelectedIndex = index;
            remove.IsEnabled = index >= 0;
            if (index < 0) return;
            updating = true;
            var mark = document.State.Marks[index]; SelectColor(colors, mark.Color); strokes.SelectedItem = mark.Width; fonts.SelectedItem = mark.FontSize;
            arrows.Visibility = mark.Kind == MarkKind.Arrow ? Visibility.Visible : Visibility.Collapsed; styles.Visibility = mark.Kind == MarkKind.Arrow ? Visibility.Collapsed : Visibility.Visible;
            styles.SelectedIndex = mark.Style == MarkStyle.Sketch ? 0 : 1;
            arrows.SelectedIndex = (int)(mark.Arrow ?? (mark.Style == MarkStyle.Sketch ? ArrowStyle.Curved : ArrowStyle.Filled));
            if (mark.Kind == MarkKind.Text) { text.Text = mark.Text; textOptions.Visibility = Visibility.Visible; }
            updating = false;
        };
        foreach (var choice in new[] { colors, strokes, fonts, styles, arrows })
            choice.SelectionChanged += (_, _) => { if (!updating && Canvas.Selected >= 0) UpdateSelected(); };
        width.TextChanged += (_, _) => SyncRatio(true); height.TextChanged += (_, _) => SyncRatio(false);
        aspect.Checked += (_, _) => { if (int.TryParse(width.Text, out int w) && int.TryParse(height.Text, out int h) && h > 0) resizeRatio = (double)w / h; };
        zoom.SelectionChanged += (_, _) => UpdateZoom(); viewport.SizeChanged += (_, _) => UpdateZoom();
        document.Changed += Refresh;
        document.Changed += QueueHistorySave;
        historySave.Tick += (_, _) => FlushHistory();
        Closing += (_, e) => { if (Application.Current is not CaptureApp { Quitting: true } && !ConfirmClose()) e.Cancel = true; };
        Closed += (_, _) => { historySave.Stop(); Canvas.Dispose(); document.Changed -= Refresh; document.Changed -= QueueHistorySave; };
        KeyDown += HandleKey;
        Refresh(); ChooseTool("Arrow");
    }

    private ArrowStyle SelectedArrow => arrows.SelectedItem is ComboBoxItem { Tag: ArrowStyle style } ? style : ArrowStyle.Classic;
    private MarkStyle SelectedStyle => styles.SelectedIndex == 0 ? MarkStyle.Sketch : MarkStyle.Clean;
    private static TextBlock Label(string value) { var label = Ui.Text(value, 11, Ui.Muted); label.VerticalAlignment = VerticalAlignment.Center; label.Margin = new Thickness(8, 0, 5, 0); return label; }
    private static Button Action(string label, string id, Action action, bool accent = false) => Ui.Button(label, id, action, accent);
    private static TextBox Field(string id)
    { var box = new TextBox { FontSize = 12, Padding = new Thickness(7), Margin = new Thickness(0, 6, 5, 6) }; AutomationProperties.SetAutomationId(box, id); return box; }
    private static ComboBox Choice(string id)
    { var box = new ComboBox { FontSize = 12, Padding = new Thickness(5), Margin = new Thickness(0, 0, 5, 0), MinHeight = 28, VerticalAlignment = VerticalAlignment.Center }; AutomationProperties.SetAutomationId(box, id); return box; }
    private static void AddColors(ComboBox combo, bool transparent)
    {
        combo.DisplayMemberPath = "Name";
        if (transparent) combo.Items.Add(new Swatch("Transparent", "Transparent"));
        foreach (var item in new[] { new Swatch("Coral", "#F34461"), new Swatch("Blue", "#087FF5"), new Swatch("Amber", "#FFD561"), new Swatch("Green", "#16875C"), new Swatch("Slate", "#182A43"), new Swatch("Black", "#000000"), new Swatch("White", "#FFFFFF") }) combo.Items.Add(item);
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
        dimensions.Text = $"{state.CanvasWidth:N0} × {state.CanvasHeight:N0} px";
        Title = "Snipsera — editor · " + name + (Document.IsDirty ? " *" : ""); updating = false; UpdateZoom();
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
        foreach (var (key, button) in tools) { button.Background = key == tool ? Ui.Blue : Brushes.Transparent; button.Foreground = key == tool ? Ui.OnAccent : Ui.Ink; }
        arrows.Visibility = tool == "Arrow" ? Visibility.Visible : Visibility.Collapsed; styles.Visibility = tool == "Arrow" ? Visibility.Collapsed : Visibility.Visible;
        textOptions.Visibility = tool == "Text" ? Visibility.Visible : Visibility.Collapsed;
        SetStatus(tool switch { "Text" => "Type a note in the toolbar, then click the image to place it.", "Arrow" => "Drag an arrow. Drag its round endpoints to refine it; Curved also has a bend handle.", "Counter" => "Click to place the next numbered counter.", "Select" => "Click an annotation to select it, then drag to move. Remove is undoable.", "Crop" => "Drag the part of the image to keep. Undo restores the full crop.", "Redact" => "Drag a solid black mask. Projects still contain the original; share a flattened export.", "Blur" or "Pixelate" => "Drag to obscure an area. Use Solid mask for sensitive text; blur and pixelation are not secure redaction.", _ => "Drag on the image to add " + tool.ToLowerInvariant() + ". Esc cancels the current stroke." });
    }
    private void UpdateSelected()
    {
        if (Canvas.Selected < 0) { SetStatus("Select an annotation in the image or Objects list first."); return; }
        Try(() => { var mark = Document.State.Marks[Canvas.Selected]; Document.Replace(Canvas.Selected, mark with { Color = SelectedColor(colors), Width = Number(strokes, mark.Width), Style = SelectedStyle, Arrow = mark.Kind == MarkKind.Arrow ? SelectedArrow : mark.Arrow, Smooth = mark.Kind == MarkKind.Pen ? SelectedStyle == MarkStyle.Sketch : mark.Smooth, FontSize = Number(fonts, mark.FontSize), Text = mark.Kind == MarkKind.Text ? text.Text : mark.Text }); });
    }
    private void RemoveSelected() { if (Canvas.Selected >= 0) Try(() => { Document.Remove(Canvas.Selected); Canvas.Select(-1); }); }
    private void SetStatus(string value, bool error = false) { status.Text = value; status.Foreground = error ? Ui.Brush("#A33E16") : Ui.Muted; }
    private void Try(Action action) { try { Canvas.CancelGesture(); action(); SetStatus("Edit applied"); } catch (Exception ex) { SetStatus(ex.Message, true); } }

    public bool SaveProject()
    {
        bool legacyProject = projectPath is not null && Path.GetExtension(projectPath).Equals(".framelet", StringComparison.OrdinalIgnoreCase);
        var dialog = new SaveFileDialog { Title = "Save editable project — includes original image", Filter = "Snipsera project (*.snipsera)|*.snipsera|Legacy Framelet project (*.framelet)|*.framelet", FilterIndex = legacyProject ? 2 : 1, DefaultExt = legacyProject ? ".framelet" : AppIdentity.ProjectExtension, AddExtension = true,
            FileName = projectPath is null ? name + AppIdentity.ProjectExtension : Path.GetFileName(projectPath), InitialDirectory = projectPath is null ? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) : Path.GetDirectoryName(projectPath) };
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
        if (saveHistory is not null) { Canvas.CancelGesture(); return FlushHistory(); }
        if (!Document.IsDirty) return true;
        var choice = ConfirmWindow.Ask(this, "Save your edits?", "Save an editable Snipsera project to keep your annotations and original image. PNG and JPEG exports contain the flattened image only.", "Save project…", "Discard edits");
        return choice == Confirmation.Secondary || choice == Confirmation.Primary && SaveProject();
    }

    private void QueueHistorySave()
    {
        if (saveHistory is null || savingHistory) return;
        historySave.Stop();
        if (Document.State != historyState) historySave.Start();
    }

    public bool FlushHistory()
    {
        historySave.Stop();
        if (saveHistory is null || Document.State == historyState) return true;
        try
        {
            savingHistory = true;
            saveHistory(Document); historyState = Document.State; Document.MarkSaved();
            SetStatus("Saved to history"); return true;
        }
        catch (Exception ex)
        {
            SetStatus("Edits could not be saved to history. Keep this editor open and retry, or export a copy. " + ex.Message, true);
            return false;
        }
        finally { savingHistory = false; }
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
