# Aircraft patch ownership validation — MTK 0.28.0

Date: 2026-10-08. Tested on macOS with .NET 10.0.105. All aircraft writes used disposable fixtures; operational aircraft and patch repositories were not modified.

## Automated checks

- Full .NET solution: 824 passed, 6 platform or volume-dependent cases skipped in the ordinary run.
- APFS volume runner: 9 passed, including the volume-dependent move and restore cases.
- Python catalog-generation tests: 5 passed.
- Release app build: no warnings or errors.
- `git diff --check`: clean.

The new tests exercise every catalog ownership policy, malformed and case-changed marker namespaces, matching unowned patch evidence, policy serialization, remote/cache transport, schema downgrade rejection, saved-policy restore, missing backups, changes after planning and rollback after final validation fails. A group test checks that an unselected standalone scope is preserved, while selecting that scope blocks without changing files or state.

The existing lifecycle tests still cover installation, update, repair, selection/deselection, uninstall, restore, source-component migration, known official baseline recovery, aircraft replacement and aircraft restore. Two old tests that adopted matching unowned outputs now require rejection. Directory targets and mutations added after review now require preflight rejection; separate final-validation tests retain actual rollback coverage.

## Public package probe

The production GitHub release adapter resolved and provisioned both groups. Archive sizes, release hashes, manifests and payloads were checked by the production adapter and package loader.

| Source | Release |
| --- | --- |
| Zibo VNAV | v0.2.0 |
| LevelUp VNAV | v0.2.0 |
| FANS CDU | v0.1.7 |
| Tablet Performance | v0.1.7 |
| Weight & Balance | v0.5.3 |
| Auto Jetway | v0.2.3 |
| CPDLC | v1.2.0 |
| VREF | v0.1.0-beta.1 |
| Intentional Fixes | v0.1.1 |
| GSE | v0.2.2 |

Both groups passed default-selection and all-module install/repeat/restore cases. Zibo's default selection is empty. Restore checks compared the complete fixture file list and all original bytes, rather than checking only one Lua script.

The fixtures copied the affected stock files and ACFs from the local LevelUp baseline source. The Zibo group used the shared stock Lua files with an explicit Zibo product variant. This is not a full official Zibo aircraft installation or proof of simulator behavior.

## Previous-version state

An isolated archive of commit `b011e456433ae4b8aeedcc1845e81867486b61b2` built the previous 0.27.0 Core. Its embedded baseline resource was assigned the explicit logical name expected by that Core; no C# source behavior was changed. It installed actual provisioned packages and generated state and backups normally.

The new Core consumed those unchanged states: Zibo VNAV with five managed files and LevelUp's three required modules with thirteen managed files. Both passed reuse, unchanged repeat and restore to the original hashes/absence. No ownership record, output hash or backup was invented to make the cases pass. The LevelUp case additionally retained a standalone GSE receipt and foreign GSE file in the separate unselected scope.

## Limits

This release verifies MTK-side ownership and restore boundaries. Public standalone installers require their own checks before writing into an MTK-managed installation. Their changes are separate work in the patch repositories. Older MTK applications cannot receive this protection through a catalog update alone.

No simulator flight validation was performed. VREF's Beta status and any pending simulator validation of other patches remain unchanged.
