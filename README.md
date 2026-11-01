# Framelet

A screenshot app for Windows 11. Capture part of your screen, mark it up, and copy or save it. Pin images beside your work when you need a reference.

## Download and run

<!-- FRAMELET_DOWNLOADS_START -->
[Download for Windows (0.5.2)](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.2/Framelet-0.5.2-win-x64.zip) · [Try the preview (0.6.0-preview.4)](https://github.com/hooksha-DA7/framelet/releases/tag/v0.6.0-preview.4) · [All releases](RELEASES.md)
<!-- FRAMELET_DOWNLOADS_END -->

1. Install the [.NET 10 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).
2. Download and extract the Windows ZIP.
3. Open **Framelet.exe**. Keep the extracted files together.

Use **0.5.2** for capture and annotation. The **0.6 previews** add scrolling capture and temporary overlay hiding, but those features still need testing on a real desktop.

Framelet is portable and currently unsigned. It runs locally, without an account or cloud service.

## Take a screenshot

| Action | Default shortcut |
| --- | --- |
| Select an area | Ctrl+Alt+Shift+1 |
| Capture a window | Ctrl+Alt+Shift+2 |
| Capture a display | Ctrl+Alt+Shift+3 |
| Repeat the last area | Ctrl+Alt+Shift+4 |

You can also capture from the home window or tray menu. Change the shortcuts under **Keyboard shortcuts** on the home screen.

A small preview appears at the bottom left. Hover over it to **Copy**, **Save**, **Pin** or **Annotate**. Take another screenshot whenever you need one; the previous capture stays in history.

## Work with your captures

- **Pin references.** Keep up to eight images on top of other windows. Drag or resize a pin, or adjust its opacity with Ctrl+mouse wheel.
- **Annotate and edit.** Add arrows, shapes, text, numbered steps, highlights or freehand marks. Crop, resize and add a background, with undo and redo.
- **Copy, save or drag.** Copy to the clipboard, save a PNG/JPEG from the editor, or drag an image into another app.
- **Keep an editable project.** Save a `.framelet` file to return to your annotations later.
- **Find recent screenshots.** History keeps your latest 100 captures on this PC. Save anything you want to keep permanently.

Closing the home window leaves Framelet in the tray. Use **Quit Framelet** from its tray menu to exit. Quit an older copy before starting another version so the capture shortcuts are available.

## More help

[User guide](docs/USER-GUIDE.md) · [Release notes and downloads](RELEASES.md) · [Build from source](docs/DEVELOPMENT.md) · [Report a problem](https://github.com/hooksha-DA7/framelet/issues)

Framelet is an independent Windows app inspired by CleanShot's workflow. It is not affiliated with CleanShot.
