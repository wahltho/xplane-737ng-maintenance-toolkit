# Tool package dependencies

Tool and aircraft-component release manifests may declare other Toolkit-managed
packages that must already be installed. The field is optional; omitting it or
using an empty array preserves the existing independent-package behavior.

```json
{
  "dependencies": [
    {
      "packageId": "wahltho.yal",
      "minimumVersion": "4.8b1"
    }
  ]
}
```

`packageId` must identify a tool or aircraft-component entry in the active,
trusted catalog. `minimumVersion` is optional. When omitted, any recorded and
verified installed version satisfies the requirement. Package IDs must be
unique within a manifest and a package cannot depend on itself.

For the Auto-Unicom Helper, the required declaration is YAL with
`minimumVersion` `4.8b1`, because earlier YAL releases do not provide the
standalone provider interface.

Dependencies use the catalog entry for repository, product, installation scope
and platform restrictions. A dependency that is missing, too old, unavailable
for the selected product, or unsupported on the current platform blocks the
dependent package unless the Toolkit can prepare a compatible release.

## Installation planning

Before downloading archives or changing files, the Toolkit resolves the full
dependency graph. Cycles and unavailable minimum versions are rejected. Missing
or outdated dependencies are ordered before their dependents and displayed in
one confirmation dialog. Each approved package remains a separate validated,
backed-up transaction. If one transaction fails, later package actions do not
start.

The Toolkit records each package's direct resolved dependencies in local state.
It does not automatically remove a dependency when a dependent package is
restored or replaced, because another installation or package may still need
it.

## Restore and version safety

Installing an older release, switching channels, or restoring a backup is
blocked when the resulting dependency version would be missing or below the
minimum required by an installed dependent package. Restore also returns the
restored package's own dependency declarations to the state captured with that
backup generation.

These checks complement the existing file ownership, backup and rollback
guards. They do not weaken or bypass package validation.

## Compatibility with older Toolkit versions

Older Toolkit versions ignore unknown optional manifest properties. A catalog
that introduces a package whose safe installation depends on this feature must
therefore raise its global `minimumToolkitVersion` to the first released Toolkit
version that supports dependency planning. Existing catalog entries and release
manifests do not need to add an empty `dependencies` field.
