using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;
using static Framelet.RecordingSetupVisuals;

namespace Framelet;

// Compact floating recording setup in two panels. Top: source, width × height in physical pixels, capture toggles, and an
// inline settings section (quality, frame rate, resolution, notifications, last recording) kept in this one capture-excluded
// window. Bottom: Record GIF and Record Video. A Record action selects first when there is no fresh selection and starts only
// if that selection was accepted; after Esc nothing older is reused. While recording, this window is hidden and locked and the
// floating bar owns Pause/Stop/Discard.
public sealed class RecordingWindow : Window
{
    private static readonly int[] Rates = [5, 10, 15, 24, 30, 60];
    private readonly RecordingController controller;
    private readonly RecordingTargetKind[] kinds = [RecordingTargetKind.Area, RecordingTargetKind.Window, RecordingTargetKind.Display, RecordingTargetKind.Custom];
    private readonly RadioButton[] sources;
    private readonly TextBox customWidth, customHeight;
    private readonly ToggleButton cursor, clicks, microphone, computerAudio, settings;
    private readonly ComboBox quality, rate, resolution;
    private readonly CheckBox? dnd;
    private readonly TextBlock status, result;
    private readonly Button close, recordGif, recordVideo;
    private readonly Border top, actions;
    private readonly FrameworkElement more, resultRow;
    private readonly UIElement[] locks;
    private bool syncing, busy, moved, invalid, canceled, savedMicrophone, savedComputerAudio;
    private int savedRate;
    private string shownWidth = "", shownHeight = "", announced = "";
    private string? error;
    // A target that predates this window or survived a canceled selection: shown nowhere and never recorded without reselecting.
    private RecordingTarget? stale, anchored;
    private System.Drawing.Rectangle home;

    public RecordingWindow(CaptureApp app)
    {
        controller = app.Recording; Ui.Setup(this);
        Title = "Snipsera — record screen";
        WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = System.Windows.Media.Brushes.Transparent; ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual; FontSize = 13;
        var o = controller.Options; stale = controller.Target; savedRate = o.FramesPerSecond;
        if (o.Format == RecordingFormat.Mp4) { savedMicrophone = o.Microphone; savedComputerAudio = o.ComputerAudio; }

        // Row 1: source segment | width × height.
        string[] glyphs = ["Area", "Window", "Display", "Custom"];
        string[] names = ["Area", "Window", "Display", "Custom size"];
        string[] helps = ["Select an area to record", "Choose a window to record", "Choose a display to record", "Position a frame of the size below"];
        var sourceRow = new StackPanel { Orientation = Orientation.Horizontal };
        KeyboardNavigation.SetTabNavigation(sourceRow, KeyboardNavigationMode.Once); KeyboardNavigation.SetDirectionalNavigation(sourceRow, KeyboardNavigationMode.Cycle);
        AutomationProperties.SetName(sourceRow, "Source");
        sources = new RadioButton[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            var kind = kinds[i];
            var radio = sources[i] = new RadioButton { Content = new RecordingGlyph(glyphs[i]), GroupName = "RecordingSource", ToolTip = helps[i] };
            Square(radio); AutomationProperties.SetAutomationId(radio, "RecordingSource" + kind); AutomationProperties.SetName(radio, names[i]); AutomationProperties.SetHelpText(radio, helps[i]);
            radio.Checked += (_, _) => { if (syncing) return; if (kind != RecordingTargetKind.Custom) Revert(); Apply(x => x with { TargetKind = kind }); };
            // Clicking (or Space on) a source is the explicit "select it now"; arrow keys only move focus.
            radio.Click += async (_, _) => { if (kind == RecordingTargetKind.Custom && !CommitDimensions()) return; await Act(async () => { await SelectTarget(); }); };
            sourceRow.Children.Add(radio);
        }
        customWidth = Number("RecordingCustomWidth", "Width in pixels"); customHeight = Number("RecordingCustomHeight", "Height in pixels");
        var times = Ui.Text("×", 13, Ui.Muted); times.Margin = new Thickness(6, 0, 6, 0); times.VerticalAlignment = VerticalAlignment.Center;
        var size = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        size.Children.Add(Field(customWidth, "W")); size.Children.Add(times); size.Children.Add(Field(customHeight, "H"));
        var row1 = new DockPanel { LastChildFill = false };
        row1.Children.Add(sourceRow); row1.Children.Add(Rule(true)); DockPanel.SetDock(size, Dock.Right); row1.Children.Add(size);

        // Row 2: capture toggles | settings, close.
        const string audioHelp = "Recorded with video. GIF recordings are silent.";
        cursor = Capture("Cursor", "Show cursor", "RecordingCursor", null, on => Apply(x => x with { ShowCursor = on }));
        clicks = Capture("Click", "Highlight clicks", "RecordingClicks", "Shows a ring where you click", on => Apply(x => x with { HighlightClicks = on }));
        microphone = Capture("Microphone", "Microphone", "RecordingMicrophone", audioHelp, on => Audio(true, on));
        computerAudio = Capture("Speaker", "Computer audio", "RecordingComputerAudio", audioHelp, on => Audio(false, on));
        settings = new ToggleButton { Content = new RecordingGlyph("Settings"), ToolTip = "Recording settings" };
        Square(settings); AutomationProperties.SetAutomationId(settings, "RecordingSettings"); AutomationProperties.SetName(settings, "Recording settings");
        AutomationProperties.SetHelpText(settings, "Quality, frame rate, resolution, notifications and the last recording");
        close = new Button { Content = new RecordingGlyph("Close", 14), ToolTip = "Close" };
        Square(close); AutomationProperties.SetAutomationId(close, "RecordingClose"); AutomationProperties.SetName(close, "Close recording setup"); close.Click += (_, _) => Close();
        var row2 = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 0) };
        foreach (var toggle in new[] { cursor, clicks, microphone, computerAudio }) row2.Children.Add(toggle);
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(settings, Dock.Right); close.Margin = new Thickness(0);
        row2.Children.Add(close); row2.Children.Add(settings);

        status = Ui.Text("", 12, Ui.Muted); status.Margin = new Thickness(4, 6, 4, 2);
        AutomationProperties.SetAutomationId(status, "RecordingStatus"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);

        // Settings section, inline so it stays inside this excluded window rather than in a separate popup window.
        var section = new StackPanel { Margin = new Thickness(4, 0, 4, 2) };
        quality = Choice("RecordingQuality", ("Compact", RecordingQuality.Compact), ("Balanced", RecordingQuality.Balanced), ("High", RecordingQuality.High));
        quality.SelectionChanged += (_, _) => { if (quality.SelectedItem is ComboBoxItem { Tag: RecordingQuality q }) Apply(x => x with { Quality = q }); };
        rate = Choice("RecordingFps", Rates.Select(r => ($"{r} fps", (object)r)).ToArray());
        rate.SelectionChanged += (_, _) => { if (!syncing && rate.SelectedItem is ComboBoxItem { Tag: int r }) { savedRate = r; Apply(x => x with { FramesPerSecond = r }); } };
        resolution = Choice("RecordingResolution", ("Original size", RecordingResolution.Original), ("1080p", RecordingResolution.FullHd), ("720p", RecordingResolution.Hd), ("Half size", RecordingResolution.Half));
        resolution.SelectionChanged += (_, _) => { if (resolution.SelectedItem is ComboBoxItem { Tag: RecordingResolution r }) Apply(x => x with { Resolution = r }); };
        section.Children.Add(Row("Quality", "Video: Compact makes smaller files; High is sharper. GIF: Compact and Balanced build the colour palette from a quick sample, so conversion is faster; " +
            "High analyses every frame for a full 256-colour palette and smoother gradients. Frame rate and resolution stay as chosen.", quality));
        section.Children.Add(Row("Frame rate", "GIF uses up to 30 fps", rate));
        section.Children.Add(Row("Resolution", "Keeps the source's proportions", resolution));
        section.Children.Add(Rule(false));
        // Do Not Disturb is only a real control when Windows lets Snipsera set it; otherwise a shortcut to the Windows setting.
        if (controller.DoNotDisturbAvailable)
        {
            var box = dnd = new CheckBox { Content = "Do Not Disturb while recording", FontSize = 13 };
            AutomationProperties.SetAutomationId(box, "RecordingDoNotDisturb");
            box.Click += (_, _) => Apply(x => x with { DoNotDisturb = box.IsChecked == true });
            section.Children.Add(box);
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(controller.DoNotDisturbExplanation) ? "Snipsera can't turn on Do Not Disturb automatically on this PC." : controller.DoNotDisturbExplanation;
            var notifications = Small("Notification settings", "RecordingNotificationSettings", () => Safe(controller.OpenNotificationSettings));
            AutomationProperties.SetHelpText(notifications, reason); notifications.ToolTip = reason;
            var line = new DockPanel(); DockPanel.SetDock(notifications, Dock.Right); line.Children.Add(notifications);
            var note = Ui.Text("Do Not Disturb is manual", 12, Ui.Muted); note.VerticalAlignment = VerticalAlignment.Center; line.Children.Add(note);
            section.Children.Add(line);
        }
        var last = new StackPanel();
        last.Children.Add(Rule(false));
        result = Ui.Text("", 12, Ui.Muted); AutomationProperties.SetAutomationId(result, "RecordingResult"); last.Children.Add(result);
        var resultActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        resultActions.Children.Add(Small("Open", "RecordingOpen", () => Safe(controller.OpenLastRecording)));
        resultActions.Children.Add(Small("Show in folder", "RecordingFolder", () => Safe(controller.OpenOutputFolder)));
        last.Children.Add(resultActions);
        resultRow = last; section.Children.Add(last);
        var sectionHost = new StackPanel(); sectionHost.Children.Add(Rule(false)); sectionHost.Children.Add(section);
        more = sectionHost; more.Visibility = Visibility.Collapsed;
        AutomationProperties.SetName(more, "Recording settings");
        settings.Checked += (_, _) => more.Visibility = Visibility.Visible;
        settings.Unchecked += (_, _) => more.Visibility = Visibility.Collapsed;

        var stack = new StackPanel();
        stack.Children.Add(row1); stack.Children.Add(row2); stack.Children.Add(more); stack.Children.Add(status);
        top = RecordingSetupVisuals.Panel(stack, "Recording source and options");

        // Bottom panel: the two record actions, full width.
        recordGif = RecordButton("Gif", "Record GIF", "RecordingRecordGif", "Silent animation, up to 30 fps. Selects first if needed.", RecordingFormat.Gif);
        recordVideo = RecordButton("Video", "Record Video", "RecordingRecordVideo", "MP4 with the chosen audio. Selects first if needed.", RecordingFormat.Mp4);
        recordVideo.Margin = new Thickness(0, 2, 0, 0);
        var buttons = new StackPanel(); buttons.Children.Add(recordGif); buttons.Children.Add(recordVideo);
        actions = RecordingSetupVisuals.Panel(buttons, "Record"); actions.Margin = new Thickness(0, Gap, 0, 0);

        var root = new StackPanel { Width = PanelWidth, Margin = new Thickness(Shadow) };
        root.Children.Add(top); root.Children.Add(actions);
        KeyboardNavigation.SetTabNavigation(root, KeyboardNavigationMode.Cycle);
        Content = root;
        locks = [sourceRow, size, cursor, clicks, microphone, computerAudio, settings, section];
        Restyle();

        // Drag from any panel background; controls keep their own input.
        foreach (var panel in new[] { top, actions })
            panel.MouseLeftButtonDown += (_, e) => { if (Interactive(e.OriginalSource as DependencyObject)) return; DragMove(); moved = true; };
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (customWidth.IsKeyboardFocusWithin || customHeight.IsKeyboardFocusWithin) { if (Revert()) { Refresh(); return; } }
            if (settings.IsChecked == true) { settings.IsChecked = false; settings.Focus(); return; }
            if (!busy) Close();
        };

        Sync();
        SourceInitialized += (_, _) => RecordingPrivacy.Exclude(this);
        Loaded += (_, _) => { Place(); (sources.FirstOrDefault(s => s.IsChecked == true) ?? sources[0]).Focus(); };
        SizeChanged += (_, _) => { if (IsLoaded) Place(); };
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Place));
        controller.Changed += Refresh;
        Appearance.Current.Changed += Restyle;
        Closed += (_, _) => { controller.Changed -= Refresh; Appearance.Current.Changed -= Restyle; };
        Refresh();
    }

    // Selected only when it is this window's own, accepted selection for the current source.
    private RecordingTarget? Fresh => controller.Target is RecordingTarget t && !ReferenceEquals(t, stale) && t.Kind == controller.Options.TargetKind ? t : null;

    private async Task Record(RecordingFormat format)
    {
        if (busy || controller.IsActive) return;
        settings.IsChecked = false;
        if (!CommitDimensions()) { customWidth.Focus(); return; }
        await Act(async () =>
        {
            if (Fresh is null && !await SelectTarget()) return;
            SetFormat(format); if (error is not null) return;
            await controller.StartAsync();
        });
    }

    // Runs the explicit picker. True only when the user accepted a selection; otherwise any surviving older target is marked stale.
    private async Task<bool> SelectTarget()
    {
        canceled = false;
        try
        {
            if (await controller.SelectTargetAsync(controller.Options.TargetKind) && controller.Target is not null) { stale = null; return true; }
        }
        catch { stale = controller.Target; throw; }
        stale = controller.Target; canceled = true; return false;
    }

    // Applies one option edit. On failure the controls return to the controller's current options and the reason shows inline.
    private void Apply(Func<RecordingOptions, RecordingOptions> edit)
    {
        if (syncing) return;
        try { controller.UpdateOptions(edit(controller.Options)); error = null; canceled = false; }
        catch (Exception ex) { error = ex.Message; }
        Sync(); Refresh();
    }

    // GIF has no audio and tops out at 30 fps. The MP4 audio choices and frame rate are remembered and restored for video.
    private void SetFormat(RecordingFormat format)
    {
        if (controller.Options.Format == format) return;
        Apply(o => format == RecordingFormat.Gif
            ? o with { Format = format, Microphone = false, ComputerAudio = false, FramesPerSecond = Math.Min(30, o.FramesPerSecond) }
            : o with { Format = format, Microphone = savedMicrophone, ComputerAudio = savedComputerAudio, FramesPerSecond = Rates.Contains(savedRate) ? savedRate : o.FramesPerSecond });
    }

    // Audio toggles always edit the video choice; while the stored format is GIF the choice is kept until a video is recorded.
    private void Audio(bool mic, bool on)
    {
        if (mic) savedMicrophone = on; else savedComputerAudio = on;
        if (controller.Options.Format == RecordingFormat.Gif) { Sync(); Refresh(); return; }
        Apply(o => mic ? o with { Microphone = on } : o with { ComputerAudio = on });
        savedMicrophone = controller.Options.Microphone; savedComputerAudio = controller.Options.ComputerAudio;
        Sync();
    }

    // Typing a size records a fixed frame of that size. Unchanged fields are not an edit, so tabbing through never switches source.
    private bool CommitDimensions()
    {
        if (customWidth.Text == shownWidth && customHeight.Text == shownHeight && !invalid) return true;
        string? problem = null;
        if (!int.TryParse(customWidth.Text, out int w) || !int.TryParse(customHeight.Text, out int h)) { problem = "Enter a width and a height in pixels."; w = h = 0; }
        else if (w < 16 || h < 16 || w > 7680 || h > 4320) problem = "Use a width from 16 to 7,680 and a height from 16 to 4,320 pixels.";
        else if (w % 2 != 0 || h % 2 != 0) problem = "Use even numbers so the video keeps the exact size.";
        else if (!System.Windows.Forms.Screen.AllScreens.Any(s => s.Bounds.Width >= w && s.Bounds.Height >= h))
        {
            var largest = System.Windows.Forms.Screen.AllScreens.Select(s => s.Bounds).OrderByDescending(b => (long)b.Width * b.Height).First();
            problem = $"That's larger than any display. The largest is {largest.Width:N0} × {largest.Height:N0}.";
        }
        if (problem is not null)
        {
            error = problem; invalid = true; Refresh();
            var field = !int.TryParse(customWidth.Text, out int fw) || fw < 16 || fw > 7680 || fw % 2 != 0 ? customWidth : customHeight;
            AutomationProperties.SetItemStatus(field, problem);
            return false;
        }
        invalid = false; error = null; AutomationProperties.SetItemStatus(customWidth, ""); AutomationProperties.SetItemStatus(customHeight, "");
        var o = controller.Options;
        if (o.TargetKind != RecordingTargetKind.Custom || w != o.CustomWidth || h != o.CustomHeight)
            Apply(x => x with { TargetKind = RecordingTargetKind.Custom, CustomWidth = w, CustomHeight = h });
        else { Sync(); Refresh(); }
        return error is null;
    }

    // Puts the fields back to what they show and drops any field error. True when there was something to undo.
    private bool Revert()
    {
        bool changed = invalid || customWidth.Text != shownWidth || customHeight.Text != shownHeight;
        if (invalid) error = null;
        invalid = false; AutomationProperties.SetItemStatus(customWidth, ""); AutomationProperties.SetItemStatus(customHeight, "");
        syncing = true; customWidth.Text = shownWidth; customHeight.Text = shownHeight; syncing = false;
        return changed;
    }

    private async Task Act(Func<Task> action)
    {
        if (busy) return;
        busy = true; error = null; canceled = false; Refresh();
        try { await action(); }
        catch (Exception ex) { error = ex.Message; }
        finally { busy = false; Sync(); Refresh(); }
    }
    private void Safe(Action action) { try { action(); error = null; } catch (Exception ex) { error = ex.Message; } Refresh(); }

    // Controls ← controller.Options. Only on open, after edits and after errors, never per elapsed tick.
    private void Sync()
    {
        syncing = true;
        try
        {
            var o = controller.Options; bool gif = o.Format == RecordingFormat.Gif;
            for (int i = 0; i < kinds.Length; i++) sources[i].IsChecked = kinds[i] == o.TargetKind;
            Select(quality, o.Quality); Select(rate, o.FramesPerSecond); Select(resolution, o.Resolution);
            foreach (ComboBoxItem item in rate.Items) item.IsEnabled = !gif || item.Tag is int r && r <= 30;
            cursor.IsChecked = o.ShowCursor; clicks.IsChecked = o.HighlightClicks;
            microphone.IsChecked = savedMicrophone; computerAudio.IsChecked = savedComputerAudio;
            if (dnd is not null) dnd.IsChecked = o.DoNotDisturb;
        }
        finally { syncing = false; }
    }

    // Cheap per-tick update: enablement, dimensions (never while being edited), status, last result and placement.
    private void Refresh()
    {
        var state = controller.State;
        bool active = controller.IsActive, locked = active || busy || state == RecordingState.Selecting;
        foreach (var element in locks) element.IsEnabled = !locked;
        recordGif.IsEnabled = recordVideo.IsEnabled = !locked;
        close.IsEnabled = !busy && state != RecordingState.Selecting;
        if (locked && settings.IsChecked == true && active) settings.IsChecked = false;

        var fresh = Fresh; var o = controller.Options;
        (shownWidth, shownHeight) = fresh is not null ? (fresh.Bounds.Width.ToString(), fresh.Bounds.Height.ToString())
            : o.TargetKind == RecordingTargetKind.Custom ? (o.CustomWidth.ToString(), o.CustomHeight.ToString()) : ("", "");
        if (!invalid && !customWidth.IsKeyboardFocusWithin && !customHeight.IsKeyboardFocusWithin && (customWidth.Text != shownWidth || customHeight.Text != shownHeight))
        { syncing = true; customWidth.Text = shownWidth; customHeight.Text = shownHeight; syncing = false; }

        string text = error ?? state switch
        {
            RecordingState.Selecting => "Selecting… Esc cancels.",
            RecordingState.Starting => "Starting…",
            RecordingState.Recording or RecordingState.Paused => "Recording. Pause or stop it from the recording bar.",
            RecordingState.Stopping => "Saving…",
            RecordingState.Converting => "Creating GIF…",
            RecordingState.Failed => string.IsNullOrWhiteSpace(controller.Status) ? "Recording failed." : controller.Status,
            RecordingState.Completed when fresh is not null => controller.Status + " Record again or choose a new source.",
            _ when canceled => "Selection canceled. Nothing will be recorded until you select again.",
            _ when fresh is not null => fresh.Kind switch
            {
                RecordingTargetKind.Window => $"Window ready · {fresh.Label}",
                RecordingTargetKind.Display => $"{fresh.Label} ready",
                _ => "Area ready · Record GIF or Video"
            },
            _ => o.TargetKind switch
            {
                RecordingTargetKind.Window => "Record asks you to choose a window first.",
                RecordingTargetKind.Display => "Record asks you to choose a display first.",
                RecordingTargetKind.Custom => $"Record positions a {o.CustomWidth:N0} × {o.CustomHeight:N0} px frame first.",
                _ => "Record asks you to select an area first. Esc cancels."
            }
        };
        status.Text = text;
        status.Foreground = error is not null || state == RecordingState.Failed ? ErrorBrush : Ui.Muted;
        if (text != announced)
        {
            announced = text;
            if (IsLoaded) UIElementAutomationPeer.CreatePeerForElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        if (controller.LastResult is RecordingResult r)
        {
            resultRow.Visibility = Visibility.Visible;
            result.Text = $"Last: {System.IO.Path.GetFileName(r.FilePath)} · {(r.Format == RecordingFormat.Gif ? "GIF" : "MP4")} · {r.Width:N0} × {r.Height:N0} · {RecordingBarWindow.Clock(r.Duration)}";
        }
        else resultRow.Visibility = Visibility.Collapsed;

        // A newly accepted selection re-anchors the panel next to it, even if it had been dragged.
        if (fresh is not null && !ReferenceEquals(fresh, anchored)) { anchored = fresh; moved = false; if (IsLoaded) Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(Place)); }
    }

    // Just outside the accepted selection when there is room; otherwise bottom centre of its work area (a full-display target),
    // or of the work area the window first opened on. The window is excluded from capture either way.
    private void Place()
    {
        if (moved || !IsVisible) return;
        if (anchored is RecordingTarget t && ReferenceEquals(t, Fresh))
        {
            if (PlaceOutside(this, t.Bounds, Shadow)) return;
            PlaceBottom(this, WorkAreaFor(t.Bounds), Shadow, 32); return;
        }
        if (home.IsEmpty) home = Native.MonitorAtCursor(true);
        PlaceBottom(this, home, Shadow, 48);
    }

    private void Restyle()
    {
        foreach (var radio in sources) RecordingSetupVisuals.Apply(radio, "Segment");
        foreach (var toggle in new[] { cursor, clicks, microphone, computerAudio }) RecordingSetupVisuals.Apply(toggle, "Toggle");
        RecordingSetupVisuals.Apply(settings, "Segment"); RecordingSetupVisuals.Apply(close, "Tool");
        RecordingSetupVisuals.Apply(recordGif, "Action"); RecordingSetupVisuals.Apply(recordVideo, "Action");
        RecordingSetupVisuals.Restyle(top); RecordingSetupVisuals.Restyle(actions);
        if (IsLoaded) Refresh();
    }

    private static void Square(ButtonBase button)
    {
        button.Width = button.Height = Tool; button.MinWidth = button.MinHeight = 0; button.Padding = new Thickness(0); button.Margin = new Thickness(0, 0, 2, 0);
        button.HorizontalContentAlignment = HorizontalAlignment.Center; button.VerticalContentAlignment = VerticalAlignment.Center;
        ToolTipService.SetShowOnDisabled(button, true);
    }
    private static ToggleButton Capture(string glyph, string label, string id, string? help, Action<bool> changed)
    {
        var button = new ToggleButton { Content = new RecordingGlyph(glyph), ToolTip = help is null ? label : label + " — " + help };
        Square(button); AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label);
        if (help is not null) AutomationProperties.SetHelpText(button, help);
        button.Click += (_, _) => changed(button.IsChecked == true);
        return button;
    }
    private Button RecordButton(string glyph, string label, string id, string help, RecordingFormat format)
    {
        var text = new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var content = new StackPanel { Orientation = Orientation.Horizontal }; content.Children.Add(new RecordingGlyph(glyph, 18)); content.Children.Add(text);
        var button = new Button { Content = content, Height = ActionHeight, MinHeight = 0, Padding = new Thickness(10, 0, 10, 0), HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = help };
        AutomationProperties.SetAutomationId(button, id); AutomationProperties.SetName(button, label); AutomationProperties.SetHelpText(button, help);
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await Record(format);
        return button;
    }
    private static Button Small(string label, string id, Action action)
    {
        var button = Ui.Button(label, id, action); button.FontSize = 12; button.MinHeight = 28; button.Padding = new Thickness(10, 3, 10, 3);
        button.Margin = new Thickness(0, 0, 6, 0); return button;
    }
    private static FrameworkElement Row(string label, string hint, ComboBox control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Ui.Text(label, 13); text.VerticalAlignment = VerticalAlignment.Center; text.ToolTip = Tip(hint);
        AutomationProperties.SetName(control, label); AutomationProperties.SetHelpText(control, hint); AutomationProperties.SetLabeledBy(control, text); control.ToolTip = Tip(hint);
        Grid.SetColumn(control, 1); grid.Children.Add(text); grid.Children.Add(control); return grid;
    }
    // Hints can run to a few sentences; wrap them instead of drawing one long tooltip line.
    private static TextBlock Tip(string hint) => new() { Text = hint, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 };
    private static ComboBox Choice(string id, params (string Label, object Value)[] items)
    {
        var box = new ComboBox { MinWidth = 148, FontSize = 13 };
        foreach (var (label, value) in items) box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        AutomationProperties.SetAutomationId(box, id); return box;
    }
    private static void Select(ComboBox box, object value) => box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, value));
    private TextBox Number(string id, string name)
    {
        var box = new TextBox { Width = 60, MinWidth = 0, MaxLength = 4, FontSize = 13, Padding = new Thickness(6, 5, 6, 6), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        Typography.SetNumeralAlignment(box, FontNumeralAlignment.Tabular);
        AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, name);
        AutomationProperties.SetHelpText(box, "Physical pixels. Type a size to record a fixed frame; use even numbers. Esc undoes the edit.");
        box.ToolTip = name + " (physical pixels)";
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        DataObject.AddPastingHandler(box, (_, e) => { if (e.DataObject.GetData(typeof(string)) is not string s || !s.All(char.IsDigit)) e.CancelCommand(); });
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        box.TextChanged += (_, _) => { if (!syncing && invalid) { invalid = false; error = null; AutomationProperties.SetItemStatus(box, ""); Refresh(); } };
        // Commit when focus leaves the pair, not when moving from width to height.
        box.LostKeyboardFocus += (_, e) => { if (e.NewFocus != customWidth && e.NewFocus != customHeight && IsVisible && !busy) CommitDimensions(); };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitDimensions(); e.Handled = true; } };
        return box;
    }
    // A text box with a muted cue while it's empty (no selection yet).
    private static FrameworkElement Field(TextBox box, string cue)
    {
        var hint = new TextBlock { Text = cue, FontSize = 13, Foreground = Ui.Muted, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        box.TextChanged += (_, _) => hint.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var grid = new Grid(); grid.Children.Add(box); grid.Children.Add(hint); return grid;
    }
    private static bool Interactive(DependencyObject? node)
    {
        for (; node is not null; node = node is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
            if (node is ButtonBase or TextBoxBase or Selector) return true;
        return false;
    }
}
