# Full-baseline patch-state recovery validation

Local validation on macOS arm64, 2026-09-17. Source fix after 0.13.6; no release
or simulator acceptance is claimed here.

## Defect and correction

Full baseline replacement swapped the aircraft directory but retained external
content ownership. The required-patch follow-up then compared official files to
an unrelated historical original/installed hash pair. The replacement now saves
content ownership with the directory backup and resets active ownership in the
same state-file transaction as recording the full update. Pending module choices
are separate from installed state. Full restore swaps the content state with the
files. No general hash-validation bypass was added.

## Automated coverage

`dotnet test tests/LevelUp.NavTableUpdater.Core.Tests --no-restore --verbosity quiet`:
336 passed, zero failed or skipped (330 existing, six new cases).

The new cases cover:

- Different historical copy-file original, both present and absent: full update
  at the same path, optional selection retention, reinstall, unchanged repeat,
  patch restore to the new baseline, full restore to the old generation, and
  patch restore to the historical original.
- Legacy per-variant ownership does not reappear after full replacement or patch
  restore.
- Standalone source selections survive full replacement and become group
  selections without migrating the old file ownership/backup chain.
- Activation failure leaves the original aircraft and state untouched.
- Full restore failure rolls back files without changing state; a legacy backup
  without a snapshot cannot restore over managed/pending patch state.

Existing tests also cover preservation of managed aircraft components, rejection
of unknown copy-file changes, missing/corrupt original backups, and update rollback.

`python3 -m unittest discover -s tests -p 'test_*.py' -q`: five passed.
`git diff --check`: clean.

## Replay with released patch payloads

The isolated replay used cached VNAV v0.2.0, FANS CDU v0.1.6 and W&B v0.5.3
payloads, with seven original input files extracted from the published S1.50
full archive. A small test-only full archive contained those input files; this
was a focused engine/filesystem replay, not another complete-aircraft UI test.

After a successful initial patch installation, the harness injected a different,
matching original-DDS/backup pair into the historical state, then placed the
official DDS in the aircraft folder. This models the conflicting historical
state; it does not identify Jochen's exact earlier DDS or claim the current
FANS manifest accepts arbitrary changed DDS inputs.

Results:

1. The historical state reproduced `Managed target changed after installation`.
2. Full baseline replacement at the same path succeeded.
3. All three required modules installed successfully.
4. Repeating the patch update succeeded without changes.
5. Patch restore returned the official DDS, SHA-256
   `878e1b0976d9ba6d7aed746db25147c91232a7460b36dd9d730892c40de65979`.
6. Full-directory restore returned the historical patch ownership together with
   the backed-up files. Its original mismatch remained blocked as expected.

Local harness and raw output:
`/Users/wahltho/dev/mtk-baseline-state-20260917/harness/Program.cs` and
`/Users/wahltho/dev/mtk-baseline-state-20260917/results.txt`.
No operational aircraft, user Toolkit state, remote installation, or release was
modified. Windows packaging and simulator runtime remain outside this validation.
