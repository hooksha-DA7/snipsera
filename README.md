<p align="center">
  <a href="https://snipsera.com"><img src="https://snipsera.com/assets/readme/banner.png" alt="Snipsera: screenshots, annotations and screen recording for Windows 11" width="880"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9NFL9VHL8GKV"><img src="https://get.microsoft.com/images/en-us%20dark.svg" alt="Get it from Microsoft" height="52"></a>
</p>

<p align="center">
  <a href="https://github.com/hooksha-DA7/snipsera/releases"><img src="https://img.shields.io/github/v/release/hooksha-DA7/snipsera?include_prereleases&label=release&color=08707F" alt="Latest release"></a>
  <a href="https://github.com/hooksha-DA7/snipsera/releases"><img src="https://img.shields.io/github/downloads/hooksha-DA7/snipsera/total?color=08707F" alt="Downloads"></a>
  <img src="https://img.shields.io/badge/Windows%2011-x64-0F2233" alt="Windows 11 x64">
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-0F2233" alt="MIT licence"></a>
</p>

<p align="center">
  <b>Capture part of your screen, mark it up, and copy or save it in seconds.</b><br>
  Free and open source. No account, no cloud, no tracking: your captures stay on your PC.<br>
  <a href="https://snipsera.com">Website</a> · <a href="https://snipsera.com/features/">Features</a> · <a href="docs/USER-GUIDE.md">User guide</a> · <a href="RELEASES.md">Releases</a>
</p>

<p align="center">
  <img src="https://snipsera.com/assets/readme/demo-annotate.gif" alt="Drawing an arrow and typing a label on a screenshot in the Snipsera editor" width="880">
</p>

## Download

<!-- FRAMELET_DOWNLOADS_START -->
[Get Snipsera from Microsoft Store](https://apps.microsoft.com/detail/9NFL9VHL8GKV) · [Download Snipsera (2.0.0)](https://github.com/hooksha-DA7/snipsera/releases/download/v2.0.0/Snipsera-2.0.0-setup-win-x64.exe) · [Portable ZIP](https://github.com/hooksha-DA7/snipsera/releases/download/v2.0.0/Snipsera-2.0.0-win-x64.zip) · [All releases](RELEASES.md)
<!-- FRAMELET_DOWNLOADS_END -->

**Microsoft Store** is the simplest way to install and keep Snipsera updated. The **EXE installer** from GitHub includes .NET; screen recording needs the [Microsoft Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe), and setup offers it if it's missing. For the portable version, extract the whole ZIP and open **Snipsera.exe**.

The GitHub EXE and portable ZIP are unsigned, so Windows may warn you when you download or start them ([signing status](docs/CODE-SIGNING.md)). New GitHub versions are marked as previews until the matching Microsoft Store update is approved.

## Features

<table>
  <tr>
    <td width="50%" valign="top">
      <img src="https://snipsera.com/assets/readme/feature-preview.png" alt="A floating capture preview with Copy and Save buttons">
      <h3>Always one drag away</h3>
      Every capture lands in a small preview. Copy, save, pin or annotate it, or drag the image straight into any app.
    </td>
    <td width="50%" valign="top">
      <img src="https://snipsera.com/assets/readme/feature-stack.png" alt="Three captures layered into a compact stack">
      <h3>A stack, not a mess</h3>
      New captures layer up neatly and earlier ones wait behind. Pin up to eight references above your other windows.
    </td>
  </tr>
  <tr>
    <td width="50%" valign="top">
      <img src="https://snipsera.com/assets/readme/feature-scrolling.png" alt="A long article captured as one tall image">
      <h3>Scrolling capture</h3>
      Capture articles, chats and documents that run past your screen as one tall image. Scroll yourself or turn on Auto-scroll.
    </td>
    <td width="50%" valign="top">
      <img src="https://snipsera.com/assets/readme/feature-recording.png" alt="The compact screen recording toolbar">
      <h3>Screen recording</h3>
      Record an area, a window or a display as MP4 or GIF, with optional microphone and computer audio, click highlights and pause.
    </td>
  </tr>
</table>

<p align="center">
  <img src="https://snipsera.com/assets/readme/demo-background.gif" alt="Adjusting padding in the Background panel while the framed image updates live" width="880">
</p>

**Also included:** arrows in four styles, text in seven styles, numbered steps, highlighter, blur and pixelate, crop and resize, backgrounds with 15 gradients, combining several screenshots in one image, a local history of your latest 100 captures, and a look that follows Windows 11's light, dark and accent colors. [See every feature](https://snipsera.com/features/).

## Quick start

| Action | Default shortcut |
| --- | --- |
| Select an area | Ctrl+Alt+Shift+1 |
| Capture a window | Ctrl+Alt+Shift+2 |
| Capture a display | Ctrl+Alt+Shift+3 |
| Repeat the last area | Ctrl+Alt+Shift+4 |
| Open all-in-one capture | Ctrl+Alt+Shift+5 |
| Reopen last dismissed capture | Ctrl+Alt+Shift+6 |

You can also capture from the home window or the tray menu. Change shortcuts under **Settings > Keyboard shortcuts**; **Print Screen** works on its own and is a good fit for the **All-in-one capture** bar.

A preview appears at the bottom left. Hover over it to **Copy**, **Save**, **Pin** or **Annotate**, or right-click it for more, including **Show in folder**. Edits are saved to History automatically; save a `.snipsera` project to keep an editable copy permanently (legacy `.framelet` files still open).

To record, choose **Record screen**, pick a window, display or area, then **Record GIF** or **Record Video**. Closing the home window keeps Snipsera in the tray; use **Quit** there to exit.

## Help

[User guide](docs/USER-GUIDE.md) · [Recording help](docs/RECORDING.md) · [Updates and startup](docs/UPDATES-AND-STARTUP.md) · [Support](https://snipsera.com/support/) · [Report a problem](https://github.com/hooksha-DA7/snipsera/issues) · [Privacy](PRIVACY.md) · [Build from source](docs/DEVELOPMENT.md)

In the app, **Settings > About** has the version and these links, and the tray menu has **Help and support**.

Snipsera is the new name for Framelet; older Framelet downloads remain in the [release history](RELEASES.md). It's an independent Windows app inspired by CleanShot's workflow and is not affiliated with CleanShot.

## Licence

Snipsera's own code is available under the [MIT licence](LICENSE). Bundled third-party components retain their [respective licences](THIRD-PARTY.md).
