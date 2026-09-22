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
catalog release. Catalog 1.10.0 enables the XLua 2 prerelease on the Optimized
XLua beta channel while Stable continues to resolve XLua 1.3.7r5. Its
channel-specific schema contract requires Toolkit 0.17.0. The LevelUp group
keeps VNAV, FANS CDU and Weight & Balance required; Calculator, AUTO JETWAY and
CPDLC FANS PAGES are optional. See `../docs/CATALOG_GROUPS.md`.

Optional livery entries use category `livery` and distribution kind
`gitHubLiveryRelease`. Their release manifest uses schema 1 and package type
`livery`; the declared ZIP root and target directory must match. Livery support
is backward compatible because existing catalog entries require no new fields.

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
