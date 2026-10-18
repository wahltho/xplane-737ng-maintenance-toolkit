# Linked aircraft folders

MTK can use an aircraft reached through a directory symlink or a Windows directory junction. A link in a parent directory is supported too. The selected entry remains in place: aircraft updates replace the physical aircraft directory, not its link.

The displayed selection and the working directory have different jobs. The selection supplies the X-Plane installation context, for example for global plugin dependencies. The resolved directory owns aircraft files and Toolkit history. Opening the same installation directly or through a link must therefore use the same state and backups.

## Safety rules

- Resolve and record the working directory when scanning or preparing an operation. Recheck the selection before activation. If the link points somewhere else, stop and ask the user to select the installation again.
- Do not follow links inside the aircraft. This includes linked runtime directories, scripts, objects and individual files. Existing path, ownership and backup checks still apply.
- Store new aircraft state against the resolved directory. Do not treat identical files as permission to adopt an unowned patch.
- An older state record that names a user-created link has no recorded physical identity. Do not silently move that record to today's target. Keep its backups and request support to reconnect the history safely. Conflicting alias and physical records must not be merged.
- Aircraft update staging and recovery directories belong next to the physical aircraft. Restore returns the previous physical directory and leaves the selected link intact.
- The aircraft move command stays unavailable for a linked selection. Moving a target can invalidate links outside MTK's control.

Close X-Plane before making changes. If an external drive is disconnected or a link is broken, reconnect it and rescan; do not delete Toolkit history or backups to bypass a check.

## Validation

Regression tests cover root and ancestor links, startup checks, Zibo and LevelUp aircraft updates and offline restore, shared physical history, link retargeting during preparation, runtime update/repair/restore and nested-link rejection. Windows junction tests are platform-gated and need execution on Windows; macOS symlink tests do not establish Windows junction behavior.
