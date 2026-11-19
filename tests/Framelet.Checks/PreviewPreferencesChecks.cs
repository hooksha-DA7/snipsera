using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Framelet;

// Detached controls, synthetic settings and fake registration only. No HWNDs, physical input or Windows preference writes.
internal static class PreviewPreferencesChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        var app = new CaptureApp(Path.Combine(output, "profile"));
        var outcomes = new List<object>(); int failures = 0;
        string Folder() { string folder = Path.Combine(output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder); return folder; }
        void Check(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS " + name); outcomes.Add(new { name, status = "passed" }); }
            catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex.Message); outcomes.Add(new { name, status = "failed", error = ex.ToString() }); }
        }
        Check("Dismiss after sharing defaults on without writing a settings file", () =>
        {
            string folder = Folder(); var preferences = new PreviewPreferences(folder);
            Require(preferences.DismissAfterSharing && preferences.Notice is null && !Directory.EnumerateFiles(folder).Any(), "Default changed or wrote data");
        });
        Check("Preview preference survives restart without changing other settings or duplicate events", () =>
        {
            string folder = Folder(); File.WriteAllText(Path.Combine(folder, "settings.json"), "keep shortcuts");
            var preferences = new PreviewPreferences(folder); int events = 0; preferences.Changed += () => events++;
            preferences.SetDismissAfterSharing(false); preferences.SetDismissAfterSharing(false);
            Require(!new PreviewPreferences(folder).DismissAfterSharing && events == 1, "Preference or event count changed");
            preferences.SetDismissAfterSharing(true);
            Require(new PreviewPreferences(folder).DismissAfterSharing && events == 2 && File.ReadAllText(Path.Combine(folder, "settings.json")) == "keep shortcuts", "Other preferences changed");
        });
        Check("Corrupt and oversized preview settings are preserved until an explicit choice", () =>
        {
            foreach (string contents in new[] { "{broken", "null", "{\"DismissAfterSharing\":\"yes\"}", new string('x', 5000) })
            {
                string folder = Folder(), path = Path.Combine(folder, "preview-preferences.json"); File.WriteAllText(path, contents);
                var preferences = new PreviewPreferences(folder);
                Require(preferences.DismissAfterSharing && preferences.Notice is not null && File.ReadAllText(path) == contents, "Corrupt preference was silently replaced");
                preferences.SetDismissAfterSharing(true);
                Require(preferences.Notice is null && new PreviewPreferences(folder).Notice is null, "Explicit recovery did not save");
            }
        });
        Check("Failed preference save preserves state, file bytes and events and cleans staging", () =>
        {
            string folder = Folder(), path = Path.Combine(folder, "preview-preferences.json"); var preferences = new PreviewPreferences(folder);
            preferences.SetDismissAfterSharing(false); byte[] original = File.ReadAllBytes(path); int events = 0; preferences.Changed += () => events++;
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                try { preferences.SetDismissAfterSharing(true); throw new Exception("Locked save succeeded"); } catch (IOException) { }
            Require(!preferences.DismissAfterSharing && events == 0 && original.SequenceEqual(File.ReadAllBytes(path)) && !Directory.GetFiles(folder, "*.tmp").Any(), "Failed preference save mutated or leaked state");
        });
        Check("Four-shortcut upgrade preserves old chords and adds two unused helper defaults", () =>
        {
            var store = new HistoryStore(Folder()); string path = Path.Combine(store.Root, "settings.json");
            string original = JsonSerializer.Serialize(new { Area = "Ctrl+Alt+Shift+5", Window = "Ctrl+Alt+Shift+6", Screen = "Ctrl+Alt+Shift+7", Previous = "Ctrl+Alt+Shift+8" }); File.WriteAllText(path, original);
            var settings = store.LoadSettings();
            Require(settings.Bindings.Take(4).SequenceEqual(new[] { "Ctrl+Alt+Shift+5", "Ctrl+Alt+Shift+6", "Ctrl+Alt+Shift+7", "Ctrl+Alt+Shift+8" }), "Upgrade changed old shortcuts");
            Require(settings.AllInOne == "Ctrl+Alt+Shift+9" && settings.ReopenLast == "Ctrl+Alt+Shift+0" && settings.Bindings.Select(Hotkey.Parse).Distinct().Count() == 6, "New defaults collide");
            Require(File.ReadAllText(path) == original, "Read-only migration rewrote settings");
        });
        Check("Five-shortcut upgrade preserves Print Screen and an occupied reopen default", () =>
        {
            var store = new HistoryStore(Folder()); string path = Path.Combine(store.Root, "settings.json");
            File.WriteAllText(path, JsonSerializer.Serialize(new { Area = "PrintScreen", Window = "Ctrl+2", Screen = "Alt+3", Previous = "Ctrl+4", AllInOne = "Ctrl+Alt+Shift+6" }));
            var settings = store.LoadSettings();
            Require(settings.Area == "PrintScreen" && settings.AllInOne == "Ctrl+Alt+Shift+6" && settings.ReopenLast == "Ctrl+Alt+Shift+7", "Upgrade replaced saved keys or collided");
            store.SaveSettings(settings); Require(new HistoryStore(store.Root).LoadSettings() == settings, "Six-binding migration did not persist");
        });
        Check("Reopen supports Print Screen and duplicate aliases cannot replace saved shortcuts", () =>
        {
            var store = new HistoryStore(Folder()); var settings = new AppSettings(ReopenLast: "PrintScreen"); store.SaveSettings(settings);
            string path = Path.Combine(store.Root, "settings.json"); byte[] original = File.ReadAllBytes(path);
            Require(new HistoryStore(store.Root).LoadSettings() == settings, "Reopen shortcut did not persist");
            try { store.SaveSettings(settings with { Area = "PrtSc" }); throw new Exception("Duplicate keys accepted"); } catch (InvalidOperationException) { }
            Require(original.SequenceEqual(File.ReadAllBytes(path)), "Duplicate write replaced settings");
            using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                try { store.SaveSettings(settings with { ReopenLast = "Ctrl+R" }); throw new Exception("Locked shortcut save succeeded"); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            Require(original.SequenceEqual(File.ReadAllBytes(path)) && !Directory.GetFiles(store.Root, "*.tmp").Any(), "Failed shortcut save changed or leaked files");
        });
        Check("Duplicate loaded shortcuts fall back visibly while preserving source settings", () =>
        {
            var store = new HistoryStore(Folder()); string path = Path.Combine(store.Root, "settings.json");
            string contents = JsonSerializer.Serialize(new AppSettings(Area: "PrintScreen", ReopenLast: "PrtSc")); File.WriteAllText(path, contents);
            Require(store.LoadSettings() == new AppSettings() && store.Notice is not null && File.ReadAllText(path) == contents, "Invalid shortcut settings were accepted or destroyed");
        });
        Check("Sixth shortcut reopens independently and honors suspend, queued cancellation and disposal", () =>
        {
            var registered = new Dictionary<int, Hotkey>(); var captures = new List<CaptureKind>(); int launcher = 0, reopened = 0;
            using var service = new HotkeyService(captures.Add, () => launcher++, (id, key) => registered.TryAdd(id, key), id => registered.Remove(id), () => reopened++);
            service.Apply(new AppSettings()); Require(registered.Count == 6, "Expected six registered actions");
            Require(service.DispatchShortcut(3105), "Reopen did not dispatch"); Pump();
            Require(reopened == 1 && launcher == 0 && captures.Count == 0, "Reopen invoked capture or launcher");
            Require(service.DispatchShortcut(3105), "Queued reopen rejected");
            using (service.Suspend()) { Pump(); Require(reopened == 1 && registered.Count == 0 && !service.DispatchShortcut(3105), "Suspended reopen still ran"); }
            Require(registered.Count == 6 && service.DispatchShortcut(3104) && service.DispatchShortcut(3100), "Working shortcuts not restored"); Pump();
            Require(launcher == 1 && captures.SequenceEqual(new[] { CaptureKind.Area }), "Existing routing changed");
            service.Dispose(); Require(registered.Count == 0 && !service.DispatchShortcut(3105), "Disposed action remained active");
        });
        Check("Conflicting reopen on startup preserves five existing shortcuts through suspension and retry", () =>
        {
            var registered = new Dictionary<int, Hotkey>(); int reopen = 0;
            using var service = new HotkeyService(_ => { }, () => { }, (id, key) => key.VirtualKey != 0x2C && registered.TryAdd(id, key), id => registered.Remove(id), () => reopen++);
            try { service.Apply(new AppSettings(ReopenLast: "PrintScreen"), keepDirectOnLauncherFailure: true); throw new Exception("Conflict accepted"); } catch (InvalidOperationException) { }
            Require(registered.Count == 5 && !service.DispatchShortcut(3105), "New conflict disabled old shortcuts or enabled reopen");
            using (service.Suspend()) Require(registered.Count == 0, "Suspend did not release partial startup state");
            Require(registered.Count == 5, "Partial startup state failed to resume");
            service.Apply(new AppSettings(ReopenLast: "Ctrl+R")); Require(registered.Count == 6 && service.DispatchShortcut(3105), "Corrected reopen did not register"); Pump(); Require(reopen == 1, "Corrected reopen did not run");
        });
        Check("Rejected reopen edits leave all previous registrations working", () =>
        {
            var registered = new Dictionary<int, Hotkey>();
            using var service = new HotkeyService(_ => { }, () => { }, (id, key) => key.VirtualKey != 0x2C && registered.TryAdd(id, key), id => registered.Remove(id));
            service.Apply(new AppSettings()); var previous = registered.OrderBy(pair => pair.Key).ToArray();
            try { service.Apply(new AppSettings(ReopenLast: "PrintScreen")); throw new Exception("Conflict accepted"); } catch (InvalidOperationException) { }
            Require(registered.OrderBy(pair => pair.Key).SequenceEqual(previous), "Failed reopen edit changed previous shortcuts");
            try { service.Apply(new AppSettings(ReopenLast: "Ctrl+Alt+Shift+1")); throw new Exception("Duplicate accepted"); } catch (InvalidOperationException ex) { Require(ex.Message.Contains("Reopen last dismissed capture and Area"), "Conflict did not identify both actions"); }
            Require(registered.OrderBy(pair => pair.Key).SequenceEqual(previous), "Duplicate disabled shortcuts");
        });
        Check("General checkbox persists changes and failed writes restore the saved state", () =>
        {
            var window = new PreferencesWindow(app.Preferences, app);
            try
            {
                var root = (FrameworkElement)window.Content; root.Measure(new Size(780, 640)); root.Arrange(new Rect(0, 0, 780, 640)); root.UpdateLayout();
                var checkbox = WorkflowChecks.Children(root).OfType<CheckBox>().Single(box => AutomationProperties.GetAutomationId(box) == "DismissAfterSharing");
                Require(checkbox.IsChecked == true, "Default setting is not reflected");
                checkbox.IsChecked = false; checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Require(!app.PreviewPreferences.DismissAfterSharing && !new PreviewPreferences(app.History.Root).DismissAfterSharing, "Checkbox did not persist");
                string path = Path.Combine(app.History.Root, "preview-preferences.json");
                using (File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None)) { checkbox.IsChecked = true; checkbox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); }
                Require(checkbox.IsChecked == false && !app.PreviewPreferences.DismissAfterSharing, "Failed write left an unsaved toggle on");
                var error = WorkflowChecks.Children(root).OfType<TextBlock>().Single(text => AutomationProperties.GetAutomationId(text) == "PreviewPreferencesError");
                Require(error.Visibility == Visibility.Visible && error.Text.Contains("could not be saved"), "Failed write was not explained");
                Require(!window.IsVisible && new WindowInteropHelper(window).Handle == IntPtr.Zero, "Settings check opened a native window");
            }
            finally { window.Close(); }
        });
        Check("Settings records the reopen chord without changing other pending shortcuts", () =>
        {
            var panel = new ShortcutSettingsPanel(app);
            try
            {
                var recorders = WorkflowChecks.Children(panel).OfType<ShortcutRecorder>().OrderBy(control => AutomationProperties.GetAutomationId(control)).ToArray();
                Require(recorders.Length == 6 && recorders[5].Saved == app.Settings.ReopenLast, "Reopen recorder was not initialized");
                recorders[5].Press(Key.PrintScreen, ModifierKeys.None, true);
                Require(recorders[5].Shortcut == "PrintScreen" && recorders.Take(5).Select(control => control.Shortcut).SequenceEqual(app.Settings.Bindings.Take(5)), "Reopen recording modified another shortcut");
            }
            finally { panel.Detach(); }
        });
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes }, new JsonSerializerOptions { WriteIndented = true }));
        app.Recording.Dispose(); app.Preferences.Dispose(); app.Shutdown();
        return failures == 0 ? 0 : 1;
    }
}
