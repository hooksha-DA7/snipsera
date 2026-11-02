# Snipsera name change

Snipsera is the new name for Framelet. The first Snipsera candidate is **1.0.0-preview.1**; it remains a preview. The new major version also gives the first Store package a valid nonzero major version, **1.0.0.0**. Existing Framelet releases keep their names, tags and downloads.

## Compatibility

- App windows, the tray, export filenames, executable metadata and new packages use Snipsera. The controls and their positions are unchanged.
- New editable projects use `.snipsera`. The editor also opens `.framelet` files, uses the same document format, and can continue saving an opened legacy file under its original name.
- New installations use `%LOCALAPPDATA%\Snipsera`. If that folder does not exist and `%LOCALAPPDATA%\Framelet` does, the app continues using the old folder without moving or rewriting its contents. If both exist, Snipsera takes precedence; `--data-dir` can select a specific folder.
- The original data-root-based single-instance lock is retained. An old and new copy using the same history cannot write concurrently. The second launch can bring either product's home window forward.
- Internal C# namespaces and source project paths remain `Framelet`; they do not affect the displayed product name. Historical verification documents describe the versions originally tested.

## Repository and Store

The personal repository is [hooksha-DA7/snipsera](https://github.com/hooksha-DA7/snipsera). GitHub redirects the old repository URL; do not create a new repository at the old location. Historical asset basenames and bytes are preserved.

The Microsoft Store name was reserved on 2 November 2026 under the personal publisher. Product ID: **9NFL9VHL8GKV**. The assigned package identity is recorded in `packaging/store-identity.json`. The product is a draft; reservation is not publication, certification or signing.

EXE and MSIX candidates are prepared by `publish.ps1`. The installer includes .NET; the Visual C++ x64 runtime remains a prerequisite for recording. See [distribution](DISTRIBUTION.md) for setup and signing status. Unsigned MSIX submission files are not public installable downloads.

Domain registration and trademark clearance are separate from changing the software name. No registration or legal clearance is implied by the app or repository rename.
