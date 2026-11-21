using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Framelet;

// The preview owns its own wheel/drag interaction; it never scrolls the source app.
internal sealed class ScrollPreviewPanel : Grid
{
    private readonly Image image = new() { Stretch = Stretch.Fill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
    private readonly ScrollViewer viewport = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, CanContentScroll = false, Background = Brushes.Transparent };
    private readonly TextBlock caption = Ui.Text("", 10, Ui.Muted);
    private readonly Border topFade = new() { Height = 16, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
    private readonly Border thumb = new() { Width = 4, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, CornerRadius = new CornerRadius(2), IsHitTestVisible = false };
    private readonly Button fit, latest;
    private Point? dragStart;
    private double dragOffset;
    private bool updateQueued;
    internal bool FitAll { get; private set; }
    internal bool FollowLatest { get; private set; } = true;
    internal double Offset => viewport.VerticalOffset;
    internal double ImageWidth => image.Width;
    internal double ImageHeight => image.Height;

    internal ScrollPreviewPanel()
    {
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(22) });
        RowDefinitions.Add(new RowDefinition());
        caption.HorizontalAlignment = HorizontalAlignment.Left; caption.VerticalAlignment = VerticalAlignment.Center;
        caption.TextTrimming = TextTrimming.CharacterEllipsis;
        var header = new Grid(); header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(caption); Children.Add(header);
        AutomationProperties.SetAutomationId(image, "ScrollPreview");
        AutomationProperties.SetAutomationId(viewport, "ScrollPreviewViewport");
        AutomationProperties.SetName(viewport, "Captured image preview");
        AutomationProperties.SetHelpText(viewport, "Scroll or drag to inspect the captured image. Fit shows the whole capture; Latest follows new content.");
        viewport.Content = image;
        var body = new Grid { Margin = new Thickness(0, 2, 0, 0), ClipToBounds = true }; body.Children.Add(viewport); body.Children.Add(topFade); body.Children.Add(thumb);
        SetRow(body, 1); Children.Add(body);
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        var controls = new StackPanel { Orientation = Orientation.Horizontal };
        fit = Control("Area", "Fit entire capture", "ScrollPreviewFit", ToggleFit);
        latest = Control("Scrolling", "Follow latest captured content", "ScrollPreviewLatest", ShowLatest);
        controls.Children.Add(fit); controls.Children.Add(latest); SetColumn(controls, 1); header.Children.Add(controls);
        viewport.PreviewMouseWheel += (_, e) => { Inspect(-e.Delta / 120d * 64); e.Handled = true; };
        image.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { ToggleFit(); e.Handled = true; return; }
            if (FitAll) ToggleFit();
            FollowLatest = false; dragStart = e.GetPosition(viewport); dragOffset = viewport.VerticalOffset;
            image.CaptureMouse(); image.Cursor = Cursors.SizeNS; e.Handled = true; UpdateButtons();
        };
        image.MouseMove += (_, e) =>
        {
            if (dragStart is Point origin) { viewport.ScrollToVerticalOffset(dragOffset + origin.Y - e.GetPosition(viewport).Y); e.Handled = true; }
        };
        image.MouseLeftButtonUp += (_, e) => { EndDrag(); e.Handled = true; };
        image.LostMouseCapture += (_, _) => EndDrag();
        viewport.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Home) { Inspect(-double.MaxValue); e.Handled = true; }
            else if (e.Key == Key.End) { ShowLatest(); e.Handled = true; }
            else if (e.Key is Key.Down or Key.PageDown) { Inspect(e.Key == Key.Down ? 48 : viewport.ViewportHeight * .8); e.Handled = true; }
            else if (e.Key is Key.Up or Key.PageUp) { Inspect(e.Key == Key.Up ? -48 : -viewport.ViewportHeight * .8); e.Handled = true; }
        };
        viewport.SizeChanged += (_, _) => QueueLayout();
        viewport.ScrollChanged += (_, _) => UpdateInspectionAffordances();
        viewport.MouseEnter += (_, _) => UpdateInspectionAffordances(); viewport.MouseLeave += (_, _) => UpdateInspectionAffordances();
        RefreshTheme();
    }
    private static Button Control(string symbol, string name, string id, Action action)
    {
        var button = new Button { Content = new UiIcon(symbol, 13), ToolTip = name, Padding = new Thickness(0), Margin = new Thickness(1, 0, 0, 0), MinWidth = 0, Width = 22, Height = 22 };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, name);
        button.Click += (_, _) => action(); return button;
    }
    internal void SetImage(BitmapSource source, int width, int height, int frames)
    {
        if (!ReferenceEquals(image.Source, source)) { image.Source = source; QueueLayout(); }
        caption.Text = $"{width:N0} × {height:N0}";
        AutomationProperties.SetName(image, $"Capture preview, {width:N0} by {height:N0} pixels, {frames:N0} frames");
    }
    internal void ToggleFit() { FitAll = !FitAll; EndDrag(); QueueLayout(); UpdateButtons(); }
    internal void ShowLatest() { FitAll = false; FollowLatest = true; EndDrag(); QueueLayout(); UpdateButtons(); }
    internal void Inspect(double distance)
    {
        if (FitAll) { FitAll = false; UpdateImageSize(); viewport.UpdateLayout(); }
        FollowLatest = false;
        viewport.ScrollToVerticalOffset(Math.Clamp(viewport.VerticalOffset + distance, 0, viewport.ScrollableHeight)); UpdateButtons();
    }
    private void EndDrag()
    {
        dragStart = null; image.Cursor = Cursors.Hand;
        if (image.IsMouseCaptured) image.ReleaseMouseCapture();
    }
    internal void RefreshTheme()
    {
        UpdateButtons(); thumb.Background = Ui.Muted;
        var color = (Ui.Floating as SolidColorBrush)?.Color ?? Colors.White;
        topFade.Background = new LinearGradientBrush(color, Color.FromArgb(0, color.R, color.G, color.B), new Point(0, 0), new Point(0, 1));
        UpdateInspectionAffordances();
    }
    private void UpdateInspectionAffordances()
    {
        topFade.Opacity = !FitAll && viewport.VerticalOffset > 1 && !SystemParameters.HighContrast ? 1 : 0;
        double available = viewport.ActualHeight;
        thumb.Height = Math.Min(available, Math.Max(24, available * available / Math.Max(1, viewport.ExtentHeight)));
        thumb.Margin = new Thickness(0, Math.Max(0, available - thumb.Height) * viewport.VerticalOffset / Math.Max(1, viewport.ScrollableHeight), 0, 0);
        thumb.Opacity = !FitAll && viewport.IsMouseOver && viewport.ScrollableHeight > 0 ? 1 : 0;
    }
    private void UpdateButtons()
    {
        FloatingButtons.Apply(fit, FitAll); FloatingButtons.Apply(latest, !FitAll && FollowLatest);
        AutomationProperties.SetItemStatus(fit, FitAll ? "Showing entire capture" : "Showing capture at preview width");
        AutomationProperties.SetItemStatus(latest, FitAll ? "Showing entire capture" : FollowLatest ? "Following new content" : "Inspecting earlier content");
    }
    private void QueueLayout()
    {
        if (updateQueued) return; updateQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            updateQueued = false; UpdateImageSize(); viewport.UpdateLayout();
            if (FitAll) viewport.ScrollToTop(); else if (FollowLatest) viewport.ScrollToEnd();
        }));
    }
    private void UpdateImageSize()
    {
        if (image.Source is not BitmapSource source || viewport.ActualWidth <= 0 || viewport.ActualHeight <= 0) return;
        double scale = Math.Min(1, viewport.ActualWidth / source.PixelWidth);
        if (FitAll) scale = Math.Min(scale, viewport.ActualHeight / source.PixelHeight);
        image.Width = Math.Max(1, source.PixelWidth * scale); image.Height = Math.Max(1, source.PixelHeight * scale);
    }
}
