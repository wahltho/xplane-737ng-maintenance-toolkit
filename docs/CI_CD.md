# CI/CD Preparation

This repository is currently a temporary public development home under a
personal account. The workflows are prepared so the X-Plane 737NG Maintenance
Toolkit prototype can build and package early, but official distribution should
wait until final repository ownership and signing policy are settled.

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

### `VeloPack Preview Packages`

File: `.github/workflows/velopack-preview.yml`

Runs only by manual dispatch.

The workflow:

- builds and tests in `Release`
- publishes self-contained app output
- restores the pinned local `vpk` tool
- creates unsigned VeloPack preview packages
- uses a temporary placeholder icon for packaging
- installs `squashfs-tools` on Linux so VeloPack can create AppImage output
- uploads GitHub Actions artifacts

It does not create GitHub Releases and does not use signing or notarization
secrets.

The current macOS preview package intentionally omits an icon argument because
VeloPack expects a `.icns` file there. Linux/AppImage uses the temporary PNG
placeholder. Final branding should replace both placeholder assets before
public distribution.

## VeloPack Tooling

The VeloPack app package reference and CLI are intentionally pinned together:

- app package: `Velopack` 1.2.0
- local tool: `vpk` 1.2.0 in `dotnet-tools.json`

The workflow follows the VeloPack model of compiling with `dotnet publish` and
then passing the publish output to `vpk pack`.

## Official Release Work Still Needed

The maintainers can extend the preview workflow after repository ownership is
settled.

Recommended additions:

- official LevelUp/Monsoon app icon and branding
- protected `release` environment
- beta/stable channel policy
- official app version source
- Windows code signing
- Apple Developer ID signing
- Apple notarization and stapling
- Linux AppImage signing or checksum/signature policy
- `vpk download github` before packing to preserve update history and deltas
- `vpk upload github` to create GitHub Releases
- release notes generation
- manual approval gate before publishing stable releases

## Secrets To Add Later

Do not add these to the temporary personal repository unless LevelUp explicitly
decides to publish from it.

Likely future secrets:

- `WINDOWS_SIGNING_CERT`
- `WINDOWS_SIGNING_CERT_PASSWORD`
- `APPLE_DEVELOPER_ID_CERT`
- `APPLE_DEVELOPER_ID_CERT_PASSWORD`
- `APPLE_ID`
- `APPLE_TEAM_ID`
- `APPLE_NOTARY_PASSWORD`

Exact secret names may change depending on Colin's signing implementation.

## Temporary Repository Rule

While this repository remains under a personal account:

- build/test CI is allowed
- unsigned preview packaging is allowed
- official releases are not allowed
- signing secrets are not allowed
- notarization secrets are not allowed
- production VeloPack channels are not allowed
