# Architecture

Framelet is a native WPF application targeting `net10.0-windows`. Windows Forms supplies only the notification-area icon/menu. There are no third-party runtime packages or web views.

| Component | Responsibility |
| --- | --- |
| Program | DPI context, command-line data directory, per-directory instance mutex, startup errors |
| CaptureApp | Capture lifecycle, hiding/restoring windows, settings, tray, preview and pin ownership |
| Native | Small Win32/DWM interop surface and visible-window enumeration |
| CaptureService | GDI pixel capture, WPF cropping/loading, PNG serialization and clipboard retries |
| SelectionWindow | Frozen-desktop overlay, area drag or window picker, cancel |
| HotkeyService | Native registration and transactional binding updates |
| HistoryStore | Owned image paths, atomic index replacement, retention, recovery and settings |
| HomeWindow / ImageWindow / Ui | Main history/capture panel, preview/pins and shared native styling |
| FixtureWindow | Synthetic blue/yellow card for desktop and pixel verification |

Capture uses GDI `BitBlt` with screen coordinates. A per-monitor-v2 manifest and early DPI context request avoid automatic coordinate virtualization. The overlay maps its actual WPF canvas dimensions to the virtual desktop's physical rectangle, including negative monitor origins. A window is enumerated in z-order, excluding this process, cloaked/minimized windows and transparent/nonactivating overlays. The chosen visible window is brought forward before capture; this is not off-screen rendering.

PNG export is written to a same-folder temporary file, flushed, then renamed over the destination. History adds write the image before committing a replacement JSON index; a failed commit leaves existing entries intact. Only validated GUID-named files under the capture folder are deleted by retention/removal. Pins and previews hold frozen in-memory images, so retaining a pin does not lock its history PNG.

The app is local-only. Packaging copies an explicit set of runtime files plus user documentation. The source archive excludes the SDK, build intermediates, test data, screenshots, installer analysis and any original CleanShot files. There is no service, scheduler, login, external provider, updater or system-wide SDK installation in the application.

Follow-on phases should keep capture/storage separate from annotation, OCR and recording. Hardware acceptance on mixed-DPI and HDR setups should precede broader capture guarantees. Windows.Graphics.Capture can be evaluated for later capture requirements without coupling the editor to a specific backend.
