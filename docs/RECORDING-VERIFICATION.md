# Recording verification

The 0.7.0-preview.1 evidence below is historical, from 2 November 2026. It does not certify later recording changes.

## 1.0.3 recording update — 4 November 2026

The focused no-window release suite passed 167 checks: 131 C# checks and 36 Python checks. This includes the existing screenshot, annotation, scrolling, overlay, recording-rule and release-workflow coverage. No compiler warnings or errors were reported. The normal NuGet audit ran with network access; its first restricted-network attempt failed before tests and was retried without disabling auditing.

New checks cover physical-pixel drag geometry on negative-origin screens, monitor-local crop offsets, even alignment of odd crops, the display's final row/column, cross-display rejection, accepted/canceled/failed target selection, and the explicit recorder crop and output canvas. A separate native image-source test encoded a generated pattern to H.264 and independently decoded it: all four corners and the center came from the selected region. That check used original resolution; it did not exercise WindowsGraphicsCapture or live mouse selection.

The actual GIF converter generated Compact, Balanced and High GIFs from a six-second synthetic H.264 clip. All outputs retained 640 × 360, all 180 frames and six-second timing. Conversion progress stayed within 0–100% and moved forward. Cancellation preserved the MP4 and cleaned up its palette; existing output files were refused. A controller-level name-collision check also preserved the existing GIF and recovery MP4. Video audio choices survive reopening setup during the same app session without enabling audio in GIF. Setup controls were rendered offscreen in light/dark at 100/125/150/200%, with settings open and closed. Visible controls had accessible names and did not overlap or clip. Conversion and long-error bar states kept their controls visible, including Discard while Stop was pending.

In one controlled synthetic 30-FPS benchmark, the old Balanced conversion took 10.8 seconds at 1280 × 720 and 27.4 seconds at 1920 × 1080. Sampled palette analysis plus ordered dithering took 3.5 and 8.7 seconds respectively, retaining all 180 output frames. These are measurements on the development machine, not performance guarantees; scene motion, dimensions and hardware affect time. High keeps full-frame palette analysis and error-diffusion dithering. [FFmpeg palette documentation](https://ffmpeg.org/ffmpeg-all.html#palettegen), [palette-use options](https://ffmpeg.org/ffmpeg-all.html#paletteuse).

Live desktop/global-input testing remains paused. The user's wrong-area report has not been independently reproduced through WindowsGraphicsCapture on their desktop. Mixed-DPI pointer selection, native capture-affinity appearance, physical cursor/click behavior and final installed-MSIX recording acceptance remain pending. Historical native results below are separate from this update's offscreen and synthetic checks.

## Historical recording verification

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
