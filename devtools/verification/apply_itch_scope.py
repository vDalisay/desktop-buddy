#!/usr/bin/env python3
"""Strip autoloads for source the itch scope compiles out.

The itch build removes whole subsystems at compile time (see
docs/ITCH_HARDENING_PLAN_2026-09-07.md). Autoloads, however, live in project.godot, which is data —
a `#if` cannot reach them. An autoload pointing at a script that is no longer in the assembly is a
hard startup failure, so the export copy needs its project.godot trimmed to match.

The excluded set is NOT repeated here. It is read out of the `<Compile Remove>` entries in
DesktopBuddy.csproj's itch ItemGroup, so the compile list stays the single source of truth and the
two cannot drift apart. Adding a file to that ItemGroup is all it takes to drop its autoload too.

Usage:
    python3 devtools/verification/apply_itch_scope.py <project-root> [--check]

--check reports what would change and exits non-zero if anything would, without writing. CI runs the
write form against the disposable export copy; the check form is useful locally.
"""

from __future__ import annotations

import argparse
import pathlib
import re
import sys

ITCH_ITEMGROUP = re.compile(
    r"<ItemGroup\s+Condition=\"\s*'\$\(DesktopBuddyItchScope\)'\s*==\s*'true'\s*\">(.*?)</ItemGroup>",
    re.DOTALL,
)
COMPILE_REMOVE = re.compile(r"<Compile\s+Remove=\"([^\"]+)\"\s*/>")


def excluded_paths(csproj: pathlib.Path) -> set[str]:
    """Every path the itch scope removes from the compile, normalised to forward slashes."""
    text = csproj.read_text(encoding="utf-8")
    group = ITCH_ITEMGROUP.search(text)
    if group is None:
        raise SystemExit(
            f"{csproj}: no ItemGroup conditioned on DesktopBuddyItchScope == 'true'. "
            "The itch scope's compile list is the source of truth for this script; "
            "if it moved, update this script rather than duplicating the list."
        )
    return {path.replace("\\", "/") for path in COMPILE_REMOVE.findall(group.group(1))}


def is_excluded(script_path: str, excluded: set[str]) -> bool:
    """Whether a res:// autoload target is compiled out.

    Handles the glob form (`src/Testing/**/*.cs`) as a prefix match, and exact file entries
    literally. Anything else is kept: this must never drop an autoload the build still needs.
    """
    for entry in excluded:
        if entry.endswith("/**/*.cs"):
            if script_path.startswith(entry[: -len("**/*.cs")]):
                return True
        elif script_path == entry:
            return True
    return False


def read_exact(path: pathlib.Path) -> str:
    """Read without newline translation, so a rewrite touches only the lines it drops."""
    with path.open("r", encoding="utf-8", newline="") as handle:
        return handle.read()


def write_exact(path: pathlib.Path, text: str) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        handle.write(text)


def rewrite(project_godot: pathlib.Path, excluded: set[str]) -> list[str]:
    """Remove autoload lines whose script is compiled out. Returns the dropped names."""
    lines = read_exact(project_godot).splitlines(keepends=True)
    kept: list[str] = []
    dropped: list[str] = []
    in_autoload = False

    for line in lines:
        stripped = line.strip()
        if stripped.startswith("["):
            in_autoload = stripped == "[autoload]"

        if in_autoload:
            match = re.match(r'^([A-Za-z0-9_]+)="\*?res://(.+)"\s*$', stripped)
            if match and is_excluded(match.group(2), excluded):
                dropped.append(match.group(1))
                continue

        kept.append(line)

    if dropped:
        write_exact(project_godot, "".join(kept))
    return dropped


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=pathlib.Path)
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()

    csproj = args.root / "DesktopBuddy.csproj"
    project_godot = args.root / "project.godot"
    for required in (csproj, project_godot):
        if not required.is_file():
            raise SystemExit(f"{required}: not found")

    excluded = excluded_paths(csproj)
    if args.check:
        original = read_exact(project_godot)
        dropped = rewrite(project_godot, excluded)
        write_exact(project_godot, original)
        for name in dropped:
            print(f"would drop autoload: {name}")
        return 1 if dropped else 0

    dropped = rewrite(project_godot, excluded)
    for name in dropped:
        print(f"dropped autoload: {name}")
    print(f"itch scope: {len(dropped)} autoload(s) dropped, {len(excluded)} compile exclusion(s) read")
    return 0


if __name__ == "__main__":
    sys.exit(main())
