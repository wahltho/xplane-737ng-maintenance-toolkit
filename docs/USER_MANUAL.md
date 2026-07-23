# X-Plane 737NG Maintenance Toolkit User Manual

This manual describes the current public release of the X-Plane 737NG Maintenance
Toolkit.

The toolkit is a desktop app for selected Zibo and LevelUp 737NG maintenance
tasks:

- VNAV descent table package install, update, repair, restore and uninstall.
- Quick View and default-view maintenance after aircraft CG changes.
- Config backup and config restore for supported aircraft preference files.
- Zibo upstream aircraft package review, cache, apply and restore.

The app does not replace X-Plane, Zibo, LevelUp or their official installers.
It works on a selected local aircraft folder and writes only after validation,
backup and an explicit user action.

## Disclaimer

This toolkit is provided as-is, without warranty of any kind. It is an
independent community tool and is not an official X-Plane, Zibo or LevelUp
product unless explicitly stated otherwise.

The toolkit can modify aircraft installation files after validation and backup.
Keep your own backups and use the tool at your own risk.

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

Use the `Start` tab first.

1. Click `Auto-detect` to search common X-Plane aircraft locations.
2. Select an auto-detected installation folder, or keep the manually entered
   folder.
3. If the aircraft is not found, click `Browse` and select an X-Plane folder,
   the `Aircraft` folder, or a direct Zibo/LevelUp aircraft folder manually.
4. Click `Scan selected folder`.

The selected folder must be the aircraft root, not the X-Plane root and not a
subfolder such as `plugins` or `fmod`.

Detection is structural. Folder names are not trusted by themselves. The app
looks for expected aircraft files and target scripts before enabling write
actions.

The `Installation Folder` panel is the scan input. It contains exactly one
selected folder at a time, but that folder may contain more than one supported
product. The app then derives product targets from the folder contents.
Supported products are `Zibo` and `LevelUp`; detected variants belong to the
selected product and are used for variant-specific view maintenance.

The status cards on the `Start` tab show:

- selected product state
- installed and available VNAV package versions
- aircraft package update state where an update source is available

## Main Tabs

### Start

This is the normal user workflow. It contains product selection and three
maintenance cards for the selected product:

- `Aircraft Update`
- `VNAV Descent Tables`
- `Views After CG Change`

Zibo and LevelUp aircraft are treated as equal 737NG targets. The app enables
only the actions that apply to the detected aircraft and available package
sources.

The `Aircraft Update` card handles upstream aircraft package planning and
application. The first implemented aircraft package source is Zibo. The main
`Update` button performs the safe sequence where possible: check the package
index, download direct ZIP sources into the cache, review the cached packages,
then apply them with backup and rollback. If a source does not expose a direct
ZIP stream, import the exact required ZIP manually and retry.

The `VNAV Descent Tables` card handles manifest-owned descent table content and
Lua hooks. Use `Update` when tables are missing, outdated or need repair. The
operation modifies only manifest-owned VNAV blocks and payload files after
validation and backup.

VNAV content writes are limited to the manifest-owned Lua blocks and payload
files. The app never distributes or writes a complete modified
`B738.a_fms.lua`.

The `Views After CG Change` card contains view and configuration maintenance for
the selected aircraft variant.

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

### Log

The `Log` tab contains technical details, review output and recovery actions
that normal users should not need for routine operation.

`Review VNAV changes` calculates planned changes without writing files.
Advanced VNAV actions such as `Install`, `Repair` and `Uninstall` are kept here
instead of on the main `Start` page.

Use `Dump to file` to export the visible install and operation logs into the
configured diagnostics export folder. If users need support, this is the file
to attach or post.

For normal online use, the app tries to refresh `package-manifest.txt` and
payload files from explicit GitHub Release assets. Fallback sources are:

- the folder set in `XPLANE_737NG_PACKAGE_DIR`
- the Offline VNAV package folder configured in Settings
- bundled preview content shipped with the app
- the source-tree content folder during development

Every VNAV payload is checked against size and SHA-256 from the manifest before
it is installed.

The aircraft package details section shows:

- installed version
- available version
- update mode
- required package list
- source links
- package cache status

Zibo packages are modeled as baseline plus cumulative patch:

- `Full` means the plan includes a full baseline ZIP. If a cumulative patch is
  available for the same baseline, both packages are required.
- `Incremental` means the local aircraft is already on the current baseline
  and only the latest cumulative patch ZIP is required.

The app does not apply a chain of incremental patches. It plans either the
current full baseline plus latest cumulative patch, or only the latest
cumulative patch for the already installed baseline.

Use `Download required packages` to let the app try to download required
packages into the aircraft update cache. If the source exposes a `.zip.torrent`
URL, the app
tries the matching `.zip` URL first. Some sources may not expose a direct ZIP
stream; in that case use `Import package...`.

Use `Import ZIP` or `Import package...` to select a local package ZIP. The selected file name must
match a required package in the current plan exactly. If the file dialog closes
and nothing obvious happens, check the aircraft update status, cache status and
Advanced tab for the import result.

Use `Review aircraft changes` before applying. Review opens the cached packages
and reports which files would be added, replaced or protected. No aircraft
files are changed during review.

Use `Apply aircraft update` only after the cache contains every required
package and review is clean. The apply operation:

- blocks when X-Plane is running
- verifies cached ZIP size and SHA-256 against the recorded cache snapshot
- performs an internal review pass before writing
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

### Settings

The `Settings` tab stores directory settings in `settings.json` under the
toolkit data folder shown in the settings panel. Directory settings are
normalized and tested for write access before saving.

The selected aircraft folder is also stored in `settings.json` and is restored
when the app starts. On Linux, the toolkit data folder follows
`$XDG_CONFIG_HOME` or `~/.config`; the aircraft update ZIP cache follows
`$XDG_CACHE_HOME` or `~/.cache`.

Available settings:

- `Backup folder`: stores real backup data and restore records. Do not delete
  this folder casually.
- `Aircraft update package cache folder`: stores downloaded or imported
  upstream aircraft update packages. This can be cleared and recreated.
- `Offline VNAV package folder`: optional local source for VNAV manifest
  payload files.
- `Diagnostics export folder`: target folder reserved for diagnostic exports.

Changing the backup folder affects future backups. Existing restore records
keep their original absolute backup paths.

`Clear Cache` removes the current aircraft update ZIP cache contents. It does
not delete aircraft files and does not delete backups.

The log can be cleared from the `Log` tab. Clearing the visible log does not
delete backup files or state records.

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

Use `Check for updates` in the Updates tab first. The ZIP file name must match one of
the required packages in the current plan.

`Download failed`

The source may not expose a direct ZIP stream. Download the required ZIP
manually and use `Import package...`.

`Review blocked`

Check the review findings. Common causes are missing cache entries, an invalid
ZIP file or an unsafe ZIP path.

`Target state is not safe to patch`

The current aircraft files do not match a state the app can modify safely.
Review the Findings and Advanced tab before changing anything manually.

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
