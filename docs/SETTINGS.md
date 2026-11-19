# Settings and save locations

In version 1.0.6, open **Settings** on Home or in the tray menu. The sections are **General**, **Save locations**, **Keyboard shortcuts**, **Updates** and **Appearance**. The section list moves above the page in a narrow window.

The preview preferences, screenshot stack and reopen shortcut below describe the current local build. These additions have not been published yet.

## General: previews

Under **Settings → General → Previews**, **Dismiss after sharing** is on by default. A successful Copy, an explicit Save, or a drag accepted as a copy dismisses the unpinned screenshot card. Its capture and saved edits remain in History. Canceled or failed actions and automatic saving to the screenshot folder leave the card in place. Pins and recording cards stay visible after sharing. If its history save failed, the screenshot also stays available so you can keep a copy.

Turn the setting off to keep unpinned screenshots visible after sharing. The choice applies immediately and is saved between sessions. If saving the preference fails, the previous choice stays active. An unreadable preference file is preserved and the app uses the default with a recovery notice.

Pending screenshots form an overlapping deck at the bottom left. The latest capture is in front, with up to two narrower cards peeking out behind it. Click an exposed edge to bring that screenshot forward. With a screenshot card focused, Alt+2 or Alt+3 selects a rear layer. An unpinned recording remains a separate full card below the deck and uses one of the three slots; short displays can show fewer layers. **+N** opens additional screenshots. Capturing again or unpinning an image keeps the other pending screenshots. The front card retains its on-image controls and same-window Pin/Unpin behavior.

Use **Clear unpinned previews** in the tray or **+N** menu to clear the pending cards without removing pins, screenshot history or saved recordings. **Reopen last dismissed capture** in either menu, or its keyboard shortcut, brings back the last dismissed screenshot. Both the pending stack and dismissed list last for the current session only. History keeps the latest 100 screenshots for reopening after a restart. These changes do not add a separate library or cloud storage.

## Appearance

Open **Settings → Appearance** to choose **Tinted surface** or **Frosted glass** for the all-in-one screenshot bar, recording setup and the controls shown during recording. The setting applies immediately and is kept between sessions. The default is Tinted surface at 80% background opacity.

The opacity slider runs from 70% to 100%, with quick buttons for 70%, 80%, 90% and 100%. Higher opacity hides more of the background. Only the background changes; text, icons, button layout and the separate pin-opacity control stay as they are. With Frosted glass, this controls the tint over Windows Acrylic, rather than the blur strength.

Frosted glass requires Windows 11 version 22H2 or later. A solid surface is used when transparency is off, a contrast theme is active, Acrylic is unsupported or its native setup fails. Windows can also reduce the effect for graphics or power settings. The saved choice is retained; Snipsera does not change Windows settings. An unreadable appearance file uses the defaults and shows a recovery notice. A failed save keeps the previous preference.

## Save locations

Choose separate folders for screenshots and recordings under **Settings → Save locations**. **Change folder…** opens the Windows folder picker; **Open folder** shows the selected folder, and **Reset to default** restores that folder's default. A rejected or unwritable folder leaves the previous preference in place.

Screenshots save automatically by default to **Pictures\Snipsera**. Their edits update the same PNG automatically while you work and when you close the editor. Choose another screenshot folder to use it for future saves. **Save screenshots automatically** can be turned off; Copy, Save, editor export and the usual editable history remain available. The selected screenshot folder is also the starting location for Save and editor export dialogs. These saved PNGs are separate from the latest-100 capture history and are not deleted when history expires or an entry is removed.

Videos and GIFs always save automatically when you stop. Their initial default remains `recordings` under the app's local data folder, including the legacy Framelet folder if this copy still uses it. Choose a more convenient recording folder here. **Show in folder** on a saved recording opens its actual location, even if you have changed the default since recording it.

Folder changes apply to future saves. Existing files stay in their previous folders. A recording keeps the destination selected when it starts. If a folder becomes unavailable, the app reports the failure; it does not silently claim the file was saved somewhere else. A screenshot whose external save fails can still be in history and offers Copy and Save. Recording failures retain a recovery file when possible and report its path.

## Keyboard shortcuts

Open **Settings → Keyboard shortcuts**, select an action box and press the keys you want, then **Save shortcuts**. Print Screen works on its own. **Discard changes** restores the saved bindings. The all-in-one capture shortcut and **Reopen last dismissed capture** are listed with the four direct capture shortcuts. Reopen defaults to **Ctrl+Alt+Shift+6**. Existing shortcuts are preserved on upgrade; an unused default is chosen if that chord is already assigned. Home's shortcut link opens this section directly.

Capture shortcuts pause while a box records a new key combination. Leaving the section, hiding or closing Settings, or switching to another app releases that pause. A rejected shortcut change keeps the previous bindings. Windows can reserve Print Screen for Snipping Tool; change that setting yourself under Windows **Accessibility → Keyboard** if needed.

## Recording results

Stopping a recording closes the recording controls without reopening the setup panel. A compact recording card joins the side stack and uses the same on-card controls as screenshots. The eight-pin limit is shared; if it is full, the new recording uses the preview slot.

- **Play** or double-click opens the recording in its associated player.
- **Copy** puts the actual MP4 or GIF file on the clipboard, and **Drag** sends that file to another compatible app.
- **Save…** saves a separate copy; the original recording stays where it was saved.
- **Pin/Unpin**, move, resize, opacity, temporary hide and close follow the screenshot workflow. Closing a card does not delete the recording.
- **Show in folder** is available in the card's right-click menu.

The preview is a still thumbnail; it does not start playback or capture audio on hover. It appears immediately with a placeholder if thumbnail extraction is still in progress or unavailable.

## Capture bar

All-in-one capture opens at the bottom centre of the current work area, above the taskbar. Drag its grip to move it. The app leaves a manually moved bar where you put it for that opening. Screenshot modes and the capture/history flow are unchanged.
