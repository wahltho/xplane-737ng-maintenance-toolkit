#!/usr/bin/env python3
"""Build a Zibo ACF CG catalog from the Skymatix torrent feed.

The feed only exposes .torrent files. This tool therefore has two phases:

1. Always parse the feed and torrent metainfo into a package inventory.
2. Parse cached ZIP files when present, optionally downloading missing ZIPs via
   aria2c before scanning.

The generated JSON is intended as review/build input for the app's known ACF CG
catalog. It must not be treated as sufficient proof that a user's Quick Views
fit a given CG; local toolkit state and file hashes still own write decisions.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Callable


DEFAULT_FEED_URL = "https://skymatixva.com/tfiles/feed.xml"
DEFAULT_CACHE_ROOT = (
    Path.home()
    / "Library"
    / "Caches"
    / "XPlane737NGMaintenanceToolkit"
    / "zibo-acf-cg-catalog"
)
DEFAULT_OUTPUT = Path("artifacts/zibo-acf-cg-catalog.json")
FAMILY = "zibo-737-800x"

FULL_RE = re.compile(r"^B737-800X(?:_XP\d+)?_(\d+)_(\d+)_full\.zip$", re.IGNORECASE)
PATCH_RE = re.compile(r"^B738X(?:_XP\d+)?_(\d+)_(\d+)_(\d+)\.zip$", re.IGNORECASE)


@dataclass(frozen=True, order=True)
class Version:
    major: int
    minor: int
    patch: int

    @classmethod
    def full(cls, major: str, minor: str) -> "Version":
        return cls(int(major), int(minor), 0)

    @classmethod
    def patch_version(cls, major: str, minor: str, patch: str) -> "Version":
        return cls(int(major), int(minor), int(patch))

    def display(self) -> str:
        return f"{self.major}.{self.minor:02d}.{self.patch:02d}"

    def baseline_display(self) -> str:
        return f"{self.major}.{self.minor:02d}.00"


@dataclass(frozen=True)
class FeedPackage:
    kind: str
    version: Version
    file_name: str
    torrent_url: str
    description: str


class BencodeError(ValueError):
    pass


def bdecode(data: bytes, offset: int = 0) -> tuple[Any, int]:
    if offset >= len(data):
        raise BencodeError("Unexpected end of bencoded data.")

    token = data[offset : offset + 1]
    if token == b"i":
        end = data.index(b"e", offset)
        return int(data[offset + 1 : end]), end + 1

    if token == b"l":
        offset += 1
        items: list[Any] = []
        while data[offset : offset + 1] != b"e":
            value, offset = bdecode(data, offset)
            items.append(value)
        return items, offset + 1

    if token == b"d":
        offset += 1
        result: dict[bytes, Any] = {}
        while data[offset : offset + 1] != b"e":
            key, offset = bdecode(data, offset)
            if not isinstance(key, bytes):
                raise BencodeError("Dictionary key is not bytes.")
            value, offset = bdecode(data, offset)
            result[key] = value
        return result, offset + 1

    if token.isdigit():
        colon = data.index(b":", offset)
        length = int(data[offset:colon])
        start = colon + 1
        end = start + length
        return data[start:end], end

    raise BencodeError(f"Unexpected bencode token at {offset}: {token!r}")


def bencode(value: Any) -> bytes:
    if isinstance(value, int):
        return b"i" + str(value).encode("ascii") + b"e"
    if isinstance(value, bytes):
        return str(len(value)).encode("ascii") + b":" + value
    if isinstance(value, str):
        return bencode(value.encode("utf-8"))
    if isinstance(value, list):
        return b"l" + b"".join(bencode(item) for item in value) + b"e"
    if isinstance(value, dict):
        encoded = bytearray(b"d")
        for key in sorted(value):
            encoded.extend(bencode(key))
            encoded.extend(bencode(value[key]))
        encoded.extend(b"e")
        return bytes(encoded)
    raise TypeError(f"Cannot bencode {type(value)!r}")


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_package_name(file_name: str, torrent_url: str, description: str) -> FeedPackage | None:
    base_name = Path(file_name.strip()).name
    full_match = FULL_RE.match(base_name)
    if full_match:
        return FeedPackage(
            "fullBaseline",
            Version.full(full_match.group(1), full_match.group(2)),
            base_name,
            torrent_url.strip(),
            description.strip(),
        )

    patch_match = PATCH_RE.match(base_name)
    if patch_match:
        return FeedPackage(
            "cumulativePatch",
            Version.patch_version(patch_match.group(1), patch_match.group(2), patch_match.group(3)),
            base_name,
            torrent_url.strip(),
            description.strip(),
        )

    return None


def load_feed(feed_url: str) -> list[FeedPackage]:
    with urllib.request.urlopen(feed_url, timeout=30) as response:
        feed_xml = response.read()

    root = ET.fromstring(feed_xml)
    packages: list[FeedPackage] = []
    for item in root.findall(".//item"):
        title = item.findtext("title") or ""
        link = item.findtext("link") or ""
        description = item.findtext("description") or ""
        package = parse_package_name(title, link, description)
        if package is not None:
            packages.append(package)

    return sorted(packages, key=lambda package: (package.version, package.kind, package.file_name))


def download_file(url: str, path: Path) -> bytes:
    path.parent.mkdir(parents=True, exist_ok=True)
    with urllib.request.urlopen(url, timeout=30) as response:
        payload = response.read()
    path.write_bytes(payload)
    return payload


def load_torrent(package: FeedPackage, torrent_dir: Path) -> tuple[dict[str, Any], bytes]:
    torrent_path = torrent_dir / f"{package.file_name}.torrent"
    if torrent_path.exists():
        data = torrent_path.read_bytes()
    else:
        data = download_file(package.torrent_url, torrent_path)

    decoded, offset = bdecode(data)
    if offset != len(data):
        raise BencodeError(f"Trailing data in {torrent_path}.")
    if not isinstance(decoded, dict):
        raise BencodeError(f"Torrent root is not a dictionary: {torrent_path}.")

    info = decoded.get(b"info")
    if not isinstance(info, dict):
        raise BencodeError(f"Torrent has no info dictionary: {torrent_path}.")

    name = info.get(b"name", b"")
    payload_name = name.decode("utf-8", "replace") if isinstance(name, bytes) else ""
    payload_size = info.get(b"length")
    announce = decoded.get(b"announce", b"")
    announce_url = announce.decode("utf-8", "replace") if isinstance(announce, bytes) else ""
    metainfo = {
        "path": str(torrent_path),
        "sizeBytes": len(data),
        "sha256": hashlib.sha256(data).hexdigest(),
        "infoSha1": hashlib.sha1(bencode(info)).hexdigest(),
        "payloadName": payload_name,
        "payloadSizeBytes": payload_size if isinstance(payload_size, int) else None,
        "announce": announce_url,
        "hasWebSeed": b"url-list" in decoded or b"webseeds" in decoded,
    }
    return metainfo, data


def run_aria2c(torrent_path: Path, package_dir: Path, timeout_seconds: int) -> tuple[bool, str]:
    aria2c = shutil.which("aria2c")
    if aria2c is None:
        return False, "aria2c not found."

    package_dir.mkdir(parents=True, exist_ok=True)
    command = [
        aria2c,
        f"--dir={package_dir}",
        "--seed-time=0",
        "--file-allocation=none",
        "--summary-interval=30",
        "--max-connection-per-server=4",
        f"--bt-stop-timeout={timeout_seconds}",
        str(torrent_path),
    ]
    result = subprocess.run(
        command,
        check=False,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
    )
    output_tail = "\n".join(result.stdout.splitlines()[-40:])
    return result.returncode == 0, output_tail


def parse_acf_bytes(data: bytes) -> dict[str, Any]:
    fields: dict[str, str] = {}
    for raw_line in data.decode("utf-8", "replace").splitlines():
        parts = raw_line.split(" ", 2)
        if len(parts) != 3 or parts[0] != "P":
            continue
        key = parts[1]
        if key in {
            "acf/_name",
            "acf/_descrip",
            "acf/_studio",
            "acf/_version",
            "acf/_file_writer_version",
            "acf/_cgY",
            "acf/_cgZ",
        }:
            fields[key] = parts[2].strip()

    def parse_float(key: str) -> float | None:
        value = fields.get(key)
        if value is None:
            return None
        try:
            return float(value)
        except ValueError:
            return None

    return {
        "name": fields.get("acf/_name"),
        "description": fields.get("acf/_descrip"),
        "studio": fields.get("acf/_studio"),
        "acfVersion": fields.get("acf/_version"),
        "fileWriterVersion": fields.get("acf/_file_writer_version"),
        "cgYFeet": parse_float("acf/_cgY"),
        "cgZFeet": parse_float("acf/_cgZ"),
    }


def infer_resolution(acf_path: str) -> str:
    stem = Path(acf_path).stem.lower()
    if "4k" in stem:
        return "4k"
    if "2k" in stem:
        return "2k"
    if stem in {"b738", "b737-800x"}:
        return "2k"
    return "unknown"


def scan_zip(zip_path: Path) -> tuple[str, list[dict[str, Any]], str | None]:
    if not zip_path.exists():
        return "missingZip", [], None
    if zip_path.stat().st_size == 0:
        return "missingZip", [], "ZIP path exists but is empty."

    acf_entries: list[dict[str, Any]] = []
    try:
        with zipfile.ZipFile(zip_path) as archive:
            for member in archive.infolist():
                if member.is_dir() or not member.filename.lower().endswith(".acf"):
                    continue
                data = archive.read(member)
                metadata = parse_acf_bytes(data)
                acf_entries.append(
                    {
                        "family": FAMILY,
                        "variant": "800X",
                        "resolution": infer_resolution(member.filename),
                        "acfPath": member.filename,
                        "acfSha256": hashlib.sha256(data).hexdigest(),
                        "acfSizeBytes": len(data),
                        **metadata,
                    }
                )
    except zipfile.BadZipFile as exc:
        return "invalidZip", [], str(exc)

    return ("scanned" if acf_entries else "scannedNoAcf"), acf_entries, None


def acf_entry_key(acf: dict[str, Any]) -> tuple[str, str, str]:
    return (acf["family"], acf["variant"], acf["resolution"])


def acf_signature_payload(acf_entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
    payload: list[dict[str, Any]] = []
    for acf in sorted(acf_entries, key=acf_entry_key):
        payload.append(
            {
                "family": acf["family"],
                "variant": acf["variant"],
                "resolution": acf["resolution"],
                "cgYFeet": acf.get("cgYFeet"),
                "cgZFeet": acf.get("cgZFeet"),
                "acfVersion": acf.get("acfVersion"),
                "fileWriterVersion": acf.get("fileWriterVersion"),
                "acfSha256": acf.get("acfSha256"),
                "acfPath": acf.get("acfPath"),
            }
        )
    return payload


def cg_signature_payload(acf_entries: list[dict[str, Any]]) -> list[dict[str, Any]]:
    payload: list[dict[str, Any]] = []
    for acf in sorted(acf_entries, key=acf_entry_key):
        payload.append(
            {
                "family": acf["family"],
                "variant": acf["variant"],
                "resolution": acf["resolution"],
                "cgYFeet": acf.get("cgYFeet"),
                "cgZFeet": acf.get("cgZFeet"),
                "acfVersion": acf.get("acfVersion"),
                "fileWriterVersion": acf.get("fileWriterVersion"),
            }
        )
    return payload


def stable_json_hash(payload: Any) -> str:
    encoded = json.dumps(payload, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def baseline_entries_by_version(package_records: list[dict[str, Any]]) -> dict[str, list[dict[str, Any]]]:
    baselines: dict[str, list[dict[str, Any]]] = {}
    for package in package_records:
        if package["kind"] != "fullBaseline" or package["status"] != "scanned":
            continue
        entries = package.get("acfEntries", [])
        if entries:
            baselines[package["baselineVersion"]] = entries
    return baselines


def effective_acf_entries(
    package: dict[str, Any],
    baselines: dict[str, list[dict[str, Any]]],
) -> list[dict[str, Any]] | None:
    status = package["status"]
    if status not in {"scanned", "scannedNoAcf"}:
        return None

    package_entries = package.get("acfEntries", [])
    if package["kind"] == "fullBaseline":
        return package_entries if package_entries else None

    baseline_entries = baselines.get(package["baselineVersion"])
    if baseline_entries is None:
        return package_entries if package_entries else None

    merged = {acf_entry_key(acf): dict(acf, inheritedFromBaseline=True) for acf in baseline_entries}
    for acf in package_entries:
        merged[acf_entry_key(acf)] = dict(acf, inheritedFromBaseline=False)
    return [merged[key] for key in sorted(merged)]


def effective_signature(
    package: dict[str, Any],
    baselines: dict[str, list[dict[str, Any]]],
) -> dict[str, Any] | None:
    entries = effective_acf_entries(package, baselines)
    if not entries:
        return None

    cg_payload = cg_signature_payload(entries)
    acf_payload = acf_signature_payload(entries)
    return {
        "cgSignatureId": stable_json_hash(cg_payload),
        "acfSignatureId": stable_json_hash(acf_payload),
        "entries": acf_payload,
    }


def build_effective_ranges(package_records: list[dict[str, Any]]) -> list[dict[str, Any]]:
    active: dict[tuple[str, str, str], dict[str, Any]] = {}
    ranges: list[dict[str, Any]] = []
    baselines = baseline_entries_by_version(package_records)

    for package in package_records:
        version = package["version"]
        acf_entries = effective_acf_entries(package, baselines)
        if acf_entries is None:
            continue

        seen_keys: set[tuple[str, str, str]] = set()
        for acf in acf_entries:
            key = acf_entry_key(acf)
            seen_keys.add(key)
            signature = (
                acf.get("cgYFeet"),
                acf.get("cgZFeet"),
                acf.get("acfVersion"),
                acf.get("fileWriterVersion"),
            )
            current = active.get(key)
            if current is not None and current["_signature"] == signature:
                current["toVersion"] = version
                if package["status"] == "scannedNoAcf" or acf.get("inheritedFromBaseline"):
                    current.setdefault("inferredVersions", []).append(version)
                else:
                    current.setdefault("scannedVersions", []).append(version)
                continue

            if current is not None:
                ranges.append({k: v for k, v in current.items() if k != "_signature"})

            active[key] = {
                "_signature": signature,
                "family": acf["family"],
                "variant": acf["variant"],
                "resolution": acf["resolution"],
                "fromVersion": version,
                "toVersion": version,
                "cgYFeet": acf.get("cgYFeet"),
                "cgZFeet": acf.get("cgZFeet"),
                "acfVersion": acf.get("acfVersion"),
                "fileWriterVersion": acf.get("fileWriterVersion"),
                "firstSourcePackage": package["fileName"],
                "firstAcfPath": acf["acfPath"],
                "firstAcfSha256": acf["acfSha256"],
                "scannedVersions": [] if acf.get("inheritedFromBaseline") else [version],
                "inferredVersions": [version] if acf.get("inheritedFromBaseline") else [],
            }

        for key, entry in active.items():
            if key not in seen_keys:
                entry["toVersion"] = version
                entry.setdefault("inferredVersions", []).append(version)

    ranges.extend({k: v for k, v in entry.items() if k != "_signature"} for entry in active.values())
    return ranges


def build_package_info(package: FeedPackage, torrent_meta: dict[str, Any]) -> dict[str, Any]:
    return {
        "family": FAMILY,
        "kind": package.kind,
        "version": package.version.display(),
        "baselineVersion": package.version.baseline_display(),
        "fileName": package.file_name,
        "torrentUrl": package.torrent_url,
        "description": package.description,
        "torrent": torrent_meta,
        "status": "notProbed",
        "zipPath": None,
        "packageSha256": None,
        "packageSizeBytes": None,
        "acfEntries": [],
    }


def package_zip_path(package: FeedPackage, package_dirs: list[Path]) -> Path | None:
    for directory in package_dirs:
        candidate = directory / package.file_name
        aria2_sidecar = candidate.with_name(candidate.name + ".aria2")
        if candidate.exists() and candidate.stat().st_size > 0 and not aria2_sidecar.exists():
            return candidate
    return None


def probe_package(
    package: FeedPackage,
    package_info: dict[str, Any],
    lookup_dirs: list[Path],
    package_dir: Path,
    args: argparse.Namespace,
    downloaded_count: list[int],
) -> None:
    if package_info["status"] != "notProbed":
        return

    zip_path = package_zip_path(package, lookup_dirs)
    download_attempt: dict[str, Any] | None = None
    if (
        zip_path is None
        and args.download_missing
        and downloaded_count[0] < args.max_downloads
        and (args.download_kind == "all" or args.download_kind == package.kind)
    ):
        ok, output = run_aria2c(Path(package_info["torrent"]["path"]), package_dir, args.bt_stop_timeout)
        downloaded_count[0] += 1
        download_attempt = {
            "attempted": True,
            "succeeded": ok,
            "outputTail": output,
        }
        zip_path = package_zip_path(package, lookup_dirs)

    if download_attempt is not None:
        package_info["downloadAttempt"] = download_attempt

    if zip_path is None:
        package_info.update(
            {
                "status": "missingZip",
                "zipPath": None,
                "packageSha256": None,
                "packageSizeBytes": None,
                "acfEntries": [],
            }
        )
        return

    status, acf_entries, scan_error = scan_zip(zip_path)
    package_info.update(
        {
            "status": status,
            "zipPath": str(zip_path),
            "packageSha256": sha256_file(zip_path) if status != "missingZip" else None,
            "packageSizeBytes": zip_path.stat().st_size,
            "acfEntries": acf_entries,
        }
    )
    if scan_error:
        package_info["scanError"] = scan_error


def package_groups(package_records: list[dict[str, Any]]) -> dict[str, list[int]]:
    groups: dict[str, list[int]] = {}
    for index, package in enumerate(package_records):
        groups.setdefault(package["baselineVersion"], []).append(index)
    return groups


def latest_probe_indexes(package_records: list[dict[str, Any]]) -> list[int]:
    indexes: set[int] = set()
    for group in package_groups(package_records).values():
        full_indexes = [index for index in group if package_records[index]["kind"] == "fullBaseline"]
        if full_indexes:
            indexes.add(full_indexes[0])
        indexes.add(group[-1])
    return sorted(indexes)


def range_record(package_records: list[dict[str, Any]], left: int, right: int, reason: str) -> dict[str, Any]:
    return {
        "fromVersion": package_records[left]["version"],
        "toVersion": package_records[right]["version"],
        "fromFileName": package_records[left]["fileName"],
        "toFileName": package_records[right]["fileName"],
        "reason": reason,
    }


def run_bisect_strategy(
    package_records: list[dict[str, Any]],
    probe_index: Callable[[int], None],
) -> list[dict[str, Any]]:
    unresolved: list[dict[str, Any]] = []

    def signature_for(index: int) -> dict[str, Any] | None:
        baselines = baseline_entries_by_version(package_records)
        return effective_signature(package_records[index], baselines)

    def refine(left: int, right: int) -> None:
        if right <= left:
            return

        left_signature = signature_for(left)
        right_signature = signature_for(right)
        if left_signature is None or right_signature is None:
            unresolved.append(range_record(package_records, left, right, "missingEndpointSignature"))
            return

        if right == left + 1:
            return

        mid = (left + right) // 2
        probe_index(mid)
        mid_signature = signature_for(mid)
        if mid_signature is None:
            item = range_record(package_records, left, right, "missingMidpointSignature")
            item["midVersion"] = package_records[mid]["version"]
            item["midFileName"] = package_records[mid]["fileName"]
            unresolved.append(item)
            return

        left_id = left_signature["cgSignatureId"]
        mid_id = mid_signature["cgSignatureId"]
        right_id = right_signature["cgSignatureId"]
        if left_id == mid_id == right_id:
            if right - left > 2:
                item = range_record(package_records, left, right, "sameEndpointsSampledOnly")
                item["sampledVersions"] = [
                    package_records[left]["version"],
                    package_records[mid]["version"],
                    package_records[right]["version"],
                ]
                unresolved.append(item)
            return

        refine(left, mid)
        refine(mid, right)

    for group in package_groups(package_records).values():
        if not group:
            continue
        full_indexes = [index for index in group if package_records[index]["kind"] == "fullBaseline"]
        left = full_indexes[0] if full_indexes else group[0]
        right = group[-1]
        probe_index(left)
        if right != left:
            probe_index(right)
            refine(left, right)

    return unresolved


def apply_probe_strategy(
    strategy: str,
    packages: list[FeedPackage],
    package_records: list[dict[str, Any]],
    lookup_dirs: list[Path],
    package_dir: Path,
    args: argparse.Namespace,
) -> list[dict[str, Any]]:
    downloaded_count = [0]

    def probe_index(index: int) -> None:
        probe_package(packages[index], package_records[index], lookup_dirs, package_dir, args, downloaded_count)

    if strategy == "exhaustive":
        for index in range(len(packages)):
            probe_index(index)
        return []

    if strategy == "latest":
        for index in latest_probe_indexes(package_records):
            probe_index(index)
        return [
            range_record(package_records, group[0], group[-1], "latestStrategyDoesNotResolveIntermediateHistory")
            for group in package_groups(package_records).values()
            if len(group) > 2
        ]

    if strategy == "bisect":
        return run_bisect_strategy(package_records, probe_index)

    raise ValueError(f"Unsupported probe strategy: {strategy}")


def build_probe_summary(
    strategy: str,
    package_records: list[dict[str, Any]],
    strategy_unresolved: list[dict[str, Any]],
) -> dict[str, Any]:
    baselines = baseline_entries_by_version(package_records)
    probed: list[tuple[int, dict[str, Any], dict[str, Any] | None]] = []
    for index, package in enumerate(package_records):
        if package["status"] == "notProbed":
            continue
        probed.append((index, package, effective_signature(package, baselines)))

    change_ranges: list[dict[str, Any]] = []
    previous: tuple[int, dict[str, Any], dict[str, Any] | None] | None = None
    for current in probed:
        if previous is not None:
            previous_index, previous_package, previous_signature = previous
            current_index, current_package, current_signature = current
            if (
                previous_signature is not None
                and current_signature is not None
                and previous_signature["cgSignatureId"] != current_signature["cgSignatureId"]
            ):
                change_ranges.append(
                    {
                        "fromVersion": previous_package["version"],
                        "toVersion": current_package["version"],
                        "fromFileName": previous_package["fileName"],
                        "toFileName": current_package["fileName"],
                        "resolution": "exact" if current_index == previous_index + 1 else "bounded",
                        "fromCgSignatureId": previous_signature["cgSignatureId"],
                        "toCgSignatureId": current_signature["cgSignatureId"],
                        "fromAcfSignatureId": previous_signature["acfSignatureId"],
                        "toAcfSignatureId": current_signature["acfSignatureId"],
                    }
                )
        previous = current

    missing_packages = [
        {
            "version": package["version"],
            "fileName": package["fileName"],
            "status": package["status"],
        }
        for package in package_records
        if package["status"] in {"missingZip", "invalidZip"}
    ]

    return {
        "probeStrategy": strategy,
        "probedVersions": [package["version"] for _, package, _ in probed],
        "changeRanges": change_ranges,
        "unresolvedRanges": strategy_unresolved,
        "missingPackages": missing_packages,
    }


def build_catalog(args: argparse.Namespace) -> dict[str, Any]:
    cache_root = Path(args.cache_root).expanduser()
    torrent_dir = cache_root / "torrents"
    package_dir = cache_root / "packages"
    extra_zip_dirs = [Path(path).expanduser() for path in args.zip_dir]
    lookup_dirs = [package_dir, *extra_zip_dirs]

    packages = load_feed(args.feed_url)
    records: list[dict[str, Any]] = []

    for package in packages:
        torrent_meta, _ = load_torrent(package, torrent_dir)
        records.append(build_package_info(package, torrent_meta))

    unresolved_ranges = apply_probe_strategy(args.strategy, packages, records, lookup_dirs, package_dir, args)
    probe_summary = build_probe_summary(args.strategy, records, unresolved_ranges)

    return {
        "schemaVersion": 1,
        "generatedAt": datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z"),
        "feedUrl": args.feed_url,
        "family": FAMILY,
        "cacheRoot": str(cache_root),
        **probe_summary,
        "packageCount": len(records),
        "scannedPackageCount": sum(1 for package in records if package["status"] in {"scanned", "scannedNoAcf"}),
        "acfEntryCount": sum(len(package.get("acfEntries", [])) for package in records),
        "packages": records,
        "effectiveBaselines": build_effective_ranges(records),
        "notes": [
            "This catalog records known ACF CG values from scanned Zibo packages.",
            "Cumulative Zibo patches are evaluated as full baseline plus the selected cumulative patch.",
            "missingZip means the feed/torrent was parsed but the package ZIP was not available locally.",
            "scannedNoAcf means the patch was inspected and contained no ACF; effective ACF values inherit from the full baseline.",
            "notProbed means the selected strategy intentionally did not scan that package.",
            "Do not use this catalog alone to authorize Quick View writes; local toolkit baseline state and hashes remain authoritative.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--feed-url", default=DEFAULT_FEED_URL)
    parser.add_argument("--cache-root", default=str(DEFAULT_CACHE_ROOT))
    parser.add_argument("--output", default=str(DEFAULT_OUTPUT))
    parser.add_argument(
        "--strategy",
        choices=["latest", "bisect", "exhaustive"],
        default="exhaustive",
        help="Package probing strategy. exhaustive preserves the original scan-all behavior.",
    )
    parser.add_argument(
        "--zip-dir",
        action="append",
        default=[],
        help="Additional directory containing already downloaded Zibo ZIPs. Can be repeated.",
    )
    parser.add_argument("--download-missing", action="store_true", help="Use aria2c to download missing ZIPs from torrents.")
    parser.add_argument(
        "--download-kind",
        choices=["all", "fullBaseline", "cumulativePatch"],
        default="cumulativePatch",
        help="Which package kind to download when --download-missing is set.",
    )
    parser.add_argument("--max-downloads", type=int, default=1)
    parser.add_argument("--bt-stop-timeout", type=int, default=60)
    parser.add_argument("--pretty", action="store_true", help="Pretty-print JSON output.")
    args = parser.parse_args()

    catalog = build_catalog(args)
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(
        json.dumps(catalog, indent=2 if args.pretty else None, sort_keys=True) + "\n",
        encoding="utf-8",
    )

    missing = sum(1 for package in catalog["packages"] if package["status"] == "missingZip")
    not_probed = sum(1 for package in catalog["packages"] if package["status"] == "notProbed")
    scanned = catalog["scannedPackageCount"]
    acf_count = catalog["acfEntryCount"]
    print(f"wrote {output_path}")
    print(
        f"packages={catalog['packageCount']} strategy={catalog['probeStrategy']} "
        f"scanned={scanned} missingZip={missing} notProbed={not_probed} acfEntries={acf_count}"
    )
    for package in catalog["packages"]:
        if "downloadAttempt" in package:
            status = "ok" if package["downloadAttempt"]["succeeded"] else "failed"
            print(f"download {status}: {package['fileName']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
