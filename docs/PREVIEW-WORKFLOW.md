# Screenshot preview workflow

Version 1.0.7 candidate, 19 November 2026. These changes have not been published or submitted to Microsoft Store yet; the Store submission draft is prepared. GitHub will remain a preview until Microsoft approves the matching Store update.

New screenshots join a layered deck at the bottom left. The front screenshot keeps the familiar 200 × 150 card and its on-image controls. Up to two narrower cards peek out behind it, with 20-pixel logical steps and soft outlines. Click an exposed edge to bring that screenshot forward. The deck stays compact on hover. A **+N** button opens older pending captures in a scrollable thumbnail list. Selecting one brings it into view without dismissing another capture. Short displays can show fewer layers; an unpinned recording uses one of the three visible slots and remains a separate full card below the screenshot deck.

Rear layers only select a capture; their Copy, Save, Pin, Close and drag actions are unavailable until brought forward. When a screenshot card has keyboard focus, **Alt+2** and **Alt+3** select the second and third layers. Enter or Space activates a focused rear edge. Pinned references remain separate from the deck.

An unpinned screenshot preview closes after a successful Copy, explicit Save, or drag-and-drop accepted as a copy by another application. Its original capture and saved edits remain in History, subject to the existing 100-capture retention limit. Closing the card does not remove a saved PNG or the staged drag file.

Canceling a Save dialog, rejecting or canceling a drop, or failing to copy/save leaves the preview available. Automatic background PNG saving does not dismiss a new capture. Pinned images stay on screen after these actions. Recording cards keep their existing behavior.

If the capture is missing from History, the preview stays open with a notice even after a successful transfer. A late completion affects only the source capture, including one moved into overflow, and checks its current pinned state. It cannot dismiss a reopened or newer replacement of that capture.

Copying, exporting a PNG/JPEG or completing a drag from the editor first flushes pending edits to History, then marks that exact edit state as shared. If the history write fails, it keeps the preview available and displays the failure. Closing the editor still saves edits to History, but does not restore a preview of the already shared state. Further edits make the result eligible for a preview again. Saving an editable project alone is not a sharing action.

## Preview controls

- **Reopen last dismissed capture:** available in the tray menu and overflow list. Its default shortcut is **Ctrl+Alt+Shift+6**, configurable in Settings > Keyboard shortcuts. Upgrades keep existing shortcuts and choose another default if that chord is already assigned. Reopening loads the latest saved edits. Repeating it restores earlier dismissed screenshots when their media is still available.
- **Clear unpinned previews:** clears pending cards and an unpinned recording card, including overflow. Pins, History, saved images and recording files remain. An editor already open continues saving edits but does not bring back a card that was explicitly cleared.
- **Dismiss after sharing:** saved under Settings > General > Previews, on by default. Turning it off keeps screenshot cards after successful transfers. It does not change History saving or pin behavior.

Pin and Unpin keep the same window and controls. Unpin returns a screenshot to the pending stack without replacing another capture. Docked pins reflow around the pending cards; manually positioned pins stay where they were placed. All pending cards follow temporary hide/restore and capture suspension. Overflow controls close when the group hides or capture starts.

The pending and dismissed lists last for the app session. Restarting clears those lists, while History and the dismissal setting persist. Ordinary overflow items retain History references and load their images on demand. The overflow list uses independent small thumbnails and releases them when it closes. Captures that could not be saved retain recovery pixels in memory. The stack does not extend History's retention period or create a permanent library.

## Verification

The release check script includes isolated checks for transfer completion, preferences and shortcut migration, stack ordering and layout, app/editor integration, and detached deck and overflow visuals. They use synthetic images without showing windows, writing to the clipboard, opening a file picker or initiating a native drag. The candidate passed a fresh dependency restore, a Release build with zero warnings/errors, all 23 safe check modes and 43 Python checks. Detailed results and package verification status are recorded in [build status](../BUILD-STATUS.md).

The isolated app launched and its accessibility tree was readable, but desktop screenshots of both the app and a separate test window returned black images. Live clipboard delivery, file-picker cancellation, accepted/rejected drops into other applications, popup/deck focus, native layering and hit targets, monitor placement and capture exclusion still need desktop acceptance. Offscreen completion and render tests do not establish those native behaviors. Local installation and clean Windows 11 acceptance also remain pending.

## Later roadmap, not implemented

A later Library window can unify screenshots, MP4 and GIF with filters, favorites and editable originals. Its retention and migration policy should be defined before turning the current rolling history into a permanent collection. Keep one capture engine and shared storage initially. Optional cloud sharing/synchronization and broader redesign are a later v2 phase, not part of this change.
