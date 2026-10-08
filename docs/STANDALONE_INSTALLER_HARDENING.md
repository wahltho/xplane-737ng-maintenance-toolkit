# Standalone installer ownership checks

Installer and source validation was completed on 8 October 2026. Refreshed
standalone archives retain the existing patch versions and runtime payloads.
Catalog 1.17.0 adds the corresponding ownership evidence for MTK 0.28.0.

## Scope

The shared Python implementation is `tools/standalone_patch_guard.py`. Each
standalone archive includes a copy named `standalone_guard.py`, together with
`standalone-ownership.json`. The policy in that JSON comes from the MTK catalog;
the helper contains no list of patch identities or patch-specific markers.

Installer and packaging changes cover both VNAV repositories, FANS CDU, Tablet
Performance, Weight & Balance, Auto Jetway, CPDLC, VREF, Intentional Fixes and
GSE. The published GSE source is `X-Plane-LevelUp-GSE-Patch`, not the separate
`X-Plane-LevelUp-GSE` Filedump checkout. Its existing native transaction and
recovery mechanism stays in place. That checkout's unrelated development
files are outside the release scope. The 27K installer is owned by its own
project; its final countercheck
against the released MTK core remains separate.

No C# core, patch Lua, OBJ or texture source is changed by this work. Existing
MTK README/authoring-guide edits and GSE development changes are preserved.

## Installation methods

Standalone and MTK are both supported. Updates and removal stay with the owner
that created the installation. To switch, remove the patch through that owner
first. An identical patched file is not sufficient evidence for adoption.

The standalone guard reads the normal MTK state locations on Windows, macOS
and Linux, plus `MTK_STATE_PATH` when supplied. It does not change MTK state or
import MTK backups. A managed shared target also blocks standalone writes,
even when the installed MTK component has another package ID.

Native receipts remain in use for FANS CDU, Auto Jetway, CPDLC, Intentional
Fixes and GSE. Their originals must match the recorded backup hashes. Native
state format, schema and historical package identity are declared in the
contract; they are not special cases in the shared code. The
other installers now save original hashes, sizes, installed hashes and owned
block fingerprints under `.patch-ownership/<package-id>/receipt.json`.

For those older installers, extract the package outside the aircraft and pass
the aircraft root. Pre-copying runtime files would lose evidence of whether
the original file existed. Old installs without a complete receipt are not
automatically adopted; remove them using their original installer and backups.

## Checks and recovery

The guard checks reserved namespaces, companion files, ownership records,
original backups, case collisions, links and paths before an operation. It
rechecks target bytes, the receipt and MTK state before guarded writes and
again before accepting the result. Newly written files must match the saved
receipt. Existing installer checks still validate supported hooks and aircraft
baselines. The Intentional Fixes standalone baseline remains untouched .35.

Before a write, a per-package lock and durable journal preserve the previous
bytes and file modes. Exceptions restore those bytes, including native receipts
and backups removed during uninstall. A stopped process leaves the journal
and lock for recovery; another run must not silently replace that evidence.
X-Plane must be closed for writes and restarted afterwards.

The catalog includes each new receipt, lock and transaction path as standalone
evidence. Tablet's three copy targets accept only known public runtime hashes;
that allowlist does not grant ownership of an unrecorded installation.

## Keeping package sources synchronized

Run from the MTK repository, once per destination:

```text
python3 tools/sync_standalone_guard.py /path/to/patch-repository --package-id <catalog-package-id>
```

This copies source only. It keeps a destination's native-state adapter settings.
Refresh package checksum metadata afterwards. In VNAV pipe manifests, use
`standalone` rows for installer/helper inventories and reserve `payload` rows
for aircraft files. MTK ignores the standalone rows; the Python installer
verifies both inventories. The release builders include the
helper and JSON contract; refreshing them is not a package build or release.

## Required tests before publication

For every installer, cover fresh install, repeat, managed update and uninstall;
existing native receipts; missing or damaged originals; changed owned blocks;
unknown/duplicate/incomplete markers; foreign companion files; and preservation
of unrelated edits and other supported patches. Test LF and CRLF separately.

Cover MTK single and group ownership in both directions, including shared
targets, moved aircraft, malformed MTK state, locks and partial transactions.
Inject failures during backup, target writes, receipt writes, native cleanup
and final verification. Compare restored bytes, state, backups and permissions.
Exercise Windows, macOS and Linux state locations and symlink/reparse protection.

Check the real assembled archives and their manifests, not just loose source.
Retain patch version numbers; compare runtime payload hashes with the previous
assets. Publish the updated catalog as a new catalog version before replacing
installer assets, so current MTK users recognize the new ownership evidence.
Do not move release tags or replace public assets until tests and publication
are separately approved.

## Validation completed

The existing MTK suite passed with 826 tests and six existing skips. The final
catalog, ownership, adapter and scope checks were also run separately. No C#
production source was changed.

The shared Python guard has 27 passing tests. They cover unowned or malformed
patches, native and generic receipts, missing or changed originals, MTK single
and group ownership, shared targets, concurrent changes, path and link safety,
LF/CRLF, and rollback after injected failures. Windows, macOS and Linux state
locations are fixture-tested on the Mac; this is not a native run on each OS.

Installer checks passed for all ten repositories:

| Source | Checks |
| --- | --- |
| Zibo and LevelUp VNAV | Install, repeat, uninstall, restored originals, LF/CRLF and missing-owner refusal |
| FANS CDU | 11 source/integration tests; final native-write change rechecked with the complete install/uninstall round trip |
| Tablet Performance | Existing lifecycle script, four packaging checks and metadata validation |
| Weight & Balance | Existing lifecycle script, temporary ZIP round trip, stock ACF contract, FMS delegation, 65 aliases and MTK contract |
| Auto Jetway | Eight installer tests across the supported baselines and two package tests |
| CPDLC | Nine tests, including a backed-up historical update and refusal of an unowned legacy install |
| VREF | Nine tests, including the MTK lifecycle fixtures and temporary package extraction |
| Intentional Fixes | Three standalone and two MTK-package tests on untouched .35 |
| GSE | 23 installer/package tests, including CLI locking, partial state and MTK group ownership |

Testing found and fixed wildcard retirement bookkeeping, missing historical
W&B evidence, the native Intentional Fixes identity mapping, and unjournaled
temporary writes in four native installers. A matching file hash still does
not authorize adoption of an unowned installation.

Lua, OBJ, textures and aircraft payloads were compared with each repository's
HEAD and are unchanged. Package versions are unchanged. These tests verify
installer behavior and recovery; they do not provide new simulator validation.
The VNAV test for missing ownership isolates script hooks from companion-file
evidence so filesystem iteration order cannot select a different refusal first.

## Before replacing public assets

Build each release archive from the approved source and validate its installer
inventory and checksums. Temporary test archives do not replace this step.
Publish the catalog with a new version so existing MTK installations can detect
the new standalone receipts, locks and journals. Complete the separate 27K owner
countercheck before replacing its assets.
