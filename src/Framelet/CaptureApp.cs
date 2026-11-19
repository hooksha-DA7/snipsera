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
    public AppPreferencesController Preferences { get; }
    public StorageLocations Storage { get; }
    public ToolbarAppearanceSettings ToolbarAppearance { get; }
    public PreviewPreferences PreviewPreferences { get; }
    public event Action? SettingsChanged;
    public bool Quitting { get; private set; }
    private readonly List<ImageWindow> pins = [];
    private HomeWindow home = null!;
    private ImageWindow? preview;
    private CaptureBarWindow? captureBar;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private HotkeyService? hotkeys;
    private bool capturing;
    private CaptureKind? pendingCapture;
    private readonly Func<DrawingRect, BitmapSource> capturePixels;
    private readonly bool startInTray;
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

    public CaptureApp(string dataRoot, Func<DrawingRect, BitmapSource>? capturePixels = null, bool startInTray = false, AppPreferencesController? preferences = null,
        Action<ImageWindow, bool>? presentOverlay = null, Func<(DrawingRect WorkArea, double Scale)>? overlayPlacement = null)
    {
        this.capturePixels = capturePixels ?? CaptureService.Capture;
        this.startInTray = startInTray;
        History = new HistoryStore(dataRoot); Settings = History.LoadSettings();
        Storage = new StorageLocations(History.Root);
        ToolbarAppearance = new ToolbarAppearanceSettings(History.Root);
        PreviewPreferences = new PreviewPreferences(History.Root);
        presentNativeOverlays = presentOverlay is null;
        this.overlayPlacement = overlayPlacement ?? (() => { var work = Native.MonitorAtCursor(true); return (work, ScrollCaptureLayout.ScaleFor(work)); });
        Preferences = preferences ?? AppPreferencesController.Create(History.Root);
        Recording = new RecordingController(this, History.Root);
        Recording.Changed += RecordingChanged;
        Recording.Completed += ShowRecordingResult;
        overlays = new OverlayVisibility<ImageWindow>((window, show) =>
        {
            if (presentOverlay is not null) presentOverlay(window, show);
            else window.PresentOverlay(show, !capturing && (show || OverlaysHidden));
        });
        overlays.Changed += RefreshOverlayVisibility;
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Ui.Install(this);
        Startup += (_, _) => Start();
        ComponentDispatcher.LeaveThreadModal += ResumePendingCapture;
        Exit += (_, _) => { SystemEvents.DisplaySettingsChanged -= RefreshTrayIcon; SystemEvents.UserPreferenceChanged -= RefreshTrayIcon; ComponentDispatcher.LeaveThreadModal -= ResumePendingCapture; DisposePreferences(); hotkeys?.Dispose(); hotkeySink?.Dispose(); tray?.Dispose(); trayIcon?.Dispose(); };
        DispatcherUnhandledException += (_, e) => { e.Handled = true; Report(e.Exception); };
    }

    private void Start()
    {
        home = new HomeWindow(this); MainWindow = home;
        home.SourceInitialized += (_, _) =>
        {
            hotkeySink = new HwndSource(new HwndSourceParameters("Snipsera hotkeys") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
            hotkeys = new HotkeyService(hotkeySink.Handle, kind => _ = CaptureAsync(kind), ShowCaptureBar, ReopenLastDismissed);
            try { hotkeys.Apply(Settings, keepDirectOnLauncherFailure: true); } catch (Exception ex) { home.SetStatus(ex.Message, true); }
            HwndSource.FromHwnd(new WindowInteropHelper(home).Handle)?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            { if (msg == 0x8042) { ShowHome(); handled = true; } return IntPtr.Zero; });
        };
        if (startInTray) new WindowInteropHelper(home).EnsureHandle(); else home.Show();
        if (History.Notice is string notice) home.SetStatus(notice);
        trayIcon = AppIcon.CreateTray(AppIcon.CurrentTrayDpi());
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
        Item("All-in-one capture", "Area", ShowCaptureBar, Settings.AllInOne);
        ((MenuItem)trayMenu.Items[4]).Icon = CaptureBarVisuals.LauncherGlyph(16);
        Item("Reopen last dismissed capture", "History", ReopenLastDismissed, Settings.ReopenLast);
        reopenPreviewMenu = (MenuItem)trayMenu.Items[5];
        trayMenu.Items.Add(new Separator());
        Item("History", "History", ShowHome); Item("Scrolling capture", "Scrolling", () => _ = CaptureAsync(CaptureKind.Scrolling));
        Item("Record screen", "Record", OpenRecording);
        Item("Temporarily hide overlays", "HideOverlays", ToggleOverlays); overlayMenu = (MenuItem)trayMenu.Items[trayMenu.Items.Count - 1];
        Item("Clear unpinned previews", "Close", ClearUnpinnedPreviews); clearPreviewMenu = (MenuItem)trayMenu.Items[trayMenu.Items.Count - 1];
        Item("Quit Snipsera", "Close", Quit);
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "Snipsera — capture, copy, keep", Visible = true };
        ConfigureRecordingTray();
        tray.MouseUp += (_, e) => { if (e.Button == Forms.MouseButtons.Right) Dispatcher.BeginInvoke(ShowTrayMenu); };
        tray.DoubleClick += (_, _) => ShowHome();
        overlayShelf = new OverlayShelfWindow(this); RefreshOverlayVisibility();
        InitializePreferences();
        SystemEvents.DisplaySettingsChanged += RefreshTrayIcon;
        SystemEvents.UserPreferenceChanged += RefreshTrayIcon;
    }

    private void RefreshTrayIcon(object? sender, EventArgs e)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (Quitting || tray is null) return;
            uint dpi = AppIcon.CurrentTrayDpi();
            int size = AppIcon.TrayPixels(dpi);
            if (trayIcon?.Width == size) return;
            var next = AppIcon.CreateTray(dpi);
            var previous = trayIcon; tray.Icon = next; trayIcon = next; previous?.Dispose();
        });
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
        else { var placement = overlayPlacement(); OverlayShelfWorkArea = placement.WorkArea; OverlayShelfScale = placement.Scale; }
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
        overlayShelf?.Refresh(); RestackPreviewDeck(); RefreshPreviewActions(); OverlayVisibilityChanged?.Invoke();
    }

    public void ShowHome() { CloseCaptureBar(); home.RefreshHistory(); home.Show(); if (home.WindowState == WindowState.Minimized) home.WindowState = WindowState.Normal; home.Activate(); }

    public void ShowCaptureBar()
    {
        if (Quitting || capturing || ComponentDispatcher.IsThreadModal) return;
        if (Recording.IsActive) { home.SetStatus("Stop the recording before taking another screenshot."); return; }
        if (captureBar is not null) { CloseCaptureBar(); return; }
        trayMenu.IsOpen = false;
        DrawingRect display = Native.MonitorAtCursor();
        var bar = new CaptureBarWindow(kind => _ = CaptureAsync(kind, display),
            previousArea is DrawingRect area && Native.Desktop.Contains(area), ToolbarAppearance);
        captureBar = bar;
        bar.Closed += (_, _) => { if (captureBar == bar) captureBar = null; };
        bar.Show(); Native.SetForegroundWindow(new WindowInteropHelper(bar).Handle); bar.Activate();
    }

    private void CloseCaptureBar() { var bar = captureBar; captureBar = null; bar?.Close(); }

    public void ShowTrayMenu()
    {
        CloseCaptureBar();
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
        try { History.SaveSettings(settings); Settings = settings; for (int i = 0; i < settings.Bindings.Length; i++) ((MenuItem)trayMenu.Items[i]).InputGestureText = settings.Bindings[i]; }
        catch { hotkeys.Apply(old); throw; }
        SettingsChanged?.Invoke();
    }

    public IDisposable SuspendShortcuts() => hotkeys?.Suspend() ?? throw new InvalidOperationException("Shortcuts are not ready.");

    public async Task CaptureAsync(CaptureKind kind, DrawingRect? sourceDisplay = null)
    {
        if (Quitting) return;
        if (Recording.IsActive) { home.SetStatus("Stop the recording before taking another screenshot."); return; }
        if (capturing || ComponentDispatcher.IsThreadModal) { pendingCapture = kind; return; }
        CloseCaptureBar();
        capturing = true;
        var visible = Windows.Cast<Window>().Where(w => w.IsVisible).ToList();
        IDisposable? suspension = null;
        bool completed = false;
        try
        {
            suspension = overlays.Suspend();
            trayMenu.IsOpen = false; Mouse.Capture(null);
            DrawingRect display = sourceDisplay ?? Native.MonitorAtCursor();
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
            try { Storage.SaveCapture(image, entry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { warning = (warning is null ? "" : warning + " ") + "Could not save to your screenshot folder. Use Copy or Save, and check Save locations in Settings."; }
            foreach (var editor in visible.OfType<EditorWindow>()) editor.Show();
            AddScreenshotPreview(image, entry, warning);
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
            var image = History.LoadImage(entry);
            AddScreenshotPreview(image, entry);
        }
        catch (Exception ex) { Report(ex); }
    }

    public bool Pin(BitmapSource image, CaptureEntry entry)
    {
        if (pins.Count >= 8) { Report(new InvalidOperationException("Close a pinned capture before adding another. Up to eight captures can be pinned.")); return false; }
        var window = FloatingWindow(image, entry, true);
        var placement = overlayPlacement(); window.ConfigureDock(placement.WorkArea, placement.Scale);
        pins.Add(window); overlays.Track(window); RefreshPreviewStack(); return true;
    }

    private ImageWindow FloatingWindow(BitmapSource image, CaptureEntry? entry, bool pinned, string? warning = null, RecordingResult? recording = null)
    {
        var window = new ImageWindow(this, image, entry, pinned, warning, recording);
        window.Closed += (_, _) => PreviewClosed(window);
        return window;
    }

    public void TogglePin(ImageWindow window)
    {
        if (window.IsPinned)
        {
            pins.Remove(window); window.SetPinned(false);
            if (window.Entry is not null) AdoptScreenshotPreview(window);
            else
            {
                if (previewWorkArea.IsEmpty)
                { var placement = overlayPlacement(); previewWorkArea = placement.WorkArea; previewScale = placement.Scale; }
                recordingPreview?.Close(); recordingPreview = window; RefreshPreviewStack();
            }
        }
        else
        {
            if (pins.Count >= 8) { Report(new InvalidOperationException("Close a pinned capture before adding another. Up to eight captures can be pinned.")); return; }
            if (preview == window) preview = null;
            if (recordingPreview == window) recordingPreview = null;
            if (window.Entry is { } entry)
            {
                var removed = screenshotStack.Remove(entry.Id); if (removed is not null) removed.SuppressEditorRestore = true;
                if (editingScreenshots.ContainsKey(entry.Id)) dismissedWhileEditing.Add(entry.Id);
                screenshotWindows.Remove(entry.Id);
            }
            pins.Add(window); window.SetPinned(true); RefreshPreviewStack();
        }
    }

    public void ReflowPins()
    {
        RefreshPreviewStack();
    }

    public void EditImage(BitmapSource image, CaptureEntry? entry = null)
    {
        try
        {
            var stored = entry is null ? null : History.Entries.FirstOrDefault(e => e.Id == entry.Id);
            ShowEditor(stored is null ? new EditDocument(image) : History.LoadDocument(stored), historyEntry: stored,
                editingItem: entry is null ? null : screenshotStack.Find(entry.Id));
        }
        catch (Exception ex) { Report(ex); }
    }

    public void OpenImageOrProject()
    {
        var dialog = new OpenFileDialog { Title = "Open image or editable project", Filter = "Images and editable projects|*.png;*.jpg;*.jpeg;*.snipsera;*.framelet|PNG and JPEG images|*.png;*.jpg;*.jpeg|Snipsera and legacy projects|*.snipsera;*.framelet", CheckFileExists = true };
        var owner = Windows.Cast<Window>().FirstOrDefault(w => w.IsActive) ?? home;
        if (dialog.ShowDialog(owner) != true) return;
        try
        {
            bool project = AppIdentity.IsProject(dialog.FileName);
            var document = project ? EditFiles.LoadProject(dialog.FileName) : new EditDocument(EditFiles.LoadImage(dialog.FileName));
            ShowEditor(document, Path.GetFileNameWithoutExtension(dialog.FileName), project ? dialog.FileName : null);
        }
        catch (Exception ex) { Report(ex); }
    }

    private void ShowEditor(EditDocument document, string name = "Capture", string? projectPath = null, CaptureEntry? historyEntry = null, PendingScreenshot? editingItem = null)
    {
        var editor = CreateEditor(document, name, projectPath, historyEntry, editingItem);
        editor.Show(); editor.Activate();
    }

    internal EditorWindow CreateEditor(EditDocument document, string name = "Capture", string? projectPath = null, CaptureEntry? historyEntry = null, PendingScreenshot? editingItem = null)
    {
        bool saved = false;
        var editorItem = editingItem;
        string? editingId = editingItem?.Entry.Id;
        if (editingId is not null)
        {
            if (!editingScreenshots.ContainsKey(editingId)) dismissedWhileEditing.Remove(editingId);
            editingScreenshots[editingId] = editingScreenshots.GetValueOrDefault(editingId) + 1; RefreshPreviewStack();
        }
        void Persist(EditDocument edited)
        {
            historyEntry = History.SaveEdits(edited, historyEntry); saved = true;
            if (editorItem is not null && editorItem.Entry.Id != historyEntry.Id)
            {
                string previousId = editorItem.Entry.Id;
                var replacement = new PendingScreenshot(historyEntry) { SuppressEditorRestore = editorItem.SuppressEditorRestore };
                if (OwnsPending(editorItem)) { screenshotStack.Remove(previousId); screenshotStack.Add(replacement, promote: false); }
                else screenshotStack.ReplaceDismissed(editorItem, replacement);
                if (editingId == previousId)
                {
                    int count = editingScreenshots.GetValueOrDefault(previousId);
                    if (count <= 1) editingScreenshots.Remove(previousId); else editingScreenshots[previousId] = count - 1;
                    editingId = historyEntry.Id; editingScreenshots[editingId] = editingScreenshots.GetValueOrDefault(editingId) + 1;
                    if (dismissedWhileEditing.Contains(previousId)) dismissedWhileEditing.Add(editingId);
                }
                editorItem = replacement;
            }
            home?.RefreshHistory();
            try { Storage.SaveCapture(EditRenderer.Render(edited), historyEntry); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { home?.SetStatus("Edits are in history, but the screenshot folder could not be updated. Check Save locations in Settings.", true); }
        }
        EditState? dismissedState = null;
        var editor = new EditorWindow(document, image =>
        {
            Persist(document);
            if (Pin(image, historyEntry!) && editorItem is not null)
            { editorItem.SuppressEditorRestore = true; screenshotStack.Remove(editorItem.Entry.Id); RefreshPreviewStack(); }
        }, OpenImageOrProject, name, projectPath, Persist, () => Storage.ImageFolder,
            state =>
            {
                if (!PreviewPreferences.DismissAfterSharing || state != document.State || historyEntry is null || !HasHistory(historyEntry)) return;
                dismissedState = state;
                if (editorItem is not null && ReferenceEquals(screenshotStack.Find(editorItem.Entry.Id), editorItem))
                    DismissPendingScreenshot(editorItem.Entry.Id, manual: false);
            }, needsInitialHistorySave: historyEntry is null || !HasHistory(historyEntry));
        editor.Closed += (_, _) =>
        {
            bool explicitlyDismissed = editingId is not null && dismissedWhileEditing.Contains(editingId);
            if (editingId is not null && editingScreenshots.TryGetValue(editingId, out int count))
            { if (count <= 1) { editingScreenshots.Remove(editingId); dismissedWhileEditing.Remove(editingId); } else editingScreenshots[editingId] = count - 1; }
            if (Quitting) return;
            if (explicitlyDismissed || editorItem?.SuppressEditorRestore == true) { RefreshPreviewStack(); return; }
            if (PreviewPreferences.DismissAfterSharing && dismissedState == document.State && historyEntry is not null && HasHistory(historyEntry))
            {
                if (editorItem is not null && ReferenceEquals(screenshotStack.Find(editorItem.Entry.Id), editorItem)) DismissPendingScreenshot(editorItem.Entry.Id, manual: false);
                RefreshPreviewStack();
                return;
            }
            if (saved && historyEntry is not null) editorItem = AddScreenshotPreview(History.LoadImage(historyEntry), historyEntry, promote: false);
            else RefreshPreviewStack();
        };
        return editor;
    }

    public bool SaveImage(BitmapSource image, Window owner)
    {
        var dialog = new SaveFileDialog { Title = "Save capture", Filter = "PNG image (*.png)|*.png", DefaultExt = ".png", AddExtension = true,
            FileName = "Snipsera-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".png", InitialDirectory = Storage.ImageFolder };
        if (dialog.ShowDialog(owner) != true) return false;
        try { CaptureService.Save(image, dialog.FileName); home.SetStatus("Capture saved"); return true; }
        catch (Exception ex) { Report(ex); return false; }
    }

    public void Report(Exception exception)
    {
        string message = exception is System.ComponentModel.Win32Exception ? "Windows could not read the display. Unlock your desktop and try again." : exception.Message;
        if (home is not null) home.SetStatus(message, true);
        if (home?.IsVisible != true) ConfirmWindow.Ask(Windows.Cast<Window>().FirstOrDefault(w => w.IsActive), "Snipsera", message, "OK");
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
