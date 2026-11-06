using System;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using Microsoft.Win32;

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
        CloseCaptureBar();
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
        recordingBar = new RecordingBarWindow(Recording, ToolbarAppearance);
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
    private async void ShowRecordingResult(RecordingResult result)
    {
        if (Quitting) return;
        var image = RecordingPreview.Placeholder();
        bool pinned = pins.Count < 8;
        var window = FloatingWindow(image, null, pinned, recording: result);
        if (pinned) pins.Add(window);
        else { preview?.Close(); preview = window; }
        if (OverlaysHidden) OverlayShelfImage = image;
        overlays.Track(window);
        try
        {
            var thumbnail = await RecordingPreview.ThumbnailAsync(result.FilePath, History.Root);
            if (Quitting || !Windows.Cast<Window>().Contains(window)) return;
            window.UpdateRecordingThumbnail(thumbnail);
            if (OverlaysHidden) { OverlayShelfImage = thumbnail; RefreshOverlayVisibility(); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or System.ComponentModel.Win32Exception)
        { /* The file actions remain usable even if Windows cannot decode a thumbnail. */ }
    }
    public void SaveRecording(RecordingResult result, Window owner)
    {
        string extension = result.Format == RecordingFormat.Gif ? ".gif" : ".mp4";
        var dialog = new SaveFileDialog { Title = "Save recording copy", Filter = result.Format == RecordingFormat.Gif ? "GIF animation (*.gif)|*.gif" : "MP4 video (*.mp4)|*.mp4",
            DefaultExt = extension, AddExtension = true, InitialDirectory = Storage.VideoFolder, FileName = Path.GetFileName(result.FilePath) };
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            if (!Path.GetFullPath(dialog.FileName).Equals(Path.GetFullPath(result.FilePath), StringComparison.OrdinalIgnoreCase))
            {
                string staging = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try { File.Copy(result.FilePath, staging, false); File.Move(staging, dialog.FileName, true); }
                finally { if (File.Exists(staging)) File.Delete(staging); }
            }
            home.SetStatus("Recording copy saved");
        }
        catch (Exception ex) { Report(ex); }
    }
    public void ShowRecordingFolder(RecordingResult result)
    {
        try
        {
            var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = true };
            start.ArgumentList.Add("/select,"); start.ArgumentList.Add(result.FilePath); Process.Start(start);
        }
        catch (Exception ex) { Report(ex); }
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
            foreach (var window in visible) if (RestoreAfterRecording(window) && Windows.Cast<Window>().Contains(window)) window.Show();
            suspended.Dispose();
        });
    }
    internal static bool RestoreAfterRecording(Window window) => window is not RecordingWindow;
    private sealed class RecordingPresentation(Action restore) : IDisposable
    {
        private Action? remaining = restore;
        public void Dispose() => Interlocked.Exchange(ref remaining, null)?.Invoke();
    }
}
