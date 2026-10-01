# LevelUp delta-only release feed

A delta-only `release-index.json` keeps the normal current release identity,
sequence and cumulative patch record. Add `baselineReleaseTag` pointing to the
published full baseline release (for S1.51C: `v2.S1.50`) and require Toolkit 0.13.8.
The referenced release index and full manifest remain in that release; do not
relabel the original full archive as the new aircraft version.

The Toolkit verifies both indexes and manifests, requires the full version to
match the delta baseline and its sequence to precede the new release, and rejects
nested references. Existing installs on the matching baseline download only the
delta. Fresh installs stage full then delta, including deletions, before activation.
An existing aircraft on a different baseline can use the same verified full-plus-delta
combination. This is a full replacement, not a delta applied to an unsupported
version: preferences and local liveries are migrated, and the complete previous
aircraft is retained for Restore. The confirmation lists the packages in order.
Newer local releases are not downgraded automatically. An unknown local version
requires an explicit full-replacement confirmation. Standalone-patch conflicts
remain blocked, and the local version and distribution are checked again before
the aircraft directories are swapped. No intermediate update archives are needed.
Required maintenance patches follow the successful LevelUp fresh installation.

Publish the new Toolkit first. Upload the checked release index and updated
SHA256SUMS.txt to the aircraft release, then mark that release Latest. Test the
default public URL with both an S1.50 update check and a fresh-install check.
