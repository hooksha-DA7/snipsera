# Releases

Start with [0.5.2](https://github.com/hooksha-DA7/framelet/releases/tag/v0.5.2) for capture and annotation. The 0.6 previews add scrolling capture and temporary overlay hiding; both still need testing on a real desktop.

## 0.6.0-preview.4: Temporarily hide previews and pins

2026-11-01 · Feature preview · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.6.0-preview.4)

Clear the floating images out of your way, then bring them back when you need them.

### Added

- Scroll down one notch over a preview or pin to hide the floating group.
- A small tab at the bottom left: click it or hover briefly to restore the images.
- Hide/restore actions in the image right-click menu, tray menu and home window.
- Kept pin positions, sizes and opacity while hidden; new captures join the hidden group until you restore it.

### Improved

- Added a short slide/fade transition that respects Windows reduced-motion preferences.
- Kept Ctrl+mouse wheel available for pin opacity and left the on-image controls in place.

Experimental: hide/restore has passed automated and off-screen rendering checks, but real desktop interaction is still untested. Chrome scrolling also needs live testing. The ZIP's BUILD-STATUS heading says preview 3 in error; the app and filenames correctly identify preview 4. Packaging update, 2026-11-01: documentation refreshed; application binaries unchanged.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.4/Framelet-0.6.0-preview.4-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.4/Framelet-0.6.0-preview.4-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.4/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.6.0-preview.3: Scrolling reliability fixes

2026-11-01 · Bug-fix preview · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.6.0-preview.3)

Updates to reduce repeated overlap warnings and improve Auto-scroll in Chrome.

### Improved

- Matched pages with sparse text, short overlaps and small changing widgets more reliably in automated checks.
- Changed Chrome Auto-scroll to small native wheel steps. It pauses if you leave the selected area, switch apps or hold a modifier key.
- Reduced preview rebuilds while scrolling and delayed warnings during brief page changes.

### Fixed

- Collected intermediate views during continuous scrolling so useful overlap is not discarded.
- Corrected accumulated offsets when text moves by fractional pixels.
- Refreshed overlapping content and the final view when images finish loading late.
- Checked that the page moved before sending another Auto-scroll step, and stopped pending steps on Done or Cancel.

Experimental: automated scrolling checks pass, but these changes have not yet been tested in a live Chrome session. Scrolling capture may still need manual adjustment. Packaging update, 2026-11-01: documentation refreshed; application binaries unchanged.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.3/Framelet-0.6.0-preview.3-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.3/Framelet-0.6.0-preview.3-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.3/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.6.0-preview.2: Scrolling capture

2026-11-01 · Feature preview · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.6.0-preview.2)

Capture a page or document that is taller than the screen.

### Added

- Select a scrolling region, start capture, scroll down and choose Done to create one tall image.
- A growing preview and optional Auto-scroll, with Cancel to discard the session.
- Copy, Save, Pin and editing for the completed scrolling image.

Early preview. Overlap warnings and Chrome Auto-scroll failures were reported; preview 3 changes the scrolling behavior. The original ZIP filenames omit the preview number. Packaging update, 2026-11-01: documentation refreshed; application binaries unchanged.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.2/Framelet-0.6.0-preview-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.2/Framelet-0.6.0-preview-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.6.0-preview.2/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.5.2: Controls back on the image

2026-11-01 · Interface correction and bug fixes · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.5.2)

Copy, Save, Pin and the other floating actions are back on the screenshot itself.

### Improved

- Restored corner actions and centered Copy/Save buttons on previews and pins.
- Added compact layouts for shallow or narrow images, keeping drag access available.
- Improved button contrast, pinned states and rounded keyboard-focus outlines.

### Fixed

- Kept floating controls inside the image without overlapping each other across the supported layouts.

Recommended for capture and annotation. This is the last build tested on a real desktop; scrolling capture and temporary overlay hiding are available in the 0.6 previews. Packaging update, 2026-11-01: documentation refreshed; application binaries unchanged.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.2/Framelet-0.5.2-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.2/Framelet-0.5.2-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.2/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.5.1: Preview and pin sizing fixes

2026-10-31 · Bug-fix and interface update · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.5.1)

Previews and pins use the same size limits, and Pin/Unpin acts on the image you clicked.

### Improved

- Moved floating actions into a hover toolbar outside the image in this version.

### Fixed

- Removed extra space around very wide or tall captures and preserved their proportions.
- Kept Pin/Unpin in the same window at the same size, with a visible active pin state.
- Returned unpinned images to the preview area without overlapping the remaining pin stack.

Superseded by 0.5.2, which returns the controls to the image. Use 0.5.2 for the current capture interface.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.1/Framelet-0.5.1-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.1/Framelet-0.5.1-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.1/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.5.0: Four arrow styles and repeated captures

2026-10-31 · Feature and bug-fix release · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.5.0)

More annotation choices, smoother floating images and fixes for taking another screenshot while the last one is still open.

### Added

- Classic, Filled, Open and Curved arrows, with visual style samples.
- Draggable arrow endpoints and a bend handle for curved arrows; each drag is one undo step.
- Capture actions in the preview and pin right-click menus.

### Improved

- Smoothed new hand-drawn freehand strokes.
- Replaced the old tray menu and confirmation boxes with themed menus and clearly named Save, Discard and Cancel buttons.
- Softened floating-image shadows and smoothed image edges and thumbnails.

### Fixed

- Allowed new captures while previews, pins and editors remain open. Requests made while a capture finishes are queued.
- Made pin edges fade with the image when changing opacity.
- Pressing Escape in a pin menu now dismisses the menu without closing the image.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.0/Framelet-0.5.0-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.0/Framelet-0.5.0-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.5.0/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.4.1: Easier capture buttons

2026-10-31 · Interface improvement · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.4.1)

Capture actions on the home screen are easier to find and click.

### Improved

- Made capture actions consistent, full-width rows with larger click targets.
- Reduced visual clutter when the rows are not being hovered or selected.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.4.1/Framelet-0.4.1-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.4.1/Framelet-0.4.1-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.4.1/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.4.0: Windows 11 styling and drag and drop

2026-10-31 · Feature and interface update · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.4.0)

Framelet follows Windows light/dark mode and adds drag and drop for screenshots.

### Added

- Drag and drop from previews, pins and the editor into compatible Windows apps.
- Pin opacity controls in the right-click menu and with Ctrl+mouse wheel.
- Arrow-key positioning for pins; hold Shift to move in larger steps.

### Improved

- Used Windows caption buttons, resize borders, Snap behavior and Mica where supported.
- Updated floating controls, Copy/Save buttons and pin edges to follow the Windows theme.
- Added fallbacks for Windows contrast, transparency and animation preferences.

### Fixed

- Kept the preview accessible when the eight-pin limit is reached.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.4.0/Framelet-0.4.0-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.4.0/Framelet-0.4.0-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.4.0/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.3.0: Smaller previews and a compact editor

2026-10-30 · Interface update · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.3.0)

Previews and pins take up less room, and the editor puts its tools in a compact horizontal toolbar.

### Added

- A resize grip and a double-click toggle between compact and larger pin views.
- Hand-drawn arrow and ellipse styles, with a Clean option for regular shapes.
- Undoable style changes for selected annotations.

### Improved

- Moved new previews to the bottom left and limited their initial size independently of screenshot resolution.
- Pins stack along the left, leaving room for the next capture. Pins you move or resize stay where you put them.
- Replaced large editor buttons and side panels with an icon toolbar and a collapsible image/layers panel.
- Updated typography, spacing and colors across the editor and history window.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.3.0/Framelet-0.3.0-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.3.0/Framelet-0.3.0-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.3.0/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.2.0: Annotations and image editing

2026-10-30 · Feature release · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.2.0)

Screenshots can now be annotated and edited before you share them.

### Added

- An editor for captures, imported PNG/JPEG images and saved Framelet projects.
- Arrows, lines, rectangles, ellipses, text, numbered counters, highlighting and freehand drawing.
- Crop, resize, backgrounds, padding and zoom, with undo and redo.
- Blur, pixelation and solid masks, plus flattened PNG/JPEG export, Copy and Pin.
- Editable .framelet projects and a save prompt when closing unsaved work.

### Fixed

- Project saving when writing the original image into a .framelet file.

Projects keep the original image and editable layers. To share a masked image, export a flattened PNG/JPEG; blur and pixelation are not secure redaction.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.2.0/Framelet-0.2.0-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.2.0/Framelet-0.2.0-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.2.0/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.

## 0.1.0: First release

2026-10-30 · First release · [Release page](https://github.com/hooksha-DA7/framelet/releases/tag/v0.1.0)

Capture a screenshot, copy or save it, and keep references pinned beside your work.

### Added

- Area, window and full-display capture, plus a shortcut to capture the last area again.
- Custom global shortcuts and a tray menu for capture actions.
- A floating preview with Copy and Save, and up to eight movable, resizable pins.
- Local history for the latest 100 captures, available after restarting the app.

### Download

[Windows ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.1.0/Framelet-0.1.0-win-x64.zip) · [Source ZIP](https://github.com/hooksha-DA7/framelet/releases/download/v0.1.0/Framelet-0.1.0-source.zip) · [Checksums](https://github.com/hooksha-DA7/framelet/releases/download/v0.1.0/SHA256SUMS.txt)

Extract the Windows ZIP and open **Framelet.exe**. Requires Windows 11 x64 and the [.NET 10 Desktop Runtime x64](https://dotnet.microsoft.com/en-us/download/dotnet/10.0). Quit any older Framelet copy from its tray menu first.
