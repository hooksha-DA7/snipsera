# Recording preview 1

Build: 0.7.0-preview.1, 2026-11-02.

Adds screen recording as MP4 H.264 or GIF, source selection, quality/FPS/resolution, microphone and computer audio, cursor controls, a source-aligned click ring, pause/resume, elapsed time and separate recording history.

All 125 original capture/annotation checks, the foreground native capture lifecycle, 11 recording rules and 12 publishing checks passed. Native window/display/region recording, two-pass GIF output, an authorized five-second combined audio test and a real click-highlight test passed. Media files were checked independently for codec, dimensions, frame rate and duration. Controller integration preserves existing pins and screenshot history. See [recording verification](docs/RECORDING-VERIFICATION.md) for evidence and limits.

Automatic Do not disturb is unavailable; enable it manually in Windows. This preview needs .NET 10 Desktop Runtime x64 and the Visual C++ x64 runtime. Extract the full package, including ScreenRecorderLib.dll and the encoder folder. Quit older Framelet copies before starting it.

The stable download remains 0.6.0. Historical archives are unchanged.
