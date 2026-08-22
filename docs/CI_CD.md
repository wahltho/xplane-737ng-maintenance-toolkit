# CI/CD Preparation

This repository is the public home for the X-Plane 737NG Maintenance Toolkit.
The workflows build, test and package the app. Current public packages are
unsigned. Signed distribution should only be enabled once signing policy and
credentials are settled.

## Active Workflows

### `CI`

File: `.github/workflows/ci.yml`

Runs on push to `main`, pull requests and manual dispatch.

The workflow:

- checks out the repository
- installs .NET 10
- restores the solution
- builds in `Release`
- runs the core test suite

It runs on:

- `ubuntu-latest`
- `macos-latest`
- `windows-latest`

### `VeloPack Packages`

File: `.github/workflows/velopack-packages.yml`

Runs on version tags and can also be started by manual dispatch.

The workflow:

- builds and tests in `Release`
- publishes self-contained app output
- restores the pinned local `vpk` tool
- creates unsigned VeloPack packages
- uses the current app icon assets for packaging
- installs `squashfs-tools` on Linux so VeloPack can create AppImage output
- uploads only the VeloPack output needed by the release job
- optionally creates or updates a GitHub Release from the VeloPack artifacts
- adds release notes and `SHA256SUMS.txt` to the release assets
- removes intermediate Actions artifacts after a successful published release
- retains only the two newest stable GitHub Releases

The release step is controlled by manual workflow inputs:

- `publish_release`: create or update `v<app_version>`
- `prerelease`: mark the GitHub Release as a pre-release
- `draft`: create or update the GitHub Release as a draft

The workflow does not use signing or notarization secrets.

Manual preview builds that do not publish a release retain their VeloPack
artifacts for one day. Published release assets remain attached to the GitHub
Release, while the redundant Actions copies are deleted immediately. The two
newest non-draft, non-prerelease releases are retained as the current stable
release and one rollback release. Older release entries and their binary assets
are removed; their Git tags remain as source-history markers.

The current macOS package intentionally omits an icon argument because VeloPack
expects a `.icns` file there. Final branding should provide platform-native
icon assets before signed public distribution.

## VeloPack Tooling

The VeloPack app package reference and CLI are intentionally pinned together:

- app package: `Velopack` 1.2.0
- local tool: `vpk` 1.2.0 in `dotnet-tools.json`

The workflow follows the VeloPack model of compiling with `dotnet publish` and
then passing the publish output to `vpk pack`.

## Signed Release Work Still Needed

The maintainers can extend the packaging workflow when signed distribution is
ready.

Recommended additions:

- official LevelUp/Monsoon app icon and branding
- protected `release` environment
- release-channel policy
- official app version source
- Windows code signing
- Apple Developer ID signing
- Apple notarization and stapling
- Linux AppImage signing or checksum/signature policy
- `vpk download github` before packing to preserve update history and deltas
- manual approval gate before publishing stable releases

## Secrets To Add Later

Do not add these to a repository unless the maintainers explicitly decide to
publish signed public releases from it.

Likely future secrets:

- `WINDOWS_SIGNING_CERT`
- `WINDOWS_SIGNING_CERT_PASSWORD`
- `APPLE_DEVELOPER_ID_CERT`
- `APPLE_DEVELOPER_ID_CERT_PASSWORD`
- `APPLE_ID`
- `APPLE_TEAM_ID`
- `APPLE_NOTARY_PASSWORD`

Exact secret names may change depending on Colin's signing implementation.

## Repository Publishing Rule

Until signed distribution is explicitly approved:

- build/test CI is allowed
- unsigned packaging is allowed
- unsigned public releases are allowed when the release notes clearly state the
  signing limitations
- signed releases are not allowed
- signing secrets are not allowed
- notarization secrets are not allowed
