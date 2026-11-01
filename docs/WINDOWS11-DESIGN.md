# Framelet 0.4.0: CleanShot interactions, Windows 11 presentation

2026-10-31. This pass responds to the supplied CleanShot widget/overlay images and the request that Framelet feel at home on Windows 11.

## Design decisions

| Surface | Appearance and behavior |
| --- | --- |
| Editor/history | Native Windows caption buttons, system menu, borders, maximize/restore and Snap support. Built-in WPF Fluent controls follow system app light/dark mode and accent colors. Supported main windows use WPF's native Mica backdrop. |
| Preview at rest | Small, sharp image at the bottom left. No permanently expanded control bar. |
| Preview on interaction | The thumbnail blurs behind a light/dark translucent tint. Labeled Copy/Save actions sit centrally; Pin/Close/Annotate/Drag sit at the corners. A short fade follows Windows animation preferences. Keyboard focus reveals the same controls. |
| Pinned reference | Image remains sharp on hover, with compact edge controls. Pins retain left stacking, manual movement, proportional resize and larger/compact double-click behavior. |
| Pin opacity | The image layer can become transparent while controls remain readable. Opacity changes neither source pixels nor exported output. |
| Reduced effects | High contrast uses system colors and disables frosting/animation. Turning off Windows transparency disables thumbnail blur/translucency; turning off animation removes the fade. |

Mica is an opaque, wallpaper-tinted base material. It is not live glass. The thumbnail hover effect is implemented as a blur/tint of the thumbnail itself, not desktop Acrylic. This keeps screenshots unmodified and respects Microsoft's distinction between persistent app surfaces and transient material treatments. No whole-window opacity is used to fade labels and controls.

The palette uses Windows-style neutral base/content layers, a restrained system accent, one-pixel theme-aware borders and eight-pixel floating-card corners. Interface text uses Segoe UI Variable Text with Segoe UI fallback. Captured content and annotation colors retain their own pixels.

## Interaction details

- Drag a preview image into a compatible app to transfer a PNG. On pins, dragging the body moves the reference; the bottom-right drag handle transfers the PNG. The editor's drag handle transfers the flattened edited image.
- Native drag data includes FileDrop and Bitmap formats. FileDrop targets receive a real full-resolution PNG staged under the local `drag-exports` folder. Files remain available after the drop and owned cache files older than seven days are removed on a subsequent drag.
- Pin options/right-click exposes opacity. Ctrl+wheel changes it in 5% steps, within 20–100%. Arrow keys move the focused pin one logical pixel; Shift+arrow moves ten. Manual movement removes that pin from stack reflows.
- Copy/save/project/export formats and capture history remain compatible with 0.3.0. Editable projects are still version 2, with version 1 read support.

## Verification

The final suite in `verification/runs/windows-fluent-final/` passes 63 checks with zero compiler warnings/errors. It includes the prior capture/editor/project/DPI checks plus effect policy, native drag payload/retention and light/dark native-control checks. Theme changes do not change rendered document pixels.

Native desktop inspection at 125% scaling covered light/dark editors, frosted preview controls, sharp stacked pins, native maximize/restore, pin opacity and keyboard positioning. DWM reports backdrop type 2 (main-window Mica) for the editor; layered thumbnail windows report type 0 and use their own view treatment.

The native PNG drop target reported `Received 900 × 560 PNG`; `verification/runs/windows-fluent-dark-01/native-drop.json` records the native FileDrop receipt. It is a local synthetic test, not a claim that every third-party app supports the same formats. The source PNG and received file hash are checked separately.

Synthetic UI evidence lives under `verification/runs/windows-fluent-*/`. Native editor/workspace captures include the real Windows caption; software content renders separately verify thumbnail states. These fixtures and original reference images are excluded from release ZIPs. The source package includes the repeatable test harness.

No OS theme, transparency or contrast preferences were modified for testing. Light/dark fixture modes override only the test application's theme. Contrast/transparency/motion fallback policies were tested deterministically; all OS preference combinations and mixed-DPI hardware were not manually certified. WPF marks ThemeMode experimental in .NET 10; the app scopes its warning suppression to that integration and remains dependent on the Windows Desktop Runtime.

## References

- [Windows app design](https://learn.microsoft.com/en-us/windows/apps/design/) and [Windows 11 principles](https://learn.microsoft.com/en-us/windows/apps/design/design-principles): calm, focused, familiar and coherent experiences.
- [Windows materials](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials): Mica for persistent base layers; Acrylic for transient flyouts; effect fallbacks.
- [Title-bar design](https://learn.microsoft.com/en-us/windows/apps/design/basics/titlebar-design): system caption behavior, typography and theme awareness.
- [Layering and elevation](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/layering): separation of content and command surfaces.
- [Microsoft Design: Office's Windows 11 refresh](https://medium.com/microsoft-design/behind-the-design-refreshing-the-office-apps-for-windows-11-b9bfef5b9d3e): content focus, restrained color, material hierarchy and OS theme alignment. This 2021 article is design context, not current implementation documentation.
- [WPF Fluent integration](https://github.com/dotnet/wpf/blob/main/Documentation/docs/using-fluent.md) and [WPF .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100): theme, Mica, native control and API constraints.
- [CleanShot features](https://cleanshot.com/features), [CleanShot API documentation](https://cleanshot.com/docs-api), and [earlier visual study](UI-REFERENCES.md): quick-access actions, file dragging, pin size/opacity and precise positioning. The supplied screenshots guide treatment; their recording/GIF controls do not add recording to this phase.
