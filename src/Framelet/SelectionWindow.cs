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

    public SelectionWindow(BitmapSource frozenDesktop, DrawingRect desktop, IReadOnlyList<WindowTarget>? targets = null)
    {
        this.desktop = desktop; this.targets = targets;
        Title = targets is null ? "Framelet — select an area" : "Framelet — select a window";
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = true;
        Topmost = true; Cursor = Cursors.Cross; Background = Brushes.Black;
        var grid = new Grid();
        grid.Children.Add(new Image { Source = frozenDesktop, Stretch = Stretch.Fill, IsHitTestVisible = false });
        grid.Children.Add(canvas); canvas.Children.Add(shade); canvas.Children.Add(outline);
        var help = new Border { Background = Ui.Brush("#E6232328"), CornerRadius = new CornerRadius(9), Padding = new Thickness(18, 12, 18, 12),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(16, 24, 16, 0), IsHitTestVisible = false,
            Child = Ui.Text(targets is null ? "Drag to capture an area   ·   Esc to cancel" : "Click a window to capture   ·   Esc to cancel", 15, Brushes.White) };
        grid.Children.Add(help);
        label.Background = Ui.Brush("#E6232328"); label.Padding = new Thickness(8, 5, 8, 5); label.IsHitTestVisible = false;
        canvas.Children.Add(label); Content = grid;
        Loaded += (_, _) =>
        {
            Native.SetWindowPos(new WindowInteropHelper(this).Handle, new IntPtr(-1), desktop.Left, desktop.Top, desktop.Width, desktop.Height, 0x0040);
            Activate(); Focus(); Draw(Rect.Empty);
        };
        SizeChanged += (_, _) => Draw(visualSelection);
        MouseLeftButtonDown += (_, e) =>
        {
            if (targets is not null) { UpdateWindow(); Complete(); return; }
            origin = e.GetPosition(canvas); CaptureMouse();
        };
        MouseMove += (_, e) =>
        {
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
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); else if (e.Key == Key.Enter) { if (targets is not null) UpdateWindow(); Complete(); } };
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
