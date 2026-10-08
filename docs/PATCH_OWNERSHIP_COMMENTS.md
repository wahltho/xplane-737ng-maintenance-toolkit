# Comments which mention another patch

A comment can mention a reserved patch name without belonging to that patch.
CPDLC 1.2.0 contains this example inside its own checked module:

```lua
-- intentional fix: the stock LOAD compares an undefined global here and never
```

This is an explanation, not an Intentional Fixes marker. Catalog ownership
rules can list an exact comment under `informationalCommentLines`:

```json
{
  "relativePath": "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua",
  "namespace": "INTENTIONAL FIX",
  "informationalCommentLines": [
    "-- intentional fix: the stock LOAD compares an undefined global here and never"
  ]
}
```

This optional field requires MTK 0.28.2. Catalogs using it must declare that
minimum version. Older MTK parsers reject the unknown field.

Only a complete comment line matches. Indentation and line endings may differ;
the comment's text and case must match exactly. Code followed by the same
comment does not match. The parser rejects multiline entries, BEGIN/END marker
entries, and entries which also appear as an owned marker or allowed comment.

The comment is ignored when checking namespaces and looking for ownership
evidence, including evidence in the original backup. `allowedCommentLines`
keeps its existing meaning: those lines belong to the patch and still require
an owner and a verified original backup.

Live rules and saved installation rules retain these entries during planning,
execution and Restore. An exception cannot remove a saved owned-marker rule;
conflicting rules block the operation. Restore still needs a valid online or
cached catalog, as before.

This exception does not authorize adopting a standalone patch. CPDLC's own
blocks, standalone receipts, payloads, recorded targets and backup chain remain
checked. No patch names or comment exceptions are built into the Core.
