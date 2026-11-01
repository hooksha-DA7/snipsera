# Phase 1 verification — 2026-10-30

## Environment and result

Native Release build: **0 warnings, 0 errors**. Automated harness: **31 passed, 0 failed**, recorded in `verification/runs/core-final/results.json` in the development project.

Tested on Windows x64, OS build **26300.9711**, with .NET SDK **10.0.403** and Windows Desktop Runtime **10.0.14**. The SDK was downloaded into the project and its SHA512 matched Microsoft's release metadata. The live display capture was **2560 × 1440**. This is a local development/acceptance build, not a signed public release.

## Automated checks

- Valid/invalid hotkeys, normalized parsing and distinct defaults.
- Scaled-coordinate conversion, negative desktop origins, reverse drags, clamping and tiny/off-screen rejection.
- Exact pixel cropping, PNG pixel round trip, released file handles, malformed PNG refusal.
- Locked export destination preserves the previous file and removes temporary output.
- History survives restart; damaged primary/backup index recovery preserves the damaged index; interrupted-write orphan PNG recovery.
- Invalid image identifiers cannot escape the capture directory.
- Failed index commits preserve previous entries/images.
- Retention keeps 100 captures and leaves unrelated files alone; removal/reload and missing-image handling.
- Invalid settings fall back with a notice; valid settings survive restart.
- Actual GDI capture of the blue/yellow fixture, checked against expected pixel colors.
- Clipboard image round trip with synthetic pixel equality.
- Actual Windows shortcut conflict from a second native thread; prior bindings restored.
- 80 repeated native captures with bounded GDI handle growth.

The first run exposed an invalid .NET screen-copy operation enum combination. Replacing it with native BitBlt fixed the pixel-copy path. The final harness passes on the corrected implementation.

## Desktop acceptance

Tests used an isolated `--data-dir` beneath `verification/runs/ui-01`, not the default user history.

| Workflow | Observed result |
| --- | --- |
| Home and history UI | Capture controls, shortcut badges, empty state and saved thumbnails inspected. |
| Global area shortcut and drag | Opens selection, captures the synthetic card, shows preview, writes Area history. Final-build test produced 1250 × 750 PNG. |
| Cancel selection | Esc restores preview; history count remains 4 before and after cancellation. |
| Copy | Preview reports Copied to clipboard. Exact clipboard pixel fidelity is additionally covered by the harness. |
| Save | Native Save dialog exported `saved-from-pin.png`; SHA256 exactly matches its source history PNG. |
| Pin | Separate resizable capture window appears above the desktop, with Copy/Save/History controls. |
| Restart/history | Captures from the earlier process reopen in the new process; Open restores the floating preview. |
| Window picker/capture | Selected fixture highlighted correctly after excluding transparent overlays. Captured Window PNG and preview: 809 × 567. |
| Full display | Display PNG and preview: 2560 × 1440, including the taskbar; Framelet's home and preview are excluded. |
| Repeat area shortcut | Adds PreviousArea entry at the same 1250 × 750 dimensions as the immediately preceding Area entry. |

Desktop testing also identified utility-overlay windows competing with real window targets. The final picker excludes input-transparent/nonactivating windows and was retested successfully against the fixture. Selection/preview/pins now use targetable top-level windows. Coordinate input was used where the desktop helper returned stale accessibility geometry for floating windows. The earlier app-access timeout was resolved; it is not an outstanding verification blocker.

Synthetic captures remain in the development verification folder and are excluded from both release archives. Incidental desktop content observed by the desktop helper is not bundled into the app.

## Package verification procedure

`publish.ps1` builds and stages explicit runtime/source allowlists, creates per-file SHA256 manifests and separate ZIPs. Verify each ZIP can be fully read, has no unsafe paths/duplicate entries, and matches its manifest. Compare packaged runtime/source hashes with the built/source files, then extract into a fresh directory and launch with a fresh isolated data directory. Record results in `verification/package-results.json` and keep that receipt with the project.

## Coverage limits

The harness is native and meaningful, but it does not certify every application/GPU/display combination. Mixed-DPI multi-monitor hardware, negative-position monitors on real hardware, HDR, protected video, exclusive fullscreen games, secure desktops, touch input, screen readers, Windows 10 and ARM64 need separate acceptance testing. The scaled/negative-origin assertions are geometry tests rather than proof of those physical setups.

The tray menu and eight-pin limit are implemented; every tray action and the ninth-pin rejection were not individually exercised through the desktop helper. No deletion click was automated against the desktop history; retention and removal were verified in isolated native tests. No automatic Windows startup, installer, cloud sharing, annotations, scrolling capture, OCR or video recording are claimed.
