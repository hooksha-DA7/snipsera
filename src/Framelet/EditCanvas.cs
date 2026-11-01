using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public sealed class EditCanvas : FrameworkElement
{
    public EditDocument Document { get; }
    public string Tool { get; set; } = "Arrow";
    public Func<MarkKind, ImmutableArray<EditPoint>, Mark> MakeMark { get; set; } = (kind, points) => new(kind, points);
    public Action<Exception>? Report { get; set; }
    public event Action<int>? SelectionChanged;
    public int Selected { get; private set; } = -1;
    public BitmapSource Image { get; private set; }
    public double Zoom { get; private set; } = 1;
    private Point? start;
    private readonly List<EditPoint> points = [];
    private Mark? draft;
    private Rect crop = Rect.Empty;
    private Vector move;
    private int arrowHandle = -1;
    private Mark? arrowOriginal, arrowDraft;
    private BitmapSource? dragImage;
    private readonly Pen selectionPen = new(Ui.Blue, 1.5) { DashStyle = DashStyles.Dash };

    public EditCanvas(EditDocument document)
    {
        Document = document; Focusable = true; Cursor = Cursors.Cross;
        AutomationProperties.SetName(this, "Image editing canvas"); AutomationProperties.SetAutomationId(this, "EditCanvas");
        Image = EditRenderer.Render(document); UpdateSize();
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        document.Changed += Refresh;
        MouseLeftButtonDown += Begin; MouseMove += Drag; MouseLeftButtonUp += Finish;
        LostMouseCapture += (_, _) => CancelGesture();
    }
    public void Dispose() => Document.Changed -= Refresh;
    public void Select(int index) { Selected = index >= 0 && index < Document.State.Marks.Length ? index : -1; SelectionChanged?.Invoke(Selected); InvalidateVisual(); }
    public void SetZoom(double zoom) { Zoom = Math.Clamp(zoom, 0.02, 4); UpdateSize(); }
    public void Refresh()
    {
        Image = EditRenderer.Render(Document);
        if (Selected >= Document.State.Marks.Length) Selected = -1;
        UpdateSize();
    }
    private void UpdateSize() { Width = Image.PixelWidth * Zoom; Height = Image.PixelHeight * Zoom; InvalidateVisual(); }
    public void CancelGesture() { start = null; draft = null; arrowHandle = -1; arrowOriginal = arrowDraft = null; dragImage = null; crop = Rect.Empty; points.Clear(); move = new Vector(); if (IsMouseCaptured) ReleaseMouseCapture(); InvalidateVisual(); }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); dc.PushTransform(new ScaleTransform(Zoom, Zoom));
        dc.DrawImage(dragImage ?? Image, new Rect(0, 0, Image.PixelWidth, Image.PixelHeight));
        var state = Document.State;
        dc.PushClip(new RectangleGeometry(new Rect(state.Padding, state.Padding, state.Width, state.Height)));
        dc.PushTransform(new TranslateTransform(state.Padding, state.Padding));
        dc.PushTransform(new ScaleTransform((double)state.Width / state.CropWidth, (double)state.Height / state.CropHeight));
        dc.PushTransform(new TranslateTransform(-state.CropX, -state.CropY));
        if (draft is not null) EditRenderer.DrawMark(dc, draft);
        if (!crop.IsEmpty) dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(45, 32, 95, 219)), selectionPen, crop);
        if (Selected >= 0 && Selected < state.Marks.Length)
        {
            var selected = arrowDraft ?? state.Marks[Selected];
            if (selected.Kind == MarkKind.Arrow && selected.Arrow is not null)
            {
                double radius = 4 / Zoom * state.CropWidth / state.Width;
                foreach (Point point in Handles(selected)) dc.DrawEllipse(Brushes.White, new Pen(Ui.Blue, radius / 3), point, radius, radius);
            }
            else { var bounds = selected.Bounds; bounds.Offset(move); bounds.Inflate(4, 4); dc.DrawRectangle(null, selectionPen, bounds); }
        }
    }
    private static Point[] Handles(Mark mark) => mark.Arrow == ArrowStyle.Curved ? [mark.Points[0].Point, mark.Points[1].Point, ArrowDrawing.BendHandle(mark)] : [mark.Points[0].Point, mark.Points[1].Point];
    private Point OriginalPoint(MouseEventArgs e)
    {
        Point view = e.GetPosition(this); view.X /= Zoom; view.Y /= Zoom;
        Point p = Document.State.ToOriginal(view);
        return new Point(Math.Clamp(p.X, Document.State.Crop.Left, Document.State.Crop.Right), Math.Clamp(p.Y, Document.State.Crop.Top, Document.State.Crop.Bottom));
    }
    private void Begin(object sender, MouseButtonEventArgs e)
    {
        Focus(); CancelGesture();
        Point local = e.GetPosition(this);
        var state = Document.State;
        if (!new Rect(state.Padding * Zoom, state.Padding * Zoom, state.Width * Zoom, state.Height * Zoom).Contains(local)) return;
        Point p = OriginalPoint(e);
        if (Selected >= 0 && Selected < state.Marks.Length && state.Marks[Selected] is { Kind: MarkKind.Arrow, Arrow: not null } selected)
        {
            var handles = Handles(selected); double tolerance = 9 / Zoom * state.CropWidth / state.Width;
            for (int i = 0; i < handles.Length; i++) if ((p - handles[i]).Length <= tolerance)
            { arrowHandle = i; arrowOriginal = selected; start = p; CaptureMouse(); e.Handled = true; return; }
        }
        if (Tool == "Select")
        {
            int found = -1;
            for (int i = state.Marks.Length - 1; i >= 0; i--)
            { var bounds = state.Marks[i].Bounds; bounds.Inflate(Math.Max(6, state.Marks[i].Width), Math.Max(6, state.Marks[i].Width)); if (bounds.Contains(p)) { found = i; break; } }
            Select(found); if (found < 0) return;
        }
        else Select(-1);
        start = p; CaptureMouse(); points.Add(new EditPoint(p.X, p.Y));
        if (Tool is "Text" or "Counter")
        {
            try { Document.Add(MakeMark(Enum.Parse<MarkKind>(Tool), points.ToImmutableArray())); Select(Document.State.Marks.Length - 1); }
            catch (Exception ex) { Report?.Invoke(ex); }
            CancelGesture();
        }
        e.Handled = true;
    }
    private void Drag(object sender, MouseEventArgs e)
    {
        if (start is not Point origin || !IsMouseCaptured) return;
        Point p = OriginalPoint(e);
        if (arrowHandle >= 0 && arrowOriginal is Mark arrow)
        {
            var changed = arrowHandle == 2 ? arrow with { Bend = ArrowDrawing.BendAt(arrow, p) } : arrow with { Points = arrow.Points.SetItem(arrowHandle, new EditPoint(p.X, p.Y)) };
            if ((changed.Points[1].Point - changed.Points[0].Point).Length >= 2)
            { arrowDraft = changed; dragImage = EditRenderer.Render(Document, Selected, changed); }
        }
        else if (Tool == "Select")
        {
            var mark = Document.State.Marks[Selected];
            double minX = mark.Points.Min(q => q.X), maxX = mark.Points.Max(q => q.X), minY = mark.Points.Min(q => q.Y), maxY = mark.Points.Max(q => q.Y);
            move = new Vector(Math.Clamp(p.X - origin.X, -minX, Document.Original.PixelWidth - maxX), Math.Clamp(p.Y - origin.Y, -minY, Document.Original.PixelHeight - maxY));
        }
        else if (Tool == "Crop") crop = new Rect(origin, p);
        else
        {
            if (Tool == "Pen")
            { if (points.Count < 10000 && (p - points[^1].Point).Length >= 1) points.Add(new EditPoint(p.X, p.Y)); }
            else { if (points.Count == 1) points.Add(new EditPoint(p.X, p.Y)); else points[1] = new EditPoint(p.X, p.Y); }
            draft = MakeMark(Enum.Parse<MarkKind>(Tool), points.ToImmutableArray());
        }
        InvalidateVisual();
    }
    private void Finish(object sender, MouseButtonEventArgs e)
    {
        if (start is null) return;
        try
        {
            Drag(sender, e);
            if (arrowHandle >= 0 && arrowDraft is not null) Document.Replace(Selected, arrowDraft);
            else if (Tool == "Select" && Selected >= 0 && move.Length > 0) Document.Replace(Selected, Document.State.Marks[Selected].Offset(move.X, move.Y));
            else if (Tool == "Crop" && !crop.IsEmpty && crop.Width >= 1 && crop.Height >= 1) { Document.Crop(crop); Select(-1); }
            else if (Tool != "Select" && Tool != "Crop" && draft is not null && (draft.Kind == MarkKind.Pen || (draft.Points[^1].Point - draft.Points[0].Point).Length >= 1))
            { Document.Add(draft); Select(Document.State.Marks.Length - 1); }
        }
        catch (Exception ex) { Report?.Invoke(ex); }
        finally { CancelGesture(); }
    }
}
