# Platform-restricted tools

Toolkit 0.14.0 adds optional `supportedPlatforms` to tool/aircraft-component
catalog entries and tool release manifests. Known identifiers are `win-x64`,
`win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64` and `linux-arm64`. An omitted or
empty list retains existing unrestricted behavior. Duplicate or unknown values
are rejected. A release manifest must declare exactly the same platform set as
its trusted catalog entry.

The selector filters by host OS and OS architecture. The release source and both
tool managers enforce the same restriction independently before network or
installation operations. This is not a cross-platform deployment mechanism.

Catalog 1.8.0 requires Toolkit 0.14.0. Earlier Toolkits retain their last valid
cached/bundled catalog when the new catalog is rejected.

## XLinSpeak publication order

Publish in this order so the bundled catalog never points to an unavailable manifest.

1. Generate `XLinSpeak-1.3.1-manifest.json` from the exact already published
   `XLinSpeak-linux.1.3.1.zip`, using `tools/create_mtk_manifest.py` in the
   XLinSpeak repository. Attach only the manifest to `r1.3.1`; do not replace the
   archive or rebuild the plugin. Archive SHA-256:
   `0c4793d39eb4e8d26e7da1a723a85e944466b2b204dad59ce88b7781eb1bd18f`.
2. Publish Toolkit 0.14.0 with bundled catalog 1.8.0.
3. Publish catalog 1.8.0 as `catalog-v1.8.0` after the manifest is available.

Future XLinSpeak releases generate the versioned ZIP and matching manifest with
`release.sh`. Piper, voice models and audio setup are separate prerequisites,
explained in the catalog description and user manual.

## Reproduce the archive integration test

Run from the Toolkit repository with the downloaded archive, generated manifest,
and a new evidence directory outside the repository:

```sh
dotnet run --project tools/ToolPackageReplay -- \
  catalog/content-package-catalog.json \
  /path/XLinSpeak-1.3.1-manifest.json \
  /path/XLinSpeak-linux.1.3.1.zip \
  /path/new-evidence-directory
```

The harness serves these exact files through an offline HTTP fixture to the
production release resolver and installer. It verifies install, current-version
detection, update of an unmanaged installation, repair, restore, hashes,
executable mode and preservation of user files. It simulates the manifest's
platform for filesystem operations and never executes the plugin. Automated
Core tests separately cover upgrades between managed versions and unsupported
platforms. Linux X-Plane/Piper speech output requires a Linux runtime test.

## Validation result (2026-09-20)

- Release configuration: 419 Core tests passed, including 25 new tests for
  catalog/platform restrictions, release resolution and tool lifecycle handling.
- Application Release build: zero warnings and zero errors.
- XLinSpeak manifest generator: six tests passed, including unsafe paths,
  wrong architecture, version mismatch and preservation of the original ZIP.
- `release.sh`: syntax check and isolated packaging smoke test passed; generated
  manifest matched its ZIP. No published archive was replaced.
- Exact XLinSpeak 1.3.1 archive replay: all 17 checks passed on macOS with
  Linux x64 selected in the test harness. This is not Linux runtime proof.
