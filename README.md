# Snipsera

Screenshots and screen recording for Windows 11. Capture part of your screen, mark it up, and copy or save it. Pin references beside your work, or record a window, display or area as MP4 or GIF.

Snipsera is the new name for Framelet. The downloads below are the existing Framelet releases; the first Snipsera build is being prepared.

## Download and run

<!-- FRAMELET_DOWNLOADS_START -->
[Download for Windows (0.6.0)](https://github.com/hooksha-DA7/snipsera/releases/download/v0.6.0/Framelet-0.6.0-win-x64.zip) · [Try the preview (0.7.0-preview.1)](https://github.com/hooksha-DA7/snipsera/releases/tag/v0.7.0-preview.1) · [All releases](RELEASES.md)
<!-- FRAMELET_DOWNLOADS_END -->

1. Install the [.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
2. Download and extract the Windows ZIP.
3. Open **Framelet.exe**. Keep the extracted files together.

Version **0.6.0** is the stable screenshot release. The **0.7 preview** adds screen recording and also needs the [Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe). Extract its complete `encoder` folder with the app.

The existing downloads are portable and currently unsigned. Snipsera runs locally, without an account or cloud service.

## Take a screenshot

| Action | Default shortcut |
| --- | --- |
| Select an area | Ctrl+Alt+Shift+1 |
| Capture a window | Ctrl+Alt+Shift+2 |
| Capture a display | Ctrl+Alt+Shift+3 |
| Repeat the last area | Ctrl+Alt+Shift+4 |

You can also capture from the home window or tray menu. Change the shortcuts under **Keyboard shortcuts** on the home screen.

A small preview appears at the bottom left. Hover over it to **Copy**, **Save**, **Pin** or **Annotate**. Take another screenshot whenever you need one; the previous capture stays in history.

## Record your screen

In the 0.7 preview, choose **Record screen**, select a window, display or area, and choose MP4 or GIF. Set quality, FPS and resolution, then enable cursor/click highlighting or MP4 audio if needed. Start, pause/resume, then Stop to save. The bar and tray show elapsed time; the result offers Open and Show in folder.

Do not disturb must be enabled manually in Windows for this preview. [Recording help](docs/RECORDING.md).

## Work with your captures

- **Capture scrolling pages.** Select the content, start capture, then scroll manually or turn on Auto-scroll. Choose Done to keep one tall image.
- **Clear your workspace.** Scroll down over a preview or pin to hide the group. Hover or click the small left-edge tab to bring it back.
- **Pin references.** Keep up to eight images on top of other windows. Drag or resize a pin, or adjust its opacity with Ctrl+mouse wheel.
- **Annotate and edit.** Add arrows, shapes, text, numbered steps, highlights or freehand marks. Crop, resize and add a background, with undo and redo.
- **Copy, save or drag.** Copy to the clipboard, save a PNG/JPEG from the editor, or drag an image into another app.
- **Keep an editable project.** Save a `.framelet` file to return to your annotations later.
- **Find recent screenshots.** History keeps your latest 100 captures on this PC. Save anything you want to keep permanently.

Closing the home window leaves the app in the tray. Use its **Quit** command to exit. Quit an older copy before starting another version so the capture shortcuts are available.

## More help

[User guide](docs/USER-GUIDE.md) · [Release notes and downloads](RELEASES.md) · [Build from source](docs/DEVELOPMENT.md) · [Report a problem](https://github.com/hooksha-DA7/snipsera/issues) · [Privacy](PRIVACY.md) · [Signing status](docs/CODE-SIGNING.md)

Snipsera is an independent Windows app inspired by CleanShot's workflow. It is not affiliated with CleanShot.

## Licence

Snipsera's own code is available under the [MIT licence](LICENSE). Bundled third-party components retain their [respective licences](THIRD-PARTY.md).
