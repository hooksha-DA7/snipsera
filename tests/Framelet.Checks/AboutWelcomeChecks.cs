using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Framelet;

// About page, help links, first-run welcome and screenshot "Show in folder". Detached windows and synthetic profiles only:
// no browser or File Explorer is opened, and no native window is shown.
internal static class AboutWelcomeChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static FrameworkElement Layout(FrameworkElement root, double width, double height)
    { root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); return root; }
    // Off-screen render for visual review; written next to results.json.
    private static void Snapshot(FrameworkElement root, string path)
    {
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth, (int)root.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        var backdrop = new System.Windows.Shapes.Rectangle { Width = root.ActualWidth, Height = root.ActualHeight, Fill = Ui.Brush("#F7F9FB") };
        backdrop.Measure(new Size(root.ActualWidth, root.ActualHeight)); backdrop.Arrange(new Rect(0, 0, root.ActualWidth, root.ActualHeight));
        bitmap.Render(backdrop); bitmap.Render(root);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static T? Find<T>(DependencyObject root, string id) where T : DependencyObject =>
        WorkflowChecks.Children(root).OfType<T>().FirstOrDefault(element => AutomationProperties.GetAutomationId(element) == id);

    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var outcomes = new List<object>(); int failures = 0;
        string Folder() { string folder = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }

        Check("Help links are fixed HTTPS pages on snipsera.com or the project repository", () =>
        {
            string[] links = [WebLinks.Website, WebLinks.ReleaseNotes, WebLinks.Support, WebLinks.Privacy, WebLinks.Source, WebLinks.License];
            foreach (string link in links)
            {
                var uri = new Uri(link);
                Require(uri.Scheme == "https" && (uri.Host == "snipsera.com" || link.StartsWith("https://github.com/hooksha-DA7/snipsera", StringComparison.Ordinal)), "Unexpected link " + link);
                Require(WebLinks.IsKnown(link), "Link not allowed " + link);
            }
            Require(!WebLinks.IsKnown("https://example.com/") && !WebLinks.IsKnown("file:///C:/Windows/notepad.exe"), "Unknown link allowed");
            bool refused = false; try { WebLinks.Open("https://example.com/"); } catch (ArgumentException) { refused = true; }
            Require(refused, "Unknown link was not refused before opening");
        });

        Check("Welcome shows once on a fresh profile and stays dismissed after restart", () =>
        {
            string folder = Folder(); var state = new WelcomeState(folder, false);
            Require(state.ShouldShow && !Directory.EnumerateFiles(folder).Any(), "Fresh profile did not show welcome or wrote data early");
            int events = 0; state.Changed += () => events++;
            state.Dismiss(); state.Dismiss();
            Require(!state.ShouldShow && events == 1 && File.Exists(Path.Combine(folder, "welcome.json")), "Dismiss did not persist once");
            Require(!new WelcomeState(folder, false).ShouldShow, "Welcome returned after restart");
        });

        Check("Upgrades with existing captures never see the welcome", () =>
        {
            string folder = Folder();
            Require(!new WelcomeState(folder, true).ShouldShow, "Existing install saw welcome");
            Require(!new WelcomeState(folder, false).ShouldShow, "Welcome appeared after History was emptied");
        });

        Check("Unreadable welcome state hides the card without replacing the file", () =>
        {
            foreach (string contents in new[] { "{broken", "null", new string('x', 5000) })
            {
                string folder = Folder(), path = Path.Combine(folder, "welcome.json"); File.WriteAllText(path, contents);
                Require(!new WelcomeState(folder, false).ShouldShow && File.ReadAllText(path) == contents, "Corrupt welcome state was shown or replaced");
            }
        });

        var app = new CaptureApp(Path.Combine(output, "profile"));
        try
        {
            Check("Home shows the welcome with the saved area shortcut and Got it dismisses it", () =>
            {
                Require(app.Welcome.ShouldShow, "Fresh check profile should show welcome");
                var home = new HomeWindow(app);
                try
                {
                    var root = Layout((FrameworkElement)home.Content, 1100, 760);
                    var card = Find<Border>(root, "WelcomeCard"); var shortcut = Find<TextBlock>(root, "WelcomeShortcut");
                    Require(card is not null && card.Visibility == Visibility.Visible, "Welcome card missing");
                    Require(shortcut is not null && shortcut.Text.Contains(app.Settings.Bindings[0]), "Welcome does not show the area shortcut");
                    Require(Find<Button>(root, "WelcomeSetUpPrintScreen") is not null, "Print Screen setup button missing");
                    Snapshot(root, Path.Combine(output, "home-welcome.png"));
                    Find<Button>(root, "WelcomeDismiss")!.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Require(card!.Visibility == Visibility.Collapsed && !app.Welcome.ShouldShow, "Got it did not dismiss");
                    Require(!home.IsVisible && new WindowInteropHelper(home).Handle == IntPtr.Zero, "Check opened a native window");
                }
                finally { home.Close(); }
                var again = new HomeWindow(app);
                try { Require(Find<Border>(Layout((FrameworkElement)again.Content, 1100, 760), "WelcomeCard") is null, "Dismissed welcome came back"); }
                finally { again.Close(); }
            });

            Check("Settings About shows the version and every help link", () =>
            {
                var settings = new PreferencesWindow(app.Preferences, app);
                try
                {
                    var root = Layout((FrameworkElement)settings.Content, 780, 620);
                    settings.ShowPage(SettingsPage.About); root.UpdateLayout();
                    Require(settings.Current == SettingsPage.About && Find<ListBoxItem>(root, "SettingsAbout") is not null, "About section missing");
                    var version = Find<TextBlock>(root, "AboutVersion");
                    Require(version is not null && version.Text.Contains(app.Preferences.InstalledDisplayVersion), "About does not show the version");
                    foreach (string id in new[] { "OpenWebsite", "OpenWhatsNew", "OpenSupport", "OpenPrivacy", "OpenSource", "OpenLicense" })
                        Require(Find<Button>(root, id) is not null, "Missing link button " + id);
                    Snapshot(root, Path.Combine(output, "settings-about.png"));
                    Require((int)SettingsPage.Appearance == 4 && (int)SettingsPage.About == 5, "Existing page order changed");
                }
                finally { settings.Close(); }
            });

            Check("Screenshot previews offer Show in folder without changing on-image controls", () =>
            {
                var image = UiChecks.White(); var entry = app.History.Add(image, CaptureKind.Area);
                var card = new ImageWindow(app, image, entry, false);
                try
                {
                    var root = Layout((FrameworkElement)card.Content, 288, 180);
                    var headers = root.ContextMenu!.Items.OfType<MenuItem>().Select(item => item.Header as string).ToArray();
                    Require(headers.Count(header => header == "Show in folder") == 1, "Show in folder missing or duplicated");
                    Require(Array.IndexOf(headers, "Show in folder") > Array.IndexOf(headers, "Annotate"), "Show in folder moved ahead of the capture actions");
                    var buttons = WorkflowChecks.Children(root).OfType<Button>().Select(b => AutomationProperties.GetAutomationId(b)).ToArray();
                    Require(buttons.Contains("CopyCapture") && buttons.Contains("SaveCapture") && buttons.Contains("PinCapture"), "On-image controls changed");
                    Require(app.Storage.ImagePath(entry).StartsWith(app.Storage.ImageFolder, StringComparison.OrdinalIgnoreCase), "Screenshot path is outside the screenshot folder");
                }
                finally { card.Close(); }
            });
        }
        finally
        {
            File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
            app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown();
        }
        return failures == 0 ? 0 : 1;
    }
}
