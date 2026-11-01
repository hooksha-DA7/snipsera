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
| HomeWindow / ImageWindow / Ui / UiIcon | Main history/capture panel, preview/pins and shared native styling |
| FloatingLayout | Pure physical-pixel sizing/stack planning with DPI, left anchoring and preview clearance |
| FixtureWindow | Synthetic blue/yellow card for desktop and pixel verification |
| EditDocument / EditState / Mark | Immutable original pixels, immutable edit snapshots, validation, crop/output mapping and bounded undo/redo |
| EditRenderer | Ordered vector/effect composition and shared flattened rendering for the canvas, copy, pin and image export |
| EditFiles | Bounded PNG/JPEG import, JPEG orientation, versioned project ZIP, atomic project/image output |
| EditCanvas / EditorWindow | Zoomed image interaction, annotation tools, object/property controls and document lifecycle |

Capture uses GDI `BitBlt` with screen coordinates. A per-monitor-v2 manifest and early DPI context request avoid automatic coordinate virtualization. The overlay maps its actual WPF canvas dimensions to the virtual desktop's physical rectangle, including negative monitor origins. A window is enumerated in z-order, excluding this process, cloaked/minimized windows and transparent/nonactivating overlays. The chosen visible window is brought forward before capture; this is not off-screen rendering.

PNG export is written to a same-folder temporary file, flushed, then renamed over the destination. History adds write the image before committing a replacement JSON index; a failed commit leaves existing entries intact. Only validated GUID-named files under the capture folder are deleted by retention/removal. Pins and previews hold frozen in-memory images, so retaining a pin does not lock its history PNG.

The app is local-only. Packaging copies an explicit set of runtime files plus user documentation. The source archive excludes the SDK, build intermediates, test data, screenshots, installer analysis and any original CleanShot files. There is no service, scheduler, login, external provider, updater or system-wide SDK installation in the application.

The editor stores annotations in original physical pixel coordinates. Crop is a view into those pixels; resize and padding map the edited image into output coordinates. The renderer composes in document order, so redaction/effect regions include earlier drawn content. Blur uses three separable box passes, pixelation averages 16-pixel blocks and solid masks paint opaque black. Image editing does not overwrite the source history PNG. A single flattened renderer supplies the displayed bitmap and clipboard/pin output; export uses that renderer at the exact requested output size, independent of view zoom or monitor scale.

Version 2 `.framelet` projects are ZIPs containing only `project.json` and `original.png`. Metadata describes geometry, background, annotations and their Clean/Sketch style, with no external file references. Loading never extracts archive paths. Validation bounds input bytes, pixels, points, enum values and colors before rendering. Project export encodes the original PNG to a seekable buffer because WPF's encoder cannot write directly to a ZIP entry stream. Project writes use the same temporary-file/rename pattern as flattened image export. Projects retain original pixels and are not redacted deliverables.

Editor windows unsubscribe from document events on close. Capturing hides them and restores them on success or cancellation. App quit checks every editor for unsaved work before setting the quitting flag; choosing Cancel prevents shutdown. Original capture history and settings formats remain unchanged.

Follow-on phases should keep OCR and recording separate from capture and editing. Hardware acceptance on mixed-DPI and HDR setups should precede broader capture guarantees. Windows.Graphics.Capture can be evaluated for later capture requirements without coupling the editor to a specific backend.

The loader also reads version 1 projects, treating missing style as Clean. Newly saved projects use version 2 so older readers fail clearly rather than silently changing annotation appearance. Sketch arrows use deterministic Bezier shafts and open stroked heads; sketch ellipses use a smooth irregular Bezier loop. Clean rendering remains unchanged.

Floating windows use transparent borderless WPF cards. A pure layout planner bounds their initial logical footprint then converts to physical work-area rectangles. Docked pins reflow newest first in the monitor’s left band; preview space remains reserved below them. User dragging/resizing removes a pin from automatic placement. Thumbnail scaling never changes CaptureImage. Editor/history use the standard Windows nonclient frame and caption controls; image properties live in a collapsible inspector. Original icons and native system fonts are used.

Appearance supplies bound semantic brushes for system light/dark/high-contrast settings, a transparency/motion policy and live preference updates. WPF ThemeMode.System supplies the built-in Fluent controls and supported Mica backdrop; the locally scoped WPF0001 suppression acknowledges that API status in .NET 10. Preview frosting is a view-only BlurEffect plus theme tint on the thumbnail, while pinned references remain sharp. DragExport stages a full-resolution PNG plus native FileDrop/Bitmap data and keeps owned cache files available for seven days. No external provider or third-party UI package is introduced.
