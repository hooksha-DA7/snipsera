using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PixelRect = System.Drawing.Rectangle;

namespace Framelet;

// A small entry point for pending captures outside the three visible slots. Neither construction nor configuration shows a window.
internal sealed class PreviewOverflowWindow : Window
{
    private readonly CaptureApp app;
    private readonly Button pill;
    private readonly TextBlock count = Ui.Text("", 12, Ui.Ink, true);
    private readonly Popup flyout = new() { AllowsTransparency = true, StaysOpen = false, Placement = PlacementMode.Right, PopupAnimation = PopupAnimation.None };
    private readonly Grid content = new() { Margin = new Thickness(FloatingLayout.Margin) };
    private IReadOnlyList<PendingScreenshot> hidden = [];
    private PixelRect bounds;
    private double scale = 1, flyoutWidth = 280, flyoutHeight = 480;
    private Button? reopen;
    private bool closed;
    internal bool IsFlyoutOpen => flyout.IsOpen;
    internal bool IsCaptureExcluded { get; private set; }

    internal PreviewOverflowWindow(CaptureApp app)
    {
        this.app = app; Ui.Setup(this); Title = "Snipsera — more pending captures";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; ShowActivated = false; WindowStartupLocation = WindowStartupLocation.Manual;
        Width = FloatingLayout.MaxWidth; Height = FloatingLayout.OverflowHeight + 2 * FloatingLayout.Margin;
        count.TextWrapping = TextWrapping.NoWrap; count.TextTrimming = TextTrimming.CharacterEllipsis;
        pill = new Button { Content = count, Height = FloatingLayout.OverflowHeight, MinHeight = 0, MinWidth = 0, Width = 44,
            FontFamily = Ui.Font, FontSize = 12, FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetAutomationId(pill, "PendingCaptureOverflow"); AutomationProperties.SetLiveSetting(pill, AutomationLiveSetting.Polite);
        AutomationProperties.SetHelpText(pill, "Open pending screenshots. Captures remain in History.");
        pill.Click += (_, _) => OpenFlyout();
        pill.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down && Keyboard.Modifiers == ModifierKeys.Alt) { OpenFlyout(); e.Handled = true; }
            else if (e.Key == Key.Escape) { CloseFlyout(true); e.Handled = true; }
        };
        content.Children.Add(pill); Content = content; flyout.PlacementTarget = pill; flyout.HorizontalOffset = FloatingLayout.Gap;
        flyout.Opened += (_, _) =>
        {
            if (flyout.Child is Visual child && PresentationSource.FromVisual(child) is HwndSource source)
            {
                _ = SetWindowDisplayAffinity(source.Handle, 0x11);
                // Opening the list is an explicit interaction; give its keyboard route a foreground window.
                Native.SetForegroundWindow(source.Handle);
            }
            if (flyout.Child is FrameworkElement root) root.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        };
        flyout.Closed += (_, _) => ReleaseFlyoutContent();
        IsVisibleChanged += (_, _) => { if (!IsVisible) CloseFlyout(false); };
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(Hook); IsCaptureExcluded = RecordingPrivacy.Exclude(this); Place();
        };
        Restyle(); Appearance.Current.Changed += Restyle;
        Closed += (_, _) => { closed = true; CloseFlyout(false); Appearance.Current.Changed -= Restyle; };
    }

    internal void Configure(IReadOnlyList<PendingScreenshot> items, PixelRect placement, double dpi)
    {
        if (closed) return;
        bool changed = !hidden.Select(item => item.Entry.Id).SequenceEqual(items.Select(item => item.Entry.Id));
        hidden = items.ToArray(); bounds = placement; scale = dpi > 0 && double.IsFinite(dpi) ? dpi : 1;
        Width = Math.Max(1, bounds.Width / scale); Height = Math.Max(1, bounds.Height / scale);
        double availableWidth = Math.Max(1, Width - 2 * FloatingLayout.Margin), availableHeight = Math.Max(1, Height - 2 * FloatingLayout.Margin);
        count.Text = "+" + hidden.Count.ToString("N0");
        pill.Width = Math.Min(availableWidth, Math.Min(FloatingLayout.CardWidth, Math.Max(44, 24 + count.Text.Length * 8)));
        pill.Height = Math.Min(FloatingLayout.OverflowHeight, availableHeight);
        string name = hidden.Count == 1 ? "1 more pending screenshot" : $"{hidden.Count:N0} more pending screenshots";
        AutomationProperties.SetName(pill, name); pill.ToolTip = name;
        if (reopen is not null) reopen.IsEnabled = app.CanReopenLastDismissed;
        Place();
        if (hidden.Count == 0) { Present(false); return; }
        if (changed && flyout.IsOpen) flyout.Child = BuildFlyout(flyoutWidth, flyoutHeight);
        if (changed && UIElementAutomationPeer.FromElement(pill) is { } peer) peer.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    internal void Present(bool show)
    {
        if (closed) return;
        if (!show || hidden.Count == 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            CloseFlyout(false); if (IsVisible) Hide(); return;
        }
        if (!IsVisible) Show();
        Place();
    }

    private void Place()
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero && bounds.Width > 0 && bounds.Height > 0)
            Native.SetWindowPos(handle, new IntPtr(-1), bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0010);
    }

    private void OpenFlyout()
    {
        if (closed || !IsVisible || hidden.Count == 0) return;
        if (flyout.IsOpen) { CloseFlyout(true); return; }
        var work = Native.WorkArea(new WindowInteropHelper(this).Handle);
        flyoutWidth = Math.Max(1, Math.Min(280, work.Width / scale - 24));
        flyoutHeight = Math.Max(1, work.Height / scale - 24);
        flyout.Child = BuildFlyout(flyoutWidth, flyoutHeight);
        flyout.Placement = bounds.Right + (flyoutWidth + FloatingLayout.Gap) * scale <= work.Right ? PlacementMode.Right : PlacementMode.Top;
        flyout.IsOpen = true;
    }

    private void CloseFlyout(bool restoreFocus)
    {
        flyout.IsOpen = false;
        ReleaseFlyoutContent();
        if (restoreFocus && IsVisible) pill.Focus();
    }

    // Popup.Closed also covers StaysOpen=false outside clicks. Explicit hiding clears detached content even if never opened.
    internal void ReleaseFlyoutContent()
    {
        flyout.Child = null; reopen = null;
    }

    // The same production content can be measured/rendered detached, without opening Popup or creating an HWND.
    internal FrameworkElement BuildFlyout(double maxWidth, double maxHeight)
    {
        var rows = new StackPanel(); var actions = new List<Button>();
        foreach (var item in hidden)
        {
            var thumbnail = new Image { Width = 64, Height = 48, Stretch = Stretch.UniformToFill };
            RenderOptions.SetBitmapScalingMode(thumbnail, BitmapScalingMode.HighQuality);
            try { thumbnail.Source = app.PendingThumbnail(item); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException or System.Text.Json.JsonException)
            { /* Missing media keeps its labelled row available; opening it uses the normal history error path. */ }
            var tile = new Border { Width = 64, Height = 48, Child = thumbnail, Background = Ui.Canvas, ClipToBounds = true, CornerRadius = new CornerRadius(4), Margin = new Thickness(0, 0, 12, 0) };
            var title = Ui.Text("Screenshot", 12, Ui.Ink, true);
            var time = Ui.Text(item.Entry.TimeLabel, 11, Ui.Muted);
            var dimensions = Ui.Text($"{item.Entry.Width:N0} × {item.Entry.Height:N0}", 11, Ui.Muted);
            foreach (var label in new[] { title, time, dimensions }) { label.TextWrapping = TextWrapping.NoWrap; label.TextTrimming = TextTrimming.CharacterEllipsis; }
            var labels = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; labels.Children.Add(title); labels.Children.Add(time); labels.Children.Add(dimensions);
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new ColumnDefinition());
            row.Children.Add(tile); Grid.SetColumn(labels, 1); row.Children.Add(labels);
            var button = Ui.Button("Show screenshot in stack", "ShowPendingCapture-" + item.Entry.Id, () => app.ShowPendingScreenshot(item.Entry.Id));
            button.Content = row; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; button.Margin = new Thickness(0, 0, 0, 4); button.Padding = new Thickness(8);
            button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent;
            AutomationProperties.SetName(button, $"Screenshot, {item.Entry.TimeLabel}, {item.Entry.Width} by {item.Entry.Height}");
            AutomationProperties.SetHelpText(button, "Show in the screenshot stack without dismissing another capture.");
            rows.Children.Add(button); actions.Add(button);
        }
        rows.Children.Add(new Border { Height = 1, Background = Ui.Line, Margin = new Thickness(8, 4, 8, 8) });
        var clear = Ui.Button("Clear unpinned previews", "ClearPendingPreviews", () => { CloseFlyout(false); app.ClearUnpinnedPreviews(); });
        clear.Content = Ui.Text("Clear unpinned previews", 12); clear.Margin = new Thickness(4, 0, 4, 0); clear.Padding = new Thickness(8, 6, 8, 6);
        rows.Children.Add(clear); actions.Add(clear);
        var caption = Ui.Text("Captures stay in History.", 11, Ui.Muted); caption.Margin = new Thickness(12, 5, 12, 9); rows.Children.Add(caption);
        reopen = Ui.Button("Reopen last dismissed capture", "ReopenDismissedCapture", () => { CloseFlyout(false); app.ReopenLastDismissed(); });
        reopen.Content = Ui.Text("Reopen last dismissed capture", 12); reopen.Margin = new Thickness(4, 0, 4, 4); reopen.Padding = new Thickness(8, 6, 8, 6);
        reopen.IsEnabled = app.CanReopenLastDismissed; rows.Children.Add(reopen); actions.Add(reopen);
        var scroll = new ScrollViewer { Content = rows, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            CanContentScroll = false, MaxHeight = Math.Max(1, maxHeight - 16), Focusable = false };
        AutomationProperties.SetAutomationId(scroll, "PendingCaptureList");
        var body = new Border { Child = scroll, Width = Math.Max(1, Math.Min(280, maxWidth)), MaxHeight = Math.Max(1, maxHeight), Padding = new Thickness(7),
            CornerRadius = new CornerRadius(10), Background = Ui.Surface, BorderBrush = Ui.Line, BorderThickness = new Thickness(SystemParameters.HighContrast ? 2 : 1) };
        AutomationProperties.SetName(body, "More pending screenshots");
        KeyboardNavigation.SetTabNavigation(body, KeyboardNavigationMode.Cycle);
        body.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { CloseFlyout(true); e.Handled = true; return; }
            if (e.Key is not (Key.Up or Key.Down or Key.Home or Key.End)) return;
            var enabled = actions.Where(button => button.IsEnabled).ToArray(); if (enabled.Length == 0) return;
            int current = Array.FindIndex(enabled, button => button.IsKeyboardFocusWithin);
            int target = e.Key == Key.Home ? 0 : e.Key == Key.End ? enabled.Length - 1 : Math.Clamp(current + (e.Key == Key.Up ? -1 : 1), 0, enabled.Length - 1);
            enabled[target].Focus(); enabled[target].BringIntoView(); e.Handled = true;
        };
        return body;
    }

    private void Restyle()
    {
        FloatingButtons.Apply(pill); pill.BorderThickness = new Thickness(SystemParameters.HighContrast ? 2 : 1);
        pill.Effect = SystemParameters.HighContrast ? null : new DropShadowEffect { BlurRadius = 10, ShadowDepth = 1, Opacity = .18 };
        if (flyout.Child is Border body) body.BorderThickness = new Thickness(SystemParameters.HighContrast ? 2 : 1);
    }

    private static IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new IntPtr(3); } // MA_NOACTIVATE for the resting pill.
        return IntPtr.Zero;
    }
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
