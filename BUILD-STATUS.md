# Snipsera build status

Preview candidate: **1.0.3**, 4 November 2026. The current official Store/GitHub release is **1.0.2**.

Recording setup now uses two compact floating panels with source icons, editable pixel dimensions, capture toggles and separate Record GIF / Record Video actions. Quality, FPS, resolution and notification settings are available under the sliders icon. The bar shows GIF conversion progress and keeps its two-click Discard available while Stop is pending.

Area selection uses physical pointer coordinates through mouse release, validates a frame inside one display, and passes an explicit monitor-local crop and output canvas to the recorder. Canceling selection returns a distinct result so a Record action cannot fall back to an old source.

Compact/Balanced GIFs sample the palette and use faster ordered dithering; High retains full palette analysis and error diffusion. Output FPS, dimensions and timing remain as chosen. Six-second synthetic 720p/1080p benchmarks were about three times faster in Balanced mode; results depend on content and hardware.

Verification: **164 no-window checks passed**, plus a separate native generated-image crop/encoded-pixel check. The build had no compiler warnings or errors. See [recording verification](docs/RECORDING-VERIFICATION.md) for measured results and limits. Existing screenshot/editor/scrolling/overlay checks passed. No user desktop, microphone or computer audio was captured in these tests.

Live mouse selection, WindowsGraphicsCapture area output on the user's desktop, clean Windows 11 and final installed-MSIX capture/recording acceptance remain unverified for this candidate. Desktop testing remains paused. Synthetic evidence is not live acceptance.

The GitHub installer remains unsigned. Store certification does not sign GitHub downloads. SignPath's application was submitted on 2 November; provider status was not rechecked during this change. Store 1.0.3 has not been submitted. New GitHub versions remain previews until the matching Store update is approved. Historical tags and downloads are preserved.

See [recording help](docs/RECORDING.md), [user guide](docs/USER-GUIDE.md), [distribution setup](docs/DISTRIBUTION.md) and [release history](RELEASES.md).
