# Startup release checks and hardware configuration copying

Release candidate 0.13.11 on main. Validation: 2026-09-18, macOS arm64.

- App build succeeded without warnings or errors.
- Full .NET suite: 381 passed, 0 failed, 0 skipped.
- Hardware tests cover Zibo/LevelUp detection, Zibo 2K/4K deduplication,
  cross-product copying in both directions, byte preservation, missing target
  creation, backup/restore after a new operation instance, idempotency,
  preservation of later user edits, invalid input, missing source/aircraft,
  path rejection, symbolic links, running-simulator refusal, and rollback
  after a second-target write failure while retaining the prior restore point.
- Startup tests cover sequencing, disabled checks, target changes between checks,
  settings persistence, and actual MainWindowViewModel startup against simulated
  HTTP 503 and timeout responses. Controls recover, no installation dialog is
  opened, aircraft bytes remain unchanged, repeated initialization does not issue
  another request, and manual checks work with startup checks disabled.
- Existing metadata cache and GitHub rate-limit regression tests remain green.

Two additional automated Avalonia UI tests now pass using the real MainWindow
XAML, keyboard activation, data bindings and confirmation dialogs:

- Startup with deterministic release responses displays the installed and latest
  aircraft version and current status without pressing Update; patch release
  availability is populated. No archive download or installation is triggered.
- The Settings checkbox disables startup aircraft/patch requests and persists
  when the window and view model are recreated.
- Hardware source/destination selection updates the view model through bindings.
  Cancel leaves files untouched. Confirm copies to an existing and a missing
  destination. Restore recovers original bytes and removes the newly created file.

Rendered views of the source/target selection, confirmation, startup versions and
Settings were inspected. Evidence and test logs are in
`/Users/wahltho/dev/mtk-feature-ui-check/evidence` and its parent directory.
Tests use the Avalonia headless platform with Skia rendering on macOS; these are
repeatable UI tests, not a native OS click-through or live GitHub availability test.

The native test host's .NET runtime discovery was corrected, but computer-use
attachment to its Avalonia window still timed out. Native macOS automation remains
unverified; the test host was stopped. No real aircraft files were modified and
no Windows host was used.

This report records local validation before the release workflow. CI and publication are verified separately.
