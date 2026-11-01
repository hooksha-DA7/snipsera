# Scrolling repair — preview 3, 2026-11-01

The user reported repeated recovery warnings and Auto-scroll not working in Chrome. This revision repairs reproduced backend defects. It remains a development preview: desktop testing was paused by the user, so actual Chrome input delivery and final native output quality are pending. Version 0.5.2 remains the last verified release. Existing controls, placement, styling, image/project formats and capture integration are unchanged.

## Confirmed defects and corrections

| Finding | Verification and action |
| --- | --- |
| The previous session discarded moving frames, letting continuous manual scrolling outrun the last accepted viewport. | Source path confirmed; a synthetic session now advances on every sample and produces the exact complete source. Capture intermediate positions approximately every 80 ms plus processing time. |
| Uniform sampling/whole-area error could reject sparse article content with a changing widget. | The old matcher failed the off-screen article regression at a 47-pixel movement. Match visible vertical detail, retain consensus/uniqueness checks, and copy original output pixels. |
| The previous search excluded valid overlaps shorter than 25% of the viewport. | The old matcher failed a 395-pixel movement in a 480-pixel article viewport with identifiable remaining content. Search down to max(48 rows, one eighth of the viewport), still requiring unique detail. |
| In-place changes could remain a persistent unmatched reference. | A changed stationary widget now returns Unchanged, preserves saved pixels and permits the following scroll. Refresh the anchor only after a certified zero-offset match; ambiguous views cannot advance it. |
| Fractional text rendering could lose a match or accumulate row-rounding errors. | Off-screen fractional text initially failed at 60.4 pixels. Refine several overlap candidates at subpixel offsets, carry fractional displacement between frames, and check cumulative height. Matching filters do not alter saved strips. Native rendering quality is still pending. |
| Auto-scroll could submit another step before the previous animation settled. | An injected animated driver verifies only one pending step and exact combined output. Require settling and observed pixel movement before advancing. |
| Driver success did not prove movement, so a no-op provider never reached fallback. | A fake provider reports success without movement; the next attempt now uses the wheel path and capture completes. Four unchanged attempts pause explicitly. |
| A slow accessibility lookup could consume the entire step token, skipping fallback. | Source path confirmed. Chrome bypasses the accessibility lookup. Other apps use a separately bounded 600 ms provider attempt within the two-second step budget. External provider timing remains untested. |
| Failed accessibility searches were repeated each step. | Cache a missing pattern for the session. Live provider compatibility is pending. |

Transient mismatches are retried before showing recovery guidance. A separate regression verifies that a genuine gap still warns and saves only the last aligned portion. Done cancels pending input; rapid Auto off/on cannot be stopped by an older cancelled request.

## Verification of follow-up findings

| Finding | Disposition |
| --- | --- |
| V1: an intermediate placeholder could remain permanently in already appended rows. | Confirmed. Refresh the rows covered by each newer aligned viewport and the settled final viewport. Two pixel-exact regressions cover repainting with and without further movement. Content that scrolled out before painting remains a limitation. |
| V3: ambiguous sparse/low-contrast movement could be reported as Unchanged. | Confirmed. Only byte-identical frames or a certified zero-offset match count as unchanged. A low-contrast repeated-line regression now reports NeedsOverlap. |
| V4: continuous animation could suppress a genuine overlap-loss warning indefinitely. | Confirmed and corrected with a 1.5-second mismatch bound. A large changing-region regression verifies the warning. Auto-scroll through large video/animation is still unsupported; exclude that content from the area. |
| V5: smooth detail could fail matching. | Reproduced with a smooth gradient page. Use weaker detail only when strong detail is insufficient, and compare independent local minima rather than adjacent candidates in one gradient basin. Exact gradient joins now pass. |
| V8: preview rebuilding on every append increases work as the capture grows. | Confirmed by source. Throttle bitmap rebuilds to at most once per 350 ms. This reduces work; a long native-session performance claim is not made. |
| V9: fractional carry might drift on integer motion with pixel noise. | Not reproduced in the new eight-step noisy-article regression; exact integer heights pass. Broader native rasterization remains pending. Carry is retained because resetting it on an integer step would lose a legitimate prior fractional offset. |
| V10: a thrown accessibility search could repeat; stale cancellation could stop re-enabled Auto. | Corrected: mark the lookup attempted before calling the provider; use an Auto generation counter. The rapid off/on cancellation regression passes. |
| V11: unchanged tick notifications and transient status changes could cause redundant UI layout/announcements. | Backend now deduplicates unchanged state and keeps status stable during transient mismatches. No UI file was changed. Further native screen-reader/layout profiling remains pending. |
| V2/V6/V7: overlay hit testing, foreground routing and wheel-sensitive content under the pointer. | Native verification is pending. Keep the target/foreground guards; do not weaken them based on an unobserved assumption. The pointer movement and pause conditions are documented. Maps, canvases, viewers and nested scroll containers need a correctly chosen region and live verification. |

The final no-window suite contains 29 passing checks, with zero compilation warnings/errors. Source analysis and synthetic tests do not certify a real Chrome session.

## Chrome input change

Queued `PostMessage` success meant only that Windows accepted the message, not that Chrome processed a scroll. Preview 3 uses small native wheel events for Chrome/Chromium windows. On explicit Auto-scroll, the pointer can move once from Framelet's control to the selected content. Input requires the recorded target to be active and under the pointer. Pointer exit, app switching, mouse buttons or modifiers pause scrolling. No clicks or typing are generated. Windows integrity restrictions still apply.

This is an implemented correction, not a claim of live Chrome acceptance. The no-window tests validate the input structure and the session's simulated pacing/fallback/cancellation. They do not call SendInput, move the real pointer, read the desktop, or contact a real accessibility provider.

## Review and evidence limits

An independent source review checked the original failure paths, with regressions added for confirmed findings. Original Chrome wheel-HWND routing and the user's selected website/header/sidebar configuration were not observed, so those remain hypotheses rather than confirmed causes of this particular test failure. The review's numerical assumptions about Chrome wheel distance and accessibility step size are not used as verified facts.

Fixed headers/footers, large overlays, blank/repeating content and layout shifts still require careful selection; this revision does not implement sticky-header removal or reconstruct unseen content. A no-movement pause does not prove the bottom of the document. Integer-offset synthetic joins remain byte-exact; fractional rendering is checked for match and cumulative height, not native pixel equality.

Development receipts are under `verification/runs/scroll-repair-01`; source reviews are under `design/scrolling-repair-2026-11-01`. These directories and the reference video are excluded from user packages. This repair first shipped as `0.6.0-preview.3`; preview 4 includes the same scrolling code plus temporary overlay hiding. No installation, production-history change or stable release was performed.

Resume native testing only after the user makes the desktop available: verify Chrome manual/Auto, a nested scroller, pointer/focus interruption, Done/Cancel, clean output seams, existing capture restoration and an extracted-package launch. The 0.5.2 application binaries remain unchanged.
