#!/usr/bin/env python3
"""Fail closed when an itch.io Web export contains material the public build must not ship.

This audit intentionally works on the final browser directory rather than trusting source settings.
It verifies the outer Web package, the AOT game assemblies and (when enabled) the Godot PCK
container itself.

Usage:
    python3 devtools/verification/audit_itch_artifact.py --root build/itch-web
    python3 devtools/verification/audit_itch_artifact.py --root build/itch-web --expect-encrypted-pck

No third-party Python packages are required.
"""

from __future__ import annotations

import argparse
import pathlib
import struct
import sys
from dataclasses import dataclass

FORBIDDEN_OUTER_SUFFIXES = {
    ".pdb",
    ".map",
    ".cs",
    ".csproj",
    ".sln",
    ".props",
    ".targets",
    ".ps1",
    ".bat",
}

# Exact implementation names from source that the reduced itch assembly physically compiles out.
# Runtime policy names such as DemoScope.IncludesWorkMode are deliberately NOT included because
# those defense-in-depth gates remain in the reduced assembly by design.
FORBIDDEN_GAME_ASSEMBLY_MARKERS = (
    b"EnvironmentCustomizationBootstrap",
    b"BuddyStudioWorkspace",
    b"WorkshopPanel",
    b"WorkCompanionCoordinator",
    b"RoomShareExporter",
    b"CharacterShareExporter",
)

# These are intentionally absent resources, not merely hidden catalogue rows.
HELD_BACK_RESOURCE_MARKERS = (
    b"cosmetic.tops.utility_bib",
    b"cosmetic.shoes.soft_steps",
    b"cosmetic_top_utility_bib.tres",
    b"cosmetic_shoes_soft_steps.tres",
)

# Known strings that are present in today's legitimate unencrypted PCK. When every resource is
# encrypted, these should no longer be visible in plaintext. Requiring multiple independent
# markers makes this a useful regression gate against accidentally encrypting only the directory.
PLAINTEXT_RESOURCE_SENTINELS = (
    b"tool.pistol",
    b"tool.baseball",
)

PACK_DIR_ENCRYPTED = 1 << 0


@dataclass(frozen=True)
class PckHeader:
    version: int
    godot_major: int
    godot_minor: int
    godot_patch: int
    flags: int

    @property
    def directory_encrypted(self) -> bool:
        return bool(self.flags & PACK_DIR_ENCRYPTED)


def read_pck_header(path: pathlib.Path) -> PckHeader:
    with path.open("rb") as handle:
        header = handle.read(24)
    if len(header) < 24 or header[:4] != b"GDPC":
        raise SystemExit(f"{path}: not a standalone Godot PCK (missing GDPC header)")
    version, major, minor, patch, flags = struct.unpack_from("<IIIII", header, 4)
    if version not in (2, 3, 4):
        raise SystemExit(f"{path}: unsupported/unexpected PCK format version {version}")
    return PckHeader(version, major, minor, patch, flags)


def contains_marker(path: pathlib.Path, marker: bytes, chunk_size: int = 1024 * 1024) -> bool:
    """Search a binary file without loading a potentially large WASM into memory."""
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


def require_layout(root: pathlib.Path) -> tuple[pathlib.Path, list[pathlib.Path]]:
    index = root / "index.html"
    framework = root / "_framework"
    pck = root / "index.pck"
    for required in (index, framework, pck):
        if not required.exists():
            raise SystemExit(f"itch artifact is incomplete: missing {required.relative_to(root)}")

    wasm_files = sorted(root.rglob("*.wasm"))
    if not wasm_files:
        raise SystemExit("itch artifact is incomplete: no WebAssembly files were exported")

    game_wasm = sorted(framework.glob("DesktopBuddy*.wasm"))
    if not game_wasm:
        raise SystemExit("itch artifact is incomplete: no DesktopBuddy AOT WebAssembly was exported")
    return pck, game_wasm


def audit_outer_files(root: pathlib.Path) -> None:
    forbidden: list[str] = []
    for path in root.rglob("*"):
        if not path.is_file():
            continue
        if path.suffix.lower() in FORBIDDEN_OUTER_SUFFIXES:
            forbidden.append(path.relative_to(root).as_posix())
    if forbidden:
        raise SystemExit(
            "itch artifact contains source/debug/build material:\n  " + "\n  ".join(sorted(forbidden))
        )


def audit_game_wasm(game_wasm: list[pathlib.Path]) -> None:
    leaked: list[str] = []
    for marker in FORBIDDEN_GAME_ASSEMBLY_MARKERS:
        for path in game_wasm:
            if contains_marker(path, marker):
                leaked.append(f"{marker.decode()} in {path.name}")
                break
    if leaked:
        raise SystemExit(
            "itch AOT assembly still contains compile-excluded implementation markers:\n  "
            + "\n  ".join(leaked)
        )


def audit_pck(path: pathlib.Path, expect_encrypted: bool) -> PckHeader:
    header = read_pck_header(path)

    leaked_held_back = [
        marker.decode()
        for marker in HELD_BACK_RESOURCE_MARKERS
        if contains_marker(path, marker)
    ]
    if leaked_held_back:
        raise SystemExit(
            "itch PCK contains full-release-only authored resources/content IDs:\n  "
            + "\n  ".join(leaked_held_back)
        )

    if expect_encrypted:
        if not header.directory_encrypted:
            raise SystemExit(
                "itch PCK directory is plaintext; expected encrypt_directory=true with a keyed custom template"
            )

        visible = [
            marker.decode()
            for marker in PLAINTEXT_RESOURCE_SENTINELS
            if contains_marker(path, marker)
        ]
        if visible:
            raise SystemExit(
                "itch PCK still exposes known resource contents in plaintext despite encryption being required:\n  "
                + "\n  ".join(visible)
            )

        # Directory encryption should also hide resource paths. This catches a malformed/partial
        # encrypted export even if the content sentinels happen to change in the future.
        if contains_marker(path, b"res://src/") or contains_marker(path, b"src/Environment/"):
            raise SystemExit("itch PCK still exposes plaintext resource-directory paths")

    return header


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=pathlib.Path)
    parser.add_argument("--expect-encrypted-pck", action="store_true")
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        raise SystemExit(f"{root}: artifact directory not found")

    pck, game_wasm = require_layout(root)
    audit_outer_files(root)
    audit_game_wasm(game_wasm)
    header = audit_pck(pck, args.expect_encrypted_pck)

    print(
        "itch artifact audit passed: "
        f"PCK v{header.version} Godot {header.godot_major}.{header.godot_minor}.{header.godot_patch}, "
        f"directory_encrypted={str(header.directory_encrypted).lower()}, "
        f"game_wasm={len(game_wasm)}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
