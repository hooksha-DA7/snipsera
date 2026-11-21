# Using Snipsera

[Back to Snipsera](../README.md) · [Downloads](../RELEASES.md)

The editor and preview refinements described for **1.0.8-preview.1** are an unpublished local candidate. The current official release remains **1.0.7**.

## Run

Install Snipsera from [Microsoft Store](https://apps.microsoft.com/detail/9NFL9VHL8GKV), or run the GitHub EXE installer. For a portable download, extract the entire ZIP and open **Snipsera.exe**. These new packages include .NET. Screen recording also requires the [Visual C++ x64 runtime](https://aka.ms/vs/17/release/vc_redist.x64.exe). No account or cloud service is needed to use the app.

Historical Framelet downloads use **Framelet.exe** and require the .NET 10 Desktop Runtime x64. Close earlier copies through their tray menu before launching a new build. New projects use `.snipsera`; legacy `.framelet` projects continue to open.

Closing the home window keeps Snipsera running in the notification area. Double-click its tray icon or launch Snipsera again to reopen history. Use **Quit Snipsera** in the tray menu to exit and release the hotkeys. Startup with Windows is off by default. In version 1.0.5, open **Settings → General → Start with Windows** to turn it on or off. Startup opens quietly in the tray. If Windows or your administrator has disabled it, Settings explains the restriction and links to Windows Startup apps. Keep a portable copy in the same folder after enabling startup.

## Updates

In version 1.0.5, the Settings button on Home and **Settings and updates** in the tray menu open update preferences. Automatic checks are on by default and run daily while the app is open, with failed checks retried after an hour. Turn them off or use **Check now** whenever you want. A notice stays on Home when an update is available, with a tray alert deferred until capture or recording has finished. Windows notification settings can suppress tray alerts.

Store installations check Microsoft Store and open the Store app to update. EXE and portable installations check this project's GitHub releases and open the release page; download and run the installer yourself. **Include preview releases** is on by default for GitHub downloads. Turn it off to hear only about official releases. A newer installed preview is never replaced by an older official version. Turning off app checks does not change Microsoft Store's own automatic update settings. See [updates and startup](UPDATES-AND-STARTUP.md).

## Shortcuts and compact cards

In 1.0.6, open **Settings → Keyboard shortcuts**, select an action box and press the actual keys you want, then choose **Save shortcuts**. Earlier releases put this panel in Home. **Print Screen** works by itself or with Ctrl, Alt or Shift. Escape cancels a pending change; Tab moves to the next box. Existing capture shortcuts pause while you record a new chord. If Windows reserves Print Screen for Snipping Tool, turn off **Use the Print Screen key to open screen capture** under Windows **Settings > Accessibility > Keyboard**, then save the shortcut again. Snipsera does not change that Windows setting.

The front preview and docked pins use matching 200 × 150 thumbnails. Pending screenshots form an overlapping deck with narrower rear cards; separate pins and recording cards retain an 8-pixel logical gap. Wide, tall and square captures fill the same front card; only the thumbnail is cropped. Copy, save, drag and editing retain the complete image. Pins flow into additional columns when needed and scale down on small work areas. Manually moved or resized pins remain under your control.

## Capture

| Action | Default global shortcut | Behavior |
| --- | --- | --- |
| Area | Ctrl+Alt+Shift+1 | Drag a rectangle over the frozen desktop. Release to capture. |
| Window | Ctrl+Alt+Shift+2 | Click a visible window. Snipsera brings it forward and captures its on-screen bounds. |
| Full display | Ctrl+Alt+Shift+3 | Capture the entire monitor under the pointer, including its taskbar. |
| Repeat area | Ctrl+Alt+Shift+4 | Capture the last selected rectangle again, during the current app session. |
| All-in-one capture | Ctrl+Alt+Shift+5 | Open the compact bar for Area, Window, Display, Scroll or Last area. |
| Reopen last dismissed capture | Ctrl+Alt+Shift+6 | Bring back the most recently dismissed screenshot from this app session. |
| Scrolling capture | Home, tray or image right-click menu | Select scrolling content, Start, scroll down, then Done to create one tall image. |

Press **Esc** or right-click to cancel selection. Snipsera hides its own home, previews and pins before capture, then restores pins after success. Canceling restores the previously visible windows.

You can capture again while previews, pins or an editor are open. Each new screenshot joins the pending stack without replacing an earlier one; each original stays in history. A request received while capture is finishing is queued; multiple waiting requests coalesce to the latest action. If a save/confirmation dialog is open, capture waits for you to dismiss that dialog. Selection itself is asynchronous and does not disable the other app windows.

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

Screenshot previews form a layered deck at the **bottom left** of the monitor under the pointer. The newest capture is in front, with up to two narrower cards peeking out behind it. Click a rear edge to bring that screenshot forward; hovering keeps the deck compact. When a screenshot card has keyboard focus, **Alt+2** and **Alt+3** select the rear layers. An unpinned recording stays separate below the deck and uses one of the three slots; short displays can show fewer layers. A **+N** button opens additional screenshots without losing the others. The front card and pins use matching 200 × 150 logical-pixel thumbnails; only the thumbnail is cropped, while the complete image is kept for sharing and editing.

Hover for controls **on the image**: Pin/Unpin and Close in the top corners, Annotate and Drag in the bottom corners, and labeled Copy/Save pills in the center. **Tab** reveals the controls for keyboard use. The frame keeps its size while controls appear. A soft shade behind the controls helps readability; the image stays sharp and exports exclude every display effect.

- **Copy** puts the image on the Windows clipboard. **Ctrl+C** also works in a preview or pin.
- **Save…** exports a PNG to a location you choose. **Ctrl+S** opens the same dialog. The native dialog asks before overwriting an existing file.
- **Pin** turns the same preview window into an always-on-top reference. **Unpin** returns that window to the compact pending stack without replacing another screenshot. Options are available by right-clicking the image. Up to eight pins stack at the left above the previews; crowded stacks can fit smaller cards or use more columns. Drag the image to move it independently; use its bottom-right grip to resize it. Double-click to toggle a larger view. Manually moved/resized pins leave the automatic stack; unpinning returns them to its compact size and position. Remaining docked pins reflow as captures are added or closed. Pins last for the current session; the underlying capture remains in history until removed or expired.
- **History** in the right-click menu opens the home window. Select **Open** on an older capture to copy, save or pin it again.
- **Annotate** (pencil icon) opens an independent document in the image editor. The original capture stays in history. The floating preview hides to give you room to edit; a new capture still produces a preview.
- **Drag & drop:** drag the preview image, or use its Drag button, to send an original-resolution PNG to another compatible Windows app. Pins themselves drag to move; their Drag action exports. Drag stays visible when other actions fold into More. On an exceptionally tiny manually resized pin, the other actions remain in the right-click menu. The editor has its own drag handle for the flattened edited PNG.
- **Pin opacity:** right-click for the opacity slider, or use Ctrl+wheel for 5% steps. Opacity affects the on-screen image only; copy/save/drag retain full opacity and resolution. Arrow keys move a focused pin by one logical pixel; Shift+arrow moves ten.
- **Esc** dismisses open controls/menu; otherwise it closes the preview or pin. Closing it does not remove its history entry.

**Dismiss after sharing** is on by default under **Settings → General → Previews**. A successful Copy, an explicit Save, or a drag accepted by another app as a copy dismisses the unpinned screenshot and brings the next pending screenshot into view. Canceled or failed actions leave it in place. Automatic saving to the screenshot folder does not dismiss a card. Pins and recording cards stay visible after sharing. If the screenshot could not be saved to history, it also stays visible so you can keep a copy. Turn the setting off to leave screenshot previews open after sharing.

Use **Reopen last dismissed capture** in the tray or **+N** menu, or press **Ctrl+Alt+Shift+6**, to bring back the last dismissed screenshot. Customize its shortcut under **Settings → Keyboard shortcuts**. Existing bindings are preserved on upgrade; an unused default is chosen if that combination is already assigned. **Clear unpinned previews** in the tray or **+N** menu clears the pending cards while keeping pins, screenshot history and saved recording files.

The pending stack and dismissed-capture list last only for the current app session. After restarting, use History to open earlier screenshots. History still retains the latest 100 captures; the stack does not extend that retention or add a separate library or cloud storage.

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
- `preview-preferences.json`: the saved **Dismiss after sharing** choice; it does not store the pending stack.
- `drag-exports\<owned-id>.png`: local PNGs staged for file drag-and-drop. Targets can read them after the gesture ends. Owned files older than seven days are cleaned on a later drag; unrelated files are kept. These are unencrypted local copies, like capture history.

History retains the latest **100 captures**, pruning older owned PNGs and their automatic edit projects after the new history index is committed. Export anything you need to keep permanently. **Remove** asks for confirmation and removes that history image and its automatic edit project; exported copies elsewhere remain. Screenshots are unencrypted local files. Snipsera does not upload captures or send telemetry. Optional update checks retrieve version information from GitHub or Microsoft Store; see [privacy](../PRIVACY.md).

Damaged indexes are preserved and recoverable PNGs are rediscovered. Recovered files can lose their original capture mode and are labeled Area. If saving history fails during a capture, the preview still offers Copy and Save and displays a warning.

For an isolated history/settings folder, launch `Snipsera.exe --data-dir "C:\path\to\folder"`. The same folder permits only one app instance. Use a separate folder for development tests so production history is unaffected.

## Annotate and edit

The **1.0.8-preview.1 local candidate** adds direct **Edit** buttons in History, inline text, richer annotation selection, combined images and backgrounds. See the [editor guide](EDITOR.md) for the full workflow and [verification status](EDITOR-REFINEMENT-VERIFICATION.md) for remaining acceptance checks.

Open **Edit** from a History row, **Annotate** from a preview or pin, or **Open image / project…** on Home. PNG, JPEG, `.snipsera` and legacy `.framelet` files are supported. JPEG orientation is applied on import.

- **Select / move** selects and moves an annotation or added image. Corner handles resize supported objects. Arrows have endpoint handles; Curved arrows also have a bend handle. Toolbar changes apply to the selection and can be undone.
- **Text** lets you type where you click on the image. Enter finishes, Shift+Enter adds a line, and Esc cancels the current text edit. Double-click a note to edit it. Choose Plain, Outline, Shadow, Label, Soft label, Callout or Marker. **Stamp** adds symbols such as ✓, √ and ×.
- **Add image**, file drops onto the canvas, Ctrl+V and **Layers > From history…** add images to a composition. Move and resize them independently or change their overlap order. The base screenshot stays anchored; **Crop** affects that original image only.
- **Background**, **Layers** and **Image size** open named sections in the side panel. Background offers 15 gradients, solid colors, padding, alignment, aspect ratio, rounded corners and shadow. **Auto-balance** trims uneven plain margins and centers the content; it can be undone. It does not rearrange added images.

Edits save automatically to local History after a short pause and before closing. Closing normally needs no save-or-discard prompt. If saving fails, the editor stays open with an error so you can retry or export. History retains the latest 100 captures; export anything you need to keep permanently. Changes just before a crash may not have reached disk.

**Save project…** writes the original pixels, added images and editable annotations into one file. New saves use **project version 4**, even with a `.framelet` filename. This candidate reads versions 1–4, but **Snipsera 1.0.7 and earlier cannot reopen version 4 projects**. Save a separate copy if you need to retain an older project for an older app. Session undo history is not stored.

PNG, JPEG, Copy, Pin and drag sharing use the rendered result. JPEG fills transparency with white. A solid mask hides content in flattened output; editable projects still retain the original pixels. Blur and pixelation are not secure redaction. Reopening a History capture restores saved editable layers and updates the same entry.

Editor shortcuts: **Ctrl+Z** undo, **Ctrl+Y** redo, **Ctrl+S** save project, **Ctrl+C** copy the edited image, **Ctrl+V** add a clipboard image and **Delete** remove a selected object. Text fields retain their own text shortcuts. Save project opens a filename dialog, including for an already saved project.

## Capture and platform limits

- Intended target: Windows 11 x64. Snipsera packages include .NET; historical Framelet ZIPs require the .NET 10 Desktop Runtime. Other Windows releases and ARM64 have not been certified.
- Screenshots use GDI to copy visible screen pixels. Minimized, hidden, off-screen and protected content are not reconstructed. Other always-on-top applications can still cover a selected window.
- Output is a static SDR PNG. HDR color fidelity, game/exclusive-fullscreen capture and secure desktops are not certified. The pointer itself is not deliberately composited into the screenshot.
- Full display means one monitor, not a stitched all-monitor export. Area selection spans the virtual desktop. Coordinate tests cover negative origins and scaled layouts; mixed-DPI multi-monitor hardware still needs a separate acceptance pass.
- The editor supports images/output up to 20,000 pixels per side and 40 million total pixels, up to 500 annotations and 50,000 total freehand points. The 1.0.8 candidate supports 32 added image layers and an 80-million-pixel decoded asset budget, including assets retained for undo. Padding is 0–500 pixels; project metadata is capped at 4 MB and embedded image data at 160 MB. An image can remain capturable/viewable under Phase 1 limits even if it exceeds editor limits.
- Scrolling capture requires overlapping, stable views of one vertical scroll region. Sticky elements, animations, horizontal movement, large jumps, changing window geometry and repeating/blank content can prevent reliable alignment. Exclude fixed elements when selecting the region. Auto-scroll uses a native accessibility scroll action when available, with guarded native wheel input as a fallback. Applications that support neither can be scrolled manually.
- No OCR, upload links or persistent pins. Screen recording, optional Windows startup and update notifications are supported as described above. GitHub installers remain unsigned; Microsoft Store packages use Store distribution and signing. Project files are Snipsera's own format; `.cleanshot` files are not supported.
