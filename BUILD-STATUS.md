# Snipsera build status

## All-in-one capture candidate: 1.0.4

The new configurable shortcut opens a compact floating bar with Area, Window, Display, Scroll and Last area. Home and the tray expose the same launcher. Print Screen can be recorded for it in Keyboard shortcuts. The four existing shortcuts are preserved, and old settings choose an unused launcher default when needed.

The bar dismisses before capture, then uses the existing preview/history workflow. Escape, Close, clicking outside or pressing its shortcut again cancels without capture. Display uses the monitor where the bar opened. A narrow work area wraps the same full-size controls. The new tests use detached controls, synthetic key events and fake native registration; actual global activation and mixed-DPI placement remain pending while desktop testing is paused. See [capture bar help](docs/CAPTURE-BAR.md).

Verification: **195 automated/offscreen checks passed**, including 28 capture-bar checks. The Release build has no compiler warnings or errors. The full existing focused screenshot/editor/scrolling/overlay/recording suite remains in the release gate. No visible test window, real global key registration, desktop capture or local installation was used.

## Recording update: 1.0.3

GitHub preview: **1.0.3**, 4 November 2026. The current official Store/GitHub release is **1.0.2**.

Recording setup now uses two compact floating panels with source icons, editable pixel dimensions, capture toggles and separate Record GIF / Record Video actions. Quality, FPS, resolution and notification settings are available under the sliders icon. The bar shows GIF conversion progress and keeps its two-click Discard available while Stop is pending.

Area selection uses physical pointer coordinates through mouse release, validates a frame inside one display, and passes an explicit monitor-local crop and output canvas to the recorder. Canceling selection returns a distinct result so a Record action cannot fall back to an old source.

Compact/Balanced GIFs sample the palette and use faster ordered dithering; High retains full palette analysis and error diffusion. Output FPS, dimensions and timing remain as chosen. Six-second synthetic 720p/1080p benchmarks were about three times faster in Balanced mode; results depend on content and hardware.

Verification: **167 no-window checks passed**, plus a separate native generated-image crop/encoded-pixel check. The build had no compiler warnings or errors. See [recording verification](docs/RECORDING-VERIFICATION.md) for measured results and limits. Existing screenshot/editor/scrolling/overlay checks passed. No user desktop, microphone or computer audio was captured in these tests.

Live mouse selection, WindowsGraphicsCapture area output on the user's desktop, clean Windows 11 and final installed-MSIX capture/recording acceptance remain unverified for this candidate. Desktop testing remains paused. Synthetic evidence is not live acceptance.

The GitHub installer remains unsigned. Store certification does not sign GitHub downloads. SignPath's application was submitted on 2 November; provider status was not rechecked during this change. Store package 1.0.3.0 was submitted on 5 November 2026 as Submission 3 (1152921505702318460). It is in certification, with automatic publication after approval. New GitHub versions remain previews until the matching Store update is approved. Historical tags and downloads are preserved.

See [recording help](docs/RECORDING.md), [user guide](docs/USER-GUIDE.md), [distribution setup](docs/DISTRIBUTION.md) and [release history](RELEASES.md).

The published preview bytes come from [hosted Windows run 41779038100](https://github.com/hooksha-DA7/snipsera/actions/runs/41779038100), source `1c9bdf8d70488bf4199dea2a608b028e2dcac394`. That run passed all 167 focused checks, package/source verification and exact installer install, synthetic launch, reinstall, increasing-version fixture upgrade and uninstall on Windows Server 2025. The downloaded artifacts were independently hashed and checked against their source before publication. The Store submission uses that exact hosted MSIX, SHA256 `aca5595380464b6b8ce8c7cf1210cd5f7087901e68979a2081a7888b1b3fb1a0`; certification is pending.
