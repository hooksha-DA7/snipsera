# Settings and save locations

In version 1.0.6, open **Settings** on Home or in the tray menu. The sections are **General**, **Save locations**, **Keyboard shortcuts**, **Updates** and **Appearance**. The section list moves above the page in a narrow window.

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

Open **Settings → Keyboard shortcuts**, select an action box and press the keys you want, then **Save shortcuts**. Print Screen works on its own. **Discard changes** restores the saved bindings. The all-in-one capture shortcut is listed with the four direct capture shortcuts. Home's shortcut link opens this section directly.

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
