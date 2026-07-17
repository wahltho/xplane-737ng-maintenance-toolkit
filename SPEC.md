# X-Plane 737NG Maintenance Toolkit / VNAV Descent Tables Installer

Product specification for the initial public app. The original LevelUp VNAV
updater scope has been widened into a neutral Zibo/LevelUp 737NG maintenance
toolkit while keeping VNAV content packages manifest-driven and separately
versioned.

## Goal

Build a small cross-platform installer/updater and maintenance tool for 737NG
VNAV descent table packages and conservative view-maintenance utilities.

The updater should allow users to safely install, update, verify, repair,
restore and uninstall the VNAV descent table hooks without manually editing
`B738.a_fms.lua` and without requiring Python.

VeloPack is the chosen application installer and update framework.

## Ownership

The updater application should live in a GitHub organization-owned open-source
repository, not under a personal account.

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

The first public app should stay deliberately small:

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
- Simple install log.
- Exportable diagnostic report without sensitive data.
- Clear "X-Plane restart required" message.

The app should not attempt to restart X-Plane automatically.

## Application Stack

Recommended stack:

- Current stable .NET LTS.
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

The app should be a single-page tool, not a marketing landing page.

The UI can move through these states:

### 1. Locate Aircraft

- Show detected X-Plane / Zibo / LevelUp installations.
- Provide a Browse button for manual aircraft folder selection.
- Validate the selected folder structurally, not by name only.

### 2. Scan Aircraft And Package

- Locate `plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua`.
- Load current local install state.
- Fetch or load package manifest.
- Verify payload metadata and checksums.
- Classify install state.

### 3. Show Options

- Installed package version.
- Available package version.
- Component list.
- Planned changes.
- Install / Update / Repair / Restore / Uninstall actions.

### 4. Apply Transaction

- Validate target.
- Ensure X-Plane is not running.
- Create backup.
- Prepare patched files in temp location.
- Validate result.
- Replace files.
- Write state/log.

### 5. Complete

- Success/failure status.
- Restart-required banner.
- Collapsed install log.
- Diagnostic export option.

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

Backups are automatic before any patching and cannot be skipped.

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
changed. The first implementation should support local ZIP selection/import
against the planned package list. Direct Google Drive or torrent download can
be added later behind the same source interface.

The update planner is intentionally family-agnostic. LevelUp can later provide
a different index source, package naming parser or release API while reusing the
same baseline/cumulative planning and transaction layers if that distribution
model fits.

## VeloPack Releases

VeloPack handles app installation and app auto-update only.

Expected app release channels:

- Stable public channel.
- Optional review/testing channel when needed.

Expected platform artifacts:

- Windows x64 setup, optional portable.
- macOS app bundle/package for Intel and Apple Silicon as needed.
- Linux AppImage.

Signing/notarization should be handled in CI once credentials are available.

## CI/CD

GitHub Actions should eventually provide:

- Build.
- Tests.
- Package app.
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

- Full aircraft/package manager.
- Auto-restarting X-Plane.
- Telemetry by default.
- Editing unrelated LevelUp/Zibo files.
- Distributing complete aircraft or complete modified Lua files.
- Arbitrary unsupported patching.
- Native integration into LevelUp/Zibo.

## Open Decisions

- GitHub organization and repository name.
- Updater app name and branding.
- Official vs semi-official status.
- License for updater app.
- License/status of VNAV content package.
- Supported LevelUp versions.
- Supported platforms and CPU architectures for the initial release.
- Signing and notarization budget.
- Release channel naming.
- Support/issue workflow.
- Whether content and app releases are published together or separately.
