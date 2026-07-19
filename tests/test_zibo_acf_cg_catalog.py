import importlib.util
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path
from types import SimpleNamespace


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "tools" / "build_zibo_acf_cg_catalog.py"
SPEC = importlib.util.spec_from_file_location("zibo_acf_cg_catalog", SCRIPT_PATH)
catalog = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = catalog
SPEC.loader.exec_module(catalog)


def make_packages(count):
    packages = [
        catalog.FeedPackage(
            "fullBaseline",
            catalog.Version.full("4", "05"),
            "B737-800X_XP12_4_05_full.zip",
            "https://example.invalid/B737-800X_XP12_4_05_full.zip.torrent",
            "",
        )
    ]
    for patch in range(1, count):
        packages.append(
            catalog.FeedPackage(
                "cumulativePatch",
                catalog.Version.patch_version("4", "05", str(patch)),
                f"B738X_XP12_4_05_{patch:02d}.zip",
                f"https://example.invalid/B738X_XP12_4_05_{patch:02d}.zip.torrent",
                "",
            )
        )
    return packages


def acf_text(cg_y, cg_z=49.840001678):
    return "\n".join(
        [
            "I",
            "800 Version",
            "P acf/_name Boeing 737-800X",
            "P acf/_descrip Test ACF",
            "P acf/_studio Zibo",
            "P acf/_version 1200",
            "P acf/_file_writer_version 120000",
            f"P acf/_cgY {cg_y}",
            f"P acf/_cgZ {cg_z}",
            "",
        ]
    )


def write_zip(zip_dir, package, cg_y):
    with zipfile.ZipFile(zip_dir / package.file_name, "w") as archive:
        if cg_y is None:
            archive.writestr("readme.txt", "patch without ACF\n")
        else:
            archive.writestr("B737-800X/b738.acf", acf_text(cg_y))


class ZiboAcfCgCatalogTests(unittest.TestCase):
    def build_catalog(self, packages, strategy, zip_values):
        with tempfile.TemporaryDirectory() as temp_dir:
            root = Path(temp_dir)
            zip_dir = root / "zips"
            zip_dir.mkdir()
            for index, cg_y in zip_values.items():
                write_zip(zip_dir, packages[index], cg_y)

            old_load_feed = catalog.load_feed
            old_load_torrent = catalog.load_torrent
            catalog.load_feed = lambda _feed_url: packages
            catalog.load_torrent = lambda package, _torrent_dir: (
                {
                    "path": str(root / "torrents" / f"{package.file_name}.torrent"),
                    "sizeBytes": 10,
                    "sha256": "0" * 64,
                    "infoSha1": "0" * 40,
                    "payloadName": package.file_name,
                    "payloadSizeBytes": 123,
                    "announce": "https://example.invalid/announce",
                    "hasWebSeed": False,
                },
                b"",
            )
            try:
                args = SimpleNamespace(
                    feed_url="https://example.invalid/feed.xml",
                    cache_root=str(root / "cache"),
                    zip_dir=[str(zip_dir)],
                    download_missing=False,
                    download_kind="all",
                    max_downloads=0,
                    bt_stop_timeout=1,
                    strategy=strategy,
                )
                return catalog.build_catalog(args)
            finally:
                catalog.load_feed = old_load_feed
                catalog.load_torrent = old_load_torrent

    def test_exhaustive_classifies_single_change_and_baseline_inheritance(self):
        packages = make_packages(3)
        data = self.build_catalog(packages, "exhaustive", {0: -2.0, 1: -3.0, 2: None})

        self.assertEqual(data["probedVersions"], ["4.05.00", "4.05.01", "4.05.02"])
        self.assertEqual([package["status"] for package in data["packages"]], ["scanned", "scanned", "scannedNoAcf"])
        self.assertEqual(
            [(item["fromVersion"], item["toVersion"], item["resolution"]) for item in data["changeRanges"]],
            [("4.05.00", "4.05.01", "exact"), ("4.05.01", "4.05.02", "exact")],
        )
        ranges = [(item["fromVersion"], item["toVersion"], item["cgYFeet"]) for item in data["effectiveBaselines"]]
        self.assertIn(("4.05.00", "4.05.00", -2.0), ranges)
        self.assertIn(("4.05.01", "4.05.01", -3.0), ranges)
        self.assertIn(("4.05.02", "4.05.02", -2.0), ranges)

    def test_latest_strategy_only_probes_baseline_and_latest(self):
        packages = make_packages(4)
        data = self.build_catalog(packages, "latest", {0: -2.0, 1: -3.0, 2: -4.0, 3: -4.0})

        self.assertEqual(data["probedVersions"], ["4.05.00", "4.05.03"])
        self.assertEqual(data["packages"][1]["status"], "notProbed")
        self.assertEqual(data["packages"][2]["status"], "notProbed")
        self.assertEqual(data["changeRanges"][0]["resolution"], "bounded")
        self.assertEqual(data["unresolvedRanges"][0]["reason"], "latestStrategyDoesNotResolveIntermediateHistory")

    def test_bisect_detects_revert_when_midpoint_differs(self):
        packages = make_packages(3)
        data = self.build_catalog(packages, "bisect", {0: -2.0, 1: -3.0, 2: None})

        self.assertEqual(data["probedVersions"], ["4.05.00", "4.05.01", "4.05.02"])
        self.assertEqual(
            [(item["fromVersion"], item["toVersion"], item["resolution"]) for item in data["changeRanges"]],
            [("4.05.00", "4.05.01", "exact"), ("4.05.01", "4.05.02", "exact")],
        )

    def test_bisect_detects_multiple_changes(self):
        packages = make_packages(4)
        data = self.build_catalog(packages, "bisect", {0: -2.0, 1: -3.0, 2: -4.0, 3: -4.0})

        self.assertEqual(data["probedVersions"], ["4.05.00", "4.05.01", "4.05.02", "4.05.03"])
        self.assertEqual(
            [(item["fromVersion"], item["toVersion"], item["resolution"]) for item in data["changeRanges"]],
            [("4.05.00", "4.05.01", "exact"), ("4.05.01", "4.05.02", "exact")],
        )

    def test_bisect_reports_missing_midpoint(self):
        packages = make_packages(3)
        data = self.build_catalog(packages, "bisect", {0: -2.0, 2: -3.0})

        self.assertEqual(data["missingPackages"], [{"version": "4.05.01", "fileName": "B738X_XP12_4_05_01.zip", "status": "missingZip"}])
        self.assertEqual(data["unresolvedRanges"][0]["reason"], "missingMidpointSignature")
        self.assertEqual(data["unresolvedRanges"][0]["midVersion"], "4.05.01")


if __name__ == "__main__":
    unittest.main()
