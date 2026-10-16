# Updating Zibomod inside LevelUp

This is an implementation plan. The current Toolkit does not offer this
operation. The catalog candidate only records the currently accepted aircraft
and patch combinations.

## What the user would do

MTK would show the installed LevelUp version and the installed Zibomod version
separately. If an upstream Zibo release exists but has not been approved for
LevelUp, MTK would show that information and leave the current aircraft usable.
It would not offer an Apply button for that release.

Once approved, the user would review one operation: update the Zibomod plugin
and its original Lua files, keep the chosen XLua runtime, reapply the selected
patches, and update the compatibility version file. X-Plane must be closed.
One confirmation covers the complete operation and its full backup. The
LevelUp release number stays unchanged.

## Package boundary

Use a verified official Zibo archive as input. Do not copy its complete aircraft
or blindly replace LevelUp's entire `plugins` folder. The operation needs an
explicit file list containing:

- the Zibomod native plugin files for the supported platforms;
- the corresponding original Zibo Lua files under `plugins/xlua/scripts`;
- stock XLua runtime files only when the selected runtime is `upstream`;
- a newly generated root-level `LU & Zibo Version.txt`.

LevelUp's ACFs, cockpit objects, textures, sound files, its own Lua modules,
liveries, preferences and other plugins remain outside this change. A new
upstream file is not automatically eligible because it happens to be below
`plugins`. Retired files need explicit ownership and hash checks as well.

Replacing only `plugins/zibomod` is insufficient: the native plugin and its Lua
scripts belong to the same release. Conversely, a managed Optimized XLua install
must not be replaced by the upstream runtime. Its `init.lua` and native binaries
are retained and verified. Its protected `scripts/**` rule applies to runtime
package installation; it does not mean that an approved Zibomod update can never
update the aircraft's original scripts.

## Catalog additions needed later

Keep the current separation between an aircraft baseline, patch artifacts and
runtime artifacts. A future LevelUp baseline would identify the same LevelUp
release with a different Zibomod release and exact resulting file identities.
It needs its own LevelUp approval; approval on the Zibo aircraft is insufficient.

The plugin update additionally needs a declared operation type and a restricted
projection of the upstream archive: source file, destination file, size and
hash; explicit retirements; and the version metadata payload. The archive
identity must be checked before extracting anything. The current aircraft
updater must not interpret this as permission to apply the whole Zibo ZIP.
These fields are proposed, not active catalog fields in this change.

The generated version file must describe the resulting installation, for example
LevelUp `2.S1.51C` and a separately approved Zibomod version. Do not copy Zibo's
root `version.txt` into LevelUp, and do not take the version from a guessed
release-note line. Preserve the existing format used by CPFlight's integration.

The current `version-info` module is pinned to the S1.51C/4.05.35 backfill. A
future plugin update needs a matching metadata artifact and approval so that a
later group update cannot write 4.05.35 back over the new value. Version that
artifact by the resulting LU/Zibomod pair; it need not create a new LU release.

## Transaction and restore

Reuse the joint aircraft transaction rather than adding a second installer:

1. Verify the current baseline, all managed owners, source archives and backups.
   Unknown standalone edits block the operation with the existing patch-specific
   recovery guidance. Equal bytes alone do not establish ownership.
2. Make a private copy of the complete aircraft and retain a snapshot of the
   affected MTK state. Remove its managed patches in that copy through their
   verified backup chains. Keep the original active aircraft untouched.
3. Apply only the approved upstream file projection. Verify the resulting
   original scripts and native plugin against the new LevelUp baseline. Keep
   the selected managed XLua runtime and verify its exact files.
4. Apply the approved patch selection and matching version metadata in the copy.
   Validate the complete resulting files and ownership state. No user needs to
   judge whether the old hooks still fit the new Lua scripts.
5. Recheck the active aircraft, catalog, state, archives and X-Plane process;
   activate the prepared directory and projected state through the existing
   journal. An error restores the previous complete aircraft and its state.

Restore returns to the preceding LevelUp/Zibomod/runtime/patch generation,
including its version file. Later managed changes must be restored first;
unrecognized file changes block Restore. Unrelated global tool state is retained.

## Tests before enabling the feature

Use the existing current-version evidence as the starting point. Add tests for
the new projection, rather than another general patch test campaign:

- stock XLua and each supported managed XLua channel;
- required LevelUp modules alone and the existing shared-script combinations;
- source Lua and native files updated together, LevelUp-only files unchanged;
- copied, omitted, retired, unknown and tampered archive entries;
- version metadata updated together with the plugin and retained on later patch updates;
- mismatched current files, standalone evidence and changed backups blocked;
- failures during preparation, activation and state save, followed by recovery;
- complete Restore, including original scripts, runtime, metadata and MTK state.

The first real new Zibomod release also needs a focused LevelUp simulator check
of the changed interfaces and affected patches. Until that approval exists, MTK
keeps offering the current accepted combination. No automatic plugin update,
forced runtime change or mandatory patch selection is introduced by this plan.
