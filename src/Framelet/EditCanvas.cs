using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

// Pointer methods use document coordinates so the same interaction runs at every zoom and DPI.
public sealed class EditCanvas : Grid
{
    public EditDocument Document { get; }
    public string Tool { get; set; } = "Arrow";
    public Func<MarkKind, ImmutableArray<EditPoint>, Mark> MakeMark { get; set; } = (kind, points) => new(kind, points);
    public Action<Exception>? Report { get; set; }
    public event Action<int>? SelectionChanged;
    public event Action<string>? ToolRequested;
    public int Selected { get; private set; } = -1;
    public int SelectedImage { get; private set; } = -1;
    public BitmapSource Image { get; private set; }
    public double Zoom { get; private set; } = 1;
    internal bool IsEditingText => textInput is not null;
    private Point? start;
    private readonly List<EditPoint> points = [];
    private Mark? draft, changedMark, originalMark;
    private ImageLayer? changedImage, originalImage;
    private Rect crop = Rect.Empty;
    private int handle = -1;
    private bool moving;
    private TextBox? textInput;
    private Mark? textMark;
    private int textIndex = -1;
    private bool finishingText;
    private EditState renderedState = null!;
    private EditState? previewState;
    /// <summary>The state on screen: an uncommitted preview while a panel control is being dragged, otherwise the document state.</summary>
    public EditState ViewState => previewState ?? Document.State;
    public bool IsPreviewing => previewState is not null;
    private readonly Pen selectionPen = new(Ui.Blue, 1.5);

    public EditCanvas(EditDocument document)
    {
        Document = document; Focusable = true; Cursor = Cursors.Cross; Background = Brushes.Transparent;
        AutomationProperties.SetName(this, "Image editing canvas"); AutomationProperties.SetAutomationId(this, "EditCanvas");
        Image = EditRenderer.Render(document); renderedState = document.State; UpdateSize();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        document.Changed += DocumentChanged;
        MouseLeftButtonDown += Begin; MouseMove += Drag; MouseLeftButtonUp += Finish;
        LostMouseCapture += (_, _) => CancelGesture();
    }
    public void Dispose() => Document.Changed -= DocumentChanged;
    public void Select(int index)
    {
        Selected = index >= 0 && index < Document.State.Marks.Length ? index : -1;
        SelectedImage = -1; SelectionChanged?.Invoke(Selected); InvalidateVisual();
    }
    public void SelectImage(int index)
    {
        Selected = -1; SelectedImage = index >= 0 && index < Document.State.Images.Length ? index : -1;
        SelectionChanged?.Invoke(-1); InvalidateVisual();
    }
    public void SetZoom(double zoom) { Zoom = Math.Clamp(zoom, .02, 4); UpdateSize(); }
    /// <summary>Shows <paramref name="state"/> without committing it, so undo history and the saved document stay unchanged.
    /// Any later document change replaces the preview.</summary>
    public void Preview(EditState state)
    {
        if (state == Document.State) { EndPreview(); return; }
        var image = EditRenderer.Render(Document, stateOverride: state);
        previewState = state; Image = image; renderedState = state; UpdateSize();
    }
    public void EndPreview() { if (previewState is null) return; previewState = null; Refresh(); }
    private void DocumentChanged() { previewState = null; Refresh(); }
    public void Refresh()
    {
        string? selectedImageId = SelectedImage >= 0 && SelectedImage < renderedState.Images.Length ? renderedState.Images[SelectedImage].Id : null;
        var view = ViewState;
        if (renderedState != view) { Image = EditRenderer.Render(Document, stateOverride: previewState); renderedState = view; }
        if (Selected >= Document.State.Marks.Length) Selected = -1;
        if (selectedImageId is not null) SelectedImage = Array.FindIndex(Document.State.Images.ToArray(), image => image.Id == selectedImageId);
        UpdateSize();
    }
    private void UpdateSize()
    {
        Width = Image.PixelWidth * Zoom; Height = Image.PixelHeight * Zoom;
        PositionTextInput(); InvalidateVisual();
    }
    public void CancelGesture()
    {
        start = null; draft = changedMark = originalMark = null; changedImage = originalImage = null;
        crop = Rect.Empty; handle = -1; moving = false; points.Clear();
        if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        var state = ViewState;
        // Keep layout stable during dragging; the composition expands once the gesture commits.
        if (changedMark is not null || textInput is not null)
            EditRenderer.DrawDocument(dc, Document, replacementIndex: textInput is null ? Selected : textIndex,
                replacement: textInput is null ? changedMark : textMark! with { Text = "" });
        else if (changedImage is not null && SelectedImage >= 0)
        {
            // Moving an image is a lightweight overlay over an image-free document snapshot.
            EditRenderer.DrawDocument(dc, Document, state with { Layers = state.Images.SetItem(SelectedImage, changedImage) }, layoutState: state);
        }
        else dc.DrawImage(Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        var origin = state.ToCanvas(new Point(0, 0));
        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        dc.PushTransform(new ScaleTransform(state.ScaleX, state.ScaleY));
        if (textInput is not null && textMark is not null) TextAnnotation.DrawDecoration(dc, textMark with { Text = textInput.Text });
        if (draft is not null) EditRenderer.DrawMark(dc, draft);
        double radius = 4 / Zoom / Math.Max(.01, state.ScaleX);
        var pen = new Pen(Ui.Blue, 1.5 / Zoom / Math.Max(.01, state.ScaleX));
        if (!crop.IsEmpty) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 32, 95, 219)), pen, crop);
        if (Selected >= 0 && Selected < state.Marks.Length && textInput is null)
        {
            var mark = changedMark ?? state.Marks[Selected];
            if (mark.Kind is not (MarkKind.Arrow or MarkKind.Line)) dc.DrawRectangle(null, pen, mark.Bounds);
            foreach (var point in EditInteraction.Handles(mark)) dc.DrawEllipse(Brushes.White, pen, point, radius, radius);
        }
        if (SelectedImage >= 0 && SelectedImage < state.Images.Length)
        {
            var layer = changedImage ?? state.Images[SelectedImage]; var rect = new Rect(layer.X, layer.Y, layer.Width, layer.Height);
            dc.DrawRectangle(null, pen, rect);
            foreach (var point in EditInteraction.Corners(rect)) dc.DrawEllipse(Brushes.White, pen, point, radius, radius);
        }
        dc.Pop(); dc.Pop(); dc.Pop();
    }
    public Point DocumentPoint(Point view) => ViewState.ToOriginal(new Point(view.X / Zoom, view.Y / Zoom));
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        if (textInput is not null && textInput.IsMouseOver) return;
        if (!CommitText()) return;
        Focus();
        PointerDown(DocumentPoint(e.GetPosition(this)), e.ClickCount, Keyboard.Modifiers.HasFlag(ModifierKeys.Alt));
        if (start is not null) CaptureMouse();
        e.Handled = true;
    }
    private void Drag(object sender, MouseEventArgs e)
    {
        var at = DocumentPoint(e.GetPosition(this));
        if (start is not null && IsMouseCaptured) PointerMove(at);
        else if (!IsEditingText) Cursor = Tool != "Crop" && EditInteraction.HitMark(Document.State, at, 6 / Zoom / Document.State.ScaleX) >= 0
            ? (Tool == "Text" ? Cursors.IBeam : Cursors.SizeAll) : Tool == "Select" ? Cursors.Arrow : Tool == "Text" ? Cursors.IBeam : Cursors.Cross;
    }
    private void Finish(object sender, MouseButtonEventArgs e)
    { if (start is not null) { PointerUp(DocumentPoint(e.GetPosition(this))); e.Handled = true; } }

    internal void PointerDown(Point p, int clicks = 1, bool ignoreObjects = false)
    {
        CancelGesture(); var state = Document.State; double tolerance = 9 / Zoom / state.ScaleX;
        if (!ignoreObjects && Tool != "Crop" && Selected >= 0 && Selected < state.Marks.Length)
        {
            var mark = state.Marks[Selected]; var handles = EditInteraction.Handles(mark);
            handle = Array.FindIndex(handles, q => (q - p).Length <= tolerance);
            if (handle >= 0) { start = p; originalMark = mark; return; }
        }
        if (!ignoreObjects && Tool != "Crop" && SelectedImage >= 0 && SelectedImage < state.Images.Length)
        {
            var layer = state.Images[SelectedImage];
            handle = Array.FindIndex(EditInteraction.Corners(new Rect(layer.X, layer.Y, layer.Width, layer.Height)), q => (q - p).Length <= tolerance);
            if (handle >= 0) { start = p; originalImage = layer; return; }
        }
        int hit = EditInteraction.HitMark(state, p, tolerance);
        if (!ignoreObjects && hit >= 0 && Tool != "Crop")
        {
            Select(hit); originalMark = state.Marks[hit];
            if (originalMark.Kind == MarkKind.Text && (clicks >= 2 || Tool == "Text")) { BeginText(hit, p); return; }
            start = p; moving = true; return;
        }
        if (Tool == "Select")
        {
            for (int i = state.Images.Length - 1; i >= 0; i--)
            {
                var layer = state.Images[i];
                if (!new Rect(layer.X, layer.Y, layer.Width, layer.Height).Contains(p)) continue;
                SelectImage(i); originalImage = layer; start = p; moving = true; return;
            }
            Select(-1); return;
        }
        if (!(Tool == "Crop" ? state.Crop : state.ContentBounds).Contains(p)) return;
        Select(-1);
        if (Tool == "Text") { BeginText(-1, p); return; }
        start = p; points.Add(new EditPoint(p.X, p.Y));
        if (Tool is "Counter" or "Stamp")
        {
            try
            {
                Document.Add(MakeMark(Tool == "Stamp" ? MarkKind.Text : MarkKind.Counter, points.ToImmutableArray()));
                Select(Document.State.Marks.Length - 1);
            }
            catch (Exception ex) { Report?.Invoke(ex); }
            CancelGesture();
        }
    }
    internal void PointerMove(Point p)
    {
        if (start is not Point origin) return;
        if ((p - origin).Length < .01 && Tool != "Pen")
        { changedMark = null; changedImage = null; draft = null; crop = Rect.Empty; InvalidateVisual(); return; }
        try
        {
            var state = Document.State;
            if (originalImage is not null)
            {
                changedImage = moving ? originalImage with { X = originalImage.X + p.X - origin.X, Y = originalImage.Y + p.Y - origin.Y }
                    : EditInteraction.ResizeImage(originalImage, handle, p);
            }
            else
            {
                var area = Tool == "Crop" ? state.Crop : state.ContentBounds;
                p = new Point(Math.Clamp(p.X, area.Left, area.Right), Math.Clamp(p.Y, area.Top, area.Bottom));
                if (originalMark is not null)
                {
                    changedMark = moving ? EditInteraction.Move(originalMark, p - origin, area) : EditInteraction.Resize(originalMark, handle, p);
                    if (!moving && changedMark.Kind is MarkKind.Text or MarkKind.Counter)
                    {
                        var at = changedMark.Points[0];
                        changedMark = changedMark.Offset(Math.Clamp(at.X, area.Left, area.Right) - at.X, Math.Clamp(at.Y, area.Top, area.Bottom) - at.Y);
                    }
                }
                else if (Tool == "Crop") crop = new Rect(origin, p);
                else
                {
                    if (Tool == "Pen") { if (points.Count < 10000 && (p - points[^1].Point).Length >= 1) points.Add(new(p.X, p.Y)); }
                    else { if (points.Count == 1) points.Add(new(p.X, p.Y)); else points[1] = new(p.X, p.Y); }
                    draft = MakeMark(Enum.Parse<MarkKind>(Tool), points.ToImmutableArray());
                }
            }
        }
        catch (Exception ex) { Report?.Invoke(ex); CancelGesture(); }
        InvalidateVisual();
    }
    internal void PointerUp(Point p)
    {
        if (start is null) return;
        try
        {
            PointerMove(p);
            if (changedImage is not null) { if (changedImage != originalImage) Document.ReplaceImage(SelectedImage, changedImage); }
            else if (changedMark is not null) { if (originalMark is not null && !SameMark(changedMark, originalMark)) Document.Replace(Selected, changedMark); }
            else if (Tool == "Crop" && !crop.IsEmpty && crop.Width >= 1 && crop.Height >= 1) { Document.Crop(crop); Select(-1); }
            else if (draft is not null && (draft.Kind == MarkKind.Pen || (draft.Points[^1].Point - draft.Points[0].Point).Length >= 1))
            {
                Document.Add(draft); Select(Document.State.Marks.Length - 1);
                if (Tool != "Pen") RequestSelect();
            }
        }
        catch (Exception ex) { Report?.Invoke(ex); }
        finally { CancelGesture(); }
    }
    private void RequestSelect() { Tool = "Select"; Cursor = Cursors.Arrow; ToolRequested?.Invoke("Select"); }
    private static bool SameMark(Mark left, Mark right) => left with { Points = right.Points } == right && left.Points.SequenceEqual(right.Points);

    internal void BeginText(int index, Point point)
    {
        if (!CommitText()) return;
        textIndex = index;
        textMark = index >= 0 ? Document.State.Marks[index] : MakeMark(MarkKind.Text, [new(point.X, point.Y)]) with { Text = "" };
        textInput = new TextBox { Text = textMark.Text, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            MaxLength = 2000, FontFamily = TextAnnotation.Typeface(textMark).FontFamily, FontWeight = TextAnnotation.Typeface(textMark).Weight, Background = Brushes.Transparent, Foreground = TextAnnotation.Foreground(textMark), CaretBrush = TextAnnotation.Foreground(textMark),
            BorderBrush = Ui.Blue, BorderThickness = new Thickness(1), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, MinWidth = 100, ToolTip = "Type here · Enter to finish · Shift+Enter for a new line · Esc to cancel" };
        AutomationProperties.SetAutomationId(textInput, "InlineAnnotationText"); AutomationProperties.SetName(textInput, "Text on image");
        textInput.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CancelText(); e.Handled = true; Focus(); }
            else if (e.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift) { CommitText(); e.Handled = true; Focus(); }
        };
        textInput.LostKeyboardFocus += (_, _) => { if (!finishingText) CommitText(); };
        textInput.TextChanged += (_, _) => InvalidateVisual();
        Children.Add(textInput); PositionTextInput(); InvalidateVisual(); textInput.Focus(); textInput.SelectAll();
    }
    private void PositionTextInput()
    {
        if (textInput is null || textMark is null) return;
        var inset = TextAnnotation.Insets(textMark);
        var at = ViewState.ToCanvas(textMark.Points[0].Point + new Vector(inset.Left, inset.Top));
        textInput.Margin = new Thickness(at.X * Zoom, at.Y * Zoom, 0, 0);
        textInput.FontSize = Math.Max(1, textMark.FontSize * ViewState.ScaleX * Zoom);
        textInput.MaxWidth = Math.Max(100, Width - at.X * Zoom);
    }
    public bool CommitText()
    {
        if (textInput is null || finishingText) return true;
        var value = textInput.Text; var mark = textMark! with { Text = value };
        try
        {
            finishingText = true;
            if (!string.IsNullOrWhiteSpace(value))
            {
                if (textIndex >= 0) { if (!SameMark(Document.State.Marks[textIndex], mark)) Document.Replace(textIndex, mark); } else Document.Add(mark);
                Select(textIndex >= 0 ? textIndex : Document.State.Marks.Length - 1);
            }
            else if (textIndex >= 0) { Document.Remove(textIndex); Select(-1); }
            Children.Remove(textInput); textInput = null; textMark = null; textIndex = -1;
            RequestSelect(); InvalidateVisual(); return true;
        }
        catch (Exception ex) { Report?.Invoke(ex); return false; }
        finally { finishingText = false; }
    }
    public void CancelText()
    {
        finishingText = true; if (textInput is not null) Children.Remove(textInput);
        textInput = null; textMark = null; textIndex = -1; finishingText = false; InvalidateVisual();
    }
}
