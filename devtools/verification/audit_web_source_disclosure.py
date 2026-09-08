#!/usr/bin/env python3
"""Fail if a public Web artifact exposes Desktop Buddy source/build explanation material.

This is intentionally an *artifact* audit. C# comments disappear during compilation, so stripping
comments from a disposable source tree does not improve the shipped binary and can introduce parser
or source-generator regressions. The meaningful invariant is that no source, generated XML docs,
debug mapping directives, or characteristic source-comment/build text reaches the browser package.

Usage:
    python3 devtools/verification/audit_web_source_disclosure.py --root build/itch-web
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

FORBIDDEN_SUFFIXES = {
    ".cs",
    ".csproj",
    ".sln",
    ".props",
    ".targets",
    ".pdb",
    ".map",
    ".md",
    ".rst",
    ".ps1",
    ".bat",
}

# These phrases are deliberately chosen from comments/build configuration, not legitimate player
# strings. Seeing one in a final artifact means source/build explanation material leaked through a
# packaging or documentation path.
SOURCE_EXPLANATION_MARKERS = (
    b"/// <summary>",
    b"owner instruction",
    b"owner confirmation",
    b"docs/ITCH_HARDENING_PLAN",
    b"<Compile Remove=",
    b"DesktopBuddyItchScope",
    b"DesktopBuddyShippingBuild",
    b"DefaultItemExcludes",
)

MAPPING_DIRECTIVE = re.compile(
    rb"(?m)//[#@][ \t]*(?:sourceMappingURL|sourceURL)=|/\*[#@][ \t]*(?:sourceMappingURL|sourceURL)="
)


def scan_markers(path: pathlib.Path, markers: tuple[bytes, ...]) -> set[bytes]:
    """Scan a file once while handling markers split across chunk boundaries."""
    if not markers:
        return set()
    longest = max(len(marker) for marker in markers)
    tail = b""
    found: set[bytes] = set()

    with path.open("rb") as handle:
        while True:
            chunk = handle.read(1024 * 1024)
            if not chunk:
                break
            block = tail + chunk
            for marker in markers:
                if marker not in found and marker in block:
                    found.add(marker)
            if len(found) == len(markers):
                break
            tail = block[-(longest - 1) :] if longest > 1 else b""
    return found


def contains_mapping_directive(path: pathlib.Path) -> bool:
    # Mapping directives are only meaningful in generated browser text. Keep the scan bounded and
    # avoid decoding minified JavaScript just to perform a byte-level check.
    if path.suffix.lower() not in {".js", ".css"}:
        return False
    return MAPPING_DIRECTIVE.search(path.read_bytes()) is not None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=pathlib.Path)
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        raise SystemExit(f"{root}: artifact directory not found")

    forbidden_files: list[str] = []
    mapping_files: list[str] = []
    explanation_hits: list[str] = []

    files = [path for path in sorted(root.rglob("*")) if path.is_file()]
    for path in files:
        relative = path.relative_to(root).as_posix()
        suffix = path.suffix.lower()

        if suffix in FORBIDDEN_SUFFIXES:
            forbidden_files.append(relative)
        if suffix == ".xml" and path.stem.startswith("DesktopBuddy"):
            forbidden_files.append(relative)

        if contains_mapping_directive(path):
            mapping_files.append(relative)

        for marker in sorted(scan_markers(path, SOURCE_EXPLANATION_MARKERS)):
            explanation_hits.append(f"{relative}: {marker.decode('utf-8', errors='replace')}")

    problems: list[str] = []
    if forbidden_files:
        problems.append(
            "source/debug/documentation files:\n  " + "\n  ".join(sorted(set(forbidden_files)))
        )
    if mapping_files:
        problems.append(
            "debug source mapping directives:\n  " + "\n  ".join(sorted(set(mapping_files)))
        )
    if explanation_hits:
        problems.append(
            "source/build explanation markers:\n  " + "\n  ".join(sorted(set(explanation_hits)))
        )

    if problems:
        raise SystemExit("public Web artifact disclosure audit failed:\n" + "\n".join(problems))

    print(
        f"public Web source-disclosure audit passed: {len(files)} file(s) scanned; "
        "no source/comments/XML docs/debug mappings found"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
