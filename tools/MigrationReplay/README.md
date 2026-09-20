# Patch lifecycle reproduction

This is a diagnostic replay, not a release acceptance test or simulator test.
It invokes the production patch planners, patch engine, aircraft delta installer
and restore operation. It does not edit the generated `state.json` files.

Inputs are local, unmodified packages; the first argument must be a new output
directory. Only that directory is modified. The baseline ZIP is a subset of the
published S1.50 full aircraft containing its original ACF, FANS object/textures,
FMS script and tablet script. The actual complete 1.51C delta archive must be
beside its manifest. Its size and SHA-256 are checked before applying it.

```sh
dotnet run --project tools/MigrationReplay --configuration Release -- \
  /absolute/new-output-directory \
  /absolute/baseline-subset.zip \
  /absolute/extracted-fans-package \
  /absolute/extracted-performance-package \
  /absolute/prepared-catalog-group \
  /absolute/aircraft-delta.manifest.json
```

Scenarios begin by installing the actual standalone performance and FANS
packages, then validating that migration into the required group is possible.

- `linear`: apply the aircraft delta, then migrate.
- `refresh-first`: update the first standalone patch again before the delta;
  the update must succeed without changing aircraft files.
- `clean-original`: replace the seven baseline input files with their exact
  original bytes before the delta. This models external file replacement,
  not the MTK full-baseline replacement operation.
- `clean-original-missing-backups`: same replacement, then remove only the
  patch backups created inside this isolated scenario; preserve the state file.

Every scenario records state snapshots, the tablet hashes and backup validity,
delta logs and group result. A blocked group attempt is checked for unchanged
tablet bytes and unchanged state. A successful result is followed by a repeated
update and restore to the original tablet bytes.

After the replay completes, isolate the delta's influence by restoring it with
the production operation and replanning the group:

```sh
dotnet run --project tools/MigrationReplay --configuration Release -- \
  --restore-probe /absolute/replay-output /absolute/prepared-catalog-group
```

This second command modifies only the generated scenarios and saves additional
logs. Keep the initial snapshots for the before/after comparison. The runner is
intended for disposable replay outputs, never an operational aircraft/state root.

Execute the two safe restored controls through apply, repeat and restore:

```sh
dotnet run --project tools/MigrationReplay --configuration Release -- \
  --complete-controls /absolute/replay-output /absolute/prepared-catalog-group \
  /absolute/baseline-subset.zip
```

Limitations: no GUI or flight test, no claim that this is the reporter's exact
history, and no assertion that an aircraft subset is a complete flyable install.
The concrete package identities and results belong in the accompanying report.

## Recovery of already-written legacy history

`--legacy-recovery /absolute/old-scenario /absolute/new-output /absolute/group`
clones a prior scenario which has completed the restore probe, relocates paths
and their lookup keys, and reinstates the captured post-delta state and file
preimages. It checks required group installation, unchanged repeat, optional
performance enable/disable and restoration of the official delta object. The
legacy state and original evidence remain unchanged.

`--restore-active-group /absolute/scenario /absolute/group /absolute/baseline.zip`
installs the group in an isolated completed replay, restores the aircraft delta
while that group is active, then applies/repeats/restores the group. It verifies
that the final cockpit object and tablet match the original baseline bytes.
These commands are exclusively for the disposable replay directories.
