# Snipsera privacy policy

Effective date: 5 November 2026.

Snipsera captures screenshots and screen recordings on your Windows PC. It does not require an account. The app itself does not upload your captures, send analytics or use a cloud processing service.

## What stays on your PC

- Screenshots, recordings, editable projects and exports can contain anything visible in the selected screen area. Check a capture before sharing it.
- Microphone and computer audio are recorded only when you enable those options for an MP4 recording. They are off by default. GIF output does not include audio.
- History and preferences are stored in your local Windows profile. New installations use `%LOCALAPPDATA%\Snipsera`. An existing `%LOCALAPPDATA%\Framelet` folder may continue to be used after upgrading. A custom `--data-dir` takes precedence.
- Editing a capture automatically saves its original pixels, annotations and image settings in a local history project. History thumbnails and copies use the edited result.
- Editable projects retain the original image and reversible annotations. A masked region can still be recovered from an editable project; share a flattened image when you need to cover sensitive content.

Snipsera does not add its own encryption to these files. Access is governed by Windows file permissions and any encryption you enable separately. Files saved in a folder managed by another sync service may be uploaded by that service.

## Copying and sharing

Copy places an image on the Windows clipboard. Dragging or saving a capture makes it available to the destination you choose. Other apps, clipboard synchronization and cloud folders follow their own privacy settings and policies.

## Keeping and removing data

Screenshot history keeps the latest 100 captures. Removing or pruning an entry also removes its automatic history project. Images and editable projects saved separately, and recordings, may remain after their history entries disappear. Delete files you no longer need. Uninstalling Snipsera preserves your saved capture data; remove its data folder separately if you want to erase it. Windows backups, synchronization services and the Recycle Bin may retain their own copies.

## External services

From version 1.0.5, optional update checks contact the app's installation source: Microsoft Store for the Store app, or GitHub's public Snipsera release feed for an EXE/portable download. Automatic checks are on by default, run daily while Snipsera is open (retrying failed checks after an hour), and can be turned off in **Settings → Updates**. **Check now** makes a manual request. Checks do not send captures, recordings, microphone audio, file paths or analytics. GitHub receives an ordinary HTTPS request with an app user-agent; service providers can receive connection information such as your IP address. Microsoft Store requests use Windows' Store service and its policies.

The optional Start with Windows setting registers this app with Windows only when you enable it, so it can open quietly in the tray at sign-in. You can turn it off in app Settings or Windows Startup apps.

The app stores the last check time, available release and notification acknowledgement locally so it can avoid repeat alerts. Update buttons open Microsoft Store or the project's GitHub release page. Snipsera does not automatically download or run a GitHub installer; Microsoft Store's own update settings remain separate.

Downloading through GitHub or Microsoft Store is subject to those services' policies. A missing prerequisite may open Microsoft's official runtime download in your browser. These are separate from the app's local capture workflow.

## Contact and changes

Report privacy questions through [the project's issue tracker](https://github.com/hooksha-DA7/snipsera/issues). Do not attach private screenshots, recordings or personal information to a public issue. This policy will be updated if the app's data handling changes.
