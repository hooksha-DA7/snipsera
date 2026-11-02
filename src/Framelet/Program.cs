using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Framelet;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        if (Array.IndexOf(args, "--fixture") >= 0)
        { var fixtureApp = new Application(); Ui.Install(fixtureApp); fixtureApp.Run(new FixtureWindow()); return; }
        string root = AppIdentity.DataRoot(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        int dataOption = Array.IndexOf(args, "--data-dir");
        if (dataOption >= 0)
        {
            if (dataOption + 1 >= args.Length) { MessageBox.Show("Provide a folder after --data-dir.", AppIdentity.Name); return; }
            root = Path.GetFullPath(args[dataOption + 1]);
        }
        using var mutex = new Mutex(true, AppIdentity.InstanceName(root), out bool first);
        if (!first)
        {
            IntPtr existing = FindWindow(null, AppIdentity.Name);
            if (existing == IntPtr.Zero) existing = FindWindow(null, AppIdentity.LegacyName);
            if (existing != IntPtr.Zero) PostMessage(existing, 0x8042, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        try { new CaptureApp(root).Run(); }
        catch (Exception ex) { MessageBox.Show("Snipsera could not start. Check access to its data folder.\n\n" + ex.Message, AppIdentity.Name, MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
