# Directory tool-package migrations

Directory-based tool and aircraft-component manifests use schema 1 for ordinary
replacement installs. Schema 3 adds a required `retiredFiles` contract for a
release that must remove specifically named files while preserving all other
protected and unowned local files.

```json
{
  "schemaVersion": 3,
  "layout": "directory",
  "targetPath": "plugins/xlua",
  "protectedPaths": ["scripts/**"],
  "retiredFiles": [
    {
      "path": "mac_x64/xlua.xpl",
      "optional": true,
      "sourceSha256": ["<known-xlua-1-sha256>"]
    }
  ]
}
```

Each retired path is relative to `targetPath` and must identify one regular
file. It cannot overlap a declared package file or a protected path. Paths and
hashes must be unique and pass the same traversal, symbolic-link and SHA-256
validation as ordinary package files.

`optional: true` allows the old file to be absent before migration, which is
appropriate for platform-specific runtimes. A present file is removable only
when its current hash is listed in `sourceSha256`, or the current Toolkit state
for the same package and installation root records that exact path, size and
hash as package-owned. The hash list may be empty when migration is intentionally
limited to previously managed installations. Any unowned file then blocks the
transaction.

The manager backs up the complete existing target directory before creating a
staged image. Retired files are excluded from the staged copy. Protected files,
including `scripts/**` for XLua, retain their exact bytes and file mode. The
stage must contain every declared new file with its expected size and hash and
must contain none of the retired paths before the directory is activated.
Activation uses a sibling directory swap. A failure after backup, staging or
activation restores the previous complete directory. X-Plane must be closed
during apply, repair and restore and must be restarted afterward.

## Status, repair and restore

The Toolkit persists the retired paths as an expected-absence contract. A
release is current only when all declared package files are valid and all
retired paths are absent. A reappeared retired file produces `RepairRequired`.
Repair can remove it after a new full backup only when the schema-3 manifest
authorizes its current hash. An unknown or modified file blocks repair.

Restore verifies the currently installed package-owned files and also requires
all current retired paths to remain absent. It then restores the complete prior
directory and its previous protected-path, retired-path and dependency state.
This restores the previous runtime, `init.lua` and protected aircraft scripts as
one generation.

## Stable and beta schemas

A package can keep a schema-1 Stable release while using schema 3 for Beta. The
trusted catalog binds the accepted schema to each channel:

```json
"distribution": {
  "kind": "gitHubToolRelease",
  "manifestAssetNamePattern": "Xlua.*-manifest.json",
  "manifestSchemaVersions": {
    "stable": 1,
    "beta": 3
  }
}
```

Existing entries retain the single `manifestSchemaVersion` field. A distribution
must use exactly one form. Every supported channel must be covered by the
channel-specific form. Old Toolkit versions reject schema 3, so they cannot
partially install a migration package. A catalog that first uses
`manifestSchemaVersions` must also raise `minimumToolkitVersion` to the first
Toolkit release that supports this contract.

The production Optimized XLua catalog entry must not switch to the
channel-specific contract until the XLua 2 prerelease provides a complete,
matching schema-3 MTK manifest and archive.
