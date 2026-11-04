# Snipsera build status

Official release: **1.0.2**, 4 November 2026. Immutable build/tag: **1.0.2-preview.2**.

Docked previews and pins now use matching 200 × 150 thumbnails with an 8-DIP visible gap. The complete image remains available to copy, save, drag and edit. Shortcut fields record the keys pressed, including Print Screen, and temporarily suspend capture bindings while focused. Edits save automatically in local history, with their original pixels and editable layers; closing the editor flushes pending edits without the usual save prompt. A failed save keeps the editor open.

Verification: **130 automated and offscreen checks passed** with no compiler warnings. They cover editable-history persistence/recovery/retention and failed writes, shortcut event logic, consistent card geometry across DPI and negative-origin monitors, actual detached card rendering with reachable on-image actions, existing scrolling/overlay/recording rules, identity and packaging/release rules. The test renders use generated pixels and do not show windows or read the desktop.

Live keyboard/global-hotkey behavior, clean Windows 11 and final installed-MSIX capture/recording acceptance remain unverified for this build. Desktop testing remains paused. EXE/MSIX packaging and archive verification are separate from runtime acceptance.

Partner Center confirmed that submission **1152921505702310050** passed certification and package **1.0.2.0** is now available in Microsoft Store on 4 November 2026. The read-only published package view confirms its filename and version. The [Store download](https://apps.microsoft.com/detail/9NFL9VHL8GKV) and matching GitHub build are the official release. Store certification does not sign the EXE downloaded from GitHub.

The GitHub installer remains unsigned. SignPath's application was submitted on 2 November; no signing-provider status check or live signing request was performed in this update. Snipsera 1.0.2 is the current distribution baseline; Framelet 0.6.0 is preserved as an earlier release. Historical downloads are preserved.

See [user guide](docs/USER-GUIDE.md), [distribution setup](docs/DISTRIBUTION.md) and [release history](RELEASES.md).

The published files come from [hosted Windows run 41755579172](https://github.com/hooksha-DA7/snipsera/actions/runs/41755579172), which passed the same checks, package verification and installer installation, launch, reinstall, increasing-version fixture upgrade and uninstall on Windows Server 2025. The downloaded artifacts were independently checked against their hashes and tagged source before publication.

The release process now validates the Store product, submission, package version/hash and source commit before official GitHub classification. Its 36 Python workflow checks passed. The promotion changes release metadata without rebuilding or replacing the existing 1.0.2 artifacts.
