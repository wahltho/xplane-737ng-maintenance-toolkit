"""Execute both public VNAV installers against frozen original .35 Lua."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

PROJECTS = Path(__file__).resolve().parents[2]
BASELINE = Path(os.environ.get("ZIBO_40535_LUA", "/Users/wahltho/dev/Zibo Mod/Original/Zibo Mod Original/B738X_XP12_4_05_35/plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua"))
SOURCE_HASH = "ff313b0e88c62845ad1c4a2b1f4bd599f57d8799e8d6707bfc10a3369fd63a8e"
TARGET = "plugins/xlua/scripts/B738.a_fms/B738.a_fms.lua"
PACKAGES = ("X-Plane-ZIBO-Descent-Tables", "X-Plane-LevelUp-737NG-Descent-Tables")


def snapshot(root):
    return {p.relative_to(root).as_posix(): p.read_bytes() for p in root.rglob("*") if p.is_file()}


class VnavLifecycleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not BASELINE.is_file():
            raise unittest.SkipTest("Provide the frozen Zibo 4.05.35 baseline via ZIBO_40535_LUA")
        assert hashlib.sha256(BASELINE.read_bytes()).hexdigest() == SOURCE_HASH

    def invoke(self, package, root, *args, code=0):
        result = subprocess.run([sys.executable, str(package / "z_Install.py"),
            "--aircraft-root", str(root), *args], capture_output=True, text=True)
        self.assertEqual(result.returncode, code, result.stdout + result.stderr)
        return result

    def test_fresh_repeat_uninstall_lf_and_crlf_preserve_foreign_edits(self):
        for name in PACKAGES:
            for eol in (b"\n", b"\r\n"):
                with self.subTest(package=name, eol=eol), tempfile.TemporaryDirectory() as t:
                    root = Path(t).resolve() / "aircraft"
                    target = root / TARGET
                    target.parent.mkdir(parents=True)
                    original = BASELINE.read_bytes().replace(b"\r\n", b"\n").replace(b"\n", eol)
                    target.write_bytes(original)
                    package = PROJECTS / name
                    contract = json.loads((package / "standalone-ownership.json").read_text())
                    pid = contract["policy"]["packageId"]
                    self.invoke(package, root)
                    installed = snapshot(root)
                    self.assertEqual(target.read_bytes().count(b"BEGIN"), original.count(b"BEGIN") + 3)
                    self.invoke(package, root)
                    self.assertEqual(snapshot(root), installed)
                    foreign = b"-- unrelated local edit" + eol
                    target.write_bytes(target.read_bytes() + foreign)
                    receipt = root / (".patch-ownership/" + pid + "/receipt.json")
                    state = json.loads(receipt.read_text())
                    self.assertEqual((root / state["files"][TARGET]["backupRelativePath"]).read_bytes(), original)
                    self.invoke(package, root, "--uninstall")
                    self.assertEqual(target.read_bytes(), original + foreign)
                    self.assertFalse(receipt.exists())
                    for payload in contract["policy"]["payloadPaths"]:
                        self.assertFalse((root / payload).exists())

    def test_missing_backup_and_unowned_patch_block_without_changes(self):
        for name in PACKAGES:
            with self.subTest(package=name), tempfile.TemporaryDirectory() as t:
                root = Path(t).resolve() / "aircraft"
                target = root / TARGET
                target.parent.mkdir(parents=True)
                target.write_bytes(BASELINE.read_bytes())
                package = PROJECTS / name
                self.invoke(package, root)
                policy = json.loads((package / "standalone-ownership.json").read_text())["policy"]
                receipt = root / (".patch-ownership/" + policy["packageId"] + "/receipt.json")
                state = json.loads(receipt.read_text())
                backup = root / state["files"][TARGET]["backupRelativePath"]
                backup.unlink()
                before = snapshot(root)
                for action in ((), ("--uninstall",)):
                    result = self.invoke(package, root, *action, code=1)
                    self.assertIn("backup is missing or changed", result.stderr)
                    self.assertEqual(snapshot(root), before)
                receipt.unlink()
                # Isolate unowned hooks from the independently tested companion-file gate.
                # Otherwise traversal order decides which correct refusal is reported first.
                for pattern in policy["payloadPaths"]:
                    for companion in root.glob(pattern):
                        if companion.is_file():
                            self.assertNotEqual(companion, target)
                            companion.unlink()
                before = snapshot(root)
                result = self.invoke(package, root, code=1)
                self.assertIn("no verified standalone owner", result.stderr)
                self.assertEqual(snapshot(root), before)


if __name__ == "__main__":
    unittest.main()
