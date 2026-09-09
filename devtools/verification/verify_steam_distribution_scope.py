#!/usr/bin/env python3
"""Verify that Steam Demo/full binaries match the authoritative physical distribution scope.

This deliberately inspects the managed assembly *before* any future AOT/renaming stage and the
final standalone Godot PCK after export. Runtime Godot feature tags are not accepted as evidence.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import struct
import sys

DEMO_FORBIDDEN_ASSEMBLY_MARKERS = (
    b"DecorationLightProfileResource",
    b"EnvironmentDecorationCatalogueResource",
    b"EnvironmentDecorationLayer",
    b"EnvironmentDecorationPresenter",
    b"EnvironmentDecorationRegistry",
    b"EnvironmentDecorationResource",
    b"EnvironmentDecorationVisualFactory",
    b"EnvironmentDecorator",
    b"EnvironmentPlacementController",
)

# Initial Steam Demo explicitly keeps these shared systems.
SHARED_REQUIRED_ASSEMBLY_MARKERS = (
    b"EnvironmentBackgroundEditor",
    b"BuddyStudioWorkspace",
    b"WorkshopPanel",
)

FULL_REQUIRED_ASSEMBLY_MARKERS = (
    b"EnvironmentDecorator",
    b"EnvironmentDecorationRegistry",
)

# These are checked against actual PCK file-table paths, never arbitrary payload bytes.
DEMO_FORBIDDEN_PCK_PREFIXES = (
    "data/environment/",
)
DEMO_FORBIDDEN_PCK_BASENAME_PREFIXES = (
    "cosmetic_top_",
    "cosmetic_shoes_",
    "cosmetic_accessories_",
)
DEMO_FORBIDDEN_PCK_BASENAMES = (
    "EnvironmentDecorator.cs",
    "EnvironmentDecorationLayer.cs",
    "EnvironmentDecorationRegistry.cs",
)

FULL_REQUIRED_PCK_PATHS = (
    "data/environment/launch_decorations.tres",
)

PACK_DIR_ENCRYPTED = 1 << 0
MAX_PCK_FILES = 1_000_000
MAX_PCK_PATH_BYTES = 16 * 1024 * 1024


def assembly_identifiers(path: pathlib.Path) -> set[bytes]:
    # Match whole names: the shared EnvironmentDecoratorPreferences property is not the
    # excluded EnvironmentDecorator class.
    return set(re.findall(rb"[A-Za-z_][A-Za-z_0-9]*", path.read_bytes()))


def require_identifiers(identifiers: set[bytes], markers: tuple[bytes, ...], label: str) -> None:
    missing = [marker.decode("ascii") for marker in markers if marker not in identifiers]
    if missing:
        raise SystemExit(f"{label} is missing required scope markers:\n  " + "\n  ".join(missing))


def forbid_identifiers(identifiers: set[bytes], markers: tuple[bytes, ...], label: str) -> None:
    leaked = [marker.decode("ascii") for marker in markers if marker in identifiers]
    if leaked:
        raise SystemExit(f"{label} contains physically excluded scope markers:\n  " + "\n  ".join(leaked))


def _read_exact(handle, size: int, label: str) -> bytes:
    data = handle.read(size)
    if len(data) != size:
        raise SystemExit(f"{label}: truncated PCK directory")
    return data


def _read_u32(handle, label: str) -> int:
    return struct.unpack("<I", _read_exact(handle, 4, label))[0]


def _read_u64(handle, label: str) -> int:
    return struct.unpack("<Q", _read_exact(handle, 8, label))[0]


def _normalize_pck_path(path: str) -> str:
    normalized = path.replace("\\", "/")
    if normalized.startswith("res://"):
        normalized = normalized[6:]
    while normalized.startswith("./"):
        normalized = normalized[2:]
    return normalized.lstrip("/")


def read_pck_directory(path: pathlib.Path) -> set[str]:
    """Return actual file-table paths from a standalone unencrypted Godot PCK.

    Raw byte searches are intentionally not used: C# metadata, resource caches, and compiled
    payloads may contain source path strings for files that are not shipped as PCK entries.
    """
    label = str(path)
    file_size = path.stat().st_size
    with path.open("rb") as handle:
        header = _read_exact(handle, 20, label)
        if header[:4] != b"GDPC":
            raise SystemExit(f"{path}: not a standalone Godot PCK")

        version, major, minor, patch = struct.unpack_from("<IIII", header, 4)
        if version not in (2, 3, 4):
            raise SystemExit(f"{path}: unsupported/unexpected PCK version {version}")

        pack_flags = _read_u32(handle, label)
        _file_base = _read_u64(handle, label)

        if version in (3, 4):
            directory_offset = _read_u64(handle, label)
            if directory_offset < handle.tell() or directory_offset >= file_size:
                raise SystemExit(
                    f"{path}: invalid PCK directory offset {directory_offset} for {file_size}-byte pack"
                )
            handle.seek(directory_offset)
        else:
            _read_exact(handle, 16 * 4, label)  # V2 reserved header words.

        file_count = _read_u32(handle, label)
        if file_count > MAX_PCK_FILES:
            raise SystemExit(f"{path}: unreasonable PCK file count {file_count}")
        if pack_flags & PACK_DIR_ENCRYPTED:
            raise SystemExit(
                f"{path}: encrypted PCK directories are not supported by the Steam scope verifier"
            )
        if file_count == 0:
            raise SystemExit(f"{path}: PCK directory is empty")

        paths: set[str] = set()
        for index in range(file_count):
            path_len = _read_u32(handle, label)
            if path_len == 0 or path_len > MAX_PCK_PATH_BYTES:
                raise SystemExit(f"{path}: invalid PCK path length {path_len} at entry {index}")

            raw_path = _read_exact(handle, path_len, label)
            try:
                entry_path = raw_path.decode("utf-8")
            except UnicodeDecodeError as exc:
                raise SystemExit(f"{path}: invalid UTF-8 PCK path at entry {index}: {exc}") from exc

            _read_u64(handle, label)  # file offset
            _read_u64(handle, label)  # file size
            _read_exact(handle, 16, label)  # md5
            _read_u32(handle, label)  # per-file flags

            normalized = _normalize_pck_path(entry_path)
            if not normalized:
                raise SystemExit(f"{path}: empty normalized PCK path at entry {index}")
            paths.add(normalized)

    print(f"PCK header: version={version}, Godot={major}.{minor}.{patch}, files={file_count}")
    return paths


def forbid_demo_pck_paths(paths: set[str]) -> None:
    leaked: list[str] = []
    for entry in sorted(paths):
        basename = entry.rsplit("/", 1)[-1]
        if (
            any(entry.startswith(prefix) for prefix in DEMO_FORBIDDEN_PCK_PREFIXES)
            or any(basename.startswith(prefix) for prefix in DEMO_FORBIDDEN_PCK_BASENAME_PREFIXES)
            or basename in DEMO_FORBIDDEN_PCK_BASENAMES
        ):
            leaked.append(entry)

    if leaked:
        raise SystemExit(
            "Initial Steam Demo PCK contains physically excluded file-table entries:\n  "
            + "\n  ".join(leaked)
        )


def require_pck_paths(paths: set[str], required: tuple[str, ...], label: str) -> None:
    missing = [entry for entry in required if entry not in paths]
    if missing:
        raise SystemExit(f"{label} is missing required file-table entries:\n  " + "\n  ".join(missing))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--target", required=True, choices=("demo", "full"))
    parser.add_argument("--assembly", required=True, type=pathlib.Path)
    parser.add_argument("--pck", type=pathlib.Path)
    args = parser.parse_args()

    assembly = args.assembly.resolve()
    if not assembly.is_file():
        raise SystemExit(f"{assembly}: managed game assembly not found")

    identifiers = assembly_identifiers(assembly)
    require_identifiers(identifiers, SHARED_REQUIRED_ASSEMBLY_MARKERS, "DesktopBuddy managed assembly")
    if args.target == "demo":
        forbid_identifiers(identifiers, DEMO_FORBIDDEN_ASSEMBLY_MARKERS, "Initial Steam Demo managed assembly")
    else:
        require_identifiers(identifiers, FULL_REQUIRED_ASSEMBLY_MARKERS, "Full Release managed assembly")

    if args.pck is not None:
        pck = args.pck.resolve()
        if not pck.is_file():
            raise SystemExit(f"{pck}: exported PCK not found")
        paths = read_pck_directory(pck)
        if args.target == "demo":
            forbid_demo_pck_paths(paths)
        else:
            require_pck_paths(paths, FULL_REQUIRED_PCK_PATHS, "Full Release PCK")

    print(f"Steam {args.target} physical scope verification passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
