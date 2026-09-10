# Catalog 1.7.0 validation

Validated on 2026-09-10 using the unchanged Toolkit 0.13.1 production engine.

## Added content

CPDLC FANS PAGES is optional for Zibo and LevelUp. The LevelUp group uses module
`cpdlc` at order 60 after AUTO JETWAY. Its three required modules are unchanged.
The catalog requires Toolkit 0.13.1, the published version with FANS legacy-block
upgrade support used by the current group.

CPDLC v1.1.0 archive SHA-256:
`aeb01abf263352b8ed8d0b25ecda727adf5f988b06cf8a75eb27b8f9e25a76ae`.
Its 15 operations comprise eight exact replacements in one payload and fourteen
marked insertions, all on the same FMS target. Version 1.0.0 was rejected by the
Toolkit integration test and was never published in the catalog.

## Filesystem integration checks

Disposable copies of original Zibo 4.05.35 and LevelUp V2.S1.50A files were used;
operational aircraft installations were not changed.

- Zibo: install, unchanged repeat, modified owned-block rejection without writes,
  exact uninstall, reinstall and exact restore passed.
- Original LevelUp V2.S1: structural rejection with target bytes unchanged passed.
- LevelUp: required modules, addition of all optional modules, unchanged repeat
  of all six modules, optional removal and complete byte-for-byte restoration
  of all original targets passed.
- Group releases: VNAV v0.2.0, FANS CDU v0.1.6, calculator v0.1.6,
  W&B v0.5.3, AUTO JETWAY v0.2.3, CPDLC v1.1.0. Original archives were resolved,
  downloaded and checksum-verified by the production catalog resolver.

## Limitations

An existing standalone CPDLC v1.0.0 installation must first be uninstalled with
its own installer before switching to Toolkit management. Direct adoption can
leave its old unmarked hooks; this is not the supported migration path.
Simulator/Hoppie-controller runtime testing remains pending. See the package's
RUNTIME_TEST_PLAN.md. Filesystem integration does not prove flight behavior.
