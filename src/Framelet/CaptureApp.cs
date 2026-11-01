using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DrawingRect = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace Framelet;

public sealed class CaptureApp : Application
{
    public HistoryStore History { get; }
    public AppSettings Settings { get; private set; }
    public bool Quitting { get; private set; }
    private readonly List<ImageWindow> pins = [];
    private HomeWindow home = null!;
    private ImageWindow? preview;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private HotkeyService? hotkeys;
    private bool capturing;
    private DrawingRect? previousArea;

    public CaptureApp(string dataRoot)
    {
        History = new HistoryStore(dataRoot); Settings = History.LoadSettings();
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Ui.Install(this);
        Startup += (_, _) => Start();
        Exit += (_, _) => { hotkeys?.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); };
        DispatcherUnhandledException += (_, e) => { e.Handled = true; Report(e.Exception); };
    }

    private void Start()
    {
        home = new HomeWindow(this); MainWindow = home;
        home.SourceInitialized += (_, _) =>
        {
            hotkeys = new HotkeyService(new WindowInteropHelper(home).Handle, kind => _ = CaptureAsync(kind));
            try { hotkeys.Apply(Settings); } catch (Exception ex) { home.SetStatus(ex.Message, true); }
            HwndSource.FromHwnd(new WindowInteropHelper(home).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            { if (msg == 0x8042) { ShowHome(); handled = true; } return IntPtr.Zero; });
        };
        home.Show();
        if (History.Notice is string notice) home.SetStatus(notice);
        trayIcon = AppIcon.Create();
        home.Icon = Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Capture area", null, (_, _) => _ = CaptureAsync(CaptureKind.Area));
        menu.Items.Add("Capture window", null, (_, _) => _ = CaptureAsync(CaptureKind.Window));
        menu.Items.Add("Capture display", null, (_, _) => _ = CaptureAsync(CaptureKind.Screen));
        menu.Items.Add("Repeat last area", null, (_, _) => _ = CaptureAsync(CaptureKind.PreviousArea));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("History and shortcuts", null, (_, _) => ShowHome());
        menu.Items.Add("Quit Framelet", null, (_, _) => Quit());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Framelet — capture, copy, keep", Visible = true, ContextMenuStrip = menu };
        tray.DoubleClick += (_, _) => ShowHome();
    }

    public void ShowHome() { home.RefreshHistory(); home.Show(); if (home.WindowState == WindowState.Minimized) home.WindowState = WindowState.Normal; home.Activate(); }

    public void UpdateSettings(AppSettings settings)
    {
        if (hotkeys is null) throw new InvalidOperationException("Shortcuts are not ready.");
        var old = Settings;
        hotkeys.Apply(settings);
        try { History.SaveSettings(settings); Settings = settings; }
        catch { hotkeys.Apply(old); throw; }
    }

    public async Task CaptureAsync(CaptureKind kind)
    {
        if (capturing) return;
        capturing = true;
        var visible = Windows.Cast<Window>().Where(w => w.IsVisible).ToList();
        bool completed = false;
        try
        {
            DrawingRect display = Native.MonitorAtCursor();
            foreach (Window window in visible) window.Hide();
            await Task.Delay(180); Native.DwmFlush();
            DrawingRect desktop = Native.Desktop;
            BitmapSource image;
            if (kind == CaptureKind.Screen) image = CaptureService.Capture(display);
            else if (kind == CaptureKind.PreviousArea)
            {
                if (previousArea is not DrawingRect area || !desktop.Contains(area))
                    throw new InvalidOperationException("Select an area first. If your display layout changed, select it again.");
                image = CaptureService.Capture(area);
            }
            else
            {
                var frozen = CaptureService.Capture(desktop);
                var selection = new SelectionWindow(frozen, desktop, kind == CaptureKind.Window ? Native.CaptureTargets() : null);
                if (selection.ShowDialog() != true || selection.Selected is not DrawingRect area) return;
                if (kind == CaptureKind.Window && selection.SelectedWindow is WindowTarget target)
                {
                    if (Native.GetForegroundWindow() != target.Handle && !Native.SetForegroundWindow(target.Handle))
                        throw new InvalidOperationException("That window could not be brought forward. Bring it to the front and try again.");
                    await Task.Delay(180); Native.DwmFlush();
                    image = CaptureService.Capture(Native.Bounds(target.Handle));
                }
                else { image = CaptureService.Crop(frozen, area, desktop); previousArea = area; }
            }
            CaptureEntry entry;
            string? warning = null;
            try { entry = History.Add(image, kind); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, kind.ToString(), image.PixelWidth, image.PixelHeight); warning = "History could not be saved. Use Copy or Save to keep this capture."; }
            preview?.Close();
            foreach (var pin in pins.ToArray()) pin.Show();
            foreach (var editor in visible.OfType<EditorWindow>()) editor.Show();
            preview = new ImageWindow(this, image, entry, false, warning); preview.Show();
            home.RefreshHistory(); home.SetStatus(warning ?? "Capture ready");
            completed = true;
        }
        catch (Exception ex) { Report(ex); }
        finally
        {
            capturing = false;
            if (!completed) foreach (Window window in visible) if (Windows.Cast<Window>().Contains(window)) window.Show();
        }
    }

    public void OpenEntry(CaptureEntry entry)
    {
        try { var image = CaptureService.Load(History.ImagePath(entry)); preview?.Close(); preview = new ImageWindow(this, image, entry, false); preview.Show(); }
        catch (Exception ex) { Report(ex); }
    }

    public void Pin(BitmapSource image, CaptureEntry entry)
    {
        if (pins.Count >= 8) { Report(new InvalidOperationException("Close a pinned capture before adding another. Up to eight captures can be pinned.")); return; }
        var window = new ImageWindow(this, image, entry, true);
        pins.Add(window); window.Closed += (_, _) => { pins.Remove(window); ReflowPins(); }; window.Show();
    }

    public void ReflowPins()
    {
        foreach (var group in pins.Where(p => p.IsLoaded && p.IsDocked).Reverse().GroupBy(p => p.DockWorkArea))
        {
            var docked = group.ToArray(); double scale = docked[0].DockScale;
            var bounds = FloatingLayout.Pins(group.Key, docked.Select(p => new Size(p.CaptureImage.PixelWidth, p.CaptureImage.PixelHeight)).ToArray(), scale);
            for (int i = 0; i < docked.Length; i++) docked[i].Place(bounds[i], scale);
        }
    }

    public void EditImage(BitmapSource image) { ShowEditor(new EditDocument(image)); preview?.Hide(); }

    public void OpenImageOrProject()
    {
        var dialog = new OpenFileDialog { Title = "Open image or editable project", Filter = "Images and Framelet projects|*.png;*.jpg;*.jpeg;*.framelet|PNG and JPEG images|*.png;*.jpg;*.jpeg|Framelet project|*.framelet", CheckFileExists = true };
        var owner = Windows.Cast<Window>().FirstOrDefault(w => w.IsActive) ?? home;
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            bool project = Path.GetExtension(dialog.FileName).Equals(".framelet", StringComparison.OrdinalIgnoreCase);
            var document = project ? EditFiles.LoadProject(dialog.FileName) : new EditDocument(EditFiles.LoadImage(dialog.FileName));
            ShowEditor(document, Path.GetFileNameWithoutExtension(dialog.FileName), project ? dialog.FileName : null);
        }
        catch (Exception ex) { Report(ex); }
    }

    private void ShowEditor(EditDocument document, string name = "Capture", string? projectPath = null)
    {
        var editor = new EditorWindow(document, image => Pin(image, new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, "Edited", image.PixelWidth, image.PixelHeight)), OpenImageOrProject, name, projectPath);
        editor.Show(); editor.Activate();
    }

    public void SaveImage(BitmapSource image, Window owner)
    {
        var dialog = new SaveFileDialog { Title = "Save capture", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true,
            FileName = "Framelet-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".png", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures) };
        if (dialog.ShowDialog(owner) != true) return;
        try { CaptureService.Save(image, dialog.FileName); home.SetStatus("Capture saved"); }
        catch (Exception ex) { Report(ex); }
    }

    public void Report(Exception exception)
    {
        string message = exception is System.ComponentModel.Win32Exception ? "Windows could not read the display. Unlock your desktop and try again." : exception.Message;
        if (home is not null) home.SetStatus(message, true);
        if (home?.IsVisible != true) MessageBox.Show(message, "Framelet", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    public void Quit()
    {
        foreach (var editor in Windows.Cast<Window>().OfType<EditorWindow>().ToArray()) if (!editor.ConfirmClose()) return;
        Quitting = true; tray?.Dispose(); foreach (Window window in Windows.Cast<Window>().ToArray()) window.Close(); Shutdown();
    }
}
