# Publishing a Snipsera version

Repository: [hooksha-DA7/snipsera](https://github.com/hooksha-DA7/snipsera). The user requested version history with download links and continued updates as new versions are delivered. Release work runs during project work or an explicitly scheduled approval follow-up; Snipsera itself does not run a publishing service.

## Preserve the release contract

Use a fresh version for every published artifact. Preserve the original local archives and already published assets. Publish new Snipsera builds as GitHub prereleases. Promote the same reviewed build to an official GitHub release and Latest only after Microsoft approves the matching Store update. Snipsera `1.0.6` (immutable build/tag `v1.0.6-preview.3`) is the current distribution baseline after matching Store approval. Automatic Store publication is in progress as verified on 6 November 2026. Framelet `0.6.0` remains the earlier baseline with automated/native integration and tester-confirmed workflows; independent live pixel comparison remains unverified. Future previews must state their remaining checks. A checksum proves identity, not runtime correctness.

The first import contains eight historical numbered builds and three complete scrolling/overlay previews. The external-toolbar `0.5.1` build is marked superseded. Incomplete private staging attempts were excluded. Historical Git commits were reconstructed from archived source ZIPs in chronological order using the actual import date. Release notes retain the original build dates; GitHub publication dates are not backdated.

Historical releases attach Windows/source ZIPs with `SHA256SUMS.txt`. The distribution decision from 2026-11-02 requires both an EXE installer and an MSIX for future delivered versions, plus reviewed source and checksums. The portable ZIP may remain available. SignPath Foundation is the selected route for installer signing; Microsoft Store is the selected MSIX distribution/signing route. See [distribution setup](DISTRIBUTION.md) for onboarding and verification requirements. GitHub's automatic source downloads are the tagged repository tree and are distinct from the attached source ZIPs.

`publish.ps1` and the release publisher support EXE, portable/source ZIPs and a separately tracked Store MSIX. The unsigned Store submission is validated but is not a public GitHub asset. Preserve the historical ZIP verification path. Signed releases require the actual certificate subject. Unsigned candidates use the guarded `unsigned-preview` path. An official unsigned installer uses `signing_status: "unsigned"` and requires the matching Store approval record described below. Both require visible unsigned notices; Store approval never implies an EXE signature.

Snipsera's own code is MIT-licensed. Include the root `LICENSE`, `THIRD-PARTY.md` and component licence files in source and runtime packages, including the EXE/MSIX payloads. Preserve third-party terms and previously published artifacts.

## Add the next version

1. Implement and verify the change. Update the project version/informational version, README, CHANGELOG and relevant notes. Follow the current desktop-testing instruction; safe checks are:

   ```powershell
   dotnet Framelet.Checks.dll --backend-check <fresh-output-folder>
   dotnet Framelet.Checks.dll --overlay-style-check <fresh-render-folder>
   ```

   The ordinary full suite opens windows and must wait while desktop testing is paused.

2. Build/package the EXE installer and MSIX for the same version, plus source and any optional portable ZIP. Check allowlisted contents, source/build parity, documentation links and final artifact hashes. Check ZIP paths/CRC/manifests where applicable; validate the MSIX manifest/identity/version/dependencies and test installer install/upgrade/uninstall without deleting user history. Inspect signatures on the exact final artifacts. Store submission packages may be unsigned before certification and must not be advertised as directly installable. Never include reference media/installers, user screenshots/history, credentials, private logs or agent transcripts. Do not relabel an old package as a new binary build.
3. Add a curated entry to `releases/catalog.json` with `kind`, `intro`, categorized `changes` (Added/Improved/Fixed) and any user-facing `notice`. Record the real build date, runtime/source asset names, byte sizes and SHA256, status, verification limits. Describe behavior users will notice; avoid agent attribution, test-count reports and private workflow history in public notes. Mark previews `prerelease: true`, `latest: false`. Promote the distribution baseline only after the matching Store update has passed certification; preserve all remaining runtime-test limits.
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

## Promote an approved build

The release policy chosen on 4 November 2026 is **GitHub preview first, official release after matching Store approval**. Package validation, an older live Store version, or submission for certification do not satisfy this gate. Independent native acceptance and installer signing remain separate facts.

1. Verify the current submission in Partner Center. Confirm its approved status, product `9NFL9VHL8GKV`, submission ID, package version, and uploaded package/source mapping. Keep a private screenshot or receipt. Do not infer approval from the product's existing live listing.
2. Record `store_approval` on the matching catalog entry, with `status: "approved"`, `product_id`, `submission_id`, `package_version`, `package_sha256`, `source_commit`, `verified_date` and the source URL `https://partner.microsoft.com/en-us/dashboard/products/9NFL9VHL8GKV/overview`. The package hash must equal that entry's `store_submission.sha256`; the source commit and numeric MSIX version must match too. `validate_store_approval` checks this record, but the operator must establish the live fact first.
3. Preserve the existing version identifier, tag, source commit, asset URLs, bytes and hashes. Set the entry's `prerelease` to false, `latest` to true, `status` to `release`, and `kind` to `Release`. A `display_version` such as `1.0.2` may label an older preview-suffixed build without renaming it. Use numeric intended release versions for future candidates and let GitHub's prerelease flag represent their review status.
4. If the EXE is still unsigned, change `signing_status` to `unsigned` and retain a clear unsigned-installer notice; do not add a certificate claim. Update obsolete submission/preview language and include the actual Store link, preserving any uncompleted verification limits. Signed releases still require their verified certificate subject. Unsigned MSIX files remain submission-only.
5. Set `verified_baseline` to the approved tag, clear `latest_preview` only if it points to that build, and clear old `latest` flags. A newer official release must never be replaced by an older one. Regenerate the public files and update README version/status language. Run the Python release checks and plan the exact existing artifacts through `publish_release.py` before the remote change.
6. Verify the personal GitHub login and exact source tag. Use `gh release edit <existing-tag> --prerelease=false --latest=true --title <official-title> --notes-file <generated-note-file> --repo hooksha-DA7/snipsera`. This changes release metadata only; do not upload replacement assets or retag the source. Inspect state before resuming an uncertain write.
7. Commit/push the catalog, generated notes/log and README using the personal identity. Run `publish_release.py --version <original-build-version> --verify-only`; verify GitHub Latest, owner/uploader attribution, source mapping, download hashes and the Store URL. Record completion and stop the approval follow-up.
