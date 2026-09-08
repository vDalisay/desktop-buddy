#!/usr/bin/env python3
"""Remove non-runtime debug breadcrumbs from a generated Web artifact.

C# source comments are not emitted into Desktop Buddy's compiled WebAssembly and the itch target
already disables XML documentation, PDBs, source maps and symbol maps. Rewriting C# source just to
remove comments would therefore add parser/build risk without changing what players receive.

This sanitizer operates on the *generated* browser files instead. It removes only JavaScript/CSS
source mapping/source URL directives and known generated stamp files which have no runtime purpose
in the hardened release. It does not remove general comments because generated Godot/.NET files
carry license/copyright notices that must remain intact.

Usage:
    python3 devtools/verification/sanitize_web_artifact.py --root build/itch-web
    python3 devtools/verification/sanitize_web_artifact.py --root build/itch-web --check
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

TEXT_SUFFIXES = {".js", ".css"}
NON_RUNTIME_FILE_NAMES = {".stamp"}

# Browser debugging directives. These can point developer tooling at source-map/source identities
# even when the actual map is intentionally absent. They are semantically comments and safe to
# remove without touching license comments or executable JavaScript.
LINE_DIRECTIVE = re.compile(
    r"(?m)^[ \t]*//[#@][ \t]*(?:sourceMappingURL|sourceURL)=[^\r\n]*(?:\r?\n|$)"
)
BLOCK_DIRECTIVE = re.compile(
    r"/\*[#@][ \t]*(?:sourceMappingURL|sourceURL)=[^*]*(?:\*(?!/)[^*]*)*\*/",
    re.MULTILINE,
)


def sanitize_text(text: str) -> tuple[str, int]:
    text, line_count = LINE_DIRECTIVE.subn("", text)
    text, block_count = BLOCK_DIRECTIVE.subn("", text)
    return text, line_count + block_count


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", required=True, type=pathlib.Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    root = args.root.resolve()
    if not root.is_dir():
        raise SystemExit(f"{root}: artifact directory not found")

    changed_files: list[tuple[str, int]] = []
    metadata_files: list[str] = []
    total_removed = 0

    for path in sorted(root.rglob("*")):
        if not path.is_file():
            continue

        relative = path.relative_to(root).as_posix()
        if path.name in NON_RUNTIME_FILE_NAMES:
            metadata_files.append(relative)
            if not args.check:
                path.unlink()
            continue

        if path.suffix.lower() not in TEXT_SUFFIXES:
            continue
        try:
            original = path.read_text(encoding="utf-8")
        except UnicodeDecodeError as exc:
            raise SystemExit(f"{path}: expected UTF-8 generated Web text: {exc}") from exc

        sanitized, removed = sanitize_text(original)
        if removed == 0:
            continue

        changed_files.append((relative, removed))
        total_removed += removed
        if not args.check:
            path.write_text(sanitized, encoding="utf-8", newline="")

    if args.check and (changed_files or metadata_files):
        for relative, count in changed_files:
            print(f"would remove {count} debug directive(s): {relative}")
        for relative in metadata_files:
            print(f"would remove non-runtime generated metadata: {relative}")
        return 1

    for relative, count in changed_files:
        print(f"removed {count} debug directive(s): {relative}")
    for relative in metadata_files:
        print(f"removed non-runtime generated metadata: {relative}")
    print(
        f"web artifact sanitizer: {total_removed} debug/source-map directive(s) and "
        f"{len(metadata_files)} generated metadata file(s) removed; "
        "license/general comments preserved"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
