# Zibo ACF CG Catalog Builder

`tools/build_zibo_acf_cg_catalog.py` builds a reviewable known-CG catalog for
Zibo 737-800X packages.

The Skymatix feed exposes `.torrent` links, not direct `.zip` downloads. The
builder therefore separates package discovery from ZIP scanning:

1. Parse `https://skymatixva.com/tfiles/feed.xml`.
2. Download and parse each `.torrent` metainfo file.
3. Select packages according to the configured probing strategy.
4. Scan selected package ZIPs when they already exist in the cache or in a supplied
   `--zip-dir`.
5. Optionally ask `aria2c` to download missing ZIPs from the torrents.
6. Extract `.acf` files from scanned ZIPs and record:
   - package version and kind
   - package and ACF SHA-256
   - ACF path
   - `acf/_cgY`
   - `acf/_cgZ`
   - `acf/_version`
   - `acf/_file_writer_version`
7. Derive effective CG ranges from scanned packages. Zibo update patches are
   cumulative, so a scanned patch without an ACF inherits the full baseline ACF
   for that baseline version. A missing ZIP does not.

## Commands

Build a package/torrent inventory without downloading ZIPs:

```bash
tools/build_zibo_acf_cg_catalog.py \
  --pretty \
  --output artifacts/zibo-acf-cg-catalog.json
```

Probe only the full baseline and latest cumulative patch:

```bash
tools/build_zibo_acf_cg_catalog.py \
  --pretty \
  --strategy latest \
  --output artifacts/zibo-acf-cg-catalog-latest.json
```

Use midpoint probing to narrow CG change ranges:

```bash
tools/build_zibo_acf_cg_catalog.py \
  --pretty \
  --strategy bisect \
  --output artifacts/zibo-acf-cg-catalog-bisect.json
```

Scan already downloaded ZIPs from an extra directory:

```bash
tools/build_zibo_acf_cg_catalog.py \
  --pretty \
  --strategy exhaustive \
  --zip-dir ~/Downloads \
  --output artifacts/zibo-acf-cg-catalog.json
```

Try one torrent download through `aria2c`:

```bash
tools/build_zibo_acf_cg_catalog.py \
  --pretty \
  --download-missing \
  --download-kind cumulativePatch \
  --max-downloads 1 \
  --bt-stop-timeout 60 \
  --output artifacts/zibo-acf-cg-catalog-download-test.json
```

The default cache root is:

```text
~/Library/Caches/XPlane737NGMaintenanceToolkit/zibo-acf-cg-catalog
```

## Current Feed Shape

On 2026-07-19 the feed listed:

- `B737-800X_XP12_4_05_full.zip`
- `B738X_XP12_4_05_01.zip` through `B738X_XP12_4_05_35.zip`

The torrent metainfo was available for all 36 packages. The metainfo did not
contain web seeds, so direct HTTP ZIP download is not available from the
torrent files alone.

On the same date, the public Google Drive XP12 folder listed all 4.05 patch
ZIPs except `B738X_XP12_4_05_16.zip`. Patch `4.05.16` was retrieved from the
Skymatix torrent feed. The full package was available locally as an extracted
`B737-800X` reference folder rather than as a ZIP.

The verified Zibo XP12 ACF CG ranges used by the tool reference catalog are:

| Versions | `acf/_cgY` ft | `acf/_cgZ` ft |
| --- | ---: | ---: |
| `4.05.00`-`4.05.02` | -1.000000000 | 59.500000000 |
| `4.05.03`-`4.05.04` | -1.000000000 | 62.419998169 |
| `4.05.05`-`4.05.14` | -1.000000000 | 61.419998169 |
| `4.05.15` | -1.000000000 | 60.720001221 |
| `4.05.16`-`4.05.17` | -2.000000000 | 59.979999542 |
| `4.05.18`-`4.05.35` | -2.000000000 | 60.340000153 |

The 2K and 4K ACF files had identical CG values for every verified version.

## Status Values

- `notProbed`: the selected strategy intentionally did not scan that package.
- `missingZip`: feed and torrent metadata are known, but no complete ZIP is
  available locally.
- `scanned`: ZIP was scanned and at least one `.acf` was found.
- `scannedNoAcf`: ZIP was scanned and no `.acf` was found; for cumulative Zibo
  patches, effective ACF values inherit from the full baseline package.
- `invalidZip`: a local file exists but is not a valid ZIP.

Incomplete `aria2c` downloads leave a `.zip.aria2` sidecar. The builder ignores
those ZIP files until the sidecar disappears.

## Probe Strategies

- `latest`: scans only the full baseline and latest cumulative patch. This is a
  fast current-state check. It deliberately leaves intermediate history
  unresolved.
- `bisect`: scans baseline, latest and midpoints. It recursively halves ranges
  where sampled CG signatures differ. It can find exact boundaries with far
  fewer scans when changes are sparse. Same-endpoint ranges that were only
  sampled are reported as unresolved rather than guessed.
- `exhaustive`: scans every package. This is the only strategy that can prove
  every intermediate CG change or revert when all ZIPs are available.

The generated JSON records `probeStrategy`, `probedVersions`, `changeRanges`,
`unresolvedRanges` and `missingPackages` so downstream tooling can distinguish
known boundaries from unverified gaps.

## Safety Boundary

This catalog is a reference and diagnostic input only. It can tell the app that
a local ACF CG matches a known Zibo package version. It must not be used by
itself to authorize Quick View writes. Local toolkit baseline state, Quick
View/X-Camera hashes and explicit baseline adoption remain authoritative.
