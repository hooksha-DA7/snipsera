# Snipsera artwork

The Pinned Pair identity uses a teal capture card, a slate rear card and a coral pin. The large app artwork and the small companion drawings share that identity.

## Source and generated assets

- `src/Framelet/Assets/Brand/master.png` is the transparent source for the sculpted app artwork.
- `Brand/tray.json` and the four matching SVGs define the separately drawn 16, 20, 24 and 32 px tray marks. They remove the tilt, bevel and small corner detail. Larger tray frames use the 32 px geometry.
- `scripts/generate_app_assets.ps1` generates the PNGs and multi-resolution ICO files. Application frames use the sculpted artwork at every size; the notification area has its own companion drawings.
- `Snipsera.ico` is embedded into the EXE and used for window identity. `Snipsera-Tray.ico` is a separate embedded resource for the notification area. Icons do not depend on loose files beside the executable.
- MSIX assets include scale variants and exact target-size images, including transparent light/dark unplated variants. MakePri indexes the scale, target-size and theme qualifiers in a single resources.pri before MakeAppx packages them. The package verifier checks the index hash and exact asset inventory.

Run the generator with PowerShell 7 (`pwsh`) on Windows to recreate the exports. Its matte normalization is specific to this connected card-and-pin silhouette: it removes coverage below 16/255 and isolated matte fragments, keeps the largest connected object, preserves RGB values and makes near-opaque interiors fully opaque. `-NormalizeMaster` also saves that normalized source. A future mark containing detached objects needs its own matte policy.

The runtime selects the tray frame for the taskbar's DPI and refreshes it after Windows display/preference changes. Common 100%, 125%, 150% and 200% display scales use the separately drawn 16, 20, 24 and 32 px frames.

## Verification

`--branding-check` loads the embedded resources without displaying windows. It checks frame selection, transparent corners, retained colours, closed-stream lifetime, shared small application/tray drawings, window setup and GDI handle disposal. Python packaging checks compare ICO blocks with the exact PNGs, verify Store dimensions/theme variants and include the maintained source assets.

These checks do not establish how the Windows notification area, taskbar, Start or ICO frame selection looks on an installed Windows 11 app. Live mixed-DPI, theme and installed-package acceptance remain separate from source/resource verification.

Design references: [Windows app icon construction](https://learn.microsoft.com/en-us/windows/apps/design/iconography/app-icon-construction) and [Windows icon sizes](https://learn.microsoft.com/en-us/windows/win32/menurc/about-icons).
