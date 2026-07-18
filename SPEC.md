# X-Plane 737NG Maintenance Toolkit / VNAV Descent Tables Installer

Product specification for the initial public app. The original LevelUp VNAV
updater scope has been widened into a neutral Zibo/LevelUp 737NG maintenance
toolkit while keeping VNAV content packages manifest-driven and separately
versioned.

## Goal

Build a small cross-platform installer/updater and maintenance tool for 737NG
VNAV descent table packages, conservative view-maintenance utilities and
validated upstream aircraft package maintenance.

The updater should allow users to safely install, update, verify, repair,
restore and uninstall the VNAV descent table hooks without manually editing
`B738.a_fms.lua` and without requiring Python.

VeloPack is the chosen application installer and update framework.

## Ownership

The official updater application should live in a GitHub organization-owned
open-source repository when the owning team is ready. The current public
development repository can remain a temporary personal repository as long as it
is treated as transferable development infrastructure, not as final official
ownership.

The VNAV content package remains a separate versioned package with its own
manifest, payload files and release assets.

No custom update infrastructure is required. GitHub Releases are the intended
distribution source.

## Versioning Model

There are two separate update layers.

### 1. Updater Application

- Own app version.
- Installed and updated via VeloPack.
- Own VeloPack releases and release channels.
- Platform-specific signed artifacts.

### 2. VNAV Content Package

- Own package version.
- Own manifest.
- Own payload hashes.
- Distributed through explicit GitHub Release assets.
- Installed locally by the patch engine.
- Should be updateable independently from the updater app where possible.

A shared release process is possible, but technical ownership must remain
separated.

## Initial Scope

The first public app should stay deliberately small, but the current beta scope
already spans four clearly separated tool areas:

- Single-page desktop app.
- Auto-detect Zibo and LevelUp aircraft folders.
- Manual aircraft folder override.
- Detect current install state.
- Show installed and available package/component versions.
- Read package manifest as source of truth.
- Verify downloaded payload hashes.
- Verify known markers, anchors and installer-owned files where possible.
- Dry-run / planned changes view.
- Install / Update.
- Repair.
- Restore from backup.
- Uninstall installer-owned hooks/payloads.
- Automatic backup before patching.
- Manual config backup and config-only restore for user preferences, camera
  CSVs, root cfg files and toolkit metadata.
- Quick View adaptation after ACF CG changes.
- Default-view update from Quick View 0.
- Zibo upstream package review, import/download into cache, dry-run, apply and
  restore.
- Separate settings for backup data, aircraft update ZIP cache, offline VNAV
  package source and diagnostics export target.
- Simple install log.
- Diagnostic export target configuration. A full one-click diagnostic export is
  still intended product scope.
- Clear "X-Plane restart required" message.

The app should not attempt to restart X-Plane automatically.

## Application Stack

Recommended stack:

- Current stable .NET LTS. The current repository targets .NET 10.
- Avalonia UI.
- VeloPack SDK/CLI.
- GitHub Releases.
- GitHub Actions.

Suggested module boundaries:

- UI app.
- Core models.
- Aircraft detection.
- Manifest/content update client.
- Patch engine.
- Backup/restore/state store.
- VeloPack app-update integration.

## UI Flow

The app should be a single-page tool, not a marketing landing page. The current
UI is organized as a target selector plus tabs.

### 1. Aircraft Target

- Show detected X-Plane / Zibo / LevelUp installations.
- Provide a Browse button for manual aircraft folder selection.
- Validate the selected folder structurally, not by name only.

### 2. Scan Summary

- Locate `plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua`.
- Load current local install state.
- Fetch or load package manifest.
- Verify payload metadata and checksums.
- Classify install state.

### 3. Views And Config

- Show detected variants, ACF CG, reference CG, delta, Quick View status and
  default-view status.
- Offer Quick View CG adaptation.
- Offer Quick View 0 to default-view application.
- Offer config backup, config restore and latest-backup restore.

### 4. Aircraft Updates

- Show local and available upstream package versions.
- Show update mode: none, full or incremental.
- Show required package list and source links.
- Show aircraft update ZIP cache state.
- Offer refresh, import, download, dry-run, apply and restore.

### 5. VNAV Tables

- Installed package version.
- Available package version.
- Component list.
- Planned changes.
- Install / Update / Repair / Restore / Uninstall actions.

### 6. Activity

- Show current write/restore transaction progress.
- Show elapsed time and transaction console.

### 7. Settings

- Configure backup root.
- Configure aircraft update ZIP cache root.
- Configure offline VNAV package root.
- Configure diagnostics export root.
- Show toolkit data, state and settings file paths.

### 8. Logs

- Show session log.
- Allow clearing the visible log without deleting state or backups.

## Aircraft Detection

The updater must support:

- Multiple X-Plane installations.
- Multiple LevelUp installations.
- Steam and standalone installs.
- Windows, macOS and Linux.
- Manual folder selection.
- Renamed or moved aircraft folders.
- Symlinks.
- Non-writable paths.
- Unsupported or wrong aircraft versions.
- Multiple valid targets.

Detection must use structural signatures of the expected LevelUp v2 aircraft,
not folder names alone.

## Patch State Classification

The patch engine should classify the target as:

- Not installed.
- Correctly installed.
- Installed but outdated.
- Known legacy installation.
- Partially installed.
- Corrupted installer-owned blocks.
- Aircraft update overwrote installation.
- Unknown third-party modification.
- Unsupported target.

Unknown or unsafe states should stop and offer a diagnostic report instead of
overwriting files.

`Correctly installed` requires both current manifest-owned hook blocks and all
required manifest payload files matching size and SHA-256. Current hook blocks
with missing or changed payload files are not a correct install state; they
must be classified as partially installed or repair-required.

## Hard Patch Rules

- Never distribute a full `B738.a_fms.lua`.
- Never overwrite unknown user/third-party changes.
- Marker and anchor matches must be unique.
- No patching if anchors are missing or duplicated.
- No duplicate hook blocks.
- Preserve LF/CRLF line endings.
- Preserve file permissions where possible.
- Define UTF-8/BOM behavior.
- Protect against path traversal.
- Use temp files and atomic replace where possible.
- Roll back completely on failure.
- Always show restart-required after changes.

## Backup And Restore

Backups are automatic before any patching and cannot be skipped. The app also
supports an explicit config-only backup action for user-facing aircraft
configuration files before experiments or upstream maintenance work.

Backups should be stored per target installation and should include:

- Target path.
- Canonical aircraft path.
- Original file hash.
- Pre-patch file hash.
- Package version.
- Manifest hash.
- Timestamp.
- App version.
- EOL/BOM metadata.
- Restore eligibility metadata.

Restore must not blindly overwrite newer legitimate LevelUp/Zibo/user changes.
If the current target no longer matches the stored restore preconditions, the
app should stop and explain the conflict.

Repair, Restore and Uninstall are separate actions.

Config backup and config restore are separate from generic latest-backup
restore. Config restore must select only the latest `ConfigBackup` generation
and must create a pre-restore image before replacing config files.

User-facing storage settings are separate from aircraft content:

- Backup root: durable restore data and toolkit state references.
- Aircraft update ZIP cache root: downloaded/imported upstream aircraft ZIPs.
- Offline VNAV package root: optional local VNAV payload source.
- Diagnostics export root: target folder for future diagnostic exports.

Changing the backup root affects future backups only. Existing restore records
keep their original absolute backup paths.

## Manifest

The package manifest is the source of truth.

It should describe:

- Schema version.
- Package ID.
- Package version.
- Release tag.
- Release channel.
- Source repository.
- Supported aircraft.
- Supported X-Plane versions.
- Target paths.
- Payload files.
- File sizes and SHA-256 hashes.
- Patch operations.
- Anchors.
- Block markers.
- Legacy signatures.
- Conflict rules.
- Install order.
- Verification rules.
- Restart requirement.
- Optional signature metadata.

The current `package-manifest.txt` can be supported as a v1 compatibility
format, but the long-term format should be versioned JSON.

Example shape:

```json
{
  "schemaVersion": 2,
  "packageId": "x-plane-levelup-737ng-vnav-descent-tables",
  "packageVersion": "0.2.0",
  "releaseTag": "content-v0.2.0",
  "releaseChannel": "stable",
  "repository": "https://github.com/JT8D-17/X-Plane-LevelUp-737NG-Descent-Tables",
  "supportedAircraft": ["levelup_737ng_v2"],
  "supportedXPlaneVersions": ["12.x"],
  "targetPaths": [
    "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua"
  ],
  "payloads": [
    {
      "id": "table",
      "path": "B738.a_fms_levelup_tables.lua",
      "size": 22051,
      "sha256": "d79ef7c2c7c613ae51547a44dbefb6667cdce47d238cc595a4c34d66a643b516"
    }
  ],
  "patchOperations": [
    {
      "id": "dofile",
      "anchor": "jit.off()",
      "beginMarker": "-- BEGIN LEVELUP_VNAV_DESCENT_TABLES DOFILE",
      "endMarker": "-- END LEVELUP_VNAV_DESCENT_TABLES DOFILE"
    },
    {
      "id": "kias",
      "anchor": "function take_alt_dist(x_idx_alt, x_spd_alt, x_spd_wnd_alt, x_flap)",
      "beginMarker": "-- BEGIN LEVELUP_VNAV_DESCENT_TABLES KIAS",
      "endMarker": "-- END LEVELUP_VNAV_DESCENT_TABLES KIAS",
      "legacySignatures": ["pcall(B738_variant_test_take_alt_dist,"]
    },
    {
      "id": "mach",
      "anchor": "function take_alt_dist_mach(x_idx_alt, x_spd_alt, x_spd_wnd_alt)",
      "beginMarker": "-- BEGIN LEVELUP_VNAV_DESCENT_TABLES MACH",
      "endMarker": "-- END LEVELUP_VNAV_DESCENT_TABLES MACH",
      "legacySignatures": ["pcall(B738_variant_test_take_alt_dist_mach,"]
    }
  ],
  "restartRequired": true,
  "signature": {
    "type": "minisign|cosign|gpg",
    "asset": "manifest.json.sig"
  }
}
```

## GitHub Releases

Do not use `main` or GitHub auto-generated source ZIPs as the stable update API.

Content packages should be published as explicit GitHub Release assets:

- Manifest.
- Payload files or content ZIP.
- Checksums.
- Optional signature.
- Release notes.

The updater app should consume only authorized release assets matching the
selected channel.

## Aircraft Upstream Updates

Aircraft upstream updates are separate from VNAV content updates and VeloPack
app updates. They should use the same validation, dry-run, staging, backup and
transaction concepts, but must not be mixed with the manifest-owned VNAV patch
state.

The first supported upstream-update source is Zibo. Zibo packages are modeled as
baseline plus cumulative patch:

- A full package establishes the current baseline, for example
  `B737-800X_XP12_4_05_full.zip`.
- A patch package is cumulative within that baseline, for example
  `B738X_XP12_4_05_35.zip`.
- The app must not apply an incremental chain such as `.31 -> .32 -> .33 ->
  .34 -> .35`.
- If the local install is already on the current baseline, only the latest
  cumulative patch is required.
- If the local install is on an older baseline, or no reliable local version is
  available, the full baseline package plus the latest cumulative patch are
  required.

The Zibo RSS feed is an update index, not an aircraft payload manifest. It can
identify available full and cumulative packages and their source links, but the
actual aircraft ZIPs still need staging and validation before any live file is
changed.

Current implementation:

- Reads local Zibo version from custom toolkit metadata, `version.txt`, or the
  legacy Lua version fallback.
- Refreshes the Zibo feed.
- Computes a full or incremental plan.
- Imports user-selected ZIP files only when their file names exactly match the
  current package plan.
- Attempts direct ZIP download into the aircraft update cache when the source
  exposes a readable ZIP stream.
- For `.zip.torrent` links, tries the matching `.zip` candidate first and falls
  back to manual import when direct download is not possible.
- Records cached package size and SHA-256.
- Opens cached ZIPs for dry-run.
- Applies cached ZIPs transactionally after a clean dry-run.
- Restores the latest aircraft-update backup generation.

Cached aircraft update ZIP dry-run reports planned add/replace operations,
preserves local preference/config files and toolkit metadata, blocks unsafe,
path-traversing or unreadable ZIP entries, and does not write into the aircraft
folder.

Aircraft update apply must:

- block while X-Plane is running
- reject missing or changed cache entries
- verify cached ZIP size and SHA-256 against the local cache snapshot
- run an internal dry-run before writing
- create backups for replaced files
- track files added by the update so restore can remove them
- preserve protected local preference/config files
- write `xplane-737ng-maintenance.json` after a successful apply
- record installed aircraft update family, version, mode and package list
- roll back changed files on failure where possible

Official Zibo package hashes are not available from the feed. Until an
authorized upstream manifest exists, ZIP integrity is cache-snapshot integrity,
not official upstream authenticity.

Custom no-Lua ports should declare their own distribution state in
`xplane-737ng-maintenance.json` at the aircraft root. This metadata takes
precedence over upstream `version.txt` or legacy Lua version fallbacks and
applies to both Zibo- and LevelUp-based ports. The upstream base version remains
separate from the custom distribution version so official upstream packages are
not treated as automatically installable over a custom port.
For custom distributions, official upstream package information is review-only
unless a dedicated custom-port update source is defined.

The update planner is intentionally family-agnostic. LevelUp can later provide
a different index source, package naming parser or release API while reusing the
same baseline/cumulative planning and transaction layers if that distribution
model fits.

## VeloPack Releases

VeloPack handles app installation and app auto-update only.

Expected app release channels:

- Active beta channel for review/testing builds.
- Stable public channel once release policy, signing and ownership are settled.

Expected platform artifacts:

- Windows x64 setup, optional portable.
- macOS package and portable output for the supported architecture set.
- Linux AppImage.

Signing/notarization should be handled in CI once credentials are available.

## CI/CD

GitHub Actions currently provide:

- Build.
- Tests.
- Package app.
- Upload manual VeloPack workflow artifacts.

GitHub Actions should later provide:

- Sign artifacts.
- Notarize macOS artifacts.
- Create/update GitHub Release.
- Publish VeloPack releases.
- Publish or validate content releases.
- Verify checksums and manifests.

Content-release workflow and app-release workflow may be coordinated but should
remain technically separate.

## Tests

Patch core must be testable without GUI.

Required tests:

- LF and CRLF targets.
- Repeated install.
- Update older marked version.
- Known legacy hooks.
- Missing anchor.
- Duplicated anchor.
- Partial/corrupt install.
- Foreign modifications.
- Payload hash mismatch.
- Manifest schema migration.
- Path traversal rejection.
- Backup creation.
- Restore after aircraft update.
- Rollback after write failure.
- Uninstall own blocks only.
- Windows/macOS/Linux path behavior.
- App update separate from content update.

## Out Of Scope For Initial Release

- General full aircraft/package manager behavior beyond the explicitly planned
  Zibo baseline/cumulative update flow and future authorized LevelUp extension.
- Auto-restarting X-Plane.
- Telemetry by default.
- Editing unrelated LevelUp/Zibo files.
- Distributing complete aircraft or complete modified Lua files.
- Arbitrary unsupported patching.
- Native integration into LevelUp/Zibo.

## Open Decisions

- Official GitHub organization/repository ownership and transfer/fork path.
- Final app name and branding.
- Official vs semi-official status.
- Final signing/notarization policy.
- License/status of VNAV content package.
- Supported LevelUp versions.
- Supported platforms and CPU architectures for signed public releases.
- Signing and notarization budget.
- Stable release-channel policy.
- Support/issue workflow.
- Whether content and app releases are published together or separately.
- Whether Zibo or LevelUp will provide official package manifests and hashes
  for aircraft update ZIPs.
- Whether full diagnostic export belongs in the first stable release.
