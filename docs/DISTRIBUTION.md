# Snipsera distribution

Decision: 2026-11-02. Future delivered versions must produce both an EXE installer and an MSIX. Keep source downloads and SHA256 checksums. The portable ZIP may remain an additional download, and historical releases stay unchanged.

## Selected channels

| Channel | Package | Signing and updates |
| --- | --- | --- |
| Personal GitHub releases | EXE installer | SignPath Foundation is the selected signing route, subject to project acceptance and a configured trusted build. |
| Microsoft Store | MSIX | Microsoft signs accepted Store packages and provides Store updates. |

These channels have separate approval processes. Neither is enrolled or approved merely by recording this decision. A Store signature does not sign the EXE downloaded from GitHub. An unsigned MSIX prepared for Store submission is not a directly installable public download.

## Before the first submission

1. **Licence selected:** MIT for Snipsera's own code, approved on 2026-11-02 and included in the root `LICENSE`. Include it and `THIRD-PARTY.md` in source and runtime packages. Keep the licences of ScreenRecorderLib, FFmpeg and other components intact. This addresses the app-level licence requirement; SignPath acceptance still requires its remaining project and build checks.
2. The personal Microsoft Store account has reserved Snipsera. Use the actual package identity, publisher and publisher display name supplied by Partner Center. Never invent a Store identity or use a company account as a substitute. Identity verification must be completed by the publisher.
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

## Build candidates

`publish.ps1` prepares an EXE installer, a Store-submission MSIX, portable/source ZIPs and SHA256 checksums in a fresh private folder. It does not upload, sign, submit or install them. The app version comes from the project; the corresponding four-part MSIX version uses a zero final field. The first Snipsera preview is 1.0.0-preview.1 / MSIX 1.0.0.0.

The assigned personal Store identity is in `packaging/store-identity.json`. Do not replace it with a guessed publisher. `-DevelopmentMsix` instead creates an explicitly labelled local-test identity and must never be used for a Store submission.

The build needs the Inno Setup 7.1.1 compiler and Windows SDK BuildTools 10.0.28000.2957. Their official source URLs and checked archive hashes are recorded in `packaging/toolchain.json`. Verify Inno Setup's signature (Pyrsys B.V.) and Microsoft packaging-tool signatures before running. Tools are private build dependencies, not application payload. The script defaults to project-local `.tools/packaging` paths; supply `-Inno` and `-MakeAppx` for other installations.

```powershell
./publish.ps1
# Or use a fresh explicit staging folder:
./publish.ps1 -Output dist/Snipsera-candidate
```

The payload includes .NET; the installer checks for the Microsoft Visual C++ x64 runtime and offers its official download if missing. Setup installs per user, creates a Start menu shortcut and offers an optional desktop shortcut. It does not install a startup task or delete screenshot/recording data on uninstall.

## Current setup status

The personal Store name **Snipsera** is reserved as an MSIX/PWA app, product **9NFL9VHL8GKV**, publisher display name **Mo7**. It is in draft; no Store submission or certification has been completed. The EXE/MSIX build path is implemented. Candidate files remain unsigned until the selected signing routes are configured and accepted. SignPath acceptance and final installer/MSIX installation and runtime checks remain separate requirements.

The public downloads remain the original Framelet 0.6.0 stable and 0.7.0-preview.1 recording ZIPs. Do not replace their files with the new build. The existing GitHub publisher implements the historical ZIP release path; extend and verify its final EXE/Store delivery handling before publishing the first signed Snipsera release.

References: [Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), [Store registration](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account), [MSIX desktop packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion), [SignPath conditions](https://signpath.org/terms.html), [SignPath trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github).
