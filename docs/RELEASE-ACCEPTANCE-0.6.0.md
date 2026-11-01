# 0.6.0 acceptance checks

Release: **0.6.0**, 2026-11-02. This release combines automated and native integration checks with tester-confirmed desktop workflows. Live pixel accuracy has a separate verification limit below.

## Automated and native integration results

- Release compilation: zero warnings and errors.
- Full suite: 125 checks passed, with no failures. This includes native synthetic-window capture, clipboard restoration, shortcut-conflict recovery, GDI resource checks, image editing and project compatibility, scrolling logic, overlay state and rendered control layout.
- Capture lifecycle: repeated captures preserve the dirty editor and existing pin; Pin/Unpin keeps the same window and size; capture requests resume after a confirmation dialog; the tray menu retains focus and opens history.
- Native hidden-group integration: two pins restore with the same window handles and physical bounds, the editor stays open, and a new capture remains hidden until restoration. Native visibility flags confirm that the floating images and restore tab are hidden when the capture callback runs. That callback uses synthetic image data; it is not a pixel-level exclusion check.
- Six off-screen restore-tab template checks passed across light, dark and contrast-template profiles.
- Release-publishing regression checks cover draft lookup, retry handling, archive links and README download pointers, including promotion without offering an older preview.
- The downloaded preview archive passed SHA256, ZIP safety/CRC and manifest checks. Its extracted executable reached Ready to capture with empty isolated history. The final runtime and source archives receive their own integrity, allowlist, parity, documentation-link and relocated-startup checks during release staging.

Development receipts remain under `verification/runs/release-060-acceptance-01` and `verification/runs/release-060-acceptance-02`, with the final build and focused lifecycle repeat under `verification/runs/release-060-final-01`. Test captures, private history and development records are excluded from Git and the release packages.

## Foreground-focus test correction

The final background-launched lifecycle run failed its tray keyboard-focus assertion. A diagnostic repeat showed a different process owned foreground focus both before and after the menu opened: the menu stayed open but did not receive keyboard focus. Windows restricts background processes from taking foreground focus ([platform reference](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setforegroundwindow)).

A test-only `--capture-stress-ready <fresh-output-folder>` mode now waits for F9 in its own foreground window before exercising the workflow. That repeat passed with the same test process owning foreground focus before and after menu opening, and passed the remaining capture and hidden-group checks. No application focus or menu code was changed. The failed receipts are retained alongside the passing run.

## Tester-confirmed desktop workflows

The tester first reported that manual and Auto scrolling worked in the app used the previous day. A separate current-build checklist was then provided, and the tester confirmed all four steps worked:

1. Chrome scrolling capture: manual scrolling followed by Auto-scroll, Done and Pin.
2. A second area capture and pin, with both pins moved over the test article.
3. Wheel-down hiding of both pins, followed by hover restoration at the same positions and sizes.
4. Another capture while hidden, remaining hidden until restoration, with only article content visible in the resulting image.

These are tester confirmations, not independently recorded native passes. The isolated observer still contained no captures or interaction events after those confirmations, so there are no corresponding saved outputs available for independent inspection.

## Verification limits

Desktop screenshot control failed first with 0x8007000E, then with 0x887A0005 (GPU device suspended), and did not recover after reconnecting or restarting the desktop client. Accessibility reads and the application itself remained available. No display or security settings were changed as a workaround.

Independent live Chrome seam/pixel comparison and pixel-level exclusion of floating controls remain unverified. Native window-state integration and the tester's visual checks support the release, but do not establish byte-exact live output. Exact synthetic stitching checks pass. Active-session native Esc/Cancel, nested scrollers, broader theme/accessibility coverage and mixed-DPI hardware are also outside this release's confirmed hands-on coverage; cancellation and restoration paths have automated coverage. See [scrolling constraints](SCROLLING-CAPTURE.md) when selecting a region.

## Repeating an observed session

Build `tests/Framelet.Checks`, then run `Framelet.Checks --acceptance-fixture <fresh-output-folder>`. It uses the production UI and capture backend, with isolated history under that folder. It records own button actions, wheel events over floating images, restore-tab mouse events and native visibility/bounds. It does not drive input. Quit from the tray menu to write the final summary. `--acceptance-fixture-smoke` checks observer startup and shutdown only.

## Packaging corrections

Packages include `RELEASES.md` and `BUILD-STATUS.md`, so README links resolve after extraction. Stable staging requires the project version and informational version to match the release filename. Historical archives are preserved.
