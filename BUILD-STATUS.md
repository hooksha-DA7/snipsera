# Development preview 4

Build: 0.6.0-preview.4, 2026-11-01.
The full 125-check suite, capture-lifecycle integration and six off-screen template checks passed; compilation has zero warnings/errors.
Live Chrome scrolling, overlay wheel/hover interaction and capture exclusion remain pending. The published preview passed archive integrity and extracted startup checks; the final staged package will need its own launch check.
Desktop testing resumed on 2026-11-01, then the desktop-control GPU capture connection failed. See [release acceptance](docs/RELEASE-ACCEPTANCE-0.6.0.md) for passed checks and remaining work.
The last locally verified capture/annotation baseline remains 0.5.2.

Extract the full Windows ZIP and quit previous Framelet copies before opening Framelet.exe.
Keep all four runtime files together. Requires .NET 10 Desktop Runtime x64.
See docs/TEMPORARY-HIDE.md, docs/SCROLLING-CAPTURE.md and RELEASES.md for behavior and evidence limits.

The original preview-4 ZIPs retain a stale "preview 3" heading in this document; their Build line and binary metadata identify preview 4. This repository copy corrects the heading without altering the historical archives.
