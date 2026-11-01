# UI reference study — 2026-10-30

The user requested closer CleanShot UI/interaction fidelity, small left-side previews, compact stacked pins and hand-drawn-style arrows/circles. This replaces the earlier feature-led layout.

## Primary visual references

- [CleanShot features](https://cleanshot.com/features): Quick Access Overlay, floating screenshots and annotation controls.
- [Quick Access Overlay demo](https://cleanshot.com/video/home/quickaccess.mp4): compact bottom-left image thumbnail and controls revealed on interaction.
- [Floating screenshot demo](https://cleanshot.com/video/screenshots/pin.mp4): small image-first always-on-top cards with minimal permanent chrome.
- [Annotation demo](https://cleanshot.com/video/screenshots/markup.mp4): slim horizontal icon toolbar, neutral editor shell, large canvas and rounded open arrowheads.
- [CleanShot changelog](https://cleanshot.com/changelog): product/version context. These current vendor demos may differ from the supplied 4.5.2 binary.

The demos were opened and visually inspected in a browser. They inform proportions, control density and interaction design. Framelet uses its own vector icons and drawing implementation with Windows system fonts. No vendor artwork, fonts or implementation code is distributed.

## Supplied artifact inspection

REA opened the previously extracted `CleanShot X.app` resources from the supplied 4.5.2 DMG, then decoded the available Interface Builder archives. The expanded pass recognized 17 nib documents, including MainMenu and preferences, producing 3,618 graph nodes and 3,696 edges. The result contains control/selector evidence but incomplete hierarchy and no reliable editor/overlay pixel geometry.

Local development evidence: `verification/ui-redesign/rea-ui-resources.json`. It is excluded from distributable packages. This was resource analysis on Windows, not execution of the Mac application. The visual layout choices are grounded in the official videos rather than inferred from incomplete nib geometry.

## Framelet behavior

The implementation follows the observed compact thumbnail and editor approach, with the user’s explicit left-side placement. Pins stack separately without covering one another, reflow within a left-side band and preserve room for the next preview. Manually moved/resized pins remain where the user puts them. This is a focused independent Windows implementation, not a claim of complete CleanShot parity.
