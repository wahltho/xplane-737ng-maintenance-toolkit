# Catalog-controlled maintenance groups

Available in Toolkit 0.13.0 with content catalog 1.6.0.

A `catalogGroup` entry defines product support and references existing package
IDs. Each member declares its module ID, required/optional policy, installation
order, release asset pattern, source format and manifest location. Patch
repositories remain the only published sources. There is no additional bundle
archive, bundle repository or bundle release to maintain.

The LevelUp group requires VNAV descent tables, FANS CDU and Weight & Balance.
Tablet Performance Calculator and AUTO JETWAY remain optional. Zibo retains
its existing product-specific entries; the group implementation is not tied to
LevelUp in code.

## Resolution and execution

The resolver reads current stable GitHub release metadata and fixes each tag
and archive hash for one operation. It downloads and verifies the original
release archives before planning any aircraft changes. Legacy VNAV,
declarative schema 2, compatibility schema 3 and the existing module-source
format are adapted to the existing compatibility planner. VNAV uses its existing
transaction rewrite code, including marker migration, rather than new hooks.

A local preparation directory is a cache, not another distributed product.
Its internal identity hashes the catalog group definition and resolved release
metadata. The installation state records source package IDs, module IDs, tags,
repositories and archive hashes. Ordinary upstream patch releases need no
catalog edit. Changes in membership, policy or source contracts do.

Required modules cannot be deselected. Optional selections already managed by
the Toolkit are carried into the group. Source product/release contracts,
payload sizes and hashes, paths, structural anchors and generated outputs are
validated before the existing engine writes the combined plan. A structural
check does not establish simulator compatibility of an arbitrary new release.

## Existing installations

The planner follows recorded installed-to-original hash transitions through
existing member backups. Every link must be verifiable; absent, corrupt or
inconsistent chains block migration without changing aircraft files. It does
not blindly choose the oldest backup or overwrite independent changes.

After the transaction succeeds, group state and removal of former constituent
ownership are committed in one state-file save. Existing backup files remain.
Individual patch install/restore actions cannot bypass a recorded group owner.
The VNAV maintenance entry point offers the complete catalog group operation.

Standalone installations without Toolkit backup records are not assumed to
have recoverable original state. Structural handlers still decide whether their
current files can be accepted. A pre-group snapshot preserves accepted existing
files; it is not evidence of an unpatched aircraft baseline.

## Verification

The Release test suite passes all 307 cases, including source policy/adaptation,
backup-chain migration, source updates, changed-file protection, rollback and
update-popup/settings behavior. All five Python tests also pass.

Real public archives were resolved, hash-checked and adapted. On a disposable
copy of original LevelUp 737NG Series_V2.S1.50A inputs, required-module install,
repeat install, adding/removing both optional modules and complete restore
passed. A separate replay installed all five modules individually, migrated
their recorded ownership to the group and restored the original files exactly.
See [validation evidence](VALIDATION_0.13.0.md) for scope and source versions.
Simulator runtime acceptance is a separate proof layer.
