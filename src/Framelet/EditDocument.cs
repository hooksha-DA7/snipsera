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
public readonly record struct EditPoint(double X, double Y)
{
    [JsonIgnore] public Point Point => new(X, Y);
}
public sealed record Mark(MarkKind Kind, ImmutableArray<EditPoint> Points, string Color = "#EB4D3D",
    double Width = 4, string Text = "", double FontSize = 24, int Number = 1, MarkStyle Style = MarkStyle.Clean)
{
    [JsonIgnore] public Rect Bounds
    {
        get
        {
            var rect = new Rect(new Point(Points.Min(p => p.X), Points.Min(p => p.Y)), new Point(Points.Max(p => p.X), Points.Max(p => p.Y)));
            if (Kind == MarkKind.Text) return new Rect(rect.Location, new Size(Math.Max(FontSize, Text.Split('\n').Max(l => l.Length) * FontSize * 0.65), Text.Split('\n').Length * FontSize * 1.4));
            if (Kind == MarkKind.Counter) return new Rect(rect.X - FontSize, rect.Y - FontSize, FontSize * 2, FontSize * 2);
            if (Kind == MarkKind.Arrow && Style == MarkStyle.Sketch) rect.Inflate(Math.Min(50, (Points[^1].Point - Points[0].Point).Length * .18) + Width * 5, Math.Min(50, (Points[^1].Point - Points[0].Point).Length * .18) + Width * 5);
            return rect;
        }
    }
    public Mark Offset(double x, double y) => this with { Points = Points.Select(p => new EditPoint(p.X + x, p.Y + y)).ToImmutableArray() };
}

public sealed record EditState(int CropX, int CropY, int CropWidth, int CropHeight, int Width, int Height,
    int Padding, string Background, ImmutableArray<Mark> Marks)
{
    [JsonIgnore] public int CanvasWidth => checked(Width + Padding * 2);
    [JsonIgnore] public int CanvasHeight => checked(Height + Padding * 2);
    [JsonIgnore] public Rect Crop => new(CropX, CropY, CropWidth, CropHeight);
    public Point ToCanvas(Point original) => new(Padding + (original.X - CropX) * Width / CropWidth, Padding + (original.Y - CropY) * Height / CropHeight);
    public Point ToOriginal(Point canvas) => new(CropX + (canvas.X - Padding) * CropWidth / Width, CropY + (canvas.Y - Padding) * CropHeight / Height);
}

public sealed class EditDocument
{
    public const long MaximumPixels = 40_000_000;
    public const int MaximumMarks = 500;
    public BitmapSource Original { get; }
    public EditState State { get; private set; }
    public bool IsDirty => State != saved;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public event Action? Changed;
    private EditState saved;
    private readonly List<EditState> undo = [];
    private readonly List<EditState> redo = [];

    public EditDocument(BitmapSource original, EditState? state = null)
    {
        ValidateSize(original.PixelWidth, original.PixelHeight);
        Original = original.IsFrozen ? original : original.Clone(); Original.Freeze();
        State = state ?? new(0, 0, original.PixelWidth, original.PixelHeight, original.PixelWidth, original.PixelHeight, 0, "Transparent", []);
        Validate(State, Original.PixelWidth, Original.PixelHeight); saved = State;
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
        ValidateSize(state.Width, state.Height); ValidateSize(state.CanvasWidth, state.CanvasHeight);
        if (state.Marks.IsDefault || state.Marks.Length > MaximumMarks) throw new ArgumentException("A project supports up to 500 annotations.");
        long points = 0;
        foreach (var mark in state.Marks)
        {
            if (mark is null || !Enum.IsDefined(mark.Kind) || !Enum.IsDefined(mark.Style) || mark.Points.IsDefaultOrEmpty || mark.Points.Length > 10000 ||
                !IsColor(mark.Color) || !double.IsFinite(mark.Width) || mark.Width is < 1 or > 100 || !double.IsFinite(mark.FontSize) || mark.FontSize is < 8 or > 200 ||
                mark.Text is null || mark.Text.Length > 2000 || mark.Number is < 1 or > 999)
                throw new ArgumentException("This project contains an unsupported annotation.");
            int count = mark.Points.Length;
            if (mark.Kind is MarkKind.Text or MarkKind.Counter ? count != 1 : mark.Kind != MarkKind.Pen && count != 2)
                throw new ArgumentException("An annotation has an invalid number of points.");
            points += count;
            foreach (var p in mark.Points)
                if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || p.X < 0 || p.Y < 0 || p.X > originalWidth || p.Y > originalHeight)
                    throw new ArgumentException("Annotation points must stay within the original image.");
        }
        if (points > 50000) throw new ArgumentException("This project has too many pen points. Split it into smaller projects.");
    }

    private void Commit(EditState next)
    {
        if (next == State) return;
        Validate(next, Original.PixelWidth, Original.PixelHeight);
        undo.Add(State); if (undo.Count > 100) undo.RemoveAt(0);
        redo.Clear(); State = next; Changed?.Invoke();
    }
    public void Add(Mark mark) => Commit(State with { Marks = State.Marks.Add(mark) });
    public void Replace(int index, Mark mark) => Commit(State with { Marks = State.Marks.SetItem(index, mark) });
    public void Remove(int index) => Commit(State with { Marks = State.Marks.RemoveAt(index) });
    public void Resize(int width, int height) => Commit(State with { Width = width, Height = height });
    public void Background(int padding, string color) => Commit(State with { Padding = padding, Background = color });
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
