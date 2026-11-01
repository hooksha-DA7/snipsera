# Framelet project workflow

- Personal public repository: https://github.com/hooksha-DA7/framelet. Keep Framelet isolated from the parent workspace and other projects.
- Use per-command Git identity overrides for this personal repository: `user.name=hooksha-DA7`, `user.email=72675037+hooksha-DA7@users.noreply.github.com`. Leave the user's global Git identity unchanged.
- The user requested a chronological version/download log and continued version publishing. For future delivered versions, maintain `releases/catalog.json`, curated notes, `RELEASES.md`, source tags and GitHub Releases using `docs/RELEASING.md`. Use a fresh version and preserve published assets.
- Preserve the on-image controls and same-window Pin/Unpin. Routine mechanical checking must not become an unsolicited redesign.
- Desktop testing is currently paused at the user's explicit request. A later explicit readiness instruction can revoke that pause. Until then, use only no-window/off-screen checks and do not manipulate the running app or test windows.
- `0.5.2` is the last locally verified capture/annotation baseline. `0.6.0-preview.4` is the latest feature preview. Do not promote a preview based only on synthetic tests; record native acceptance limits accurately.
- Publish only reviewed source and runtime packages plus checksums. Keep `.tools`, `design`, private verification records, agent logs, capture history, test captures, credentials and the original reference installers/media out of Git and releases.
- Historical commits were imported from original source ZIPs on 2026-11-01; do not backdate or claim they are the original development commits. Build dates and original archive SHA256 values are recorded separately.
- No background sync or automatic native testing is configured. Follow the documented release steps during authorized Framelet work.
