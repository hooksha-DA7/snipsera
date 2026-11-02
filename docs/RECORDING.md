# Screen recording

Available in the 0.7 recording preview. Open **Record screen** from Home or the tray menu.

1. Choose **Area**, **Window**, **Display** or **Custom size**, then use the selection button. For a custom frame, enter even pixel dimensions, move it into place, then click or press Enter. Arrow keys nudge one pixel; Shift+arrow moves ten.
2. Choose **MP4 video** or **GIF animation**, quality, frame rate and resolution. Resolution keeps the source's proportions. Original area captures are rounded to even output dimensions for H.264; custom frames require even dimensions so their size remains exact.
3. Set **Show cursor**, **Highlight clicks** and, for MP4, **Microphone** and **Computer audio**. Audio is off by default. Both sources use the Windows default devices and can be enabled independently. GIF has no sound.
4. Choose **Start recording**. The compact bar shows elapsed time, Pause/Resume, Stop and Discard. Discard requires a second click. The tray tooltip and Stop recording menu item also show elapsed time.
5. Stop to save. **Open** plays the latest result; **Show in folder** opens the output folder. Recordings and settings are stored separately from screenshot history in the app's local data folder, under `recordings`. Discarding removes only that recording's temporary files.

While recording, screenshot commands are unavailable. Stop first to take another screenshot. Existing previews, pins and editors return after recording with their original state. A window recording stops if its source closes or is minimized.

## Options

| Control | Choices |
| --- | --- |
| Source | One window, a full display, a selected area or an exact-size frame |
| Format | MP4 with H.264 video and optional AAC audio; animated GIF |
| Quality | Compact, Balanced, High |
| FPS | 5, 10, 15, 24, 30 or 60; GIF up to 30 |
| Resolution | Original size, fit within 1080p, fit within 720p, or half size |
| Audio | Microphone, computer audio, either or both; MP4 only |
| Pointer | Show/hide cursor; optional thin blue expanding click ring |

## Do not disturb

**Automatic Do not disturb is unavailable in this preview.** Enable it manually in Windows before recording; the setup window includes a notification-settings shortcut. Snipsera does not change that setting or turn a manually enabled mode off afterward.

Microsoft restricts starting and stopping Focus sessions through its public API to approved applications using a Limited Access Feature token. This build has no such approval and does not use undocumented system-setting changes. [Microsoft API reference](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager?view=winrt-26100).

## Requirements and limits

- Windows 11 x64, .NET 10 Desktop Runtime x64 and the [Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe). Windows N/KN installations also need Media Foundation through the Media Feature Pack.
- Extract the entire download. Keep `ScreenRecorderLib.dll` and the complete `encoder` folder alongside Snipsera's original runtime files.
- Keep a region inside one display. Choose a smaller output resolution for sources larger than the encoder permits. Protected content or a locked desktop may produce black frames or a capture error.
- Window mode records the selected window. Choose display/area mode when you also need separate windows or dialogs.
- Higher FPS, larger resolution and High quality use more CPU/storage. GIF conversion uses a temporary MP4 and two bounded-memory conversion passes, so it takes extra time and disk space after Stop.
- If microphone capture is denied, enable desktop-app microphone access yourself in Windows privacy settings. Snipsera does not bypass that permission.

The recording bar is excluded through Windows capture affinity. If Windows refuses the exclusion request, the bar is hidden and the tray remains available for Pause/Stop. Cursor/click capture is local; the click hook observes mouse-button positions only while that option is enabled. It neither injects input nor records keystrokes. No recordings are uploaded.

[Recording verification](RECORDING-VERIFICATION.md) · [User guide](USER-GUIDE.md) · [Component licenses](../THIRD-PARTY.md).
