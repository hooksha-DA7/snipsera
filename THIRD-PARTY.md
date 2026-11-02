# Third-party components

Snipsera's own code is licensed under the [MIT licence](LICENSE). The components below retain their respective licences; Snipsera's licence does not replace their terms.

Snipsera recording uses ScreenRecorderLib 7.0.1 (MIT), maintained by Sverre Kristoffer Skodje. [Project and source](https://github.com/sskodje/ScreenRecorderLib) · [MIT license](docs/licenses/ScreenRecorderLib-MIT.txt).

GIF conversion uses an unmodified LGPL FFmpeg shared build supplied by BtbN. The executables and shared libraries remain separate in the `encoder` folder and may be replaced by a compatible build. [Build project and recipes](https://github.com/BtbN/FFmpeg-Builds) · [corresponding FFmpeg revision](https://github.com/FFmpeg/FFmpeg/tree/ad3b795b72) · [LGPL v3](docs/licenses/FFmpeg-LGPL-3.0.txt) · [incorporated GPL v3 terms](docs/licenses/FFmpeg-GPL-3.0.txt).

The exact downloaded archive, component hashes and version are recorded in `recording-dependencies.json`. The build verifies these hashes before copying the converter. Snipsera invokes it as a separate local process to convert a temporary MP4 into GIF; no converter is downloaded or installed at recording time.

Microsoft Media Foundation, Windows Graphics Capture and the Visual C++ runtime are Windows/Microsoft components. The Visual C++ runtime and .NET Desktop Runtime must be installed separately from their official Microsoft downloads.
