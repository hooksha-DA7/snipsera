# Editing screenshots

These instructions cover **Snipsera 1.0.9**. Earlier versions have fewer editing controls.

Open **Edit** beside a capture in History, or **Annotate** on a preview or pin. History opens the original image with its saved edits. **Open image / project…** also accepts PNG, JPEG, `.snipsera` and legacy `.framelet` projects.

## Select, move and change annotations

Choose **Select / move**, then click an object on the image or in **Layers**. Drag it to move it. Corner handles resize text, rectangles, ellipses and added images. Arrows and lines have endpoint handles; Curved arrows also have a bend handle. An added image keeps its proportions when resized.

Use the toolbar to change a selected annotation's color, width, font size or style. Arrow choices are **Classic**, **Filled**, **Open** and **Curved**. **Clean** and **Hand-drawn** control supported shape and pen styling. Delete removes the selected object. Completed moves, resizing, restyling and removals can be undone.

## Write directly on the image

Choose **Text** and click where the note belongs. Type in place, then press **Enter** to finish. **Shift+Enter** adds a new line; **Esc** cancels the current text edit. Clicking elsewhere also finishes the edit. Double-click an existing note to edit its words, or select it and use **Edit selected text on image**. After finishing, use Select / move to reposition or resize it.

Choose one of seven styles: **Plain, Outline, Shadow, Label, Soft label, Callout** or **Marker**. Their samples show the effect before placing text. Label and outline colors adapt to keep light colors readable. Plain retains the appearance of older projects.

**Stamp** places a reusable symbol: ✓, √, ×, ✕, x, !, ? or ★. Stamps remain editable text objects. **Counter** places numbered circles.

## Combine images

Use **Add image**, drag PNG/JPEG files onto the canvas area, or paste an image with **Ctrl+V**. **Layers > From history…** adds a recent screenshot, including its visible edits, as one image layer. It does not import that screenshot's individual annotation objects. File drops are accepted in the canvas viewport, not on the toolbar or side panels.

Select an added image to move or resize it. It can sit beside, above or partly over the original; the composition expands to include it. **Bring image forward** and **Send image backward** change overlap between added images. Annotations sit above the images. Up to **32 added image layers** are supported.

The original screenshot stays anchored. **Crop** crops that original image only; added images are kept. Delete an unwanted added layer through selection or Layers. Undo restores it.

## Background and image size

**Background**, **Layers** and **Image size** each open a named section in the editor's side panel. They replace the former combined settings control.

Background offers **15 gradients**, solid colors and transparent padding. Adjust padding, rounded corners, soft shadow, alignment and aspect ratio. Since 1.0.9 the image updates while you drag the **Padding** or **Corners** slider; releasing it saves the change as one undo step, and **Esc** during the drag restores the previous value. Ratios include Automatic, square, landscape and portrait choices; extra space is added without stretching the content. Alignment is enabled when a chosen fixed aspect ratio adds spare space. It is disabled for Automatic, or when the image already fills the chosen ratio. Click **Remove background** to return to an unframed composition.

**Auto-balance** trims uneven, plain outer margins on the original screenshot and centers the result. It works best on a uniform background. It preserves annotations and the original pixels, and can be undone. It does not identify subjects or rearrange added images.

Open **Image size** from the dimensions control to change output dimensions. Keep the ratio locked to avoid stretching. The footer shows final dimensions including the background. Fit and percentage zoom change the view only.

## Saving and sharing

Edits save automatically to the capture's local History after a short pause and before closing. Closing normally needs no save-or-discard prompt. If saving fails, the editor stays open so you can retry or export. History retains the latest 100 captures; export anything you need to keep permanently.

**Save project** keeps the original image, added images and editable annotations in one file. New saves use **project version 4**, including files with a `.framelet` extension. Snipsera 1.0.8 and later read versions 1–4, but **Snipsera 1.0.7 and earlier cannot reopen version 4 projects**. Save a separate copy when an older installation still needs the original project. Session undo history is not saved.

PNG, JPEG, Copy, Pin and drag sharing use the rendered result. JPEG fills transparency with white. A solid mask hides pixels in flattened output, but editable projects retain the original pixels. Blur and pixelation are not secure redaction.

Shortcuts: **Ctrl+Z** undo, **Ctrl+Y** redo, **Ctrl+S** save project, **Ctrl+C** copy image, **Ctrl+V** add a clipboard image and **Delete** remove a selected object. Text fields keep their normal text shortcuts. Esc also cancels an unfinished drawing gesture.

## Limits

Images and output are limited to 20,000 pixels per side and 40 million output pixels. The original and added image assets, including assets retained for undo, share an 80-million-pixel budget. Other limits are 500 annotations, 50,000 freehand points, 500 pixels of padding, 4 MB of project metadata and 160 MB of embedded image data. PNG and JPEG are the supported image inputs; animated images and `.cleanshot` projects are not supported.

See [verification and remaining checks](EDITOR-REFINEMENT-VERIFICATION.md).
