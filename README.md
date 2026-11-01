# Framelet 0.5.1

A local Windows screenshot app with an editable annotation workspace. The interface combines CleanShot-inspired capture interactions with Windows 11 Fluent controls, system light/dark styling, native caption buttons and Mica on the main windows. Framelet is an independent implementation; the original CleanShot installer, code and artwork are not included.

## Run

Extract the entire `Framelet-0.5.1-win-x64.zip` folder, then double-click **Framelet.exe**. Keep the DLL, JSON and EXE files together. This portable build requires the **.NET 10 Desktop Runtime x64**, which is already installed on the development/test machine. Close earlier Framelet copies using their tray menu before starting this version, so the shortcuts are available. There is no installer, account or cloud service.

Closing the home window keeps Framelet running in the notification area. Double-click its tray icon or launch Framelet again to reopen history. Use **Quit Framelet** in the tray menu to exit and release the hotkeys. It does not automatically start with Windows.

## Capture

| Action | Default global shortcut | Behavior |
| --- | --- | --- |
| Area | Ctrl+Alt+Shift+1 | Drag a rectangle over the frozen desktop. Release to capture. |
| Window | Ctrl+Alt+Shift+2 | Click a visible window. Framelet brings it forward and captures its on-screen bounds. |
| Full display | Ctrl+Alt+Shift+3 | Capture the entire monitor under the pointer, including its taskbar. |
| Repeat area | Ctrl+Alt+Shift+4 | Capture the last selected rectangle again, during the current app session. |

Press **Esc** or right-click to cancel selection. Framelet hides its own home, previews and pins before capture, then restores pins after success. Canceling restores the previously visible windows.

You can capture again while the previous preview, pins or editor are open. The new preview replaces the previous preview after a successful capture; each original stays in history. A request received while capture is finishing is queued; multiple waiting requests coalesce to the latest action. If a save/confirmation dialog is open, capture waits for you to dismiss that dialog. Selection itself is asynchronous and does not disable the other app windows.

Use the home buttons or tray menu for the same actions. Expand **Keyboard shortcuts** to customize them, then select **Save shortcuts**. Supported combinations contain Ctrl and/or Alt, optional Shift, and one letter, digit or F1–F11. If another application owns a requested shortcut, Framelet explains the conflict and keeps the previous bindings. A startup conflict leaves the capture buttons usable; change the shortcuts to enable them.

## Preview, save and pin

The preview is a small rounded thumbnail at the **bottom left** of the monitor under the pointer. Preview and pins share the same 240 × 166 logical-pixel limit including shadow space and preserve the image's proportions. Hover for a compact toolbar outside the image: Pin/Unpin, Annotate, Copy, Save, Drag and Close. Its buttons have tooltips; **Tab** opens it for keyboard use. The image remains sharp and its frame does not grow or change shape to fit controls. The toolbar adjusts at screen edges and hides when you leave it; it is not part of the screenshot.

- **Copy** puts the image on the Windows clipboard. **Ctrl+C** also works in a preview or pin.
- **Save…** exports a PNG to a location you choose. **Ctrl+S** opens the same dialog. The native dialog asks before overwriting an existing file.
- **Pin** turns the same preview window into an always-on-top reference. The button changes to **Unpin**, which returns it to the preview area at the bottom left, keeping its size. Options are available by right-clicking the image. Up to eight pins stack at the left above space reserved for the next preview; crowded stacks can fit smaller cards or use more columns. Drag the image to move it independently; use its bottom-right grip to resize it. Double-click to toggle a larger view. Manually moved/resized pins leave the automatic stack; remaining docked pins reflow as captures are added or closed. Pins last for the current session; the underlying capture remains in history until removed or expired. Unpin replaces an existing preview, whose original remains in history.
- **History** in the right-click menu opens the home window. Select **Open** on an older capture to copy, save or pin it again.
- **Annotate** (pencil icon) opens an independent document in the image editor. The original capture stays in history. The floating preview hides to give you room to edit; a new capture still produces a preview.
- **Drag & drop:** drag the preview image, or use the toolbar's Drag button, to send an original-resolution PNG to another compatible Windows app. Pins themselves drag to move; their toolbar Drag button exports. The editor has its own drag handle for the flattened edited PNG.
- **Pin opacity:** open Pin options or right-click for the opacity slider, or use Ctrl+wheel for 5% steps. Opacity affects the on-screen image only; copy/save/drag retain full opacity and resolution. Arrow keys move a focused pin by one logical pixel; Shift+arrow moves ten.
- **Esc** dismisses an open toolbar/menu; otherwise it closes the preview or pin. Closing it does not remove its history entry.

## Windows appearance

The app follows Windows app light/dark mode and accent colors. Editor/history use the real Windows title bar, resize border, system menu and maximize/Snap behavior. Main-window Mica is supplied by WPF on supported Windows versions. The floating toolbar has its own rounded themed surface; previews and pins remain sharp while using it. Pin opacity blends the reference image with the desktop and is not desktop Acrylic. Windows contrast, transparency and motion preferences retain their effect fallbacks. Presentation never changes source pixels.

## Local storage

Default folder: `%LOCALAPPDATA%\Framelet`

- `captures\<capture-id>.png`: screenshots.
- `history.json` and `history.json.bak`: capture metadata and previous committed index.
- `settings.json`: shortcuts after saving custom settings.
- `drag-exports\<owned-id>.png`: local PNGs staged for file drag-and-drop. Targets can read them after the gesture ends. Owned files older than seven days are cleaned on a later drag; unrelated files are kept. These are unencrypted local copies, like capture history.

History retains the latest **100 captures**, pruning older owned PNGs after the new history index is committed. Export anything you need to keep permanently. **Remove** asks for confirmation and removes that history image; exported copies elsewhere remain. Screenshots are unencrypted local files. Framelet contains no upload, telemetry or network code.

Damaged indexes are preserved and recoverable PNGs are rediscovered. Recovered files can lose their original capture mode and are labeled Area. If saving history fails during a capture, the preview still offers Copy and Save and displays a warning.

For an isolated history/settings folder, launch `Framelet.exe --data-dir "C:\path\to\folder"`. The same folder permits only one app instance. Use a separate folder for development tests so production history is unaffected.

## Annotate and edit

Open **Annotate** from a preview or pin, or use **Open image / project…** on the home window. PNG, JPEG and `.framelet` files are supported. JPEG orientation metadata is applied on import.

- **Arrows:** choose **Classic** (tapered solid), **Filled** (uniform shaft and solid head), **Open** (straight with open head) or **Curved** (editable curve and open head). Select an arrow and drag its round endpoint handles; Curved also has a bend handle. Each completed drag is one undo step. New arrows default to Classic.
- Draw lines, rectangles, ellipses, highlighting or freehand strokes by dragging over the image. **Hand-drawn** gives ellipses a gently irregular outline and smooths newly drawn freehand paths. **Clean** keeps regular ellipses and the sampled freehand path. Existing version 1/2 arrows and pens retain their original appearance until you explicitly restyle them.
- For text, choose **Text**, enter your note in the toolbar, then click the image. **Counter** places the next numbered circle with a click.
- Choose **Select / move** and drag an existing annotation to reposition it. The Layers list selects annotations too. Color, stroke width, font size and style changes apply immediately to a selected annotation and can be undone. Edit a selected text note in the toolbar and click the checkmark to apply its text. The trash icon removes the selected annotation.
- **Crop**: drag the rectangle to keep. It resets image output dimensions to that crop's source pixels. **Undo** restores the previous crop. Hidden objects remain editable in the project.
- Open the sliders icon for image size, background and layers. Set output width/height and choose **Resize**. The aspect-ratio checkbox keeps the current ratio while you edit either field. Image dimensions exclude padding; the size in the bottom bar includes it.
- Choose a background and padding, then **Apply background**. Transparent padding shows a checkerboard. Use Fit or a percentage to zoom; scroll at larger zoom levels. Zoom changes only the view.
- Undo/redo covers annotations, movement, restyling, removal, crop, resize and backgrounds. The latest 100 operations are available during the current editing session. **Esc** cancels an in-progress stroke; it does not close the editor.

**Solid mask** covers a region with opaque black in flattened output. **Blur** and **Pixelate** soften or obscure content and should not be used as secure redaction. Exported images contain the rendered result; the `.framelet` project retains the original image and reversible layers, including masked pixels. Share a flattened PNG/JPEG when masking sensitive content.

**Save project…** writes a self-contained `.framelet` file with the original pixels, layers and image settings. New projects use version 3 for arrow styles, bend geometry and freehand smoothing and require Framelet 0.5.0 or newer. This version reads versions 1, 2 and 3, preserving legacy rendering. Reopen it using Open; it does not depend on the screenshot remaining in history. Undo history is not stored in the project. Closing a dirty editor or quitting the app prompts you with **Save project…**, **Discard edits** and **Cancel**; Cancel keeps it open. There is no autosave or automatic recovery of unsaved work after a crash.

**PNG…** and **JPEG…** export a flattened image. JPEG uses quality 95 and fills transparency with white. **Copy** and **Pin** use the same edited result. Editing a pinned flattened result starts a new document with that flattened image as its original. Edited output is not added automatically to capture history; save a project or export it to keep it. Projects and exported copies are outside the 100-capture retention policy.

Editor shortcuts: **Ctrl+Z** undo, **Ctrl+Y** redo, **Ctrl+S** save project, **Ctrl+C** copy the edited image, **Delete** remove the selected object. Text fields retain their own copy/undo behavior. Save project opens a filename dialog, including for an already saved project.

## Capture and platform limits

- Intended target: Windows 11 x64 with .NET 10 Desktop Runtime. The current machine is Windows build 26300.9711; other Windows releases and ARM64 have not been certified.
- Screenshots use GDI to copy visible screen pixels. Minimized, hidden, off-screen and protected content are not reconstructed. Other always-on-top applications can still cover a selected window.
- Output is a static SDR PNG. HDR color fidelity, game/exclusive-fullscreen capture and secure desktops are not certified. The pointer itself is not deliberately composited into the screenshot.
- Full display means one monitor, not a stitched all-monitor export. Area selection spans the virtual desktop. Coordinate tests cover negative origins and scaled layouts; mixed-DPI multi-monitor hardware still needs a separate acceptance pass.
- The editor supports images/output up to 20,000 pixels per side and 40 million total pixels, up to 500 annotations and 50,000 total freehand points. Padding is 0–500 pixels; project metadata is capped at 4 MB and embedded image data at 160 MB. An image can remain capturable/viewable under Phase 1 limits even if it exceeds editor limits.
- No scrolling capture, OCR, recording, upload links, persistent pins, autostart or updater in this phase. The app/package is unsigned. Project files are Framelet's own format; `.cleanshot` files are not supported.

## Build and verify

Requires a .NET 10 SDK on Windows x64. Scripts prefer a project-local SDK at `.tools/dotnet/dotnet.exe`, otherwise use `dotnet` from PATH. No third-party NuGet packages are needed. `NuGet.Config` clears external sources.

```powershell
./build.ps1 -Release
./test.ps1
./publish.ps1
```

Close running development copies, including the synthetic fixture, before rebuilding to avoid locking the EXE. `Framelet.exe --fixture` opens a blue/yellow test card without hotkeys or history. The verification harness briefly shows this card and exercises the clipboard, attempting to restore its previous contents afterward. Run tests on an unlocked desktop.

See [floating image sizing and pin fixes](docs/FLOATING-IMAGE-FIXES.md), [annotation and capture refinement](docs/ANNOTATION-REFINEMENT.md), [Windows 11 design and verification](docs/WINDOWS11-DESIGN.md), [UI redesign verification](docs/UI-REDESIGN-VERIFICATION.md), [visual references](docs/UI-REFERENCES.md), [Phase 2 verification](docs/PHASE2-VERIFICATION.md), [Phase 1 verification](docs/VERIFICATION.md), [architecture](docs/ARCHITECTURE.md) and [changes](CHANGELOG.md). Release ZIPs also have per-file SHA256 manifests. After publishing, the optional standard-library Python verifier is `python verification/verify_packages.py`; it checks packaged file contents, CRC, paths, hashes, source parity and documentation links.

Microsoft runtime download: https://dotnet.microsoft.com/en-us/download/dotnet/10.0 (choose Windows Desktop Runtime x64).
