# CPDLC installation fix — MTK 0.28.2

Date: 2026-10-08. Status: validated before publication.

## Cause

CPDLC 1.2.0 includes the comment `-- intentional fix: the stock LOAD compares
an undefined global here and never`. The Intentional Fixes catalog rule reserves
`INTENTIONAL FIX`, ignoring case. MTK 0.28.1 therefore rejected the CPDLC comment
during final validation and rolled back the installation. The failure was also
reproduced without 27K.

Changing only the CPDLC payload would leave existing installations with the
old comment. Changing only the live namespace would leave old saved namespace
rules in force. The fix adds a generic exact-comment exception to the ownership
contract, supplied by the catalog and retained in installation state.

## Checks

The full .NET suite passed: 849 tests, six platform or volume-dependent tests
skipped, no failures. The change adds 15 cases covering exact comment matching,
original backups containing the comment, damaged markers, conflicting rules,
serialization, saved rules across later updates, and direct-package context
without granting another patch ownership. Existing plan/execution/restore and
rollback tests remain in place.

Disposable aircraft checks use the published CPDLC 1.2.0 ZIP with SHA-256
`ec44908c8d20dd45aa19af30454594d4c8b8582e58d4aea82c802b0689b74a20`
and the already verified public Zibo and LevelUp group payloads.

For each product, the checks cover:

- All modules selected, including CPDLC, Intentional Fixes and 27K: install,
  unchanged repeat, repair, CPDLC deselection while retaining the other patches,
  re-selection, and complete original Restore.
- CPDLC selected without Intentional Fixes: install, repeat and Restore.
- An unchanged state produced by MTK 0.27.0 with CPDLC and Intentional Fixes:
  update into the current group, repeat and Restore.
- An unchanged state produced by released MTK 0.28.1 with Intentional Fixes:
  add CPDLC and 27K, repeat and Restore.
- All selected modules removed through Uninstall: original file list and bytes
  restored, no remaining component owner.
- Direct import of the public CPDLC package: install, repeat and Restore using
  the saved exception after it is removed from the live rule.

The historical states and backups were created by those older implementations,
not assembled or edited by the test harness. Restores compare the complete
fixture file list and SHA-256 hashes. Unrelated files are included in those
comparisons.

The released 0.28.1 Core rejects catalog 1.19.0's unknown field, even when no
caller version is supplied. It continues to accept the unchanged 1.18.0 cache.
Catalog package entries, membership, defaults and ordering match 1.18.0 exactly.
The only ownership-rule change is the exact comment exception.

## Limits and publication

The fixture inputs use the shared stock .35 Lua scripts and local LevelUp ACF,
OBJ and other affected files, with explicit Zibo/LevelUp product selections.
These are package, state and filesystem checks, not a simulator flight test or
a complete stock Zibo installation. CPDLC runtime and public assets are unchanged.

Publish MTK 0.28.2 before catalog 1.19.0. The new exception is catalog data;
future comment entries need no compiled patch registry. CPDLC remains 1.2.0.

Local evidence is under `/Users/wahltho/dev/mtk-cpdlc-namespace-verification`:
TRX results, replay programs/results, source hashes, original fixture inventories
and historical states. Existing unrelated README and authoring-guide edits were
retained byte for byte. These results were recorded before commit and publication.
