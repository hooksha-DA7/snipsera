# Snipsera

Screenshots and screen recording for Windows 11. Capture part of your screen, mark it up, and copy or save it. Pin references beside your work, or record a window, display or area as MP4 or GIF.

Snipsera is the new name for Framelet. Get it from Microsoft Store or try the latest GitHub preview below. Historical Framelet downloads are still available.

## Download and run

<!-- FRAMELET_DOWNLOADS_START -->
[Get Snipsera from Microsoft Store](https://apps.microsoft.com/detail/9NFL9VHL8GKV) · [Download Snipsera preview (1.0.1-preview.1)](https://github.com/hooksha-DA7/snipsera/releases/download/v1.0.1-preview.1/Snipsera-1.0.1-preview.1-setup-win-x64.exe) · [Portable ZIP](https://github.com/hooksha-DA7/snipsera/releases/download/v1.0.1-preview.1/Snipsera-1.0.1-preview.1-win-x64.zip) · [Stable 0.6.0](https://github.com/hooksha-DA7/snipsera/releases/tag/v0.6.0) · [All releases](RELEASES.md)
<!-- FRAMELET_DOWNLOADS_END -->

Install Snipsera from **Microsoft Store** above, then open it from the Start menu. If you prefer the GitHub download, run the **EXE installer** and follow the setup steps.

The installer includes .NET. Screen recording needs the [Microsoft Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe); setup checks for it and offers the official download when it is missing.

For the portable version, extract the complete ZIP and open **Snipsera.exe**. Keep its files and `encoder` folder together.

**The GitHub EXE and portable ZIP are unsigned previews.** Windows may show a warning when you download or start them. The Store release is currently 1.0.1; the latest preview changes are in the GitHub download. Installer code signing is pending; see the [release notes](releases/notes/v1.0.1-preview.1.md) for current limits. Snipsera is also available from Microsoft Store. [Code signing policy](docs/CODE-SIGNING.md).

Snipsera runs locally, without an account or cloud service. Older Framelet downloads remain available in the [release history](RELEASES.md).

## Take a screenshot

| Action | Default shortcut |
| --- | --- |
| Select an area | Ctrl+Alt+Shift+1 |
| Capture a window | Ctrl+Alt+Shift+2 |
| Capture a display | Ctrl+Alt+Shift+3 |
| Repeat the last area | Ctrl+Alt+Shift+4 |

You can also capture from the home window or tray menu. In the latest preview, change them under **Keyboard shortcuts**: select a box, press your keys and save. You can use **Print Screen** on its own.

A compact preview appears at the bottom left. In the latest preview, docked screenshots use matching cards with a tight gap. Hover over it to **Copy**, **Save**, **Pin** or **Annotate**. Take another screenshot whenever you need one; the previous capture stays in history.

## Record your screen

Choose **Record screen**, select a window, display or area, and choose MP4 or GIF. Set quality, FPS and resolution, then enable cursor/click highlighting or MP4 audio if needed. Start, pause/resume, then Stop to save. The bar and tray show elapsed time; the result offers Open and Show in folder.

Do not disturb must be enabled manually in Windows for this preview. [Recording help](docs/RECORDING.md).

## Work with your captures

- **Capture scrolling pages.** Select the content, start capture, then scroll manually or turn on Auto-scroll. Choose Done to keep one tall image.
- **Clear your workspace.** Scroll down over a preview or pin to hide the group. Hover or click the small left-edge tab to bring it back.
- **Pin references.** Keep up to eight images on top of other windows. Drag or resize a pin, or adjust its opacity with Ctrl+mouse wheel.
- **Annotate and edit.** Add arrows, shapes, text, numbered steps, highlights or freehand marks. Crop, resize and add a background, with undo and redo.
- **Copy, save or drag.** Copy to the clipboard, save a PNG/JPEG from the editor, or drag an image into another app.
- **Keep your edits.** The latest preview saves edits in history automatically when you close the editor. Save a separate `.snipsera` project to keep an editable copy permanently; legacy `.framelet` files still open.
- **Find recent screenshots.** History keeps your latest 100 captures on this PC. Save anything you want to keep permanently.

Closing the home window leaves the app in the tray. Use its **Quit** command to exit. Quit an older copy before starting another version so the capture shortcuts are available.

## More help

[User guide](docs/USER-GUIDE.md) Ã‚Â· [Release notes and downloads](RELEASES.md) Ã‚Â· [Build from source](docs/DEVELOPMENT.md) Ã‚Â· [Report a problem](https://github.com/hooksha-DA7/snipsera/issues) Ã‚Â· [Privacy](PRIVACY.md) Ã‚Â· [Signing status](docs/CODE-SIGNING.md)

Snipsera is an independent Windows app inspired by CleanShot's workflow. It is not affiliated with CleanShot.

## Licence

Snipsera's own code is available under the [MIT licence](LICENSE). Bundled third-party components retain their [respective licences](THIRD-PARTY.md).
