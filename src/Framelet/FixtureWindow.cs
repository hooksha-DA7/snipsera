using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Framelet;

public sealed class FixtureWindow : Window
{
    public FixtureWindow()
    {
        Title = "Framelet Capture Test"; Width = 660; Height = 460; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = Ui.Brush("#205FDB");
        var grid = new Grid { Margin = new Thickness(32) };
        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        var heading = Ui.Text("CAPTURE TEST", 32, Brushes.White, true); heading.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(heading);
        var stripe = new Border { Background = Ui.Brush("#FFD561"), CornerRadius = new CornerRadius(12), Padding = new Thickness(20) };
        var content = new StackPanel(); content.Children.Add(Ui.Text("Only synthetic content", 22, Ui.Ink, true));
        content.Children.Add(Ui.Text("A blue and yellow test card for pixel, clipboard and export checks.", 14));
        content.Children.Add(Ui.Button("Close test window", "CloseFixture", Close)); stripe.Child = content;
        Grid.SetRow(stripe, 1); grid.Children.Add(stripe); Content = grid;
    }
}
