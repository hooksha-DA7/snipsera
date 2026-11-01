# On-image capture controls — 0.5.2

Version 0.5.2 returns the floating controls to the capture itself, replacing 0.5.1's external hover toolbar.

## Interaction

Normal previews and pins use the same arrangement: Pin/Unpin at top left, Close at top right, Annotate at bottom left, Drag at bottom right, and labeled Copy/Save pills in the center. The buttons appear on hover or Tab and dismiss after pointer departure. Copy gives brief feedback. Pin is a direct toggle with an accent state; it does not open options. Right-click opens the themed options menu, including opacity.

The preview remains at the bottom left. Pin/Unpin retains the same window and displayed size. Preview and pins share a 240 × 166 logical-pixel initial cap including shadow, preserving source proportions. Up to eight docked pins reflow above the reserved preview space. These backend corrections from 0.5.1 are retained.

Controls adapt to available image space: labeled pills first, compact icon controls when needed, then a centered on-image row or column with More for folded actions. Controls can use the existing shadow margin on extremely shallow/slender captures; revealing them does not enlarge or reshape the image frame. The output image never contains the controls, shade, frame, shadow or display opacity.

Drag never folds away. A 400 × 2400 pin keeps Pin, Copy, Drag, More and Close visible, with Annotate and Save in More. If a pin is manually resized so small that Drag plus More cannot fit, Drag remains and the other actions are available by right-click. Right-click also offers Copy and Save alongside the existing commands. No modifier-based drag behavior or backend export change was needed.

## Visual scope

Rounded, translucent themed control surfaces and a subtle image shade provide contrast over the source image. The source thumbnail remains sharp. This is compositing within the image window, not desktop Acrylic. Main history/editor windows retain their existing Fluent controls and Windows caption behavior. Annotation rendering and editor tool layouts are outside this correction.

High-contrast, Windows transparency and reduced-motion policies remain part of the design. Theme brushes follow the app's existing preference bindings. The implementation is independent Framelet code; vendor artwork, the CleanShot installer and reference media are excluded from release packages.

## Implementation and evidence

The update changes the floating controls and shared color/icon helpers, with supporting C# integration fixes. CaptureApp, FloatingLayout, capture/hotkey services, history storage, annotation rendering and project formats remain unchanged by this correction.

Native inspection found a Fluent pointer-over state that replaced the button material, making Copy text hard to read over a light capture. The button material was corrected before release.

The corrected button template retains its material during pointer-over/press, applies a subtle interaction tint, and keeps the pinned accent. Circles and pills derive their radius from their height. A rounded two-tone focus indicator appears for keyboard navigation. This style is scoped to floating image controls; it does not replace the app-wide Fluent style.

## Verification

- Release build: zero warnings and errors. All **71 core checks passed**, with zero failures. The separate capture-lifecycle integration test also passed. Detailed receipts are retained in the development checkout.
- Six source profiles (300 × 240, 1551 × 574, 1187 × 547, 3000 × 200, 400 × 2400 and 100 × 100) pass checks for controls inside the window, no overlapping action hit targets, unchanged frame bounds, available Drag export and unchanged source pixels. Hiding the image also hides its actions.
- The lifecycle integration invokes Pin and Unpin, checking the same HWND and displayed size, the Unpin accessible label and clearance from the reflowed pin stack. It verifies six visible actions and labeled Copy/Save on a normal capture, repeated capture with the old preview open, preservation of a dirty editor and pin, recovery after a modal dialog, and tray-menu focus/History behavior.
- Native Windows inspection at **125% scaling** confirmed readable resting and hover surfaces in light and dark app themes, preserved pinned accent on hover, direct Pin/Unpin without a menu, Copy feedback, rounded keyboard focus on a circle and pill, and the portrait More menu containing Annotate and Save.
- Native Save produced a PNG byte-for-byte equal to the 1551 × 574 synthetic original. Native drag from the 400 × 2400 portrait pin's Drag button delivered a full-resolution PNG to a separate synthetic drop-target window. Test receipts and screenshots are retained in the development checkout.
- Automated checks retain native screenshot pixels, clipboard, hotkey-conflict handling, GDI resource checks, annotation/project compatibility, history, opacity/motion policy and geometry coverage at simulated 100%, 125%, 150% and 200% scales.

These checks do not certify mixed-DPI multi-monitor hardware, native text rendering at 150% scaling, every destination app, or every Windows contrast theme. High-contrast policy is covered by existing automated checks and the new template was reviewed in source; Windows contrast settings were not changed for a native acceptance pass. A held pressed-state screenshot was not captured. User acceptance of the visual design remains separate from these functional checks.

Run `./test.ps1 -Output verification/runs/<fresh-name>` on an unlocked Windows desktop. For native review, use the checks executable with `--floating-fixture "<fresh-output-directory>"` or `--floating-fixture-light "<fresh-output-directory>"`. The fixture uses separate synthetic history/settings and supplies F9 to record native evidence. Close development fixtures before rebuilding.

Release packages contain maintained source, documentation and the application, excluding test output, development logs, reference media, credentials and user history. Quit an older Framelet using its tray menu before starting 0.5.2, so hotkeys are available. No Windows restart is required.
