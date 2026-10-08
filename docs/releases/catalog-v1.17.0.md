# Content catalog 1.17.0

The catalog recognizes the receipts, locks and incomplete transactions used by
the refreshed standalone aircraft-patch installers. Historical Weight & Balance
markers and payload names, the GSE native transaction directory, and the 27K
standalone directory and lock are also covered.

Standalone installation remains supported. Updates and removal must use the
installer that owns the patch. To switch between standalone and MTK, remove the
patch with its current installer first. Matching files alone do not authorize
MTK to adopt another installer's backups.

This is a catalog release for Toolkit 0.28.0 or newer. No aircraft behavior or
patch version changes. The refreshed standalone ZIPs and checksums replace the
existing release assets; their release tags stay unchanged. The 27K installer
itself is handled separately by its project.

Validation: the MTK suite passed with 826 tests and six skips. The shared Python
installer checks passed 27 tests, plus both VNAV lifecycle tests. Release ZIPs
were built and checked against the previous public packages for unchanged Lua,
objects, textures and patch payloads. Installer recovery and ownership checks
are documented in `docs/STANDALONE_INSTALLER_HARDENING.md`.
