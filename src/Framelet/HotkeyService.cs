using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Framelet;

public sealed class HotkeyService : IDisposable
{
    private readonly HwndSource source;
    private readonly Action<CaptureKind> capture;
    private readonly Action openCaptureBar;
    private readonly Func<int, Hotkey, bool> register;
    private readonly Action<int> unregister;
    private readonly List<int> active = [];
    private Hotkey[]? previous;
    private int suspensions;
    private bool disposed;
    private static readonly CaptureKind[] kinds = [CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.PreviousArea];
    private static readonly string[] names = ["Area", "Window", "Display", "Repeat last area", "All-in-one capture"];

    public HotkeyService(IntPtr handle, Action<CaptureKind> capture, Action openCaptureBar)
        : this(handle, capture, openCaptureBar,
            (id, key) => Native.RegisterHotKey(handle, id, key.Modifiers | 0x4000, key.VirtualKey),
            id => Native.UnregisterHotKey(handle, id)) { }

    internal HotkeyService(IntPtr handle, Action<CaptureKind> capture, Action openCaptureBar,
        Func<int, Hotkey, bool> register, Action<int> unregister)
    {
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Capture window is not ready.");
        this.capture = capture; this.openCaptureBar = openCaptureBar;
        this.register = register; this.unregister = unregister;
        source.AddHook(HandleMessage);
    }

    public void Apply(AppSettings settings, bool keepDirectOnLauncherFailure = false)
    {
        Hotkey[] keys = settings.Bindings.Select(Hotkey.Parse).ToArray();
        if (disposed) throw new ObjectDisposedException(nameof(HotkeyService));
        for (int i = 0; i < keys.Length; i++)
            for (int j = 0; j < i; j++)
                if (keys[i] == keys[j]) throw new InvalidOperationException($"{names[i]} and {names[j]} use the same shortcut. Change one of them, then save again.");
        Unregister();
        try { if (suspensions == 0) Register(keys); previous = keys; }
        catch
        {
            // A newly added launcher must not disable the four working direct shortcuts on startup.
            if (keepDirectOnLauncherFailure && previous is null && active.Count == kinds.Length)
            { previous = keys.Take(kinds.Length).ToArray(); throw; }
            Unregister();
            if (previous is not null && suspensions == 0)
            {
                try { Register(previous); }
                catch { Unregister(); }
            }
            throw;
        }
    }

    public IDisposable Suspend()
    {
        if (disposed) throw new ObjectDisposedException(nameof(HotkeyService));
        if (suspensions++ == 0) Unregister();
        return new Suspension(this);
    }

    private sealed class Suspension(HotkeyService owner) : IDisposable
    {
        private HotkeyService? service = owner;
        public void Dispose()
        {
            if (service is not { } value) return;
            service = null;
            if (--value.suspensions != 0 || value.disposed || value.previous is null) return;
            try { value.Register(value.previous); }
            catch { value.Unregister(); throw; }
        }
    }

    private void Register(Hotkey[] keys)
    {
        for (int index = 0; index < keys.Length; index++)
        {
            int id = 3100 + index;
            if (!register(id, keys[index]))
                throw new InvalidOperationException($"{names[index]} shortcut is already in use or could not be registered (Windows error {Marshal.GetLastWin32Error()}). "
                    + (keys[index].VirtualKey == 0x2C ? "If Windows opens Snipping Tool for Print Screen, turn off that option under Settings > Accessibility > Keyboard, then save again. " : "Choose another shortcut. ")
                    + "Capture buttons still work.");
            active.Add(id);
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            handled = DispatchShortcut(wParam.ToInt32());
        }
        return IntPtr.Zero;
    }

    internal bool DispatchShortcut(int id)
    {
        int index = id - 3100;
        if (disposed || suspensions > 0 || index < 0 || index >= names.Length || !active.Contains(id)) return false;
        source.Dispatcher.BeginInvoke(() =>
        {
            if (disposed || suspensions > 0 || !active.Contains(id)) return;
            if (index == kinds.Length) openCaptureBar(); else capture(kinds[index]);
        });
        return true;
    }

    private void Unregister() { foreach (int id in active) unregister(id); active.Clear(); }
    public void Dispose() { if (disposed) return; disposed = true; Unregister(); source.RemoveHook(HandleMessage); }
}
