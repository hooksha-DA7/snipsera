using System;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;

namespace Framelet;

public sealed partial class CaptureApp
{
    public RecordingController Recording { get; }
    private RecordingWindow? recordingWindow;
    private RecordingBarWindow? recordingBar;
    private MenuItem? stopRecordingMenu, pauseRecordingMenu;
    internal string RecordingTrayText => tray?.Text ?? "";

    public void OpenRecording()
    {
        if (Recording.IsActive) { if (recordingBar?.IsCaptureExcluded == true) { recordingBar.Show(); recordingBar.Activate(); } else ShowTrayMenu(); return; }
        if (recordingWindow is null)
        {
            recordingWindow = new RecordingWindow(this);
            recordingWindow.Closed += (_, _) => recordingWindow = null;
        }
        recordingWindow.Show(); recordingWindow.Activate();
    }
    private void ConfigureRecordingTray()
    {
        pauseRecordingMenu = new MenuItem { Header = "Pause recording", IsEnabled = false };
        stopRecordingMenu = new MenuItem { Header = "Stop recording", IsEnabled = false };
        pauseRecordingMenu.Click += (_, _) => { trayMenu.IsOpen = false; Recording.TogglePause(); };
        stopRecordingMenu.Click += async (_, _) => { trayMenu.IsOpen = false; try { await Recording.StopAsync(); } catch (Exception ex) { Report(ex); } };
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, pauseRecordingMenu);
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, stopRecordingMenu);
        RecordingChanged();
    }
    private void RecordingChanged()
    {
        string clock = Recording.Elapsed.TotalHours >= 1 ? Recording.Elapsed.ToString(@"hh\:mm\:ss") : Recording.Elapsed.ToString(@"mm\:ss");
        if (tray is not null) tray.Text = Recording.IsActive ? $"Snipsera — {(Recording.State == RecordingState.Paused ? "Paused" : "Recording")} {clock}" : "Snipsera — capture, copy, keep";
        if (stopRecordingMenu is not null)
        {
            stopRecordingMenu.IsEnabled = Recording.State is RecordingState.Recording or RecordingState.Paused;
            stopRecordingMenu.Header = Recording.IsActive ? "Stop recording · " + clock : "Stop recording";
            pauseRecordingMenu!.IsEnabled = stopRecordingMenu.IsEnabled;
            pauseRecordingMenu.Header = Recording.State == RecordingState.Paused ? "Resume recording" : "Pause recording";
        }
    }
    internal void ShowRecordingBar()
    {
        recordingBar = new RecordingBarWindow(Recording);
        recordingBar.Show();
        if (!recordingBar.IsCaptureExcluded)
        {
            recordingBar.Hide();
            home.SetStatus("Windows could not exclude the recording bar. Use the tray menu to pause or stop.");
        }
    }
    internal void CloseRecordingBar()
    {
        var bar = recordingBar; recordingBar = null; bar?.Close();
    }
    internal async Task<RecordingTarget?> SelectRecordingTargetAsync(RecordingTargetKind kind, int width, int height)
    {
        if (capturing || Recording.IsActive || ComponentDispatcher.IsThreadModal) throw new InvalidOperationException("Finish the current capture or dialog before selecting a recording area.");
        capturing = true;
        var visible = Windows.Cast<Window>().Where(w => w.IsVisible).ToArray();
        using var suspended = overlays.Suspend();
        try
        {
            foreach (var window in visible) window.Hide();
            trayMenu.IsOpen = false; Mouse.Capture(null); await Task.Delay(180); Native.DwmFlush();
            var desktop = Native.Desktop;
            var targets = kind == RecordingTargetKind.Window ? Native.CaptureTargets() : kind == RecordingTargetKind.Display
                ? System.Windows.Forms.Screen.AllScreens.Select((screen, index) => new WindowTarget(IntPtr.Zero, $"Display {index + 1} · {screen.Bounds.Width} × {screen.Bounds.Height}", screen.Bounds)).ToArray() : null;
            var selection = new SelectionWindow(CaptureService.Capture(desktop), desktop,
                targets, kind == RecordingTargetKind.Custom ? new System.Drawing.Size(width, height) : null,
                kind == RecordingTargetKind.Display ? "Click a display to record   ·   Esc to cancel" : kind == RecordingTargetKind.Area ? "Drag the area to record   ·   Esc to cancel" : null,
                physicalDrag: kind == RecordingTargetKind.Area);
            if (!await selection.PickAsync() || selection.Selected is not Rectangle area) return null;
            if (kind == RecordingTargetKind.Custom)
            {
                if (area.Width != width || area.Height != height) throw new InvalidOperationException("The selected frame does not match the custom dimensions.");
                if (!desktop.Contains(area)) throw new InvalidOperationException("The custom rectangle extends beyond the desktop. Choose a frame that fits inside one display.");
            }
            if (area.Width < 16 || area.Height < 16) throw new InvalidOperationException("Select an area at least 16 × 16 pixels.");
            if (kind is RecordingTargetKind.Area or RecordingTargetKind.Custom && !System.Windows.Forms.Screen.AllScreens.Any(screen => screen.Bounds.Contains(area)))
                throw new InvalidOperationException("Keep the recording area inside one display. Select the area again without crossing the display edge.");
            var targetWindow = selection.SelectedWindow;
            return new(kind, area, targetWindow?.Handle ?? IntPtr.Zero, targetWindow?.Title ?? $"{area.Width} × {area.Height} px");
        }
        finally
        {
            foreach (var window in visible) if (window is not ImageWindow and not OverlayShelfWindow && Windows.Cast<Window>().Contains(window)) window.Show();
            capturing = false;
        }
    }
    internal IDisposable SuspendForRecording()
    {
        if (capturing || ComponentDispatcher.IsThreadModal) throw new InvalidOperationException("Finish the current screenshot or dialog before recording.");
        var visible = Windows.Cast<Window>().Where(w => w.IsVisible && w is not ImageWindow && w is not OverlayShelfWindow).ToArray();
        var suspended = overlays.Suspend();
        foreach (var window in visible) window.Hide();
        return new RecordingPresentation(() =>
        {
            foreach (var window in visible) if (Windows.Cast<Window>().Contains(window)) window.Show();
            suspended.Dispose();
        });
    }
    private sealed class RecordingPresentation(Action restore) : IDisposable
    {
        private Action? remaining = restore;
        public void Dispose() => Interlocked.Exchange(ref remaining, null)?.Invoke();
    }
}
