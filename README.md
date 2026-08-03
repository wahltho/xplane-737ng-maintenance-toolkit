# X-Plane 737NG Maintenance Toolkit

Cross-platform desktop app for Zibo and LevelUp 737NG maintenance tasks. It
manages aircraft and VNAV packages, explicit optional patches, X-Plane-wide
tools and conservative view maintenance for supported aircraft variants.

This repository is the public development home for the app. The architecture
keeps package content, aircraft patching, and application updates separate.

Release version: **0.4.0**

- [Download the latest stable release](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest)
- [Read the user manual](docs/USER_MANUAL.md)
- [Use the prepared X-Plane.org listing](docs/XPLANE_ORG_LISTING.md)

## Compatibility

- X-Plane 12. X-Plane 11 is not supported.
- Zibo 737-800X 2K and 4K variants.
- LevelUp 737-600, 737-700, 737-800, 737-900 and 737-900ER.
- Windows x64.
- macOS arm64 (Apple silicon).
- Linux x64.

Aircraft detection is structural. A folder name alone is not accepted as proof
of a supported installation.

## Download And Installation

Choose one normal-use artifact from the
[latest GitHub Release](https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest):

| Platform | Recommended artifact | Portable alternative |
| --- | --- | --- |
| Windows x64 | `XPlane737NGMaintenanceToolkit-stable-win-x64-Setup.exe` | `XPlane737NGMaintenanceToolkit-stable-win-x64-Portable.zip` |
| macOS arm64 | `XPlane737NGMaintenanceToolkit-stable-osx-arm64-Setup.pkg` | `XPlane737NGMaintenanceToolkit-stable-osx-arm64-Portable.zip` |
| Linux x64 | `XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage` | None |

On Windows or macOS, run the setup package or extract the portable ZIP and start
the app from the extracted folder. On Linux, make the AppImage executable and
run it:

```bash
chmod +x XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage
./XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage
```

Current public packages are unsigned. Platform signing and notarization belong
to the release policy for later signed distribution.

- Windows may display a SmartScreen warning.
- macOS may block the unsigned, non-notarized build.
- Linux AppImage builds are not separately signed.

Verify downloaded artifacts against `SHA256SUMS.txt` on the release page. If a
verified macOS download is blocked, follow Apple's documented
[Privacy & Security "Open Anyway" process](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac).

Download future app releases manually from GitHub. VeloPack currently provides
the application package and lifecycle integration; version 0.4.0 does not yet
check for or download new Toolkit versions automatically. Aircraft and VNAV
content updates are separate and can be checked from inside the app.

## First Use

1. Close X-Plane.
2. Start the Toolkit and use `Auto-detect`, or browse to an X-Plane, `Aircraft`,
   Zibo or LevelUp folder.
3. Select the required detected product when more than one is available.
4. Click `Scan selected folder`.
5. Use the main `Update` action and review each confirmation before files are
   changed.

The `Advanced` tab contains detailed review, recovery and diagnostic controls.
The `Settings` tab contains backup, cache, offline-package and diagnostics
directories.

## Disclaimer

This toolkit is provided as-is, without warranty of any kind. It is an
independent community tool and is not an official X-Plane, Zibo or LevelUp
product unless explicitly stated otherwise.

The toolkit can modify aircraft installation files after validation and backup.
Users should keep their own backups and use the tool at their own risk.

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
- It supports explicit optional schema-v2 patch packages through the same
  transaction engine. Optional packages are never folded into the automatic
  aircraft/VNAV update flow and require a separate user confirmation.
- It ships a versioned trusted package catalog. The Start page filters managed
  content and optional patches by the selected Zibo or LevelUp product. Trusted
  optional entries can resolve their latest stable GitHub Release directly;
  manual package-folder selection remains an Advanced fallback.
- It offers optional X-Plane-wide tools for compatible detected products. YAL
  is available for Zibo and LevelUp at `Resources/plugins/YAL`; YAL HoppieHelper
  is also available for both products at `Resources/plugins/YAL_HoppieHelper`,
  matching its published release manifest.
- Tool Stable and Beta channels are selected independently per tool and from app
  and aircraft updates. Release manifests, GitHub asset digests, archives and every
  payload SHA-256 must agree before install, update or repair.
- Tool updates preserve manifest-declared configuration and output paths plus
  local unowned files, create generation backups and provide guarded restore.
- GitHub optional-package archives are selected by an explicit asset pattern,
  checked against GitHub's published size and SHA-256 digest, and safely
  reduced to the declared manifest and payload files in the local cache.
- Declarative optional packages may use only built-in exact-text, OBJ8,
  bounded sparse-byte and PNG-region operations. Downloaded packages cannot
  execute scripts inside the toolkit.
- It can restore the latest recorded backup generation for the selected
  aircraft variant.
- Before a VNAV write action it tries to refresh `package-manifest.txt` and
  payload files from the package GitHub Release assets. The bundled manifest
  and local/offline package directories are fallback sources.
- It can review Zibo upstream baseline/cumulative package plans, import exact
  matching aircraft update ZIPs into a local cache, download direct ZIP sources
  when available, review cached ZIP contents in the background, then confirm
  and apply cached packages with backups, rollback and restore support.
- It can read the authorized public LevelUp release index, select either the
  exact full aircraft package or the matching cumulative patch, verify the
  published manifest/archive hashes, and use the same review and transactional
  update path. Manual LevelUp manifest/archive import remains available as an
  offline fallback.
- Package import, download and pre-write validation are cancellable. Once the
  confirmed aircraft write transaction starts, it completes or rolls back.
- The main `Update` action checks aircraft and VNAV package state in one user
  flow. A validated offline LevelUp package remains active when `Update` is
  clicked and is not replaced by an unavailable online release check. The flow
  can continue with VNAV-only maintenance when no aircraft update is available
  or required.
- The main `Update` action is hidden after the app has established that neither
  an aircraft-package action nor a safe VNAV action is available.
- Aircraft and VNAV writes remain separate confirmed transactions with
  independent backup, rollback and restore state.
- It distinguishes full aircraft updates, which include a full baseline ZIP,
  from incremental updates, which apply only the latest cumulative patch ZIP for
  the same baseline.
- The UI follows the operating system's light or dark appearance.

Review remains available for planned changes before write actions. Real write
actions are limited to manifest-owned VNAV content, explicitly selected
declarative patch packages, view-maintenance files and explicitly applied
cached aircraft update packages. For Zibo, direct ZIP
download is attempted from the feed source URL and from a `.zip` candidate when
the feed exposes `.zip.torrent` links. LevelUp uses explicit GitHub Release
assets described by its public release index. Manual import remains the fallback
when a source is unavailable.

## Development Build

```bash
dotnet build LevelUp.NavTableUpdater.slnx
```

## Development Test

```bash
dotnet test tests/LevelUp.NavTableUpdater.Core.Tests/LevelUp.NavTableUpdater.Core.Tests.csproj
```

## Development Run

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
- Generic transactional content-patch engine with managed VNAV and explicit
  opt-in lifecycle policies.
- Product-scoped trusted content catalog with secure GitHub Release package
  provisioning for optional declarative patches.
- Product-gated, X-Plane-wide tool packages with separate Stable/Beta release
  channels and transactional install/update/repair/restore.
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

The current public app does not yet create an `UpdateManager` or perform an
automatic app release check. Users install newer Toolkit versions manually.
This is separate from the aircraft-package and VNAV-content update sources used
inside the app.

Packaging is available through the manual VeloPack GitHub Actions workflow.
The workflow produces Windows, macOS and Linux VeloPack artifacts for the
selected release channel.

## License

This app is licensed under the MIT License. See [LICENSE](LICENSE).
