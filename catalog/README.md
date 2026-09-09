# Content Package Catalog

This directory is the source of truth for package discovery metadata used by
the X-Plane 737NG Maintenance Toolkit. Package payloads and their manifests
remain in their owning repositories and GitHub Releases.

The catalog is deliberately independent from Toolkit application releases:

- `content-package-catalog.json` contains the current package entries.
- `content-package-catalog.schema.json` documents schema version 1.
- immutable `catalog-v<catalogVersion>` GitHub Releases publish both files.
- the Toolkit validates a remote catalog before caching or using it.
- the last valid cached catalog and the bundled catalog are fallbacks.

Adding a package repository does not publish it in the Toolkit. A package is
visible only after an explicit catalog change, review, version increment and
catalog release. Catalog 1.6.0 defines a LevelUp group with VNAV, FANS CDU and
Weight & Balance required; Calculator and AUTO JETWAY are optional. It requires
Toolkit 0.13.0. See `../docs/CATALOG_GROUPS.md`.

## Publishing

1. Update `content-package-catalog.json` and increment `catalogVersion`.
2. Keep `minimumToolkitVersion` at the oldest Toolkit version that supports
   every declared package contract.
3. Run the complete test suite.
4. Push the reviewed change.
5. Run the `Content Catalog` workflow with the matching catalog version, or
   push the matching `catalog-v<catalogVersion>` tag.

Catalog releases are created with `--latest=false`, so `releases/latest`
continues to identify the current VeloPack application release.
