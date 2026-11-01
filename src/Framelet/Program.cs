using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Framelet");
        int dataOption = Array.IndexOf(args, "--data-dir");
        if (dataOption >= 0)
        {
            if (dataOption + 1 >= args.Length) { MessageBox.Show("Provide a folder after --data-dir.", "Framelet"); return; }
            root = Path.GetFullPath(args[dataOption + 1]);
        }
        string identity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..20];
        using var mutex = new Mutex(true, "Local\\Framelet-" + identity, out bool first);
        if (!first)
        {
            IntPtr existing = FindWindow(null, "Framelet");
            if (existing != IntPtr.Zero) PostMessage(existing, 0x8042, IntPtr.Zero, IntPtr.Zero);
            return;
        }
        try { new CaptureApp(root).Run(); }
        catch (Exception ex) { MessageBox.Show("Framelet could not start. Check access to its data folder.\n\n" + ex.Message, "Framelet", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern IntPtr FindWindow(string? className, string title);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}
