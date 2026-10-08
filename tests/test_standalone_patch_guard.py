"""Ownership/rollback contracts exercised without touching simulator installs."""
import importlib.util
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch


ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location("standalone_guard_tested", ROOT / "tools/standalone_patch_guard.py")
g = importlib.util.module_from_spec(spec)
spec.loader.exec_module(g)
PID = "test.community.patch"
SCRIPT = "plugins/xlua/scripts/demo/main.lua"
PAYLOAD = "plugins/xlua/scripts/demo/runtime.lua"
ORIGINAL = b"jit.off()\n-- other author's edit\n"
INSTALLED = ORIGINAL + b"-- BEGIN DEMO_PATCH HOOK\nlocal demo = true\n-- END DEMO_PATCH HOOK\n"


def tree(root):
    return {p.relative_to(root).as_posix(): (p.read_bytes(), stat.S_IMODE(p.stat().st_mode))
            for p in root.rglob("*") if p.is_file() and not p.is_symlink()}


class OwnershipTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="standalone-ownership-tests-")
        self.addCleanup(self.temp.cleanup)
        base = Path(self.temp.name).resolve()
        self.root = base / "aircraft"
        self.package = base / "package"
        self.root.mkdir()
        self.package.mkdir()
        self.put(SCRIPT, ORIGINAL)
        self.state_path = base / "mtk-state.json"
        self.addCleanup(patch.stopall)
        patch.object(g, "state_candidates", return_value=[self.state_path]).start()
        patch.object(g.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "python\n", "")).start()
        self.policy = dict(packageId=PID, targetPaths=[SCRIPT, PAYLOAD], payloadPaths=[PAYLOAD],
            standaloneEvidencePaths=[], signatures=[], originalFiles=[], resultFiles=[],
            markerNamespaces=[dict(relativePath=SCRIPT, namespace="DEMO_PATCH", blocks=[dict(
                beginMarker="-- BEGIN DEMO_PATCH HOOK", endMarker="-- END DEMO_PATCH HOOK")])])
        self.contract = dict(schemaVersion=1, policy=self.policy)
        self.save_contract()

    def save_contract(self):
        (self.package / "standalone-ownership.json").write_text(json.dumps(self.contract))

    def put(self, relative, data):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        return path

    def guard(self):
        return g.Guard(self.root, self.package)

    def install(self):
        with self.guard() as guard:
            guard.apply({SCRIPT: INSTALLED, PAYLOAD: b"runtime"}, version="1.0")

    def native(self, kind="files"):
        state_relative = ".native/state.json"
        self.contract["nativeState"] = dict(kind=kind, path=state_relative)
        record = dict(relativePath=SCRIPT, originalSha256=g.digest(ORIGINAL), installedSha256=g.digest(INSTALLED))
        if kind == "single":
            state = dict(schemaVersion=1, packageId=PID, targetRelativePath=SCRIPT,
                originalSha256=record["originalSha256"], installedSha256=record["installedSha256"],
                backupRelativePath=".native/original/" + SCRIPT)
        elif kind == "mapping":
            self.contract["nativeState"]["originalDirectory"] = ".native/original"
            state = dict(schemaVersion=1, packageId=PID, files={SCRIPT: record})
        else:
            state = dict(schemaVersion=1, packageId=PID, backupRelativePath=".native/original", files=[record])
        self.save_contract()
        self.put(SCRIPT, INSTALLED)
        self.put(".native/original/" + SCRIPT, ORIGINAL)
        self.put(state_relative, json.dumps(state).encode())
        return state_relative

    def test_install_repeat_update_uninstall_preserves_original_and_modes(self):
        (self.root / SCRIPT).chmod(0o640)
        self.install()
        with self.guard() as guard:
            before = guard.state_raw
            guard.apply({SCRIPT: INSTALLED, PAYLOAD: b"runtime"}, version="1.0")
            self.assertEqual(before, guard.state_raw)
        updated = INSTALLED.replace(b"true", b"false")
        with self.guard() as guard:
            guard.apply({SCRIPT: updated, PAYLOAD: b"runtime v2"}, version="2.0")
        foreign = b"-- edit after installation\n"
        self.put(SCRIPT, updated + foreign)
        with self.guard() as guard:
            self.assertEqual(guard.original_payload(SCRIPT), ORIGINAL)
            guard.apply({SCRIPT: ORIGINAL + foreign, PAYLOAD: guard.original_payload(PAYLOAD)}, uninstall=True)
        self.assertEqual((self.root / SCRIPT).read_bytes(), ORIGINAL + foreign)
        self.assertEqual(stat.S_IMODE((self.root / SCRIPT).stat().st_mode), 0o640)
        self.assertFalse((self.root / PAYLOAD).exists())
        self.assertFalse((self.root / (".patch-ownership/" + PID + "/receipt.json")).exists())

    def test_identical_unowned_payload_is_not_adopted(self):
        self.put(PAYLOAD, b"runtime")
        before = tree(self.root)
        with self.assertRaisesRegex(g.OwnershipError, "Unowned companion"):
            self.install()
        self.assertEqual(tree(self.root), before)

    def test_identical_unowned_patch_is_not_adopted(self):
        self.put(SCRIPT, INSTALLED)
        with self.assertRaisesRegex(g.OwnershipError, "no verified standalone owner"):
            self.guard()

    def test_unknown_duplicate_incomplete_nested_or_case_changed_markers_block(self):
        variants = [b"-- BEGIN DEMO_PATCH UNKNOWN\n", INSTALLED + INSTALLED,
            b"-- BEGIN DEMO_PATCH HOOK\n", b"-- END DEMO_PATCH HOOK\n",
            b"-- BEGIN DEMO_PATCH HOOK\n-- BEGIN DEMO_PATCH HOOK\n",
            INSTALLED.replace(b"DEMO_PATCH", b"demo_patch")]
        for data in variants:
            with self.subTest(data=data):
                self.put(SCRIPT, data)
                before = tree(self.root)
                with self.assertRaises(g.OwnershipError):
                    self.install()
                self.assertEqual(tree(self.root), before)

    def test_lf_and_crlf_marker_fingerprints_match(self):
        self.assertEqual(g.marker_fingerprints(INSTALLED, self.policy, SCRIPT),
                         g.marker_fingerprints(INSTALLED.replace(b"\n", b"\r\n"), self.policy, SCRIPT))

    def test_mixed_line_endings_reject_before_writes(self):
        self.put(SCRIPT, INSTALLED.replace(b"\n", b"\r\n") + b"-- foreign\n")
        with self.assertRaisesRegex(g.OwnershipError, "line endings"):
            self.guard()

    def test_damaged_owned_blocks_and_payload_reject(self):
        self.install()
        before = tree(self.root)
        for relative, bad in [(SCRIPT, INSTALLED.replace(b"true", b"evil")), (PAYLOAD, b"foreign")]:
            with self.subTest(relative=relative):
                self.put(relative, bad)
                with self.assertRaises(g.OwnershipError):
                    self.guard()
                self.put(relative, before[relative][0])

    def test_original_backup_is_required_and_verified(self):
        self.install()
        guard = self.guard()
        backup = guard.state["files"][SCRIPT]["backupRelativePath"]
        self.put(backup, b"bad")
        with self.assertRaisesRegex(g.OwnershipError, "backup is missing or changed"):
            self.guard()

    def test_backup_containing_patch_cannot_be_claimed_as_original(self):
        self.native()
        state = json.loads((self.root / ".native/state.json").read_bytes())
        state["files"][0]["originalSha256"] = g.digest(INSTALLED)
        self.put(".native/original/" + SCRIPT, INSTALLED)
        self.put(".native/state.json", json.dumps(state).encode())
        with self.assertRaisesRegex(g.OwnershipError, "already contains this patch"):
            self.guard()

    def test_native_receipt_formats_and_explicit_historical_identity(self):
        for kind in ("files", "single", "mapping"):
            with self.subTest(kind=kind):
                state_path = self.native(kind)
                self.guard()
                state = json.loads((self.root / state_path).read_bytes())
                state["packageId"] = "historical.clean-only"
                self.put(state_path, json.dumps(state).encode())
                with self.assertRaises(g.OwnershipError):
                    self.guard()
                self.contract["nativeState"]["packageId"] = "historical.clean-only"
                self.save_contract()
                self.guard()

    def test_incomplete_or_conflicting_native_inventory_rejects(self):
        self.native()
        state = json.loads((self.root / ".native/state.json").read_bytes())
        state["files"] *= 2
        self.put(".native/state.json", json.dumps(state).encode())
        with self.assertRaisesRegex(g.OwnershipError, "duplicate"):
            self.guard()

    def test_orphan_receipt_evidence_and_lock_and_journal_block(self):
        for relative in (".old/receipt.json", ".patch-ownership/" + PID + ".lock",
                         ".patch-ownership/" + PID + "/transaction/journal.json"):
            with self.subTest(relative=relative):
                self.policy["standaloneEvidencePaths"] = [".old/receipt.json"]
                self.save_contract()
                p = self.put(relative, b"orphan")
                with self.assertRaises(g.OwnershipError):
                    self.guard()
                p.unlink()
                for parent in list(p.parents):
                    if parent == self.root:
                        break
                    try:
                        parent.rmdir()
                    except OSError:
                        break

    def test_mtk_single_group_and_shared_target_ownership_blocks(self):
        for component in ({PID: dict(Files=[dict(RelativePath=SCRIPT)])},
                          {"group": dict(Sources=[dict(PackageId=PID)], Files=[dict(RelativePath=SCRIPT)])},
                          {"other": dict(Files=[dict(RelativePath=SCRIPT)])}):
            for group in ("Aircraft", "ContentInstallations"):
                with self.subTest(component=component, group=group):
                    self.state_path.write_text(json.dumps(dict(SchemaVersion=10, **{group: {
                        str(self.root): dict(AircraftFolder=str(self.root), ContentComponents=component)}})))
                    before = tree(self.root)
                    with self.assertRaisesRegex(g.OwnershipError, "MTK manages"):
                        self.install()
                    self.assertEqual(before, tree(self.root))

    def test_unrelated_mtk_aircraft_or_target_is_allowed(self):
        self.state_path.write_text(json.dumps(dict(SchemaVersion=10, ContentInstallations={str(self.root):
            dict(ContentComponents={"other": dict(Files=[dict(RelativePath="objects/other.obj")])})})))
        self.install()

    def test_malformed_and_incomplete_mtk_records_block(self):
        records = ["not json", json.dumps(dict(SchemaVersion=11)),
                   json.dumps(dict(SchemaVersion=10, Aircraft={str(self.root):
                       dict(InstalledContentPackageId=PID)})),
                   json.dumps(dict(SchemaVersion=10, Aircraft={str(self.root):
                       dict(ContentComponents={PID: dict(Files=[])})}))]
        for raw in records:
            with self.subTest(raw=raw):
                self.state_path.write_text(raw)
                with self.assertRaises(g.OwnershipError):
                    self.guard()

    def test_target_receipt_backup_and_mtk_rechecked_before_writes(self):
        self.install()
        for target in (SCRIPT, ".patch-ownership/" + PID + "/receipt.json", "backup", "mtk"):
            with self.subTest(target=target):
                guard = self.guard()
                if target == "backup":
                    target = guard.state["files"][SCRIPT]["backupRelativePath"]
                p = self.state_path if target == "mtk" else self.root / target
                old = p.read_bytes() if p.exists() else None
                p.write_bytes(b"concurrent")
                before = tree(self.root)
                with self.assertRaises(g.OwnershipError):
                    with guard:
                        guard.apply({SCRIPT: INSTALLED, PAYLOAD: b"new"})
                self.assertEqual(before, tree(self.root))
                if old is None:
                    p.unlink()
                else:
                    p.write_bytes(old)

    def test_links_traversal_and_case_collisions_block(self):
        for relative in ("../outside", "/outside", "a/../b", "a//b", "a\\b", "C:/x"):
            with self.subTest(relative=relative), self.assertRaises(g.OwnershipError):
                g.safe(self.root, relative)
        (self.root / "plugins").rename(self.root / "Plugins")
        with self.assertRaises(g.OwnershipError):
            self.guard()
        (self.root / "Plugins").rename(self.root / "plugins")
        p = self.root / PAYLOAD
        p.symlink_to(self.package / "outside")
        with self.assertRaises(g.OwnershipError):
            self.guard()

    def test_flat_scope_rejects_unknown_subdirectory_and_links(self):
        self.policy["targetPaths"].append("objects/GSE/**")
        self.save_contract()
        (self.root / "objects/GSE/subdirectory").mkdir(parents=True)
        with self.assertRaises(g.OwnershipError):
            self.guard()

    def test_wildcard_retired_absence_remains_idempotent_and_restorable(self):
        self.policy["targetPaths"].append("objects/GSE/**")
        self.save_contract()
        retired = "objects/GSE/old.obj"
        self.put(retired, b"known original")
        before = tree(self.root)
        with self.guard() as guard:
            guard.apply({SCRIPT: INSTALLED, retired: None}, version="1.0")
        self.assertFalse((self.root / retired).exists())
        with self.guard() as guard:
            guard.apply({SCRIPT: INSTALLED, retired: None}, version="1.0")
        with self.guard() as guard:
            guard.apply({SCRIPT: ORIGINAL, retired: guard.original_payload(retired)}, uninstall=True)
        self.assertEqual((self.root / retired).read_bytes(), before[retired][0])

    def test_failed_backup_target_receipt_and_final_validation_roll_back_exactly(self):
        for stage in ("backup", "target", "receipt", "final"):
            with self.subTest(stage=stage):
                before = tree(self.root)
                original_atomic = g._atomic
                fired = False
                def failing(path, data, mode=0o644):
                    nonlocal fired
                    relative = path.relative_to(self.root).as_posix()
                    matches = ((stage == "backup" and "/backups/" in relative)
                        or (stage == "target" and relative == PAYLOAD)
                        or (stage == "receipt" and relative.endswith("receipt.json")))
                    if matches and not fired:
                        fired = True
                        raise OSError("injected " + stage)
                    return original_atomic(path, data, mode)
                with patch.object(g, "_atomic", side_effect=failing):
                    with self.assertRaises((OSError, g.OwnershipError)):
                        with self.guard() as guard:
                            guard.apply({SCRIPT: INSTALLED, PAYLOAD: b"runtime"}, version="1.0")
                            if stage == "final":
                                guard.state["files"][PAYLOAD]["installedSha256"] = "bad"
                self.assertEqual(tree(self.root), before)

    def test_native_cleanup_failure_restores_receipt_backups_and_target(self):
        self.native()
        before = tree(self.root)
        with self.assertRaisesRegex(OSError, "after cleanup"):
            with self.guard():
                g.owned_write(self.root / SCRIPT, ORIGINAL)
                g.owned_rmtree(self.root / ".native")
                raise OSError("after cleanup")
        self.assertEqual(tree(self.root), before)

    def test_concurrent_target_is_retained_with_recovery_journal(self):
        with self.assertRaisesRegex(g.OwnershipError, "manual recovery"):
            with self.guard() as guard:
                guard.apply({SCRIPT: INSTALLED, PAYLOAD: b"runtime"}, version="1.0")
                self.put(SCRIPT, b"other process")
                raise OSError("crash")
        self.assertEqual((self.root / SCRIPT).read_bytes(), b"other process")
        self.assertTrue((self.root / (".patch-ownership/" + PID + ".lock")).exists())
        self.assertTrue((self.root / (".patch-ownership/" + PID + "/transaction/journal.json")).exists())

    def test_xplane_running_blocks_first_write(self):
        before = tree(self.root)
        with patch.object(g.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "X-Plane\n", "")):
            with self.assertRaisesRegex(g.OwnershipError, "Close X-Plane"):
                self.install()
        self.assertEqual(tree(self.root), before)

    def test_native_json_failure_does_not_leave_a_temporary_receipt(self):
        repositories = ("X-Plane-LevelUp-737NG-FANS-CDU", "X-Plane-Zibo-Auto-Jetway",
                        "X-Plane-Zibo-LevelUp-737NG-CPDLC", "X-Plane-Zibo-LevelUp-737NG-Intentional-Fixes")
        for name in repositories:
            with self.subTest(repository=name):
                repository = ROOT.parent / name
                spec = importlib.util.spec_from_file_location("native_json_writer", repository / "z_Install.py")
                module = importlib.util.module_from_spec(spec)
                with patch.dict(sys.modules, {"standalone_guard": g}):
                    original_path = list(sys.path)
                    old_patchlib = sys.modules.pop("patchlib", None)
                    try:
                        sys.path.insert(0, str(repository))
                        spec.loader.exec_module(module)
                    finally:
                        sys.path[:] = original_path
                        sys.modules.pop("patchlib", None)
                        if old_patchlib is not None:
                            sys.modules["patchlib"] = old_patchlib
                writer = getattr(module, "_write_json_atomic", None) or module.write_json_atomic
                before = tree(self.root)
                with self.assertRaisesRegex(OSError, "receipt activation"):
                    with self.guard() as owner, patch.object(module, "owned_replace", side_effect=OSError("receipt activation")):
                        writer(self.root / owner.receipt_relative, {"fixture": True})
                self.assertEqual(tree(self.root), before)


class StateLocationTests(unittest.TestCase):
    def test_catalog_blocks_historical_700_payload_without_hooks_or_receipt(self):
        package = ROOT.parent / "X-Plane-LevelUp-737NG-Weight-Balance"
        if not package.is_dir():
            self.skipTest("Provide the sibling Weight & Balance source repository")
        with tempfile.TemporaryDirectory() as t, patch.object(g, "state_candidates", return_value=[]):
            root = Path(t).resolve()
            payload = root / "plugins/xlua/scripts/B738.tablet/B738.tablet_levelup700_wb_adapter.lua"
            payload.parent.mkdir(parents=True)
            payload.write_bytes(b"historical standalone runtime")
            with self.assertRaisesRegex(g.OwnershipError, "Unowned companion"):
                g.Guard(root, package)
            self.assertEqual(payload.read_bytes(), b"historical standalone runtime")

    def test_standard_windows_macos_linux_locations_and_custom_state(self):
        for platform, expected in (("win32", "/appdata/XPlane737NGMaintenanceToolkit/state.json"),
                                   ("darwin", "/home/test/Library/Application Support/XPlane737NGMaintenanceToolkit/state.json"),
                                   ("linux", "/xdg/XPlane737NGMaintenanceToolkit/state.json")):
            with self.subTest(platform=platform), patch.object(g.sys, "platform", platform), \
                    patch.object(g.Path, "home", return_value=Path("/home/test")), \
                    patch.dict(os.environ, dict(APPDATA="/appdata", XDG_CONFIG_HOME="/xdg", MTK_STATE_PATH="/custom/state.json")):
                candidates = g.state_candidates()
                self.assertIn(Path(expected), candidates)
                self.assertIn(Path("/custom/state.json"), candidates)


if __name__ == "__main__":
    unittest.main()
