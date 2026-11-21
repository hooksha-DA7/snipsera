using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Framelet;

public sealed partial class EditorWindow
{
    private readonly ComboBox textPresets = Choice("TextPreset"), stamps = Choice("StampSymbol"), ratios = Choice("BackgroundRatio");
    private readonly StackPanel backgroundPanel = new(), sizePanel = new(), layersPanel = new();
    private readonly TextBlock panelTitle = Ui.Text("Background", 15, Ui.Ink, true);
    private readonly Slider backgroundPadding = new() { Minimum = 0, Maximum = 500, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly Slider corners = new() { Minimum = 0, Maximum = 80, TickFrequency = 1, IsSnapToTickEnabled = true };
    private readonly CheckBox shadow = new() { Content = "Soft shadow", Margin = new Thickness(0, 8, 0, 8) };
    private readonly TextBlock paddingValue = Ui.Text("0 px", 12, Ui.Muted);
    private readonly System.Collections.Generic.Dictionary<string, Button> gradientButtons = [];
    private readonly System.Collections.Generic.List<Button> alignmentButtons = [];
    private readonly TextBlock alignmentHint = Ui.Text("Choose an aspect ratio to align the image.", 11, Ui.Muted);
    private string backgroundPreset = "Solid";
    private double alignmentX = .5, alignmentY = .5;
    private sealed record Ratio(string Name, double Value);
    private TextPreset SelectedTextPreset => textPresets.SelectedItem is ComboBoxItem { Tag: TextPreset preset } ? preset : TextPreset.Plain;

    private void BuildTextOptions()
    {
        string[] names = ["Plain", "Outline", "Shadow", "Label", "Soft label", "Callout", "Marker"];
        foreach (TextPreset preset in Enum.GetValues<TextPreset>())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(new TextPresetSample(preset)); var label = Ui.Text(names[(int)preset], 12);
            label.Margin = new Thickness(8, 0, 0, 0); label.VerticalAlignment = VerticalAlignment.Center; row.Children.Add(label);
            textPresets.Items.Add(new ComboBoxItem { Content = row, Tag = preset });
        }
        textPresets.SelectedIndex = 0; textPresets.Width = 160; textPresets.ToolTip = "Text appearance — seven styles";
        textOptions.Children.Add(textPresets);
        textOptions.Children.Add(Ui.IconButton("Pen", "Edit selected text on image", "EditSelectedText", () =>
        { if (Canvas.Selected >= 0 && Document.State.Marks[Canvas.Selected].Kind == MarkKind.Text) Canvas.BeginText(Canvas.Selected, Document.State.Marks[Canvas.Selected].Points[0].Point); else SetStatus("Click the image to start typing."); }));
    }
    private void BuildInspector()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var close = Ui.IconButton("Close", "Close panel", "CloseEditorPanel", () => inspector.Visibility = Visibility.Collapsed);
        DockPanel.SetDock(close, Dock.Right); heading.Children.Add(close); heading.Children.Add(panelTitle);
        DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
        var panels = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; panels.Children.Add(backgroundPanel); panels.Children.Add(sizePanel); panels.Children.Add(layersPanel);
        root.Children.Add(new ScrollViewer { Content = panels, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
        inspector.Width = 264; inspector.Background = Ui.Chrome; inspector.BorderBrush = Ui.Line; inspector.BorderThickness = new Thickness(0, 0, 1, 0);
        inspector.Child = root; inspector.Visibility = Visibility.Collapsed;

        sizePanel.Children.Add(Ui.Text("Image size", 13, Ui.Ink, true));
        sizePanel.Children.Add(Ui.Text("Resize the screenshot and its annotations together.", 12, Ui.Muted));
        var resize = new UniformGrid { Columns = 2 }; resize.Children.Add(width); resize.Children.Add(height); sizePanel.Children.Add(resize); sizePanel.Children.Add(aspect);
        sizePanel.Children.Add(Action("Resize image", "ApplyResize", () => Try(() => Document.Resize(Parse(width, 1, 20000), Parse(height, 1, 20000)))));
        var layerLabel = Ui.Text("Layers", 13, Ui.Ink, true); layerLabel.Margin = new Thickness(0, 24, 0, 4); layersPanel.Children.Add(layerLabel);
        layersPanel.Children.Add(Ui.Text("Select an object, then drag it on the image. Use the corners to resize.", 12, Ui.Muted));
        AutomationProperties.SetAutomationId(objects, "AnnotationObjects"); layersPanel.Children.Add(objects);
        layersPanel.Children.Add(Action("Add image…", "AddCompositionImage", AddImages));
        layersPanel.Children.Add(Action("From history…", "AddHistoryImage", AddHistoryImage));
        layersPanel.Children.Add(Action("Bring image forward", "LayerForward", () => MoveImageOrder(true)));
        layersPanel.Children.Add(Action("Send image backward", "LayerBackward", () => MoveImageOrder(false)));
        var note = Ui.Text("Drag a screenshot here or paste an image with Ctrl+V. Added images remain editable in history and saved projects.", 12, Ui.Muted);
        note.Margin = new Thickness(0, 12, 0, 0); layersPanel.Children.Add(note);

        backgroundPanel.Children.Add(Ui.Text("Frame your capture", 12, Ui.Muted));
        var presets = new UniformGrid { Columns = 5, Margin = new Thickness(0, 8, 0, 12) };
        foreach (string preset in CompositionLayout.Presets.Where(p => p != "Solid"))
        {
            var example = Document.State with { Background = "#F5F5F7", Backdrop = new BackgroundOptions(preset) };
            var swatch = new Border { Height = 32, Width = 32, Background = CompositionLayout.BackgroundBrush(example), CornerRadius = new CornerRadius(5) };
            var content = swatch;
            var button = Ui.Button(preset, "Background" + preset, () => { backgroundPreset = preset; if (Document.State.Padding == 0) backgroundPadding.Value = 48; ApplyBackgroundOptions(); });
            button.Content = content; button.Padding = new Thickness(2); button.Margin = new Thickness(0, 0, 4, 4); button.ToolTip = preset + " background"; presets.Children.Add(button); gradientButtons.Add(preset, button);
        }
        backgroundPanel.Children.Add(presets);
        AddColors(background, true); background.Margin = new Thickness(0, 0, 0, 14); background.ToolTip = "Solid background color";
        backgroundPanel.Children.Add(Ui.Text("Solid color", 12, Ui.Muted)); backgroundPanel.Children.Add(background);
        background.SelectionChanged += (_, _) => { if (!updating) { backgroundPreset = "Solid"; if (SelectedColor(background) != "Transparent" && Document.State.Padding == 0) backgroundPadding.Value = 48; ApplyBackgroundOptions(); } };
        backgroundPanel.Children.Add(Ui.Text("Padding", 12, Ui.Muted)); backgroundPanel.Children.Add(paddingValue);
        AutomationProperties.SetAutomationId(backgroundPadding, "BackgroundPaddingSlider"); backgroundPadding.Margin = new Thickness(0, 4, 0, 14);
        backgroundPanel.Children.Add(backgroundPadding); backgroundPadding.ValueChanged += (_, _) => paddingValue.Text = $"{backgroundPadding.Value:N0} px";
        CommitSlider(backgroundPadding);
        backgroundPanel.Children.Add(Ui.Text("Aspect ratio", 12, Ui.Muted));
        foreach (var ratio in new[] { new Ratio("Automatic", 0), new Ratio("Square · 1:1", 1), new Ratio("Landscape · 16:9", 16d / 9), new Ratio("Portrait · 9:16", 9d / 16), new Ratio("Classic · 4:3", 4d / 3), new Ratio("Photo · 3:2", 1.5) }) ratios.Items.Add(ratio);
        ratios.DisplayMemberPath = "Name"; ratios.SelectedIndex = 0; ratios.Margin = new Thickness(0, 4, 0, 14); backgroundPanel.Children.Add(ratios);
        ratios.SelectionChanged += (_, _) => { if (!updating) ApplyBackgroundOptions(); };
        backgroundPanel.Children.Add(Ui.Text("Alignment", 12, Ui.Muted));
        var alignment = new UniformGrid { Columns = 3, Width = 114, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 5, 0, 14) };
        string[] labels = ["Top left", "Top", "Top right", "Left", "Center", "Right", "Bottom left", "Bottom", "Bottom right"];
        for (int i = 0; i < 9; i++)
        {
            int cell = i; var button = Ui.Button(labels[i], "BackgroundAlign" + i, () => { alignmentX = cell % 3 / 2d; alignmentY = cell / 3 / 2d; ApplyBackgroundOptions(); });
            button.Content = "·"; button.Width = 32; button.FontSize = 24; button.Padding = new Thickness(0); button.Margin = new Thickness(2); button.ToolTip = labels[i]; alignment.Children.Add(button); alignmentButtons.Add(button);
        }
        backgroundPanel.Children.Add(alignment);
        alignmentHint.Margin = new Thickness(0, 0, 0, 12); backgroundPanel.Children.Add(alignmentHint);
        backgroundPanel.Children.Add(Ui.Text("Corners", 12, Ui.Muted)); corners.Margin = new Thickness(0, 5, 0, 8);
        AutomationProperties.SetAutomationId(corners, "BackgroundCorners"); backgroundPanel.Children.Add(corners); CommitSlider(corners);
        backgroundPanel.Children.Add(shadow); shadow.Checked += (_, _) => { if (!updating) ApplyBackgroundOptions(); }; shadow.Unchecked += (_, _) => { if (!updating) ApplyBackgroundOptions(); };
        backgroundPanel.Children.Add(Action("Auto-balance", "BackgroundAutoBalance", () => Try(() => Document.AutoBalance())));
        var help = Ui.Text("Trims matching empty margins and centers the content. Original pixels are preserved; Undo restores the previous layout.", 11, Ui.Muted);
        help.Margin = new Thickness(0, 6, 0, 12); backgroundPanel.Children.Add(help);
        backgroundPanel.Children.Add(Action("Remove background", "RemoveBackground", () => Try(() => Document.SetBackground(0, "Transparent", new BackgroundOptions()))));
    }
    private void CommitSlider(Slider slider)
    {
        slider.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => ApplyBackgroundOptions()));
        slider.PreviewMouseLeftButtonUp += (_, _) => ApplyBackgroundOptions();
        slider.KeyUp += (_, _) => ApplyBackgroundOptions();
    }
    private void ApplyBackgroundOptions()
    {
        if (updating) return;
        Try(() => Document.SetBackground((int)backgroundPadding.Value, SelectedColor(background), new BackgroundOptions(backgroundPreset,
            (ratios.SelectedItem as Ratio)?.Value ?? 0, alignmentX, alignmentY, corners.Value, shadow.IsChecked == true)));
    }
    private void RefreshBackground()
    {
        var state = Document.State; var options = state.Backdrop ?? new BackgroundOptions(); backgroundPreset = options.Preset;
        backgroundPadding.Value = state.Padding; corners.Value = options.CornerRadius; shadow.IsChecked = options.Shadow;
        alignmentX = options.AlignmentX; alignmentY = options.AlignmentY;
        foreach (var (name, button) in gradientButtons) { button.BorderBrush = name == options.Preset ? Ui.Blue : Ui.Line; button.BorderThickness = new Thickness(name == options.Preset ? 2 : 1); AutomationProperties.SetItemStatus(button, name == options.Preset ? "Selected" : ""); }
        ratios.SelectedItem = ratios.Items.Cast<Ratio>().FirstOrDefault(r => Math.Abs(r.Value - options.AspectRatio) < .0001) ?? ratios.Items[0];
        var frame = CompositionLayout.Calculate(state);
        bool hasSpace = options.AspectRatio > 0 && (frame.Width - frame.Content.Width - state.Padding * 2 > 1 || frame.Height - frame.Content.Height - state.Padding * 2 > 1);
        for (int i = 0; i < alignmentButtons.Count; i++)
        {
            var button = alignmentButtons[i]; bool selected = Math.Abs(alignmentX - i % 3 / 2d) < .001 && Math.Abs(alignmentY - i / 3 / 2d) < .001;
            button.IsEnabled = hasSpace; button.BorderBrush = selected ? Ui.Blue : Ui.Line; button.BorderThickness = new Thickness(selected ? 2 : 1);
            button.Content = selected ? "•" : "·"; AutomationProperties.SetItemStatus(button, selected ? "Selected" : "");
        }
        alignmentHint.Text = options.AspectRatio == 0 ? "Choose an aspect ratio to align the image." : !hasSpace ? "This image already fills the chosen ratio." : "Positions the image in the extra space.";
    }
    private void ShowPanel(string name)
    {
        Canvas.CommitText(); inspector.Visibility = Visibility.Visible; panelTitle.Text = name;
        backgroundPanel.Visibility = name == "Background" ? Visibility.Visible : Visibility.Collapsed;
        sizePanel.Visibility = name == "Image size" ? Visibility.Visible : Visibility.Collapsed;
        layersPanel.Visibility = name == "Layers" ? Visibility.Visible : Visibility.Collapsed;
    }
    private void SelectLayer(int index)
    { if (index >= Document.State.Marks.Length) Canvas.SelectImage(index - Document.State.Marks.Length); else Canvas.Select(index); }
    private void MoveImageOrder(bool forward)
    {
        if (Canvas.SelectedImage < 0) { SetStatus("Select an added image first."); return; }
        int from = Canvas.SelectedImage, to = Math.Clamp(from + (forward ? 1 : -1), 0, Document.State.Images.Length - 1);
        if (from == to) return;
        Try(() => { Document.MoveImage(from, to); Canvas.SelectImage(to); });
    }
    private void AddImages()
    {
        var dialog = new OpenFileDialog { Title = "Add images to this composition", Filter = "PNG and JPEG images|*.png;*.jpg;*.jpeg", Multiselect = true, CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) ImportFiles(dialog.FileNames);
    }
    private void AddHistoryImage()
    {
        if (Application.Current is not CaptureApp app) { SetStatus("History is available when the app is running."); return; }
        var menu = new ContextMenu();
        foreach (var entry in app.History.Entries.Take(30))
        {
            var capture = entry; var item = new MenuItem { Header = $"{entry.Mode} · {entry.Width} × {entry.Height}" };
            item.Click += (_, _) => Try(() => AddImageLayer(app.History.LoadImage(capture), null)); menu.Items.Add(item);
        }
        if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "No captures in history", IsEnabled = false });
        menu.PlacementTarget = inspector; menu.IsOpen = true;
    }
    private static bool ImageFile(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg";
    internal bool ImportFiles(string[] files, Point? at = null)
    {
        if (files.Length == 0 || !Canvas.CommitText()) return false;
        int added = 0;
        foreach (string file in files)
        {
            try { if (!ImageFile(file)) throw new InvalidDataException("Drop PNG or JPEG images to combine them."); AddImageLayer(EditFiles.LoadImage(file), at); added++; if (at is Point p) at = p + new Vector(24, 24); }
            catch (Exception ex) { SetStatus((added == 0 ? "" : $"Added {added} image(s). ") + ex.Message, true); return false; }
        }
        SetStatus("Image added. Drag to position it; use the corner handles to resize.");
        return true;
    }
    private void AddImageLayer(BitmapSource image, Point? at)
    {
        double targetWidth = Math.Min(image.PixelWidth, Document.State.CropWidth * .8);
        Point center = at ?? new Point(Document.State.ContentBounds.X + Document.State.ContentBounds.Width / 2, Document.State.ContentBounds.Y + Document.State.ContentBounds.Height / 2);
        Point position = center - new Vector(targetWidth / 2, targetWidth * image.PixelHeight / image.PixelWidth / 2);
        int index = Document.AddImage(image, position, targetWidth); Canvas.SelectImage(index); ChooseTool("Select");
    }
    private void ImagesDragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.Length > 0 && paths.All(ImageFile);
        e.Effects = IsCanvasDrop(e.GetPosition(viewport)) && e.AllowedEffects.HasFlag(DragDropEffects.Copy) && (files || e.Data.GetDataPresent(DataFormats.Bitmap)) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true;
    }
    private void ImagesDrop(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        if (IsCanvasDrop(e.GetPosition(viewport)) && e.AllowedEffects.HasFlag(DragDropEffects.Copy) && ImportDroppedImage(e.Data, Canvas.DocumentPoint(e.GetPosition(Canvas)))) e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }
    private bool IsCanvasDrop(Point point) => new Rect(viewport.RenderSize).Contains(point);
    internal bool ImportDroppedImage(IDataObject data, Point at)
    {
        try
        {
            if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files) return ImportFiles(files, at);
            if (data.GetData(DataFormats.Bitmap) is not BitmapSource image || !Canvas.CommitText()) return false;
            AddImageLayer(image, at); SetStatus("Image added. Drag to position it; use the corner handles to resize."); return true;
        }
        catch (Exception ex) { SetStatus(ex.Message, true); return false; }
    }
    private void PasteImage()
    { try { if (Clipboard.ContainsImage() && Clipboard.GetImage() is BitmapSource image) Try(() => AddImageLayer(image, null)); else SetStatus("Copy an image first, then paste it here."); } catch (Exception ex) { SetStatus(ex.Message, true); } }
}

internal sealed class TextPresetSample(TextPreset preset) : FrameworkElement
{
    public TextPresetSample() : this(TextPreset.Plain) { }
    protected override Size MeasureOverride(Size availableSize) => new(56, 28);
    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawRoundedRectangle(Ui.Surface, null, new Rect(0, 0, 56, 28), 4, 4);
        dc.PushTransform(new ScaleTransform(.65, .65));
        TextAnnotation.Draw(dc, new Mark(MarkKind.Text, [new(4, 2)], "#B83861", Text: "Aa", FontSize: 23, TextPreset: preset)); dc.Pop();
    }
}
