# Compatibility Package Contract

Toolkit 0.11.0 introduces schema 3 compatibility packages for product-owned
aircraft adaptations that must be versioned, tested and released together.
This is separate from aircraft releases, VNAV-only legacy packages, optional
X-Plane tools and downloadable resources.

## Package Model

One package has one `packageId`, `packageVersion`, supported aircraft release
set and transaction state. It contains any number of ordered modules. Adding a
new module does not require a Toolkit code change when it uses a registered
declarative patch operation.

Module policies are:

- `required`: always selected and cannot be disabled.
- `recommended`: controlled by the package owner and normally enabled by
  default for the supported public configuration.
- `optional`: disabled by default and requires explicit user selection.

Dependencies are included automatically. Conflicting selected modules block
the complete plan. The Toolkit always rebuilds the selected module pipeline
from the exact original backup, so a module can later be enabled or disabled
without stacking an unknown patch on top of an older result.

## Directory Layout

```text
package-manifest.json
modules/
  vnav/
    patch.json
  fans-cdu/
    patch.json
  efb/
    patch.json
  flap-gauge/
    patch.json
```

Payload paths in each module are relative to that module's directory. Every
payload declares its exact size and SHA-256. Aircraft target paths remain
relative to the structurally validated product root.

## Manifest Shape

```json
{
  "schemaVersion": 3,
  "packageType": "compatibilityPackage",
  "packageId": "levelup.737ng.compatibility",
  "packageVersion": "1.0.0",
  "repositoryUrl": "https://github.com/example/levelup-compatibility",
  "aircraftFamily": "LevelUp 737NG Series",
  "supportedProducts": ["levelup-737ng"],
  "restartRequired": true,
  "supportedUpstreamReleases": ["V2.S1.50"],
  "modules": [
    {
      "moduleId": "fans-cdu",
      "displayName": "FANS CDU",
      "description": "LevelUp FANS CDU integration.",
      "policy": "recommended",
      "defaultEnabled": true,
      "installationOrder": 20,
      "requires": [],
      "conflictsWith": [],
      "payloads": [
        {
          "path": "patch.json",
          "size": 1234,
          "sha256": "<64 lowercase hexadecimal characters>"
        }
      ],
      "targets": [
        {
          "operation": "exact-text-replacements-v1",
          "payload": "patch.json",
          "relativePath": "plugins/xlua/scripts/B738.tablet/B738.tablet.lua",
          "sourceSha256": ["<supported input SHA-256>"],
          "resultSha256": "<expected result SHA-256>"
        }
      ]
    }
  ]
}
```

The package may contain several operations for the same aircraft target in
one or several modules. Module `installationOrder` and then target array order
define the pipeline. Each operation validates the output of the preceding
operation before it runs.

`copy-file-v1` installs a raw manifest payload at the declared target path. Its
`resultSha256` must equal the payload SHA-256. It can safely create a new file;
if a supported existing file may be replaced, its hashes are listed in
`sourceSha256`. This operation allows VNAV table payloads and other authorized
module-owned files to use the same transaction as structural Lua hooks.

## Transaction Rules

Before writing, the Toolkit validates package identity, supported product and
aircraft release, module policy, dependencies, conflicts, payload hashes,
target paths, source state and final generated hashes. It then creates exact
backups and writes the complete plan through the existing atomic content
transaction. A failure rolls back every file changed by that transaction.

Installed package version, enabled module IDs, original hashes, installed
hashes and backup paths are recorded per aircraft product installation. A
later package update or module-selection change is accepted only while every
managed target still matches its recorded installed state.

## LevelUp Release Policy

The schema supports the intended LevelUp package containing VNAV, FANS, EFB,
flight-control, weight-and-balance, flap-gauge and future modules. Which modules
are required or recommended remains a LevelUp package-owner decision and is
declared in the release manifest, not hardcoded in the Toolkit.

The existing VNAV and FANS package workflows remain available during
migration. No incomplete compatibility package is advertised in the bundled
online catalog until an authorized release exists.
