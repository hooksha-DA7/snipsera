# Temporarily hide overlays

Available in 0.6.0. Automated/native window-state checks passed, and the tester confirmed wheel hiding, hover restoration, retained pin geometry and capture while hidden. Independent pixel-level capture exclusion remains unverified. See [acceptance results](RELEASE-ACCEPTANCE-0.6.0.md).

## Use

1. Scroll down one full notch over a floating preview or pin. Small precision-wheel deltas accumulate for up to 400 ms. The image's right-click menu, tray menu and Home hide/show row provide alternatives.
2. The floating group hides, leaving a small tab at the bottom left of the initiating monitor's work area, above the taskbar. The tab is normally 112 logical pixels wide and 22 pixels high, expanding to 68 pixels on hover. It uses rounded top corners, an up chevron, a representative image hint and a count.
3. Hover for 380 ms, click the tab, or use the tray/Home toggle to restore. If the tab appeared under the pointer, first leave its area and return; it cannot immediately undo the hide. Enter/Space work if the tab has keyboard focus. The tray and Home are the supported keyboard routes; Alt+Tab access is not promised.

Plain wheel-down hides; upward or modified input does not. Ctrl+wheel on a pin continues to adjust opacity and clears any partial hide gesture. Wheel over action buttons, resize grips, open menus or an active drag is excluded. This is a Windows wheel equivalent of the supplied swipe interaction, rather than a dedicated touchpad gesture integration. Preview drag-out still exports an image; pin dragging still moves the pin.

## What is preserved

- The same existing windows, pin/unpin state, image data, sizes, positions, opacity and stack order. Short manual slide/fade animation affects only the content inside each frame; native frame bounds do not animate. A one-time placement guard prevents subsequent Loaded events from re-docking a moved pin.
- Home, editor documents, unsaved edits and local history. A preview already hidden for editing is not resurrected when the floating group restores.
- Temporary-hide intent across capture success, cancellation, errors and preview replacement. New captures made while hidden join the group and update the tab/count; they stay hidden until restoration. A newly created, never-shown card receives its normal placement when first shown.
- The existing four hotkeys, settings/history schemas, image/project formats and on-image action arrangement. No new dependency, network service or persistent hide setting is introduced.

Hide state is in memory for the current app session. It does not delete captures. Closing hidden cards removes them from the restore count; closing the last eligible card clears the mode. Closing the tab itself restores the group instead of removing the recovery control. Quit retains the existing unsaved-editor confirmation behavior.

## Capture, display and preferences

Capture suspension is separate from temporary hiding. The app hides the restore tab and synchronously hides every visible image card, cancelling any transition before it reads pixels. Cleanup resumes visible intent; it does not force a hidden group to reappear. This integration applies to area, window, display, repeat and scrolling capture. Native window-visibility checks and tester-confirmed capture exclusion passed; independent pixel-level exclusion remains unverified.

The tab uses the saved monitor work area and live DPI. If a monitor/work area changes, it selects an overlapping or nearest available area and stays above the taskbar. Display, DPI and Windows setting notifications trigger placement checks. If a tiny work area cannot contain a usable tab and its shadow padding, the tab is omitted; tray/Home restoration remains available. The tab repositions; original-card placement on changed physical hardware remains part of native acceptance.

System light/dark brushes, contrast styling and transparency/motion policy use existing Framelet helpers. Reduced motion makes slide/fade and tab rise instant. High contrast uses an outline and omits blur/image effects. Showing/clicking the tab requests no activation. Native focus, screen-reader, animation and hardware behavior are pending verification.

## Historical preview verification

These entries record preview 4 development. Current native integration and tester-confirmed results are listed in the [0.6.0 acceptance report](RELEASE-ACCEPTANCE-0.6.0.md); the preview-stage pause and pending checks below are historical.

- Compilation: zero warnings/errors.
- 53 no-window checks: 13 visibility-state tests, 8 hide/restore-gesture tests, 3 shelf-layout tests and the 29 unchanged scrolling regressions. State checks use fake cards, covering identity/geometry fields, editor-hidden previews, new captures, cancellation/failure, replacement, nested suspension, re-entrant close and cleanup after presentation failure. They do not substitute for showing real windows.
- Layout checks cover 100%, 125%, 150% and 200% scales, negative origins, top/left/bottom taskbar work areas, changed/removed monitors and tiny/invalid inputs. An initial tiny-area overflow was reproduced and corrected.
- Six off-screen renders parse the actual production button templates and check base bounds/chevron visibility in light, dark and a contrast-template profile, resting and expanded. The content is synthetic. This validates template construction and layout; it is not a native high-contrast or hover test.
- Integration checks led to corrections for partial wheel-gesture reset, unknown/initial pointer arming, setting-change placement notifications and tab-close recovery. No broad UI redesign was made. Scrolling, editor, image storage and floating sizing/rendering helpers remain byte-identical to preview 3.

Development receipts: `verification/runs/temporary-hide-01/final-02`, `styles-02`, and the earlier failed `integrated` layout receipt. Design records are under `design/temporary-hide-2026-11-01`. Test data, renders, video and agent logs are excluded from packages.

Run only the safe checks while desktop testing is paused:

```powershell
dotnet Framelet.Checks.dll --backend-check <fresh-output-folder>
dotnet Framelet.Checks.dll --overlay-style-check <fresh-render-folder>
```

The ordinary full test suite opens windows and performs desktop tests, so it has not been rerun during the pause. Live verification is still required for wheel/hover delivery, transitions and rapid hide/restore, real pin position/opacity retention, capture pixels excluding the tab/cards, dirty-editor/capture queue integration, native preferences/mixed DPI and an extracted-package launch. Chrome Auto-scroll acceptance from preview 3 also remains pending. No installation or stable release is claimed.

Reference: supplied `temporarily_hide.mp4`; [CleanShot's slide-down feature](https://cleanshot.com/changelog); [WPF hidden-window reuse](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/windows/how-to-close-window-dialog-box); [Windows setting-change notification](https://learn.microsoft.com/en-us/windows/win32/winmsg/wm-settingchange).
