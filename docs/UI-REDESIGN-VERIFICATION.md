# Framelet 0.3.0 UI redesign verification

2026-10-30. Windows x64; .NET SDK 10.0.403, Windows Desktop Runtime 10.0.14. Native visual inspection used the current desktop at 125% scaling. Other scales were exercised through layout calculations, not separate physical monitor setups.

## Changes checked

| Requirement | Result |
| --- | --- |
| Small preview on the left | Bottom-left work-area placement; at 125%, the 900 × 560 fixture produces a 300 × 194 physical-pixel window, including transparent shadow space. |
| Small pins | The same fixture starts at 320 × 206 physical pixels at 125%, instead of using source dimensions as window dimensions. |
| Multiple pins | Three initial pins occupy separate left-side slots above preview space. A fourth created from the edited image joined the stack during native UI testing. |
| Pin controls | Rounded image cards; copy/save/annotate/history/close appear on interaction. Compact dark control pills preserve the rest of the image. |
| Manual pin sizing | Native double-click enlarged an edited pin to its larger view; the resize grip and bounded sizing code preserve aspect and work-area limits. Manually moved/resized pins leave automatic docking. |
| Editor layout | Slim horizontal icon toolbar, consistent system typography, neutral canvas, color chips, stroke/style controls and bottom output actions. Size/background/layers inspector is collapsed initially and was opened in native testing. |
| Hand-drawn annotations | Native mouse input created an irregular oval; the fixture and pixel checks cover curved arrows with rounded open heads. Selected oval recolored immediately and remained editable. |
| Save/copy/pin | Native editor copy reported success, pin created another full-resolution image window, and native file dialogs wrote `manual-edit.framelet` and `manual-edit.png`. |
| Existing documents | Version 1 projects default to Clean. Version 2 saves persist style and reproduce rendered pixels; Clean arrow/ellipse pixel checks remain unchanged. |

## Automated checks

`./test.ps1 -Output verification/runs/ui-redesign-verified` builds with zero warnings/errors and runs 60 checks, covering capture pixels, crop mapping, negative origins, atomic saving, clipboard, history/recovery/retention, hotkey conflict rollback, GDI handle stability, all editor tools/effects, undo/redo and project validation.

New UI-specific checks exercise 100/125/150/200% scaling, negative work-area origins, landscape/portrait/extreme-aspect source images, one to eight pins, preview clearance, nonoverlap, bounded sizes, sketch pixels, style undo/redo, v1 compatibility, v2 round trips, default toolbar style, immediate recoloring and inspector access. A 200% preview-clearance failure found during development was corrected before release.

## Native evidence and reproduction

Development evidence is retained under `verification/runs/ui-redesign-native-final/`: initial native window bounds/DPI, original synthetic image, editable fixture, editor/preview/pin visual renders, and the project/PNG created through native dialogs. These files contain synthetic content and are not bundled in the portable release.

The editor image is a render of the actual WPF window content at the observed DPI, not a web mockup or a claim of Mac execution. Native UI was separately inspected through desktop screenshots.

After building tests, start the repeatable UI fixture with:

```powershell
./tests/Framelet.Checks/bin/Release/net10.0-windows/Framelet.Checks.exe --ui-fixture "C:\path\to\fresh-test-folder"
```

The fixture opens synthetic editor/preview/pin windows with isolated history. Close it before rebuilding. It is a test-harness option, not a production app startup mode.

## Release verification

`publish.ps1` creates versioned portable/source ZIPs with explicit allowlists and per-file SHA256 manifests. `verification/verify_packages.py` checks CRC, safe/unique paths, manifests, source/build parity and documentation links, then extracts to a fresh test folder. The release receipt is `verification/package-results-0.3.0.json`.

Earlier 0.1.0 and 0.2.0 ZIPs remain unchanged. The source archive excludes SDK files, build intermediates, screenshots, test output, the supplied CleanShot binary and decoded vendor resource evidence.

## Scope

This verifies Framelet’s current workflow and the requested visual direction. The Mac application was not executed on Windows. Mixed-DPI hardware transitions, HDR, all Windows accessibility configurations and full feature parity with CleanShot remain outside this acceptance pass. Pins are session-only and do not persist after app exit.
