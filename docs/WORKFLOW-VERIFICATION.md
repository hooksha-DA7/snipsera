# Settings and recording workflow verification: 1.0.6

Local candidate, 6 November 2026. This change adds sectioned Settings, automatic screenshot/edited-PNG saves and configurable folders, recording side cards, a bottom-centred all-in-one bar and recording setup dismissal after Stop.

The release gate passed **330 checks**: 293 C# checks and 37 Python release/packaging checks. The Release build completed without warnings or errors. Subsequent focused checks cover the explicit Fluent style on shortcut recorders, whose derived type otherwise falls back to a classic text-box appearance.

## Safe checks

Use the pinned local .NET SDK and dependency cache to build `tests/Framelet.Checks`. The ordinary full test runner opens windows and must not run while desktop testing is paused.

- `--workflow-check`: isolated save preferences, failure rollback, corrupt-config preservation, stable screenshot export paths/edits, video/GIF destinations, history across changed recording folders, collision-safe finalization, real-media drag data, restoration policy, card controls and Settings ownership of shortcut recorders.
- `--workflow-visual-check`: detached Settings pages at 400/544/780 logical pixels, light/dark themes and 100/150/200% rendering, plus MP4/GIF cards with controls hidden/shown at three sizes. Checks action labels, horizontal reachability and nonoverlapping media badges; long pages use their scroll viewer.
- `--capture-bar-check`: five shortcut bindings, fake native registration/conflict rollback, input recording, preserved Home capture actions, bottom-centred placement across scales and negative monitor origins, narrow bar layouts.
- Existing backend, feedback, preferences, overlay and recording checks continue to exercise screenshot editing/history, scrolling, GIF configuration and rendering without capturing the desktop.

These checks use isolated profiles and synthetic data. No real startup preference, update service, clipboard, file-picker selection, desktop capture or microphone is exercised by the new tests. A restore-policy check is not live acceptance of Stop on an installed app.

## Remaining native acceptance

Desktop/physical-input, local installation and clean Windows 11 testing remain paused. Verify global shortcuts from Settings, the native folder picker and filesystem permissions under the installed Store package, bottom placement with the taskbar/mixed-DPI monitors, actual area/window/display recordings, no setup reopening after Stop, recording card playback/copy/drag and hide/restore on a live desktop before claiming those accepted. EXE signing and Microsoft Store submission/approval are separate from these local checks.
