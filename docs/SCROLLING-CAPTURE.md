# Scrolling capture — 0.6.0

## Workflow

Launch Scrolling capture from home, the tray or an image's right-click menu. Select only the content that scrolls, leaving fixed headers, footers and scrollbars outside. Leave a gutter for controls and the growing preview. Start, scroll downward in overlapping steps, then choose Done. Cancel/Esc restores the previous windows without adding a history image. A successful result uses the existing preview, history, copy, save, pin, drag and editor workflow.

The interaction follows the supplied `scrollingcapture.mp4`: region selection, Start, manual scrolling, a growing preview on the left, optional Auto-scroll and Done/Cancel. Existing on-image preview/pin controls remain in place.

## Capture and alignment

Framelet captures the chosen physical rectangle and samples settled views. It looks for a unique vertical overlap with the last accepted viewport. Only newly exposed bottom rows are appended. Identical views and upward motion do not extend the image. Missing or ambiguous overlap leaves the collected pixels intact, pauses Auto-scroll and explains how to recover. Finishing samples the final stable view. If that view cannot be matched, a warning explains that the image ends at the last good portion.

Each appended strip stores only its new rows. The live preview is bounded to 180 × 320 pixels; the final output preserves full capture resolution. Output is bounded to 20,000 pixels per side, 40 million pixels, 1,000 accepted views and a ten-minute collection session. Limits stop collection and leave Done/Cancel available. No network, DOM access, OCR or destination-app plugin is used.

Auto-scroll is opt-in. It resolves a native accessibility scroll container in the external window selected at the beginning; targeted Windows wheel messages are a fallback. Accessibility calls run on a dedicated background thread and read scroll capability/geometry, without reading document text. Waiting for a step has a cancellation deadline; an in-flight provider call may finish later, with cancellation checked before issuing a scroll action. No clicks, typing or pointer movement are generated. A moved/minimized target pauses collection. After several unchanged views, Auto-scroll stops so the user can finish or scroll manually. Some apps support neither route; manual scrolling remains independent. No new global capture hotkey is assigned; the existing four shortcuts/settings keep their current format.

## Constraints

- Vertical downward scrolling within one stable, visible area. Scrolling upward is ignored until returning past the last captured position.
- Keep overlap between views. Large jumps or dragging a scrollbar too far can lose the match. Return to a known portion, then continue slowly.
- Exclude fixed/sticky headers, footers, floating widgets and scrollbars. This version does not automatically remove them.
- Blank or repeating content may have no unique match. Animated media, cursors, sticky elements, horizontal movement, zoom changes and lazy-loading layout shifts can also interfere. No invisible or off-screen content is reconstructed.
- The selected area must be at least 80 × 80 physical pixels. Controls, border, dimming and preview stay outside it; the selection hole stays fully transparent so native scrolling can pass through. If no gutter fits, reselect a smaller area.
- During the session, other capture commands queue. Existing pins and dirty editors are restored after completion/cancellation. The previous preview is replaced only after success; its original remains in history.
- Stitched output is an SDR bitmap, subject to the same GDI/protected-content limits as ordinary capture. Auto-scroll does not prove that the entire document was captured.

## Verification

**Status: development preview; final desktop acceptance paused by the user on 2026-11-01. No 0.6.0 stable release has been published.**

- The integrated build passed 79 core checks and the separate existing capture-lifecycle integration before the final Auto-scroll driver update. Receipts: `verification/runs/scroll-final-01` in the development checkout.
- A native 125% test exposed a clipped Start button. A rendered-bounds test reproduced it. After correcting measurement and placement, the test passed for Ready, Capturing and Finishing. The native repeat confirmed the complete bar and left preview. Receipts: `scroll-rendered-01` (failure), `scroll-rendered-02` (pass), and `scroll-native-03`.
- Manual native capture passed through the overlay, joined two downward movements and committed a 947 × 898 Scrolling image to isolated history. Numbered sections appear in order, and no capture controls appear in the image. A comparison with a later native viewport agrees on 99.55% of pixels; small localized differences remain to be checked under idle input. It was not byte-exact, so native pixel acceptance is not claimed. The pure stitching tests are pixel-exact against their synthetic originals.
- The first wheel-message Auto-scroll path did not move the WPF test document. A native accessibility driver and explicit external-HWND lookup were added. The final candidate compiles with zero warnings/errors, and all eight synthetic backend/geometry checks pass without showing windows or interacting with the desktop. These checks do not exercise an external accessibility provider.
- Final live Auto-scroll/provider verification, clean native pixel comparison, active-session Esc/Cancel, broader theme/hardware coverage and extracted-preview launch remain pending. Desktop testing is paused as requested. Mixed-DPI hardware, native high contrast and native 150%/200% acceptance are not certified.

The current source/build is a reviewable candidate. Keep the previously verified 0.5.2 release for normal use until acceptance is complete. Existing release archives are preserved. Resume desktop testing only after the user says the desktop is available.

Run `./test.ps1 -Output verification/runs/<fresh-name>`. The checks executable can open a separate synthetic numbered document using `--scroll-fixture "<fresh-output-directory>"`; use isolated Framelet data when capturing it. Reference video, development logs, test screenshots and synthetic source documents are excluded from release ZIPs.

API references: [Windows layered-window hit testing](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features), [ScrollPattern.Scroll](https://learn.microsoft.com/en-us/dotnet/api/system.windows.automation.scrollpattern.scroll?view=windowsdesktop-10.0), [WM_MOUSEWHEEL](https://learn.microsoft.com/en-us/windows/win32/inputdev/wm-mousewheel). These document the platform APIs; they do not establish compatibility with every destination app.
