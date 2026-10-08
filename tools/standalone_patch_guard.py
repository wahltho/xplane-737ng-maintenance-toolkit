"""Offline ownership checks shared by independent aircraft patch installers.

Package identities, paths and markers come from standalone-ownership.json.
This module never edits MTK state or imports MTK backups.
"""
from __future__ import annotations

from contextvars import ContextVar
from functools import wraps
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import stat
import subprocess
import sys
import tempfile
import shutil
import uuid


class OwnershipError(ValueError):
    pass


_active = ContextVar("standalone_patch_guard", default=None)


def digest(data):
    return hashlib.sha256(data).hexdigest() if data is not None else None


def safe(root, relative):
    parts = PurePosixPath(relative).parts
    if (not parts or relative != "/".join(parts) or "\\" in relative
            or any(p in (".", "..") or ":" in p for p in parts)
            or PurePosixPath(relative).is_absolute()):
        raise OwnershipError("Unsafe ownership path: " + str(relative))
    path = root
    for part in parts:
        if path.is_dir():
            for child in path.iterdir():
                if child.name.casefold() == part.casefold() and child.name != part:
                    raise OwnershipError("Case collision: " + str(child))
        path /= part
        if path.is_symlink() or (path.exists() and getattr(path.lstat(), "st_file_attributes", 0)
                                & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 0)):
            raise OwnershipError("Linked ownership path: " + str(path))
    return path


def read(root, relative):
    path = safe(root, relative)
    if not path.exists():
        return None
    if not path.is_file():
        raise OwnershipError("Expected a regular file: " + str(path))
    return path.read_bytes()


def expand(root, pattern):
    if "*" not in pattern:
        safe(root, pattern)
        return [pattern]
    parent, name = pattern.rsplit("/", 1)
    directory = safe(root, parent)
    if not directory.exists():
        return []
    if not directory.is_dir():
        raise OwnershipError("Expected a directory: " + str(directory))
    import fnmatch
    paths = []
    for child in directory.iterdir():
        if name == "**" or fnmatch.fnmatchcase(child.name.casefold(), name.casefold()):
            relative = parent + "/" + child.name
            read(root, relative)  # flat scopes reject directories, links and collisions
            paths.append(relative)
    return sorted(paths)


def covered(pattern, relative):
    import fnmatch
    if pattern.endswith("/**"):
        return relative.casefold().startswith(pattern[:-2].casefold())
    return fnmatch.fnmatchcase(relative.casefold(), pattern.casefold())


def marker_fingerprints(data, policy, relative):
    fingerprints = {}
    rules = [r for r in policy["markerNamespaces"] if r["relativePath"] == relative]
    if data is None or not rules:
        return fingerprints
    text = data.decode("utf-8-sig").replace("\r\n", "\n")
    if "\r" in text or (b"\r\n" in data and data.count(b"\r\n") != data.count(b"\n")):
        raise OwnershipError("Mixed or unsupported line endings: " + relative)
    lines = text.splitlines()
    for rule in rules:
        pairs = {b["beginMarker"]: b["endMarker"] for b in rule["blocks"]}
        ends = set(pairs.values())
        comments = set(rule.get("allowedCommentLines", []))
        opened = None
        seen = set()
        for index, line in enumerate(lines):
            stripped = line.strip()
            if rule["namespace"].casefold() not in stripped.casefold() or not stripped.startswith("--"):
                continue
            if stripped in comments:
                continue
            if stripped in pairs:
                if opened is not None or stripped in seen:
                    raise OwnershipError("Duplicate or nested patch marker: " + relative)
                opened = stripped, index
                seen.add(stripped)
            elif stripped in ends:
                if opened is None or pairs[opened[0]] != stripped:
                    raise OwnershipError("Unmatched patch marker: " + relative)
                key, start = opened
                fingerprints[key] = digest("\n".join(lines[start:index + 1]).encode("utf-8"))
                opened = None
            else:
                raise OwnershipError("Unknown patch marker: " + relative + ": " + stripped)
        if opened is not None:
            raise OwnershipError("Incomplete patch block: " + relative)
    return fingerprints


def has_patch(data, policy, relative):
    if data is None:
        return False
    if marker_fingerprints(data, policy, relative):
        return True
    for signature in policy["signatures"]:
        if signature["relativePath"] == relative and signature["text"].encode() in data:
            return True
    return any(f["relativePath"] == relative and digest(data) in f["sha256"]
               for f in policy["resultFiles"])


def verify_text_manifest(package, package_id):
    """Verify a pipe-delimited package integrity inventory before planning."""
    text = (package / "package-manifest.txt").read_text(encoding="utf-8")
    records = {}
    identity, version = None, None
    for line in text.splitlines():
        fields = line.split("|")
        if fields[:2] == ["package", "id"]:
            identity = fields[2]
        elif fields[:2] == ["package", "version"]:
            version = fields[2]
        elif fields[0] == "payload":
            if len(fields) != 7 or fields[3] != "size" or fields[5] != "sha256" or fields[2] in records:
                raise OwnershipError("Invalid or duplicate package input record")
            records[fields[2]] = int(fields[4]), fields[6]
    if identity != package_id or not version or not records:
        raise OwnershipError("Package identity or integrity inventory is missing")
    for relative, (size, hash_) in records.items():
        data = read(package, relative)
        if data is None or len(data) != size or digest(data) != hash_:
            raise OwnershipError("Package input is missing or changed: " + relative)
    return version


def state_candidates():
    home = Path.home()
    if sys.platform == "win32":
        base = Path(os.environ.get("APPDATA", home / "AppData/Roaming"))
    elif sys.platform == "darwin":
        base = home / "Library/Application Support"
    else:
        base = Path(os.environ.get("XDG_CONFIG_HOME", home / ".config"))
    candidates = [base / "XPlane737NGMaintenanceToolkit/state.json",
                  home / ".xplane-737ng-maintenance-toolkit/XPlane737NGMaintenanceToolkit/state.json"]
    extra = os.environ.get("MTK_STATE_PATH")
    if extra:
        candidates.append(Path(extra).expanduser())
    return list(dict.fromkeys(candidates))


def check_mtk(root, targets, package_id):
    snapshots = {}
    for path in state_candidates():
        path = path.absolute()
        safe(Path(path.anchor), path.relative_to(path.anchor).as_posix())
        raw = path.read_bytes() if path.exists() else None
        snapshots[str(path)] = digest(raw)
        if raw is None:
            continue
        try:
            document = json.loads(raw)
            schema = document.get("SchemaVersion", document.get("schemaVersion"))
            if not isinstance(schema, int) or not 1 <= schema <= 10:
                raise ValueError("unsupported state schema")
            groups = [document.get("ContentInstallations", document.get("contentInstallations", {})),
                      document.get("Aircraft", document.get("aircraft", {}))]
            for group in groups:
                if not isinstance(group, dict):
                    raise ValueError("invalid installation inventory")
                for key, installation in group.items():
                    folder = installation.get("AircraftFolder", installation.get("aircraftFolder"))
                    if not folder:
                        acf = installation.get("AcfPath", installation.get("acfPath"))
                        folder = str(Path(acf).parent) if acf else key
                    if str(Path(folder).resolve()).casefold() != str(root).casefold():
                        continue
                    legacy = installation.get("InstalledContentPackageId", installation.get("installedContentPackageId"))
                    if legacy and not installation.get("ContentComponents", installation.get("contentComponents")):
                        raise OwnershipError("An older MTK ownership record needs recovery in MTK before using standalone.")
                    components = installation.get("ContentComponents", installation.get("contentComponents", {}))
                    if not isinstance(components, dict):
                        raise ValueError("invalid content ownership")
                    for component_key, component in components.items():
                        files = component.get("Files", component.get("files", []))
                        sources = component.get("Sources", component.get("sources", []))
                        same = component_key == package_id or any(
                            source.get("PackageId", source.get("packageId")) == package_id for source in sources)
                        if same and not files:
                            raise OwnershipError("MTK has an incomplete ownership record for this patch; resolve it in MTK first.")
                        for item in files:
                            relative = item.get("RelativePath", item.get("relativePath"))
                            if not isinstance(relative, str):
                                raise ValueError("invalid managed target")
                            if same or any(covered(t, relative) for t in targets):
                                raise OwnershipError("MTK manages this patch or a shared file: " + relative
                                    + ". Use MTK to restore/remove that installation before switching to standalone.")
        except OwnershipError:
            raise
        except (ValueError, TypeError, AttributeError) as error:
            raise OwnershipError("Cannot verify MTK ownership in " + str(path) + ": " + str(error)) from error
    return snapshots


class Guard:
    def __init__(self, root, package, targets=None):
        absolute = Path(root).expanduser().absolute()
        safe(Path(absolute.anchor), absolute.relative_to(absolute.anchor).as_posix())
        self.root = absolute.resolve(strict=True)
        if not self.root.is_dir():
            raise OwnershipError("Aircraft folder does not exist")
        self.package = Path(package)
        raw = (self.package / "standalone-ownership.json").read_bytes()
        self.contract = json.loads(raw)
        if self.contract.get("schemaVersion") != 1:
            raise OwnershipError("Unsupported standalone ownership contract")
        self.policy = self.contract["policy"]
        native = self.contract.get("nativeState")
        if native and native.get("kind") not in ("single", "mapping", "files"):
            raise OwnershipError("Unsupported native receipt format")
        self.package_id = self.policy["packageId"]
        self.targets = list(targets or self.policy["targetPaths"])
        if any(not any(covered(p, t) for p in self.policy["targetPaths"]) for t in self.targets):
            raise OwnershipError("Installer targets differ from the ownership policy")
        self.state_relative = self.contract.get("nativeState", {}).get("path")
        self.receipt_relative = ".patch-ownership/" + self.package_id + "/receipt.json"
        self.lock_relative = ".patch-ownership/" + self.package_id + ".lock"
        self.journal_relative = ".patch-ownership/" + self.package_id + "/transaction"
        if self.state_relative is None:
            self.state_relative = self.receipt_relative
        self.state_raw = read(self.root, self.state_relative)
        self.state = json.loads(self.state_raw) if self.state_raw is not None else None
        self.expected = self.capture()
        self.mtk = check_mtk(self.root, self.targets, self.package_id)
        self.written = {}
        self.last_written = {}
        self.created_dirs = []
        self.removed_dirs = {}
        self.journal = {}
        self.initial_dirs = {}
        for relative in list(self.expected) + [self.state_relative]:
            parent = safe(self.root, relative).parent
            while parent != self.root:
                if parent.exists():
                    self.initial_dirs[parent] = stat.S_IMODE(parent.stat().st_mode)
                parent = parent.parent
        state_parent = safe(self.root, self.state_relative).parent
        if state_parent.exists():
            for directory, _, _ in os.walk(state_parent, followlinks=False):
                directory = Path(directory)
                safe(self.root, directory.relative_to(self.root).as_posix())
                self.initial_dirs[directory] = stat.S_IMODE(directory.stat().st_mode)
        self.token = None
        self.locked = False
        self.lock_bytes = (str(uuid.uuid4()) + "\n").encode("ascii")
        self._verify()

    def capture(self):
        paths = {p for pattern in self.targets for p in expand(self.root, pattern)}
        return {p: read(self.root, p) for p in paths}

    def _verify(self):
        if not self.locked and read(self.root, self.lock_relative) is not None:
            raise OwnershipError("Another standalone operation is active or needs recovery; keep the lock and backups.")
        if not self.journal and safe(self.root, self.journal_relative).exists():
            raise OwnershipError("An unfinished standalone transaction needs recovery; retain its journal and backups.")
        if "nativeState" in self.contract and read(self.root, self.receipt_relative) is not None:
            raise OwnershipError("Two different standalone receipts claim this patch; retain both for recovery.")
        if self.state is not None:
            state_package_id = self.contract.get("nativeState", {}).get("packageId", self.package_id)
            if self.state.get("packageId") != state_package_id or self.state.get("schemaVersion") != self.contract.get("nativeState", {}).get("schemaVersion", 1):
                raise OwnershipError("Unknown or inconsistent standalone state")
            self._verify_backups()
        else:
            for relative in self.policy["standaloneEvidencePaths"]:
                if relative in (self.state_relative, self.lock_relative):
                    continue
                if relative == self.journal_relative and self.journal:
                    continue
                if safe(self.root, relative).exists():
                    raise OwnershipError("Standalone evidence has no complete ownership record: " + relative)
        for relative, data in self.expected.items():
            markers = marker_fingerprints(data, self.policy, relative)
            allowed = next((f["sha256"] for f in self.policy["originalFiles"] if f["relativePath"] == relative), [])
            if self.state is None and has_patch(data, self.policy, relative) and digest(data) not in allowed:
                raise OwnershipError("Patch files have no verified standalone owner: " + relative
                    + ". Restore them with their original installer/backup first; do not delete state or backups.")
            is_payload = any(covered(p, relative) for p in self.policy["payloadPaths"])
            if self.state is None and is_payload and data is not None:
                if digest(data) not in allowed:
                    raise OwnershipError("Unowned companion file: " + relative)
            if self.state is not None and "nativeState" not in self.contract:
                entry = self.state["files"].get(relative)
                if entry is None:
                    if data is not None:
                        raise OwnershipError("File is not in the standalone ownership inventory: " + relative)
                    continue
                if is_payload and digest(data) != entry["installedSha256"]:
                    raise OwnershipError("Standalone payload is missing or changed: " + relative)
                if markers != entry.get("markers", {}):
                    raise OwnershipError("Standalone patch blocks are missing or changed: " + relative)

    def records(self):
        native = self.contract.get("nativeState")
        if native:
            kind = native["kind"]
            if kind == "single":
                records = [dict(relativePath=self.state["targetRelativePath"], originalSha256=self.state["originalSha256"],
                                backupRelativePath=self.state["backupRelativePath"], installedSha256=self.state["installedSha256"])]
            elif kind == "mapping":
                records = [dict(f, relativePath=p,
                                backupRelativePath=native["originalDirectory"] + "/" + p)
                           for p, f in self.state["files"].items()]
            else:
                records = [dict(item, backupRelativePath=self.state["backupRelativePath"] + "/" + item["relativePath"])
                           for item in self.state["files"]]
        else:
            records = [dict(value, relativePath=relative) for relative, value in self.state["files"].items()]
        return records

    def _verify_backups(self):
        records = self.records()
        if not records or len({f["relativePath"] for f in records}) != len(records):
            raise OwnershipError("Incomplete or duplicate standalone ownership inventory")
        for item in records:
            relative = item["relativePath"]
            if not any(covered(p, relative) for p in self.targets):
                raise OwnershipError("Standalone state contains an undeclared target: " + relative)
            expected = item["originalSha256"]
            if expected is not None:
                data = read(self.root, item["backupRelativePath"])
                if digest(data) != expected or ("originalSize" in item and len(data) != item["originalSize"]):
                    raise OwnershipError("Original standalone backup is missing or changed: " + relative)
                allowed = next((f["sha256"] for f in self.policy["originalFiles"]
                                if f["relativePath"] == relative), [])
                if has_patch(data, self.policy, relative) and digest(data) not in allowed:
                    raise OwnershipError("The original backup already contains this patch: " + relative)
        inventory = {f["relativePath"] for f in records}
        if any(data is not None and relative not in inventory for relative, data in self.expected.items()):
            raise OwnershipError("Standalone state omits a present target")

    def recheck(self):
        if self.locked and read(self.root, self.lock_relative) != self.lock_bytes:
            raise OwnershipError("Standalone lock changed or disappeared during the operation")
        if self.capture() != self.expected or read(self.root, self.state_relative) != self.state_raw:
            raise OwnershipError("Aircraft files or standalone state changed during preparation")
        if check_mtk(self.root, self.targets, self.package_id) != self.mtk:
            raise OwnershipError("MTK state changed during preparation")
        if self.state is not None:
            self._verify_backups()

    def recheck_mtk(self):
        if check_mtk(self.root, self.targets, self.package_id) != self.mtk:
            raise OwnershipError("MTK state changed during the standalone operation")

    def __enter__(self):
        self.recheck()
        lock = safe(self.root, self.lock_relative)
        self.make_parents(lock)
        fd = os.open(lock, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, "wb") as stream:
            stream.write(self.lock_bytes)
            stream.flush()
            os.fsync(stream.fileno())
        self.locked = True
        self.token = _active.set(self)
        return self

    def __exit__(self, exc_type, exc, tb):
        try:
            if exc_type is not None:
                self.rollback()
            else:
                try:
                    self.recheck()
                    for relative, data in self.capture().items():
                        marker_fingerprints(data, self.policy, relative)
                    self._verify()
                    if self.state is not None:
                        for record in self.records():
                            relative = record["relativePath"]
                            if relative in self.written and digest(read(self.root, relative)) != record["installedSha256"]:
                                raise OwnershipError("Installed bytes do not match the saved receipt: " + relative)
                except BaseException:
                    self.rollback()
                    raise
        finally:
            if self.token is not None:
                _active.reset(self.token)
            if self.locked:
                if read(self.root, self.lock_relative) != self.lock_bytes:
                    raise OwnershipError("Lock ownership changed; retain the transaction journal for recovery")
                journal = safe(self.root, self.journal_relative)
                if self.journal and journal.exists():
                    shutil.rmtree(journal)
                safe(self.root, self.lock_relative).unlink()
            for directory in sorted(set(self.created_dirs), key=lambda p: len(p.parts), reverse=True):
                try:
                    directory.rmdir()
                except OSError:
                    pass

    def rollback(self):
        if not self.written and not self.removed_dirs:
            return
        errors = []
        for directory, mode in sorted({**self.initial_dirs, **self.removed_dirs}.items(), key=lambda item: len(item[0].parts)):
            try:
                safe(self.root, directory.relative_to(self.root).as_posix())
                if not directory.exists():
                    directory.mkdir(parents=True)
                    directory.chmod(mode)
            except (OSError, OwnershipError) as error:
                errors.append(str(directory) + ": " + str(error))
        for relative, (data, mode) in reversed(list(self.written.items())):
            try:
                current = read(self.root, relative)
                if current == data:
                    continue
                if current != self.last_written.get(relative, data):
                    raise OwnershipError("A concurrent change was retained; manual recovery is required")
                _atomic(safe(self.root, relative), data, mode)
            except (OSError, OwnershipError) as error:
                errors.append(relative + ": " + str(error))
        if errors:
            self.locked = False  # leave the lock visible for recovery
            raise OwnershipError("Rollback needs recovery; retain backups and lock: " + "; ".join(errors))
        state_parent = safe(self.root, self.state_relative).parent
        if state_parent.exists():
            for directory, _, _ in os.walk(state_parent, topdown=False, followlinks=False):
                directory = Path(directory)
                safe(self.root, directory.relative_to(self.root).as_posix())
                if directory not in self.initial_dirs:
                    try:
                        directory.rmdir()
                    except OSError:
                        pass

    def make_parents(self, path):
        missing = []
        parent = path.parent
        while parent != self.root and not parent.exists():
            missing.append(parent)
            parent = parent.parent
        for parent in reversed(missing):
            safe(self.root, parent.relative_to(self.root).as_posix())
            parent.mkdir()
            self.created_dirs.append(parent)

    def remember(self, relative, data, mode):
        """Write the pre-operation bytes to disk before changing a file."""
        if relative in self.written:
            return
        self.written[relative] = data, mode
        backup = self.journal_relative + "/before/" + relative
        if data is not None:
            path = safe(self.root, backup)
            self.make_parents(path)
            _atomic(path, data, mode)
        self.journal[relative] = dict(sha256=digest(data), mode=mode,
                                      backupRelativePath=backup if data is not None else None)
        path = safe(self.root, self.journal_relative + "/journal.json")
        self.make_parents(path)
        _atomic(path, (json.dumps(dict(schemaVersion=1, packageId=self.package_id,
            files=self.journal), indent=2, sort_keys=True) + "\n").encode())

    def original_payload(self, relative):
        if self.state is None or relative not in self.state["files"]:
            raise OwnershipError("No standalone original is recorded for " + relative)
        item = self.state["files"][relative]
        return read(self.root, item["backupRelativePath"]) if item["originalSha256"] is not None else None

    def apply(self, changes, uninstall=False, version=""):
        """Apply an already validated legacy installer's plan, with owned backups."""
        if uninstall and self.state is None:
            raise OwnershipError("No verified standalone receipt; refusing to remove another owner's patch")
        records = dict(self.state["files"]) if self.state else {}
        for relative, data in changes.items():
            if not any(covered(p, relative) for p in self.targets):
                raise OwnershipError("Undeclared planned target: " + relative)
            marker_fingerprints(data, self.policy, relative)
            if uninstall and has_patch(data, self.policy, relative):
                raise OwnershipError("Uninstall left patch evidence: " + relative)
        self.recheck()
        if not uninstall:
            for relative, new in changes.items():
                old = read(self.root, relative)
                if relative not in records:
                    backup = ".patch-ownership/" + self.package_id + "/backups/" + (digest(old) or "absent") + "/" + relative
                    if old is not None:
                        existing = read(self.root, backup)
                        if existing is not None and existing != old:
                            raise OwnershipError("Original backup collision: " + relative)
                        if existing is None:
                            owned_write(safe(self.root, backup), old)
                    records[relative] = dict(originalSha256=digest(old), originalSize=len(old) if old is not None else None,
                                             backupRelativePath=backup)
                records[relative].update(installedSha256=digest(new), markers=marker_fingerprints(new, self.policy, relative))
        for relative, new in changes.items():
            owned_write(safe(self.root, relative), new)
        if uninstall:
            owned_unlink(safe(self.root, self.receipt_relative))
        else:
            raw = (json.dumps(dict(schemaVersion=1, packageId=self.package_id, packageVersion=version, files=records),
                              indent=2, sort_keys=True) + "\n").encode()
            owned_write(safe(self.root, self.receipt_relative), raw)


def _atomic(path, data, mode=0o644):
    if data is None:
        path.unlink(missing_ok=True)
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix=".standalone-write-", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, mode)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def _before(path):
    guard = _active.get()
    if guard is None:
        return None
    path = Path(path).absolute()
    if not path.is_relative_to(guard.root):
        raise OwnershipError("Write outside aircraft folder: " + str(path))
    guard.recheck()
    if not guard.written:
        command = ["tasklist", "/FO", "CSV", "/NH"] if sys.platform == "win32" else ["ps", "-A", "-o", "comm="]
        processes = subprocess.run(command, check=True, capture_output=True, text=True).stdout.casefold()
        if "x-plane" in processes or "xplane.exe" in processes:
            raise OwnershipError("Close X-Plane before changing aircraft files, then restart it afterwards.")
    relative = path.relative_to(guard.root).as_posix()
    safe(guard.root, relative)
    if relative not in guard.written:
        guard.remember(relative, read(guard.root, relative), stat.S_IMODE(path.stat().st_mode) if path.exists() else 0o644)
    guard.make_parents(path)
    return guard, relative


def _after(binding):
    if binding is not None:
        guard, relative = binding
        guard.last_written[relative] = read(guard.root, relative)
        if relative in guard.expected or any(covered(p, relative) for p in guard.targets):
            current = read(guard.root, relative)
            if current is None and relative not in guard.targets:
                guard.expected.pop(relative, None)
            else:
                guard.expected[relative] = current
        if relative == guard.state_relative:
            guard.state_raw = read(guard.root, relative)
            guard.state = json.loads(guard.state_raw) if guard.state_raw is not None else None


def owned_write(path, data):
    binding = _before(path)
    mode = stat.S_IMODE(Path(path).stat().st_mode) if Path(path).exists() else 0o644
    _atomic(Path(path), data, mode)
    _after(binding)


def owned_unlink(path):
    owned_write(path, None)


def owned_mkdir(path, mode=0o777, parents=False, exist_ok=False):
    guard = _active.get()
    path = Path(path).absolute()
    if guard is None:
        return path.mkdir(mode=mode, parents=parents, exist_ok=exist_ok)
    guard.recheck()
    safe(guard.root, path.relative_to(guard.root).as_posix())
    missing = []
    candidate = path
    while candidate != guard.root and not candidate.exists():
        missing.append(candidate)
        if not parents:
            break
        candidate = candidate.parent
    path.mkdir(mode=mode, parents=parents, exist_ok=exist_ok)
    guard.created_dirs.extend(reversed(missing))


def owned_replace(source, destination):
    binding = _before(destination)
    os.replace(source, destination)
    _after(binding)


def owned_copy(source, destination, *args, **kwargs):
    binding = _before(destination)
    result = shutil.copy2(source, destination, *args, **kwargs)
    _after(binding)
    return result


def owned_rmtree(path, ignore_errors=False):
    """Keep the native receipt and backups available if final validation fails."""
    guard = _active.get()
    if guard is None:
        return shutil.rmtree(path, ignore_errors=ignore_errors)
    path = Path(path).absolute()
    if not path.exists():
        if ignore_errors:
            return
        raise FileNotFoundError(path)
    guard.recheck()
    safe(guard.root, path.relative_to(guard.root).as_posix())
    files = []
    for directory, children, names in os.walk(path, followlinks=False):
        directory = Path(directory)
        safe(guard.root, directory.relative_to(guard.root).as_posix())
        guard.removed_dirs.setdefault(directory, stat.S_IMODE(directory.stat().st_mode))
        for child in children:
            safe(guard.root, (directory / child).relative_to(guard.root).as_posix())
        for name in names:
            candidate = directory / name
            relative = candidate.relative_to(guard.root).as_posix()
            guard.remember(relative, read(guard.root, relative), stat.S_IMODE(candidate.stat().st_mode))
            files.append(relative)
    shutil.rmtree(path)
    for relative in files:
        guard.last_written[relative] = None
    if guard.state_relative in files:
        guard.state_raw, guard.state = None, None


def native_operation(function):
    """Guard a native state-based command without replacing its state format."""
    @wraps(function)
    def wrapped(root, manifest, *args, **kwargs):
        if _active.get() is not None:
            return function(root, manifest, *args, **kwargs)
        package = Path(function.__globals__["__file__"]).resolve().parent
        targets = [t["relativePath"] for t in manifest.get("targets", [])]
        if "target" in manifest:
            targets = [manifest["target"]["relativePath"]]
        with Guard(root, package, targets or None):
            return function(root, manifest, *args, **kwargs)
    return wrapped
