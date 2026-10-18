# Intentional Fixes 0.1.2 catalog qualification

## Package review

Catalog 2.0.3 adds the published schema-5 MTK source package for Intentional
Fixes 0.1.2. Its ZIP size is 58,470 bytes and its SHA-256 is
`7bfd3df26a75ee4e47a6525613f7bdb4e2069202a26ebdfa764365b34a62cef1`.
The embedded manifest SHA-256 is
`cb26f11ebe177eebd0abf92bf33fbf485f90befbb61e67c32eabce2e9fb81338`.
The package adds I37-I39 and revises I30. Both product modules remain optional
at order 70; CPDLC and W&B changes keep their existing conditions. Package
ownership rules, aircraft baselines and all other artifacts remain unchanged.

The existing 2,048 approval records are retained unchanged.
The catalog adds 1,024 records for selections containing Intentional Fixes,
replacing only its artifact reference with 0.1.2. Their priority is 20 above
the corresponding 0.1.1 record. Repair and Restore can therefore still identify
the previous generation, while new installs and updates prefer 0.1.2.

The new records use Beta validation: package and filesystem checks do not
establish simulator behavior. The release itself remains in its published
Stable channel. No new flight, FPS or full ALT INTV validation is claimed.

## Lifecycle checks

Checked with the published MTK Core 0.29.5, using hash-verified copies of the
Zibo 4.05.35 and LevelUp S1.51C baseline files. Public release packages were
resolved and provisioned through the production release adapter.

Four scenarios passed: each product with Intentional Fixes (plus required
modules), and each product with all available patch modules. All 36 checks
passed: install 0.1.1, Repair with the new catalog, update to 0.1.2, unchanged
repeat, Repair 0.1.2, deselection, byte-exact Restore, fresh install and
byte-exact Uninstall. Restore and Uninstall were compared against SHA-256
snapshots of every fixture file.

The focused catalog tests passed 267/267. The complete Core suite passed
1,098 tests, with eight platform-specific skips and no failures. The catalog
generator tests passed 2/2, release-boundary tests 23/23, and `git diff --check`
was clean. These are filesystem and automated test results, not simulator
validation.
