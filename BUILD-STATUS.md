# Framelet 0.6.0

Build: 0.6.0, 2026-11-02.

The 125-check suite, capture-lifecycle integration and six off-screen template checks passed. Compilation has zero warnings and errors. Native integration covers retained windows and geometry, dirty editors, queued captures and hiding floating windows at the capture callback.

The tester confirmed Chrome manual and Auto scrolling, pinning two images, wheel-to-hide and hover-to-restore with retained sizes/positions, and taking another capture while the group stays hidden. These are hands-on confirmations. The test observer recorded no corresponding captures or events, and desktop screenshot control failed with GPU error 0x887A0005; independent live pixel continuity and exclusion are not certified.

Release staging checks archive integrity, manifests, source/build parity, documentation links and startup from a fresh extraction. See [acceptance results](docs/RELEASE-ACCEPTANCE-0.6.0.md) for the evidence and remaining coverage limits.

Extract the full Windows ZIP and quit previous Framelet copies before opening Framelet.exe. Keep all four runtime files together. Requires .NET 10 Desktop Runtime x64.
