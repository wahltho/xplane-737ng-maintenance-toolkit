# X-Plane 737NG Maintenance Toolkit

Cross-platform desktop app for Zibo and LevelUp 737NG maintenance tasks. It
installs and maintains VNAV descent table packages and provides conservative
view-maintenance utilities for supported aircraft variants.

This repository is the public development home for the app. The architecture
keeps package content, aircraft patching, and application updates separate.

Current public packages are unsigned. Platform signing and notarization belong
to the release policy for later signed distribution.

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
- It can create and restore dedicated config backups for root-level aircraft
  preferences, camera CSVs, cfg files and toolkit metadata.
- It can install, update, repair and uninstall manifest-owned VNAV Lua hooks and
  payload files after validation and backup.
- It can restore the latest recorded backup generation for the selected
  aircraft variant.
- Before a VNAV write action it tries to refresh `package-manifest.txt` and
  payload files from the package GitHub Release assets. The bundled manifest
  and local/offline package directories are fallback sources.
- It can review Zibo upstream baseline/cumulative package plans, import exact
  matching aircraft update ZIPs into a local cache, download direct ZIP sources
  when available, review cached ZIP contents, then apply cached packages with
  backups, rollback and restore support.
- It distinguishes full aircraft updates, which include a full baseline ZIP,
  from incremental updates, which apply only the latest cumulative patch ZIP for
  the same baseline.

Review remains available for planned changes before write actions. Real write
actions are limited to manifest-owned VNAV content, view-maintenance files and
explicitly applied cached aircraft update packages. Direct ZIP download is attempted from the
feed source URL and from a `.zip` candidate when the feed exposes `.zip.torrent`
links; manual import remains the fallback for sources that do not expose a
direct ZIP stream.

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

## Documentation

- [User Manual](docs/USER_MANUAL.md)
- [Product Specification](SPEC.md)
- [CI/CD Preparation](docs/CI_CD.md)
- [Zibo ACF CG Catalog Builder](docs/ZIBO_ACF_CG_CATALOG.md)
- [LevelUp ACF CG History](docs/LEVELUP_ACF_CG_HISTORY.md)

## Current Scope

- .NET 10.
- Avalonia UI.
- VeloPack SDK startup hook via `VelopackApp.Build().Run()`.
- Manifest-driven package support for LevelUp and Zibo VNAV content.
- Aircraft detection and install-state analysis.
- Real backup-backed View Utility operations.
- VeloPack packaging workflow.
- GitHub Release publishing path for VeloPack app artifacts.
- Real VNAV Lua patch writes for manifest-owned hooks and payloads.
- GitHub Release manifest/payload loading with local/offline fallback.

## VeloPack Integration

The app references the `Velopack` NuGet package and calls
`VelopackApp.Build().Run()` at the start of `Program.Main`, before Avalonia is
initialized. That is the required application-side hook for install/update
lifecycle handling.

Packaging is available through the manual VeloPack GitHub Actions workflow.
The workflow produces Windows, macOS and Linux VeloPack artifacts for the
selected release channel.

## License

This app is licensed under the MIT License. See [LICENSE](LICENSE).
