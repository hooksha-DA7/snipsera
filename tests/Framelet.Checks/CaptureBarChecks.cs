using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet;

// Detached controls and fake native shortcut registration: no desktop pixels or physical input.
internal static class CaptureBarChecks
{
    private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
    private static IEnumerable<DependencyObject> Children(DependencyObject node)
    {
        yield return node;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
            foreach (var child in Children(VisualTreeHelper.GetChild(node, i))) yield return child;
    }
    internal static int Run(string output)
    {
        Directory.CreateDirectory(output);
        var app = new CaptureApp(Path.Combine(output, "isolated-app"));
        var outcomes = new List<object>(); int failures = 0;
        void Test(string name, Action check)
        {
            try { check(); outcomes.Add(new { name, status = "passed" }); Console.WriteLine("PASS " + name); }
            catch (Exception ex) { failures++; outcomes.Add(new { name, status = "failed", error = ex.ToString() }); Console.WriteLine("FAIL " + name + ": " + ex.Message); }
        }
        HistoryStore Store() => new(Path.Combine(output, Guid.NewGuid().ToString("N")));
        Test("New settings add a fifth distinct launcher without moving the four direct actions", () =>
        {
            var settings = new AppSettings();
            Require(settings.Bindings.Length == 5 && settings.Bindings.Distinct().Count() == 5, "Launcher shortcut missing or duplicate");
            Require(settings.Area == "Ctrl+Alt+Shift+1" && settings.Previous == "Ctrl+Alt+Shift+4" && settings.AllInOne == "Ctrl+Alt+Shift+5", "Existing defaults changed");
        });
        Test("Four-binding settings retain Print Screen and add the launcher on upgrade", () =>
        {
            var store = Store();
            File.WriteAllText(Path.Combine(store.Root, "settings.json"), JsonSerializer.Serialize(new { Area = "PrintScreen", Window = "Ctrl+2", Screen = "Alt+3", Previous = "Ctrl+4" }));
            var settings = store.LoadSettings();
            Require(settings == new AppSettings("PrintScreen", "Ctrl+2", "Alt+3", "Ctrl+4"), "Upgrade changed a saved shortcut");
        });
        Test("Upgrade chooses an unused launcher default when an old action already uses 5", () =>
        {
            var store = Store();
            File.WriteAllText(Path.Combine(store.Root, "settings.json"), JsonSerializer.Serialize(new { Area = "Ctrl+Alt+Shift+5", Window = "Ctrl+Alt+Shift+6", Screen = "Ctrl+Alt+Shift+7", Previous = "Ctrl+Alt+Shift+8" }));
            var settings = store.LoadSettings();
            Require(settings.Area == "Ctrl+Alt+Shift+5" && settings.Previous == "Ctrl+Alt+Shift+8" && settings.AllInOne == "Ctrl+Alt+Shift+9", "Migration overwrote a binding or collided");
        });
        Test("A recorded Print Screen launcher persists after restarting settings", () =>
        {
            var store = Store(); var settings = new AppSettings(AllInOne: "PrintScreen");
            store.SaveSettings(settings);
            Require(new HistoryStore(store.Root).LoadSettings() == settings, "Launcher shortcut was not persisted");
            Require(Hotkey.Parse(settings.AllInOne) == new Hotkey(0, 0x2C), "Print Screen key differs");
        });
        Test("Native shortcut IDs route direct captures and the launcher separately", () =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Shortcut route check") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            var registered = new Dictionary<int, Hotkey>(); var captured = new List<CaptureKind>(); int opened = 0;
            using var service = new HotkeyService(source.Handle, captured.Add, () => opened++, (id, key) => registered.TryAdd(id, key), id => registered.Remove(id));
            service.Apply(new AppSettings(AllInOne: "PrintScreen"));
            Require(registered.Count == 5 && registered[3104].VirtualKey == 0x2C, "Launcher was not registered");
            Require(service.DispatchShortcut(3104), "Launcher message rejected"); Pump();
            Require(opened == 1 && captured.Count == 0, "Launcher took a screenshot directly");
            for (int i = 0; i < 4; i++) Require(service.DispatchShortcut(3100 + i), "Direct action rejected");
            Pump(); Require(captured.SequenceEqual(new[] { CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.PreviousArea }), "Direct actions changed");
            Require(!service.DispatchShortcut(3105) && !service.DispatchShortcut(3099), "Unregistered shortcut dispatched");
            using (service.Suspend())
            {
                Require(registered.Count == 0 && !service.DispatchShortcut(3104), "Shortcut recorder suspension still dispatches");
                using (service.Suspend()) Require(registered.Count == 0, "Nested suspension registered keys");
            }
            Require(registered.Count == 5, "Suspension failed to restore all shortcuts");
            service.Dispose(); Require(registered.Count == 0 && !service.DispatchShortcut(3104), "Disposed service retained keys");
        });
        Test("Duplicate Print Screen reports the two actions and leaves working registrations intact", () =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Duplicate check") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            var registered = new Dictionary<int, Hotkey>();
            using var service = new HotkeyService(source.Handle, _ => { }, () => { }, (id, key) => registered.TryAdd(id, key), id => registered.Remove(id));
            service.Apply(new AppSettings()); var before = registered.ToArray(); string? message = null;
            try { service.Apply(new AppSettings(Area: "PrintScreen", AllInOne: "PrtSc")); } catch (InvalidOperationException ex) { message = ex.Message; }
            Require(message?.Contains("All-in-one capture and Area") == true, "Conflict did not identify the actions");
            Require(registered.OrderBy(x => x.Key).SequenceEqual(before.OrderBy(x => x.Key)), "Conflict replaced working shortcuts");
        });
        Test("A Windows registration conflict on the fifth key rolls back all previous shortcuts", () =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Rollback check") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            var registered = new Dictionary<int, Hotkey>();
            using var service = new HotkeyService(source.Handle, _ => { }, () => { }, (id, key) => key.VirtualKey != 0x2C && registered.TryAdd(id, key), id => registered.Remove(id));
            service.Apply(new AppSettings()); var before = registered.ToArray(); string? message = null;
            try { service.Apply(new AppSettings(AllInOne: "PrintScreen")); } catch (InvalidOperationException ex) { message = ex.Message; }
            Require(message?.Contains("All-in-one capture shortcut") == true && message.Contains("Snipping Tool"), "No actionable Print Screen conflict");
            Require(registered.OrderBy(x => x.Key).SequenceEqual(before.OrderBy(x => x.Key)), "Registration failure did not restore old bindings");
        });
        Test("A newly conflicting launcher keeps four working direct shortcuts available on startup", () =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Upgrade conflict check") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            var registered = new Dictionary<int, Hotkey>();
            using var service = new HotkeyService(source.Handle, _ => { }, () => { }, (id, key) => key.VirtualKey != 0x2C && registered.TryAdd(id, key), id => registered.Remove(id));
            try { service.Apply(new AppSettings(AllInOne: "PrintScreen"), keepDirectOnLauncherFailure: true); } catch (InvalidOperationException) { }
            Require(registered.Count == 4 && !registered.ContainsKey(3104), "New launcher disabled existing direct shortcuts");
            using (service.Suspend()) Require(registered.Count == 0, "Suspension failed");
            Require(registered.Count == 4, "Partial startup registration not restored");
            service.Apply(new AppSettings()); Require(registered.Count == 5, "Changing the conflicting launcher did not recover it");
        });
        Test("A rejected Save after failed startup never activates unpersisted direct shortcuts", () =>
        {
            using var source = new HwndSource(new HwndSourceParameters("Rejected save check") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            var registered = new Dictionary<int, Hotkey>();
            using var service = new HotkeyService(source.Handle, _ => { }, () => { }, (id, key) => key.VirtualKey is not (0x2C or 0x31) && registered.TryAdd(id, key), id => registered.Remove(id));
            try { service.Apply(new AppSettings(), keepDirectOnLauncherFailure: true); } catch (InvalidOperationException) { }
            Require(registered.Count == 0, "Failed startup retained a partial direct set");
            try { service.Apply(new AppSettings("Ctrl+A", "Ctrl+B", "Ctrl+C", "Ctrl+D", "PrintScreen")); } catch (InvalidOperationException) { }
            Require(registered.Count == 0, "Rejected Save activated unsaved shortcuts");
        });
        Test("Closing the bar precedes its single capture callback for all five modes", () =>
        {
            foreach (var kind in new[] { CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.Scrolling, CaptureKind.PreviousArea })
            {
                bool closed = false; var captured = new List<CaptureKind>();
                var bar = new CaptureBarWindow(mode => { Require(closed, "Bar remained open during capture"); captured.Add(mode); }, true);
                bar.Closed += (_, _) => closed = true;
                Require(bar.Choose(kind), "Mode not selected"); Pump();
                Require(closed && captured.SequenceEqual(new[] { kind }), "Wrong or repeated capture");
                Require(!bar.Choose(kind), "Closed bar accepted another capture"); Pump(); Require(captured.Count == 1, "Callback fired twice");
            }
        });
        Test("Unavailable repeat and Escape leave screenshots and history untouched", () =>
        {
            int captures = 0; bool closed = false;
            var bar = new CaptureBarWindow(_ => captures++, false); bar.Closed += (_, _) => closed = true;
            Require(!bar.Choose(CaptureKind.PreviousArea), "Repeat accepted without an area");
            Require(!bar.HandleKey(Key.A, ModifierKeys.None) && !bar.HandleKey(Key.D1, ModifierKeys.Control), "Other shortcut was consumed");
            Require(bar.HandleKey(Key.Escape, ModifierKeys.None), "Escape not handled"); Pump();
            Require(closed && captures == 0, "Cancel captured pixels");
        });
        Test("Number keys select the same modes as the visible controls", () =>
        {
            var modes = new[] { CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.Scrolling, CaptureKind.PreviousArea };
            for (int i = 0; i < modes.Length; i++)
            {
                CaptureKind? selected = null; var bar = new CaptureBarWindow(mode => selected = mode, true);
                Require(bar.HandleKey((Key)((int)Key.D1 + i), ModifierKeys.None), "Number key rejected"); Pump();
                Require(selected == modes[i], "Number key selected wrong mode");
            }
        });
        Test("Clicking a mode button follows the same close-before-capture path", () =>
        {
            bool closed = false; CaptureKind? selected = null;
            var bar = new CaptureBarWindow(kind => { Require(closed, "Button captured with the bar open"); selected = kind; }, true);
            bar.Closed += (_, _) => closed = true;
            var button = Children((FrameworkElement)bar.Content).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "CaptureBarScreen");
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(selected == CaptureKind.Screen, "Display button routed incorrectly");
        });
        Test("Clicking outside or closing the bar never dispatches a screenshot", () =>
        {
            foreach (bool outside in new[] { true, false })
            {
                int captured = 0; bool closed = false; var bar = new CaptureBarWindow(_ => captured++, true);
                bar.Closed += (_, _) => closed = true;
                if (outside) typeof(Window).GetMethod("OnDeactivated", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(bar, new object[] { EventArgs.Empty });
                else Children((FrameworkElement)bar.Content).OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == "CaptureBarClose").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(closed && captured == 0 && !bar.Choose(CaptureKind.Area), "Dismissed bar captured an image");
            }
        });
        Test("Home exposes the fifth key recorder and preserves all direct capture controls", () =>
        {
            var home = new HomeWindow(app);
            try
            {
                var root = (FrameworkElement)home.Content; home.Content = null;
                root.Measure(new Size(880, 670)); root.Arrange(new Rect(0, 0, 880, 670)); root.UpdateLayout();
                var controls = Children(root).OfType<Control>().ToArray();
                // Unmeasured templates are not materialized; the recorder fields are direct logical content.
                var recorders = (ShortcutRecorder[])typeof(HomeWindow).GetField("shortcuts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(home)!;
                Require(recorders.Length == 5 && recorders[4].Saved == app.Settings.AllInOne, "Fifth key recorder missing or incorrectly initialized");
                recorders[4].Press(Key.PrintScreen, ModifierKeys.None, true);
                Require(recorders[4].Shortcut == "PrintScreen" && recorders.Take(4).Select(r => r.Shortcut).SequenceEqual(app.Settings.Bindings.Take(4)), "Changing launcher altered direct shortcuts");
                Require(controls.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == "ShowCaptureBar"), "Home launcher missing");
                foreach (string id in new[] { "CaptureArea", "CaptureWindow", "CaptureScreen", "CapturePreviousArea", "CaptureScrolling", "CaptureRecording" })
                    Require(controls.OfType<Button>().Any(b => AutomationProperties.GetAutomationId(b) == id), "Removed existing action: " + id);
            }
            finally { home.Close(); }
        });
        Test("Placement stays in the source work area at negative origins and varied DPI", () =>
        {
            foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
            foreach (var work in new[] { new System.Drawing.Rectangle(-1920, -200, 1920, 1040), new System.Drawing.Rectangle(1200, 80, 900, 700) })
            {
                var box = CaptureBarWindow.Placement(work, new Size(468, 100), scale);
                Require(work.Contains(box), "Bar escaped its work area");
                Require(Math.Abs(box.Left - work.Left - (work.Width - box.Width) / 2d) <= 1, "Bar is not centered");
            }
        });
        foreach (var limit in new[] { new Size(360, 260), new Size(210, 400), new Size(150, 500), new Size(200, 180) })
        {
            Test($"Narrow capture bar keeps full-size controls reachable within {limit}", () =>
            {
                var bar = new CaptureBarWindow(_ => { }, false);
                try
                {
                    var size = bar.Layout(limit); var root = (FrameworkElement)bar.Content; bar.Content = null;
                    root.Measure(limit); root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    Require(size.Width <= limit.Width && size.Height <= limit.Height && bar.MaxWidth == limit.Width && bar.MaxHeight == limit.Height, "Actual window limit was not applied");
                    Require(bar.Columns > 0, "Narrow screen did not wrap the controls");
                    var buttons = Children(root).OfType<Button>().ToArray();
                    Require(buttons.Length == 6 && buttons.All(b => b.ActualWidth >= 28 && b.ActualHeight >= 28), "Mode controls were shrunk or lost");
                    if (!bar.Scrolls)
                        foreach (var button in buttons)
                        {
                            var box = button.TransformToAncestor(root).TransformBounds(new Rect(0, 0, button.ActualWidth, button.ActualHeight));
                            Require(box.Left >= 0 && box.Top >= 0 && box.Right <= size.Width + .5 && box.Bottom <= size.Height + .5, "Wrapped control is clipped");
                        }
                    else Require(Children(root).OfType<ScrollViewer>().Any(), "Small-height fallback cannot scroll to hidden controls");
                    var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width * 1.5), (int)Math.Ceiling(size.Height * 1.5), 144, 144, PixelFormats.Pbgra32);
                    image.Render(root); CaptureService.Save(image, Path.Combine(output, $"capture-bar-narrow-{limit.Width}-{limit.Height}.png"));
                }
                finally { bar.Close(); }
            });
        }
        foreach (bool dark in new[] { true, false })
        foreach (double scale in new[] { 1d, 1.25, 1.5, 2 })
        {
            string name = $"capture-bar-{(dark ? "dark" : "light")}-{scale}";
            Test(name, () =>
            {
#pragma warning disable WPF0001
                app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;
#pragma warning restore WPF0001
                Appearance.Current.Refresh(); var bar = new CaptureBarWindow(_ => { }, true);
                try
                {
                    var root = (FrameworkElement)bar.Content; bar.Content = null;
                    root.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); var size = root.DesiredSize;
                    root.Arrange(new Rect(new Point(), size)); root.UpdateLayout();
                    Require(size.Width <= 560 && size.Height <= 160, "Capture bar is too large");
                    var buttons = Children(root).OfType<Button>().ToArray();
                    Require(buttons.Length == 6, "Five modes and close must remain visible");
                    var bounds = buttons.Select(b => b.TransformToAncestor(root).TransformBounds(new Rect(0, 0, b.ActualWidth, b.ActualHeight))).ToArray();
                    for (int i = 0; i < buttons.Length; i++)
                    {
                        var b = bounds[i];
                        Require(b.Width >= 24 && b.Height >= 24 && b.Left >= 0 && b.Top >= 0 && b.Right <= size.Width + .5 && b.Bottom <= size.Height + .5, "Control is clipped");
                        Require(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(buttons[i])), "Missing accessible name");
                        for (int j = 0; j < i; j++)
                        {
                            var intersection = Rect.Intersect(b, bounds[j]);
                            Require(intersection.IsEmpty || intersection.Width <= .5 || intersection.Height <= .5, "Controls overlap");
                        }
                    }
                    var image = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    image.Render(root); CaptureService.Save(image, Path.Combine(output, name + ".png"));
                }
                finally { bar.Close(); }
            });
        }
        app.Recording.Dispose(); app.Shutdown();
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new { failures, outcomes, desktop = "not used" }, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }
}
