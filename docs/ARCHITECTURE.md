# Architecture

Snipsera is a native WPF application targeting `net10.0-windows10.0.22000.0`. Windows Forms supplies only the notification-area icon; its menu is WPF Fluent. Recording uses ScreenRecorderLib and a separate local FFmpeg converter; Windows SDK projections provide Store update and startup APIs. There are no web views.

| Component | Responsibility |
| --- | --- |
| Program | DPI context, command-line data directory, per-directory instance mutex, startup errors |
| CaptureApp | Capture lifecycle, hiding/restoring windows, settings, tray, preview and pin ownership |
| OverlayVisibility / OverlayGestures | Visible intent, temporary hiding, capture suspension, wheel accumulation and restore-hover arming, independent of native windows |
| OverlayShelfWindow / OverlayShelfLayout | bottom-left restore tab, live work-area placement, themes, hover dwell and accessible restoration |
| Native | Small Win32/DWM interop surface and visible-window enumeration |
| CaptureService | GDI pixel capture, WPF cropping/loading, PNG serialization and clipboard retries |
| ScrollStitcher / ScrollFrame | Stable viewport pixels, unique vertical-overlap alignment, bounded row-strip composition and live thumbnail |
| ScrollCaptureSession / ScrollCaptureInput / ScrollAutomation | Scrolling capture lifecycle, sampling, opt-in accessibility scroll with wheel fallback, temporary Escape registration |
| ScrollCaptureWindow / ScrollCaptureLayout | framing, controls and growing preview outside the selected rectangle |
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
| ArrowDrawing | Four arrow geometries, tangent-aware heads and curve handle mapping |
| FloatingVisuals / ConfirmWindow | Antialiased floating alpha masks and themed semantic confirmations |

Capture uses GDI `BitBlt` with screen coordinates. A per-monitor-v2 manifest and early DPI context request avoid automatic coordinate virtualization. The overlay maps its actual WPF canvas dimensions to the virtual desktop's physical rectangle, including negative monitor origins. A window is enumerated in z-order, excluding this process, cloaked/minimized windows and transparent/nonactivating overlays. The chosen visible window is brought forward before capture; this is not off-screen rendering.

PNG export is written to a same-folder temporary file, flushed, then renamed over the destination. History adds write the image before committing a replacement JSON index; a failed commit leaves existing entries intact. Only validated GUID-named files under the capture folder are deleted by retention/removal. Pins and previews hold frozen in-memory images, so retaining a pin does not lock its history PNG.

The app is local-only. Packaging copies an explicit set of runtime files plus user documentation. The source archive excludes the SDK, build intermediates, test data, screenshots, installer analysis and any original CleanShot files. There is no service, scheduler, login, external provider, updater or system-wide SDK installation in the application.

The editor stores annotations in original physical pixel coordinates. Crop is a view into those pixels; resize and padding map the edited image into output coordinates. The renderer composes in document order, so redaction/effect regions include earlier drawn content. Blur uses three separable box passes, pixelation averages 16-pixel blocks and solid masks paint opaque black. Image editing does not overwrite the source history PNG. A single flattened renderer supplies the displayed bitmap and clipboard/pin output; export uses that renderer at the exact requested output size, independent of view zoom or monitor scale.

Version 3 `.framelet` projects are ZIPs containing only `project.json` and `original.png`. Metadata describes geometry, background, annotations, Clean/Sketch style, nullable arrow style, normalized bend and explicit freehand smoothing, with no external file references. Loading never extracts archive paths. Validation bounds input bytes, pixels, points, enum values and colors before rendering. Project export encodes the original PNG to a seekable buffer because WPF's encoder cannot write directly to a ZIP entry stream. Project writes use the same temporary-file/rename pattern as flattened image export. Projects retain original pixels and are not redacted deliverables.

Editor windows unsubscribe from document events on close. Capturing hides them and restores them on success or cancellation. App quit checks every editor for unsaved work before setting the quitting flag; choosing Cancel prevents shutdown. Original capture history and settings formats remain unchanged.

Follow-on phases should keep OCR and recording separate from capture and editing. Hardware acceptance on mixed-DPI and HDR setups should precede broader capture guarantees. Windows.Graphics.Capture can be evaluated for later capture requirements without coupling the editor to a specific backend.

The loader also reads versions 1 and 2. Missing style is Clean; missing Arrow remains null and missing Smooth remains false so legacy arrow/pen rendering is unchanged. Version 3 makes older readers fail clearly. New Classic arrows have a tapered solid shaft; Filled, Open and Curved use constant-width shafts. Curved geometry is a quadratic Bezier whose actual midpoint is the bend handle, with an open head oriented to the endpoint tangent. Handle previews render a replacement without committing document state, then commit once on release; Escape/capture loss cancels. New smoothed freehand paths use cubic interpolation; legacy paths keep their original polyline.

Global hotkeys use a dedicated message-only HwndSource and dispatch capture after returning from WM_HOTKEY. SelectionWindow.PickAsync uses a completion task and Show rather than a thread-modal ShowDialog. CaptureApp holds one latest pending request during capture or a modal save/confirmation dialog, resuming after capture cleanup or ComponentDispatcher.LeaveThreadModal. Modal dialogs remain visible until answered; ordinary editors and pins hide only for capture, then restore. The latest preview is replaced only after the new capture succeeds.

Temporary overlay hiding is a separate visibility mode. Image windows are tracked with visible intent; an editor-hidden preview has false intent, while a temporarily hidden card retains true intent. Capture suspends presentation without changing intent or the temporary-hide mode, then resumes it during cleanup. Preview replacement is batched so removing the only old preview cannot reset a hidden group or flicker existing pins. Closed cards are forgotten, and an empty group clears the mode. The restore tab is suppressed during capture. New captures made while the mode is active join the group; history and document formats are unchanged. `ImageWindow.PresentOverlay` provides manual slide/fade presentation, while capture requests use synchronous presentation and cancel stale animation completions. Native interaction remains pending during the user's desktop-testing pause.

Floating windows use transparent borderless WPF cards. A pure layout planner bounds preview and pins at the same 240 × 166 logical pixels and preserves image proportions without independent width/height minimums. It converts the resulting frame to physical work-area rectangles. Docked pins reflow newest first in the monitor's left band; preview space remains reserved below them. Pin/Unpin transfers ownership of the same ImageWindow between the preview and pin list. Unpin returns the existing frame to the preview area; a close callback cleans either ownership role. User dragging/resizing removes a pin from automatic placement. Thumbnail scaling never changes CaptureImage. Editor/history use the standard Windows nonclient frame and caption controls; image properties live in a collapsible inspector. Original icons and native system fonts are used.

Appearance supplies bound semantic brushes for system light/dark/high-contrast settings, a transparency/motion policy and live preference updates. WPF ThemeMode.System supplies the built-in Fluent controls and supported Mica backdrop; the locally scoped WPF0001 suppression acknowledges that API status in .NET 10. Floating controls live on the image as corner actions and centered Copy/Save pills. Shallow/slender captures use compact on-image actions with overflow, retaining Drag. Scoped floating-button templates preserve material and accent/focus states without replacing the app-wide Fluent style. Controls do not affect image measurement or source pixels. Hiding, closing or moving an image dismisses its controls before capture; Tab provides keyboard access. DragExport stages a full-resolution PNG plus native FileDrop/Bitmap data and keeps owned cache files available for seven days; a dynamic enable predicate switches the image between preview export-drag and pinned move-drag. No external provider or third-party UI package is introduced.
