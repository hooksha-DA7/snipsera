# Publishing a Framelet version

Repository: [hooksha-DA7/framelet](https://github.com/hooksha-DA7/framelet). The user requested version history with download links and continued updates as new versions are delivered. This process runs during project work; no background service is installed.

## Preserve the release contract

Use a fresh version for every published artifact. Preserve the original local archives and already published assets. Keep experimental builds as prereleases until their required native acceptance is complete. `0.6.0` is the current baseline, with automated/native integration and tester-confirmed workflows; independent live pixel comparison remains unverified. Future previews must state their remaining checks. A checksum proves identity, not runtime correctness.

The first import contains eight historical numbered builds and three complete scrolling/overlay previews. The external-toolbar `0.5.1` build is marked superseded. Incomplete private staging attempts were excluded. Historical Git commits were reconstructed from archived source ZIPs in chronological order using the actual import date. Release notes retain the original build dates; GitHub publication dates are not backdated.

Windows/source ZIPs are attached to each release with `SHA256SUMS.txt`. GitHub's automatic source downloads are the tagged repository tree and are distinct from the attached ZIPs.

## Add the next version

1. Implement and verify the change. Update the project version/informational version, README, CHANGELOG and relevant notes. Follow the current desktop-testing instruction; safe checks are:

   ```powershell
   dotnet Framelet.Checks.dll --backend-check <fresh-output-folder>
   dotnet Framelet.Checks.dll --overlay-style-check <fresh-render-folder>
   ```

   The ordinary full suite opens windows and must wait while desktop testing is paused.

2. Build/package fresh Windows and source ZIPs. Check ZIP paths/CRC, allowlisted contents, manifests, source/build parity and documentation links. Inspect the exact files for public disclosure. Never include reference media/installers, user screenshots/history, credentials, private logs or agent transcripts. Do not relabel an old ZIP as a new binary build.
3. Add a curated entry to `releases/catalog.json` with `kind`, `intro`, categorized `changes` (Added/Improved/Fixed) and any user-facing `notice`. Record the real build date, runtime/source asset names, byte sizes and SHA256, status, verification limits. Describe behavior users will notice; avoid agent attribution, test-count reports and private workflow history in public notes. Mark previews `prerelease: true`, `latest: false`. Update the verified baseline only after its acceptance is complete.
4. Commit the source with the git-commit workflow. Tag the exact source snapshot as `v<VERSION>` and push that commit/tag to the personal repository. The release script uses `--verify-tag` and does not create or push source for you.
5. Plan the upload (no remote mutation):

   ```powershell
   python scripts/publish_release.py --version <VERSION> --windows <Windows-ZIP> --source <Source-ZIP>
   ```

6. With GitHub CLI authenticated to the repository's personal owner, publish:

   ```powershell
   python scripts/publish_release.py --version <VERSION> --windows <Windows-ZIP> --source <Source-ZIP> --publish
   ```

   The script checks archive hashes/manifests, creates a draft, uploads both ZIPs/checksums, verifies the uploaded digests, publishes with the curated prerelease/latest state, then downloads each asset anonymously to verify public access and SHA256. It never overwrites an existing asset. An existing public release is only verified. If a matching draft was interrupted, inspect it before using `--resume`.

7. Run `python scripts/update_release_log.py`; it generates `RELEASES.md`, `CHANGELOG.md`, individual `releases/notes` and the marked README download block from the catalog pointers. Commit/push the updated catalog, curated notes, log and README. Verify the public repository, tagged source, release states and download links. Report the actual URLs and any pending native checks.

The publisher requires Python 3.10+ and GitHub CLI. It uses GitHub CLI's existing authentication/environment; it does not print, collect or persist credentials. The application itself has no publishing dependency or network service.

The publisher's offline regression checks can run without GitHub access:

```powershell
python -m unittest discover -s tests -p test_release_publishing.py
```

They cover published and missing release lookups, draft lookup through owner listings, ambiguous draft detection, complete archive links, README preview-pointer updates and bounded retry of transient reads without blindly repeating writes. Draft listing is used because the published-release tag endpoint may return 404 for an unpublished draft.

## Check an existing published version

```powershell
python scripts/publish_release.py --version <VERSION> --verify-only
```

References: [GitHub repository creation](https://cli.github.com/manual/gh_repo_create), [release creation/assets](https://cli.github.com/manual/gh_release_create), [GitHub CLI authentication environment](https://cli.github.com/manual/gh_help_environment).

## Repository ownership and public copy

The personal owner is **hooksha-DA7** (user ID 72675037). Verify `gh api user --jq .login` before publishing. Do not change the user's global Git identity; use per-command overrides `git -c user.name=hooksha-DA7 -c user.email=72675037+hooksha-DA7@users.noreply.github.com ...` for personal commits.

The README is the app's front door: explain the tool, installation and the basic workflow. Keep detailed instructions in USER-GUIDE.md and development/verification details in the other docs. For every release, write the actual product changes under Added, Improved or Fixed and identify previews or known issues. Edit the catalog first, then regenerate the public files. Preserve existing binaries, tags and asset checksums when correcting release descriptions.

## Documentation packaging correction: 2026-11-01

The 0.5.2, 0.6.0-preview.2, 0.6.0-preview.3 and 0.6.0-preview.4 packages have refreshed documentation and source comments. Executables, libraries and debug symbols are unchanged. Their archive checksums have been updated; the original local archives are retained privately for rollback. Imported source history and tags use the personal owner identity. Commit IDs changed during this correction; existing version download URLs are retained. Future releases should use a fresh version and preserve published assets.
