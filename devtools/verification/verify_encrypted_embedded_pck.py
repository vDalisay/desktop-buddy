#!/usr/bin/env python3
"""Verify that a Windows export contains one encrypted PCK embedded in its executable."""

from __future__ import annotations

import argparse
from pathlib import Path
import struct

MAGIC = b"GDPC"
PACK_DIR_ENCRYPTED = 1 << 0
PLAINTEXT_RESOURCE_SENTINELS = (b"tool.pistol", b"tool.baseball")


def embedded_pck(path: Path) -> tuple[int, int, int]:
    size = path.stat().st_size
    if size < 36:
        raise SystemExit(f"{path}: too small to contain an embedded PCK")

    with path.open("rb") as handle:
        handle.seek(-12, 2)
        pack_size, footer_magic = struct.unpack("<Q4s", handle.read(12))
        start = size - 12 - pack_size
        if footer_magic != MAGIC or start < 0:
            raise SystemExit(f"{path}: missing or invalid embedded PCK footer")
        handle.seek(start)
        header = handle.read(24)

    if len(header) != 24 or header[:4] != MAGIC:
        raise SystemExit(f"{path}: embedded PCK header is missing")
    version, major, minor, patch, flags = struct.unpack_from("<IIIII", header, 4)
    if version not in (2, 3, 4):
        raise SystemExit(f"{path}: unsupported embedded PCK version {version}")
    if (major, minor, patch) != (4, 6, 1):
        raise SystemExit(f"{path}: expected Godot 4.6.1 pack, found {major}.{minor}.{patch}")
    if not flags & PACK_DIR_ENCRYPTED:
        raise SystemExit(f"{path}: embedded PCK directory is not encrypted")
    return start, pack_size, flags


def contains(path: Path, marker: bytes) -> bool:
    overlap = len(marker) - 1
    tail = b""
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            block = tail + chunk
            if marker in block:
                return True
            tail = block[-overlap:] if overlap else b""
    return False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("executable", type=Path)
    args = parser.parse_args()
    executable = args.executable.resolve()
    if not executable.is_file():
        raise SystemExit(f"{executable}: exported executable not found")
    if executable.with_suffix(".pck").exists():
        raise SystemExit(f"{executable}: loose PCK fallback is forbidden")

    start, size, _ = embedded_pck(executable)
    visible = [marker.decode() for marker in PLAINTEXT_RESOURCE_SENTINELS if contains(executable, marker)]
    if visible:
        raise SystemExit("encrypted export exposes plaintext resource sentinels:\n  " + "\n  ".join(visible))

    print(f"Encrypted embedded PCK verified: offset={start}, size={size}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
