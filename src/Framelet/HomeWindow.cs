using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Framelet;

public sealed class HomeWindow : Window
{
    private readonly CaptureApp app;
    private readonly StackPanel history = new();
    private readonly TextBlock status = Ui.Text("Ready to capture", 12, Ui.Muted);
    private readonly ShortcutRecorder[] shortcuts = new ShortcutRecorder[4];
    private readonly TextBlock[] badges = new TextBlock[4];
    public HomeWindow(CaptureApp app)
    {
        this.app = app; Ui.Setup(this);
        Title = "Snipsera"; Ui.UseSystemChrome(this); Width = 880; Height = 670; MinWidth = 760; MinHeight = 570;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var shell = new DockPanel();
        var dock = new DockPanel { Margin = new Thickness(20) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 18) };
        var brand = new StackPanel();
        var name = Ui.Text("Snipsera", 22, Ui.Ink, true);
        brand.Children.Add(name); brand.Children.Add(Ui.Text("Capture, annotate, keep close.", 12, Ui.Muted));
        var hide = Ui.Button("Keep in tray", "HideToTray", Hide); hide.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(hide, Dock.Right); header.Children.Add(hide); header.Children.Add(brand);
        var open = Ui.Button("Open image / project…", "OpenImageProject", app.OpenImageOrProject);
        open.VerticalAlignment = VerticalAlignment.Center; DockPanel.SetDock(open, Dock.Right);
        header.Children.Insert(1, open);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        status.Margin = new Thickness(0, 16, 0, 0); DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var capturePanel = new StackPanel();
        capturePanel.Children.Add(Ui.Text("Capture", 13, Ui.Ink, true));
        string[] labels = ["Select an area", "Choose a window", "Capture this display", "Repeat last area"];
        string[] descriptions = ["Drag a rectangle across your screen.", "Bring a visible window forward and capture it.", "Use the display under your pointer.", "Capture the same area again."];
        CaptureKind[] kinds = [CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.PreviousArea];
        for (int i = 0; i < kinds.Length; i++)
        {
            int index = i;
            var stack = new StackPanel(); stack.Children.Add(Ui.Text(labels[i], 13, Ui.Ink, true));
            
            badges[i] = Ui.Text(app.Settings.Bindings[i], 11, Ui.Blue); badges[i].Margin = new Thickness(0, 8, 0, 0);
            stack.Children.Add(badges[i]);
            var button = Ui.Button(labels[i], "Capture" + kinds[i], async () => await app.CaptureAsync(kinds[index]));
            var item = new DockPanel(); var icon = new UiIcon(new[] { "Area", "Window", "Screen", "History" }[i], 22) { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }; DockPanel.SetDock(icon, Dock.Left); item.Children.Add(icon); item.Children.Add(stack); button.Content = item; button.HorizontalContentAlignment = HorizontalAlignment.Left; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.Background = Brushes.Transparent; button.BorderBrush = Brushes.Transparent; button.MinHeight = 64; button.Margin = new Thickness(0, 4, 0, 0);
            capturePanel.Children.Add(button);
        }
        // Scrolling capture has no shortcut in this release; it starts here, from the tray or from a capture's right-click menu.
        var scrollText = new StackPanel(); scrollText.Children.Add(Ui.Text("Scrolling capture", 13, Ui.Ink, true));
        var scrollHint = Ui.Text("Select content, then scroll", 11, Ui.Muted); scrollHint.Margin = new Thickness(0, 8, 0, 0); scrollText.Children.Add(scrollHint);
        var scrollItem = new DockPanel(); var scrollIcon = new UiIcon("Scrolling", 22) { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(scrollIcon, Dock.Left); scrollItem.Children.Add(scrollIcon); scrollItem.Children.Add(scrollText);
        var scrolling = Ui.Button("Scrolling capture", "CaptureScrolling", async () => await app.CaptureAsync(CaptureKind.Scrolling));
        scrolling.Content = scrollItem; scrolling.HorizontalContentAlignment = HorizontalAlignment.Left; scrolling.HorizontalAlignment = HorizontalAlignment.Stretch; scrolling.Background = Brushes.Transparent; scrolling.BorderBrush = Brushes.Transparent; scrolling.MinHeight = 64; scrolling.Margin = new Thickness(0, 4, 0, 0);
        capturePanel.Children.Add(scrolling);
        // Screen recording opens its own setup window; no shortcut in this release.
        var recordText = new StackPanel(); recordText.Children.Add(Ui.Text("Record screen", 13, Ui.Ink, true));
        var recordHint = Ui.Text("MP4 or GIF, with optional audio", 11, Ui.Muted); recordHint.Margin = new Thickness(0, 8, 0, 0); recordText.Children.Add(recordHint);
        var recordItem = new DockPanel(); var recordIcon = new UiIcon("Record", 22) { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(recordIcon, Dock.Left); recordItem.Children.Add(recordIcon); recordItem.Children.Add(recordText);
        var recording = Ui.Button("Record screen", "CaptureRecording", app.OpenRecording);
        recording.Content = recordItem; recording.HorizontalContentAlignment = HorizontalAlignment.Left; recording.HorizontalAlignment = HorizontalAlignment.Stretch; recording.Background = Brushes.Transparent; recording.BorderBrush = Brushes.Transparent; recording.MinHeight = 64; recording.Margin = new Thickness(0, 4, 0, 0);
        capturePanel.Children.Add(recording);
        left.Children.Add(Ui.Card(capturePanel));
        // Temporary hide/show for floating previews and pins; the same toggle as the tray item and the restore tab.
        var overlayLabel = Ui.Text("", 13, Ui.Ink, true); var overlayHint = Ui.Text("", 11, Ui.Muted); overlayHint.Margin = new Thickness(0, 4, 0, 0);
        var overlayText = new StackPanel(); overlayText.Children.Add(overlayLabel); overlayText.Children.Add(overlayHint);
        var overlayItem = new DockPanel(); var overlayIcon = new UiIcon("ChevronUp", 22) { Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(overlayIcon, Dock.Left); overlayItem.Children.Add(overlayIcon); overlayItem.Children.Add(overlayText);
        var overlays = Ui.Button("Temporarily hide captures", "ToggleOverlays", app.ToggleOverlays);
        overlays.Content = overlayItem; overlays.HorizontalContentAlignment = HorizontalAlignment.Left; overlays.HorizontalAlignment = HorizontalAlignment.Stretch; overlays.Background = Brushes.Transparent; overlays.BorderBrush = Brushes.Transparent; overlays.MinHeight = 56; overlays.Margin = new Thickness(0, 10, 0, 0);
        void OverlayState()
        {
            bool hidden = app.OverlaysHidden; int count = app.HiddenOverlayCount;
            // Enabled only when the click will do something; never a silent no-op.
            overlays.IsEnabled = app.CanHideOverlays || app.CanShowOverlayShelf; overlayItem.Opacity = overlays.IsEnabled || SystemParameters.HighContrast ? 1 : .5;
            overlayLabel.Text = hidden ? "Show hidden captures" : "Temporarily hide captures";
            overlayHint.Text = hidden ? (count == 1 ? "1 capture is hidden" : $"{count:N0} captures are hidden")
                : overlays.IsEnabled ? "Or scroll down on a floating capture" : "No floating captures to hide";
            overlayIcon.RenderTransform = hidden ? null : new RotateTransform(180, 11, 11);
            System.Windows.Automation.AutomationProperties.SetName(overlays, overlayLabel.Text);
        }
        OverlayState(); app.OverlayVisibilityChanged += OverlayState; Closed += (_, _) => app.OverlayVisibilityChanged -= OverlayState;
        left.Children.Add(overlays);
        var settings = new StackPanel();
        var help = Ui.Text("Select a box and press the keys you want, then Save shortcuts. Esc cancels a change; Tab moves on.", 11, Ui.Muted);
        var rule = Ui.Text(ShortcutRecorder.Rule, 11, Ui.Muted); rule.Margin = new Thickness(0, 4, 0, 8);
        settings.Children.Add(help); settings.Children.Add(rule);
        for (int i = 0; i < shortcuts.Length; i++)
        {
            var label = Ui.Text(labels[i], 11); settings.Children.Add(label);
            shortcuts[i] = new ShortcutRecorder(labels[i], app.Settings.Bindings[i], app.SuspendShortcuts, ex => SetStatus(ex.Message, true)) { FontSize = 12, Padding = new Thickness(7), Margin = new Thickness(0, 3, 0, 8) };
            System.Windows.Automation.AutomationProperties.SetAutomationId(shortcuts[i], "Shortcut" + i);
            System.Windows.Automation.AutomationProperties.SetLabeledBy(shortcuts[i], label);
            shortcuts[i].Changed += recorder => SetStatus(recorder.IsPending ? $"{labels[Array.IndexOf(shortcuts, recorder)]}: {recorder.Shortcut} — select Save shortcuts to apply" : "Shortcut unchanged");
            shortcuts[i].Rejected += message => SetStatus(message, true);
            settings.Children.Add(shortcuts[i]);
        }
        settings.Children.Add(Ui.Button("Save shortcuts", "SaveShortcuts", () =>
        {
            try
            {
                // Pressing Save moves focus off the recorder first, so the current shortcuts are restored before they are replaced.
                app.UpdateSettings(new AppSettings(shortcuts[0].Shortcut, shortcuts[1].Shortcut, shortcuts[2].Shortcut, shortcuts[3].Shortcut));
                for (int i = 0; i < badges.Length; i++) { badges[i].Text = app.Settings.Bindings[i]; shortcuts[i].MarkSaved(app.Settings.Bindings[i]); }
                SetStatus("Shortcuts saved");
            }
            catch (Exception ex) { SetStatus(ex.Message, true); }
        }));
        // A hidden or closed Home window never keeps the capture shortcuts suspended.
        void Release() { foreach (var recorder in shortcuts) recorder.Release(); }
        IsVisibleChanged += (_, _) => { if (!IsVisible) Release(); }; Deactivated += (_, _) => Release(); Closed += (_, _) => Release();
        // WPF restores keyboard focus after Activated and may not raise a new focus event, so check once input has settled.
        Activated += (_, _) => Dispatcher.InvokeAsync(() => { if (IsActive) foreach (var recorder in shortcuts) recorder.Resume(); }, DispatcherPriority.Input);
        left.Children.Add(new Expander { Header = "Keyboard shortcuts", Content = settings, Margin = new Thickness(0, 16, 0, 0), Padding = new Thickness(8) });
        var leftScroll = new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        columns.Children.Add(leftScroll);
        var recent = new DockPanel();
        var recentHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        recentHeader.Children.Add(Ui.Text("Recent captures", 16, Ui.Ink, true));
        recentHeader.Children.Add(Ui.Text("The latest 100 captures, stored on this PC.", 12, Ui.Muted));
        DockPanel.SetDock(recentHeader, Dock.Top); recent.Children.Add(recentHeader);
        recent.Children.Add(new ScrollViewer { Content = history, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(recent, 2); columns.Children.Add(recent); dock.Children.Add(columns); shell.Children.Add(dock); Content = shell;
        Closing += (_, e) => { if (!app.Quitting) { e.Cancel = true; Hide(); } };
        RefreshHistory();
    }

    public void SetStatus(string message, bool error = false) { status.Text = message; status.Foreground = error ? Ui.Brush("#A33E16") : Ui.Muted; }
    public void RefreshHistory()
    {
        history.Children.Clear();
        if (app.History.Entries.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(14, 60, 14, 60) };
            empty.Children.Add(Ui.Text("No captures yet", 16, Ui.Ink, true));
            empty.Children.Add(Ui.Text("Take a capture with a button or shortcut. It will appear here with copy, save and pin actions.", 14, Ui.Muted));
            history.Children.Add(Ui.Card(empty)); return;
        }
        foreach (var entry in app.History.Entries)
        {
            var row = new DockPanel();
            var image = new Image { Width = 142, Height = 84, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 16, 0) };
            try { image.Source = app.History.LoadImage(entry, 280); }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FileFormatException or System.Text.Json.JsonException) { image.ToolTip = "Image or saved edits could not be opened. The local files are preserved."; }
            DockPanel.SetDock(image, Dock.Left); row.Children.Add(image);
            var text = new StackPanel(); text.Children.Add(Ui.Text(entry.Label, 12, Ui.Ink, true)); text.Children.Add(Ui.Text(entry.TimeLabel, 11, Ui.Muted));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            actions.Children.Add(Ui.IconButton("Open", "Open preview", "Open" + entry.Id, () => app.OpenEntry(entry)));
            actions.Children.Add(Ui.IconButton("Copy", "Copy", "Copy" + entry.Id, async () =>
            { try { await CaptureService.Copy(app.History.LoadImage(entry)); SetStatus("Copied to clipboard"); } catch (Exception ex) { SetStatus(ex.Message, true); } }));
            actions.Children.Add(Ui.IconButton("Delete", "Remove", "Remove" + entry.Id, () =>
            {
                if (ConfirmWindow.Ask(this, "Remove this capture?", "This removes the image from local history. Copies you saved elsewhere are kept.", "Remove") != Confirmation.Primary) return;
                try { app.History.Remove(entry); RefreshHistory(); } catch (Exception ex) { SetStatus(ex.Message, true); }
            }));
            text.Children.Add(actions); row.Children.Add(text);
            var card = Ui.Card(row, new Thickness(12)); card.Margin = new Thickness(0, 0, 0, 10); history.Children.Add(card);
        }
    }
}
