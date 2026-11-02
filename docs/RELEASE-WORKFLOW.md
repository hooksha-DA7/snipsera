# Building and signing a release

The **Windows release candidates** workflow builds on GitHub-hosted Windows runners. It can be run manually from a branch for an unsigned candidate. Pushing a version tag also builds candidates. Signing is off by default and requires a clean checkout of the tag matching the project's informational version.

The workflow checks the source version, prepares hash-pinned tools, builds EXE/MSIX/source/portable packages, runs focused checks, verifies package contents and tests installation. Its increasing-version installer fixture is a test build from the same source, not a published version. A hosted Windows Server runner is not a substitute for final Windows 11 testing.

## SignPath setup

After Foundation acceptance, configure the approved project using the artifact configurations in `packaging/signpath`. The application configuration signs only `Snipsera.exe` and `Snipsera.dll`. The installer configuration signs the outer installer. Upstream binaries and the generated Inno uninstaller are not re-signed by these configurations.

Configure these in the protected **release-signing** GitHub environment:

| Setting | Kind |
| --- | --- |
| SIGNPATH_API_TOKEN | Secret; enter it directly in GitHub, never in source or a release |
| SIGNPATH_ORGANIZATION_ID | Variable |
| SIGNPATH_PROJECT_SLUG | Variable |
| SIGNPATH_POLICY_SLUG | Variable |
| SIGNPATH_APP_CONFIGURATION | Variable; actual configured application artifact slug |
| SIGNPATH_INSTALLER_CONFIGURATION | Variable; actual configured installer artifact slug |
| SIGNPATH_EXPECTED_PUBLISHER | Variable; exact approved certificate subject |

Restrict the environment to release tags and require approval from hooksha-DA7. Keep multi-factor authentication enabled on the maintainer's source and signing accounts. Install the SignPath GitHub App only with the repository access approved during setup. No signing token is present merely because this workflow exists.

## Run the signed build

1. Finish native acceptance, update the version and review the source.
2. Tag that source as `v<InformationalVersion>` without replacing an existing tag.
3. Run the workflow **on that tag**, enabling the `sign` input. Approve the protected signing environment.
4. The first request signs the application files. A second packaging pass includes those verified files in the portable ZIP, installer and Store MSIX. A second request signs the installer.
5. Verify the returned signatures, timestamps and publisher. The final step refreshes checksums after signing, checks package/source parity and tests installation again.

The MSIX remains an unsigned **Store submission** package. It is not a public installable MSIX and must not be offered as one. Keep it for Partner Center upload and Microsoft's signing/certification process.

## Publication

The workflow uploads reviewable build artifacts but does not create a public GitHub release. Publication uses the personal maintainer's authenticated CLI, preserving personal release attribution. Extend the reviewed catalog entry with `product: Snipsera`, the certificate subject, EXE/source/optional portable assets and `store_submission` metadata. The existing publisher accepts `--installer`, `--source`, optional `--windows`, and `--store-submission`. It validates the Store package but does not upload that unsigned submission as a public GitHub download.

Publish only the reviewed signed output. Keep a Store link separate until the app is actually available there. The public stable and preview pointers stay on their previous releases until a new release is approved.

References: [GitHub workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [SignPath GitHub integration](https://docs.signpath.io/trusted-build-systems/github), [Foundation conditions](https://signpath.org/terms.html).
