# Using Snipsera

[Back to Snipsera](../README.md) · [Downloads](../RELEASES.md)

## Run

Install Snipsera from [Microsoft Store](https://apps.microsoft.com/detail/9NFL9VHL8GKV), or run the GitHub EXE installer. For a portable download, extract the entire ZIP and open **Snipsera.exe**. These new packages include .NET. Screen recording also requires the [Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe). No account or cloud service is needed to use the app.

Historical Framelet downloads use **Framelet.exe** and require the .NET 10 Desktop Runtime x64. Close earlier copies through their tray menu before launching a new build. New projects use `.snipsera`; legacy `.framelet` projects continue to open.

Closing the home window keeps Snipsera running in the notification area. Double-click its tray icon or launch Snipsera again to reopen history. Use **Quit Snipsera** in the tray menu to exit and release the hotkeys. Startup with Windows is off by default. In version 1.0.5, open **Settings → General → Start with Windows** to turn it on or off. Startup opens quietly in the tray. If Windows or your administrator has disabled it, Settings explains the restriction and links to Windows Startup apps. Keep a portable copy in the same folder after enabling startup.

## Updates

In version 1.0.5, the Settings button on Home and **Settings and updates** in the tray menu open update preferences. Automatic checks are on by default and run daily while the app is open, with failed checks retried after an hour. Turn them off or use **Check now** whenever you want. A notice stays on Home when an update is available, with a tray alert deferred until capture or recording has finished. Windows notification settings can suppress tray alerts.

Store installations check Microsoft Store and open the Store app to update. EXE and portable installations check this project's GitHub releases and open the release page; download and run the installer yourself. **Include preview releases** is on by default for GitHub downloads. Turn it off to hear only about official releases. A newer installed preview is never replaced by an older official version. Turning off app checks does not change Microsoft Store's own automatic update settings. See [updates and startup](UPDATES-AND-STARTUP.md).

## Shortcuts and compact cards

In 1.0.6, open **Settings → Keyboard shortcuts**, select an action box and press the actual keys you want, then choose **Save shortcuts**. Earlier releases put this panel in Home. **Print Screen** works by itself or with Ctrl, Alt or Shift. Escape cancels a pending change; Tab moves to the next box. Existing capture shortcuts pause while you record a new chord. If Windows reserves Print Screen for Snipping Tool, turn off **Use the Print Screen key to open screen capture** under Windows **Settings > Accessibility > Keyboard**, then save the shortcut again. Snipsera does not change that Windows setting.

Docked previews and pins use matching 200 × 150 thumbnails with an 8-pixel logical gap between visible cards. Wide, tall and square captures fill the same card; only the thumbnail is cropped. Copy, save, drag and editing retain the complete image. Pins flow into additional columns when needed and scale down on small work areas. Manually moved or resized pins remain under your control.

## Capture

| Action | Default global shortcut | Behavior |
| --- | --- | --- |
| Area | Ctrl+Alt+Shift+1 | Drag a rectangle over the frozen desktop. Release to capture. |
| Window | Ctrl+Alt+Shift+2 | Click a visible window. Snipsera brings it forward and captures its on-screen bounds. |
| Full display | Ctrl+Alt+Shift+3 | Capture the entire monitor under the pointer, including its taskbar. |
| Repeat area | Ctrl+Alt+Shift+4 | Capture the last selected rectangle again, during the current app session. |
| All-in-one capture | Ctrl+Alt+Shift+5 | Open the compact bar for Area, Window, Display, Scroll or Last area. |
| Scrolling capture | Home, tray or image right-click menu | Select scrolling content, Start, scroll down, then Done to create one tall image. |

Press **Esc** or right-click to cancel selection. Snipsera hides its own home, previews and pins before capture, then restores pins after success. Canceling restores the previously visible windows.

You can capture again while the previous preview, pins or editor are open. The new preview replaces the previous preview after a successful capture; each original stays in history. A request received while capture is finishing is queued; multiple waiting requests coalesce to the latest action. If a save/confirmation dialog is open, capture waits for you to dismiss that dialog. Selection itself is asynchronous and does not disable the other app windows.

Use the home buttons or tray menu for the same actions. Open **Settings → Keyboard shortcuts** to customize them, then select **Save shortcuts**. Supported shortcuts include Print Screen on its own or with modifiers, or Ctrl and/or Alt, optional Shift, and one letter, digit or F1–F11. If another application owns a requested shortcut, Snipsera explains the conflict and keeps the previous bindings. A startup conflict leaves the capture buttons usable; change the shortcuts to enable them.

## All-in-one capture bar

Press **Ctrl+Alt+Shift+5**, or choose **All-in-one capture** in Home or the tray. The floating bar offers **Area**, **Window**, **Display**, **Scroll** and **Last area**. Choose a mode to continue through the usual capture, preview and history workflow. Display captures the monitor where the bar opened, including its taskbar. Last area becomes available after selecting an area during the current session.

To use **Print Screen**, open **Settings → Keyboard shortcuts**, select the **All-in-one capture** box, press Print Screen and choose **Save shortcuts**. If it is already assigned to Area or another action, change that action's shortcut too. Existing shortcuts are preserved when upgrading; an unused Ctrl+Alt+Shift+5–9 is chosen for the new bar if necessary.

With the bar open, use **1–5** to choose a mode, or arrow keys/Tab and Enter. **Esc**, Close, clicking outside, or pressing the launcher shortcut again dismisses it without taking a screenshot. Drag its grip to move it. The bar closes before capture and does not appear in the result. It uses the same colors, rounded surfaces and keyboard focus style as the other floating controls.

## Scrolling capture

Choose **Scrolling capture**, then drag around the content that scrolls. Leave fixed headers, footers and scrollbars outside the selection and enough space around it for the capture controls. Choose **Start capture**, then scroll down slowly, keeping some of the previous view visible. A growing preview shows the image collected so far. **Auto-scroll** is optional; you can turn it off and continue manually. Choose **Done** to send the tall image to the usual preview/history workflow, where you can copy, save, pin or annotate it. **Cancel** or Esc discards the session and restores the previous windows.

This version stitches vertical downward scrolling from visible pixels. Duplicate views and backward motion do not add another copy. If a view cannot be aligned, scroll back slightly and continue more slowly. Snipsera keeps the last good portion and reports an alignment/limit warning when applicable. Auto-scroll stops after no movement or an alignment problem; it does not assume that every app has reached the end of its document. The result is limited to 20,000 pixels per side and 40 million total pixels. Read the [scrolling capture notes](SCROLLING-CAPTURE.md) for supported conditions and verification.

In Chrome, Auto-scroll uses small native wheel events and moves the pointer from its Snipsera control into the selected content once. Leave the pointer inside the selection and keep Chrome active. Moving outside, switching apps or holding modifiers pauses Auto-scroll. No clicks or typing are generated. Progress is checked from captured pixels before another step is sent.

## Screen recording

Open **Record screen** in Home or the tray. Use the compact panel's source icons and pixel dimensions to choose what to record, then **Record GIF** or **Record Video**. The sliders icon opens quality, FPS and resolution. Use the recording bar or tray to pause and stop; GIF conversion shows progress. [Full recording instructions](RECORDING.md), including audio requirements and the manual Do not disturb limitation.

## Preview, save and pin

The preview is a small rounded thumbnail at the **bottom left** of the monitor under the pointer. Preview and pins share the same 240 Ã— 166 logical-pixel limit including shadow space and preserve the image's proportions. Hover for controls **on the image**: Pin/Unpin and Close in the top corners, Annotate and Drag in the bottom corners, and labeled Copy/Save pills in the center. **Tab** reveals the controls for keyboard use. Very shallow or slender captures use compact on-image controls and a More menu when needed. The frame keeps its size and proportions while controls appear. A soft shade behind the controls helps readability; the image stays sharp and exports exclude every display effect.

- **Copy** puts the image on the Windows clipboard. **Ctrl+C** also works in a preview or pin.
- **Save…** exports a PNG to a location you choose. **Ctrl+S** opens the same dialog. The native dialog asks before overwriting an existing file.
- **Pin** turns the same preview window into an always-on-top reference. The button changes to **Unpin**, which returns it to the preview area at the bottom left, keeping its size. Options are available by right-clicking the image. Up to eight pins stack at the left above space reserved for the next preview; crowded stacks can fit smaller cards or use more columns. Drag the image to move it independently; use its bottom-right grip to resize it. Double-click to toggle a larger view. Manually moved/resized pins leave the automatic stack; remaining docked pins reflow as captures are added or closed. Pins last for the current session; the underlying capture remains in history until removed or expired. Unpin replaces an existing preview, whose original remains in history.
- **History** in the right-click menu opens the home window. Select **Open** on an older capture to copy, save or pin it again.
- **Annotate** (pencil icon) opens an independent document in the image editor. The original capture stays in history. The floating preview hides to give you room to edit; a new capture still produces a preview.
- **Drag & drop:** drag the preview image, or use its Drag button, to send an original-resolution PNG to another compatible Windows app. Pins themselves drag to move; their Drag action exports. Drag stays visible when other actions fold into More. On an exceptionally tiny manually resized pin, the other actions remain in the right-click menu. The editor has its own drag handle for the flattened edited PNG.
- **Pin opacity:** right-click for the opacity slider, or use Ctrl+wheel for 5% steps. Opacity affects the on-screen image only; copy/save/drag retain full opacity and resolution. Arrow keys move a focused pin by one logical pixel; Shift+arrow moves ten.
- **Esc** dismisses open controls/menu; otherwise it closes the preview or pin. Closing it does not remove its history entry.

## Temporarily hide overlays

Scroll **down one notch over the image** in a preview or pin to hide the floating group. You can also use **Temporarily hide overlays** in an image's right-click menu, the tray menu, or the hide/show row in Home. The existing on-image controls stay in place. Ctrl+wheel on a pin still adjusts opacity.

A small rounded tab appears at the bottom left of the initiating monitor, above its taskbar. Move away and hover over it briefly, or click it, to restore the same windows. The tray and Home toggles are keyboard-accessible alternatives. Hiding keeps image data, pin state, positions, sizes and opacity. Editors and history remain available. New captures made while hidden join the hidden group until you restore it. This mode lasts for the current app session.

Cards use a short slide/fade within their existing frames; Windows reduced-motion preferences make this instant. Capture hides cards and the restore tab synchronously before collecting pixels. See [behavior and verification limits](TEMPORARY-HIDE.md).

## Windows appearance

The app follows Windows app light/dark mode and accent colors. Editor/history use the real Windows title bar, resize border, system menu and maximize/Snap behavior. Main-window Mica is supplied by WPF on supported Windows versions. On-image controls have rounded themed surfaces; previews and pins remain sharp while using them. Pin opacity blends the reference image with the desktop and is not desktop Acrylic. Windows contrast, transparency and motion preferences retain their effect fallbacks. Presentation never changes source pixels.

## Local storage

In 1.0.6, **Settings → Save locations** chooses separate screenshot and recording folders. Screenshots and edits automatically save as PNGs to Pictures\Snipsera by default; video/GIF saving is automatic on Stop. Changing folders leaves old files in place. The auto-save switch can be turned off. These saved copies stay separate from editable capture history and are not pruned with it. Recording results appear as side cards with Play, Copy, Save, Drag and Pin controls. [Settings and recording result help](SETTINGS.md).

Default folder: `%LOCALAPPDATA%\Snipsera`

- `captures\<capture-id>.png`: original screenshots.
- `captures\<capture-id>.snipsera`: automatically saved edits, original pixels and reversible layers for that history entry.
- `history.json` and `history.json.bak`: capture metadata and previous committed index.
- `settings.json`: shortcuts after saving custom settings.
- `drag-exports\<owned-id>.png`: local PNGs staged for file drag-and-drop. Targets can read them after the gesture ends. Owned files older than seven days are cleaned on a later drag; unrelated files are kept. These are unencrypted local copies, like capture history.

History retains the latest **100 captures**, pruning older owned PNGs and their automatic edit projects after the new history index is committed. Export anything you need to keep permanently. **Remove** asks for confirmation and removes that history image and its automatic edit project; exported copies elsewhere remain. Screenshots are unencrypted local files. Snipsera does not upload captures or send telemetry. Optional update checks retrieve version information from GitHub or Microsoft Store; see [privacy](../PRIVACY.md).

Damaged indexes are preserved and recoverable PNGs are rediscovered. Recovered files can lose their original capture mode and are labeled Area. If saving history fails during a capture, the preview still offers Copy and Save and displays a warning.

For an isolated history/settings folder, launch `Snipsera.exe --data-dir "C:\path\to\folder"`. The same folder permits only one app instance. Use a separate folder for development tests so production history is unaffected.

## Annotate and edit

Open **Annotate** from a preview or pin, or use **Open image / project…** on the home window. PNG, JPEG, `.snipsera` and legacy `.framelet` files are supported. JPEG orientation metadata is applied on import.

- **Arrows:** choose **Classic** (tapered solid), **Filled** (uniform shaft and solid head), **Open** (straight with open head) or **Curved** (editable curve and open head). Select an arrow and drag its round endpoint handles; Curved also has a bend handle. Each completed drag is one undo step. New arrows default to Classic.
- Draw lines, rectangles, ellipses, highlighting or freehand strokes by dragging over the image. **Hand-drawn** gives ellipses a gently irregular outline and smooths newly drawn freehand paths. **Clean** keeps regular ellipses and the sampled freehand path. Existing version 1/2 arrows and pens retain their original appearance until you explicitly restyle them.
- For text, choose **Text**, enter your note in the toolbar, then click the image. **Counter** places the next numbered circle with a click.
- Choose **Select / move** and drag an existing annotation to reposition it. The Layers list selects annotations too. Color, stroke width, font size and style changes apply immediately to a selected annotation and can be undone. Edit a selected text note in the toolbar and click the checkmark to apply its text. The trash icon removes the selected annotation.
- **Crop**: drag the rectangle to keep. It resets image output dimensions to that crop's source pixels. **Undo** restores the previous crop. Hidden objects remain editable in the project.
- Open the sliders icon for image size, background and layers. Set output width/height and choose **Resize**. The aspect-ratio checkbox keeps the current ratio while you edit either field. Image dimensions exclude padding; the size in the bottom bar includes it.
- Choose a background and padding, then **Apply background**. Transparent padding shows a checkerboard. Use Fit or a percentage to zoom; scroll at larger zoom levels. Zoom changes only the view.
- Undo/redo covers annotations, movement, restyling, removal, crop, resize and backgrounds. The latest 100 operations are available during the current editing session. **Esc** cancels an in-progress stroke; it does not close the editor.

**Solid mask** covers a region with opaque black in flattened output. **Blur** and **Pixelate** soften or obscure content and should not be used as secure redaction. Exported images contain the rendered result; the editable project retains the original image and reversible layers, including masked pixels. Share a flattened PNG/JPEG when masking sensitive content.

**Save project…** writes a self-contained `.snipsera` file (or keeps an opened legacy `.framelet` filename) with the original pixels, layers and image settings. New projects use version 3 for arrow styles, bend geometry and freehand smoothing and are readable by Snipsera and Framelet 0.5.0 or newer. This version reads versions 1, 2 and 3, preserving legacy rendering. Reopen it using Open; it does not depend on the screenshot remaining in history. Undo history is not stored in the project. Edits save automatically to local history after a short pause and before the editor closes or the app quits. Closing the editor does not ask you to save or discard. If saving fails, it stays open with an error so you can retry or export a copy. Changes made just before a crash may not have reached disk.

**PNG…** and **JPEG…** export a flattened image. JPEG uses quality 95 and fills transparency with white. **Copy** and **Pin** use the same edited result. Reopening a history capture for annotation restores its original pixels and saved layers. Edits update that history entry rather than adding duplicates; imported images enter history when first edited. An external project or exported copy stays separate from automatic history saves and is outside the 100-capture retention policy.

Editor shortcuts: **Ctrl+Z** undo, **Ctrl+Y** redo, **Ctrl+S** save project, **Ctrl+C** copy the edited image, **Delete** remove the selected object. Text fields retain their own copy/undo behavior. Save project opens a filename dialog, including for an already saved project.

## Capture and platform limits

- Intended target: Windows 11 x64. Snipsera packages include .NET; historical Framelet ZIPs require the .NET 10 Desktop Runtime. Other Windows releases and ARM64 have not been certified.
- Screenshots use GDI to copy visible screen pixels. Minimized, hidden, off-screen and protected content are not reconstructed. Other always-on-top applications can still cover a selected window.
- Output is a static SDR PNG. HDR color fidelity, game/exclusive-fullscreen capture and secure desktops are not certified. The pointer itself is not deliberately composited into the screenshot.
- Full display means one monitor, not a stitched all-monitor export. Area selection spans the virtual desktop. Coordinate tests cover negative origins and scaled layouts; mixed-DPI multi-monitor hardware still needs a separate acceptance pass.
- The editor supports images/output up to 20,000 pixels per side and 40 million total pixels, up to 500 annotations and 50,000 total freehand points. Padding is 0–500 pixels; project metadata is capped at 4 MB and embedded image data at 160 MB. An image can remain capturable/viewable under Phase 1 limits even if it exceeds editor limits.
- Scrolling capture requires overlapping, stable views of one vertical scroll region. Sticky elements, animations, horizontal movement, large jumps, changing window geometry and repeating/blank content can prevent reliable alignment. Exclude fixed elements when selecting the region. Auto-scroll uses a native accessibility scroll action when available, with guarded native wheel input as a fallback. Applications that support neither can be scrolled manually.
- No OCR, upload links, persistent pins, autostart or updater. Screen recording is supported as described above. The app/package is unsigned. Project files are Snipsera's own format; `.cleanshot` files are not supported.
