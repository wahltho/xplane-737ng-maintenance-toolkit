# Content Package Catalog

This directory is the source of truth for package discovery metadata used by
the X-Plane 737NG Maintenance Toolkit. Package payloads and their manifests
remain in their owning repositories and GitHub Releases.

The catalog is deliberately independent from Toolkit application releases:

- `content-package-catalog.json` contains the current package entries.
- `content-package-catalog.schema.json` documents schema versions 1 and 2.
- immutable `catalog-v<catalogVersion>` GitHub Releases publish both files.
- the Toolkit validates a remote catalog before caching or using it.
- the last valid cached catalog and the bundled catalog are fallbacks.

Schema 2 adds `ownershipPolicies` for aircraft patches. Package IDs, reserved
markers, standalone receipts and payload paths are catalog data. The Core has
no patch-specific ownership registry. Each patch source needs a matching policy;
group members refer to that source's package and module IDs. Non-patch entries
do not need a policy.

A policy can exist without a download entry. The 27K/SFP policy currently serves
this purpose: it permits checking local development packages and detecting
foreign installations, but does not offer 27K/SFP for download or select it in a
group. Adding a policy does not publish a patch.

New clients retain the schema-2 bundled catalog when a remote or cached catalog
uses schema 1. They must not lose ownership checks during fallback. Older clients
reject schema 2 and may fall back to their own bundled catalog; updating the
catalog alone cannot retrofit protection into an old application.

Catalog 1.16.0 introduces these rules and requires Toolkit 0.28.0. Publish the
application before the new catalog. Do not replace an existing immutable catalog
release. Patch versions and release tags are unchanged by this update.

## Maintaining ownership policies

For a new patch, record its package/repository identity, products, module IDs and
all target paths. Then declare the filenames or marker namespace that distinguish
its output, including its independent installer's state files. Restrict known
original and result hashes to the exact files they describe. Identical output
hashes help recognize a patch; they do not authorize adoption.

Use an exact path, one wildcard in a filename, or `directory/**` for a flat
exclusive directory. A flat scope must have no subdirectories, symlinks or case
collisions. Reserve the full filename family for companion payloads, including
unexpected extensions. Namespace declarations list exact begin/end pairs and
any permitted non-marker comments; unknown, duplicate and broken blocks stop
the operation.

`originalFiles` describes recognized original files, including explicitly retired
inputs in a managed scope. `resultFiles` recognizes whole-file patch outputs.
Neither grants ownership. Keep recovery instructions short and specific, and
link the patch's own documentation. Standalone installation remains supported;
automatic transfer of its backups into MTK is a separate feature.

For the lifecycle and review matrix, see
[PATCH_INTEROPERABILITY.md](../docs/PATCH_INTEROPERABILITY.md).

Adding a package repository does not publish it in the Toolkit. A package is
visible only after an explicit catalog change, review, version increment and
catalog release. Catalog 1.13.0 adds one optional Intentional Fixes selection
to the LevelUp group and a Zibo group with no required patches. It requires
Toolkit 0.21.1 for the exact-text insertion handling used by the package.
Catalog 1.14.0 adds LevelUp GSE as an optional LevelUp group module from its
regular 0.2.0 release. Its schema-4 managed scopes require Toolkit 0.19.0 or
newer; the existing catalog minimum remains 0.21.1. Equipment positions still
need simulator feedback across all five variants.
Catalog 1.15.0 adds VREF tables (Beta) as an optional, initially unselected
module for Zibo and LevelUp, before Intentional Fixes. It requires Toolkit
0.21.2 for the schema-3 loader migration used by VREF 0.1.0-beta.1. The source
uses a regular GitHub release for discovery through `releases/latest`; its
version, display name and description identify it as Beta. Simulator
validation remains open.
Catalog 1.12.0 offers the Tablet Performance Calculator to
compatible Zibo installations as well as LevelUp, using package v0.1.7.
Catalog 1.11.0 enables the same-path XLua 2 opt4 prerelease on
the Optimized XLua beta channel while Stable continues to resolve XLua
1.3.7r5. Its schema-3 replacement contract requires Toolkit 0.18.0. The LevelUp group
keeps VNAV, FANS CDU and Weight & Balance required; Calculator, AUTO JETWAY,
CPDLC FANS PAGES, VREF tables (Beta), Intentional Fixes and LevelUp GSE are optional. See
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
