# X-Plane.org Listing Copy

For direct pasting into the X-Plane.org rich-text editor, open
`XPLANE_ORG_LISTING_COPY.html` in a browser and copy the rendered content. The
editor does not interpret raw Markdown headings or lists.

## Title

X-Plane 737NG Maintenance Toolkit

## Current Version

0.11.0

## Short Description

Cross-platform maintenance utility for supported Zibo and LevelUp 737NG aircraft in X-Plane 12, providing aircraft and VNAV updates, transactional backups, CG/view correction, optional patches, tools and resources.

## Full Description

The X-Plane 737NG Maintenance Toolkit is an independent community application for selected maintenance tasks affecting the Zibo 737-800X and LevelUp 737NG Series.

The normal workflow is product-neutral: select or auto-detect an aircraft installation, scan it, review the detected product and use the relevant maintenance action. Before writing files, the Toolkit validates package metadata, hashes and target paths, creates backups, preserves protected local configuration files and rolls back failed transactions where possible.

Current functions include:

- structural detection of supported Zibo and LevelUp installations
- support for multiple X-Plane installations and manual folder selection
- Zibo baseline/cumulative aircraft update planning with automatic direct or
  official BitTorrent package download
- LevelUp full/cumulative updates from its authorized public GitHub release index
- complete fresh installation of Zibo or LevelUp into an unused X-Plane 12
  Aircraft subfolder, using a validated staging image before activation
- VNAV descent table install, update, repair, restore and uninstall
- separate backup and restore state for aircraft, VNAV and tool transactions
- Quick View correction after an aircraft CG change
- optional correction of a matching X-Camera file
- use of Quick View 0 as the aircraft default viewpoint
- CG-corrected Quick View and Default Viewpoint transfer across all variants in
  one detected LevelUp installation
- configuration backup and restore
- cancellable download and review before a write transaction begins
- detailed Advanced log and diagnostic export for support requests
- automatic operating-system light and dark appearance
- optional installation, update, repair and restore of supported X-Plane tools
- verified file-wise installation, repair and restore of the optional 737NG
  Realbench Logger while preserving unrelated DataRefMonitor profiles and logs
- stable and beta release channels for supported optional tools
- aircraft-scoped Optimized XLua install, update, repair and restore while
  preserving aircraft-owned Lua scripts
- explicit optional LevelUp FANS CDU patch management
- verified LevelUp Paintkit download and extraction into a user-selected folder
- background Toolkit application update checks with cancellable verified
  download and confirmed restart through VeloPack

Version 0.11.0 currently offers:

- Optimized XLua 1.3.7r3
- Yet Another Linda (YAL)
- YAL HoppieHelper
- 737NG Realbench Logger 0.1.3
- LevelUp FANS CDU patch
- LevelUp 737NG Paintkit 1.1.0

Optimized XLua is managed separately for each selected Zibo or LevelUp aircraft and preserves the complete aircraft-owned `plugins/xlua/scripts` tree. YAL, YAL HoppieHelper and the Realbench Logger are installed once per X-Plane installation and are available for both products. The Logger preserves unrelated DataRefMonitor profiles and generated logs. The FANS CDU patch and Paintkit are LevelUp-only. Every item remains optional.

Aircraft packages, VNAV content, optional patches, tools, resources and Toolkit application releases are separate update layers. The app does not distribute a complete modified `B738.a_fms.lua`; VNAV hooks and authorized payload files are applied locally after validation.

Zibo packages are obtained from the official Skymatix feed. The Toolkit tries a direct archive first and automatically uses the feed's official BitTorrent metadata when required. Before peer-to-peer transfer starts, the app explains that peers can see the user's public IP address and that package pieces may be uploaded while the client runs.

This is not an official Laminar Research, Zibo or LevelUp product. It does not replace the aircraft developers' official distribution channels.

## Compatibility

- X-Plane 12 only
- Zibo 737-800X 2K and 4K
- LevelUp 737-600, 737-700, 737-800, 737-900 and 737-900ER
- Windows x64
- macOS arm64 (Apple silicon)
- Linux x64

X-Plane 11 is not supported.

## Download

Download the latest stable build and select the normal-use artifact for your platform:

https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest

- Windows: `XPlane737NGMaintenanceToolkit-stable-win-x64-Setup.exe`, or the Windows portable ZIP
- macOS Apple silicon: `XPlane737NGMaintenanceToolkit-stable-osx-arm64-Setup.pkg`, or the macOS portable ZIP
- Linux x64: `XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage`

Do not download the `.nupkg`, `RELEASES-*` or `assets.*.json` files for a normal manual installation. Those assets are VeloPack release metadata.

## Installation

Windows:

1. Run the Setup EXE, or extract the portable ZIP.
2. Start `XPlane737NGMaintenanceToolkit`.

macOS Apple silicon:

1. Run the Setup PKG, or extract the portable ZIP.
2. Start the Toolkit application.

Linux x64:

1. Download the AppImage.
2. Make it executable and start it:

```bash
chmod +x XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage
./XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage
```

The current builds are unsigned. Windows may display a SmartScreen warning, macOS may block the unsigned and non-notarized build, and the Linux AppImage is not separately signed.

Verify the download against `SHA256SUMS.txt` on the release page. If a verified macOS download is blocked, try to open it once, then use Apple's documented Privacy & Security "Open Anyway" process:

https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac

VeloPack-managed installations check for stable Toolkit updates in the background. Available updates are downloaded and verified only after user action and applied through an explicitly confirmed restart. App updates remain separate from aircraft and content maintenance.

## First Use

1. Close X-Plane completely.
2. Start the Toolkit.
3. Click `Auto-detect`, or browse to an X-Plane, `Aircraft`, Zibo or LevelUp folder.
4. Select the required detected product when more than one is available.
5. Click `Scan selected folder`.
6. Review the displayed product and version state.
7. Use `Update` and read each confirmation before applying changes.

The Toolkit blocks modifying actions while X-Plane is running. Restart X-Plane fully after an install, update, repair, restore or uninstall.

## Documentation And Support

User manual:

https://github.com/wahltho/xplane-737ng-maintenance-toolkit/blob/main/docs/USER_MANUAL.md

Source code and issue tracker:

https://github.com/wahltho/xplane-737ng-maintenance-toolkit

https://github.com/wahltho/xplane-737ng-maintenance-toolkit/issues

When reporting a problem, use `Dump to file` on the Advanced tab and attach the exported operation log. Do not upload complete copyrighted aircraft files.

## License And Disclaimer

The Toolkit source code is available under the MIT License. The application is provided as-is, without warranty of any kind. It can modify aircraft files after validation and backup; users should retain their own backups and use the tool at their own risk.

## Changelog

### 0.11.0

- Added the Toolkit version to the application header.
- Improved LevelUp 7z review and installation performance with a single
  sequential archive pass.
- Embedded the application icon in the Windows executable so installed
  shortcuts display it correctly.

### 0.10.0

- Added the optional 737NG Realbench Logger for supported Zibo and LevelUp
  installations.
- Added verified schema-v2 X-Plane-root overlay packages whose owned files may
  span multiple simulator directories.
- Added file-wise transactional backup, install, update, repair and guarded
  restore while preserving generated logs and unrelated local files.
- Limited each tool's release-channel selector to channels declared by its
  trusted catalog entry.

### 0.9.0

- Added background stable-channel Toolkit update checks through VeloPack and
  GitHub Releases.
- Added a dedicated update banner with release notes, Later, cancellable
  download progress and explicit Restart and update actions.
- Kept application updates isolated from aircraft, VNAV, patch, tool and
  resource transactions; failures do not block maintenance functions.

### 0.8.2

- Added embedded cross-platform BitTorrent downloads for official Zibo
  baseline and cumulative update packages.
- Added explicit peer-to-peer confirmation, live percentage/peer/rate progress,
  cancellation and an inactivity timeout.
- Added exact torrent package identity checks, piece verification and final
  archive validation before caching.

### 0.8.1

- Prevented torrent metadata, HTML responses and unsupported content from being
  parsed as aircraft package archives.
- Added safe download and archive error handling so invalid upstream content
  cannot terminate the Toolkit process.
- Added per-package and per-file progress while aircraft packages are reviewed
  and verified.

### 0.8.0

- Added complete fresh-aircraft installation for Zibo 737-800X and LevelUp
  737NG Series into an unused X-Plane 12 Aircraft subfolder.
- Added verified LevelUp full-package resolution and download through the
  authorized public release index.
- Added Zibo full-baseline plus latest-cumulative-patch planning with exact
  offline package import when the official feed exposes torrent links.
- Added cancellable staging, dry-run review, product-identity validation and
  atomic activation without overwriting an existing aircraft folder.

### 0.7.0

- Added Optimized XLua 1.3.7r3 as an optional aircraft-scoped component for
  supported Zibo and LevelUp installations.
- Added verified Stable/Beta release discovery, transactional install, update,
  repair, backup and guarded restore for aircraft components.
- Preserved the complete aircraft-owned `plugins/xlua/scripts` tree during
  XLua component operations.
- Added hash-verified XLua preservation across incremental and clean-baseline
  aircraft updates; missing or changed managed files require repair first.

### 0.6.1

- Reworked cross-baseline Zibo updates as clean staged aircraft replacements
  instead of overlays onto an older baseline.
- Added exact full-directory backup, transactional activation, rollback and
  full-directory restore for clean baseline updates.
- Added ZIP content-root detection, obsolete baseline-file removal and guarded
  migration of protected preferences and local liveries.
- Limited very large review lists to 500 visible details while retaining full
  plan totals.

### 0.6.0

- Added a confirmed LevelUp fleet view transfer from one selected source
  variant to all other variants in the same aircraft folder.
- Added per-target feet-to-meters CG correction for transferred Quick Views.
- Added QV0-derived Default Viewpoints, per-file backups, all-target
  pre-validation and rollback for the fleet transaction.

### 0.5.0

- Added the LevelUp FANS CDU package as the first visible explicit optional aircraft patch.
- Added a verified Resources workflow with user-selected extraction directories.
- Added the official LevelUp 737NG Paintkit 1.1.0 as the first visible resource package.
- Added guarded optional-patch restore and removal controls.
- Added resource manifest, GitHub asset, archive traversal, file hash and installation-state validation.

### 0.4.0

- Added a product-gated Tools workflow for YAL and YAL HoppieHelper.
- Added independent Stable and Beta channels with verified GitHub Release manifests, archives and payload hashes.
- Added transactional tool install, update, repair and guarded restore while preserving manifest-declared user data and local unowned files.
- Added a generic transactional content-patch engine while retaining the existing managed VNAV workflow for Zibo and LevelUp.
- Added a trusted product-scoped package catalog without advertising an optional aircraft patch in this release.

### 0.3.10

- Added clear completion dialogs for aircraft update and restore operations.
- Added explicit blocked-operation feedback when X-Plane is running.
- Improved final update status and installed-version reporting.

### 0.3.9

- Corrected product-wide LevelUp update state and backup naming.
- Improved update progress reporting and product-level confirmation text.
- Clarified CG/view maintenance controls.

### 0.3.8

- Corrected offline LevelUp package import and version detection.
- Improved compatibility with the LevelUp manifest/archive package workflow.

## Suggested X-Plane.org Tags

X-Plane 12, Utilities, Zibo, LevelUp, 737NG, Updater, VNAV
