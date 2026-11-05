using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;

namespace Framelet;

// Startup and update settings. Changes apply at once, like Windows Settings. Building the window only reads state;
// Windows startup state is refreshed when the window is really activated.
public sealed class PreferencesWindow : Window
{
    private readonly AppPreferencesController controller;
    private readonly CheckBox startup, automatic;
    private readonly CheckBox? previews;
    private readonly TextBlock startupStatus = Ui.Text("", 12, Ui.Muted), startupError = Ui.Text("", 12),
        updateStatus = Ui.Text("", 13), updateChecked = Ui.Text("", 12, Ui.Muted), updateError = Ui.Text("", 12),
        offerTitle = Ui.Text("", 14, Ui.Ink, true), offerDetail = Ui.Text("", 12, Ui.Muted);
    private readonly Button startupSettings, check, offerAction;
    private readonly Border offer;
    private bool startupBusy, startupWanted, checkBusy, refreshing;
    private string? startupProblem, updateProblem;

    public PreferencesWindow(AppPreferencesController controller)
    {
        this.controller = controller; Ui.Setup(this); Ui.UseSystemChrome(this);
        Title = "Snipsera — settings"; Width = 560; MinWidth = 420; MinHeight = 320; SizeToContent = SizeToContent.Height;
        MaxHeight = SystemParameters.WorkArea.Height * .9; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        body.Children.Add(Ui.Text("Settings", 20, Ui.Ink, true));

        // General: the checkbox mirrors the Windows state; Windows or policy can keep it off.
        var general = new StackPanel();
        startup = Box("Start with Windows", "StartWithWindows", "Opens quietly in the notification area when you sign in.", general);
        startup.Click += (_, _) => ToggleStartup();
        startupStatus.Margin = new Thickness(28, 6, 0, 0); Describe(startupStatus, "StartupStatus", AutomationLiveSetting.Polite); general.Children.Add(startupStatus);
        startupSettings = Ui.Button("Open Windows startup settings", "OpenStartupSettings", () => Run(controller.OpenStartupSettings, true));
        startupSettings.HorizontalAlignment = HorizontalAlignment.Left; startupSettings.Margin = new Thickness(28, 10, 0, 0); general.Children.Add(startupSettings);
        startupError.Margin = new Thickness(28, 8, 0, 0); Describe(startupError, "StartupError", AutomationLiveSetting.Assertive); general.Children.Add(startupError);
        body.Children.Add(Section("General", general));

        // Updates: checks only report availability; the action opens the Store or release page for the person to decide.
        var updates = new StackPanel();
        var installed = Ui.Text($"Snipsera {controller.InstalledVersion} · {controller.UpdateSourceName}", 13); Describe(installed, "InstalledVersion", AutomationLiveSetting.Off);
        updates.Children.Add(installed); updates.Children.Add(Rule());
        automatic = Box("Automatically check for updates", "AutomaticUpdateChecks", "Checks once a day, retries hourly after a failed check, and lets you know when an update is ready.", updates);
        automatic.Click += (_, _) => SavePreferences();
        if (!controller.IsStoreInstall)
        {
            var spacer = new Border { Height = 12 }; updates.Children.Add(spacer);
            previews = Box("Include preview releases", "IncludePreviewReleases", "Preview builds arrive sooner and may change before the full release.", updates);
            previews.Click += (_, _) => SavePreferences();
        }
        updates.Children.Add(Rule());
        check = Ui.Button("Check now", "CheckForUpdates", Check); check.Margin = new Thickness(12, 0, 0, 0); check.VerticalAlignment = VerticalAlignment.Top;
        AutomationProperties.SetName(check, "Check for updates now");
        var result = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        Describe(updateStatus, "UpdateStatus", AutomationLiveSetting.Polite); Describe(updateChecked, "UpdateLastChecked", AutomationLiveSetting.Off);
        updateChecked.Margin = new Thickness(0, 2, 0, 0); result.Children.Add(updateStatus); result.Children.Add(updateChecked);
        var checkRow = new DockPanel(); DockPanel.SetDock(check, Dock.Right); checkRow.Children.Add(check); checkRow.Children.Add(result);
        updates.Children.Add(checkRow);
        offerAction = Ui.Button(UpdateNotice.ActionLabel(controller.IsStoreInstall), "OpenUpdate", () => Run(controller.OpenUpdate, false), true);
        offerAction.HorizontalAlignment = HorizontalAlignment.Left; offerAction.Margin = new Thickness(0, 10, 0, 0);
        Describe(offerTitle, "UpdateOfferTitle", AutomationLiveSetting.Polite); offerDetail.Margin = new Thickness(0, 4, 0, 0);
        offerDetail.Text = controller.IsStoreInstall ? "Microsoft Store downloads and installs the update."
            : "Opens the release page in your browser. Download and run the installer when you're ready.";
        var offerBody = new StackPanel(); offerBody.Children.Add(offerTitle); offerBody.Children.Add(offerDetail); offerBody.Children.Add(offerAction);
        offer = new Border { BorderBrush = Ui.Blue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(12), Margin = new Thickness(0, 12, 0, 0), Child = offerBody };
        AutomationProperties.SetAutomationId(offer, "UpdateOffer"); updates.Children.Add(offer);
        updateError.Margin = new Thickness(0, 8, 0, 0); Describe(updateError, "UpdateError", AutomationLiveSetting.Assertive); updates.Children.Add(updateError);
        body.Children.Add(Section("Updates", updates));
        var privacy = Ui.Text($"Update checks contact {controller.UpdateSourceName} for version information only. Your captures and recordings aren't sent.", 12, Ui.Muted);
        privacy.Margin = new Thickness(2, 10, 0, 0); body.Children.Add(privacy);

        Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Focusable = false };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += (_, _) => startup.Focus();
        Activated += async (_, _) =>
        {
            if (refreshing || startupBusy) return;
            refreshing = true;
            try { await controller.RefreshStartupAsync(); }
            catch (Exception ex) { startupProblem = ex.Message; }
            finally { refreshing = false; Sync(); }
        };
        controller.Changed += Sync; Appearance.Current.Changed += Sync;
        Closed += (_, _) => { controller.Changed -= Sync; Appearance.Current.Changed -= Sync; };
        Sync();
    }

    // Controls always show the controller's state; a busy control keeps focus and ignores repeat input instead of being disabled.
    private void Sync()
    {
        var s = controller.Startup; var p = controller.Preferences; var u = controller.Update;
        bool reading = controller.StartupBusy && !startupBusy;
        startup.IsChecked = startupBusy ? startupWanted : s.Enabled;
        startup.IsEnabled = s.CanChange || startupBusy;
        AutomationProperties.SetItemStatus(startup, startupBusy ? "Updating" : reading ? "Reading Windows startup status" : "");
        string fallback = s.Enabled ? "On" : s.CanChange ? "Off" : "Windows is managing this setting right now.";
        Show(startupStatus, startupBusy ? "Updating…" : reading ? "Reading Windows startup status…" : string.IsNullOrWhiteSpace(s.Message) ? fallback : s.Message);
        startupSettings.Visibility = s.ManageInWindows ? Visibility.Visible : Visibility.Collapsed;
        Show(startupError, startupProblem); startupError.Foreground = UpdateNotice.Error;

        automatic.IsChecked = p.AutomaticChecks;
        if (previews is not null) previews.IsChecked = p.IncludePreviews;
        bool checking = checkBusy || u.Checking;
        check.Content = checking ? "Checking…" : "Check now";
        AutomationProperties.SetItemStatus(check, checking ? "Checking" : "");
        string status = checking ? "Checking for updates…" : !string.IsNullOrWhiteSpace(u.Message) ? u.Message
            : u.Offer is null && u.CheckedAt is not null ? "Snipsera is up to date." : u.CheckedAt is null ? "Not checked yet." : "";
        Show(updateStatus, status); Show(updateChecked, checking ? "" : UpdateNotice.Checked(u.CheckedAt));
        if (u.Offer is { } available)
        {
            UpdateNotice.Heading(offerTitle, available); offer.Visibility = Visibility.Visible;
            AutomationProperties.SetName(offer, UpdateNotice.Title(available) + (available.IsPreview ? ", preview" : ""));
            AutomationProperties.SetName(offerAction, UpdateNotice.ActionName(available, controller.IsStoreInstall));
        }
        else offer.Visibility = Visibility.Collapsed;
        Show(updateError, updateProblem); updateError.Foreground = UpdateNotice.Error;
    }

    private async void ToggleStartup()
    {
        // While a write or a Windows read is in progress, undo the click and announce why nothing changed.
        if (startupBusy || controller.StartupBusy)
        {
            Sync();
            if (IsLoaded) UIElementAutomationPeer.CreatePeerForElement(startupStatus)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
            return;
        }
        startupWanted = startup.IsChecked == true;
        startupBusy = true; startupProblem = null; Sync();
        try { await controller.SetStartupAsync(startupWanted); }
        catch (Exception ex) { startupProblem = ex.Message; }
        finally { startupBusy = false; Sync(); }
    }

    private void SavePreferences()
    {
        try { controller.SetUpdatePreferences(automatic.IsChecked == true, previews?.IsChecked ?? controller.Preferences.IncludePreviews); updateProblem = null; }
        catch (Exception ex) { updateProblem = ex.Message; }
        finally { Sync(); }
    }

    private async void Check()
    {
        if (checkBusy || controller.Update.Checking) return;
        checkBusy = true; updateProblem = null; Sync();
        try { await controller.CheckUpdatesAsync(); }
        catch (Exception ex) { updateProblem = ex.Message; }
        finally { checkBusy = false; Sync(); }
    }

    private void Run(Action action, bool startupSection)
    {
        try { action(); if (startupSection) startupProblem = null; else updateProblem = null; }
        catch (Exception ex) { if (startupSection) startupProblem = ex.Message; else updateProblem = ex.Message; }
        finally { Sync(); }
    }

    private static CheckBox Box(string label, string id, string help, Panel host)
    {
        var box = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap }, FontSize = 14 };
        AutomationProperties.SetAutomationId(box, id); AutomationProperties.SetName(box, label); AutomationProperties.SetHelpText(box, help);
        var hint = Ui.Text(help, 12, Ui.Muted); hint.Margin = new Thickness(28, 0, 0, 0);
        host.Children.Add(box); host.Children.Add(hint); return box;
    }

    private static UIElement Section(string title, UIElement content)
    {
        var section = new StackPanel { Margin = new Thickness(0, 20, 0, 0) };
        var heading = Ui.Text(title, 14, Ui.Ink, true); heading.Margin = new Thickness(2, 0, 0, 8);
        AutomationProperties.SetHeadingLevel(heading, AutomationHeadingLevel.Level2);
        section.Children.Add(heading); section.Children.Add(Ui.Card(content));
        AutomationProperties.SetName(section, title); return section;
    }

    private static Border Rule() => new() { Height = 1, Background = Ui.Line, Margin = new Thickness(0, 12, 0, 12) };
    private static void Describe(TextBlock text, string id, AutomationLiveSetting live) { AutomationProperties.SetAutomationId(text, id); AutomationProperties.SetLiveSetting(text, live); }
    private static void Show(TextBlock text, string? value) { text.Text = value ?? ""; text.Visibility = string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible; }
}
