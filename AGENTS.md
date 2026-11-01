# Framelet project workflow

- Personal public repository: https://github.com/hooksha-DA7/framelet. Keep Framelet isolated from the parent workspace and other projects.
- Use per-command Git identity overrides for this personal repository: `user.name=hooksha-DA7`, `user.email=72675037+hooksha-DA7@users.noreply.github.com`. Leave the user's global Git identity unchanged.
- The user requested a chronological version/download log and continued version publishing. For future delivered versions, maintain `releases/catalog.json`, curated notes, `RELEASES.md`, source tags and GitHub Releases using `docs/RELEASING.md`. Use a fresh version and preserve published assets.
- Preserve the on-image controls and same-window Pin/Unpin. Routine mechanical checking must not become an unsolicited redesign.
- The user authorized live desktop testing for 0.6.0 acceptance on 2026-11-01. Use isolated test history and synthetic content, preserve existing user windows/data, and honor any later explicit pause. A desktop-control failure does not count as application acceptance.
- `0.6.0` is the current release baseline, combining automated/native integration checks and tester-confirmed Chrome and hide/restore workflows. Independent live pixel comparison is unverified; preserve that distinction in future reports. Do not promote future previews based only on synthetic tests.
- Publish only reviewed source and runtime packages plus checksums. Keep `.tools`, `design`, private verification records, agent logs, capture history, test captures, credentials and the original reference installers/media out of Git and releases.
- Historical commits were imported from original source ZIPs on 2026-11-01; do not backdate or claim they are the original development commits. Build dates and original archive SHA256 values are recorded separately.
- No background sync or automatic native testing is configured. Follow the documented release steps during authorized Framelet work.

- Publish only to the personal repository owned by `hooksha-DA7`. Check the authenticated login before publication.
- Keep README focused on the app, downloading it and using it. Release notes should describe concrete Added, Improved and Fixed behavior in plain language; keep agent/workflow details and test receipts in developer documentation.

- Public files, comments and release descriptions should use the project and owner identity only. Keep internal tooling, work assignments and development conversations private. Use the personal owner for commit, tag, release and upload attribution.
