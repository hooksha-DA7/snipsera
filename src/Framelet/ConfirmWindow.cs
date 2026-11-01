using System.Windows;
using System.Windows.Controls;

namespace Framelet;

public enum Confirmation { Cancel, Primary, Secondary }

public sealed class ConfirmWindow : Window
{
    public Confirmation Choice { get; private set; }
    public ConfirmWindow(Window? owner, string title, string message, string primary, string? secondary = null)
    {
        Ui.Setup(this); Ui.UseSystemChrome(this); Title = title; Width = 480; SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        if (owner?.IsVisible == true) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var root = new StackPanel();
        var body = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };
        body.Children.Add(Ui.Text(title, 20, Ui.Ink, true));
        var detail = Ui.Text(message, 14, Ui.Muted); detail.Margin = new Thickness(0, 14, 0, 0); body.Children.Add(detail); root.Children.Add(body);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 16, 18, 16) };
        void Choose(Confirmation value) { Choice = value; Close(); }
        if (secondary is not null) buttons.Children.Add(Ui.Button(secondary, "DialogSecondary", () => Choose(Confirmation.Secondary)));
        var accept = Ui.Button(primary, "DialogPrimary", () => Choose(Confirmation.Primary), true); accept.IsDefault = true;
        buttons.Children.Add(accept);
        var cancel = Ui.Button("Cancel", "DialogCancel", () => Choose(Confirmation.Cancel)); cancel.IsCancel = true; buttons.Children.Add(cancel);
        root.Children.Add(new Border { Background = Ui.Chrome, BorderBrush = Ui.Line, BorderThickness = new Thickness(0, 1, 0, 0), Child = buttons }); Content = root;
    }
    public static Confirmation Ask(Window? owner, string title, string message, string primary, string? secondary = null)
    { var dialog = new ConfirmWindow(owner, title, message, primary, secondary); dialog.ShowDialog(); return dialog.Choice; }
}
