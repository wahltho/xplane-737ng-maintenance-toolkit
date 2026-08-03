# X-Plane.org Listing Copy

For direct pasting into the X-Plane.org rich-text editor, open
`XPLANE_ORG_LISTING_COPY.html` in a browser and copy the rendered content. The
editor does not interpret raw Markdown headings or lists.

## Title

X-Plane 737NG Maintenance Toolkit

## Current Version

0.4.0

## Short Description

Cross-platform maintenance utility for supported Zibo and LevelUp 737NG aircraft in X-Plane 12, providing aircraft and VNAV updates, transactional backups, CG/view correction and optional tool management.

## Full Description

The X-Plane 737NG Maintenance Toolkit is an independent community application for selected maintenance tasks affecting the Zibo 737-800X and LevelUp 737NG Series.

The normal workflow is product-neutral: select or auto-detect an aircraft installation, scan it, review the detected product and use the relevant maintenance action. Before writing files, the Toolkit validates package metadata, hashes and target paths, creates backups, preserves protected local configuration files and rolls back failed transactions where possible.

Current functions include:

- structural detection of supported Zibo and LevelUp installations
- support for multiple X-Plane installations and manual folder selection
- Zibo baseline/cumulative aircraft update planning and package handling
- LevelUp full/cumulative updates from its authorized public GitHub release index
- VNAV descent table install, update, repair, restore and uninstall
- separate backup and restore state for aircraft, VNAV and tool transactions
- Quick View correction after an aircraft CG change
- optional correction of a matching X-Camera file
- use of Quick View 0 as the aircraft default viewpoint
- configuration backup and restore
- cancellable download and review before a write transaction begins
- detailed Advanced log and diagnostic export for support requests
- automatic operating-system light and dark appearance
- optional installation, update, repair and restore of supported X-Plane tools
- stable and beta release channels for supported optional tools

Version 0.4.0 currently offers the following optional tools:

- Yet Another Linda (YAL)
- YAL HoppieHelper

These tools are installed once per X-Plane installation and are available for supported Zibo and LevelUp aircraft. They remain entirely optional.

Aircraft packages, VNAV content, optional tools and Toolkit application releases are separate update layers. The app does not distribute a complete modified `B738.a_fms.lua`; VNAV hooks and authorized payload files are applied locally after validation.

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

Version 0.4.0 does not automatically check for or install newer Toolkit versions. Download future app releases manually from the GitHub release page.

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
