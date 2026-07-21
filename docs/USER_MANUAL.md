# X-Plane 737NG Maintenance Toolkit User Manual

This manual describes the current public release of the X-Plane 737NG Maintenance
Toolkit.

The toolkit is a desktop app for selected Zibo and LevelUp 737NG maintenance
tasks:

- VNAV descent table package install, update, repair, restore and uninstall.
- Quick View and default-view maintenance after aircraft CG changes.
- Config backup and config restore for supported aircraft preference files.
- Zibo upstream aircraft package review, cache, dry-run, apply and restore.

The app does not replace X-Plane, Zibo, LevelUp or their official installers.
It works on a selected local aircraft folder and writes only after validation,
backup and an explicit user action.

## Before You Start

Close X-Plane before using any action that changes files. The app detects a
running X-Plane process and blocks write actions when X-Plane is open.

After every real install, update, repair, restore or uninstall, restart
X-Plane fully. Reloading Lua, reloading the aircraft or reloading plugins is
not treated as enough.

Current release builds are unsigned:

- Windows may show a SmartScreen warning.
- macOS builds are not notarized.
- Linux AppImage builds are not separately signed.

Use the released setup, package or portable artifact for your platform from
the GitHub Release page.

## Selecting An Aircraft

Use the left Aircraft Target panel first.

1. Click `Auto-detect` to search common X-Plane aircraft locations.
2. Select the intended Zibo or LevelUp candidate.
3. If the aircraft is not found, click `Browse` and select the aircraft root
   folder manually.
4. Click `Scan selected folder`.

The selected folder must be the aircraft root, not the X-Plane root and not a
subfolder such as `plugins` or `fmod`.

Detection is structural. Folder names are not trusted by themselves. The app
looks for expected aircraft files and target scripts before enabling write
actions.

The status cards at the top show:

- `Aircraft status`: overall VNAV patch state for the selected target.
- `Installed package`: detected installed VNAV content version.
- `Available package`: package version from the active manifest.
- `Line endings`: detected line-ending style of the target Lua script.

## Main Tabs

### Views And Config

This tab contains view and configuration maintenance for the selected aircraft
variant.

`Adapt Quick Views` adjusts X-Plane quick-view positions after an aircraft CG
change. The app reads the ACF CG values in feet and the quick-view positions in
meters, then applies the required conversion internally. A matching
`X-Camera_<acf-stem>.csv` file is adjusted as well when one is present.

`Apply QV0 to Default View` writes the aircraft ACF default view from Quick
View 0. The app calculates the ACF default-view coordinates in feet from Quick
View 0 and the current ACF CG.

`Create Config Backup` backs up supported root-level aircraft configuration
files without changing aircraft files.

`Restore Config Backup` restores the latest config-only backup generation. It
creates a pre-restore image before replacing current config files.

`Restore Latest Backup` restores the latest recorded toolkit backup generation
for the selected variant. Use this when a previous toolkit operation should be
reverted.

Supported config backup files include:

- `*_prefs.txt`
- `*_vrconfig.txt`
- `X-Camera_*.csv`
- `*.cfg`
- `b738_config.txt`
- `version.txt`
- `xplane-737ng-maintenance.json`

### Aircraft Updates

This tab handles upstream aircraft package planning and application. The first
implemented source is Zibo.

Click `Refresh` to read the local aircraft version and refresh the upstream
package index. The app then shows:

- installed version
- available version
- update mode
- required package list
- source links
- cache status

Zibo packages are modeled as baseline plus cumulative patch:

- `Full` means the plan includes a full baseline ZIP. If a cumulative patch is
  available for the same baseline, both packages are required.
- `Incremental` means the local aircraft is already on the current baseline
  and only the latest cumulative patch ZIP is required.

The app does not apply a chain of incremental patches. It plans either the
current full baseline plus latest cumulative patch, or only the latest
cumulative patch for the already installed baseline.

Use `Download ZIPs` to let the app try to download required ZIPs into the
aircraft update cache. If the source exposes a `.zip.torrent` URL, the app
tries the matching `.zip` URL first. Some sources may not expose a direct ZIP
stream; in that case use `Import ZIP`.

Use `Import ZIP` to select a local package ZIP. The selected file name must
match a required package in the current plan exactly. If the file dialog closes
and nothing obvious happens, check the Aircraft Updates status line, cache
status and Logs tab for the import result.

Use `Dry-run ZIPs` before applying. Dry-run opens the cached ZIPs and reports
which files would be added, replaced or protected. No aircraft files are
changed during dry-run.

Use `Apply cached ZIPs` only after the cache contains every required package
and dry-run is clean. The apply operation:

- blocks when X-Plane is running
- verifies cached ZIP size and SHA-256 against the recorded cache snapshot
- performs an internal dry-run before writing
- backs up replaced files and tracks files added by the update
- preserves protected local config and preference files
- writes toolkit metadata after a successful update
- rolls back changed files if the transaction fails

Use `Restore update` to restore the latest aircraft-update backup generation.
Files that were added by the update are removed again during restore.

Official Zibo package hashes are not available from the feed. The app verifies
that the cached ZIP has not changed since import or download; it cannot verify
the ZIP against an official upstream manifest hash unless such a manifest is
provided later.

Custom distributions and no-Lua ports can declare their own state in
`xplane-737ng-maintenance.json`. For those targets, official upstream Zibo
package information is review-only unless a dedicated custom-port update
source is implemented.

### VNAV Tables

This tab handles manifest-owned VNAV descent table content.

`Dry-run` calculates planned changes without writing files.

`Install` adds the manifest-owned Lua hooks and payload files when the package
is not installed.

`Update` replaces older manifest-owned hooks or payload files with the current
package version.

`Repair` rewrites the current manifest-owned package even when the installed
version appears current. Use it when payload files are missing or changed.

`Restore` restores the latest recorded backup generation for the selected
variant.

`Uninstall` removes manifest-owned VNAV hook blocks and matching manifest
payload files. Changed payload files that no longer match the manifest hash are
left in place instead of being deleted blindly.

VNAV content writes are limited to the manifest-owned Lua blocks and payload
files. The app never distributes or writes a complete modified
`B738.a_fms.lua`.

For normal online use, the app tries to refresh `package-manifest.txt` and
payload files from explicit GitHub Release assets. Fallback sources are:

- the folder set in `XPLANE_737NG_PACKAGE_DIR`
- the Offline VNAV package folder configured in Settings
- bundled preview content shipped with the app
- the source-tree content folder during development

Every payload is checked against size and SHA-256 from the manifest before it
is installed.

### Activity

This tab shows the current write operation with progress, elapsed time and a
console-style transaction log.

Use it while a longer apply, restore or backup operation is running.

### Settings

Settings are stored in `settings.json` under the toolkit data folder shown in
the Settings tab. Directory settings are normalized and tested for write
access before saving.

The selected aircraft folder is also stored in `settings.json` and is restored
when the app starts. On Linux, the toolkit data folder follows
`$XDG_CONFIG_HOME` or `~/.config`; the aircraft update ZIP cache follows
`$XDG_CACHE_HOME` or `~/.cache`.

Available settings:

- `Backup folder`: stores real backup data and restore records. Do not delete
  this folder casually.
- `Aircraft update ZIP cache folder`: stores downloaded or imported upstream
  aircraft update ZIPs. This can be cleared and recreated.
- `Offline VNAV package folder`: optional local source for VNAV manifest
  payload files.
- `Diagnostics export folder`: target folder reserved for diagnostic exports.

Changing the backup folder affects future backups. Existing restore records
keep their original absolute backup paths.

`Clear Cache` removes the current aircraft update ZIP cache contents. It does
not delete aircraft files and does not delete backups.

### Logs

The Logs tab contains the session install log. It records scans, blocked
actions, backups, dry-runs, writes, restores and errors.

The log can be cleared from the UI. Clearing the visible log does not delete
backup files or state records.

## Safety Rules

The app follows these safety rules for modifying operations:

- X-Plane must be closed.
- The selected aircraft must be structurally recognized.
- VNAV hooks are applied only when manifest markers and anchors are safe.
- Required VNAV payload files must match manifest size and SHA-256.
- Aircraft update ZIP paths must stay inside the selected aircraft folder.
- Protected local preference/config files are not overwritten by aircraft
  update ZIPs.
- A backup or restore record is created before replacing existing files.
- Failed write transactions attempt rollback.
- A full X-Plane restart is required after changes.

Stop and inspect the findings/log if the app reports an unsafe state, unknown
modification, duplicate anchor, missing anchor, invalid ZIP, missing payload or
read-only target.

## Common Problems

`No supported aircraft found`

Select the aircraft root folder manually and scan again. The aircraft root is
the folder that contains the ACF file and aircraft subfolders.

`X-Plane is running`

Close X-Plane fully, then retry the operation.

`Import blocked`

Refresh the Aircraft Updates tab first. The ZIP file name must match one of
the required packages in the current plan.

`Download failed`

The source may not expose a direct ZIP stream. Download the required ZIP
manually and use `Import ZIP`.

`Dry-run blocked`

Check the dry-run findings. Common causes are missing cache entries, an invalid
ZIP file or an unsafe ZIP path.

`Target state is not safe to patch`

The current aircraft files do not match a state the app can modify safely.
Review the Findings and Logs tabs before changing anything manually.

`Custom distribution detected`

Official upstream aircraft packages are shown as review-only for custom/no-Lua
distributions unless a dedicated update source is defined.

## Current Limitations

- App builds are unsigned releases.
- macOS builds are not notarized.
- Zibo upstream ZIPs are verified against the local cache snapshot, not an
  official upstream hash manifest.
- Aircraft update support currently targets Zibo package planning.
- LevelUp can later use the same aircraft-update transaction layer if an
  authorized package index/source is provided.
- The diagnostics export folder is configurable; a full one-click diagnostic
  export workflow is still a planned product feature.
