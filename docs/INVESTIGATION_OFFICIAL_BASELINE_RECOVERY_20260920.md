# Official aircraft files blocked by legacy patch ownership

Status: correction prepared for Toolkit 0.14.2; verified locally, not yet validated on the reporter's machine.

## Evidence

Jochen's complete `xplane-737ng-maintenance-log-20260920-093424.txt` reports Toolkit
0.14.1, installed aircraft S1.51C, and standalone FANS ownership blocking catalog
installation at `objects/737_cockpit_ovhd2.obj`. Current SHA-256 is
`a0b8681870c099fe4b770e22ecd659434cbcb3af71479e3f9d459d8810012d3d`.
That is the exact official S1.51C object hash, verified against the published
release manifest and the retained real aircraft file. The log alone does not
identify which old journal or backup record is absent; the reporter's actual
state document is not available.

The previous fix required a surviving verified aircraft-update preimage plus
an intact standalone original backup. That is insufficient to recover an
otherwise valid official file when the historical evidence is unavailable.
This is an MTK recovery defect, not a requirement to reinstall the aircraft.

## Reproduction

`tools/MigrationReplay --legacy-recovery` clones the retained 0.13.12 post-delta
state and actual released aircraft/package files into a new isolated directory.
Its optional fifth argument injects missing aircraft history, a missing or
corrupt old object backup, official baseline files with all old backups removed,
or an unknown object edit. Only the new clone is changed.

Before the production change, `missing-aircraft-history` reproduces the exact
reported path, current hash, owning package and disconnected-history rejection.
This establishes a reproducer for the defect, not the reporter's exact sequence.

Evidence directory: `/Users/wahltho/dev/mtk-recovery-20260920/`.

## Correction

`KnownAircraftBaselines.json` contains seven release-file records extracted from
the official S1.50 full and S1.51C delta manifests, intersected with the existing
maintenance group's target paths. It contains hashes, sizes and provenance, no
aircraft payload. Both manifest SHA-256 values were checked against the public
GitHub release asset digests before extraction:

- S1.50: `69821f3de2206f8897237a0bd10b21b6533e6c61b0c412fcfffa29a277e8943e`
- S1.51C: `0bd1813c6150014fbac0e4804efb4ef0394e037dc55bc95b17ed6e443441427c`

The metadata is embedded in the app. Neither the user's writable state/cache,
a version string, nor merely passing structural patch validation creates trust.
Each recovery requires exact product, relative path, byte size and SHA-256.
It also requires normal package and patch validation.

A verified current official file becomes the new per-file pre-patch baseline.
The existing transaction captures a fresh backup before writing and records the
ownership change only on success. Historical backups and journal entries are
retained. Restore returns that verified baseline, including S1.51C's changes,
rather than reverting to the old standalone patch's S1.50 original. The same
rule handles stale standalone ownership and an existing catalog group.

This is bounded recovery data for released historical states, not a new package
or a mandatory metadata update for every aircraft release. Modern aircraft
updates already reconcile ownership for the files they replace. Additional
historical hashes require independently verified release provenance.

## Validation

- Full automated suite: 431 passed, none skipped or failed.
- Regression cases cover standalone and catalog ownership; missing/corrupt old
  backups; planning without writes; install, unchanged repeat, optional selection,
  repair, uninstall and byte-exact restore; and transaction rollback on failure.
- Product/path/size/hash mismatches cannot establish an official baseline.
- Actual released-package replay succeeds with missing aircraft history and with
  a missing or corrupt original cockpit-object backup. Required VNAV, FANS CDU
  and W&B install, repeated update is unchanged, optional performance can be
  enabled/removed, and restore preserves the official delta object.
- Official baseline files with all old backups deleted also pass the released-package
  replay; all managed targets restore byte-for-byte to their newly verified originals.
- Historical backup integrity was checked against the original captured evidence:
  the only missing/changed file is the deliberately injected fault in its scenario.
- An unknown edit to that actual object blocks and leaves state/aircraft intact.

No simulator, operational installation or Windows host was used. Missing original
bytes for an unrecognized or already-modified file are not invented. Recovery
of such a file still requires valid independent evidence.
