# Framelet 0.2.0 — Phase 2 verification

Verified October 30, 2026. Native Windows x64 WPF/.NET 10 application; SDK 10.0.403. The Phase 2 Release build has **0 warnings and 0 errors**. The final native suite has **55 passed, 0 failed** in `verification/runs/phase2-core-final/results.json` in the development project.

## New behavior

The capture preview and pins have an Edit action, and home opens PNG/JPEG images or `.framelet` projects. The editor includes arrows, lines, shapes, text, counters, highlighting, freehand, selection/movement, restyling, removal, crop, resize, backgrounds/padding, view zoom, blur/pixelation/solid masks, undo/redo, projects, PNG/JPEG export, copy and pin.

Editor windows retain their documents when capture hides and restores them. Source history PNGs and the Phase 1 history/settings schema remain unchanged. The new portable/source packages are version 0.2.0; the existing 0.1.0 archives are preserved as rollback/reference artifacts. Close an older running copy first to release its hotkeys.

## Automated evidence

All 31 original native checks still pass: shortcut parsing/conflicts, area geometry and cropping, native synthetic-window capture, PNG/clipboard fidelity, history restart/recovery/retention, failure-path persistence and repeated GDI handle use.

The additional 24 checks cover:

- Original pixel preservation at a different input DPI and a normalized 96-DPI output.
- Arrow shaft/head, rectangle/ellipse interiors, text, counters, translucent highlighting, freehand lines/dots and solid redaction pixels.
- Pixelation and blur applied to the image plus earlier drawn annotations; outside pixels remain unchanged.
- Exact crop, reversible crop, resize/padding dimensions and annotation coordinate alignment.
- Undo/redo across movement, style, object removal, crop, resize and backgrounds; a new edit clears redo.
- PNG pixels equal the shared rendered output. JPEG preserves dimensions, flattens transparency to white and retains an opaque black mask region within compression tolerance.
- Project save/reload preserves every annotation kind, original image and exact rendered pixels. The archive contains just the expected two entries.
- Locked project saves and edited PNG/JPEG exports leave previous output intact and clean temporary files. A failed project save keeps the document dirty.
- Invalid coordinates, geometry, colors and annotation kinds, future/malformed metadata, archive path entries and oversized compressed JSON are rejected.
- PNG/JPEG loading releases file handles; JPEG rotation metadata is applied; other image formats are refused.
- Edited clipboard pixels equal the rendered image, including masks and padding.
- Editing/project save/export leave the original capture PNG and history metadata unchanged.
- A real editor window follows document updates and releases its subscriptions on close.

The first editor run found a PNG encoding failure when writing directly to a nonseekable ZIP-entry stream. Saving the original through a seekable buffer fixed project output; round-trip and locked-save checks now pass. A separate arrowhead assertion was moved from an antialiased boundary to a true interior pixel.

## Desktop acceptance

The primary acceptance run used synthetic image content and isolated history in `verification/runs/phase2-ui-01`. An older 0.1.0 tray process initially owned the shortcuts and received one synthetic-window test capture; it was closed before the Phase 2 acceptance sequence. Existing user captures were retained.

| Workflow | Observed result |
| --- | --- |
| Global window capture → preview → Edit | Correct synthetic window captured at 809 × 567; original preview actions remain, with a new Edit button. |
| Draw and undo/redo | Arrow drag and text placement appear on the image and Objects list. Undo removes the text; redo restores it. |
| Capture with unsaved editor open | New window capture hides the editor, excludes it from pixels, and restores the same dirty document with both annotations intact. |
| Save editable project | Native dialog writes `annotated.framelet`; the unsaved indicator clears. |
| Restart and reopen | Home retains two isolated captures. Opening the saved project restores both editable annotations and 809 × 567 dimensions. |
| Flattened export | Reopened project exports `annotated.png` using the native dialog; the editor reports success. |
| Copy and pin | Copy reports success. A separate Edited pin displays both the arrow and note at the expected dimensions. |
| Resize | Width 400 with aspect ratio enabled sets height 280; Apply updates the rendered canvas and dirty indicator. |
| Unsaved close / Cancel | Closing the dirty editor prompts Yes/No/Cancel. Cancel keeps the 400 × 280 edit open. Undo returns to 809 × 567 and the saved state; clean close succeeds. |

The saved project/PNG receive independent read-only artifact checks for archive CRC, entries, mark count, dimensions, changed pixels, arrow position and preservation of the original history pixels. Results are recorded in `verification/runs/phase2-ui-01/artifact-results.json`. GUI screenshots and test media are excluded from release archives.

## Package acceptance

`publish.ps1` stages explicit runtime/source allowlists with per-file SHA256 manifests. `verification/verify_packages.py` checks CRC, safe paths, duplicates, manifests, source/build parity and documentation links, then extracts into a fresh test folder. The development receipt is `verification/package-results-0.2.0.json`; source ZIP rebuild and relocated launch are recorded there. The app assembly in the final native test output must equal the packaged assembly.

## Practical limits

This release remains Windows 11 x64 with .NET 10 Desktop Runtime, local-only and unsigned. The original GDI/SDR capture limitations and unverified mixed-DPI multi-monitor/HDR setups remain. Editor bounds are documented in the user guide; maximum-size images and long-running sessions with hundreds of effects have not been benchmarked.

Projects intentionally retain original pixels and reversible marks, so a project is not a redacted shareable image. Blur/pixelation are not secure redaction; use an opaque solid mask and share a flattened image. Projects use a new Framelet format, without `.cleanshot` compatibility. Undo history is session-only; there is no crash autosave. PNG/JPEG export, copy and pin do not automatically add edited images to capture history.

The native suite checks every tool, including JPEG export. Desktop acceptance focuses on the complete capture/edit/project/PNG/copy/pin workflow and resize/close protection; it does not manually exercise every color, shape, effect, JPEG dialog, tray action, nine-pin rejection or screen reader. App quit's editor-save gate is implemented and reviewed; its full tray-driven dialog sequence is not separately automated.
