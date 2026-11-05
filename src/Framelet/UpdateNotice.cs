using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Framelet;

// A quiet, durable Home banner for an available update. It only opens Settings; nothing downloads or installs from here.
public sealed class UpdateNotice : Border
{
    private readonly AppPreferencesController controller;
    private readonly TextBlock title = Ui.Text("", 13, Ui.Ink, true), detail = Ui.Text("", 12, Ui.Muted);
    private readonly Button action;

    public UpdateNotice(AppPreferencesController controller, Action open)
    {
        this.controller = controller;
        Background = Ui.Surface; BorderBrush = Ui.Blue; BorderThickness = new Thickness(1); CornerRadius = new CornerRadius(8);
        Padding = new Thickness(14, 10, 10, 10); Margin = new Thickness(0, 0, 0, 14);
        AutomationProperties.SetAutomationId(this, "HomeUpdateNotice"); AutomationProperties.SetLiveSetting(title, AutomationLiveSetting.Polite);
        AutomationProperties.SetAutomationId(title, "HomeUpdateTitle");
        action = Ui.Button("View update", "HomeViewUpdate", open); action.VerticalAlignment = VerticalAlignment.Center; action.Margin = new Thickness(12, 0, 0, 0);
        var icon = new UiIcon("Save", 20) { Ink = Ui.Blue, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        detail.Margin = new Thickness(0, 2, 0, 0);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(title); text.Children.Add(detail);
        var row = new DockPanel(); DockPanel.SetDock(icon, Dock.Left); DockPanel.SetDock(action, Dock.Right);
        row.Children.Add(icon); row.Children.Add(action); row.Children.Add(text); Child = row;
        Refresh();
    }

    public void Refresh()
    {
        if (controller.Update.Offer is not { } offer) { Visibility = Visibility.Collapsed; return; }
        Visibility = Visibility.Visible; Heading(title, offer);
        detail.Text = controller.IsStoreInstall ? "Review it in Settings and install it from Microsoft Store whenever it suits you." : "See what's new and download it from GitHub whenever it suits you.";
        AutomationProperties.SetName(action, FromStore(offer) ? "View update for Snipsera" : $"View update for Snipsera {offer.Version}");
        AutomationProperties.SetName(this, Title(offer) + (offer.IsPreview ? ", preview" : ""));
    }

    // Microsoft Store reports that an update exists, not which version it is, so Store offers never show offer.Version.
    public static bool FromStore(UpdateOffer offer) => offer.Source == "Microsoft Store";
    public static string Title(UpdateOffer offer) => FromStore(offer) ? "Snipsera update available" : $"Snipsera {offer.Version} is available";
    public static string ActionName(UpdateOffer offer, bool store) => FromStore(offer) ? $"{ActionLabel(store)} to update Snipsera" : $"{ActionLabel(store)} for Snipsera {offer.Version}";
    public static string ActionLabel(bool store) => store ? "Open Microsoft Store" : "View GitHub release";
    public static Brush Error => SystemParameters.HighContrast ? Ui.Ink : Ui.Brush(Appearance.Current.Dark ? "#FF99A4" : "#C42B1C");

    // The Preview label sits inline with the title so long versions wrap instead of clipping.
    public static void Heading(TextBlock target, UpdateOffer offer)
    {
        target.Inlines.Clear(); target.Inlines.Add(new Run(Title(offer)));
        if (!offer.IsPreview) return;
        var label = Ui.Text("Preview", 11, Ui.Blue); label.TextWrapping = TextWrapping.NoWrap;
        var badge = new Border { BorderBrush = Ui.Blue, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0, 6, 1), Margin = new Thickness(8, 0, 0, 0), Child = label };
        AutomationProperties.SetAutomationId(badge, "UpdatePreviewLabel");
        target.Inlines.Add(new InlineUIContainer(badge) { BaselineAlignment = BaselineAlignment.Center });
    }

    public static string Checked(DateTimeOffset? at)
    {
        if (at is not { } value) return "";
        var local = value.ToLocalTime(); int days = (DateTime.Today - local.Date).Days;
        return days == 0 ? $"Last checked today at {local:t}" : days == 1 ? $"Last checked yesterday at {local:t}" : $"Last checked {local:d}";
    }
}
