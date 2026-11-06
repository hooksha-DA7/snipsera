using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Framelet;
using static PreferencesChecks;

internal static class PreferencesVisualChecks
{
    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    private static bool Visible(DependencyObject node, DependencyObject root)
    {
        for (DependencyObject? current = node; current is not null; current = VisualTreeHelper.GetParent(current))
        { if (current is UIElement { Visibility: not Visibility.Visible }) return false; if (ReferenceEquals(current, root)) return true; }
        return false;
    }
    private static T Find<T>(DependencyObject root, string id) where T : DependencyObject => Children(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
    private static void Layout(FrameworkElement root)
    { root.Measure(new Size(544, double.PositiveInfinity)); root.Arrange(new Rect(0, 0, 544, root.DesiredSize.Height)); root.UpdateLayout(); }
    private static void Render(FrameworkElement root, double width, double scale, string path)
    {
        double height = 0;
        // The navigation moves above the page at compact widths; let its SizeChanged layout settle before checking bounds.
        for (int pass = 0; pass < 3; pass++)
        { root.Measure(new Size(width, double.PositiveInfinity)); height = root.DesiredSize.Height; root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout(); }
        var controls = Children(root).OfType<Control>().Where(c => c is Button or CheckBox && Visible(c, root)).ToArray();
        var bounds = controls.Select(c => c.TransformToAncestor(root).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight))).ToArray();
        for (int i = 0; i < controls.Length; i++)
        {
            var rect = bounds[i];
            Require(rect.Width > 0 && rect.Height > 0 && rect.Left >= -.5 && rect.Top >= -.5 && rect.Right <= width + .5 && rect.Bottom <= height + .5, "Settings control clipped: " + AutomationProperties.GetAutomationId(controls[i]));
            Require(!string.IsNullOrEmpty(AutomationProperties.GetName(controls[i])), "Settings action has no accessible name");
            for (int j = 0; j < i; j++)
            { var overlap = Rect.Intersect(rect, bounds[j]); Require(overlap.IsEmpty || overlap.Width <= .5 || overlap.Height <= .5, "Settings controls overlap"); }
        }
        Require(width <= 720 && height < 1400, "Settings are unexpectedly large");
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * scale), (int)Math.Ceiling(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        var scene = new DrawingVisual();
        using (var drawing = scene.RenderOpen())
        {
            drawing.DrawRectangle(Ui.Paper, null, new Rect(0, 0, width, height));
        }
        bitmap.Render(scene); bitmap.Render(root); CaptureService.Save(bitmap, path);
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown }; Ui.Install(app);
        System.Threading.SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
        var outcomes = new List<object>(); int failures = 0;
        void Test(string name, Action check)
        {
            try { check(); outcomes.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; outcomes.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        AppPreferencesFile Store() => new(Path.Combine(output, Guid.NewGuid().ToString("N")));
        Test("Settings actions persist update choices and toggle only the injected startup provider", () =>
        {
            var updates = new UpdateFake(); var startup = new StartupFake(); var file = Store();
            using var controller = new AppPreferencesController(file, updates, startup, "1.0.5", false); Wait(controller.RefreshStartupAsync());
            var window = new PreferencesWindow(controller);
            try
            {
                var root = (FrameworkElement)window.Content; window.Content = null; root.Measure(new Size(544, double.PositiveInfinity)); root.Arrange(new Rect(0, 0, 544, root.DesiredSize.Height)); root.UpdateLayout();
                var automatic = Find<CheckBox>(root, "AutomaticUpdateChecks"); automatic.IsChecked = false; automatic.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(!controller.Preferences.AutomaticChecks && !file.Load().AutomaticChecks && updates.Calls == 0, "Update option failed or contacted service");
                var start = Find<CheckBox>(root, "StartWithWindows"); start.IsChecked = true; start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(controller.Startup.Enabled && startup.Writes == 1, "Startup switch did not apply");
            }
            finally { window.Close(); }
        });
        Test("Reading startup status preserves the enabled control and ignores premature toggles", () =>
        {
            var startup = new StartupFake();
            using var controller = new AppPreferencesController(Store(), new UpdateFake(), startup, "1.0.5", false); Wait(controller.RefreshStartupAsync());
            var window = new PreferencesWindow(controller);
            try
            {
                var root = (FrameworkElement)window.Content; Layout(root); var toggle = Find<CheckBox>(root, "StartWithWindows");
                startup.PendingRead = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);
                var reading = controller.RefreshStartupAsync();
                Require(toggle.IsEnabled && AutomationProperties.GetItemStatus(toggle).Contains("Reading"), "Reading disabled or hid the focused control");
                toggle.IsChecked = true; toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(toggle.IsChecked == false && startup.Writes == 0, "A click during refresh changed Windows startup");
                startup.PendingRead.SetResult(startup.Status); Wait(reading);
                Require(toggle.IsEnabled && AutomationProperties.GetItemStatus(toggle) == "", "Read status did not clear");
            }
            finally { window.Close(); }
        });
        Test("Check now coalesces repeated clicks and renders the new offer", () =>
        {
            var updates = new UpdateFake { Pending = new(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously) };
            using var controller = new AppPreferencesController(Store(), updates, new StartupFake(), "1.0.5", false);
            var window = new PreferencesWindow(controller);
            try
            {
                var root = (FrameworkElement)window.Content; Layout(root); var check = Find<Button>(root, "CheckForUpdates");
                check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); check.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(updates.Calls == 1 && controller.Update.Checking, "Repeated click started another request");
                updates.Pending.SetResult(GitHub("1.0.6", true));
                var completed = new System.Threading.Tasks.TaskCompletionSource();
                void Changed() { if (!controller.Update.Checking) completed.TrySetResult(); }
                controller.Changed += Changed; Changed(); Wait(completed.Task); controller.Changed -= Changed;
                Require(controller.Update.Offer is not null && Find<Border>(root, "UpdateOffer").Visibility == Visibility.Visible, "Available update stayed hidden");
            }
            finally { window.Close(); }
        });
        Test("Windows-disabled startup is shown off with a settings link", () =>
        {
            var startup = new StartupFake { Status = new(false, false, "Startup is disabled in Windows.", true) };
            using var controller = new AppPreferencesController(Store(), new UpdateFake(), startup, "1.0.5", true); Wait(controller.RefreshStartupAsync());
            var window = new PreferencesWindow(controller);
            try
            {
                var root = (FrameworkElement)window.Content;
                Layout(root);
                Require(!Find<CheckBox>(root, "StartWithWindows").IsEnabled && Find<CheckBox>(root, "StartWithWindows").IsChecked == false && Find<Button>(root, "OpenStartupSettings").Visibility == Visibility.Visible, "OS disablement was hidden");
                Require(!Children(root).OfType<CheckBox>().Any(c => AutomationProperties.GetAutomationId(c) == "IncludePreviewReleases"), "Store install exposed GitHub preview switching");
            }
            finally { window.Close(); }
        });
        foreach (bool dark in new[] { true, false })
        foreach (bool store in new[] { true, false })
        foreach (bool error in new[] { false, true })
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            string name = $"settings-{(store ? "store" : "github")}-{(dark ? "dark" : "light")}-{(error ? "error" : "offer")}-{scale}";
            Test(name, () =>
            {
#pragma warning disable WPF0001
                app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
                Appearance.Current.Refresh();
                var updates = new UpdateFake { Name = store ? "Microsoft Store" : "GitHub releases", Offer = store ? new("Store update", "Microsoft Store", StoreUpdateSource.StoreUrl, false, "store:1.0.5") : GitHub("1.0.6-preview.2", true) };
                if (error) updates.Error = new HttpRequestException();
                var startup = new StartupFake { Status = error ? new(false, false, "Startup is disabled in Windows. Enable Snipsera in Startup apps to turn it back on.", true) : new(true, true, "Snipsera will start quietly in the tray when you sign in.", true) };
                using var controller = new AppPreferencesController(Store(), updates, startup, "1.0.5", store); Wait(controller.RefreshStartupAsync()); Wait(controller.CheckUpdatesAsync());
                var window = new PreferencesWindow(controller); window.ShowPage(SettingsPage.Updates);
                try
                {
                    var root = (FrameworkElement)window.Content; window.Content = null;
                    Render(root, scale == 2 ? 400 : 544, scale, Path.Combine(output, name + ".png"));
                    if (store && !error)
                    {
                        var title = Find<TextBlock>(root, "UpdateOfferTitle");
                        Require(new TextRange(title.ContentStart, title.ContentEnd).Text.Trim() == "Snipsera update available", "Store guessed an update version");
                    }
                }
                finally { window.Close(); }
            });
        }
        foreach (bool dark in new[] { true, false })
        foreach (bool store in new[] { true, false })
        foreach (double width in new[] { 320d, 720 })
        {
            string name = $"notice-{(store ? "store" : "github")}-{(dark ? "dark" : "light")}-{width}";
            Test(name, () =>
            {
#pragma warning disable WPF0001
                app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
                Appearance.Current.Refresh();
                var source = new UpdateFake { Offer = store ? new("Store update", "Microsoft Store", StoreUpdateSource.StoreUrl, false, "store:1.0.5") : GitHub("1.0.6-preview.2", true) };
                using var controller = new AppPreferencesController(Store(), source, new StartupFake(), "1.0.5", store); Wait(controller.CheckUpdatesAsync());
                var notice = new UpdateNotice(controller, () => { }); Render(notice, width, 1.5, Path.Combine(output, name + ".png"));
            });
        }
        app.Shutdown(); File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes, desktop = "not used; detached controls" }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
