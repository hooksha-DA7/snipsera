# Annotation and capture refinement — 0.5.0

Verified on 2026-10-31 on Windows 11, at the machine's 125% desktop scaling, with .NET SDK 10.0.403 and Windows Desktop Runtime 10.0.14. This update addresses the supplied markup video, legacy-menu/dialog screenshots, rough floating edges and repeated-capture report.

## Delivered behavior

| Request | Implementation |
| --- | --- |
| Four arrow styles and better drawing | Classic tapered solid arrow, Filled solid head, Open straight arrow and Curved arrow; graphical choices, round endpoint handles and a bend handle on the actual curve midpoint. Newly drawn Hand-drawn pen paths interpolate smoothly through their samples. |
| Modern right-click and close UI | WPF Fluent tray/pin menus, icons and shortcut labels; themed confirmation with Save project, Discard edits and Cancel. Native Windows frame, close button and safe Cancel behavior remain. |
| Smoother pins and translucency | Rounded alpha mask with partial-alpha edge coverage, high-quality image scaling, softer shadow/tint and a rim that fades with image opacity. Pinned references remain sharp; preview hover frosting is view-only. |
| Capture again with images open | Independent native hotkey window, asynchronous selection and a latest-request queue. Existing pins/editors return after capture, the new preview replaces the old one on success, and both originals remain in history. Capture requests received during a modal dialog resume after dismissal. |

Escape dismisses an open pin menu without closing its pin. Outside a menu, Escape continues to close the floating image. Preview/pin menus also expose capture area/window/display directly.

## Reference evidence and scope

The user-supplied `markup.mp4` shows a curved open arrow with editable endpoints and a central bend handle. The supplied annotated CleanShot screenshot shows a tapered filled arrow and smooth annotation outlines. CleanShot's [official features page](https://cleanshot.com/features) specifies four arrow styles, including curved arrows, and automatic pencil smoothing; its [changelog](https://cleanshot.com/changelog) records the additional arrow styles and classic-arrow refinement. Classic/Filled/Open/Curved are Framelet's descriptive choices, not a claim that these are the vendor's exact four menu labels or pixel-identical geometries. No original binaries, artwork, demo frames or fonts are bundled.

[Microsoft's WPF modality documentation](https://learn.microsoft.com/en-us/dotnet/api/system.windows.window.showdialog?view=windowsdesktop-10.0) explains the thread-wide disabling behavior of ShowDialog. The selector now uses Show and a completion task. [LeaveThreadModal](https://learn.microsoft.com/en-us/dotnet/api/system.windows.interop.componentdispatcher.leavethreadmodal?view=windowsdesktop-10.0) resumes requests after genuine modal save/confirmation dialogs.

## Verification

- Release build: zero warnings/errors. The final automated run has 70 passing core checks, zero failures, plus a passing separate capture lifecycle integration process.
- Arrow tests compare all four rendered styles, validate curve handle mapping, preview/commit separation and undo/redo, and round-trip geometry through version 3 projects.
- Versions 1 and 2 keep exact legacy arrow and pen pixels. New smoothed pen rendering leaves the original image unchanged. Partial-alpha rounded corners are checked at 100%, 125%, 150% and 200% rendering scales; this is not multi-monitor hardware certification.
- The selector leaves its parent enabled and does not enter thread-modal state. Confirmation tests inspect the semantic actions, real Windows chrome and default cancellation.
- The integration process injects synthetic pixels to isolate capture lifecycle behavior from desktop contents: two rapid requests preserve an existing preview workflow, pin and dirty editor, then a request inside a modal confirmation resumes after dismissal. Actual GDI pixels, clipboard, native hotkey conflicts and GDI-handle stability are checked separately by the core suite.
- Native desktop checks exercised the four-style popup, endpoint and bend drags, undo/redo, Cancel on the Fluent close prompt and saving the changed project. An area capture and two repeat-area captures succeeded consecutively without closing the previous preview/editor/pins. History grew from one fixture entry to four, retaining all three real 682 × 407 captures.
- Native pin-menu testing covered opacity and discovered an Escape propagation bug, which was corrected before release. Tray persistence and its History command are verified by the native integration process; that process also records the actual tray popup. The desktop automation's detached-tray keyboard trigger was inconclusive, so it is not counted as a manual notification-icon click test.

In the 0.4.1 baseline, a rapid second request was silently discarded by the `capturing` guard; a slower second request did work. That confirms a dropped-request defect, but does not prove it explained every instance of the user's longer blockage. The new lifecycle tests and native repeated-capture checks verify the corrected paths without overstating that diagnosis.

Local receipts are under `verification/runs/annotation-release-verified`, `annotation-ui-01`, `annotation-ui-release` and the package run in the maintained project. Test captures and reference media are excluded from the distributable packages.

## Compatibility and remaining acceptance

New `.framelet` saves use version 3 and require Framelet 0.5.0 or newer. This release reads versions 1, 2 and 3; old arrows and pens retain their appearance until explicitly restyled. Capture history/settings and original capture PNGs retain their existing format. Previous release archives remain available.

The latest waiting capture wins if several requests arrive while busy. A modal dialog must be answered before its queued capture starts. This avoids hiding a save/unsaved-work prompt. Capturing from an ordinary open editor does not require closing or saving it.

Rounded edges and lower tint are implementation improvements; aesthetic acceptance still depends on the user's actual images and desktop. The pin's image opacity is not desktop Acrylic: it blends the sharp reference with the desktop, while preview hover frosting blurs only the thumbnail. Existing contrast/transparency/motion fallbacks remain. Mixed-DPI hardware, HDR fidelity and every external application's interactions remain outside the verified scope.

Reproduce with `./test.ps1 -Output verification/runs/<fresh-name>`, then `./publish.ps1 -Version 0.5.0` and `python verification/verify_packages.py --version 0.5.0 --run <fresh-name>`. Publishing refuses an existing staged release. The native fixture can be launched with the checks executable's `--ui-fixture-dark` or `--ui-fixture-light` option and a fresh output directory; F9 records visible test-window/popup proof images. Harness keys are not product shortcuts. The separate capture-lifecycle process verifies the tray path and records `tray-native.png`.
