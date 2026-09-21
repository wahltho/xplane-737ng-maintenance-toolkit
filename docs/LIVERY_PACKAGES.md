# Livery Packages

The Toolkit discovers liveries through the content package catalog while the
payload and manifest remain in the livery's own GitHub repository and release.
A livery is always optional and is installed only after explicit confirmation.

## Catalog entry

```json
{
  "packageId": "publisher.product.livery.name",
  "displayName": "Example Livery",
  "description": "Example livery for supported aircraft variants.",
  "category": "livery",
  "activation": "explicitOptIn",
  "supportedProducts": ["levelup-737ng"],
  "repositoryUrl": "https://github.com/owner/repository",
  "supportedChannels": ["stable"],
  "distribution": {
    "kind": "gitHubLiveryRelease",
    "assetNamePattern": "Example-Livery-v*.zip",
    "manifestAssetNamePattern": "Example-Livery-v*.manifest.json",
    "manifestSchemaVersion": 1
  }
}
```

The new category and distribution kind are optional additions to catalog schema
1. Existing catalog entries do not need to change.

## Release manifest

The release manifest must declare:

- `schemaVersion`: `1`
- `packageType`: `livery`
- identity fields matching the catalog and GitHub release
- `supportedProducts` matching the catalog
- one or more unique, safe `supportedVariants`
- `installScope`: `aircraftLivery`
- identical safe values for `archiveRoot` and `targetDirectory`
- `restartRequired`: `true`
- one ZIP archive with exact size and SHA-256
- `files` relative to the archive root, each with exact size and SHA-256
- totals matching the declared file list

The ZIP contains exactly one declared root directory. Entries outside that root,
links, traversal paths, duplicate paths, undeclared files and missing files are
rejected.

## Lifecycle and safety

The Toolkit installs to `<selected aircraft>/liveries/<targetDirectory>`. State
is keyed by the normalized aircraft root and package ID, so the same livery can
be managed independently in several aircraft installations.

Downloads are verified before extraction. Extraction occurs in a staging
directory next to the final target. An update or repair moves the existing
managed directory aside, publishes the verified staging directory, records the
new state and then removes the rollback directory. A failure restores the prior
directory.

Normal install and update refuse to overwrite an unmanaged target or a managed
target whose files changed. Repair is a separate, explicitly confirmed action.
Removal also verifies the complete managed directory and stops if files were
changed or added.

Publishing a livery release alone does not expose it in the Toolkit. Add its
catalog entry only after the release assets and manifest are public and have
passed the Toolkit tests, then publish a new immutable catalog release.
