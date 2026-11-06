using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using static Framelet.SettingsUi;

namespace Framelet;

// Keyboard shortcut editing for Settings. Each box records the keys actually pressed; nothing changes until Save, and a save
// that Windows rejects keeps the previous shortcuts working. The owner calls Release whenever the page stops being in use
// (hidden, deactivated, closed or another section chosen) so the capture shortcuts are never left suspended, and Resume
// after reactivation.
public sealed class ShortcutSettingsPanel : StackPanel
{
    internal static readonly string[] Labels = ["Select an area", "Choose a window", "Capture this display", "Repeat last area", "All-in-one capture"];
    private static readonly string[] Hints = ["Drag a rectangle across your screen", "Bring a visible window forward and capture it", "Capture the display under your pointer",
        "Capture the same area again", "Open the capture bar with every screenshot mode"];
    // Below this width each box moves under its label instead of beside it.
    private const double StackBelow = 460;
    private readonly CaptureApp app;
    private readonly ShortcutRecorder[] recorders = new ShortcutRecorder[Labels.Length];
    private readonly Grid[] rows = new Grid[Labels.Length];
    private readonly TextBlock status = Ui.Text("", 12, Ui.Muted), error = Ui.Text("", 12);
    private readonly Button save, discard;
    private bool attached = true;

    public ShortcutSettingsPanel(CaptureApp app)
    {
        this.app = app ?? throw new ArgumentNullException(nameof(app));
        AutomationProperties.SetAutomationId(this, "ShortcutSettings");
        var list = new StackPanel();
        var help = Ui.Text("Select a box and press the keys you want, then select Save shortcuts. Esc cancels a change; Tab moves on.", 12, Ui.Muted);
        var rule = Ui.Text(ShortcutRecorder.Rule, 12, Ui.Muted); rule.Margin = new Thickness(0, 4, 0, 0);
        list.Children.Add(help); list.Children.Add(rule);
        for (int i = 0; i < recorders.Length; i++)
        {
            list.Children.Add(Rule());
            var label = Ui.Text(Labels[i], 14); var hint = Ui.Text(Hints[i], 12, Ui.Muted);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(label); text.Children.Add(hint);
            var recorder = recorders[i] = new ShortcutRecorder(Labels[i], app.Settings.Bindings[i], app.SuspendShortcuts, ex => Fail(ex.Message))
                { FontSize = 13, MinWidth = 180, Padding = new Thickness(8, 6, 8, 6), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetAutomationId(recorder, "Shortcut" + i); AutomationProperties.SetLabeledBy(recorder, label);
            recorder.Changed += Changed; recorder.Rejected += Fail;
            var row = rows[i] = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); row.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            row.Children.Add(text); row.Children.Add(recorder);
            list.Children.Add(row);
        }
        Children.Add(Group("Capture shortcuts", list));

        save = Ui.Button("Save shortcuts", "SaveShortcuts", Save, true);
        discard = Ui.Button("Discard changes", "DiscardShortcuts", Discard);
        var actions = Actions(save, discard);
        Describe(status, "ShortcutStatus", AutomationLiveSetting.Polite); status.Margin = new Thickness(2, 4, 0, 0);
        Describe(error, "ShortcutError", AutomationLiveSetting.Assertive); error.Margin = new Thickness(2, 4, 0, 0);
        Children.Add(actions); Children.Add(status); Children.Add(error);

        SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        app.SettingsChanged += SettingsChanged; Appearance.Current.Changed += Sync;
        Arrange(double.PositiveInfinity); Sync();
    }

    // Restores the capture shortcuts. Safe to call repeatedly.
    public void Release() { foreach (var recorder in recorders) recorder.Release(); }
    // Suspends them again if a box still has keyboard focus after the window is reactivated.
    public void Resume() { foreach (var recorder in recorders) recorder.Resume(); }
    // Releases and stops listening for settings changes; used when the Settings window closes.
    public void Detach()
    {
        Release();
        if (!attached) return;
        attached = false; app.SettingsChanged -= SettingsChanged; Appearance.Current.Changed -= Sync;
    }

    private void Save()
    {
        try
        {
            // Save moves focus off the recorder first, so the current shortcuts are restored before they are replaced.
            app.UpdateSettings(new AppSettings(recorders[0].Shortcut, recorders[1].Shortcut, recorders[2].Shortcut, recorders[3].Shortcut, recorders[4].Shortcut));
            for (int i = 0; i < recorders.Length; i++) recorders[i].MarkSaved(app.Settings.Bindings[i]);
            Report("Shortcuts saved.", null);
        }
        // The previous shortcuts stay registered; the pending boxes stay so the person can adjust them.
        catch (Exception ex) { Report(null, ex.Message + " Your previous shortcuts are still in use."); }
    }

    private void Discard()
    {
        foreach (var recorder in recorders) recorder.Cancel();
        Report("Changes discarded.", null);
    }

    private void Changed(ShortcutRecorder recorder)
    {
        int index = Array.IndexOf(recorders, recorder);
        Report(recorder.IsPending ? $"{Labels[index]}: {recorder.Shortcut}. Select Save shortcuts to apply." : $"{Labels[index]} is unchanged.", null);
    }

    private void Fail(string message) => Report(null, message);

    // Saved elsewhere: adopt the new bindings, but never overwrite a change the person is still editing.
    private void SettingsChanged()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(SettingsChanged)); return; }
        if (!attached) return;
        for (int i = 0; i < recorders.Length; i++) if (!recorders[i].IsPending) recorders[i].MarkSaved(app.Settings.Bindings[i]);
        Sync();
    }

    private void Report(string? message, string? problem) { Show(status, message); Show(error, problem); Sync(); }

    private void Sync()
    {
        bool pending = recorders.Any(r => r.IsPending);
        // Buttons stay focusable; Discard is only offered when there is something to discard.
        discard.IsEnabled = pending;
        AutomationProperties.SetItemStatus(save, pending ? "Unsaved changes" : "");
        error.Foreground = UpdateNotice.Error;
    }

    private void Arrange(double width)
    {
        bool stacked = width < StackBelow;
        foreach (var row in rows)
        {
            var box = row.Children[1];
            Grid.SetRow(box, stacked ? 1 : 0); Grid.SetColumn(box, stacked ? 0 : 1);
            ((FrameworkElement)box).HorizontalAlignment = stacked ? HorizontalAlignment.Stretch : HorizontalAlignment.Right;
            ((FrameworkElement)box).Margin = stacked ? new Thickness(0, 8, 0, 0) : new Thickness(16, 0, 0, 0);
        }
    }
}
