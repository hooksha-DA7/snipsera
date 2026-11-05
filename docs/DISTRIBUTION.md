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

`publish.ps1` prepares an EXE installer, a Store-submission MSIX, portable/source ZIPs and SHA256 checksums in a fresh private folder. It does not upload, sign, submit or install them. The app version comes from the project; the corresponding four-part MSIX version uses a zero final field. The current official build is 1.0.2-preview.2 (display version 1.0.2) / Store MSIX 1.0.2.0. Use a fresh package version when Store validation has already associated an earlier version with different contents.

The assigned personal Store identity is in `packaging/store-identity.json`. Do not replace it with a guessed publisher. `-DevelopmentMsix` instead creates an explicitly labelled local-test identity and must never be used for a Store submission.

The build needs the Inno Setup 7.1.1 compiler and Windows SDK BuildTools 10.0.28000.2957. Their official source URLs and checked archive hashes are recorded in `packaging/toolchain.json`. Verify Inno Setup's signature (Pyrsys B.V.) and Microsoft packaging-tool signatures before running. Tools are private build dependencies, not application payload. The script defaults to project-local `.tools/packaging` paths; supply `-Inno` and `-MakeAppx` for other installations.

```powershell
./publish.ps1
# Or use a fresh explicit staging folder:
./publish.ps1 -Output dist/Snipsera-candidate
```

The payload includes .NET; the installer checks for the Microsoft Visual C++ x64 runtime and offers its official download if missing. Setup installs per user, creates a Start menu shortcut and offers an optional desktop shortcut. From 1.0.5, startup is an opt-in app setting: the Store MSIX declares a disabled startup task; EXE/portable installations register a per-user Run entry only when enabled. Uninstall removes only the exact EXE copy's startup entry and preserves screenshot/recording data. Windows user and policy restrictions remain authoritative.

The Store MSIX declares `Microsoft.VCLibs.140.00.UWPDesktop` version `14.0.33321.0` or later so Microsoft Store can install the recorder's C++ runtime dependency. The minimum comes from Microsoft's signed x64 Desktop framework package, whose exports were checked against the native recorder's imported C++ symbols. This dependency declaration does not replace final clean Windows 11 and packaged-recording acceptance. Sideload tests must install the Microsoft framework separately. See [Microsoft's desktop packaging guidance](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-prepare).

## Current setup status

Snipsera **1.0.4** is the latest GitHub preview, with all-in-one screenshot capture. Its EXE, portable/source ZIPs and checksums use the exact reviewed hosted build. Store MSIX **1.0.4.0** is prepared but not submitted. It remains submission-only; this preview does not replace the separately submitted 1.0.3.0 Store update or promote itself based on that update's approval.

Update on 5 November 2026: **1.0.3.0** was submitted as Submission 3 (`1152921505702318460`) and is in certification. Automatic publication after approval is enabled. The submitted MSIX comes from reviewed source `1c9bdf8d70488bf4199dea2a608b028e2dcac394` and has SHA256 `aca5595380464b6b8ce8c7cf1210cd5f7087901e68979a2081a7888b1b3fb1a0`. GitHub **1.0.3 remains a preview** until Microsoft approves this exact update. The current live Store/GitHub release remains **1.0.2**. Published tags, downloads and hashes are unchanged; live desktop and final clean Windows 11/installed-MSIX acceptance remain pending.

The personal Store app **Snipsera**, product **9NFL9VHL8GKV**, publisher display name **Mo7**, was submitted for certification on 2 November 2026 with package version **1.0.1.0**. The submission includes an English listing, three synthetic-content app screenshots, free pricing, privacy/support links, age ratings and the full-trust capability explanation. Certification passed and the maintainer requested publication on 4 November 2026. Partner Center now shows **In Microsoft Store** and provides the [public download link](https://apps.microsoft.com/detail/9NFL9VHL8GKV).

The certification notes disclose the passed automated/hosted installer checks and the remaining clean Windows 11 and installed-MSIX recording acceptance. The maintainer requested Store publication while those final checks remained pending. Store certification does not establish independent installed-package runtime acceptance. The 1.0.2.0 update was submitted on 4 November 2026 (submission 1152921505702310050), passed certification and published automatically under the maintainer's explicit approval. Partner Center's live Store presence and read-only package view confirm Submission 2 and its exact 1.0.2.0 package. The matching GitHub build is the official release under the promotion procedure in RELEASING.md.

The SignPath application was submitted on 2 November 2026. The protected GitHub signing environment and hosted build workflow are prepared. Acceptance, provider configuration, credentials and a verified live signing request remain pending. The workflow builds and checks unsigned EXE/MSIX/source/portable candidates and tests installer installation, reinstall, an increasing-version fixture upgrade and uninstall on Windows Server 2025. Clean Windows 11 and final packaged capture/recording checks remain separate requirements.

Snipsera **1.0.2** is the official GitHub release, promoted from immutable build/tag **1.0.2-preview.2** after matching Store approval, with the same EXE installer, portable ZIP, source and checksums. Its notice identifies installer signing and final Windows 11 acceptance as pending. All historical downloads are preserved. The publisher validates signed releases or the guarded unsigned paths with matching Store approval required for official release classification, and separately tracks the Store submission without offering it as a public unsigned MSIX download. A live signed publication still requires approved provider configuration. See [release workflow](RELEASE-WORKFLOW.md) and [Windows acceptance](WINDOWS-ACCEPTANCE.md).

References: [Microsoft signing options](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/code-signing-options), [Store registration](https://learn.microsoft.com/en-us/windows/apps/publish/partner-center/open-a-developer-account), [MSIX desktop packaging](https://learn.microsoft.com/en-us/windows/msix/desktop/desktop-to-uwp-manual-conversion), [SignPath conditions](https://signpath.org/terms.html), [SignPath trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github).
