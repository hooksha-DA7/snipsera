using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Framelet;

internal static class AcceptanceFixture
{
    // Observe real product interactions without driving controls or changing production behavior.
    public static int Run(string output, bool smoke = false)
    {
        output = Path.GetFullPath(output); Directory.CreateDirectory(output);
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        using var writer = new StreamWriter(new FileStream(Path.Combine(output, "events.jsonl"), FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite)) { AutoFlush = true };
        CaptureApp? running = null;
        int samples = 0, visibleFloatingSamples = 0, states = 0, wheelEvents = 0;
        var actions = new Dictionary<string, int>();
        var app = new CaptureApp(Path.Combine(output, "data"), bounds =>
        {
            samples++;
            if (running!.Windows.Cast<Window>().Where(w => w is ImageWindow or OverlayShelfWindow)
                .Any(w => new WindowInteropHelper(w).Handle is var handle && handle != IntPtr.Zero && Native.IsWindowVisible(handle)))
                visibleFloatingSamples++;
            return CaptureService.Capture(bounds);
        });
        running = app;
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler((_, e) =>
        {
            if (e.Source is not Button button) return;
            string id = AutomationProperties.GetAutomationId(button);
            if (string.IsNullOrEmpty(id)) return;
            actions.TryGetValue(id, out int count); actions[id] = count + 1;
            writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, action = id }));
        }), true);
        InputManager.Current.PostProcessInput += (_, e) =>
        {
            if (e.StagingItem.Input is not MouseEventArgs mouse || mouse.OriginalSource is not DependencyObject source) return;
            var window = Window.GetWindow(source);
            if (mouse is MouseWheelEventArgs wheel && window is ImageWindow)
            {
                wheelEvents++;
                writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, input = mouse.RoutedEvent.Name, wheel.Delta, hidden = app.OverlaysHidden }));
            }
            else if (window is OverlayShelfWindow && mouse.RoutedEvent.Name is "MouseEnter" or "MouseDown" or "PreviewMouseDown")
                writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, input = mouse.RoutedEvent.Name, surface = "restore-tab", hidden = app.OverlaysHidden }));
        };
        string previous = "";
        void Observe()
        {
            var windows = app.Windows.Cast<Window>().Select(window =>
            {
                var handle = new WindowInteropHelper(window).Handle;
                var bounds = handle == IntPtr.Zero ? System.Drawing.Rectangle.Empty : Native.Bounds(handle);
                return new
                {
                    title = window.Title, handle = handle.ToInt64(), visible = window.IsVisible,
                    nativeVisible = handle != IntPtr.Zero && Native.IsWindowVisible(handle),
                    x = bounds.X, y = bounds.Y, width = bounds.Width, height = bounds.Height,
                    pinned = window is ImageWindow { IsPinned: true },
                    sourceWidth = (window as ImageWindow)?.CaptureImage.PixelWidth,
                    sourceHeight = (window as ImageWindow)?.CaptureImage.PixelHeight,
                    scrolling = window is ScrollCaptureWindow ? string.Join(" | ", Texts(window).Where(text => text.IsVisible).Select(text => text.Text)) : null
                };
            }).ToArray();
            var captures = app.History.Entries.Select(entry => new { entry.Id, entry.Mode, entry.Width, entry.Height }).ToArray();
            string state = JsonSerializer.Serialize(new { hidden = app.OverlaysHidden, hiddenCount = app.HiddenOverlayCount, windows, captures });
            if (state == previous) return;
            previous = state; states++;
            writer.WriteLine(JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, samples, visibleFloatingSamples, state = JsonSerializer.Deserialize<JsonElement>(state) }));
            File.WriteAllText(Path.Combine(output, "latest-state.json"), JsonSerializer.Serialize(new { samples, visibleFloatingSamples, states, wheelEvents, actions, state = JsonSerializer.Deserialize<JsonElement>(state) }, new JsonSerializerOptions { WriteIndented = true }));
        }
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        timer.Tick += (_, _) => Observe();
        app.Startup += (_, _) =>
        {
            timer.Start(); Observe();
            if (smoke)
            {
                var close = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                close.Tick += (_, _) => { close.Stop(); app.Quit(); }; close.Start();
            }
        };
        app.Exit += (_, _) =>
        {
            timer.Stop(); Observe();
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { samples, visibleFloatingSamples, states, wheelEvents, actions, scope = "Real product UI and capture backend; input operated by the tester" }, new JsonSerializerOptions { WriteIndented = true }));
        };
        return app.Run();
    }

    private static IEnumerable<TextBlock> Texts(DependencyObject parent)
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is TextBlock text) yield return text;
            foreach (var nested in Texts(child)) yield return nested;
        }
    }
}
