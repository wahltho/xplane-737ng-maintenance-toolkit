# Toolkit 0.13.0 validation

Local validation on macOS arm64, 2026-09-09:

- `dotnet test LevelUp.NavTableUpdater.slnx --configuration Release --no-restore`: 307 passed, zero failed or skipped.
- `python3 -m unittest discover -s tests -p 'test_*.py' -v`: five passed.
- `git diff --check`: clean.

Regression coverage includes required/optional catalog policy, supported-product
and source identity validation, legacy adapters, tampered payload rejection,
verified/broken migration chains, source payload updates, foreign edits between
planning and execution, transaction rollback and restoration. Application tests
cover Update/OK, repeated notification, failed update checks, download/restart
flow and persistence of the startup-check preference.

## Real source archives

All five current stable releases were downloaded and verified against published
checksums, then adapted together through the production resolver:

| Source | Release | Archive SHA-256 |
| --- | --- | --- |
| VNAV descent tables | v0.2.0 | fd832797832fd20f696030ce73d7e2a366fa60af5a43eaf6524e466a98380e56 |
| FANS CDU | v0.1.5 | 54c081441e22abb67e8da9ba3e116072ab58f14dc783f0c779eddea264bbbca6 |
| Tablet Performance Calculator | v0.1.6 | fbbc6c23c465d4b7096c420ff7049e7dc7bc6f5738596a13dd07204e09e8f4d0 |
| Weight & Balance | v0.5.3 | 20b68793f3ce585c9db14b036950f7d01c7acf3af0f5a4bb6ec0c8190c160b49 |
| AUTO JETWAY | v0.2.2 | 6dd2c3221c88193c18dace96e409db8f9a985e8b3d0f2862255b1fd62a1bc07d |

A temporary harness called the production resolver/planner/engine on disposable
copies of original `737NG Series_V2.S1.50A` target files. It verified required
installation, unchanged repeat, optional-module addition and removal, and full
restore by file existence and SHA-256 against the original inputs. A second
replay installed the same modules individually with their source package IDs,
then verified migration to group ownership, unchanged repeat and exact restore.
This tests recorded ownership migration; it does not establish recovery of
unrecorded third-party installations.

No operational aircraft installation was modified. These are automated source
and filesystem checks, not simulator runtime or interactive GUI acceptance.
Platform packaging and release checks are recorded by the corresponding GitHub
Actions runs for the release commit.
