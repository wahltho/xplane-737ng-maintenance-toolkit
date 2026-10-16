# Aircraft patch ownership and interoperability

The Toolkit and each patch's independent installer are separate installation
paths. Both remain supported. Their state and backup formats are not assumed to
be interchangeable.

`XPlane737NGPatchCli` is different: it ships the Toolkit Core and uses the same
MTK state store as the app. Running this CLI without the GUI is still an
MTK-managed installation.

## Source of the rules

Catalog schema 2 contains an `ownershipPolicies` entry for every aircraft patch
source. The catalog names the package, repository, supported products, module
IDs, target paths, reserved markers, payload filenames and standalone receipts.
The C# verifier applies those rules without a list of known patch IDs in code.

The current scope covers:

| Patch | Products | Shared aircraft targets | Other evidence |
| --- | --- | --- | --- |
| VNAV / Descent | Separate Zibo and LevelUp packages | FMS Lua | Table and insertion payloads |
| FANS CDU | LevelUp | Tablet Lua | Cockpit OBJ and textures |
| Tablet Performance | Zibo and LevelUp | Tablet Lua | Calculator payloads |
| Weight & Balance | LevelUp | FMS and Tablet Lua | W&B payloads |
| Auto Jetway | Zibo and LevelUp | FMS and Tablet Lua | Exact installed code signatures |
| CPDLC | Zibo and LevelUp | FMS Lua | CPDLC marker namespace |
| VREF | Zibo and LevelUp | FMS and Calc Lua | VREF payloads |
| Intentional Fixes | Zibo and LevelUp | FMS Lua | Fix-specific signatures and comments |
| GSE | LevelUp | None | Exclusive object and script directories |
| 27K / SFP | Zibo and LevelUp | FMS, Calc and Tablet Lua | SFP payloads and receipt |

The 27K policy does not add a catalog download or a selected group member.
Compatibility with an aircraft release still comes from the package manifest;
a product in this table is not a promise that every release is supported.

A policy uses exact paths, a single filename wildcard, or a flat exclusive
`directory/**` scope. Marker rules list every accepted begin/end pair in the
reserved namespace. Case-changed markers are unknown markers. Unrecognized, repeated, unmatched or incorrectly nested
markers block the operation. Permitted ordinary comments are listed separately.
Known marker names alone do not authorize changed block contents.

## What proves ownership

A present patch needs a recorded MTK owner, verified current file hashes and a
complete, unambiguous path back to the original files or their recorded absence.
For a current single owner, the installed hash and verified original backup
provide a direct restore chain. For shared files, the verifier follows the
existing component and transaction backup records. Every backup used as evidence must pass its size and hash check.
An original containing the same patch's markers or installed signatures is not
accepted as an unpatched original.

An identical result hash does not prove ownership. A standalone receipt,
reserved payload or installed patch signature without a valid MTK chain blocks
MTK writes. This also covers a manually copied patch with no receipt. Unknown
files in exclusive scopes and foreign companion payloads block before mutation.

An optional patch in a separate scope is left alone when it is neither selected
nor already managed by the group. Shared files are still checked against all
patches that can affect them. Aircraft replacement checks the complete aircraft.

An independently verified official aircraft preimage can be backed up again
after a clean baseline replacement. This existing recovery route must not be
confused with adopting patch output as an original. Planning never modifies
historical backups to make a broken chain appear valid.

Older MTK components without a saved policy use the current catalog. Older group
states with module selections but no resolved sources are matched through the
catalog's group members. They must pass the same file and backup proof. A new
installation stores a copy of its own policy for later offline operations;
historical policies add checks rather than removing current catalog rules.

## Operation boundaries

1. Before building a plan, validate all declared package targets against the
   catalog. Capture the selected targets, existing owners that may be transferred,
   their aircraft files, policy, MTK state
   and backup hashes. Validate package identity and target coverage.
2. Classify the source files and build the patch plan. Compare its inputs with
   the captured evidence; changes during planning block the plan.
3. Before execution, check the policy, state, files and backup chain again. An
   empty or already-current plan still receives the check.
4. Create normal preimage backups, then recheck before the first aircraft write.
   If this check fails, leave the aircraft untouched.
5. Apply the mutations inside the existing rollback transaction. Verify the
   intended outputs, reserved namespaces, scopes and untouched evidence before
   recording the new state. A final-check failure rolls back the changed files.
6. Direct Restore verifies current ownership and original backups before writing
   and checks the restored files and unaffected evidence before saving state.

These boundaries apply to Install, Update, Repair, repeated application, module
selection and deselection, Uninstall and Restore. Aircraft Update and aircraft
Restore check the selected installation's patch ownership before replacing
files. Patch reapplication after an aircraft update uses the same guarded plan
and transaction as a manual patch operation.

Selecting or deselecting a group module recomposes all enabled modules from the
verified original generation. It does not copy an individual module's old
whole-file backup over later patches. Known historical blocks are accepted only
when the selected handler explicitly supports their migration.

## Standalone and MTK in the same folder

This implementation detects conflicts; it does not import standalone backups
or synchronize the two managers.

- Standalone users can continue using the patch's independent installer.
- MTK must not adopt its output without verified ownership and original backups.
- A user switching managers must first use the current manager's supported
  restore or uninstall procedure, preserving unrelated changes. Do not instruct
  users to delete receipts, state or backups to bypass a block.
- Public standalone installers need their own MTK detection before they modify
  files already managed by MTK. This repository cannot add that protection to an
  already downloaded Python installer.

Changes to public installer scripts or package manifests are separate work in
the patch repositories. Do not upload replacements or alter release assets as
part of this Core/catalog implementation. A future handoff must verify the
source manager's receipt, current output, original backup and all overlapping
owners before transferring responsibility.

## Shared-file authoring rules

Every inserted block needs unique markers and one unique anchor. Insertion must
not consume the anchor. Companion loaders should use a declared shared slot so
that several modules can coexist.

A replacement owns an exact source block and exact installed block. It must be
reversible without restoring a complete shared Lua file. Earlier installed
blocks must be declared explicitly through the handler's migration contract.
Do not infer a historical version from a broad text fragment.

Copy targets own only their declared files. Their accepted preimages need an
explicit source-hash contract when replacement is allowed. Reserved companion
payloads that lack an MTK owner block even when their bytes match the download.
Unrelated files outside a reserved scope must remain unchanged.

Keep LF/CRLF, UTF-8 BOM and file permissions intact. No package may assume it is
the only owner of a shared script.

## Test gate

Implementation does not substitute for the following tests. Run them only with
the separate test authorization, using actual source manifests and representative
old MTK state, not only synthetic marker fixtures.

| Case | Required result |
| --- | --- |
| Catalog schema 1/2 and fallback | Old data parses; guarded clients do not fall back to unprotected rules |
| Policy transport | App, Core CLI, imports, cache and saved state retain the contract |
| Fresh install and repeat | Correct output and original backup; repeat keeps the original chain |
| Managed individual and old/new groups | Update remains possible with complete recorded evidence |
| Repair | Supported missing output can be repaired; unknown changed output blocks |
| Selection and deselection | Remaining modules and unrelated edits survive recomposition |
| Uninstall and Restore | Exact original state or recorded absence; no loss of another owner |
| Standalone then MTK | Receipts, markers or matching payloads without MTK ownership block |
| MTK then standalone | MTK detects a broken chain; installer-side prevention is tested separately |
| Namespace damage | Unknown, partial, duplicate, nested-invalid and conflicting markers block |
| Foreign payloads/scopes | Unknown names, hashes, case collisions, directories and links block |
| Historical migration | Only explicitly supported old blocks are transformed |
| Race between planning and writing | Changed files, state, catalog or backups block before mutation |
| Final validation failure | All files written by the failed operation roll back |
| Aircraft update and reapplication | Guarded update preserves matching state and reapplies the selected pipeline |
| Moved aircraft | Rebound MTK paths retain valid ownership and original backups |
| Offline Restore | Saved policies and verified backups remain usable |

Test both product groups, every module alone, current shared-file combinations,
LF/CRLF inputs and supported aircraft baselines. Follow focused tests with the
existing suite and an independent review before any build or publication.
