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
    private readonly List<int> active = [];
    private AppSettings? previous;
    private int suspensions;
    private bool disposed;
    private static readonly CaptureKind[] kinds = [CaptureKind.Area, CaptureKind.Window, CaptureKind.Screen, CaptureKind.PreviousArea];

    public HotkeyService(IntPtr handle, Action<CaptureKind> capture)
    {
        source = HwndSource.FromHwnd(handle) ?? throw new InvalidOperationException("Capture window is not ready.");
        this.capture = capture;
        source.AddHook(HandleMessage);
    }

    public void Apply(AppSettings settings)
    {
        Hotkey[] keys = settings.Bindings.Select(Hotkey.Parse).ToArray();
        if (keys.Distinct().Count() != keys.Length) throw new InvalidOperationException("Each capture action needs a different shortcut.");
        Unregister();
        try { if (suspensions == 0) Register(keys); previous = settings; }
        catch
        {
            Unregister();
            if (previous is not null && suspensions == 0)
            {
                try { Register(previous.Bindings.Select(Hotkey.Parse).ToArray()); }
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
            try { value.Register(value.previous.Bindings.Select(Hotkey.Parse).ToArray()); }
            catch { value.Unregister(); throw; }
        }
    }

    private void Register(Hotkey[] keys)
    {
        for (int index = 0; index < keys.Length; index++)
        {
            int id = 3100 + index;
            if (!Native.RegisterHotKey(source.Handle, id, keys[index].Modifiers | 0x4000, keys[index].VirtualKey))
                throw new InvalidOperationException($"Shortcut {index + 1} is already in use or could not be registered (Windows error {Marshal.GetLastWin32Error()}). "
                    + (keys[index].VirtualKey == 0x2C ? "If Windows opens Snipping Tool for Print Screen, turn off that option under Settings > Accessibility > Keyboard, then save again. " : "Choose another shortcut. ")
                    + "Capture buttons still work.");
            active.Add(id);
        }
    }

    private IntPtr HandleMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == 0x0312)
        {
            int index = wParam.ToInt32() - 3100;
            if (index >= 0 && index < kinds.Length && active.Contains(wParam.ToInt32()))
            { handled = true; var kind = kinds[index]; source.Dispatcher.BeginInvoke(() => { if (!disposed && suspensions == 0) capture(kind); }); }
        }
        return IntPtr.Zero;
    }

    private void Unregister() { foreach (int id in active) Native.UnregisterHotKey(source.Handle, id); active.Clear(); }
    public void Dispose() { if (disposed) return; disposed = true; Unregister(); source.RemoveHook(HandleMessage); }
}
