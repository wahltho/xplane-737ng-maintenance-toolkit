# 737NG Patch Interoperability Contract

This contract applies whether a package is installed by the Maintenance
Toolkit UI or by the standalone patch CLI. The UI is an orchestrator, not a
compatibility boundary.

## Current Target Matrix

| Package | Product | Shared Lua targets | Other targets | Standalone migration requirement |
| --- | --- | --- | --- | --- |
| VNAV descent tables | Zibo and LevelUp product-specific packages | `B738.a_fms.lua` | table payload | retain unique marked hooks; never restore the generic backup over later patches |
| LevelUp FANS CDU | LevelUp | `B738.tablet.lua` | cockpit OBJ and three textures | reverse only the owned Tablet replacements; full restore is permitted only for the FANS-owned visual targets |
| Tablet performance calculator | LevelUp | `B738.tablet.lua` | three module payloads | use a non-consuming marked insertion slot for the loader and remove only owned markers |
| LevelUp weight and balance | LevelUp | `B738.tablet.lua`, `B738.a_fms.lua` | three module payloads and semantic ACF contract | use the shared loader slot and remove only owned markers |
| AUTO JETWAY | Zibo and LevelUp | `B738.tablet.lua`, `B738.a_fms.lua` | none | classify structurally instead of requiring an unmodified whole-file hash; reverse only owned replacements |

No package may assume it is the only owner of a shared Lua target.

## Migration Status

| Package | Development version | Interoperability result |
| --- | --- | --- |
| LevelUp/Zibo VNAV | unchanged | existing unique markers and non-destructive install already satisfy the shared-target contract |
| LevelUp FANS CDU | 0.1.4 | Tablet verification/uninstall is block-owned; OBJ and PNG accept structurally identical upstream encodings |
| Tablet performance calculator | 0.1.5 | loader and page hooks are separate operations; standalone uninstall removes only its markers |
| LevelUp weight and balance | 0.3.3 | loader is a non-consuming marked insertion; existing replacement blocks remain reversible |
| AUTO JETWAY | 0.2.1 | whole-file fingerprints are diagnostic only; verify/uninstall operate on owned replacements |

These are source-development versions until their individual repositories are
committed and released. Existing public releases remain unchanged.

## Composition Rules

1. Every module has a globally unique ID, version, policy, installation order,
   supported product/release set, dependencies and conflicts.
2. Operations are applied in ascending installation order. Target array order
   is preserved within a module.
3. Every text insertion uses unique begin/end markers and a unique structural
   anchor. An insertion must not consume its anchor.
4. Every replacement owns an exact source block and exact installed block. A
   replacement must be reversible without restoring the complete target file.
   A replacement may additionally declare `legacyNewLines`, a list of installed
   blocks written by earlier releases of the same package. Exactly one such
   block, with neither the source nor the current installed block present, is
   upgraded in place to the current installed block. Legacy blocks must never
   equal or contain the source or current installed block.
5. Whole-file hashes may identify a known baseline, but they may not be the
   only acceptance criterion for shared text targets. Exact structural
   operations decide whether a modified pipeline remains supported.
6. Copy operations own only their declared destination. They must not remove
   unowned neighboring files.
7. Unknown, partial, duplicated or edited marked blocks stop the complete
   transaction.
8. LF/CRLF, UTF-8 BOM state and file permissions are preserved.

## Transaction And State Model

The compatibility package captures one exact pre-package generation for every
affected target. Every later module-selection change is rebuilt from that
generation and all still-enabled modules. This avoids stacking a new patch on
an unknown result.

The shared state records package version, enabled module IDs, original and
installed hashes, backup paths and operation time. Toolkit and standalone CLI
use the same state store and the same Core engine. Removing one module means
recomposing the remaining pipeline, not restoring that module's historical
whole-file backup.

Module source repositories remain independently versioned. End-user releases
may consolidate pinned module versions into one product compatibility package.
The consolidated package is also usable without the Toolkit GUI through
`XPlane737NGPatchCli`.

Typical standalone flow:

```text
XPlane737NGPatchCli inspect --aircraft-root <path> --package <package-directory>
XPlane737NGPatchCli apply --aircraft-root <path> --package <package-directory> --modules <id,id> --yes
XPlane737NGPatchCli uninstall --aircraft-root <path> --package <package-directory> --yes
```

Changing `--modules` on a later `apply` recomposes the complete managed
pipeline from its recorded original generation. `uninstall` removes the whole
compatibility package; `restore` restores the latest recorded package backup.

## Shared Loader Slot

Modules that only need to load a companion Lua file use
`insert-marked-block-v1`. The preferred Tablet loader anchor is the unique
`jit.off()` line. Each module declares its own markers and inserts after that
anchor. Because the anchor remains in place, later modules can use the same
slot. When several modules share it, their final order is deterministic from
the package operation order (later insertions appear directly below the
anchor).

Example payload:

```json
{
  "format": "insert-marked-block-v1",
  "name": "Example module loader",
  "beginMarker": "-- BEGIN EXAMPLE_MODULE DOFILE",
  "endMarker": "-- END EXAMPLE_MODULE DOFILE",
  "contentLines": ["dofile(\"example_module.lua\")"],
  "anchorLines": ["jit.off()"],
  "position": "after"
}
```

## Required Compatibility Tests

For each supported aircraft baseline, CI must cover:

- every module alone;
- all default modules together in declared order;
- every optional module added to the default set;
- repeated installation and repair;
- removal of each module while every other module remains byte-identical;
- complete uninstall back to the exact original generation;
- LF and CRLF inputs;
- missing, duplicate and modified markers;
- foreign changes outside owned blocks;
- upstream replacement followed by explicit compatibility reapplication;
- Lua syntax validation for every generated Lua target.

Pairwise source-hash allowlists are not a substitute for this matrix. They grow
combinatorially and cannot make independent whole-file restore operations safe.
