# X-Plane 737NG Maintenance Toolkit

Beta prototype for a VeloPack-based X-Plane 737NG maintenance tool covering
VNAV table package analysis and conservative view-maintenance utilities for
Zibo and LevelUp aircraft.

This repository is a temporary public development home for the prototype. It is
intended to transfer to LevelUp/Monsoon ownership once the final GitHub
organization/repository decision is settled.

No official releases, installer signing credentials or distribution secrets
should live here while the repository remains under a personal account.

Current capabilities:

- It can detect likely Zibo and LevelUp aircraft folders.
- It can scan a manually selected folder.
- It explicitly detects LevelUp port/no-Lua installs as not applicable to the
  Lua patch package.
- It parses the current v1 package manifest format for LevelUp and Zibo VNAV
  table packages.
- It classifies hook state in `B738.a_fms.lua`.
- It shows component status, findings and planned changes.
- It can apply Quick View 0 to the ACF default view after creating a backup.
- It can adapt X-Plane quick views after an ACF CG change after creating a
  backup.
- It can optionally adapt a matching `X-Camera_<acf-stem>.csv` file when one is
  present.
- It can install, update, repair and uninstall manifest-owned VNAV Lua hooks and
  payload files after validation and backup.
- It can restore the latest recorded backup generation for the selected
  aircraft variant.
- Before a VNAV write action it tries to refresh `package-manifest.txt` and
  payload files from the package GitHub Release assets. The embedded preview
  manifest and local/offline package directories are fallback sources.

Dry-run remains available as a preview action. Real write actions are limited
to manifest-owned VNAV content and view-maintenance files.

## Build

```bash
dotnet build LevelUp.NavTableUpdater.slnx
```

## Test

```bash
dotnet test tests/LevelUp.NavTableUpdater.Core.Tests/LevelUp.NavTableUpdater.Core.Tests.csproj
```

## Run

```bash
dotnet run --project src/LevelUp.NavTableUpdater.App/LevelUp.NavTableUpdater.App.csproj
```

The app requires a usable desktop GUI session.

For offline or development package testing, set `XPLANE_737NG_PACKAGE_DIR` to a
folder containing `package-manifest.txt` and all manifest payload files. GitHub
Release assets remain the preferred package source for normal use.

## Current Scope

- .NET 10.
- Avalonia UI.
- VeloPack SDK startup hook via `VelopackApp.Build().Run()`.
- Manifest-driven package preview for LevelUp and Zibo VNAV content.
- Aircraft detection and install-state analysis.
- Real backup-backed View Utility operations.
- Preview VeloPack packaging workflow.
- No GitHub Release publishing yet.
- Real VNAV Lua patch writes for manifest-owned hooks and payloads.
- GitHub Release manifest/payload loading with local/offline fallback.

## VeloPack Integration

The app references the `Velopack` NuGet package and calls
`VelopackApp.Build().Run()` at the start of `Program.Main`, before Avalonia is
initialized. That is the required application-side hook for install/update
lifecycle handling.

Preview packaging is available through the manual VeloPack GitHub Actions
workflow. Official beta/stable publishing still needs final repository
ownership, signing, notarization and release-channel policy.

## License

This updater prototype is licensed under the MIT License. See [LICENSE](LICENSE).
