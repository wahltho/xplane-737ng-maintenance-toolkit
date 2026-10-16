# Current aircraft compatibility baseline

Prepared on 15 October 2026 for Catalog 2.0.0 and Maintenance Toolkit 0.29.0.

## Accepted baseline

The maintainer confirmed that the current aircraft and patch combinations are
the working starting point. The catalog carries that decision forward:

| Aircraft | Aircraft release | Zibomod |
| --- | --- | --- |
| Zibo | 4.05.35 | 4.05.35 |
| LevelUp, all five variants | 2.S1.51C | 4.05.35 |

`catalog/aircraft-compatibility-baseline.json` records exact archive sizes,
SHA-256 values, source manifest hashes and release URLs. These were read from
the existing public releases and checked against the downloaded patch assets.
The LevelUp file identities come from its published S1.50 full manifest and
S1.51C cumulative manifest. The Zibo identities come from the original full
tree overlaid with the original 4.05.35 cumulative ZIP; update files take
precedence. No simulator installation or user state was changed to obtain them.

Baseline checks include the native Zibomod binaries, stock XLua runtime and
stock Zibo Lua scripts. LevelUp also includes its five ACFs and the original
cockpit assets used by FANS CDU. Zibo includes `version.txt`. LevelUp's generated
`LU & Zibo Version.txt` is a managed metadata module rather than an upstream
identity file: old copies have stale contents and some original packages omit
it. A matching label alone cannot approve different native or Lua files.

Original LevelUp S1/2.S1.0 and S1.50 are recognized as update sources, with no
patch approval on those older file baselines. S1's identities were read from
the exact Git commit named by the published cumulative manifest. Its embedded
Zibomod is 4.05.08, not the 4.05.35 used by the current release.

The affected original files in the S1.51A and S1.51B Git tags match the S1.51C
baseline exactly. They therefore remain recognized without creating duplicate,
ambiguous baseline records. Their commits are recorded in the seed provenance.

The S1.51C target accepts three explicit archive sequences:
its cumulative patch, S1.50 full plus that patch, or S1-to-S1.50 plus that patch.
This does not authorize an unknown source installation or an arbitrary archive
combination. Zibo accepts its current cumulative ZIP alone or the official
full ZIP followed by that same 4.05.35 cumulative ZIP.

## Selections

The existing groups, ownership policies, module order and default selections
are copied without changes. Zibo has no required patches. LevelUp keeps its
required modules, including the version metadata backfill. Optional patches
remain optional and are not enabled by this contract.

The accepted current selections include stock XLua, managed Optimized XLua
1.3.7r5, and the retained XLua 2 opt4 and opt5 Beta releases. Opt5 is the preferred
Beta update target; a recorded opt4 Repair keeps opt4. XLua 2 still requires
X-Plane 12.4.4 or newer according to its own manifest. VREF, 27K and Lua Timer
retain the catalog's existing Beta classification. This does not promote them
to stable or close their open simulator/FPS checks.

The seed lists every accepted module and artifact explicitly. The generator
expands the optional selections into 2,048 exact tuples: 256 selections for
each product, with four runtime choices. Each tuple lists all four supported
platforms. Adding a module to a group without reviewing the seed fails
generation. An approval for these artifacts grants nothing to a later asset,
release tag, upstream version or runtime.

## Lifecycle checks

The earlier package and combination checks remain the evidence for the accepted
starting point. See [the ownership checks](ownership-0.28.0.md) and the existing
patch lifecycle tests. This work adds focused checks of the new version gate:

- known current versions remain installable when the feed offers a newer release;
- a changed version file, native plugin or original script blocks planning;
- a change between review and execution blocks execution before patch writes;
- an unapproved aircraft archive is blocked, while recorded Repair and Restore work;
- a previously managed schema-2 group updates with its original backup chain;
- explicitly approved alternative archive sequences keep full rollback behavior;
- recognized historical LevelUp sources can use a verified full replacement and Restore;
- the production parser accepts the complete candidate and rejects older clients.

The full solution suite passed 943 tests. Six platform-dependent tests were
skipped on this Mac, together with the opt-in external-archive theory described
below. All 21 focused current-baseline/version-gate cases passed.
Automated tests establish these filesystem, catalog and state behaviors, not
aircraft behavior in flight.

The same 943 tests passed with Catalog 2.0.0 promoted to the live source path.
Older lifecycle and UI fixtures explicitly use the schema-2 contract with their
synthetic aircraft and release data. Schema-3 compatibility, online/cache
transport and joint-update tests keep using schema 3. The production gates were
not relaxed to accommodate those older fixtures.

## Existing use

Regular approvals refer to the maintainer's acceptance of the already working
current baseline. No new simulator run or performance measurement is claimed
here. The Beta tuples deliberately leave simulator evidence open. A new Zibo
release will need a new decision based on its actual changes; its release
number does not inherit the current approval.

## Prepare and check

```sh
python3 tools/materialize_compatibility_catalog.py
python3 tools/materialize_compatibility_catalog.py --check
dotnet test LevelUp.NavTableUpdater.slnx --no-restore --verbosity quiet
git diff --check
```

Catalog 2.0.0 promotes the verified `.next.json` generation to
`content-package-catalog.json`, the catalog release workflow's source. The
minimum Toolkit version is 0.29.0. Publish that client before publishing the
catalog. The generator accepts both the reviewed 1.24.0 predecessor and its
promoted 2.0.0 output; a different source version still requires review. All 16
pinned public archive sizes and GitHub SHA-256 digests were rechecked for this
rollout. Later compatible Zibo releases can be approved through catalog updates
rather than another app build.

## Official Zibo full archive

The official feed's torrent was downloaded on 15 October 2026. The complete ZIP
was obtained through BitTorrent and independently checked against all 7,987
SHA-1 pieces, followed by a ZIP CRC check. Its identity is:

```text
B737-800X_XP12_4_05_full.zip
Size:   2093597956 bytes
SHA256: c02f79db689c47ef4213f60b564cb7310a62863dbf9050db856ea0a93f90ce10
```

The source torrent is
<https://skymatixva.com/tfiles/B737-800X_XP12_4_05_full.zip.torrent>;
its SHA-256 is
`f08517e665ce5021825c67e309f882df8b3bc671b59963c8d649978463e03c8d`.
All 24 catalog baseline files match the actual full-plus-4.05.35 archive pair.
The candidate now permits that exact pair as an alternative to the existing
cumulative-only route. It grants no approval to a different full ZIP or patch.

The small regression fixtures cover a same-version full reinstall with managed
patches, both stock and managed XLua, complete offline Restore, preferences,
local liveries, and a changed full or cumulative archive before activation.

The additional `ExternalArchive` tests use the actual pinned full and cumulative
ZIPs and the actual public patch payloads. They run the complete Zibo optional
selection through the production group resolver and joint transaction, once
with stock XLua and once with managed Optimized XLua 1.3.7r5. Their GitHub HTTP
responses come from the pinned local assets, with no live release lookup.
They are opt-in to keep the normal suite independent of a multi-gigabyte download:

```sh
MTK_ZIBO_TEST_ARCHIVES='/path/to/original-zibo-zips' \
MTK_ZIBO_TEST_ASSETS='/path/to/pinned-compatibility-assets' \
dotnet test tests/LevelUp.NavTableUpdater.Core.Tests/LevelUp.NavTableUpdater.Core.Tests.csproj \
  --no-build --no-restore --filter FullyQualifiedName~ZiboFullArchiveReinstallTests
```

The asset directory uses `owner--repository/release-tag/asset-name`, matching the
archives and manifests recorded in the seed. Each test writes a log and JSON
result there, then removes its isolated aircraft, state and backup directories.

Both external-archive cases passed on 15 October 2026: stock XLua and managed
Optimized XLua 1.3.7r5. Each installed all eight Zibo modules, reinstalled the
official full-plus-4.05.35 pair, verified the retained modules and runtime,
preferences and local livery, then restored the complete previous generation
offline. The final comparison covered file contents, modes, empty directories
and aircraft-specific state. These are filesystem lifecycle checks; no simulator
flight or FPS result is implied.
