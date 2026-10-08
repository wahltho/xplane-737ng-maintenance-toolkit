# Content catalog 1.18.0

Adds **737-800WSFP2 27K / SFP (Beta)** to the Zibo and LevelUp patch groups.
It is optional, unselected by default, and runs at order 68 between VREF and
Intentional Fixes. The LevelUp required modules and all ownership rules stay
unchanged.

This is a catalog update. The application remains MTK 0.28.1; users do not need
a new Toolkit download. MTK 0.28.0 or newer can read this catalog.

The patch uses the original .35 Lua baseline and supports adding 27K to files
already managed by MTK. It applies to the passenger 737-800WSFP2 with SFP
enabled. Other variants and SFP-off retain their existing behavior. Simulator
validation and certified dispatch performance remain open.

The existing 27K `0.1.0-beta.1` ZIP was refreshed to let its FMS reset hook
coexist with the required LevelUp Weight & Balance patch. Its version, runtime
Lua and performance tables are unchanged. Existing standalone receipts remain
recognized, with the original-backup and ownership checks still enforced.

As with VREF Beta, GitHub publishes this release as a regular release so
existing MTK clients can resolve it through `releases/latest`. Its version,
release title and catalog label explicitly identify it as Beta.

## Checks

The focused catalog/UI tests passed (101). The full Toolkit suite passed
(834, with six skipped). Selection is available for both products and remains
off by default. No application source changes were needed.

The corrected package passed the Lua, public-export and previous-installer
round-trip tests. Disposable aircraft checks with the released 0.28.1 Core
cover adding 27K to existing VNAV/required LevelUp ownership, repeat, module
deselection and complete original Restore. These checks do not replace a
simulator test.

The public ZIP was downloaded again and matched its GitHub digest and checksum
file. The released Core then passed ten lifecycle/default/ownership cases for
both products, including an older MTK-owned Beta package updating into the
group. All selected modules except the known CPDLC blocker installed, repeated
and restored successfully. Two additional checks confirmed rollback on that
preexisting CPDLC failure; they do not establish working CPDLC combinations.

## Separate existing issue

CPDLC 1.2.0 contains a generic `-- intentional fix:` comment which the current
Intentional Fixes ownership namespace rejects. This was reproduced without
27K. The operation rolls back; CPDLC combinations must not be described as
fully tested or working until that separate collision is fixed.
