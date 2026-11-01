# Changes

## 0.5.2 — 2026-11-01

- Restored capture controls on the image after the user rejected the external toolbar. - Scoped floating-button styling preserves readable material and pinned accent states during interaction, with rounded focus treatment and theme/policy fallbacks. Main app controls and editor tool layouts retain their existing design.
- Retained 0.5.1's shared preview/pin sizing, source proportions, same-window direct Pin/Unpin, bottom-left preview and pin-stack clearance.
- Added on-image bounds/nonoverlap and source-pixel checks across six image profiles. Updated capture-lifecycle verification for the restored layout. Previous releases and user history/settings remain unchanged.

## 0.5.1 — 2026-10-31

- Unified preview/pin size limits and removed independent minimum image dimensions that added empty space to wide/tall captures.
- Moved floating actions into one compact hover toolbar outside the image, keeping all six controls usable regardless of image proportions. Images remain sharp and retain their dimensions while controls are visible.
- Made Pin/Unpin toggle the same window directly, with a distinct active icon. Unpin returns to the preview area without overlapping the reflowed stack. Right-click retains options and opacity controls.
- Kept native file drag, resizing, keyboard access, repeated captures, original history pixels and project format version 3. Added aspect-ratio, in-place toggle, toolbar sizing/visibility and stack-clearance verification.

## 0.5.0 — 2026-10-31

- Added four arrow choices: tapered Classic, Filled, Open and Curved, with graphical samples and editable endpoint/bend handles. A handle drag is a single undo operation.
- Smoothed newly drawn Hand-drawn freehand paths. Version 3 projects preserve arrow geometry and smoothing; versions 1 and 2 retain their legacy rendered pixels.
- Replaced the legacy tray menu and unsaved-edit/history confirmations with themed WPF Fluent controls and explicit action labels.
- Applied antialiased alpha masks and high-quality thumbnail scaling to floating cards, softened shadows/tint, and made pin edges follow image opacity. Escape dismisses the pin menu without closing its image.
- Made capture selection asynchronous, moved global hotkeys to an independent message-only window and queued the latest request during capture or a modal dialog. Added capture commands directly to preview/pin context menus.
- Added arrow/project/alpha-edge/nonmodal-selector/confirmation regressions and a separate capture lifecycle integration test. Preserved previous release archives and existing user history/settings.

## 0.4.1 — 2026-10-31

- Made capture actions consistent full-width rows with larger hit targets and quiet resting surfaces.

## 0.4.0 — 2026-10-31

- Applied the built-in WPF Fluent theme with system light/dark mode, accent colors and native controls.
- Replaced macOS-style traffic-light chrome with actual Windows caption buttons, resize borders, system menu and maximize/Snap behavior; main windows use WPF's Mica backdrop on supported systems.
- Rebuilt preview hover actions around a frosted thumbnail, labeled Copy/Save controls and corner actions. Pinned reference images stay sharp with edge controls.
- Added native file drag-and-drop from preview images and drag handles on pins/editor, preserving original/edited PNG resolution respectively.
- Added display-only pin opacity via a slider and Ctrl+wheel, plus arrow-key positioning and Shift+arrow larger steps.
- Added contrast/transparency/reduced-animation effect policies and live preference refresh. Existing image/project formats remain unchanged.
- Fixed pin-limit failure so a preview remains accessible when an additional pin cannot be opened.
- Added native light/dark and file-drop fixtures plus theme/policy/payload regression checks. Preserved earlier release archives.

## 0.3.0 — 2026-10-30

- Rebuilt previews and pins as compact rounded image cards with hover controls, without permanent title/footer bars.
- Moved the capture preview to the bottom left and bounded initial sizes in logical pixels, independent of source image resolution.
- Added left-side pin stacking, preview clearance, multiple columns when needed and DPI-aware physical placement. Dragging/resizing a pin keeps it independent of subsequent stack reflows.
- Added a pin resize grip and double-click larger/compact toggle; copy/save/edit retain full original pixels.
- Replaced editor side tool walls and large text buttons with a horizontal icon toolbar, contextual color/style controls, neutral canvas and collapsible size/background/layers inspector.
- Applied consistent Segoe typography, quieter colors and compact custom window chrome to editor/history.
- Added hand-drawn-style curved arrows with open rounded heads and gently irregular ellipses; new marks default to this style. Clean remains available.
- Added immediate undoable style changes for selected annotations.
- Added project format version 2 for annotation styles while continuing to read version 1 without changing legacy rendering.
- Added UI/DPI/stacking/style compatibility checks and a synthetic native UI fixture. Preserved 0.1.0 and 0.2.0 archives.

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
