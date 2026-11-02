# Code signing

SignPath Foundation is the selected route for signing Snipsera's Windows installer and its own application binaries. The application was submitted on November 2, 2026; acceptance and certificate availability are pending. Current unsigned packages are not signed or endorsed by SignPath Foundation.

Microsoft Store uses a separate package identity and signs accepted MSIX packages through its certification process. A Store signature does not sign the EXE installer downloaded from GitHub.

## Maintainer and approvals

The personal project maintainer is [hooksha-DA7](https://github.com/hooksha-DA7). The maintainer owns source changes, reviews outside contributions and approves signing requests and releases. Signing access must use multi-factor authentication. Release signing must be approved by the maintainer; it must never run on a pull request or an unreviewed branch.

## Build and signing policy

- Build from the matching version tag on GitHub-hosted Windows runners. Record the exact source commit in the package receipt.
- Pin build actions and downloaded tools. Verify dependency archive and component hashes before use.
- Sign only Snipsera's own application binaries and installer. Bundled upstream binaries keep their original authorship and licences and must not be re-signed as Snipsera's code.
- Verify signatures and the configured certificate subject after signing. Compute final package checksums after signatures have been added.
- Keep signing credentials in the protected signing environment. Source archives, logs, runtime packages and installer payloads must not contain credentials.
- Publish under the personal maintainer account after installation and release checks. Preserve all previously published assets.

The initial SignPath configuration examples are in `packaging/signpath`. Their slugs must be configured in the approved SignPath project; the files alone do not create an account or certificate. The Windows workflow defaults to an unsigned candidate build and does not publish a GitHub release automatically.

The Inno-generated uninstaller is outside the initial signing scope. Do not describe every installed executable as signed. Installation and uninstall remain per user and preserve local capture data.

References: [Foundation conditions](https://signpath.org/terms.html), [trusted GitHub builds](https://docs.signpath.io/trusted-build-systems/github), [artifact configurations](https://docs.signpath.io/artifact-configuration/examples).
