# Recording preview verification

Build: 0.7.0-preview.1, 2026-11-02. This is a recording preview; 0.6.0 remains the stable download. Automated checks, native recordings and hands-on input are listed separately below.

## Verified recording paths

- Native H.264 MP4 and animated GIF from an isolated animated window. An independent media probe confirmed dimensions, frame counts and 15 FPS MP4 output. A 600 ms pause was excluded from the video duration, and decoded frames retained the changing animation.
- Full-display and exact 640 Ã— 360 region recordings from a synthetic cover screen, with audio off. Media probes confirmed H.264, expected output dimensions and 30 FPS. Cursor-on/off settings were exercised; pointer movement during the display comparison means it is not a stationary byte-exact test.
- One explicitly authorized five-second microphone + computer-audio test. The resulting 4.999934-second file contains H.264 video and stereo AAC at 48 kHz. This checks the combined path; no claim is made about separate per-device signal isolation or listening quality. The audio file remains private and is excluded from publication.
- Recording-controller integration: pause clock, tray elapsed time, duplicate Stop coalescing, two-pass MP4/GIF saving, discarded captures staying out of history, separate recording/screenshot histories, startup-failure recovery, automatic stopping when the source window closes, and retained pin window/bounds. Windows accepted capture exclusion for the recording bar.
- Physical click: the tester clicked the synthetic source's test button, confirmed by a mouse-down event in that separate source process. Decoded video showed a native cursor and a correctly centered thin blue expanding/fading ring. A separate half-resolution renderer probe placed the ring at the expected output coordinate, including the window's nonzero screen origin.

The first native-library highlight test failed: its filled dot was displaced by the window origin. That path is disabled. Framelet now supplies its own source-relative ring overlay and observes clicks through a short-lived mouse hook, without injecting input. Failed and passing receipts are retained.

## Compatibility and remaining coverage

Settings/validation checks cover supported rates, GIF/audio incompatibility, H.264 sizing, output limits, separate persistence, path restrictions, native-library loading and click-coordinate mapping. The original screenshot/annotation suite passed all 125 checks on this build. The native capture lifecycle also passed with foreground input, including tray keyboard focus/history, repeated captures, dirty-editor/modal queuing and hidden-group restoration. There are 11 recording rule/native-library checks and 12 publishing checks.

Setup/bar renders are checked at normal and narrow sizes. Those renders validate layout, not native Mica pixels. Native display/theme/accessibility coverage remains limited to this Windows environment. Protected media, multi-device audio routing, long-duration performance and every high-DPI/mixed-adapter combination are not certified.

Automatic Windows Do not disturb remains unavailable because the required Microsoft Limited Access Feature approval is absent. The app explains that limitation and offers a manual settings shortcut; no automated DND success is claimed.

Detailed local receipts are under `verification/runs/recording-070-*`. Source videos, audio, test images, private design discussions and development dependencies are excluded from Git and source packages. Runtime packages contain only reviewed dependencies, licenses, documentation and application files.
