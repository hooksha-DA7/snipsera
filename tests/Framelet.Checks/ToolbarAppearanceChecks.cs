using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using Framelet;

// No HWNDs, desktop input, capture, installation or changes to the user's Windows preferences.
internal static class ToolbarAppearanceChecks
{
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output); var results = new List<object>(); int failures = 0;
        var app = new CaptureApp(Path.Combine(output, "profile"));
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); results.Add(new { name, passed = true }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); results.Add(new { name, passed = false, error = ex.ToString() }); }
        }
        static void Require(bool value, string message = "Assertion failed") { if (!value) throw new Exception(message); }
        string Folder() { string folder = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
        static T Find<T>(DependencyObject root, string id) where T : DependencyObject => WorkflowChecks.Children(root).OfType<T>().Single(c => AutomationProperties.GetAutomationId(c) == id);
        static FrameworkElement Layout(FrameworkElement root) { root.Measure(new Size(760, 900)); root.Arrange(new Rect(0, 0, 760, 900)); root.UpdateLayout(); return root; }

        Check("Default appearance does not create a settings file", () =>
        {
            string root = Folder(); var settings = new ToolbarAppearanceSettings(root);
            Require(settings.Options == new ToolbarAppearanceOptions() && settings.Notice is null && !Directory.EnumerateFiles(root).Any());
        });
        Check("Frosted choice and opacity survive a restart independently of other preferences", () =>
        {
            string root = Folder(); File.WriteAllText(Path.Combine(root, "storage.json"), "keep");
            var settings = new ToolbarAppearanceSettings(root); int changed = 0; settings.Changed += () => changed++;
            settings.Update(ToolbarMaterial.Frosted, 70); settings.Update(ToolbarMaterial.Frosted, 70);
            Require(new ToolbarAppearanceSettings(root).Options == new ToolbarAppearanceOptions(ToolbarMaterial.Frosted, 70));
            Require(changed == 1 && File.ReadAllText(Path.Combine(root, "storage.json")) == "keep");
        });
        Check("Invalid material and opacity cannot replace saved settings", () =>
        {
            var settings = new ToolbarAppearanceSettings(Folder()); settings.Update(ToolbarMaterial.Tinted, 90);
            foreach (var value in new[] { new ToolbarAppearanceOptions((ToolbarMaterial)999, 80), new(ToolbarMaterial.Frosted, 69), new(ToolbarMaterial.Tinted, 101) })
            {
                try { settings.Update(value.Material, value.Opacity); throw new Exception("Invalid value accepted"); } catch (ArgumentOutOfRangeException) { }
                Require(settings.Options == new ToolbarAppearanceOptions(ToolbarMaterial.Tinted, 90));
            }
        });
        Check("Corrupt, oversized and unknown values recover visibly without overwriting evidence", () =>
        {
            foreach (string text in new[] { "{broken", "null", "{\"Material\":\"Unknown\"}", "{\"Opacity\":15}", new string('x', 5000) })
            {
                string root = Folder(), path = Path.Combine(root, "toolbar-appearance.json"); File.WriteAllText(path, text);
                var settings = new ToolbarAppearanceSettings(root);
                Require(settings.Notice is not null && settings.Options == new ToolbarAppearanceOptions() && File.ReadAllText(path) == text);
                settings.Update(ToolbarMaterial.Tinted, 80); Require(settings.Notice is null && new ToolbarAppearanceSettings(root).Notice is null);
            }
        });
        Check("Locked settings preserve prior bytes, state and events, and remove temporary files", () =>
        {
            string root = Folder(), path = Path.Combine(root, "toolbar-appearance.json"); var settings = new ToolbarAppearanceSettings(root);
            settings.Update(ToolbarMaterial.Tinted, 90); byte[] before = File.ReadAllBytes(path); int changes = 0; settings.Changed += () => changes++;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                try { settings.Update(ToolbarMaterial.Frosted, 70); throw new Exception("Locked save succeeded"); } catch (IOException) { }
            Require(settings.Options.Opacity == 90 && changes == 0 && before.SequenceEqual(File.ReadAllBytes(path)) && !Directory.GetFiles(root, "*.tmp").Any());
        });
        Check("Solid fallback covers contrast, transparency, unsupported Windows and native failure", () =>
        {
            foreach (bool transparency in new[] { false, true }) foreach (bool supported in new[] { false, true }) foreach (bool failed in new[] { false, true })
            foreach (var material in Enum.GetValues<ToolbarMaterial>())
            {
                var plan = ToolbarSurfacePlan.Resolve(new(material, 70), transparency, supported, failed);
                bool solid = !transparency || material == ToolbarMaterial.Frosted && (!supported || failed);
                Require(plan.Alpha == (solid ? 255 : 179));
                Require(plan.Frosted == (material == ToolbarMaterial.Frosted && !solid));
            }
            Require(!ToolbarSurfacePlan.Resolve(new(ToolbarMaterial.Frosted, 100), true, true).Frosted, "Opaque backgrounds should not allocate a hidden native plate");
        });
        Check("Inset native bounds stay entirely inside the existing rounded panel", () =>
        {
            double radius = FloatingVisuals.Radius, inset = ToolbarSurface.BackplateInset(radius);
            Require(inset == 4 && Math.Sqrt(2 * Math.Pow(radius - inset, 2)) <= radius);
            Require(inset * 2 < 46, "The smallest recording control panel lost its backdrop");
        });
        Check("Runtime fallback feedback never changes the saved appearance or rewrites the file", () =>
        {
            string root = Folder(), path = Path.Combine(root, "toolbar-appearance.json"); var settings = new ToolbarAppearanceSettings(root);
            settings.Update(ToolbarMaterial.Frosted, 70); byte[] before = File.ReadAllBytes(path); int changes = 0; settings.Changed += () => changes++;
            settings.ReportNativeResult(false); settings.ReportNativeResult(false);
            Require(changes == 1 && settings.Options == new ToolbarAppearanceOptions(ToolbarMaterial.Frosted, 70));
            Require(File.ReadAllBytes(path).SequenceEqual(before));
            if (Appearance.Current.Policy.Frosting && ToolbarAppearanceSettings.NativeFrostingSupported) Require(settings.CapabilityMessage.Contains("could not initialize"));
            settings.ReportNativeResult(true); Require(!settings.CapabilityMessage.Contains("could not initialize"));
        });
        Check("Appearance UI saves both radio choices and all four opacity presets", () =>
        {
            var settings = new ToolbarAppearanceSettings(Folder()); var panel = Layout(new AppearanceSettingsPanel(settings));
            Find<RadioButton>(panel, "ToolbarFrosted").IsChecked = true; Require(settings.Options.Material == ToolbarMaterial.Frosted);
            foreach (int preset in new[] { 70, 80, 90, 100 })
            {
                Find<Button>(panel, "ToolbarOpacity" + preset).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(settings.Options.Opacity == preset && Find<TextBlock>(panel, "ToolbarOpacityValue").Text == preset + "%");
            }
            Find<RadioButton>(panel, "ToolbarTinted").IsChecked = true; Require(settings.Options.Material == ToolbarMaterial.Tinted);
            ((AppearanceSettingsPanel)panel).Detach();
        });
        Check("Appearance UI restores previous choice and reports a failed save", () =>
        {
            string root = Folder(); var settings = new ToolbarAppearanceSettings(root); settings.Update(ToolbarMaterial.Tinted, 90);
            var panel = Layout(new AppearanceSettingsPanel(settings));
            using (File.Open(Path.Combine(root, "toolbar-appearance.json"), FileMode.Open, FileAccess.Read, FileShare.None))
                Find<RadioButton>(panel, "ToolbarFrosted").IsChecked = true;
            Require(settings.Options.Material == ToolbarMaterial.Tinted && Find<RadioButton>(panel, "ToolbarTinted").IsChecked == true);
            Require(Find<TextBlock>(panel, "ToolbarAppearanceError").Text.Contains("could not be saved"));
            Find<Button>(panel, "ToolbarOpacity90").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(Find<TextBlock>(panel, "ToolbarAppearanceError").Visibility == Visibility.Collapsed, "Stale save failure remains after choosing the current preference");
            ((AppearanceSettingsPanel)panel).Detach();
        });
        Check("Closing Appearance flushes a pending keyboard opacity change and detaches listeners", () =>
        {
            foreach (bool dragging in new[] { false, true })
            {
                var settings = new ToolbarAppearanceSettings(Folder()); var panel = Layout(new AppearanceSettingsPanel(settings));
                var slider = Find<Slider>(panel, "ToolbarOpacity");
                if (dragging) slider.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0, 0) { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragStartedEvent });
                slider.Value = 73; ((AppearanceSettingsPanel)panel).Detach();
                Require(settings.Options.Opacity == 73);
                settings.Update(ToolbarMaterial.Frosted, 95); Require(slider.Value == 73);
            }
        });
        Check("Appearance navigation keeps all existing section identities", () =>
        {
            Require((int)SettingsPage.General == 0 && (int)SettingsPage.SaveLocations == 1 && (int)SettingsPage.KeyboardShortcuts == 2 && (int)SettingsPage.Updates == 3);
            var window = new PreferencesWindow(app.Preferences, app); window.ShowPage(SettingsPage.Appearance); var root = Layout((FrameworkElement)window.Content);
            Require(Find<ListBoxItem>(root, "SettingsAppearance") is not null && Find<AppearanceSettingsPanel>(root, "ToolbarAppearanceSettings") is not null);
            window.Close();
        });
        Check("All three bar types follow saved tint without changing control opacity or dimensions", () =>
        {
            Appearance.Current.Refresh(); app.ToolbarAppearance.Update(ToolbarMaterial.Tinted, 70);
            Window[] windows = [new CaptureBarWindow(_ => { }, true, app.ToolbarAppearance), new RecordingWindow(app), new RecordingBarWindow(app.Recording, app.ToolbarAppearance)];
            foreach (var window in windows)
            {
                var root = (FrameworkElement)(window.Content ?? throw new Exception("Missing bar content")); window.Content = null; root.Measure(new Size(800, 1000)); root.Arrange(new Rect(root.DesiredSize)); root.UpdateLayout();
                var size = root.RenderSize;
                var surfaces = WorkflowChecks.Children(root).OfType<Border>().Where(b => b.Background is SolidColorBrush c && c.Color.A == (Appearance.Current.Policy.Frosting ? 179 : 255)).ToArray();
                Require(surfaces.Length > 0, window.GetType().Name + " has no configured surface");
                var controlOpacities = WorkflowChecks.Children(root).OfType<Control>().ToDictionary(c => c, c => c.Opacity);
                app.ToolbarAppearance.Update(ToolbarMaterial.Tinted, 100);
                Require(surfaces.All(b => ((SolidColorBrush)b.Background).Color.A == 255));
                Require(window.Opacity == 1 && root.Opacity == 1 && surfaces.All(p => p.Opacity == 1));
                Require(controlOpacities.All(pair => pair.Key.Opacity == pair.Value), "Material changed foreground opacity");
                root.UpdateLayout(); Require(root.RenderSize == size, "Material altered layout"); window.Close();
                app.ToolbarAppearance.Update(ToolbarMaterial.Tinted, 70);
            }
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, nativeVisualVerification = "pending; no windows or native plates created", results }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown(); return failures == 0 ? 0 : 1;
    }
}
