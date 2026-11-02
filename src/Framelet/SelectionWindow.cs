using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using DrawingRect = System.Drawing.Rectangle;

namespace Framelet;

public sealed class SelectionWindow : Window
{
    private readonly Canvas canvas = new();
    private readonly Path shade = new() { Fill = new SolidColorBrush(Color.FromArgb(120, 8, 18, 33)), IsHitTestVisible = false };
    private readonly Rectangle outline = new() { Stroke = Ui.Blue, StrokeThickness = 2, IsHitTestVisible = false };
    private readonly TextBlock label = Ui.Text("", 14, Brushes.White, true);
    private readonly DrawingRect desktop;
    private readonly IReadOnlyList<WindowTarget>? targets;
    // Fixed-size mode: an exact physical-pixel frame follows the pointer; one click places it.
    private readonly System.Drawing.Size? fixedSize;
    private readonly bool fits;
    private Native.Point pointer;
    private Point? origin;
    private Rect visualSelection;
    public DrawingRect? Selected { get; private set; }
    public WindowTarget? SelectedWindow { get; private set; }
    private bool accepted;

    public Task<bool> PickAsync()
    {
        var result = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => result.TrySetResult(accepted);
        Show(); return result.Task;
    }

    public SelectionWindow(BitmapSource frozenDesktop, DrawingRect desktop, IReadOnlyList<WindowTarget>? targets = null, System.Drawing.Size? fixedSize = null, string? targetHint = null)
    {
        this.desktop = desktop; this.targets = targets; this.fixedSize = fixedSize;
        fits = fixedSize is { } size && size.Width >= 2 && size.Height >= 2 && size.Width <= desktop.Width && size.Height <= desktop.Height;
        // targetHint replaces the window-picking help when the caller supplies other targets (e.g. whole displays).
        Title = fixedSize is not null ? "Framelet — position the frame" : targets is null ? "Framelet — select an area" : targetHint is not null ? "Framelet — choose what to capture" : "Framelet — select a window";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
        Topmost = true; Cursor = fixedSize is not null ? Cursors.SizeAll : Cursors.Cross; Background = Brushes.Black;
        var grid = new Grid();
        grid.Children.Add(new Image { Source = frozenDesktop, Stretch = Stretch.Fill, IsHitTestVisible = false });
        grid.Children.Add(canvas); canvas.Children.Add(shade); canvas.Children.Add(outline);
        var help = new Border { Background = Ui.Brush("#E6232328"), CornerRadius = new CornerRadius(9), Padding = new Thickness(18, 12, 18, 12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 24, 16, 0), IsHitTestVisible = false,
            Child = Ui.Text(fixedSize is { } frame
                ? fits ? $"Move the {frame.Width:N0} × {frame.Height:N0} px frame   ·   Click to place   ·   Arrow keys nudge   ·   Esc to cancel"
                       : $"{frame.Width:N0} × {frame.Height:N0} px is larger than the desktop   ·   Esc to cancel"
                : targets is null ? "Drag to capture an area   ·   Esc to cancel" : targetHint ?? "Click a window to capture   ·   Esc to cancel", 15, Brushes.White) };
        grid.Children.Add(help);
        label.Background = Ui.Brush("#E6232328"); label.Padding = new Thickness(8, 5, 8, 5); label.IsHitTestVisible = false;
        canvas.Children.Add(label); Content = grid;
        Loaded += (_, _) =>
        {
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), desktop.Left, desktop.Top, desktop.Width, desktop.Height, 0x0040);
            Activate(); Focus(); Draw(Rect.Empty);
            if (fixedSize is not null) FollowPointer(true);
        };
        SizeChanged += (_, _) => Draw(visualSelection);
        MouseLeftButtonDown += (_, e) =>
        {
            if (fixedSize is not null) { FollowPointer(); Complete(); return; }
            if (targets is not null) { UpdateWindow(); Complete(); return; }
            origin = e.GetPosition(canvas); CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
            if (fixedSize is not null) { FollowPointer(); return; }
            if (targets is not null) { UpdateWindow(); return; }
            if (origin is Point start)
            {
                Point end = e.GetPosition(canvas);
                var bounds = CaptureGeometry.FromDrag(start.X, start.Y, end.X, end.Y, canvas.ActualWidth, canvas.ActualHeight, desktop);
                Selected = bounds; Draw(ToView(bounds));
            }
        };
        MouseLeftButtonUp += (_, _) => { if (targets is null && origin is not null) { ReleaseMouseCapture(); origin = null; Complete(); } };
        MouseRightButtonDown += (_, _) => Close();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
            else if (e.Key == Key.Enter) { if (fixedSize is not null) { if (Selected is null) FollowPointer(true); } else if (targets is not null) UpdateWindow(); Complete(); }
            else if (fixedSize is not null && e.Key is Key.Left or Key.Right or Key.Up or Key.Down) { Nudge(e.Key, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1); e.Handled = true; }
        };
        Closed += (_, _) => { if (IsMouseCaptured) ReleaseMouseCapture(); };
    }

    private Rect ToView(DrawingRect rect) => new((rect.Left - desktop.Left) * canvas.ActualWidth / desktop.Width,
        (rect.Top - desktop.Top) * canvas.ActualHeight / desktop.Height,
        rect.Width * canvas.ActualWidth / desktop.Width, rect.Height * canvas.ActualHeight / desktop.Height);

    private void UpdateWindow()
    {
        Native.GetCursorPos(out Native.Point point);
        SelectedWindow = targets!.FirstOrDefault(t => t.Bounds.Contains(point.X, point.Y));
        Selected = SelectedWindow?.Bounds;
        Draw(Selected is DrawingRect rect ? ToView(rect) : Rect.Empty);
    }

    // Centres the exact physical frame on the pointer, clamped so it stays entirely on the desktop.
    // Only real pointer movement repositions it, so a keyboard nudge survives redraw-synthesized moves and the confirming click.
    private void FollowPointer(bool force = false)
    {
        if (!fits || !Native.GetCursorPos(out Native.Point point)) return;
        if (!force && Selected is not null && point.X == pointer.X && point.Y == pointer.Y) return;
        pointer = point; var size = fixedSize!.Value;
        Place(point.X - size.Width / 2, point.Y - size.Height / 2);
    }
    // Moves the placed frame by whole physical pixels (Shift: 10). The pointer takes over again on its next move.
    private void Nudge(Key key, int step)
    {
        if (!fits) return;
        if (Selected is not DrawingRect frame) { FollowPointer(true); if (Selected is not DrawingRect placed) return; frame = placed; }
        Place(frame.Left + (key == Key.Left ? -step : key == Key.Right ? step : 0), frame.Top + (key == Key.Up ? -step : key == Key.Down ? step : 0));
    }
    private void Place(int left, int top)
    {
        var size = fixedSize!.Value;
        left = Math.Clamp(left, desktop.Left, desktop.Right - size.Width); top = Math.Clamp(top, desktop.Top, desktop.Bottom - size.Height);
        Selected = new DrawingRect(left, top, size.Width, size.Height); Draw(ToView(Selected.Value));
    }

    private void Complete()
    {
        if (Selected is DrawingRect rect && rect.Width >= 2 && rect.Height >= 2) { accepted = true; Close(); }
    }

    private void Draw(Rect selection)
    {
        visualSelection = selection;
        if (canvas.ActualWidth <= 0 || canvas.ActualHeight <= 0) return;
        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(new Rect(0, 0, canvas.ActualWidth, canvas.ActualHeight)));
        if (!selection.IsEmpty) geometry.Children.Add(new RectangleGeometry(selection));
        shade.Data = geometry;
        outline.Visibility = label.Visibility = selection.IsEmpty ? Visibility.Collapsed : Visibility.Visible;
        if (selection.IsEmpty) return;
        outline.Width = selection.Width; outline.Height = selection.Height;
        Canvas.SetLeft(outline, selection.Left); Canvas.SetTop(outline, selection.Top);
        label.Text = SelectedWindow?.Title ?? $"{Selected?.Width:N0} × {Selected?.Height:N0} px";
        label.MaxWidth = Math.Max(100, Math.Min(500, canvas.ActualWidth - 24));
        Canvas.SetLeft(label, Math.Max(8, Math.Min(selection.Left, canvas.ActualWidth - 300)));
        Canvas.SetTop(label, Math.Max(8, Math.Min(selection.Bottom + 10, canvas.ActualHeight - 42)));
    }
}
