# Resource Packages

Resource packages are optional, product-scoped assets such as paintkits. They
are installed only into a user-selected directory outside X-Plane and never
participate in aircraft, VNAV, tool backup, or restore transactions.

The resource area is shown only when the trusted bundled content catalog has a
`resource` entry compatible with the selected aircraft product.

Version 0.5.0 bundles the first resource entry: the official LevelUp 737NG
Paintkit 1.1.0 from `petrolpram/737NG-Updates`. It is offered only for a
detected LevelUp product, uses the Stable channel and extracts the declared
`737NG V2_Paintkit` directory into a user-selected parent directory.

## Catalog Entry

```json
{
  "packageId": "example.paintkit",
  "displayName": "Example Paintkit",
  "description": "Optional paint templates.",
  "category": "resource",
  "activation": "explicitOptIn",
  "supportedProducts": ["levelup-737ng"],
  "repositoryUrl": "https://github.com/example/releases",
  "restartRequired": false,
  "installScope": "userSelectedDirectory",
  "supportedChannels": ["stable"],
  "distribution": {
    "kind": "gitHubResourceRelease",
    "assetNamePattern": "Example-Paintkit-*.7z",
    "manifestAssetNamePattern": "Example-Paintkit-*-manifest.json",
    "manifestSchemaVersion": 1
  }
}
```

Repository URLs, asset patterns, supported products, channels, activation, and
distribution type are trusted application data. Downloaded manifests cannot
change these boundaries.

## Release Manifest

The matching GitHub Release contains one 7z archive and one JSON manifest:

```json
{
  "schemaVersion": 1,
  "packageType": "resource",
  "packageId": "example.paintkit",
  "packageVersion": "2.1",
  "releaseTag": "resource-paintkit-v2.1",
  "channel": "stable",
  "repository": "https://github.com/example/releases",
  "supportedProducts": ["levelup-737ng"],
  "deliveryMode": "extract",
  "archiveRoot": "Example Paintkit",
  "targetDirectory": "Example Paintkit",
  "extractedSize": 42,
  "files": [
    {
      "path": "readme.txt",
      "size": 42,
      "sha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
    }
  ],
  "archive": {
    "fileName": "Example-Paintkit-2.1.7z",
    "size": 123456789,
    "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"
  }
}
```

The Toolkit requires exactly one matching manifest and one exact archive asset.
GitHub asset digests, manifest metadata, archive size, and SHA-256 must agree.

## Installation Lifecycle

1. Select a compatible detected product and Stable or Beta channel.
2. Choose the parent directory in which the resource folder will be created.
3. Stream the archive into a hidden temporary file in that directory while
   checking its declared size and SHA-256.
4. Reject archive traversal, symbolic links, special entries, duplicates,
   case collisions, unexpected roots, and files not declared by the manifest.
5. Extract into a hidden staging directory without requiring a local 7-Zip
   installation.
6. Verify every extracted file's size and SHA-256.
7. Move the complete verified staging directory to the declared final name.
8. Remove the temporary archive.

An unknown target directory is never overwritten. An installed resource that
contains missing, changed, or additional files cannot be updated or removed
automatically. An unchanged managed installation may be replaced by a newer
verified release using a rollback directory while the final directory is
swapped.

Cancellation or validation failure removes the temporary download and staging
directory and leaves an existing installation unchanged. The archive is not
retained after successful extraction.
