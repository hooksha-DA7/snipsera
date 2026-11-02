using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Framelet;

// Recording setup: source, output and capture options, then Select → Start. Uses the native Windows caption and Fluent controls
// like Home. Options apply immediately through the controller; while a recording runs they are locked and the floating bar
// owns Pause/Stop, so nothing here can stop a recording by accident.
public sealed class RecordingWindow : Window
{
    private static readonly int[] Rates = [5, 10, 15, 24, 30, 60];
    private static readonly Brush Error = Ui.Brush("#A33E16");
    private readonly RecordingController controller;
    private readonly RadioButton[] sources;
    private readonly RecordingTargetKind[] kinds = [RecordingTargetKind.Area, RecordingTargetKind.Window, RecordingTargetKind.Display, RecordingTargetKind.Custom];
    private readonly RadioButton mp4, gif;
    private readonly ComboBox quality, rate, resolution;
    private readonly TextBox customWidth, customHeight;
    private readonly FrameworkElement customRow;
    private readonly CheckBox cursor, clicks, microphone, computerAudio;
    private readonly CheckBox? dnd;
    private readonly TextBlock audioNote, status, target, result;
    private readonly Button select, start;
    private readonly Border resultCard;
    private readonly StackPanel options = new();
    private bool syncing, busy, savedMicrophone, savedComputerAudio;
    private string? error;
    private RecordingState announced = (RecordingState)(-1);

    public RecordingWindow(CaptureApp app)
    {
        controller = app.Recording; Ui.Setup(this);
        Title = "Framelet — record screen"; Ui.UseSystemChrome(this);
        Width = 500; Height = 720; MinWidth = 440; MinHeight = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var dock = new DockPanel { Margin = new Thickness(20) };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(Ui.Text("Record screen", 20, Ui.Ink, true));
        header.Children.Add(Ui.Text("Choose what to record, then start. Recordings are kept in Framelet's history.", 12, Ui.Muted));
        DockPanel.SetDock(header, Dock.Top); dock.Children.Add(header);

        // Footer: a full-width wrapping status line, then Select and Start right-aligned, set off by a hairline.
        // Stop/Pause live only on the recording bar.
        status = Ui.Text("", 12, Ui.Muted); status.Margin = new Thickness(0, 0, 0, 10);
        AutomationProperties.SetAutomationId(status, "RecordingStatus"); AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        select = Ui.Button("Select area…", "RecordingSelect", async () => { if (CommitCustom()) await Act(() => controller.SelectTargetAsync(controller.Options.TargetKind)); });
        start = Ui.Button("Start recording", "RecordingStart", async () => { if (CommitCustom()) await Act(controller.StartAsync); }, true);
        start.Margin = new Thickness(0);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(select); buttons.Children.Add(start);
        var footerStack = new StackPanel(); footerStack.Children.Add(status); footerStack.Children.Add(buttons);
        var footer = new Border { Child = footerStack, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 12, 0, 0), Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); dock.Children.Add(footer);

        // Source
        var source = Section("Source");
        // A fixed 2 × 2 grid: Fluent radio buttons have a wide minimum, so a wrap panel splits 3 + 1 at common widths.
        var sourceRow = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
        string[] sourceLabels = ["Area", "Window", "Display", "Custom size"];
        sources = new RadioButton[kinds.Length];
        for (int i = 0; i < kinds.Length; i++)
        {
            var kind = kinds[i];
            sources[i] = new RadioButton { Content = sourceLabels[i], GroupName = "RecordingSource", Margin = new Thickness(0, 2, 12, 2), MinWidth = 0 };
            AutomationProperties.SetAutomationId(sources[i], "RecordingSource" + kind);
            sources[i].Checked += (_, _) => Apply(o => o with { TargetKind = kind });
            sourceRow.Children.Add(sources[i]);
        }
        source.Children.Add(sourceRow);
        customWidth = Number("RecordingCustomWidth", "Width in pixels"); customHeight = Number("RecordingCustomHeight", "Height in pixels");
        var custom = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        custom.Children.Add(customWidth); custom.Children.Add(Ui.Text("×", 13, Ui.Muted)); ((TextBlock)custom.Children[1]).Margin = new Thickness(8, 0, 8, 0); ((TextBlock)custom.Children[1]).VerticalAlignment = VerticalAlignment.Center;
        custom.Children.Add(customHeight); var unit = Ui.Text("px", 12, Ui.Muted); unit.Margin = new Thickness(8, 0, 0, 0); unit.VerticalAlignment = VerticalAlignment.Center; custom.Children.Add(unit);
        customRow = custom; source.Children.Add(custom);
        target = Ui.Text("", 12, Ui.Muted); target.Margin = new Thickness(0, 8, 0, 0); AutomationProperties.SetAutomationId(target, "RecordingTarget");
        source.Children.Add(target);

        // Output
        var output = Section("Output");
        // Same two columns as Source, so the radio buttons line up between cards.
        var formats = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
        mp4 = new RadioButton { Content = "MP4 video", GroupName = "RecordingFormat", Margin = new Thickness(0, 2, 12, 2), MinWidth = 0 };
        gif = new RadioButton { Content = "GIF animation", GroupName = "RecordingFormat", Margin = new Thickness(0, 2, 12, 2), MinWidth = 0 };
        AutomationProperties.SetAutomationId(mp4, "RecordingMp4"); AutomationProperties.SetAutomationId(gif, "RecordingGif");
        mp4.Checked += (_, _) => SetFormat(RecordingFormat.Mp4); gif.Checked += (_, _) => SetFormat(RecordingFormat.Gif);
        formats.Children.Add(mp4); formats.Children.Add(gif); output.Children.Add(formats);
        quality = Choice("RecordingQuality", ("Compact", RecordingQuality.Compact), ("Balanced", RecordingQuality.Balanced), ("High", RecordingQuality.High));
        quality.SelectionChanged += (_, _) => { if (quality.SelectedItem is ComboBoxItem { Tag: RecordingQuality q }) Apply(o => o with { Quality = q }); };
        rate = Choice("RecordingFps", Rates.Select(r => ($"{r} fps", (object)r)).ToArray());
        rate.SelectionChanged += (_, _) => { if (rate.SelectedItem is ComboBoxItem { Tag: int r }) Apply(o => o with { FramesPerSecond = r }); };
        resolution = Choice("RecordingResolution", ("Original size", RecordingResolution.Original), ("1080p", RecordingResolution.FullHd), ("720p", RecordingResolution.Hd), ("Half size", RecordingResolution.Half));
        resolution.SelectionChanged += (_, _) => { if (resolution.SelectedItem is ComboBoxItem { Tag: RecordingResolution r }) Apply(o => o with { Resolution = r }); };
        output.Children.Add(Row("Quality", "Compact makes smaller files; High is sharper", quality));
        output.Children.Add(Row("Frame rate", "GIF is limited to 30 fps", rate));
        output.Children.Add(Row("Resolution", "Keeps the source's proportions", resolution));

        // Capture
        var capture = Section("Capture");
        cursor = Toggle("Show cursor", "RecordingCursor", (o, on) => o with { ShowCursor = on });
        clicks = Toggle("Highlight clicks", "RecordingClicks", (o, on) => o with { HighlightClicks = on });
        microphone = Toggle("Microphone", "RecordingMicrophone", (o, on) => o with { Microphone = on });
        computerAudio = Toggle("Computer audio", "RecordingComputerAudio", (o, on) => o with { ComputerAudio = on });
        capture.Children.Add(Described(cursor, null)); capture.Children.Add(Described(clicks, "Shows a blue ring where you click"));
        capture.Children.Add(Described(microphone, null)); capture.Children.Add(Described(computerAudio, null));
        audioNote = Ui.Text("GIF recordings have no sound. Choose MP4 to record audio.", 11, Ui.Muted); audioNote.Margin = new Thickness(28, 2, 0, 0);
        capture.Children.Add(audioNote);

        // Focus: Do Not Disturb is only a real control when Windows lets Framelet set it.
        var focus = Section("Notifications");
        if (controller.DoNotDisturbAvailable)
        {
            dnd = Toggle("Turn on Do Not Disturb while recording", "RecordingDoNotDisturb", (o, on) => o with { DoNotDisturb = on });
            focus.Children.Add(Described(dnd, null));
        }
        else
        {
            var reason = string.IsNullOrWhiteSpace(controller.DoNotDisturbExplanation) ? "Framelet can't turn on Do Not Disturb automatically on this PC." : controller.DoNotDisturbExplanation;
            focus.Children.Add(Ui.Text("Do Not Disturb isn't available automatically", 13, Ui.Ink));
            var why = Ui.Text(reason, 11, Ui.Muted); why.Margin = new Thickness(0, 2, 0, 6); focus.Children.Add(why);
            var settings = Ui.Button("Open notification settings", "RecordingNotificationSettings", () => Safe(controller.OpenNotificationSettings));
            settings.HorizontalAlignment = HorizontalAlignment.Left; settings.Margin = new Thickness(0); focus.Children.Add(settings);
        }

        // Last result
        var last = new StackPanel();
        last.Children.Add(Ui.Text("Last recording", 13, Ui.Ink, true));
        result = Ui.Text("", 12, Ui.Muted); result.Margin = new Thickness(0, 4, 0, 8); AutomationProperties.SetAutomationId(result, "RecordingResult"); last.Children.Add(result);
        var resultActions = new StackPanel { Orientation = Orientation.Horizontal };
        resultActions.Children.Add(Ui.Button("Open", "RecordingOpen", () => Safe(controller.OpenLastRecording)));
        resultActions.Children.Add(Ui.Button("Show in folder", "RecordingFolder", () => Safe(controller.OpenOutputFolder)));
        last.Children.Add(resultActions);
        resultCard = Ui.Card(last, new Thickness(14)); resultCard.Margin = new Thickness(0, 0, 0, 12);

        foreach (var panel in new[] { source, output, capture, focus })
        { var card = Ui.Card(panel, new Thickness(14)); card.Margin = new Thickness(0, 0, 0, 12); options.Children.Add(card); }
        var scroll = new StackPanel(); scroll.Children.Add(resultCard); scroll.Children.Add(options);
        dock.Children.Add(new ScrollViewer { Content = scroll, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false });
        Content = dock;

        Sync();
        SourceInitialized += (_, _) => RecordingPrivacy.Exclude(this);
        controller.Changed += Refresh;
        Closed += (_, _) => controller.Changed -= Refresh;
        Refresh();
    }

    // Applies one option edit. On failure the controls return to the controller's current options and the reason shows inline.
    private void Apply(Func<RecordingOptions, RecordingOptions> edit)
    {
        if (syncing) return;
        try { controller.UpdateOptions(edit(controller.Options)); error = null; }
        catch (Exception ex) { error = ex.Message; }
        Sync(); Refresh();
    }

    // GIF has no audio and tops out at 30 fps. Audio choices are remembered and restored on switching back to MP4.
    private void SetFormat(RecordingFormat format)
    {
        if (syncing) return;
        var current = controller.Options;
        if (format == RecordingFormat.Gif && current.Format != RecordingFormat.Gif) { savedMicrophone = current.Microphone; savedComputerAudio = current.ComputerAudio; }
        Apply(o => format == RecordingFormat.Gif
            ? o with { Format = format, Microphone = false, ComputerAudio = false, FramesPerSecond = Math.Min(30, o.FramesPerSecond) }
            : o with { Format = format, Microphone = o.Format == RecordingFormat.Gif ? savedMicrophone : o.Microphone, ComputerAudio = o.Format == RecordingFormat.Gif ? savedComputerAudio : o.ComputerAudio });
    }

    // Validates both dimension fields; leaves the previous values in place with an inline error if either is unusable.
    private bool CommitCustom()
    {
        if (controller.Options.TargetKind != RecordingTargetKind.Custom) return true;
        if (!int.TryParse(customWidth.Text, out int w) || !int.TryParse(customHeight.Text, out int h) || w < 16 || h < 16 || w > 7680 || h > 4320)
        { error = "Enter a width from 16 to 7,680 and a height from 16 to 4,320 pixels."; Refresh(); return false; }
        error = null;
        if (w != controller.Options.CustomWidth || h != controller.Options.CustomHeight) Apply(o => o with { CustomWidth = w, CustomHeight = h });
        Refresh(); return error is null;
    }

    private async Task Act(Func<Task> action)
    {
        if (busy) return;
        busy = true; error = null; Refresh();
        try { await action(); }
        catch (Exception ex) { error = ex.Message; }
        finally { busy = false; Sync(); Refresh(); }
    }
    private void Safe(Action action) { try { action(); error = null; } catch (Exception ex) { error = ex.Message; } Refresh(); }

    // Controls ← controller.Options. Only on open, after edits and after errors, never per elapsed tick, so typing isn't overwritten.
    private void Sync()
    {
        syncing = true;
        try
        {
            var o = controller.Options;
            for (int i = 0; i < kinds.Length; i++) sources[i].IsChecked = kinds[i] == o.TargetKind;
            mp4.IsChecked = o.Format == RecordingFormat.Mp4; gif.IsChecked = o.Format == RecordingFormat.Gif;
            Select(quality, o.Quality); Select(rate, o.FramesPerSecond); Select(resolution, o.Resolution);
            foreach (ComboBoxItem item in rate.Items) item.IsEnabled = o.Format != RecordingFormat.Gif || item.Tag is int r && r <= 30;
            if (!customWidth.IsKeyboardFocused) customWidth.Text = o.CustomWidth.ToString();
            if (!customHeight.IsKeyboardFocused) customHeight.Text = o.CustomHeight.ToString();
            customRow.Visibility = o.TargetKind == RecordingTargetKind.Custom ? Visibility.Visible : Visibility.Collapsed;
            cursor.IsChecked = o.ShowCursor; clicks.IsChecked = o.HighlightClicks;
            bool audio = o.Format != RecordingFormat.Gif;
            microphone.IsEnabled = computerAudio.IsEnabled = audio;
            microphone.IsChecked = audio && o.Microphone; computerAudio.IsChecked = audio && o.ComputerAudio;
            audioNote.Visibility = audio ? Visibility.Collapsed : Visibility.Visible;
            if (dnd is not null) dnd.IsChecked = o.DoNotDisturb;
            select.Content = o.TargetKind switch
            {
                RecordingTargetKind.Window => "Choose window…",
                RecordingTargetKind.Display => "Choose display…",
                RecordingTargetKind.Custom => "Position frame…",
                _ => "Select area…"
            };
            AutomationProperties.SetName(select, (string)select.Content);
        }
        finally { syncing = false; }
    }

    // Cheap per-tick update: enablement, target, status and the last result.
    private void Refresh()
    {
        var state = controller.State;
        bool active = controller.IsActive, locked = active || busy || state == RecordingState.Selecting;
        options.IsEnabled = !locked;
        select.IsEnabled = !locked;
        start.IsEnabled = !locked && controller.CanStart;
        start.Content = active ? "Recording…" : "Start recording";

        target.Text = controller.Target is RecordingTarget t
            ? $"{(string.IsNullOrWhiteSpace(t.Label) ? t.Kind.ToString() : t.Label)} · {t.Bounds.Width:N0} × {t.Bounds.Height:N0} px"
            : "Nothing selected yet. Choose a source, then select it.";

        string text = error ?? (active ? $"{controller.Status} · {RecordingBarWindow.Clock(controller.Elapsed)}" : controller.Status);
        status.Text = string.IsNullOrWhiteSpace(text) ? (controller.CanStart ? "Ready to record" : "Select what to record") : text;
        status.Foreground = error is not null || state == RecordingState.Failed ? Error : Ui.Muted;
        if (active && error is null) status.Text += " — use the recording bar to pause or stop";

        if (controller.LastResult is RecordingResult r)
        {
            resultCard.Visibility = Visibility.Visible;
            result.Text = $"{System.IO.Path.GetFileName(r.FilePath)} · {(r.Format == RecordingFormat.Gif ? "GIF" : "MP4")} · {r.Width:N0} × {r.Height:N0} · {RecordingBarWindow.Clock(r.Duration)}";
        }
        else resultCard.Visibility = Visibility.Collapsed;

        if (state != announced)
        {
            announced = state;
            if (IsLoaded) UIElementAutomationPeer.CreatePeerForElement(status)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }
    }

    private static StackPanel Section(string title)
    {
        var panel = new StackPanel(); var heading = Ui.Text(title, 13, Ui.Ink, true); heading.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(heading); return panel;
    }
    private static FrameworkElement Row(string label, string hint, Control control)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition()); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(Ui.Text(label, 13)); text.Children.Add(Ui.Text(hint, 11, Ui.Muted));
        AutomationProperties.SetName(control, label); AutomationProperties.SetHelpText(control, hint);
        Grid.SetColumn(control, 1); control.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(text); grid.Children.Add(control); return grid;
    }
    private static FrameworkElement Described(CheckBox box, string? hint)
    {
        if (hint is null) { box.Margin = new Thickness(0, 4, 0, 4); return box; }
        var panel = new StackPanel { Margin = new Thickness(0, 4, 0, 4) }; panel.Children.Add(box);
        var note = Ui.Text(hint, 11, Ui.Muted); note.Margin = new Thickness(28, 0, 0, 0); panel.Children.Add(note);
        AutomationProperties.SetHelpText(box, hint); return panel;
    }
    private CheckBox Toggle(string label, string id, Func<RecordingOptions, bool, RecordingOptions> edit)
    {
        var box = new CheckBox { Content = label };
        AutomationProperties.SetAutomationId(box, id);
        box.Click += (_, _) => Apply(o => edit(o, box.IsChecked == true));
        return box;
    }
    private static ComboBox Choice(string id, params (string Label, object Value)[] items)
    {
        var box = new ComboBox { MinWidth = 150 };
        foreach (var (label, value) in items) box.Items.Add(new ComboBoxItem { Content = label, Tag = value });
        AutomationProperties.SetAutomationId(box, id); return box;
    }
    private static void Select(ComboBox box, object value) => box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => Equals(i.Tag, value));
    private TextBox Number(string id, string name)
    {
        var box = new TextBox { Width = 84, MaxLength = 4, HorizontalContentAlignment = HorizontalAlignment.Right };
        AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, name);
        box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsDigit);
        DataObject.AddPastingHandler(box, (_, e) => { if (e.DataObject.GetData(typeof(string)) is not string s || !s.All(char.IsDigit)) e.CancelCommand(); });
        box.LostKeyboardFocus += (_, _) => CommitCustom();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { CommitCustom(); e.Handled = true; } };
        return box;
    }
}
