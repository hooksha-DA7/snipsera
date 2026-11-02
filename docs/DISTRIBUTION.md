# Framelet distribution

Decision: 2026-11-02. Future delivered versions must produce both an EXE installer and an MSIX. Keep source downloads and SHA256 checksums. The portable ZIP may remain an additional download, and historical releases stay unchanged.

## Selected channels

| Channel | Package | Signing and updates |
| --- | --- | --- |
| Personal GitHub releases | EXE installer | SignPath Foundation is the selected signing route, subject to project acceptance and a configured trusted build. |
| Microsoft Store | MSIX | Microsoft signs accepted Store packages and provides Store updates. |

These channels have separate approval processes. Neither is enrolled or approved merely by recording this decision. A Store signature does not sign the EXE downloaded from GitHub. An unsigned MSIX prepared for Store submission is not a directly installable public download.

## Before the first submission

1. Choose an app-level open-source licence. The public repository currently has no such licence; third-party notices do not license Framelet's own code. SignPath requires qualifying open-source licensing. Keep the licences of ScreenRecorderLib, FFmpeg and other components intact.
2. Complete a personal Microsoft Store developer account and reserve the app. Use the actual package identity, publisher and publisher display name supplied by Partner Center. Never invent a Store identity or use a company account as a substitute. Identity verification must be completed by the publisher.
3. Prepare the Store listing, support/privacy links, app screenshots and certification notes. Explain the full-trust desktop requirement and screen/microphone capture behavior accurately. Test the packaged tray, shortcuts, screenshot history and recording paths, including access to bundled native dependencies and writable local data.
4. Apply to SignPath Foundation after the licence and public project requirements are met. Follow its requirements for repository access, multi-factor authentication, trusted hosted builds, artifact provenance and release approval. Approval and certificate issuance are external dependencies; do not report a package as signed while they are pending.
5. Extend the packaging and release scripts to build and verify both formats. Use a self-contained runtime for the installer where practical and verify the remaining native runtime prerequisites. Use the same app version across outputs, with a valid, monotonically increasing four-part MSIX version. Use the reserved Store identity for Store packages.

## Release checks

- Build from the tagged reviewed source. Preserve third-party licences and exclude private media, captures, signing keys, tokens and development logs.
- Verify EXE installation, launching, upgrade and uninstall. Preserve screenshot/recording data on upgrade and ordinary uninstall. Test on a clean Windows environment for runtime prerequisites.
- Validate MSIX contents, executable entry point, visual assets, identity, architecture, version and full-trust capability. Install and test a properly signed test/Store package before claiming packaged-app acceptance; manifest validation alone is insufficient.
- Verify the final installer signature, timestamp and expected publisher. Obtain hashes after signing, because signing changes the bytes. For Store delivery, track the actual submission and certification state separately from local package creation.
- Keep unsigned candidates clearly identified in private staging. Publish public direct-download packages only with accurately stated signing/installation status. A valid signature is not a guarantee that SmartScreen immediately stops warning.
- Update the catalog, human-readable release notes and download links for the actual delivered formats. Preserve verification of historical ZIP releases.

## Current setup status

The release workflow documents the new required formats. The published 0.7.0-preview.1 remains the previously verified portable ZIP release. No EXE installer or MSIX has been built by this policy change. Store onboarding/certification, SignPath acceptance, the app licence decision, packaging implementation and native installer/MSIX acceptance still need completion before the first delivery through these routes.

References: [Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), [Store registration](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account), [MSIX desktop packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion), [SignPath conditions](https://signpath.org/terms.html), [SignPath trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github).
