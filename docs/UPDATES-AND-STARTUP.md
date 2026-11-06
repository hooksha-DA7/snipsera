# Updates and Windows startup

Available from Snipsera 1.0.5. In 1.0.6, open **Settings** on Home or in the tray menu, then **General** for startup or **Updates** for update preferences. [Save locations and keyboard shortcuts](SETTINGS.md) have their own sections.

## Update checks

Automatic checks are on by default and run daily while the app is running; failed requests retry after an hour. **Check now** works even when automatic checks are off. A known update stays visible on Home across app restarts, until a new check clears it or the installed version changes. Tray alerts are acknowledged once per available build and deferred during capture, recording and modal dialogs. Windows can suppress notifications; the Home notice remains available.

Store installations use `StoreContext.GetAppAndOptionalStorePackageUpdatesAsync` and filter for Snipsera's assigned package identity. They open Snipsera's Microsoft Store page to update. The API identifies a package with an update available, so the UI does not invent a target version. Other optional/framework package updates are not advertised as Snipsera updates.

EXE and portable installations read the public `hooksha-DA7/snipsera` GitHub release feed without an account or credentials. They choose the highest newer version that includes the expected Windows EXE installer. Drafts, missing installers and older versions are excluded. GitHub's prerelease flag determines preview status, including the historical approved `v1.0.2-preview.2` build. **Include preview releases** is on by default and can be turned off. No installer is downloaded or executed automatically. The action opens the fixed project's release page.

Failed or offline checks display an error and keep any known update; they do not report success. Responses, request duration and cached preferences are bounded. Changing the preview preference during a request discards that request's result. App preferences are separate from capture shortcuts and stored in `app-preferences.json`. See [privacy](../PRIVACY.md).

Turning off checks in Snipsera does not disable Microsoft Store's own automatic updates. An app downloaded before 1.0.5 must first be updated manually or through Microsoft Store to gain these controls.

## Start with Windows

This is **off by default**. Enabling it starts Snipsera with `--startup` after Windows sign-in, with the Home window hidden and capture hotkeys available. Starting a second copy this way does not bring the existing app forward. Double-click the tray icon to open Home.

Store packages declare the disabled `SnipseraStartup` task and use the Windows `StartupTask` API. EXE/portable downloads use only their own `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` value, `SnipseraStartup`, with a quoted path to this copy of `Snipsera.exe`. Turning it off does not remove another copy's startup command. Keep a portable copy in its registered location. EXE uninstall removes a matching owned command.

Windows user or administrator policy restrictions take priority. A disabled or policy-controlled setting is explained with a Windows Startup apps shortcut. The unpackaged adapter reads Windows' StartupApproved value conservatively; its binary layout is an implementation detail rather than a documented Microsoft contract. It never writes or deletes that approval. After reinstall, a previously disabled entry can be listed again, but Windows must still enable it.

Isolated `--data-dir` test profiles cannot change real Windows startup settings or contact production update services. Other development package identities do not use the public update channels.

## Verification and remaining checks

The release gate exercises release ordering, previews, offline/error states, request coalescing, cancellation, notification acknowledgement, cached notices, startup ownership/quoting and Windows state mapping. It renders detached settings controls and update notices in light/dark themes at several scales, without desktop input. Startup tests use injected storage and state providers. The optional `--update-network-check` uses the production GitHub reader for anonymous public metadata requests and is separate from the offline gate.

The hosted installer check verifies that install does not enable startup, uninstall removes its own command and another copy's command is preserved. These synthetic checks do not establish real sign-in startup, tray notifications or Store API acceptance. Live Windows 11 sign-in, installed Store package updates/startup and desktop capture/recording remain pending while native testing is paused. No startup setting was enabled on the user's PC during development.

References: [Store package updates](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/package-updates-from-store), [StartupTask](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask), [desktop startup manifest](https://learn.microsoft.com/en-us/uwp/schemas/appxpackage/uapmanifestschema/element-desktop-startuptask), [Run keys](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys), [WinRT APIs from desktop .NET](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/winrt-apis-desktop-apps).
