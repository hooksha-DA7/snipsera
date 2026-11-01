# Floating image sizing and Pin/Unpin — 0.5.1

Historical release notes: the user rejected the external toolbar introduced here. Version 0.5.2 restores [on-image controls](ON-IMAGE-CONTROLS.md), retaining the sizing and same-window Pin/Unpin corrections below.

This update addresses the user's two 2026-10-31 screenshots showing wide captures, crowded hover overlays and a pin icon that opened a menu.

## Confirmed causes and changes

The layout used different limits for previews (240 × 166 logical pixels) and pins (256 × 192). It also imposed minimum image width and height separately, adding empty space around wide or tall images. Controls shared the image's clipped surface, so the central Copy/Save pills and corner buttons could crowd one another on short captures. The pin icon was explicitly wired to open options once pinned.

Preview and pins now share the 240 × 166 limit, including shadow space, and fit the source aspect ratio without independent image minimums. Pin toggles the existing window rather than creating and hiding another one. Its action and icon change to Unpin, which returns the same-sized image to the bottom-left preview area. The stack reflows above the reserved preview space. Crowded stacks may still fit smaller cards; manually resized/moved pins remain independent.

All six actions sit in one compact rounded hover toolbar outside the image: Pin/Unpin, Annotate, Copy, Save, Drag and Close. The image stays sharp; showing controls changes neither its frame nor its pixels. The toolbar can extend beyond a narrow or shallow image without being clipped. WPF positions it at the screen edge as needed. A short delay lets the pointer cross the gap. Tab opens it for keyboard use and cycles the actions. Escape dismisses it before closing the image on a subsequent press. Image instructions use accessibility help text rather than a large tooltip over the capture.

Right-click retains the Fluent options menu, including pin opacity. The pin button itself does not open it. Unpin replaces any existing preview, whose source remains in history. Copy, save and drag retain full source resolution; this change only affects display and window ownership. Projects remain version 3; history/settings retain their existing format.

## Verification and reproduction

- Release build has zero warnings/errors. 71 core checks pass, with zero failures, plus the separate capture lifecycle integration test.
- Geometry tests use 1551 × 574 and 1187 × 547 (the sizes visible in the supplied screenshots), 3000 × 200, 400 × 2400 and square images. They check matching preview/single-pin sizes and aspect-preserving frames at 100%, 125%, 150% and 200% scales. Existing one-to-eight-pin tests still verify bounds, nonoverlap and reserved preview space.
- Native integration clicks the actual toolbar button, confirms the same HWND and size across Pin/Unpin, verifies Unpin text, six unclipped controls, hiding the popup with its parent, and separation from the reflowed stack. Existing rapid-capture, dirty-editor/pin preservation, modal-dialog recovery, tray persistence, clipboard, drag-payload and native-pixel checks remain.
- The desktop fixture supplies wide and portrait synthetic images. Manual desktop inspection confirmed the compact toolbar and direct Pin/Unpin interaction. Testing exposed a stack overlap on Unpin; it was corrected by returning the preview to its reserved area, then covered by the integration test.
- Native drag from the toolbar successfully delivered a full-resolution 400 × 2400 PNG to a separate synthetic drop-target window. The toolbar remains available during a held drag, then dismisses after pointer departure; explicit keyboard navigation retains focus access.

Run `./test.ps1 -Output verification/runs/<fresh-name>` on an unlocked Windows desktop. Core and integration receipts for this release are under `verification/runs/floating-final-verified`. Launch the checks executable with `--floating-fixture "<fresh-output-directory>"` for a native visual fixture. It uses synthetic images and separate history/settings; test screenshots and fixtures are excluded from release ZIPs.

The regression pass also caught a tray-focus issue when moving from the new popup: assigning native focus after WPF had entered keyboard menu mode could immediately close the menu. The tray path now closes floating actions and obtains native focus before enabling keyboard focus. Its integration check starts with an open floating toolbar and verifies that the tray remains open with keyboard focus before executing History.

This is desktop verification on the current Windows machine at 125% scaling plus simulated geometry at other scales. It does not certify mixed-DPI hardware or every destination application. The toolbar has a themed surface; it does not claim desktop Acrylic. Existing user app instances and their unsaved work are preserved: quit the older Framelet from its tray before opening the new portable build.
