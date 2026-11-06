using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using static Framelet.SettingsUi;

namespace Framelet;

public enum SettingsPage { General, SaveLocations, KeyboardShortcuts, Updates }

// Settings, in separate sections like Windows Settings: a section list beside the selected page, or above it in a narrow
// window. Changes apply at once, except shortcuts, which are saved together. Building the window only reads state; Windows
// startup state is refreshed when the window is really activated. Without a CaptureApp (isolated checks), the sections that
// need the running app explain that instead of offering controls.
public sealed class PreferencesWindow : Window
{
    // Below this content width the section list moves above the page.
    private const double CompactBelow = 640;
    private static readonly string[] PageNames = ["General", "Save locations", "Keyboard shortcuts", "Updates"];
    private static readonly string[] PageIds = ["SettingsGeneral", "SettingsSaveLocations", "SettingsKeyboardShortcuts", "SettingsUpdates"];
    private static readonly string[] PageGlyphs =
    [
        "M4 6 L20 6 M4 12 L20 12 M4 18 L20 18 M8 3 L8 9 M16 9 L16 15 M10 15 L10 21",
        "M3 7 L3 20 L19 20 L22 10 L8 10 L5 20 M3 7 L9 7 L11 4 L20 4 L20 8",
        "M3 6 L21 6 L21 18 L3 18 Z M7 10 L7.1 10 M11 10 L11.1 10 M15 10 L15.1 10 M7 14 L17 14",
        "M20 12 A8 8 0 1 1 17.7 6.3 M20 3 L20 8 L15 8"
    ];
    private readonly AppPreferencesController controller;
    private readonly ShortcutSettingsPanel? shortcutPanel;
    private readonly StorageSettingsPanel? storagePanel;
    private readonly CheckBox startup, automatic;
    private readonly CheckBox? previews;
    private readonly TextBlock startupStatus = Ui.Text("", 12, Ui.Muted), startupError = Ui.Text("", 12),
        updateStatus = Ui.Text("", 13), updateChecked = Ui.Text("", 12, Ui.Muted), updateError = Ui.Text("", 12),
        offerTitle = Ui.Text("", 14, Ui.Ink, true), offerDetail = Ui.Text("", 12, Ui.Muted);
    private readonly Button startupSettings, check, offerAction;
    private readonly Border offer;
    private readonly Grid layout = new();
    private readonly StackPanel navigationHost = new();
    private readonly ListBox navigation = new();
    private readonly ScrollViewer pageHost;
    private readonly FrameworkElement[] pages = new FrameworkElement[PageNames.Length];
    private bool startupBusy, startupWanted, checkBusy, refreshing;
    private bool? compact;
    private string? startupProblem, updateProblem;

    public PreferencesWindow(AppPreferencesController controller, CaptureApp? app = null)
    {
        this.controller = controller; Ui.Setup(this); Ui.UseSystemChrome(this);
        Title = "Snipsera — settings"; Width = 780; MinWidth = 400; MinHeight = 360;
        Height = Math.Min(640, SystemParameters.WorkArea.Height * .9); WindowStartupLocation = WindowStartupLocation.CenterScreen;

        // General: the checkbox mirrors the Windows state; Windows or policy can keep it off.
        var general = new StackPanel();
        startup = Box("Start with Windows", "StartWithWindows", "Opens quietly in the notification area when you sign in.", general);
        startup.Click += (_, _) => ToggleStartup();
        startupStatus.Margin = new Thickness(28, 6, 0, 0); Describe(startupStatus, "StartupStatus", AutomationLiveSetting.Polite); general.Children.Add(startupStatus);
        startupSettings = Ui.Button("Open Windows startup settings", "OpenStartupSettings", () => Run(controller.OpenStartupSettings, true));
        startupSettings.HorizontalAlignment = HorizontalAlignment.Left; startupSettings.Margin = new Thickness(28, 10, 0, 0); general.Children.Add(startupSettings);
        startupError.Margin = new Thickness(28, 8, 0, 0); Describe(startupError, "StartupError", AutomationLiveSetting.Assertive); general.Children.Add(startupError);
        pages[0] = Page(SettingsPage.General, "How Snipsera starts.", Group("Startup", general));

        // Save locations and shortcuts belong to the running app.
        if (app is not null) storagePanel = new StorageSettingsPanel(app.Storage);
        pages[1] = Page(SettingsPage.SaveLocations, "Choose where Snipsera saves screenshots and recordings.",
            storagePanel ?? Unavailable("Save locations can be changed from Settings in the running Snipsera app."));
        if (app is not null) shortcutPanel = new ShortcutSettingsPanel(app);
        pages[2] = Page(SettingsPage.KeyboardShortcuts, "Start a capture from anywhere in Windows.",
            shortcutPanel ?? Unavailable("Keyboard shortcuts can be changed from Settings in the running Snipsera app."));

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
        var privacy = Ui.Text($"Update checks contact {controller.UpdateSourceName} for version information only. Your captures and recordings aren't sent.", 12, Ui.Muted);
        privacy.Margin = new Thickness(2, 10, 0, 0);
        pages[3] = Page(SettingsPage.Updates, "Keep Snipsera up to date.", Group("Update checks", updates), privacy);

        // Every page stays in the tree and only the selected one is shown, so each keeps its state while another is open.
        var stack = new Grid { Margin = new Thickness(24, 20, 24, 24) };
        foreach (var page in pages) stack.Children.Add(page);
        pageHost = new ScrollViewer { Content = stack, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
        AutomationProperties.SetAutomationId(pageHost, "SettingsPage");

        var heading = Ui.Text("Settings", 14, Ui.Ink, true); heading.Margin = new Thickness(12, 0, 0, 8);
        navigationHost.Children.Add(heading); navigationHost.Children.Add(navigation);
        AutomationProperties.SetAutomationId(navigation, "SettingsSections"); AutomationProperties.SetName(navigation, "Settings sections");
        navigation.BorderThickness = new Thickness(0); navigation.Background = System.Windows.Media.Brushes.Transparent;
        ScrollViewer.SetHorizontalScrollBarVisibility(navigation, ScrollBarVisibility.Disabled);
        for (int i = 0; i < PageNames.Length; i++) navigation.Items.Add(Entry(i));
        navigation.SelectionChanged += (_, _) => Select();
        navigation.SelectedIndex = 0;

        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); layout.RowDefinitions.Add(new RowDefinition());
        layout.Children.Add(navigationHost); layout.Children.Add(pageHost);
        layout.SizeChanged += (_, e) => Arrange(e.NewSize.Width);
        Arrange(Width);
        Content = layout;

        KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        Loaded += (_, _) => { if (navigation.SelectedIndex == 0) startup.Focus(); else (navigation.ItemContainerGenerator.ContainerFromIndex(navigation.SelectedIndex) as UIElement)?.Focus(); };
        Activated += async (_, _) =>
        {
            // WPF restores keyboard focus after Activated and may not raise a new focus event, so check once input has settled.
            if (shortcutPanel is not null) _ = Dispatcher.InvokeAsync(() => { if (IsActive && Current == SettingsPage.KeyboardShortcuts) shortcutPanel.Resume(); }, DispatcherPriority.Input);
            if (refreshing || startupBusy) return;
            refreshing = true;
            try { await controller.RefreshStartupAsync(); }
            catch (Exception ex) { startupProblem = ex.Message; }
            finally { refreshing = false; Sync(); }
        };
        // A hidden, inactive or closed Settings window never keeps the capture shortcuts suspended.
        IsVisibleChanged += (_, _) => { if (!IsVisible) shortcutPanel?.Release(); };
        Deactivated += (_, _) => shortcutPanel?.Release();
        // High contrast swaps the section-list template; theme colors already follow the bound tokens.
        void Restyle() { foreach (ListBoxItem item in navigation.Items) item.Style = SectionEntry(); }
        controller.Changed += Sync; Appearance.Current.Changed += Sync; Appearance.Current.Changed += Restyle;
        Closed += (_, _) => { controller.Changed -= Sync; Appearance.Current.Changed -= Sync; Appearance.Current.Changed -= Restyle; shortcutPanel?.Detach(); storagePanel?.Detach(); };
        Sync();
    }

    public SettingsPage Current => (SettingsPage)Math.Max(0, navigation.SelectedIndex);

    // Opens a section, for example from Home's shortcut link.
    public void ShowPage(SettingsPage page) => navigation.SelectedIndex = (int)page;

    private void Select()
    {
        if (navigation.SelectedIndex < 0) { navigation.SelectedIndex = 0; return; }
        // Leaving the shortcuts page restores the capture shortcuts even if a box kept logical focus.
        if (Current != SettingsPage.KeyboardShortcuts) shortcutPanel?.Release();
        for (int i = 0; i < pages.Length; i++) pages[i].Visibility = i == navigation.SelectedIndex ? Visibility.Visible : Visibility.Collapsed;
        pageHost.ScrollToTop();
    }

    // Wide: the section list beside the page. Narrow: the list wraps above it, so nothing is clipped or hidden.
    private void Arrange(double width)
    {
        bool narrow = width < CompactBelow;
        if (compact == narrow) return;
        compact = narrow;
        Grid.SetRow(navigationHost, 0); Grid.SetColumn(navigationHost, 0); Grid.SetColumnSpan(navigationHost, narrow ? 2 : 1); Grid.SetRowSpan(navigationHost, narrow ? 1 : 2);
        Grid.SetRow(pageHost, narrow ? 1 : 0); Grid.SetColumn(pageHost, narrow ? 0 : 1); Grid.SetColumnSpan(pageHost, narrow ? 2 : 1); Grid.SetRowSpan(pageHost, narrow ? 1 : 2);
        navigationHost.Width = narrow ? double.NaN : 220;
        navigationHost.Margin = narrow ? new Thickness(12, 12, 12, 0) : new Thickness(12, 20, 0, 12);
        var panel = new FrameworkElementFactory(narrow ? typeof(WrapPanel) : typeof(StackPanel));
        navigation.ItemsPanel = new ItemsPanelTemplate(panel);
    }

    private ListBoxItem Entry(int index)
    {
        var glyph = Glyph(PageGlyphs[index]); glyph.Margin = new Thickness(0, 0, 12, 0); glyph.VerticalAlignment = VerticalAlignment.Center;
        var label = new TextBlock { Text = PageNames[index], FontSize = 14, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        var row = new DockPanel(); DockPanel.SetDock(glyph, Dock.Left); row.Children.Add(glyph); row.Children.Add(label);
        var item = new ListBoxItem { Content = row, MinHeight = 36, Padding = new Thickness(10, 6, 12, 6), Style = SectionEntry() };
        AutomationProperties.SetAutomationId(item, PageIds[index]); AutomationProperties.SetName(item, PageNames[index]);
        return item;
    }

    private static FrameworkElement Page(SettingsPage page, string summary, params UIElement[] content)
    {
        var body = new StackPanel();
        body.Children.Add(PageTitle(PageNames[(int)page]));
        var intro = Ui.Text(summary, 13, Ui.Muted); intro.Margin = new Thickness(2, 0, 0, 0); body.Children.Add(intro);
        foreach (var part in content) body.Children.Add(part);
        AutomationProperties.SetAutomationId(body, PageIds[(int)page] + "Page"); AutomationProperties.SetName(body, PageNames[(int)page]);
        return body;
    }

    private static UIElement Unavailable(string message)
    {
        var text = Ui.Text(message, 13, Ui.Muted); AutomationProperties.SetAutomationId(text, "SectionUnavailable");
        var card = Ui.Card(text); card.Margin = new Thickness(0, 16, 0, 0); return card;
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
        SettingsUi.Show(startupStatus, startupBusy ? "Updating…" : reading ? "Reading Windows startup status…" : string.IsNullOrWhiteSpace(s.Message) ? fallback : s.Message);
        startupSettings.Visibility = s.ManageInWindows ? Visibility.Visible : Visibility.Collapsed;
        SettingsUi.Show(startupError, startupProblem); startupError.Foreground = UpdateNotice.Error;

        automatic.IsChecked = p.AutomaticChecks;
        if (previews is not null) previews.IsChecked = p.IncludePreviews;
        bool checking = checkBusy || u.Checking;
        check.Content = checking ? "Checking…" : "Check now";
        AutomationProperties.SetItemStatus(check, checking ? "Checking" : "");
        string status = checking ? "Checking for updates…" : !string.IsNullOrWhiteSpace(u.Message) ? u.Message
            : u.Offer is null && u.CheckedAt is not null ? "Snipsera is up to date." : u.CheckedAt is null ? "Not checked yet." : "";
        SettingsUi.Show(updateStatus, status); SettingsUi.Show(updateChecked, checking ? "" : UpdateNotice.Checked(u.CheckedAt));
        if (u.Offer is { } available)
        {
            UpdateNotice.Heading(offerTitle, available); offer.Visibility = Visibility.Visible;
            AutomationProperties.SetName(offer, UpdateNotice.Title(available) + (available.IsPreview ? ", preview" : ""));
            AutomationProperties.SetName(offerAction, UpdateNotice.ActionName(available, controller.IsStoreInstall));
        }
        else offer.Visibility = Visibility.Collapsed;
        SettingsUi.Show(updateError, updateProblem); updateError.Foreground = UpdateNotice.Error;
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
}
