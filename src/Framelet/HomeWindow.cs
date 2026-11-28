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
    private readonly TextBlock[] badges = new TextBlock[5];
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
        // Settings sits just left of the existing actions, so Open and Keep in tray stay where they were.
        var preferences = Ui.IconButton("Settings", "Settings", "OpenSettings", app.OpenPreferences);
        preferences.VerticalAlignment = VerticalAlignment.Center; preferences.Margin = new Thickness(0, 0, 6, 0); DockPanel.SetDock(preferences, Dock.Right);
        header.Children.Insert(2, preferences);
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);
        // Stays until the update is installed, even if the Windows notification was missed or turned off.
        var notice = new UpdateNotice(app.Preferences, app.OpenPreferences); DockPanel.SetDock(notice, Dock.Top); dock.Children.Add(notice);
        app.Preferences.Changed += notice.Refresh; Closed += (_, _) => app.Preferences.Changed -= notice.Refresh;
        // First run only: the essentials in three lines, then it stays dismissed.
        if (app.Welcome.ShouldShow) { var welcome = WelcomeCard(); DockPanel.SetDock(welcome, Dock.Top); dock.Children.Add(welcome); }
        status.Margin = new Thickness(0, 16, 0, 0); DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(260) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel();
        var capturePanel = new StackPanel();
        var captureTitle = Ui.Text("Capture", 13, Ui.Ink, true); captureTitle.Margin = new Thickness(0, 0, 0, 10); left.Children.Add(captureTitle);
        string[] labels = ["Select an area", "Choose a window", "Capture this display", "Repeat last area", "All-in-one capture"];
        // Opens the capture bar with every screenshot mode; its shortcut badge is the fifth binding.
        var barText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        barText.Children.Add(Ui.Text(labels[4], 13, Ui.Ink, true)); barText.Children.Add(Ui.Text("Every capture mode in one bar", 11, Ui.Muted));
        badges[4] = Ui.Text(app.Settings.Bindings[4], 11, Ui.Blue); badges[4].TextWrapping = TextWrapping.NoWrap; badges[4].Margin = new Thickness(0, 4, 0, 0); barText.Children.Add(badges[4]);
        var barItem = new DockPanel(); var barIcon = CaptureBarVisuals.LauncherGlyph(); barIcon.Margin = new Thickness(0, 0, 14, 0); barIcon.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(barIcon, Dock.Left); barItem.Children.Add(barIcon); barItem.Children.Add(barText);
        var launcher = Ui.Button(labels[4], "ShowCaptureBar", () => app.ShowCaptureBar());
        launcher.Content = barItem; launcher.HorizontalContentAlignment = HorizontalAlignment.Stretch; launcher.HorizontalAlignment = HorizontalAlignment.Stretch; launcher.MinHeight = 56; launcher.Margin = new Thickness(0, 10, 0, 6);
        launcher.ToolTip = "Area, window, display, scrolling and last area, in one compact bar";
        capturePanel.Children.Add(launcher);
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
        // Scrolling capture has no direct shortcut; it starts here, from the capture bar, the tray or a capture's right-click menu.
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
        left.Children.Add(LibraryPanel(capturePanel, new Thickness(16), "CapturePanel"));
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
        // Shortcuts are edited in Settings; the badges above always show the saved bindings.
        var shortcutLink = Ui.Button("Change keyboard shortcuts…", "OpenShortcutSettings", app.OpenKeyboardShortcuts);
        shortcutLink.HorizontalAlignment = HorizontalAlignment.Left; shortcutLink.Margin = new Thickness(0, 12, 0, 0);
        System.Windows.Automation.AutomationProperties.SetHelpText(shortcutLink, "Opens Settings, where you can change the capture shortcuts.");
        left.Children.Add(shortcutLink);
        void Badges()
        {
            if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Badges)); return; }
            for (int i = 0; i < badges.Length; i++) badges[i].Text = app.Settings.Bindings[i];
            if (welcomeShortcut is not null) welcomeShortcut.Text = WelcomeShortcutText();
        }
        app.SettingsChanged += Badges; Closed += (_, _) => app.SettingsChanged -= Badges;
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

    private TextBlock? welcomeShortcut;
    private string WelcomeShortcutText() => $"Press {app.Settings.Bindings[0]} to select an area, or use the buttons below.";

    private Border WelcomeCard()
    {
        var body = new StackPanel();
        var title = Ui.Text("Welcome to Snipsera", 15, Ui.Ink, true);
        System.Windows.Automation.AutomationProperties.SetHeadingLevel(title, System.Windows.Automation.AutomationHeadingLevel.Level2);
        body.Children.Add(title);
        welcomeShortcut = Ui.Text(WelcomeShortcutText(), 13); welcomeShortcut.Margin = new Thickness(0, 6, 0, 0);
        System.Windows.Automation.AutomationProperties.SetAutomationId(welcomeShortcut, "WelcomeShortcut");
        body.Children.Add(welcomeShortcut);
        var preview = Ui.Text("Each capture appears at the bottom left. Hover over it to copy, save, pin or annotate.", 13, Ui.Muted); preview.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(preview);
        var tip = Ui.Text("Tip: set Print Screen to open the all-in-one capture bar.", 13, Ui.Muted); tip.Margin = new Thickness(0, 2, 0, 0);
        body.Children.Add(tip);
        Border card = null!;
        void Dismiss() { app.Welcome.Dismiss(); card.Visibility = Visibility.Collapsed; }
        var setUp = Ui.Button("Set up Print Screen…", "WelcomeSetUpPrintScreen", () => { Dismiss(); app.OpenKeyboardShortcuts(); }, true);
        var gotIt = Ui.Button("Got it", "WelcomeDismiss", Dismiss);
        var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        setUp.Margin = new Thickness(0, 0, 8, 0); actions.Children.Add(setUp); actions.Children.Add(gotIt);
        body.Children.Add(actions);
        card = Ui.Card(body, new Thickness(16));
        card.BorderBrush = Ui.Blue; card.Margin = new Thickness(0, 0, 0, 16);
        System.Windows.Automation.AutomationProperties.SetAutomationId(card, "WelcomeCard");
        System.Windows.Automation.AutomationProperties.SetName(card, "Welcome to Snipsera");
        return card;
    }

    public void SetStatus(string message, bool error = false) { status.Text = message; status.Foreground = error ? Ui.Brush("#A33E16") : Ui.Muted; }
    public void RefreshHistory()
    {
        history.Children.Clear();
        if (app.History.Entries.Count == 0)
        {
            var empty = new StackPanel { Margin = new Thickness(14, 60, 14, 60) };
            empty.Children.Add(Ui.Text("No captures yet", 16, Ui.Ink, true));
            empty.Children.Add(Ui.Text("Take a capture with a button or shortcut. Open it here to edit, copy or keep it close.", 14, Ui.Muted));
            history.Children.Add(LibraryPanel(empty, new Thickness(16), "EmptyHistory")); return;
        }
        foreach (var entry in app.History.Entries)
        {
            var row = new DockPanel();
            var image = new Image { Width = 128, Height = 80, Stretch = Stretch.Uniform };
            try
            {
                var source = app.History.LoadImage(entry, 280); image.Source = source;
                double scale = Math.Min(128d / source.PixelWidth, 80d / source.PixelHeight);
                image.Width = Math.Max(1, source.PixelWidth * scale); image.Height = Math.Max(1, source.PixelHeight * scale);
                image.Clip = new RectangleGeometry(new Rect(0, 0, image.Width, image.Height), 6, 6);
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or ArgumentException or FileFormatException or System.Text.Json.JsonException) { image.ToolTip = "Image or saved edits could not be opened. The local files are preserved."; }
            var imageFrame = new Border { Child = image, BorderBrush = Ui.Line, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var thumbnail = new Grid { Width = 130, Height = 82, Margin = new Thickness(0, 0, 16, 0) }; thumbnail.Children.Add(imageFrame);
            DockPanel.SetDock(thumbnail, Dock.Left); row.Children.Add(thumbnail);
            var text = new StackPanel(); text.Children.Add(Ui.Text(entry.Label, 12, Ui.Ink, true)); text.Children.Add(Ui.Text(entry.TimeLabel, 11, Ui.Muted));
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var edit = Ui.Button("Edit", "Edit" + entry.Id, () => app.EditEntry(entry));
            edit.MinHeight = edit.Height = 28;
            var editContent = new StackPanel { Orientation = Orientation.Horizontal };
            editContent.Children.Add(new UiIcon("Pen", 15) { Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            editContent.Children.Add(Ui.Text("Edit", 12, Ui.Ink, true)); edit.Content = editContent;
            edit.ToolTip = "Open in editor, including saved annotations";
            System.Windows.Automation.AutomationProperties.SetName(edit, "Edit capture");
            System.Windows.Automation.AutomationProperties.SetHelpText(edit, "Open this capture directly in the editor with its saved edits.");
            actions.Children.Add(edit);
            actions.Children.Add(Ui.IconButton("Open", "Open preview", "Open" + entry.Id, () => app.OpenEntry(entry)));
            actions.Children.Add(Ui.IconButton("Copy", "Copy", "Copy" + entry.Id, async () =>
            { try { await CaptureService.Copy(app.History.LoadImage(entry)); SetStatus("Copied to clipboard"); } catch (Exception ex) { SetStatus(ex.Message, true); } }));
            actions.Children.Add(Ui.IconButton("Delete", "Remove", "Remove" + entry.Id, () =>
            {
                if (ConfirmWindow.Ask(this, "Remove this capture?", "This removes the image from local history. Copies you saved elsewhere are kept.", "Remove") != Confirmation.Primary) return;
                try { app.History.Remove(entry); RefreshHistory(); } catch (Exception ex) { SetStatus(ex.Message, true); }
            }));
            for (int i = 1; i < actions.Children.Count; i++)
            { var button = (Button)actions.Children[i]; button.MinWidth = button.Width = 28; button.MinHeight = button.Height = 28; button.Padding = new Thickness(5); }
            ((Button)actions.Children[^1]).Margin = new Thickness(9, 1, 1, 1);
            text.Children.Add(actions); row.Children.Add(text);
            var card = LibraryPanel(row, new Thickness(12), "History" + entry.Id); card.Margin = new Thickness(0, 0, 0, 8); history.Children.Add(card);
        }
    }

    // The window owns the material; translucent cards follow it rather than painting opaque gray blocks over Mica.
    private static Border LibraryPanel(UIElement child, Thickness padding, string id)
    {
        var panel = new Border { Background = Ui.CardMaterial, BorderBrush = Ui.CardStroke, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = padding, Child = child };
        if (id.StartsWith("History", StringComparison.Ordinal))
        {
            var hover = new Border { Background = Ui.Ink, Opacity = 0, IsHitTestVisible = false, CornerRadius = new CornerRadius(8),
                Margin = new Thickness(-padding.Left, -padding.Top, -padding.Right, -padding.Bottom) };
            var content = new Grid(); panel.Child = content; content.Children.Add(child); content.Children.Add(hover);
            void Highlight(bool on)
            {
                double opacity = on && !SystemParameters.HighContrast ? .035 : 0;
                if (Appearance.Current.Policy.Animate)
                    hover.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(opacity, TimeSpan.FromMilliseconds(80)));
                else { hover.BeginAnimation(OpacityProperty, null); hover.Opacity = opacity; }
            }
            panel.MouseEnter += (_, _) => Highlight(true); panel.MouseLeave += (_, _) => Highlight(false);
        }
        System.Windows.Automation.AutomationProperties.SetAutomationId(panel, id); return panel;
    }
}
