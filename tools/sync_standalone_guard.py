#!/usr/bin/env python3
"""Copy the shared standalone guard and catalog policy into one patch repository.

This edits source files only. It does not build packages or publish releases.
Native installer state settings are retained from the destination contract.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("repository", type=Path)
    parser.add_argument("--package-id", required=True)
    args = parser.parse_args()
    destination = args.repository.resolve(strict=True)
    catalog = json.loads((ROOT / "catalog/content-package-catalog.json").read_text())
    policies = [p for p in catalog["ownershipPolicies"] if p["packageId"] == args.package_id]
    if len(policies) != 1:
        raise ValueError("Expected exactly one catalog ownership policy for " + args.package_id)
    path = destination / "standalone-ownership.json"
    contract = json.loads(path.read_text()) if path.exists() else {"schemaVersion": 1}
    if contract.get("schemaVersion") != 1:
        raise ValueError("Unsupported destination ownership contract")
    existing = contract.get("policy", {}).get("packageId")
    if existing is not None and existing != args.package_id:
        raise ValueError("Destination belongs to a different package")
    contract["policy"] = policies[0]
    path.write_text(json.dumps(contract, indent=2) + "\n")
    (destination / "standalone_guard.py").write_bytes((ROOT / "tools/standalone_patch_guard.py").read_bytes())
    print("Updated standalone sources for " + args.package_id + "; no build or publication.")


if __name__ == "__main__":
    main()
