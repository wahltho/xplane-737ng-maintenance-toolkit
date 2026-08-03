# X-Plane 737NG Maintenance Toolkit User Manual

This manual describes the current public release of the X-Plane 737NG Maintenance
Toolkit, version 0.3.10.

The toolkit is a desktop app for selected Zibo and LevelUp 737NG maintenance
tasks:

- VNAV descent table package install, update, repair, restore and uninstall.
- Quick View and default-view maintenance after aircraft CG changes.
- Config backup and config restore for supported aircraft preference files.
- Zibo and LevelUp aircraft package check, cache, review, apply and restore.

The app does not replace X-Plane, Zibo, LevelUp or their official installers.
It works on a selected local aircraft folder and writes only after validation,
backup and an explicit user action.

## Disclaimer

This toolkit is provided as-is, without warranty of any kind. It is an
independent community tool and is not an official X-Plane, Zibo or LevelUp
product unless explicitly stated otherwise.

The toolkit can modify aircraft installation files after validation and backup.
Keep your own backups and use the tool at your own risk.

## Compatibility And Installation

Version 0.3.10 supports:

- X-Plane 12. X-Plane 11 is not supported.
- Zibo 737-800X 2K and 4K variants.
- LevelUp 737-600, 737-700, 737-800, 737-900 and 737-900ER.
- Windows x64.
- macOS arm64 (Apple silicon).
- Linux x64.

Download the current stable release from:

https://github.com/wahltho/xplane-737ng-maintenance-toolkit/releases/latest

Choose one normal-use artifact for the target platform:

| Platform | Recommended artifact | Portable alternative |
| --- | --- | --- |
| Windows x64 | `XPlane737NGMaintenanceToolkit-stable-win-x64-Setup.exe` | `XPlane737NGMaintenanceToolkit-stable-win-x64-Portable.zip` |
| macOS arm64 | `XPlane737NGMaintenanceToolkit-stable-osx-arm64-Setup.pkg` | `XPlane737NGMaintenanceToolkit-stable-osx-arm64-Portable.zip` |
| Linux x64 | `XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage` | None |

On Windows or macOS, run the setup package or extract the portable ZIP and start
the app from the extracted folder. On Linux, make the AppImage executable before
starting it:

```bash
chmod +x XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage
./XPlane737NGMaintenanceToolkit-stable-linux-x64.AppImage
```

Current release builds are unsigned:

- Windows may show a SmartScreen warning.
- macOS builds are unsigned and not notarized, so macOS may block the first
  launch.
- Linux AppImage builds are not separately signed.

Verify downloaded artifacts against `SHA256SUMS.txt` on the release page. If a
verified macOS download is blocked, try to open it once, then follow Apple's
documented [Privacy & Security "Open Anyway" process](https://support.apple.com/guide/mac-help/open-a-mac-app-from-an-unknown-developer-mh40616/mac).

Download future Toolkit releases manually from GitHub. VeloPack provides the
application package and lifecycle integration, but version 0.3.10 does not yet
check for or download new Toolkit versions automatically. This is separate from
aircraft-package and VNAV-content updates performed inside the app.

## Before You Start

Close X-Plane before using any action that changes files. The app detects a
running X-Plane process and blocks write actions when X-Plane is open.

After every real install, update, repair, restore or uninstall, restart
X-Plane fully. Reloading Lua, reloading the aircraft or reloading plugins is
not treated as enough.

## Selecting An Aircraft

Use the `Start` tab first.

1. On startup, the app searches common X-Plane aircraft locations in the
   background and keeps a valid previously selected folder.
2. Select another auto-detected installation folder when required. Use
   `Auto-detect` to refresh the discovered choices.
3. If the aircraft is not found, click `Browse` and select an X-Plane folder,
   the `Aircraft` folder, or a direct Zibo/LevelUp aircraft folder manually.
4. Click `Scan selected folder`.

The selected folder can be an X-Plane root, an `Aircraft` folder, or a direct
supported aircraft folder. Do not select a component subfolder such as
`plugins` or `fmod`.

Detection is structural. Folder names are not trusted by themselves. The app
looks for expected aircraft files and target scripts before enabling write
actions.

The `Installation Folder` panel is the scan input. It contains exactly one
selected folder at a time, but that folder may contain more than one supported
product. The app then derives product targets from the folder contents.
Supported products are `Zibo` and `LevelUp`; detected variants belong to the
selected product and are used for variant-specific view maintenance.

The status areas on the `Start` tab show:

- selected product state
- installed and available VNAV package versions
- aircraft package update state where an update source is available

## Main Tabs

### Start

This is the normal user workflow. It contains product selection and two
maintenance cards for the selected product:

- `Updates`
- `Views After CG Change`

Zibo and LevelUp aircraft are treated as equal 737NG targets. The app enables
only the actions that apply to the detected aircraft and available package
sources.

The `Updates` card shows aircraft-package and VNAV-table status together. Its
main `Update` button checks both layers and performs the safe sequence where
possible: check the product release source, download the required aircraft
package into the cache, review its contents, ask for confirmation, then apply
it with backup and rollback. The app subsequently offers the required VNAV
install, update or repair. If the aircraft package is already current, the same
button can continue directly with the VNAV action. If a source is unavailable,
import the exact required aircraft package manually and retry.

Zibo uses its public feed and the baseline/cumulative package model described
below. LevelUp uses the authorized public `737NG-Updates` GitHub Release index.
For LevelUp, an installation matching the declared baseline receives only the
cumulative patch; an unknown or different baseline requires the exact full
package. The release index, package manifests, archive sizes and SHA-256 hashes
must all agree before download or review is enabled. The app also enforces the
release's declared minimum toolkit version.

The VNAV area of the `Updates` card handles manifest-owned descent table
content and Lua hooks. The operation modifies only manifest-owned VNAV blocks
and payload files after validation and backup. Aircraft-package and VNAV writes
remain separate confirmed transactions with separate backup and restore state.

VNAV content writes are limited to the manifest-owned Lua blocks and payload
files. The app never distributes or writes a complete modified
`B738.a_fms.lua`.

The `Views After CG Change` card contains view and configuration maintenance for
the selected aircraft variant.

`Fix` adjusts X-Plane quick-view positions after an aircraft CG change when the
Toolkit has a reliable previous and current CG baseline. The app reads the ACF
CG values in feet and the quick-view positions in meters, then applies the
required conversion internally. A matching `X-Camera_<acf-stem>.csv` file is
adjusted as well when one is present.

`Adopt Current CG as Baseline` records the current ACF CG as the local baseline
without moving any view. Use it only when the current views are already correct
and the Toolkit cannot identify their previous CG baseline reliably.

`Use Quick View 0 as Default Viewpoint` writes the aircraft ACF default view
from Quick View 0. The app calculates the ACF default-view coordinates in feet
from Quick View 0 and the current ACF CG.

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

### Advanced

The `Advanced` tab contains technical details, review output and recovery actions
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
stream; in that case use `Import package`.

Use `Import package` to select a local package. The selected
file name must match a required package in the current plan exactly. For an
offline LevelUp test, the app also accepts the published `.manifest.json` or
its adjacent `.7z` archive and builds the plan from that authoritative
manifest. Both selections produce the same plan. Keep the manifest and archive
together in the same folder. After a successful import, the main `Update`
button uses that verified offline plan directly instead of replacing it with
another online release check. If the file dialog closes and nothing obvious
happens, check the aircraft update status, cache status and Advanced tab for the
import result.
Package copying and integrity checks run in the background. They can be
canceled before the verified package is committed to the toolkit cache;
aircraft files are never changed by import.

Use `Review aircraft changes` before applying. Review opens the cached packages
and reports which files would be added, replaced or protected. No aircraft
files are changed during review. The review runs in the background and can be
canceled.

Use `Apply cached update` only after the cache contains every required
package and review is clean. A confirmation dialog summarizes the reviewed
target, version transition and file counts before any write is allowed. The
internal validation can still be canceled. When the write phase starts, Cancel
is disabled and the transaction must either complete or roll back. The apply
operation:

- blocks when X-Plane is running
- verifies cached ZIP size and SHA-256 against the recorded cache snapshot
- performs an internal review pass before writing
- backs up replaced files and tracks files added by the update
- preserves protected local config and preference files
- writes toolkit metadata after a successful update
- rolls back changed files if the transaction fails

After a successful aircraft update, the app rescans the selected target. If
the VNAV package is missing, outdated or repairable, a separate confirmation
offers the appropriate VNAV action. Skipping it leaves the aircraft update in
place and does not merge the aircraft and VNAV transactions. The completion
status reports the installed version and explicitly confirms that the existing
aircraft folder name was retained. Aircraft folders are user-owned installation
paths and are not renamed during an update.

The main `Update` button is shown while an aircraft source can still be checked,
an aircraft package action is available, or VNAV content can be safely
installed, updated or repaired. It is hidden once the app has established that
none of those actions can be performed.

Use `Restore aircraft` on the Start tab to restore the latest aircraft-update
backup generation. Files that were added by the update are removed again during
restore. `Restore VNAV` restores the latest separate VNAV backup generation.

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

The log can be cleared from the `Advanced` tab. Clearing the visible log does not
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

For Zibo, check for updates first and select the exact package required by the
current plan. For an offline LevelUp package, keep the manifest and `.7z`
together and select either file. Review the Advanced findings if validation
rejects the pair.

`Online source unavailable`

The public LevelUp release index or Zibo package source could not be reached.
Check the network connection and retry. For LevelUp, an authorized manifest and
its adjacent `.7z` archive can be imported as an offline fallback. For Zibo,
download the exact package required by the current plan and use `Import package`.
Technical HTTP details remain in Advanced and the log.

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

## Support And Diagnostics

Use `Dump to file` on the Advanced tab to export the visible operation log into
the configured diagnostics folder. Review the file before posting it and attach
it when reporting a problem.

Report Toolkit issues at:

https://github.com/wahltho/xplane-737ng-maintenance-toolkit/issues

Include the Toolkit version, operating system, detected product, operation being
attempted and exported log. Do not upload complete copyrighted aircraft files.

## Current Limitations

- App builds are unsigned releases.
- macOS builds are not notarized.
- Version 0.3.10 does not automatically check for or install new Toolkit
  versions. Download newer app releases manually from GitHub.
- Zibo upstream ZIPs are verified against the local cache snapshot, not an
  official upstream hash manifest.
- Online LevelUp aircraft update checks require the authorized public
  `petrolpram/737NG-Updates` endpoint to be reachable and to contain a published
  compatible release. Draft or offline packages require manual
  manifest/archive import.
- `Dump to file` exports the current visible operation log. A broader bundled
  diagnostic-report workflow is still a planned product feature.
