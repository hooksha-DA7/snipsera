using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media.Imaging;

namespace Framelet;

public enum MarkKind { Arrow, Line, Rectangle, Ellipse, Text, Counter, Highlight, Pen, Blur, Pixelate, Redact }
public enum MarkStyle { Clean, Sketch }
public enum ArrowStyle { Classic, Filled, Open, Curved }
public readonly record struct EditPoint(double X, double Y)
{
    [JsonIgnore] public Point Point => new(X, Y);
}
public sealed record Mark(MarkKind Kind, ImmutableArray<EditPoint> Points, string Color = "#EB4D3D",
    double Width = 4, string Text = "", double FontSize = 24, int Number = 1, MarkStyle Style = MarkStyle.Clean,
    ArrowStyle? Arrow = null, double Bend = .2, bool Smooth = false, TextPreset TextPreset = TextPreset.Plain)
{
    [JsonIgnore] public Rect Bounds
    {
        get
        {
            if (Kind == MarkKind.Arrow && Arrow is not null) return ArrowDrawing.Bounds(this);
            var rect = new Rect(new Point(Points.Min(p => p.X), Points.Min(p => p.Y)), new Point(Points.Max(p => p.X), Points.Max(p => p.Y)));
            if (Kind == MarkKind.Text) return TextAnnotation.Bounds(this);
            if (Kind == MarkKind.Counter) return new Rect(rect.X - FontSize, rect.Y - FontSize, FontSize * 2, FontSize * 2);
            if (Kind == MarkKind.Arrow && Style == MarkStyle.Sketch) rect.Inflate(Math.Min(50, (Points[^1].Point - Points[0].Point).Length * .18) + Width * 5, Math.Min(50, (Points[^1].Point - Points[0].Point).Length * .18) + Width * 5);
            return rect;
        }
    }
    public Mark Offset(double x, double y) => this with { Points = Points.Select(p => new EditPoint(p.X + x, p.Y + y)).ToImmutableArray() };
}

public sealed record EditState(int CropX, int CropY, int CropWidth, int CropHeight, int Width, int Height,
    int Padding, string Background, ImmutableArray<Mark> Marks, ImmutableArray<ImageLayer> Layers = default,
    BackgroundOptions? Backdrop = null, bool IsComposition = false)
{
    [JsonIgnore] public ImmutableArray<ImageLayer> Images => Layers.IsDefault ? [] : Layers;
    [JsonIgnore] public double ScaleX => (double)Width / CropWidth;
    [JsonIgnore] public double ScaleY => (double)Height / CropHeight;
    [JsonIgnore] public Rect ContentBounds => CompositionLayout.ContentBounds(this);
    [JsonIgnore] public Rect ContentRect => CompositionLayout.Calculate(this).Content;
    [JsonIgnore] public int CanvasWidth => CompositionLayout.Calculate(this).Width;
    [JsonIgnore] public int CanvasHeight => CompositionLayout.Calculate(this).Height;
    [JsonIgnore] public Rect Crop => new(CropX, CropY, CropWidth, CropHeight);
    public Point ToCanvas(Point original) => new(ContentRect.X + (original.X - ContentBounds.X) * ScaleX, ContentRect.Y + (original.Y - ContentBounds.Y) * ScaleY);
    public Point ToOriginal(Point canvas) => new(ContentBounds.X + (canvas.X - ContentRect.X) / ScaleX, ContentBounds.Y + (canvas.Y - ContentRect.Y) / ScaleY);
}

public sealed class EditDocument
{
    public const long MaximumPixels = 40_000_000;
    public const int MaximumMarks = 500;
    public const int MaximumImages = 32;
    public const long MaximumAssetPixels = 80_000_000;
    public BitmapSource Original { get; }
    public EditState State { get; private set; }
    public bool IsDirty => State != saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;
    private EditState saved;
    private readonly List<EditState> undo = [];
    private readonly List<EditState> redo = [];
    private readonly Dictionary<string, BitmapSource> images = new(StringComparer.Ordinal);
    internal IReadOnlyDictionary<string, BitmapSource> ImageAssets => images;

    public EditDocument(BitmapSource original, EditState? state = null, IReadOnlyDictionary<string, BitmapSource>? imageAssets = null)
    {
        ValidateSize(original.PixelWidth, original.PixelHeight);
        Original = original.IsFrozen ? original : original.Clone(); Original.Freeze();
        State = state ?? new(0, 0, original.PixelWidth, original.PixelHeight, original.PixelWidth, original.PixelHeight, 0, "Transparent", []);
        State = State with { Layers = State.Images };
        Validate(State, Original.PixelWidth, Original.PixelHeight);
        if (imageAssets is not null)
            foreach (var pair in imageAssets)
            {
                ValidateSize(pair.Value.PixelWidth, pair.Value.PixelHeight);
                var bitmap = pair.Value.IsFrozen ? pair.Value : pair.Value.Clone(); bitmap.Freeze(); images.Add(pair.Key, bitmap);
            }
        ValidateAssets(State); saved = State;
    }

    public static void ValidateSize(int width, int height)
    {
        if (width < 1 || height < 1 || width > 20000 || height > 20000 || (long)width * height > MaximumPixels)
            throw new ArgumentException("Choose image dimensions from 1–20,000 pixels, with at most 40 million pixels in total.");
    }
    public static bool IsColor(string color) => color is not null && Regex.IsMatch(color, "^#[0-9A-Fa-f]{6}$");

    public static void Validate(EditState state, int originalWidth, int originalHeight)
    {
        if (state.CropX < 0 || state.CropY < 0 || state.CropWidth < 1 || state.CropHeight < 1 ||
            (long)state.CropX + state.CropWidth > originalWidth || (long)state.CropY + state.CropHeight > originalHeight)
            throw new ArgumentException("The crop must stay within the original image.");
        if (state.Padding is < 0 or > 500 || state.Background != "Transparent" && !IsColor(state.Background))
            throw new ArgumentException("Choose a valid background and padding from 0–500 pixels.");
        CompositionLayout.Validate(state);
        ValidateSize(state.Width, state.Height);
        if (state.Marks.IsDefault || state.Marks.Length > MaximumMarks) throw new ArgumentException("A project supports up to 500 annotations.");
        var allowed = state.IsComposition ? new Rect(-20000, -20000, 40000, 40000) : new Rect(0, 0, originalWidth, originalHeight);
        foreach (var layer in state.Images) allowed.Union(layer.Bounds);
        long points = 0;
        foreach (var mark in state.Marks)
        {
            if (mark is null) throw new ArgumentException("This project contains an empty annotation.");
            if (!Enum.IsDefined(mark.Kind) || !Enum.IsDefined(mark.Style) || !Enum.IsDefined(mark.TextPreset) || mark.Arrow is ArrowStyle arrow && !Enum.IsDefined(arrow) ||
                !double.IsFinite(mark.Bend) || mark.Bend is < -2 or > 2 || mark.Points.IsDefaultOrEmpty || mark.Points.Length > 10000 ||
                !IsColor(mark.Color) || !double.IsFinite(mark.Width) || mark.Width is < 1 or > 100 || !double.IsFinite(mark.FontSize) || mark.FontSize is < 8 or > 200 ||
                mark.Text is null || mark.Text.Length > 2000 || mark.Number is < 1 or > 999)
                throw new ArgumentException("This project contains an unsupported annotation.");
            int count = mark.Points.Length;
            if (mark.Kind is MarkKind.Text or MarkKind.Counter ? count != 1 : mark.Kind != MarkKind.Pen && count != 2)
                throw new ArgumentException("An annotation has an invalid number of points.");
            points += count;
            foreach (var p in mark.Points)
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !allowed.Contains(p.Point))
                    throw new ArgumentException("Annotation points must stay within the image composition.");
        }
        if (points > 50000) throw new ArgumentException("This project has too many pen points. Split it into smaller projects.");
        ValidateSize(state.CanvasWidth, state.CanvasHeight);
    }

    private void Commit(EditState next)
    {
        if (next == State) return;
        Validate(next, Original.PixelWidth, Original.PixelHeight);
        ValidateAssets(next);
        undo.Add(State); if (undo.Count > 100) undo.RemoveAt(0);
        redo.Clear(); State = next; PruneAssets(); Changed?.Invoke();
    }
    public void Add(Mark mark) => Commit(State with { Marks = State.Marks.Add(mark) });
    public void Replace(int index, Mark mark) => Commit(State with { Marks = State.Marks.SetItem(index, mark) });
    public void Remove(int index) => Commit(State with { Marks = State.Marks.RemoveAt(index) });
    public void Resize(int width, int height) => Commit(State with { Width = width, Height = height });
    public void Background(int padding, string color) => Commit(State with { Padding = padding, Background = color });
    public void SetBackground(int padding, string color, BackgroundOptions? options) => Commit(State with { Padding = padding, Background = color, Backdrop = options });
    public BitmapSource GetImage(string id) => images.TryGetValue(id, out var image) ? image : throw new ArgumentException("The image layer is missing its embedded image.");
    public int AddImage(BitmapSource image, Point position, double? width = null)
    {
        ValidateSize(image.PixelWidth, image.PixelHeight);
        if (State.Images.Length >= MaximumImages) throw new ArgumentException("A composition supports up to 32 added images.");
        long pixels = (long)Original.PixelWidth * Original.PixelHeight + images.Values.Sum(v => (long)v.PixelWidth * v.PixelHeight);
        if (pixels + (long)image.PixelWidth * image.PixelHeight > MaximumAssetPixels)
            throw new ArgumentException("This project has reached its image memory limit. Save and reopen it to discard unused undo images, or use a smaller image.");
        string id = Guid.NewGuid().ToString("N"); double targetWidth = width ?? image.PixelWidth;
        var layer = new ImageLayer(id, position.X, position.Y, targetWidth, targetWidth * image.PixelHeight / image.PixelWidth);
        var frozen = image.IsFrozen ? image : image.Clone(); frozen.Freeze(); images.Add(id, frozen);
        try { Commit(State with { Layers = State.Images.Add(layer), IsComposition = true }); }
        catch { if (!State.Images.Any(layer => layer.Id == id)) images.Remove(id); throw; }
        return State.Images.Length - 1;
    }
    public void ReplaceImage(int index, ImageLayer layer)
    {
        if (layer.Id != State.Images[index].Id) throw new ArgumentException("An image edit cannot change its embedded source.");
        Commit(State with { Layers = State.Images.SetItem(index, layer) });
    }
    public void RemoveImage(int index) => Commit(State with { Layers = State.Images.RemoveAt(index) });
    public void MoveImage(int from, int to)
    {
        if (from < 0 || to < 0 || from >= State.Images.Length || to >= State.Images.Length) throw new ArgumentOutOfRangeException(nameof(to));
        if (from == to) return;
        var layer = State.Images[from];
        Commit(State with { Layers = State.Images.RemoveAt(from).Insert(to, layer) });
    }
    public bool AutoBalance()
    {
        var crop = ContentBalance.Find(Original, State.Crop);
        foreach (var mark in State.Marks)
        {
            Rect visible = mark.Bounds; visible.Inflate(Math.Max(2, mark.Width), Math.Max(2, mark.Width)); visible.Intersect(State.Crop);
            if (!visible.IsEmpty) crop.Union(visible);
        }
        int left = (int)Math.Floor(crop.Left), top = (int)Math.Floor(crop.Top);
        crop = new Rect(left, top, (int)Math.Ceiling(crop.Right) - left, (int)Math.Ceiling(crop.Bottom) - top);
        bool changed = crop != State.Crop || State.Backdrop is { AlignmentX: not .5 } or { AlignmentY: not .5 };
        if (!changed) return false;
        int width = Math.Max(1, (int)Math.Round(crop.Width * State.ScaleX));
        int height = Math.Max(1, (int)Math.Round(crop.Height * State.ScaleY));
        Commit(State with { CropX = (int)crop.X, CropY = (int)crop.Y, CropWidth = (int)crop.Width, CropHeight = (int)crop.Height,
            Width = width, Height = height, Backdrop = (State.Backdrop ?? new BackgroundOptions()) with { AlignmentX = .5, AlignmentY = .5 } });
        return changed;
    }
    private void ValidateAssets(EditState state)
    {
        if (state.Images.Any(layer => !images.ContainsKey(layer.Id))) throw new ArgumentException("The project is missing an embedded image.");
        if ((long)Original.PixelWidth * Original.PixelHeight + images.Values.Sum(v => (long)v.PixelWidth * v.PixelHeight) > MaximumAssetPixels)
            throw new ArgumentException("The project's decoded images exceed the supported memory limit.");
    }
    private void PruneAssets()
    {
        var live = State.Images.Select(i => i.Id).Concat(undo.Concat(redo).SelectMany(s => s.Images).Select(i => i.Id)).ToHashSet(StringComparer.Ordinal);
        foreach (string id in images.Keys.Where(id => !live.Contains(id)).ToArray()) images.Remove(id);
    }
    public void Crop(Rect area)
    {
        area.Intersect(State.Crop);
        if (area.IsEmpty || area.Width < 1 || area.Height < 1) throw new ArgumentException("Drag a crop at least 1 × 1 pixel inside the image.");
        int x = (int)Math.Floor(area.X), y = (int)Math.Floor(area.Y);
        int width = Math.Min(Original.PixelWidth, (int)Math.Ceiling(area.Right)) - x;
        int height = Math.Min(Original.PixelHeight, (int)Math.Ceiling(area.Bottom)) - y;
        Commit(State with { CropX = x, CropY = y, CropWidth = width, CropHeight = height, Width = width, Height = height });
    }
    public void Undo() { if (undo.Count == 0) return; redo.Add(State); State = undo[^1]; undo.RemoveAt(undo.Count - 1); Changed?.Invoke(); }
    public void Redo() { if (redo.Count == 0) return; undo.Add(State); State = redo[^1]; redo.RemoveAt(redo.Count - 1); Changed?.Invoke(); }
    public void MarkSaved() { saved = State; Changed?.Invoke(); }
}
