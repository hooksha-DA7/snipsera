# 0.6.0 acceptance checks

Status on 2026-11-02: testing resumed. The current build is still **0.6.0-preview.4**. A final 0.6.0 release has not been published.

## Passed

- Release compilation: zero warnings and errors.
- Full suite: 125 checks passed, with no failures. This includes native synthetic-window capture, clipboard restoration, shortcut-conflict recovery, GDI resource checks, image editing and project compatibility, scrolling logic, overlay state and rendered control layout.
- Capture lifecycle: repeated captures preserve the dirty editor and existing pin; Pin/Unpin keeps the same window and size; capture requests resume after a confirmation dialog; the tray menu retains focus and opens history.
- Native hidden-group integration: two pins restore with the same window handles and physical bounds, the editor stays open, and a new capture remains hidden until restoration. Native visibility flags confirm that the floating images and restore tab are hidden when the capture callback runs. That callback uses synthetic image data; a live pixel-exclusion check is still required.
- Six off-screen restore-tab template checks passed across light, dark and contrast-template profiles.
- Nine release-publishing checks passed.
- Published-preview portable launch: the downloaded 0.6.0-preview.4 archive passed SHA256, ZIP safety/CRC and manifest checks. Its extracted executable reached Ready to capture with empty isolated history. A final-release package still requires its own launch check after staging.

The live checks used separate test history and synthetic content. Existing user captures and settings were not changed. Detailed receipts are retained under `verification/runs/release-060-acceptance-01` and `verification/runs/release-060-acceptance-02` in the development checkout.

## Still required

Manual and Auto scrolling were reported working in the hands-on check. Independent output validation is still pending: the isolated observer has not recorded those captures, so they have not been inspected for stitching continuity.

Independent Chrome output checks, scrolling Cancel, overlay wheel/hover delivery and pixel-level capture exclusion, and the final staged package launch still need acceptance. These checks are required before promoting 0.6.0.

During the scrolling test, the desktop-control capture connection failed while reading the full-screen selector: first `0x8007000E`, then `0x887A0005` (GPU device suspended). The error persisted after reconnecting, including for a small window. This is a verification-tool failure; it does not establish that the application failed or passed those workflows. The selection was canceled safely.

Two display adapters were present. No display or security settings were changed to work around the capture failure. The capture-service error persisted after restarting the desktop client. The remaining checks can also be performed by a tester using the real controls while an observer records capture output and native window state. The observer itself does not drive input.

For an isolated observed session, build `tests/Framelet.Checks` and run `Framelet.Checks --acceptance-fixture <fresh-output-folder>`. It uses the production UI and capture backend, with history under that output folder. It records button actions, wheel events over floating images, restore-tab mouse events and native visibility/bounds; saved captures remain local. Quit from the tray menu after testing so the final summary is written. The `--acceptance-fixture-smoke` variant checks observer startup and shutdown only.

## Packaging corrections

The package lists now include `RELEASES.md` and `BUILD-STATUS.md`, so the current README's local links are available after extraction. Stable staging also requires a matching stable informational version, preventing a preview binary from being packaged under a final-release filename.
