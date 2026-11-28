using System;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Framelet;

public sealed partial class CaptureApp
{
    private PreferencesWindow? preferencesWindow;
    private DispatcherTimer? preferencesTimer;
    private void InitializePreferences()
    {
        Preferences.SetOwnerWindow(new System.Windows.Interop.WindowInteropHelper(home).Handle);
        var item = new MenuItem { Header = "Settings", Icon = new UiIcon("Settings", 16) { Ink = Ui.Ink } };
        item.Click += (_, _) => { trayMenu.IsOpen = false; Dispatcher.BeginInvoke(OpenPreferences); };
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, item);
        // Help opens the website's support page; About shows the version and the other public pages.
        var help = new MenuItem { Header = "Help and support", Icon = new UiIcon("Help", 16) { Ink = Ui.Ink } };
        help.Click += (_, _) => { trayMenu.IsOpen = false; Dispatcher.BeginInvoke(() => OpenLink(WebLinks.Support)); };
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, help);
        var about = new MenuItem { Header = "About Snipsera" };
        about.Click += (_, _) => { trayMenu.IsOpen = false; Dispatcher.BeginInvoke(OpenAbout); };
        trayMenu.Items.Insert(trayMenu.Items.Count - 1, about);
        tray!.BalloonTipClicked += (_, _) => Dispatcher.BeginInvoke(OpenPreferences);
        Preferences.Changed += NotifyUpdateWhenIdle;
        preferencesTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        preferencesTimer.Tick += async (_, _) =>
        {
            preferencesTimer.Interval = TimeSpan.FromMinutes(1);
            await Preferences.CheckAutomaticallyAsync();
            NotifyUpdateWhenIdle();
        };
        preferencesTimer.Start();
    }
    public async void OpenPreferences()
    {
        if (Quitting || capturing || Recording.IsActive || ComponentDispatcher.IsThreadModal) return;
        CloseCaptureBar();
        if (preferencesWindow is null)
        {
            var window = new PreferencesWindow(Preferences, this) { Owner = home };
            preferencesWindow = window;
            window.Closed += (_, _) => { if (preferencesWindow == window) preferencesWindow = null; };
        }
        preferencesWindow.Show(); preferencesWindow.Activate();
        await Preferences.RefreshStartupAsync();
    }
    public void OpenKeyboardShortcuts()
    {
        OpenPreferences();
        preferencesWindow?.ShowPage(SettingsPage.KeyboardShortcuts);
    }
    public void OpenAbout()
    {
        OpenPreferences();
        preferencesWindow?.ShowPage(SettingsPage.About);
    }
    internal void OpenLink(string url)
    {
        try { WebLinks.Open(url); }
        catch (Exception ex) { Report(ex); }
    }
    private void NotifyUpdateWhenIdle()
    {
        if (Quitting || capturing || captureBar is not null || Recording.IsActive || ComponentDispatcher.IsThreadModal || TrayMenuOpen || tray is null || Preferences.Update.Checking) return;
        string? message = Preferences.TakeNotification();
        if (message is null) return;
        if (home.IsActive || preferencesWindow?.IsActive == true) return;
        tray.BalloonTipTitle = "Snipsera update available"; tray.BalloonTipText = message;
        try { tray.ShowBalloonTip(8000); } catch (System.ComponentModel.Win32Exception) { /* Home and Settings retain the update notice. */ }
    }
    private void DisposePreferences()
    {
        preferencesTimer?.Stop(); Preferences.Changed -= NotifyUpdateWhenIdle; Preferences.Dispose();
    }
}
