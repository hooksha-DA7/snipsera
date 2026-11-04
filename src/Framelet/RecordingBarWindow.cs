using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Framelet;

// Compact floating bar shown while recording: status dot, elapsed time, Pause/Resume, Stop and a two-step Discard, plus GIF
// conversion progress. Its owner controls its lifetime. It asks to be excluded from capture, never takes focus when shown, sits
// just outside the recorded target when there is room, and refuses to close while a recording is active so it can't be orphaned.
public sealed class RecordingBarWindow : Window
{
    private const double Unit = 32;
    private static readonly Brush Live = Frozen(Color.FromRgb(0xE8, 0x11, 0x23));
    private readonly RecordingController controller;
    private readonly Border bar;
    private readonly Ellipse dot = new() { Width = 10, Height = 10, Margin = new Thickness(4, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock elapsed = new() { FontSize = 13, FontWeight = FontWeights.SemiBold, MinWidth = 44, VerticalAlignment = VerticalAlignment.Center };
    // Sits in the bar's only star column, so a long status trims instead of pushing the buttons past the window's MaxWidth.
    private readonly TextBlock state = new() { FontSize = 12, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, MaxWidth = 260, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly ProgressBar progress = new() { Width = 72, Height = 4, MinHeight = 0, Minimum = 0, Maximum = 1, Margin = new Thickness(8, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed, Focusable = false };
    private readonly Button pause, stop, discard;
    private readonly DispatcherTimer disarm = new() { Interval = TimeSpan.FromSeconds(3) };
    private RecordingState shown = (RecordingState)(-1);
    // busy: a Stop (which includes GIF conversion) or other bar action is pending. canceling: the confirmed Discard is pending.
    private bool confirming, busy, canceling, moved, paused;
    private string? error;

    // Whether Windows accepted the request to keep this bar out of screen captures (WDA_EXCLUDEFROMCAPTURE).
    internal bool IsCaptureExcluded { get; private set; }

    public RecordingBarWindow(RecordingController controller)
    {
        this.controller = controller; Ui.Setup(this);
        Title = "Snipsera — recording";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; SizeToContent = SizeToContent.WidthAndHeight; MaxWidth = 520;
        WindowStartupLocation = WindowStartupLocation.Manual; FontFamily = Ui.Font;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
        elapsed.Foreground = Ui.Ink; state.Foreground = Ui.Muted;
        Typography.SetNumeralAlignment(elapsed, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAutomationId(elapsed, "RecordingElapsed"); AutomationProperties.SetName(elapsed, "Elapsed time");
        AutomationProperties.SetAutomationId(state, "RecordingState"); AutomationProperties.SetLiveSetting(state, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(progress, "RecordingConversionProgress"); AutomationProperties.SetName(progress, "GIF conversion progress");

        pause = Tool("Pause", "Pause recording", "RecordingPause", TogglePause);
        stop = Tool("Stop", "Stop and save", "RecordingStop", async () => await Execute(controller.StopAsync), true);
        discard = Tool("Close", "Discard recording", "RecordingCancel", Discard);
        var divider = new Border { Width = 1, Height = 20, Margin = new Thickness(10, 0, 6, 0), Background = Ui.Line, VerticalAlignment = VerticalAlignment.Center };
        // Discard sits apart from Stop/Pause and needs a second click, so a hurried Stop never throws a recording away.
        // A grid rather than a stack panel: every column is Auto except the status, which takes (and trims to) what is left
        // within MaxWidth. Child order is unchanged, so the tab order stays Pause, Stop, Discard.
        var row = new Grid();
        UIElement[] cells = [dot, elapsed, state, progress, divider, pause, stop, new Border { Width = 10 }, discard];
        for (int i = 0; i < cells.Length; i++)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = cells[i] == state ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            Grid.SetColumn(cells[i], i); row.Children.Add(cells[i]);
        }
        bar = new Border { Child = row, Background = Ui.Floating, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(FloatingVisuals.Radius), Padding = new Thickness(10, 6, 6, 6) };
        AutomationProperties.SetName(bar, "Recording controls"); KeyboardNavigation.SetTabNavigation(bar, KeyboardNavigationMode.Cycle);
        Content = new Grid { Margin = new Thickness(12), Children = { bar } };
        Restyle();

        // Drag anywhere that isn't a control.
        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (InsideButton(e.OriginalSource as DependencyObject)) return;
            DragMove(); moved = true;
        };
        disarm.Tick += (_, _) => Disarm();
        SourceInitialized += (_, _) => IsCaptureExcluded = RecordingPrivacy.Exclude(this);
        Loaded += (_, _) => Place();
        SizeChanged += (_, _) => { if (!moved && IsLoaded) Place(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(() => { if (!moved) Place(); });
        controller.Changed += Refresh;
        Appearance.Current.Changed += Restyle;
        // Closing during an active recording would orphan it; the parent stops it first, then closes.
        Closing += (_, e) => { if (controller.IsActive) e.Cancel = true; };
        Closed += (_, _) => { controller.Changed -= Refresh; Appearance.Current.Changed -= Restyle; disarm.Stop(); dot.BeginAnimation(OpacityProperty, null); };
        Refresh();
    }

    private static Button Tool(string symbol, string label, string id, Action action, bool accent = false)
    {
        var button = Ui.IconButton(symbol, label, id, action, accent);
        button.Content = new UiIcon(symbol, 15, 2); button.Margin = new Thickness(4, 0, 0, 0); button.Padding = new Thickness(0);
        button.MinWidth = button.MinHeight = 0; button.Width = button.Height = Unit;
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
        button.ClearValue(BackgroundProperty); button.ClearValue(BorderBrushProperty); button.ClearValue(ForegroundProperty);
        button.Tag = accent; FloatingButtons.Apply(button, accent);
        return button;
    }

    private void TogglePause()
    {
        try { error = null; controller.TogglePause(); }
        catch (Exception ex) { error = ex.Message; }
        Refresh();
    }

    private async System.Threading.Tasks.Task Execute(Func<System.Threading.Tasks.Task> action)
    {
        if (busy) return;
        busy = true; error = null; Refresh();
        try { await action(); }
        catch (Exception ex) { error = ex.Message; }
        finally { busy = false; Refresh(); }
    }

    private async void Discard()
    {
        if (!confirming)
        {
            confirming = true; discard.Content = "Discard"; discard.Width = double.NaN; discard.Padding = new Thickness(12, 0, 12, 0);
            discard.FontSize = 12; discard.FontWeight = FontWeights.SemiBold;
            discard.ToolTip = "Click again to discard this recording"; AutomationProperties.SetName(discard, "Confirm discard recording");
            disarm.Stop(); disarm.Start(); return;
        }
        Disarm();
        // Runs alongside a pending Stop: during GIF conversion Stop is still awaiting finalization, and CancelAsync cancels the
        // conversion and awaits that same finalization rather than starting another. canceling blocks a second request.
        if (canceling) return;
        canceling = true; error = null; Refresh();
        try { await controller.CancelAsync(); }
        catch (Exception ex) { error = ex.Message; }
        finally { canceling = false; Refresh(); }
    }

    private void Disarm()
    {
        disarm.Stop(); if (!confirming) return;
        confirming = false; discard.Content = new UiIcon("Close", 15, 2); discard.Width = Unit; discard.Padding = new Thickness(0);
        discard.ToolTip = "Discard recording"; AutomationProperties.SetName(discard, "Discard recording");
    }

    private void Refresh()
    {
        var current = controller.State;
        elapsed.Text = Clock(controller.Elapsed);
        bool live = current is RecordingState.Recording or RecordingState.Paused;
        pause.IsEnabled = stop.IsEnabled = live && !busy && !canceling;
        // Discard also cancels a running GIF conversion, even while the Stop that started it is still pending; it still needs
        // the second click.
        discard.IsEnabled = !canceling && (current is RecordingState.Starting or RecordingState.Recording or RecordingState.Paused && !busy || current == RecordingState.Converting);
        if (!discard.IsEnabled) Disarm();

        bool isPaused = current == RecordingState.Paused;
        if (isPaused != paused)
        {
            paused = isPaused; string label = paused ? "Resume recording" : "Pause recording";
            pause.Content = new UiIcon(paused ? "Play" : "Pause", 15, 2); pause.ToolTip = label; AutomationProperties.SetName(pause, label);
        }

        double? converted = current == RecordingState.Converting && controller.ConversionProgress is double value && double.IsFinite(value) ? Math.Clamp(value, 0, 1) : null;
        progress.Visibility = converted is null || error is not null ? Visibility.Collapsed : Visibility.Visible;
        if (converted is double fraction) progress.Value = fraction;
        // A failed finalization's controller status carries the MP4 recovery path, so it wins over the bare exception message.
        string text = current == RecordingState.Failed && !string.IsNullOrWhiteSpace(controller.Status) ? controller.Status : error ?? current switch
        {
            RecordingState.Starting => "Starting…",
            RecordingState.Paused => "Paused",
            RecordingState.Stopping => "Saving…",
            RecordingState.Converting => converted is double done ? $"Creating GIF… {done:P0}" : "Creating GIF…",
            RecordingState.Completed => "Saved",
            RecordingState.Failed => string.IsNullOrWhiteSpace(controller.Status) ? "Recording failed" : controller.Status,
            _ => ""
        };
        state.Text = text; state.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        state.ToolTip = text.Length == 0 ? null : text;
        state.Foreground = error is not null || current == RecordingState.Failed ? (SystemParameters.HighContrast ? Ui.Ink : Live) : Ui.Muted;

        bool hc = SystemParameters.HighContrast;
        dot.Fill = current == RecordingState.Recording ? (hc ? SystemColors.HighlightBrush : Live) : Ui.Muted;
        if (current != shown)
        {
            // A slow pulse while recording; static under reduced motion or high contrast.
            if (current == RecordingState.Recording && Appearance.Current.Policy.Animate)
                dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, .35, TimeSpan.FromMilliseconds(900)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
            else { dot.BeginAnimation(OpacityProperty, null); dot.Opacity = 1; }
            shown = current;
            if (IsLoaded && text.Length > 0) UIElementAutomationPeer.CreatePeerForElement(state)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private void Restyle()
    {
        bool hc = SystemParameters.HighContrast;
        foreach (var button in new[] { pause, stop, discard }) FloatingButtons.Apply(button, button.Tag is true);
        bar.Effect = hc ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Direction = 270, Opacity = .22 };
        shown = (RecordingState)(-1); Refresh();
    }

    // Just outside the recording target when there is room (below, above, then beside); otherwise top centre of the work area
    // holding the target (else the pointer's), just below the top edge.
    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        if (controller.Target is RecordingTarget anchor && RecordingSetupVisuals.PlaceOutside(this, anchor.Bounds, 12)) return;
        System.Drawing.Rectangle work = Native.MonitorAtCursor(true);
        if (controller.Target is RecordingTarget target && !target.Bounds.IsEmpty)
        {
            var center = new System.Drawing.Point(target.Bounds.Left + target.Bounds.Width / 2, target.Bounds.Top + target.Bounds.Height / 2);
            var areas = ScrollCaptureLayout.WorkAreas();
            if (areas.FirstOrDefault(a => a.Contains(center)) is { IsEmpty: false } area) work = area;
        }
        if (!Native.GetWindowRect(handle, out var rect)) return;
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        int x = work.Left + Math.Max(0, (work.Width - width) / 2), y = work.Top + Math.Max(0, Math.Min(work.Height - height, (int)Math.Round(4 * ScrollCaptureLayout.ScaleFor(work))));
        // HWND_TOPMOST, SWP_NOSIZE | SWP_NOACTIVATE.
        Native.SetWindowPos(handle, new IntPtr(-1), x, y, 0, 0, 0x0011);
    }

    private static bool InsideButton(DependencyObject? node)
    {
        for (; node is not null; node = node is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase) return true;
        return false;
    }

    internal static string Clock(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{time.Minutes:00}:{time.Seconds:00}";
    }
    private static Brush Frozen(Color color) { var brush = new SolidColorBrush(color); brush.Freeze(); return brush; }
}
