using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using DrawingRect = System.Drawing.Rectangle;
using Forms = System.Windows.Forms;

namespace Framelet;

public sealed partial class CaptureApp : Application
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
    private CaptureKind? pendingCapture;
    private readonly Func<DrawingRect, BitmapSource> capturePixels;
    private HwndSource? hotkeySink;
    private ContextMenu trayMenu = null!;
    internal bool TrayMenuOpen => trayMenu?.IsOpen == true;
    private DrawingRect? previousArea;
    private readonly OverlayVisibility<ImageWindow> overlays;
    private OverlayShelfWindow? overlayShelf;
    private MenuItem? overlayMenu;
    public bool OverlaysHidden => overlays.Hidden;
    public int HiddenOverlayCount => overlays.HiddenCount;
    public bool CanShowOverlayShelf => OverlaysHidden && HiddenOverlayCount > 0 && !overlays.Suspended && !capturing && !Quitting;
    public bool CanHideOverlays => !OverlaysHidden && overlays.Desired.Length > 0 && !overlays.Suspended && !capturing && !Quitting;
    public DrawingRect OverlayShelfWorkArea { get; private set; }
    public double OverlayShelfScale { get; private set; } = 1;
    public BitmapSource? OverlayShelfImage { get; private set; }
    public event Action? OverlayVisibilityChanged;

    public CaptureApp(string dataRoot, Func<DrawingRect, BitmapSource>? capturePixels = null)
    {
        this.capturePixels = capturePixels ?? CaptureService.Capture;
        History = new HistoryStore(dataRoot); Settings = History.LoadSettings();
        Recording = new RecordingController(this, History.Root);
        Recording.Changed += RecordingChanged;
        overlays = new OverlayVisibility<ImageWindow>((window, show) =>
        {
            window.PresentOverlay(show, !capturing && (show || OverlaysHidden));
        });
        overlays.Changed += RefreshOverlayVisibility;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Ui.Install(this);
        Startup += (_, _) => Start();
        ComponentDispatcher.LeaveThreadModal += ResumePendingCapture;
        Exit += (_, _) => { ComponentDispatcher.LeaveThreadModal -= ResumePendingCapture; hotkeys?.Dispose(); hotkeySink?.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); };
        DispatcherUnhandledException += (_, e) => { e.Handled = true; Report(e.Exception); };
    }

    private void Start()
    {
        home = new HomeWindow(this); MainWindow = home;
        home.SourceInitialized += (_, _) =>
        {
            hotkeySink = new HwndSource(new HwndSourceParameters("Framelet hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            hotkeys = new HotkeyService(hotkeySink.Handle, kind => _ = CaptureAsync(kind));
            try { hotkeys.Apply(Settings); } catch (Exception ex) { home.SetStatus(ex.Message, true); }
            HwndSource.FromHwnd(new WindowInteropHelper(home).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            { if (msg == 0x8042) { ShowHome(); handled = true; } return IntPtr.Zero; });
        };
        home.Show();
        if (History.Notice is string notice) home.SetStatus(notice);
        trayIcon = AppIcon.Create();
        home.Icon = Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        trayMenu = new ContextMenu { Placement = PlacementMode.MousePoint, MinWidth = 250, HasDropShadow = true };
        void Item(string label, string symbol, Action action, string? shortcut = null)
        {
            var item = new MenuItem { Header = label, Icon = new UiIcon(symbol, 16) { Ink = Ui.Ink }, InputGestureText = shortcut ?? "" };
            item.Click += (_, _) => { trayMenu.IsOpen = false; Dispatcher.BeginInvoke(action); }; trayMenu.Items.Add(item);
        }
        Item("Capture area", "Area", () => _ = CaptureAsync(CaptureKind.Area), Settings.Area);
        Item("Capture window", "Window", () => _ = CaptureAsync(CaptureKind.Window), Settings.Window);
        Item("Capture display", "Screen", () => _ = CaptureAsync(CaptureKind.Screen), Settings.Screen);
        Item("Repeat last area", "History", () => _ = CaptureAsync(CaptureKind.PreviousArea), Settings.Previous);
        trayMenu.Items.Add(new Separator());
        Item("History and shortcuts", "History", ShowHome); Item("Scrolling capture", "Scrolling", () => _ = CaptureAsync(CaptureKind.Scrolling));
        Item("Record screen", "Record", OpenRecording);
        Item("Temporarily hide overlays", "HideOverlays", ToggleOverlays); overlayMenu = (MenuItem)trayMenu.Items[trayMenu.Items.Count - 1];
        Item("Quit Framelet", "Close", Quit);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Framelet — capture, copy, keep", Visible = true };
        ConfigureRecordingTray();
        tray.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) Dispatcher.BeginInvoke(ShowTrayMenu); };
        tray.DoubleClick += (_, _) => ShowHome();
        overlayShelf = new OverlayShelfWindow(this); RefreshOverlayVisibility();
    }

    public void HideOverlays(ImageWindow? initiator = null)
    {
        if (Quitting || capturing || ComponentDispatcher.IsThreadModal || OverlaysHidden || overlays.Desired.Length == 0) return;
        var desired = overlays.Desired;
        var anchor = initiator is not null && desired.Contains(initiator) ? initiator : desired.Last();
        if (anchor.IsLoaded)
        {
            var handle = new WindowInteropHelper(anchor).Handle;
            uint dpi = Native.GetDpiForWindow(handle);
            OverlayShelfWorkArea = Native.WorkArea(handle); OverlayShelfScale = dpi > 0 ? dpi / 96d : 1;
        }
        else { OverlayShelfWorkArea = Native.MonitorAtCursor(true); OverlayShelfScale = 1; }
        OverlayShelfImage = anchor.CaptureImage;
        overlays.Hide();
    }
    public void RestoreOverlays()
    {
        if (Quitting || capturing || ComponentDispatcher.IsThreadModal) return;
        overlays.Restore();
    }
    public void ToggleOverlays() { if (OverlaysHidden) RestoreOverlays(); else HideOverlays(); }
    private void RefreshOverlayVisibility()
    {
        if (overlayMenu is not null)
        {
            overlayMenu.Header = OverlaysHidden ? "Show overlays" : "Temporarily hide overlays";
            overlayMenu.IsEnabled = overlays.Desired.Length > 0 && !capturing && !Quitting;
        }
        overlayShelf?.Refresh(); OverlayVisibilityChanged?.Invoke();
    }

    public void ShowHome() { home.RefreshHistory(); home.Show(); if (home.WindowState == WindowState.Minimized) home.WindowState = WindowState.Normal; home.Activate(); }

    public void ShowTrayMenu()
    {
        foreach (var image in Windows.Cast<Window>().OfType<ImageWindow>()) image.DismissActions();
        // Acquire the native popup focus before WPF enters keyboard menu mode.
        // Otherwise WM_SETFOCUS clears its just-assigned logical focus and closes it.
        trayMenu.Focusable = false;
        trayMenu.IsOpen = true;
        if (PresentationSource.FromVisual(trayMenu) is HwndSource popup) Native.SetForegroundWindow(popup.Handle);
        trayMenu.Focusable = true;
        trayMenu.Focus();
    }

    public void UpdateSettings(AppSettings settings)
    {
        if (hotkeys is null) throw new InvalidOperationException("Shortcuts are not ready.");
        var old = Settings;
        hotkeys.Apply(settings);
        try { History.SaveSettings(settings); Settings = settings; for (int i = 0; i < 4; i++) ((MenuItem)trayMenu.Items[i]).InputGestureText = settings.Bindings[i]; }
        catch { hotkeys.Apply(old); throw; }
    }

    public async Task CaptureAsync(CaptureKind kind)
    {
        if (Quitting) return;
        if (Recording.IsActive) { home.SetStatus("Stop the recording before taking another screenshot."); return; }
        if (capturing || ComponentDispatcher.IsThreadModal) { pendingCapture = kind; return; }
        capturing = true;
        var visible = Windows.Cast<Window>().Where(w => w.IsVisible).ToList();
        IDisposable? suspension = null;
        bool completed = false;
        try
        {
            suspension = overlays.Suspend();
            trayMenu.IsOpen = false; Mouse.Capture(null);
            DrawingRect display = Native.MonitorAtCursor();
            foreach (Window window in visible)
            {
                if (window is ImageWindow imageWindow) imageWindow.PresentOverlay(false, false);
                else window.Hide();
            }
            await Task.Delay(180); Native.DwmFlush();
            DrawingRect desktop = Native.Desktop;
            BitmapSource image;
            string? scrollingWarning = null;
            if (kind == CaptureKind.Screen) image = capturePixels(display);
            else if (kind == CaptureKind.PreviousArea)
            {
                if (previousArea is not DrawingRect area || !desktop.Contains(area))
                    throw new InvalidOperationException("Select an area first. If your display layout changed, select it again.");
                image = capturePixels(area);
            }
            else
            {
                var frozen = capturePixels(desktop);
                var selection = new SelectionWindow(frozen, desktop, kind == CaptureKind.Window ? Native.CaptureTargets() : null);
                if (!await selection.PickAsync() || selection.Selected is not DrawingRect area) return;
                if (kind == CaptureKind.Window && selection.SelectedWindow is WindowTarget target)
                {
                    if (Native.GetForegroundWindow() != target.Handle && !Native.SetForegroundWindow(target.Handle))
                        throw new InvalidOperationException("That window could not be brought forward. Bring it to the front and try again.");
                    await Task.Delay(180); Native.DwmFlush();
                    image = capturePixels(Native.Bounds(target.Handle));
                }
                else if (kind == CaptureKind.Scrolling)
                {
                    if (ScrollCaptureLayout.Check(area) is string placementError) throw new InvalidOperationException(placementError);
                    using var session = new ScrollCaptureSession(area, capturePixels);
                    var scrolling = new ScrollCaptureWindow(session);
                    try
                    {
                        scrolling.Show(); var combined = await session.Completion;
                        if (combined is null) { if (scrolling.PlacementError is string error) throw new InvalidOperationException(error); return; }
                        image = combined; scrollingWarning = session.Warning;
                    }
                    finally { scrolling.Close(); }
                }
                else { image = CaptureService.Crop(frozen, area, desktop); previousArea = area; }
            }
            CaptureEntry entry;
            string? warning = scrollingWarning;
            try { entry = History.Add(image, kind); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, kind.ToString(), image.PixelWidth, image.PixelHeight); warning = "History could not be saved. Use Copy or Save to keep this capture."; }
            preview?.Close();
            foreach (var editor in visible.OfType<EditorWindow>()) editor.Show();
            preview = FloatingWindow(image, entry, false, warning); if (OverlaysHidden) OverlayShelfImage = image; overlays.Track(preview);
            home.RefreshHistory(); home.SetStatus(warning ?? "Capture ready");
            completed = true;
        }
        catch (Exception ex) { Report(ex); }
        finally
        {
            try
            {
                if (!completed) foreach (Window window in visible) if (window is not ImageWindow && window != overlayShelf && Windows.Cast<Window>().Contains(window)) window.Show();
                suspension?.Dispose();
            }
            finally
            {
                capturing = false; RefreshOverlayVisibility(); ResumePendingCapture(this, EventArgs.Empty);
            }
        }
    }

    private void ResumePendingCapture(object? sender, EventArgs e)
    {
        if (Quitting || capturing || ComponentDispatcher.IsThreadModal || pendingCapture is not CaptureKind next) return;
        pendingCapture = null; _ = Dispatcher.BeginInvoke(() => _ = CaptureAsync(next));
    }

    public void OpenEntry(CaptureEntry entry)
    {
        try
        {
            var image = CaptureService.Load(History.ImagePath(entry));
            using var replacing = overlays.BeginUpdate();
            preview?.Close(); preview = FloatingWindow(image, entry, false); if (OverlaysHidden) OverlayShelfImage = image; overlays.Track(preview);
        }
        catch (Exception ex) { Report(ex); }
    }

    public bool Pin(BitmapSource image, CaptureEntry entry)
    {
        if (pins.Count >= 8) { Report(new InvalidOperationException("Close a pinned capture before adding another. Up to eight captures can be pinned.")); return false; }
        var window = FloatingWindow(image, entry, true);
        pins.Add(window); overlays.Track(window); return true;
    }

    private ImageWindow FloatingWindow(BitmapSource image, CaptureEntry entry, bool pinned, string? warning = null)
    {
        var window = new ImageWindow(this, image, entry, pinned, warning);
        window.Closed += (_, _) => { overlays.Forget(window); if (preview == window) preview = null; if (pins.Remove(window)) ReflowPins(); };
        return window;
    }

    public void TogglePin(ImageWindow window)
    {
        if (window.IsPinned)
        {
            pins.Remove(window); preview?.Close(); preview = window;
            window.SetPinned(false); ReflowPins();
        }
        else
        {
            if (pins.Count >= 8) { Report(new InvalidOperationException("Close a pinned capture before adding another. Up to eight captures can be pinned.")); return; }
            if (preview == window) preview = null;
            pins.Add(window); window.SetPinned(true); ReflowPins();
        }
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

    public void EditImage(BitmapSource image) { ShowEditor(new EditDocument(image)); if (preview is not null) overlays.SetDesired(preview, false); }

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
        if (home?.IsVisible != true) ConfirmWindow.Ask(Windows.Cast<Window>().FirstOrDefault(w => w.IsActive), "Framelet", message, "OK");
    }

    public async void Quit()
    {
        if (Recording.State == RecordingState.Starting) { home.SetStatus("Wait for recording to start before quitting."); return; }
        if (Recording.IsActive)
        {
            try { await Recording.StopAsync(); }
            catch (Exception ex) { Report(ex); return; }
        }
        foreach (var editor in Windows.Cast<Window>().OfType<EditorWindow>().ToArray()) if (!editor.ConfirmClose()) return;
        Recording.Dispose();
        Quitting = true; tray?.Dispose(); foreach (Window window in Windows.Cast<Window>().ToArray()) window.Close(); Shutdown();
    }
}
