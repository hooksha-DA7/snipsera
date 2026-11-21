# Editor and capture refinements: verification

**Build:** 1.0.8-preview.1 release candidate. **Date:** 21 November 2026.

The reviewed source passed **599 automated/offscreen checks**: 538 C# checks across 25 modes and 61 Python checks, with zero compiler warnings or errors. No Store submission or GitHub release is implied. The official 1.0.7 artifacts remain unchanged.

The editor mode passed 68 checks covering legacy rendering, composition, cache correctness, direct manipulation, inline text, rejected drops, undo and detached layouts. The separate History checks also pass after correcting empty-layer normalization across a save/reopen. Unchanged blur and pixelation regions are cached within a 32 MiB per-document budget; changes to contributing content invalidate them. Cached output is compared against fresh rendered pixels.

## Changes and checks

| Requested improvement | Implemented behavior | Verification scope |
| --- | --- | --- |
| Faster, usable scrolling preview | Frame processing runs away from the UI thread; preview refreshes are throttled. Wheel and drag inspect the preview, with Fit and Follow latest controls. | Synthetic alignment and detached preview interaction checks; actual browser timing and live scrolling remain pending. |
| Move and edit annotations | Direct selection, movement, resize handles, arrow endpoints and bend handles; inline text and symbol stamps. | Geometry, selection, undo/redo, pending-text sharing and rendered output checks. |
| Seven text styles | Plain, Outline, Shadow, Label, Soft label, Callout and Marker. | Font-based bounds, distinct rendering, white/yellow contrast and legacy Plain metrics. |
| Combine screenshots | PNG/JPEG imports, clipboard/file-drop handlers and history images become movable, resizable layers. | Negative placement, layer order, undo/redo, limits, embedded-asset persistence and malformed-project rejection. Actual cross-application drops remain pending. |
| Clearer editor controls | Named Background, Layers and Image size sections share the side panel. | Detached control state, selection and layout checks. |
| Background tool | 15 gradients plus solids, padding, alignment, aspect ratio, corners, shadow and Auto-balance. | No-distortion layout, gradient output, reversible uniform-margin trimming and annotation preservation. |
| Direct editing from History | Each screenshot row has an Edit action that restores saved edits. | Direct-edit source wiring reviewed; detached checks cover the button label, enabled state and bounds. An actual click opening the editor remains pending native acceptance. |
| Consistent library appearance | History and capture surfaces follow the selected appearance instead of opaque gray blocks. | Detached light and dark rendering at three window widths. High-contrast rendering and native backdrop composition remain pending. |
| Smaller restore handle | Compact bottom-left handle with capture count, retaining click/hover restoration. | Geometry and state checks. Live hover, overlap and physical hit targets remain pending. |

## Test entry points

The check runner's `--editor-refinement-check` mode includes `EditorRefinementChecks`, `CompositionChecks` and `EditorRenderCacheChecks`. `scripts/check_release.ps1` runs the complete no-window gate. Its backend mode includes scrolling checks; `--library-ui-check` and `--overlay-style-check` cover the detached library and restore handle. The separate `--scroll-ui-check` creates a native window and remains deferred. Results must be tied to the final reviewed source and candidate artifacts before delivery; earlier passing runs do not establish that a later build passed.

Private EXE/MSIX, portable and source candidates use the existing packaging verifier for artifact hashes, safe ZIP paths and CRCs, source parity, documentation links, Store identity/resources and MSIX block hashes. An additional private receipt compares the portable archive to the staged runtime and records a source-file snapshot before and after packaging. For an uncommitted local candidate, the package receipt's source commit is only its base commit; the source ZIP and file snapshot establish its exact contents. Package receipts stay outside published source. Unsigned package validation does not establish installation acceptance.

Project round-trip checks cover self-contained version 4 assets, reading older project versions, render equivalence and rejection of missing, duplicated or path-bearing archive entries. Pixel checks and detached UI checks use synthetic images and do not operate the desktop.

## Compatibility and remaining acceptance

New project saves use version 4. This candidate reads versions 1–4; 1.0.7 and earlier do not read new version 4 files. The original pixels remain embedded. Opening an older project does not change its file until it is explicitly saved, but automatic History edits use the new format. Reverting the app version will therefore not restore access to newly saved editable History projects; the original capture PNGs remain separate.

The original image is anchored, Crop affects that image only, and annotations render above added images. Added History images are flattened copies of their visible edits. Auto-balance analyzes plain outer margins; it does not perform subject recognition. Images are PNG/JPEG only, with up to 32 added layers, a 40-million-pixel output limit and an 80-million-pixel decoded asset budget including undo-retained assets.

Desktop control, physical input, native capture/audio, installation and clean Windows 11 acceptance remain paused. Clipboard transfer into another app, actual file-picker cancellation, cross-app drag/drop, DPI/monitor placement, live material transparency, high-contrast appearance, opening the editor through the History button, native hide/restore hit targets and Chrome scrolling performance still require acceptance. No detached render or synthetic timing result should be described as that acceptance. Store approval would not sign a GitHub EXE installer.

Private test outputs and candidate packages belong outside published source. See [the editor guide](EDITOR.md) for user-facing instructions.
