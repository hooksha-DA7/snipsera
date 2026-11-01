namespace Framelet;

// The Windows wheel equivalent of the reference's downward swipe. No native input or UI state is read here.
internal sealed class OverlayHideGesture
{
    private long pending, last;
    internal void Reset() { pending = 0; last = 0; }
    internal bool Push(int delta, long now, bool allowed)
    {
        if (!allowed || delta >= 0) { Reset(); return false; }
        if (now < last || now - last > 400) pending = 0;
        last = now; pending -= (long)delta;
        if (pending < 120) return false;
        Reset(); return true;
    }
}

internal sealed class OverlayRestoreHover
{
    internal bool Armed { get; private set; }
    internal void Reset(bool pointerKnown = false, bool pointerInside = true) { Armed = pointerKnown && !pointerInside; }
    internal void Observe(bool pointerKnown, bool pointerInside) { if (pointerKnown && !pointerInside) Armed = true; }
    internal bool CanRestore(bool pointerOver, bool buttonPressed, bool allowed) => Armed && pointerOver && !buttonPressed && allowed;
}
