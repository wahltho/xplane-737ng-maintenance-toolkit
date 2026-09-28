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
catalog release. Catalog 1.13.0 adds one optional Intentional Fixes selection
to the LevelUp group and a Zibo group with no required patches. It requires
Toolkit 0.21.1 for the exact-text insertion handling used by the package.
Catalog 1.14.0 adds LevelUp GSE as an optional LevelUp group module from its
regular 0.2.0 release. Its schema-4 managed scopes require Toolkit 0.19.0 or
newer; the existing catalog minimum remains 0.21.1. Equipment positions still
need simulator feedback across all five variants.
Catalog 1.12.0 offers the Tablet Performance Calculator to
compatible Zibo installations as well as LevelUp, using package v0.1.7.
Catalog 1.11.0 enables the same-path XLua 2 opt4 prerelease on
the Optimized XLua beta channel while Stable continues to resolve XLua
1.3.7r5. Its schema-3 replacement contract requires Toolkit 0.18.0. The LevelUp group
keeps VNAV, FANS CDU and Weight & Balance required; Calculator, AUTO JETWAY,
CPDLC FANS PAGES, Intentional Fixes and LevelUp GSE are optional. See
`../docs/CATALOG_GROUPS.md`.

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
