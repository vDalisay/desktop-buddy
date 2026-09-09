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

DEMO_FORBIDDEN_PCK_MARKERS = (
    b"data/environment/",
    b"cosmetic_top_",
    b"cosmetic_shoes_",
    b"cosmetic_accessories_",
    b"EnvironmentDecorator.cs",
    b"EnvironmentDecorationLayer.cs",
    b"EnvironmentDecorationRegistry.cs",
)

# These paths are stable shared-surface sentinels in the exported Godot resource directory.
DEMO_REQUIRED_PCK_MARKERS = (
    b"EnvironmentBackgroundEditor.cs",
    b"WorkshopPanel.cs",
)

FULL_REQUIRED_PCK_MARKERS = (
    b"data/environment/launch_decorations.tres",
)


def contains_marker(path: pathlib.Path, marker: bytes, chunk_size: int = 1024 * 1024) -> bool:
    overlap = max(0, len(marker) - 1)
    tail = b""
    with path.open("rb") as handle:
        while True:
            chunk = handle.read(chunk_size)
            if not chunk:
                return False
            block = tail + chunk
            if marker in block:
                return True
            tail = block[-overlap:] if overlap else b""


def assembly_identifiers(path: pathlib.Path) -> set[bytes]:
    # Match whole names: the shared EnvironmentDecoratorPreferences property is not the
    # excluded EnvironmentDecorator class. PCK paths deliberately keep substring matching.
    return set(re.findall(rb"[A-Za-z_][A-Za-z_0-9]*", path.read_bytes()))


def require_markers(path: pathlib.Path, markers: tuple[bytes, ...], label: str,
                    identifiers: set[bytes] | None = None) -> None:
    missing = [marker.decode("ascii") for marker in markers
               if not (marker in identifiers if identifiers is not None else contains_marker(path, marker))]
    if missing:
        raise SystemExit(f"{label} is missing required scope markers:\n  " + "\n  ".join(missing))


def forbid_markers(path: pathlib.Path, markers: tuple[bytes, ...], label: str,
                   identifiers: set[bytes] | None = None) -> None:
    leaked = [marker.decode("ascii") for marker in markers
              if (marker in identifiers if identifiers is not None else contains_marker(path, marker))]
    if leaked:
        raise SystemExit(f"{label} contains physically excluded scope markers:\n  " + "\n  ".join(leaked))


def verify_pck_header(path: pathlib.Path) -> None:
    with path.open("rb") as handle:
        header = handle.read(20)
    if len(header) < 20 or header[:4] != b"GDPC":
        raise SystemExit(f"{path}: not a standalone Godot PCK")
    version, major, minor, patch = struct.unpack_from("<IIII", header, 4)
    if version not in (2, 3, 4):
        raise SystemExit(f"{path}: unsupported/unexpected PCK version {version}")
    print(f"PCK header: version={version}, Godot={major}.{minor}.{patch}")


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
    require_markers(assembly, SHARED_REQUIRED_ASSEMBLY_MARKERS, "DesktopBuddy managed assembly", identifiers)
    if args.target == "demo":
        forbid_markers(assembly, DEMO_FORBIDDEN_ASSEMBLY_MARKERS, "Initial Steam Demo managed assembly", identifiers)
    else:
        require_markers(assembly, FULL_REQUIRED_ASSEMBLY_MARKERS, "Full Release managed assembly", identifiers)

    if args.pck is not None:
        pck = args.pck.resolve()
        if not pck.is_file():
            raise SystemExit(f"{pck}: exported PCK not found")
        verify_pck_header(pck)
        if args.target == "demo":
            forbid_markers(pck, DEMO_FORBIDDEN_PCK_MARKERS, "Initial Steam Demo PCK")
            require_markers(pck, DEMO_REQUIRED_PCK_MARKERS, "Initial Steam Demo PCK")
        else:
            require_markers(pck, FULL_REQUIRED_PCK_MARKERS, "Full Release PCK")

    print(f"Steam {args.target} physical scope verification passed.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
