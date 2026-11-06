using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using static Framelet.SettingsUi;

namespace Framelet;

// Appearance for Settings: the surface behind the screenshot, recording setup and recording controls. Changes apply at once;
// keyboard and track changes to the opacity are saved after a short pause, and a drag is saved when it ends, so moving the
// slider doesn't write the settings file on every step. A failed save shows the error and puts the controls back to the saved
// choice. Selecting Frosted glass doesn't mean Windows is drawing it; the capability line says what Windows allows right now.
public sealed class AppearanceSettingsPanel : StackPanel
{
    private const int Lowest = 70, Highest = 100;
    private static readonly int[] Presets = [70, 80, 90, 100];
    private readonly ToolbarAppearanceSettings settings;
    private readonly RadioButton tinted, frosted;
    private readonly Slider opacity;
    private readonly TextBlock value = Ui.Text("", 13), capability = Ui.Text("", 12, Ui.Muted), notice = Ui.Text("", 12),
        status = Ui.Text("", 12, Ui.Muted), error = Ui.Text("", 12);
    private readonly Border noticeCard;
    private readonly DispatcherTimer pause;
    private string? problem;
    private bool syncing, dragging, attached = true;

    public AppearanceSettingsPanel(ToolbarAppearanceSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        AutomationProperties.SetAutomationId(this, "ToolbarAppearanceSettings");

        // A recovered or reset configuration is explained here rather than silently changing how the controls look.
        Describe(notice, "ToolbarAppearanceNotice", AutomationLiveSetting.Polite);
        noticeCard = new Border { BorderBrush = Ui.Blue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 12, 0, 0), Child = notice };
        Children.Add(noticeCard);

        var material = new StackPanel();
        tinted = Choice("Tinted surface", "ToolbarTinted", "A see-through color surface without blur. What's behind the controls stays sharp.", material);
        material.Children.Add(new Border { Height = 12 });
        frosted = Choice("Frosted glass", "ToolbarFrosted", "The same tint, with Windows softening what's behind the controls. Details blur; colors remain.", material);
        tinted.Checked += (_, _) => Picked(); frosted.Checked += (_, _) => Picked();
        material.Children.Add(Rule());
        Describe(capability, "ToolbarAppearanceCapability", AutomationLiveSetting.Polite); material.Children.Add(capability);
        Children.Add(Group("Control surface", material));

        var see = new StackPanel();
        var caption = Ui.Text("Background opacity", 14);
        Describe(value, "ToolbarOpacityValue", AutomationLiveSetting.Off); value.TextWrapping = TextWrapping.NoWrap; value.Margin = new Thickness(12, 0, 0, 0);
        var heading = new DockPanel(); DockPanel.SetDock(value, Dock.Right); heading.Children.Add(value); heading.Children.Add(caption);
        see.Children.Add(heading);
        const string help = "Higher opacity hides more of the background; Windows controls the frosted effect.";
        opacity = new Slider { Minimum = Lowest, Maximum = Highest, SmallChange = 1, LargeChange = 10, TickFrequency = 1, IsSnapToTickEnabled = true,
            TickPlacement = TickPlacement.None, Margin = new Thickness(0, 8, 0, 0), Value = 80 };
        AutomationProperties.SetAutomationId(opacity, "ToolbarOpacity"); AutomationProperties.SetName(opacity, "Background opacity");
        AutomationProperties.SetHelpText(opacity, help); AutomationProperties.SetLabeledBy(opacity, caption);
        opacity.ValueChanged += (_, _) => Moved();
        opacity.AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => dragging = true), true);
        opacity.AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, _) => { dragging = false; Save(); }), true);
        see.Children.Add(opacity);
        var presets = new Button[Presets.Length];
        for (int i = 0; i < Presets.Length; i++)
        {
            int preset = Presets[i];
            presets[i] = Ui.Button($"{preset}%", $"ToolbarOpacity{preset}", () => { syncing = true; opacity.Value = preset; syncing = false; Save(); });
            AutomationProperties.SetName(presets[i], $"Set background opacity to {preset}%");
        }
        see.Children.Add(Actions(presets));
        var hint = Ui.Text(help + " Icons and text always stay fully opaque.", 12, Ui.Muted); hint.Margin = new Thickness(0, 4, 0, 0);
        see.Children.Add(hint);
        Children.Add(Group("See-through", see));

        Describe(status, "ToolbarAppearanceStatus", AutomationLiveSetting.Polite); status.Margin = new Thickness(2, 12, 0, 0); Children.Add(status);
        Describe(error, "ToolbarAppearanceError", AutomationLiveSetting.Assertive); error.Margin = new Thickness(2, 8, 0, 0); Children.Add(error);
        var scope = Ui.Text("Applies to the screenshot mode controls, recording setup and the controls shown while recording.", 12, Ui.Muted);
        scope.Margin = new Thickness(2, 12, 0, 0); AutomationProperties.SetAutomationId(scope, "ToolbarAppearanceScope"); Children.Add(scope);

        pause = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = TimeSpan.FromMilliseconds(400) };
        pause.Tick += (_, _) => Save();
        settings.Changed += Changed; Appearance.Current.Changed += Sync;
        Sync();
    }

    // Saves an opacity still being dragged or waiting for its pause, then stops listening; used when the Settings window closes.
    public void Detach()
    {
        if (!attached) return;
        if (dragging || pause.IsEnabled) { dragging = false; Save(); }
        attached = false; pause.Stop(); settings.Changed -= Changed; Appearance.Current.Changed -= Sync;
    }

    private void Picked() { if (!syncing) Save(); }

    private void Moved()
    {
        if (syncing) return;
        value.Text = Percent(opacity.Value);
        // A drag is saved when it ends; arrow keys and track clicks after a short pause.
        pause.Stop(); if (!dragging) pause.Start();
    }

    // Saves what the controls show. A rejected value keeps the saved options, so the controls return to them with the error.
    private void Save()
    {
        pause.Stop();
        if (!attached) return;
        var material = frosted.IsChecked == true ? ToolbarMaterial.Frosted : ToolbarMaterial.Tinted;
        int percent = (int)Math.Round(opacity.Value);
        var saved = settings.Options;
        if (saved.Material == material && saved.Opacity == percent && settings.Notice is null) { problem = null; Sync(); return; }
        try { settings.Update(material, percent); problem = null; }
        catch (Exception ex) { problem = ex.Message; }
        Sync();
        Announce(problem is null ? status : error);
    }

    private void Changed()
    {
        if (!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(Changed)); return; }
        if (attached) Sync();
    }

    private void Sync()
    {
        var saved = settings.Options;
        syncing = true;
        try
        {
            tinted.IsChecked = saved.Material == ToolbarMaterial.Tinted; frosted.IsChecked = saved.Material == ToolbarMaterial.Frosted;
            // An opacity still being dragged or waiting to save isn't pulled back to the saved value.
            if (!dragging && !pause.IsEnabled) opacity.Value = saved.Opacity;
        }
        finally { syncing = false; }
        value.Text = Percent(opacity.Value);
        Show(capability, settings.CapabilityMessage);
        Show(notice, settings.Notice); noticeCard.Visibility = notice.Visibility;
        // Describes the options in use, which a missing or recovered file may not have saved yet; whether Windows draws the
        // frosted effect is the capability line's to say.
        Show(status, $"Current:{(saved.Material == ToolbarMaterial.Frosted ? "Frosted glass" : "Tinted surface")}, {saved.Opacity}% background opacity.");
        Show(error, problem); error.Foreground = UpdateNotice.Error;
    }

    private void Announce(TextBlock line)
    {
        if (IsLoaded && line.Visibility == Visibility.Visible) UIElementAutomationPeer.CreatePeerForElement(line)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private static string Percent(double percent) => $"{Math.Round(percent):0}%";

    private static RadioButton Choice(string label, string id, string help, Panel host)
    {
        var choice = new RadioButton { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, FontSize = 14, GroupName = "ToolbarMaterial" };
        AutomationProperties.SetAutomationId(choice, id); AutomationProperties.SetName(choice, label); AutomationProperties.SetHelpText(choice, help);
        var hint = Ui.Text(help, 12, Ui.Muted); hint.Margin = new Thickness(28, 0, 0, 0);
        host.Children.Add(choice); host.Children.Add(hint); return choice;
    }
}
