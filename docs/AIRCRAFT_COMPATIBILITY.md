# Aircraft updates and patch compatibility

An aircraft update can replace scripts used by several patches. A patch that
installed successfully on the previous aircraft is not automatically approved
for the next one. The catalog records which combinations have been checked.

This contract uses **catalog schema 3**. It does not change the numbering of
compatibility-package or tool-package manifests. Clients that only understand
catalog schema 2 reject the new document.

## Catalog contract

`aircraftCompatibility` is required in a schema-3 catalog. Its `contractVersion`
is `1`, and `defaultDecision` must be `unverified`.

`baselines` describe original upstream files. Each baseline has an `id`,
`productId`, `aircraftVersion` and `zibomodVersion`. The two version fields are
separate: a LevelUp release does not identify a separately installed Zibomod
update. `upstreamPackages` lists the tested archives in installation order;
`files` lists the relevant original files. Both use `path`, `size` and `sha256`.
Archive paths are filenames; file paths are relative to the aircraft folder.
`alternativeUpstreamPackages` may list other explicitly approved archive
sequences that produce the same baseline. For example, LevelUp's full package
plus cumulative patch and the cumulative patch alone have different archive
lists but must produce the same checked files. Archive order, names, sizes and
hashes must match one complete sequence; prefixes and arbitrary mixtures do not
qualify. There is still only one baseline identity for the resulting files.
Include the native runtime binaries, XLua init file and every affected original
script in the baseline evidence. A version label is not sufficient evidence.

`artifacts` identify exact patch and runtime releases. Each has an `id`,
`packageId`, `packageVersion`, `releaseTag`, `channel` and `archive`. The archive
uses the same filename/size/hash fields as an upstream archive. An optional
`manifestSha256` binds the source manifest as well. Runtime artifacts require
that hash and a `files` list with every non-protected manifest file relative to
the component target. This lets the Toolkit qualify older managed runtime state
without adopting an unowned copy. The recorded file list must match exactly,
and its previous runtime backup must also be recognized. Channels are `stable`
and `beta`. If an asset is replaced under the same release tag, its approval no
longer matches.

`approvals` qualify a complete selection, not individual patches in isolation:

```json
{
  "id": "example-only-do-not-publish",
  "baselineId": "tested-original-aircraft",
  "modules": [{ "moduleId": "vnav", "artifactId": "tested-vnav-release" }],
  "runtime": "upstream",
  "platforms": ["win-x64", "osx-arm64"],
  "decision": "unverified",
  "validationLevel": "beta",
  "reason": "Simulator checks are still open",
  "reviewEvidence": "",
  "lifecycleEvidence": "",
  "simulatorEvidence": ""
}
```

`runtime` is `upstream` or an artifact ID for a managed aircraft component.
`decision` is `approved`, `unverified` or `blocked`. Only `approved` permits a
new operation. It requires review and lifecycle evidence. A `regular` approval
also requires simulator evidence. A `beta` approval may leave simulator evidence
open; that does not establish flight behavior or performance.

Every required group module must be included. Optional modules remain optional.
The selected module IDs and artifact IDs must match exactly. An approval for A+B
does not approve A by itself. Two records for the same baseline, selection,
runtime and platform are rejected, even if their decisions agree. Existing
manifest release restrictions, dependencies and conflicts still apply.

When several release combinations are retained, `selectionPriority` explicitly
chooses the update target; the highest value wins. It defaults to zero. A tie
blocks resolution and needs a catalog correction. There is no implicit version
comparison or GitHub-latest preference. Repair keeps the recorded exact tuple.

## Preparing and applying an update

The GUI obtains a current online catalog, finds the approval for the selected
target and resolves its exact release tags. It does not substitute GitHub's
latest release. Unselected sources are not needed to prepare an approved group.
The retained source archives are checked again when planning a local import;
editing the generated manifest cannot establish release provenance.

For an aircraft with managed patches or an aircraft runtime, the shared core
requires a joint update bundle. A direct base-only update is blocked. Plain
official Zibo installations without these additions keep the ordinary update
path.

The transaction copies the aircraft into a private workspace on the same
filesystem. It verifies existing ownership and backups, removes the old managed
patches in that copy, installs the upstream package, verifies its baseline and
applies the approved selection. Retained runtime files are checked against their
recorded state. Aircraft scripts are never taken from a runtime package.

Before activation it checks the active files, state, archives, catalog and
X-Plane process again. It then swaps the directories and saves the projected
aircraft state. A journal covers the interval between those steps. A failed
activation restores the complete previous directory and state. Startup recovery
uses that journal and does not need an online catalog. Unknown changes during
recovery are retained and block automatic recovery.

The transaction replaces only the affected aircraft's state entries. It does not
replace other aircraft or tool history with an old global snapshot. New backup
files remain private until the operation commits; earlier backup files are not
rewritten. A mutation lease prevents patch, component, move and aircraft update
transactions from writing concurrently.

## Repair, uninstall and restore

Each installed qualified component records the catalog hash/version, approval,
baseline, exact modules, runtime, platform, validation level and original baseline
file identities. The prepared package path is retained for verified offline Repair. Repair of the
same verified generation may use its matching cached contract. A new selection,
release or aircraft update requires an online catalog checked within 15 minutes.
The timestamp is not serialized into the cache. The CLI checks its supplied
schema-3 catalog against the current online document before applying it. Runtime
release checks use the approved exact tag; verified cached manifests and archives
can supply the recorded runtime for Repair.

Uninstall and Restore retain their ownership, backup and dependency checks. They
do not require a new online compatibility approval. Patch Restore must not write
an older baseline's original script into a newer aircraft. A joint aircraft
Restore restores the previous aircraft, patches and tool state together. Later
file or ownership changes block this Restore instead of discarding those changes.
Runtime Restore preserves the currently protected aircraft scripts. A runtime
Restore that would invalidate installed qualified patches is blocked.

Standalone installations remain supported outside the Toolkit. This contract
does not adopt them or turn equal hashes into ownership evidence. Their verified
handoff rules remain separate.

An older managed single patch needs a verified migration into its catalog group
before it can participate in a new release combination. Incomplete release
metadata cannot make an active patch disappear from the approval check. An
aircraft update also blocks owners that no longer map to the catalog group;
the user must restore or remove those components separately. Known members can
still be deselected explicitly.

## Rollout

MTK 0.29.0 supplies schema-3 support. Catalog 2.0.0 promotes the reviewed
`catalog/content-package-catalog.next.json` into the live catalog source:
Zibo 4.05.35 and LevelUp 2.S1.51C with Zibomod 4.05.35. This carries forward the
maintainer's existing compatibility acceptance; it does not require the current
fleet to be qualified again. Beta releases retain their Beta status. See
[the baseline record](validation/current-aircraft-compatibility.md) for the exact
artifacts, covered selections and limits of the test evidence.

Do not treat unit tests as simulator evidence. Historical schema-2 behavior is
retained. Once an installation has a schema-3 receipt, a schema-2
catalog cannot authorize new patch, runtime or aircraft changes to it.

Release the client support before publishing schema 3, set the new catalog's
`minimumToolkitVersion` to that client release, and retain the existing ownership
policies. Subsequent approvals require catalog updates rather than new app builds.
Previously released clients can still fall back to their older cached catalogs;
this change cannot retroactively add enforcement to their executables.

A newer release appearing in the upstream feed does not revoke an approved
installed baseline. Changing a patched aircraft to an unapproved archive set is
blocked. If a user replaces its version file, native plugin or original scripts
manually, the changed baseline also blocks patch operations. Updating a catalog
approval must be a deliberate decision, not a side effect of `releases/latest`.

Separate LevelUp Zibomod-only updates and dynamic version-file updates are a later
feature. Their proposed scope and transaction are documented in
[LevelUp Zibomod updates](LEVELUP_ZIBOMOD_UPDATE.md). They are not enabled by the
current catalog.
