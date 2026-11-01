# Changes

## 0.2.0 — 2026-10-30

- Added an independent editor from previews, pins, imported PNG/JPEG images and saved projects.
- Added arrows, lines, rectangles, ellipses, text, counters, translucent highlighting and freehand strokes.
- Added selection/movement, restyling, object removal, crop, aspect-aware resize, backgrounds/padding, zoom and undo/redo.
- Added blur, pixelation and opaque black masks; projects clearly retain the unredacted original while exports flatten the result.
- Added self-contained versioned `.framelet` projects, atomic save, bounded input validation and unsaved-edit prompts on editor close/app quit.
- Added flattened PNG/JPEG export, edited clipboard copy and edited pinning.
- Preserved capture hotkeys, previews, pins, original history images and the existing history/settings schema. Editors hide during capture and restore with their state intact.
- Fixed project PNG encoding into ZIP entries by using a seekable intermediate stream.
- Added editor pixel/project/failure-path checks and versioned portable/source archives. Preserved 0.1.0 release archives.

## 0.1.0 — 2026-10-30

- Added area, visible-window, full-display and repeat-area capture.
- Added configurable global shortcuts, capture buttons and notification-area menu.
- Added floating preview, clipboard copy, atomic PNG export and up to eight resizable pins.
- Added local history with 100-capture retention, restart persistence, backup index and image recovery.
- Added physical-pixel coordinate conversion, canceled-selection restoration and exclusion of the app's capture chrome.
- Fixed a native pixel-copy error found by the live fixture tests.
- Excluded input-transparent/nonactivating utility overlays from window selection.
- Made selection, preview and pin windows available through the taskbar/Alt+Tab and kept actions accessible on small pins.
- Added offline build scripts, native verification harness and portable packaging.
