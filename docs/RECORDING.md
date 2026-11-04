# Screen recording

Open **Record screen** from Home or the tray menu. The compact setup has two floating panels: source and capture options above, **Record GIF** and **Record Video** below.

1. Choose the **Area**, **Window**, **Display** or **Custom size** icon to select what to record. For an exact-size frame, enter even width and height in physical pixels, move it into place, then click or press Enter. Arrow keys nudge one pixel; Shift+arrow moves ten. Keep an area inside one display.
2. Use the cursor, click-highlight, microphone and computer-audio icons to toggle capture options. Audio is off by default. Both audio sources use the Windows default devices and work independently in MP4. GIF is silent; the app remembers your video audio choices for the session.
3. Open the sliders icon for **Quality**, **Frame rate**, **Resolution** and notification settings. Resolution keeps the source's proportions. Area frames trim at most one pixel from the right/bottom edge to use even H.264 dimensions without rescaling; custom frames require even dimensions so their size stays exact.
4. Choose **Record GIF** or **Record Video**. If you have not selected a source in this setup, the picker opens first. Escape cancels without starting a recording or falling back to an old source. The floating bar shows elapsed time, Pause/Resume, Stop and Discard. Discard requires a second click. The tray also shows elapsed time.
5. Stop to save. GIF conversion shows progress and can be canceled with the same two-click Discard action. Under recording settings, **Open** plays the latest result and **Show in folder** opens the output folder. Recordings and settings are separate from screenshot history, under `recordings` in the app's local data folder. Discarding removes only that recording's temporary files.

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

**Automatic Do not disturb is unavailable.** Enable it manually in Windows before recording; the setup panel's settings include a notification-settings shortcut. Snipsera does not change that setting or turn a manually enabled mode off afterward.

Microsoft restricts starting and stopping Focus sessions through its public API to approved applications using a Limited Access Feature token. This build has no such approval and does not use undocumented system-setting changes. [Microsoft API reference](https://learn.microsoft.com/en-us/uwp/api/windows.ui.shell.focussessionmanager?view=winrt-26100).

## Requirements and limits

- Windows 11 x64 and the [Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe). Current Snipsera packages include .NET. Windows N/KN installations also need Media Foundation through the Media Feature Pack.
- Extract the entire download. Keep `ScreenRecorderLib.dll` and the complete `encoder` folder alongside Snipsera's original runtime files.
- Keep a region inside one display. Choose a smaller output resolution for sources larger than the encoder permits. Protected content or a locked desktop may produce black frames or a capture error.
- Window mode records the selected window. Choose display/area mode when you also need separate windows or dialogs.
- Higher FPS and larger resolution use more CPU/storage. GIF conversion uses a temporary MP4 and two bounded-memory passes. Compact and Balanced use a sampled palette and faster ordered dithering; High analyses every frame and uses error-diffusion dithering for smoother gradients. The output keeps the selected FPS and resolution. For smaller, quicker GIFs, choose 15 FPS or 720p explicitly. Conversion still takes time after Stop.
- If microphone capture is denied, enable desktop-app microphone access yourself in Windows privacy settings. Snipsera does not bypass that permission.

The recording bar is excluded through Windows capture affinity. If Windows refuses the exclusion request, the bar is hidden and the tray remains available for Pause/Stop. Cursor/click capture is local; the click hook observes mouse-button positions only while that option is enabled. It neither injects input nor records keystrokes. No recordings are uploaded.

[Recording verification](RECORDING-VERIFICATION.md) · [User guide](USER-GUIDE.md) · [Component licenses](../THIRD-PARTY.md).
