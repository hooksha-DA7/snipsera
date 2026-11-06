# Snipsera build status

## Settings and recording workflow candidate: 1.0.6

Local candidate, 6 November 2026. Settings now has General, Save locations, Keyboard shortcuts and Updates sections. Screenshots and their edits save automatically to Pictures\Snipsera by default, with configurable screenshot/recording folders and an auto-save switch. History keeps its independent editable originals; changing a destination does not move old files. All-in-one capture opens at the bottom of the current work area. Stopping a recording keeps setup closed and adds a compact media card to the side stack, with play, copy, save, drag and same-window Pin/Unpin.

The release gate passed **330 automated/offscreen checks** (293 C# and 37 Python), with no compiler warnings or errors. New checks include synthetic MP4/GIF thumbnail extraction, repeated-Stop finalization, failure preservation and recording-card layouts. See [Settings help](docs/SETTINGS.md) and [verification scope](docs/WORKFLOW-VERIFICATION.md). Desktop capture, physical input, local installation and clean Windows 11 acceptance remain paused. This candidate is not a Store approval or a signed GitHub installer. The official published baseline below remains 1.0.5.

## Update and startup release: 1.0.5

Settings adds automatic/manual update checks, an optional GitHub preview channel and Start with Windows. Updates follow the installation source, appear on Home and can trigger a deferred tray alert. Known notices and acknowledgement persist across restarts. GitHub actions open the owned release page; installers are never downloaded or executed automatically. Startup is off by default, opens quietly in the tray and respects Windows user/policy controls.

The app targets Windows 11's WinRT APIs through `net10.0-windows10.0.22000.0`. Store update calls require the assigned package identity and initialized owner window. Packaged startup uses the disabled manifest task; EXE/portable startup uses a quoted per-user Run entry. Isolated test profiles cannot alter real startup settings. See [updates and startup](docs/UPDATES-AND-STARTUP.md) and the updated [privacy policy](PRIVACY.md).

The local release gate passed **268 automated/offscreen checks** (231 C# and 37 Python), with no compiler warnings or errors. Checks use simulated startup storage and Store responses, plus detached UI renders. A separate read-only smoke check of the production GitHub reader returned the current official and preview releases. Real Windows sign-in startup, tray notification delivery and installed Store update/startup acceptance remain pending. Desktop input, capture/audio and local installer tests remain paused. No Windows startup setting was changed on the user's PC.

The official release uses [hosted run 42090354285](https://github.com/hooksha-DA7/snipsera/actions/runs/42090354285), source `a69041786461bd13df68223afbc07d50b3a9ca78`. The same 268 checks and exact package/source verification passed on the hosted runner. EXE install, synthetic launch, reinstall, increasing-version fixture upgrade, startup off by default, owned startup cleanup, preservation of another copy startup entry and uninstall passed on Windows Server 2025. MSIX 1.0.5.0 was submitted on 5 November 2026 as Submission 4 (`1152921505702326681`). Microsoft confirmed that this exact submission passed certification and was published automatically on 5 November 2026. GitHub 1.0.5 is promoted to official/Latest with the same source tag and artifact bytes; pending native acceptance is unchanged. GitHub installers remain unsigned.

## All-in-one capture preview: 1.0.4

The new configurable shortcut opens a compact floating bar with Area, Window, Display, Scroll and Last area. Home and the tray expose the same launcher. Print Screen can be recorded for it in Keyboard shortcuts. The four existing shortcuts are preserved, and old settings choose an unused launcher default when needed.

The bar dismisses before capture, then uses the existing preview/history workflow. Escape, Close, clicking outside or pressing its shortcut again cancels without capture. Display uses the monitor where the bar opened. A narrow work area wraps the same full-size controls. The new tests use detached controls, synthetic key events and fake native registration; actual global activation and mixed-DPI placement remain pending while desktop testing is paused. See [capture bar help](docs/CAPTURE-BAR.md).

Verification: **195 automated/offscreen checks passed**, including 28 capture-bar checks. The Release build has no compiler warnings or errors. The full existing focused screenshot/editor/scrolling/overlay/recording suite remains in the release gate. No visible test window, real global key registration, desktop capture or local installation was used.

The published preview uses [hosted run 42060962574](https://github.com/hooksha-DA7/snipsera/actions/runs/42060962574), source `ab5fb06abef8ed90aae3c7a00b4c2f70880a5b10`. Exact hosted EXE installation, synthetic launch, reinstall, increasing-version fixture upgrade and uninstall passed on Windows Server 2025. MSIX 1.0.4.0 is prepared for Store submission and is not submitted or offered as a public unsigned MSIX. All previous versions and downloads are preserved.

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
