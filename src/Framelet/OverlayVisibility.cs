using System;
using System.Collections.Generic;
using System.Linq;

namespace Framelet;

// Visibility intent is separate from capture suspension and temporary hiding. Windows keep their identity and geometry.
internal sealed class OverlayVisibility<T>(Action<T, bool> present) where T : class
{
    private sealed class State(bool desired) { internal bool Desired = desired, Presented; }
    private readonly Dictionary<T, State> items = [];
    private int suspensions, updates;
    private bool syncing, resync;
    public bool Hidden { get; private set; }
    public bool Suspended => suspensions > 0;
    public T[] Desired => items.Where(p => p.Value.Desired).Select(p => p.Key).ToArray();
    public int HiddenCount => Hidden ? items.Count(p => p.Value.Desired) : 0;
    public event Action? Changed;

    public void Track(T item)
    {
        if (items.ContainsKey(item)) { SetDesired(item, true); return; }
        items.Add(item, new State(true)); Sync();
    }
    public void Forget(T item)
    {
        if (!items.Remove(item)) return;
        Normalize(); Sync();
    }
    public void SetDesired(T item, bool visible)
    {
        if (!items.TryGetValue(item, out var state) || state.Desired == visible) return;
        state.Desired = visible; Normalize(); Sync();
    }
    public void Hide()
    {
        if (Hidden || !items.Any(p => p.Value.Desired)) return;
        Hidden = true; Sync();
    }
    public void Restore()
    {
        if (!Hidden) return;
        Hidden = false; Sync();
    }
    public IDisposable Suspend()
    {
        suspensions++;
        try { Sync(); }
        catch { suspensions--; Normalize(); Sync(); throw; }
        return new Scope(this, true);
    }
    public IDisposable BeginUpdate()
    {
        updates++; return new Scope(this, false);
    }
    private void Resume()
    {
        suspensions--; Normalize(); Sync();
    }
    private void Normalize()
    {
        // Preserve the mode while a capture replaces its preview inside a suspension scope.
        if (!Suspended && updates == 0 && !items.Any(p => p.Value.Desired)) Hidden = false;
    }
    private void Sync()
    {
        if (updates > 0) return;
        if (syncing) { resync = true; return; }
        syncing = true;
        try
        {
            do
            {
                resync = false;
                foreach (var pair in items.ToArray())
                {
                    if (!items.TryGetValue(pair.Key, out var state) || state != pair.Value) continue;
                    bool show = state.Desired && !Hidden && !Suspended;
                    if (state.Presented == show) continue;
                    present(pair.Key, show); state.Presented = show;
                }
            } while (resync);
        }
        finally { syncing = false; }
        Changed?.Invoke();
    }
    private sealed class Scope(OverlayVisibility<T> owner, bool suspend) : IDisposable
    {
        private OverlayVisibility<T>? pending = owner;
        public void Dispose()
        {
            var value = pending; pending = null;
            if (value is null) return;
            if (suspend) value.Resume();
            else { value.updates--; value.Normalize(); value.Sync(); }
        }
    }
}
