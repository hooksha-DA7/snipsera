# All-in-one capture

The capture bar is a single shortcut for Snipsera's existing screenshot modes. It uses the same floating surface, rounded corners, icons and keyboard focus style as the recording controls.

## Open and choose

Press **Ctrl+Alt+Shift+5**, or choose **All-in-one capture** in Home or the tray. Choose **Area**, **Window**, **Display**, **Scroll** or **Last area**. The bar closes before capture begins. Results use the existing preview, Copy, Save, Pin, Annotate and local history workflow.

Display captures the monitor where the bar opened. Last area is unavailable until an area has been selected during this app session. Window and scrolling selection work as they do from their direct capture buttons.

Press **1–5** to select the corresponding mode, or use arrow keys/Tab and Enter. **Esc**, Close, clicking outside, or pressing the launcher shortcut again dismisses the bar without capturing. Drag the grip to move it.

## Use Print Screen

1. Open **Keyboard shortcuts** in Home.
2. Select **All-in-one capture** and press **Print Screen** on the keyboard.
3. If Print Screen already belongs to another capture action, assign a different shortcut to that action.
4. Choose **Save shortcuts**.

Windows or another capture app can reserve Print Screen. Snipsera reports registration failures. If Windows opens Snipping Tool, change **Use the Print Screen key to open screen capture** under **Settings > Accessibility > Keyboard**, then save again. Snipsera does not change that Windows setting.

Upgrading preserves the four existing shortcut assignments. If an older assignment already uses Ctrl+Alt+Shift+5, the new bar gets an unused shortcut from Ctrl+Alt+Shift+5–9. A conflict registering the newly added launcher at startup retains any four direct shortcuts that were successfully registered; change the launcher key to resolve it.

## Verification limits

The focused release checks exercise saved settings and upgrade collisions, fake native shortcut registration/dispatch/rollback, actual bar button callbacks and dismissal, Home's fifth key recorder, and detached light/dark controls at 100%, 125%, 150% and 200% scaling. These checks never register real global shortcuts, capture desktop pixels or show the toolbar on the user's desktop.

For 1.0.4, 195 automated checks passed: 159 C# checks (including 28 capture-bar checks) and 36 Python release checks. The Release build completed without warnings or errors. Narrow layouts were rendered at 360, 210, 150 and 200 logical-pixel work-area widths, with full-size buttons and a scrollable fallback when height is constrained.

Physical Print Screen/global activation, actual mixed-DPI placement and final installed-MSIX screenshot acceptance remain pending while desktop testing is paused. Existing live-capture limitations are unchanged. See [Windows acceptance](WINDOWS-ACCEPTANCE.md) and [release workflow](RELEASING.md).
