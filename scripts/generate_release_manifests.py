#!/usr/bin/env python3
from __future__ import annotations

import argparse
import hashlib
import json
import sys
from pathlib import Path

IGNORED_PARTS = {"bin", "obj", "publish", "__pycache__", ".git"}
INVENTORY_NAME = "ARTIFACT-INVENTORY.json"
CHECKSUMS_NAME = "SHA256SUMS.txt"
MANIFEST_NAMES = {INVENTORY_NAME, CHECKSUMS_NAME}


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def package_files(root: Path) -> list[Path]:
    files: list[Path] = []
    for path in root.rglob("*"):
        relative = path.relative_to(root)
        if any(part in IGNORED_PARTS for part in relative.parts):
            continue
        if path.is_symlink():
            raise ValueError(f"Release packages must not contain symbolic links: {relative.as_posix()}")
        if not path.is_file():
            continue
        if relative.as_posix() in {INVENTORY_NAME, CHECKSUMS_NAME}:
            continue
        files.append(path)
    return sorted(files, key=lambda item: item.relative_to(root).as_posix())


def build_manifest_bytes(root: Path) -> tuple[bytes, bytes]:
    entries: list[dict[str, object]] = []
    for path in package_files(root):
        relative = path.relative_to(root).as_posix()
        entries.append({
            "path": relative,
            "sha256": sha256_file(path),
            "bytes": path.stat().st_size,
        })

    inventory_bytes = (json.dumps(entries, indent=2, ensure_ascii=False) + "\n").encode("utf-8")
    checksum_lines = [f"{sha256_bytes(inventory_bytes)}  {INVENTORY_NAME}"]
    checksum_lines.extend(f"{entry['sha256']}  {entry['path']}" for entry in entries)
    checksums_bytes = ("\n".join(checksum_lines) + "\n").encode("utf-8")
    return inventory_bytes, checksums_bytes


def write_if_changed(path: Path, data: bytes) -> None:
    if path.is_file() and path.read_bytes() == data:
        return
    temporary = path.with_name(path.name + ".tmp")
    temporary.write_bytes(data)
    temporary.replace(path)


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Generate or verify deterministic Cosmic Transcriber release inventory and SHA-256 manifests.")
    parser.add_argument("--root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--check", action="store_true", help="Verify manifests without changing files.")
    args = parser.parse_args()
    root = args.root.resolve()

    try:
        inventory_bytes, checksums_bytes = build_manifest_bytes(root)
    except (OSError, ValueError) as exc:
        print(f"MANIFEST ERROR: {exc}", file=sys.stderr)
        return 1

    inventory_path = root / INVENTORY_NAME
    checksums_path = root / CHECKSUMS_NAME

    if args.check:
        problems: list[str] = []
        if not inventory_path.is_file():
            problems.append(f"Missing {INVENTORY_NAME}")
        elif inventory_path.read_bytes() != inventory_bytes:
            problems.append(f"{INVENTORY_NAME} is stale or malformed")
        if not checksums_path.is_file():
            problems.append(f"Missing {CHECKSUMS_NAME}")
        elif checksums_path.read_bytes() != checksums_bytes:
            problems.append(f"{CHECKSUMS_NAME} is stale or malformed")
        if problems:
            for problem in problems:
                print(f"MANIFEST CHECK FAILED: {problem}", file=sys.stderr)
            return 1
        print(f"RELEASE MANIFEST CHECK PASSED ({len(json.loads(inventory_bytes))} inventoried artifacts)")
        return 0

    try:
        write_if_changed(inventory_path, inventory_bytes)
        write_if_changed(checksums_path, checksums_bytes)
    except OSError as exc:
        print(f"MANIFEST WRITE FAILED: {exc}", file=sys.stderr)
        return 1

    print(f"RELEASE MANIFESTS GENERATED ({len(json.loads(inventory_bytes))} inventoried artifacts)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
