using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace Framelet;

// Records a shortcut from the keys actually pressed. A read-only Fluent text box keeps the Windows 11 look and exposes the chord
// to UI Automation as its value. While it has keyboard focus the registered capture shortcuts are suspended, so pressing the
// current chord records it instead of starting a capture. Tab and Shift+Tab move focus; Escape cancels a pending change.
public sealed class ShortcutRecorder : TextBox
{
    public const string Rule = "Use Print Screen, or Ctrl and/or Alt with a letter, number or F1–F11. Shift is optional.";
    private readonly Func<IDisposable> suspend;
    private readonly Action<Exception> report;
    private IDisposable? suspension;
    private string shortcut;
    // Set when Print Screen's key-down was recorded, so its key-up does not re-record after the modifiers were released.
    private bool printScreenDown;

    public ShortcutRecorder(string name, string saved, Func<IDisposable> suspend, Action<Exception> report)
    {
        this.suspend = suspend; this.report = report; Saved = shortcut = saved;
        IsReadOnly = true; IsReadOnlyCaretVisible = false; IsUndoEnabled = false; AllowDrop = false; ContextMenu = null; Cursor = Cursors.Arrow;
        // Implicit Fluent styles match exact types; this derived text box explicitly follows the system TextBox style.
        SetResourceReference(StyleProperty, typeof(TextBox));
        InputMethod.SetIsInputMethodEnabled(this, false);
        AutomationProperties.SetName(this, name + " shortcut");
        AutomationProperties.SetHelpText(this, "Press the keys for this shortcut, then select Save shortcuts. Escape cancels a change.");
        ToolTip = "Press the keys for this shortcut"; Show();
    }

    // The last saved binding, and the pending one shown in the box.
    public string Saved { get; private set; }
    public string Shortcut => shortcut;
    public bool IsPending => !string.Equals(shortcut, Saved, StringComparison.OrdinalIgnoreCase);
    public event Action<ShortcutRecorder>? Changed;
    public event Action<string>? Rejected;

    public void MarkSaved(string value) { Saved = shortcut = value; Show(); }
    public void Cancel() { if (!IsPending) return; shortcut = Saved; Show(); Changed?.Invoke(this); }

    // Restores the capture shortcuts. Safe to call repeatedly; also used when the owning window closes, hides or deactivates.
    public void Release()
    {
        printScreenDown = false;
        var scope = suspension; suspension = null;
        try { scope?.Dispose(); } catch (Exception ex) { report(ex); }
    }
    // Suspends the capture shortcuts again after the window is reactivated, in case focus returned without a new focus event.
    public void Resume() { if (IsKeyboardFocused) Acquire(); }
    internal bool IsSuspending => suspension is not null;

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnGotKeyboardFocus(e); Acquire(); SelectAll(); }
    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e) { base.OnLostKeyboardFocus(e); Release(); Show(); }

    protected override void OnPreviewKeyDown(KeyEventArgs e) { if (Press(Actual(e), Keyboard.Modifiers, false)) e.Handled = true; else base.OnPreviewKeyDown(e); }
    // Handling Alt's key-up also keeps the window menu from opening.
    protected override void OnPreviewKeyUp(KeyEventArgs e) { if (Press(Actual(e), Keyboard.Modifiers, true)) e.Handled = true; else base.OnPreviewKeyUp(e); }
    protected override void OnPreviewTextInput(TextCompositionEventArgs e) => e.Handled = true;

    // Applies one key event with the modifiers held at that moment. Returns false when the key is left to normal handling (Tab).
    internal bool Press(Key key, ModifierKeys modifiers, bool released)
    {
        if (released)
        {
            if (key == Key.Tab) return false;
            // Windows often sends Print Screen only as a key-up. When its key-down was already recorded, keep that result, which
            // has the modifiers that were held when it was pressed.
            if (key == Key.PrintScreen) { if (printScreenDown) printScreenDown = false; else Record(key, modifiers); }
            else if (IsModifier(key)) { if (modifiers == ModifierKeys.None) Show(); else Partial(modifiers); }
            return true;
        }
        // Tab keeps normal keyboard navigation; it is never a supported shortcut key.
        if (key == Key.Tab && (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0) { Show(); return false; }
        if (IsModifier(key)) { Partial(modifiers); return true; }
        printScreenDown = key == Key.PrintScreen;
        if (key == Key.Escape && modifiers == ModifierKeys.None) Cancel(); else Record(key, modifiers);
        return true;
    }

    private void Acquire()
    {
        if (suspension is not null) return;
        try { suspension = suspend(); } catch (Exception ex) { report(ex); }
    }
    private void Record(Key key, ModifierKeys modifiers)
    {
        string? value = Hotkey.FromKey(key, modifiers);
        if (value is null) { Show(); Rejected?.Invoke(Rule); return; }
        bool changed = !string.Equals(value, shortcut, StringComparison.OrdinalIgnoreCase);
        shortcut = value; Show();
        if (changed) Changed?.Invoke(this);
    }
    // Live feedback while only modifiers are held, for example "Ctrl+Alt+…".
    private void Partial(ModifierKeys held)
    {
        string text = (held.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (held.HasFlag(ModifierKeys.Alt) ? "Alt+" : "") + (held.HasFlag(ModifierKeys.Shift) ? "Shift+" : "");
        if (text.Length == 0) { Show(); return; }
        Text = text + "…"; CaretIndex = Text.Length;
    }
    private void Show()
    {
        Text = shortcut; if (IsKeyboardFocused) SelectAll();
        AutomationProperties.SetItemStatus(this, IsPending ? "Unsaved change" : "");
    }

    private static Key Actual(KeyEventArgs e) => e.Key switch { Key.System => e.SystemKey, Key.ImeProcessed => e.ImeProcessedKey, Key.DeadCharProcessed => e.DeadCharProcessedKey, _ => e.Key };
    private static bool IsModifier(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;
}
