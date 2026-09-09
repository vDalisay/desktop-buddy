#!/usr/bin/env python3
"""Apply the physically reduced Initial Steam Demo scope to a disposable export checkout.

The source of truth for compiled-out C# is the DesktopBuddySteamDemoScope ItemGroup in
DesktopBuddy.csproj. This script handles the Godot/resource half of the same boundary:

* prune held-back Tops, Shoes and Accessories entries from the launch catalogue;
* remove Room Decorator authored resources from the disposable checkout;
* remove C# files that the Steam Demo compile scope excludes (plus their Godot UID sidecars);
* remove any autoload that points at a compiled-out script;
* harden the Windows Steam Demo export filter against re-importing those paths.

Runtime `steam_demo`/`full_release` feature tags remain defense-in-depth only. They are never used
here to decide what implementation exists.
"""

from __future__ import annotations

import argparse
import json
import pathlib
import re
from dataclasses import dataclass

STEAM_DEMO_ITEMGROUP = re.compile(
    r"<ItemGroup\s+Condition=\"\s*'\$\(DesktopBuddySteamDemoScope\)'\s*==\s*'true'\s*\">(.*?)</ItemGroup>",
    re.DOTALL,
)
COMPILE_REMOVE = re.compile(r"<Compile\s+Remove=\"([^\"]+)\"\s*/>")

STEAM_DEMO_PRESET = "Windows Steam Demo"
LAUNCH_CATALOGUE = pathlib.PurePosixPath("data/catalogue/launch_catalogue.tres")
HELD_BACK_CATALOGUE_GLOBS = (
    "data/catalogue/cosmetic_top_*.tres",
    "data/catalogue/cosmetic_shoes_*.tres",
    "data/catalogue/cosmetic_accessories_*.tres",
)
ROOM_DECORATOR_GLOB = "data/environment/*"


@dataclass(frozen=True)
class ScopeInventory:
    compile_patterns: tuple[str, ...]
    compile_files: tuple[str, ...]
    held_back_catalogue: tuple[str, ...]
    room_resources: tuple[str, ...]


def _read(path: pathlib.Path) -> str:
    return path.read_text(encoding="utf-8")


def _write(path: pathlib.Path, text: str) -> None:
    path.write_text(text, encoding="utf-8")


def compile_patterns(csproj: pathlib.Path) -> tuple[str, ...]:
    text = _read(csproj)
    group = STEAM_DEMO_ITEMGROUP.search(text)
    if group is None:
        raise SystemExit(
            f"{csproj}: missing DesktopBuddySteamDemoScope == 'true' ItemGroup; "
            "the csproj compile list is the source of truth"
        )
    values = tuple(dict.fromkeys(value.replace("\\", "/") for value in COMPILE_REMOVE.findall(group.group(1))))
    if not values:
        raise SystemExit(f"{csproj}: Steam Demo compile scope contains no Compile Remove entries")
    return values


def _matches(root: pathlib.Path, pattern: str) -> list[pathlib.Path]:
    return sorted(path for path in root.glob(pattern) if path.is_file())


def inventory(root: pathlib.Path) -> ScopeInventory:
    patterns = compile_patterns(root / "DesktopBuddy.csproj")
    compiled_out: list[str] = []
    for pattern in patterns:
        compiled_out.extend(path.relative_to(root).as_posix() for path in _matches(root, pattern))

    held_back: list[str] = []
    for pattern in HELD_BACK_CATALOGUE_GLOBS:
        matches = _matches(root, pattern)
        if not matches:
            raise SystemExit(
                f"Initial Steam Demo held-back category pattern {pattern!r} matched nothing; "
                "review the authoritative release scope before changing this inventory"
            )
        held_back.extend(path.relative_to(root).as_posix() for path in matches)

    room = [path.relative_to(root).as_posix() for path in _matches(root, ROOM_DECORATOR_GLOB)]
    if not room:
        raise SystemExit(
            "Initial Steam Demo Room Decorator inventory is empty; review the release scope before "
            "changing data/environment"
        )

    return ScopeInventory(
        compile_patterns=patterns,
        compile_files=tuple(sorted(set(compiled_out))),
        held_back_catalogue=tuple(sorted(set(held_back))),
        room_resources=tuple(sorted(set(room))),
    )


def _is_compiled_out(script_path: str, patterns: tuple[str, ...]) -> bool:
    path = pathlib.PurePosixPath(script_path)
    for pattern in patterns:
        if path.match(pattern):
            return True
    return False


def prune_autoloads(project_godot: pathlib.Path, patterns: tuple[str, ...]) -> list[str]:
    lines = _read(project_godot).splitlines(keepends=True)
    kept: list[str] = []
    dropped: list[str] = []
    in_autoload = False
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("["):
            in_autoload = stripped == "[autoload]"
        if in_autoload:
            match = re.match(r'^([A-Za-z0-9_]+)="\*?res://(.+)"\s*$', stripped)
            if match and _is_compiled_out(match.group(2), patterns):
                dropped.append(match.group(1))
                continue
        kept.append(line)
    if dropped:
        _write(project_godot, "".join(kept))
    return dropped


def prune_launch_catalogue(catalogue: pathlib.Path, held_back: set[str]) -> list[str]:
    text = _read(catalogue)
    lines = text.splitlines(keepends=True)
    ext_pattern = re.compile(
        r'^\[ext_resource\s+type="Resource"\s+path="res://([^"]+)"\s+id="([^"]+)"\][ \t]*$'
    )
    removed: dict[str, str] = {}
    kept: list[str] = []
    for line in lines:
        match = ext_pattern.match(line.strip())
        if match and match.group(1) in held_back:
            path, resource_id = match.groups()
            removed[path] = resource_id
            continue
        kept.append(line)

    if not removed:
        raise SystemExit(f"{catalogue}: none of the held-back Steam Demo resources were referenced")

    rewritten = "".join(kept)
    entries = re.search(r'(?m)^Entries = Array\[Resource\]\(\[(.*)\]\)[ \t]*$', rewritten)
    if entries is None:
        raise SystemExit(f"{catalogue}: could not locate Entries array")

    parts = [part.strip() for part in entries.group(1).split(",") if part.strip()]
    removed_tokens = {f'ExtResource("{resource_id}")' for resource_id in removed.values()}
    missing_tokens = sorted(token for token in removed_tokens if token not in parts)
    if missing_tokens:
        raise SystemExit(f"{catalogue}: held-back ext_resource not present in Entries: {', '.join(missing_tokens)}")
    parts = [part for part in parts if part not in removed_tokens]
    rewritten = rewritten[: entries.start(1)] + ", ".join(parts) + rewritten[entries.end(1) :]

    header = re.search(r'(?m)^(\[gd_resource[^\n]*\bload_steps=)(\d+)(\b[^\n]*\])[ \t]*$', rewritten)
    if header is None:
        raise SystemExit(f"{catalogue}: could not locate load_steps")
    new_steps = int(header.group(2)) - len(removed)
    if new_steps <= 0:
        raise SystemExit(f"{catalogue}: invalid load_steps after pruning")
    rewritten = rewritten[: header.start(2)] + str(new_steps) + rewritten[header.end(2) :]

    for path, resource_id in removed.items():
        if path in rewritten or f'ExtResource("{resource_id}")' in rewritten:
            raise SystemExit(f"{catalogue}: held-back reference survived pruning: {path}")
    _write(catalogue, rewritten)
    return sorted(removed)


def _preset_metadata(text: str, preset_name: str) -> tuple[int, int, str]:
    # Never use \s here: in multiline regexes it also consumes newlines and can swallow metadata
    # lines between the preset header and options block. Preset syntax is line-oriented.
    starts = list(re.finditer(r"(?m)^\[preset\.\d+\][ \t]*$", text))
    for index, start_match in enumerate(starts):
        block_end = starts[index + 1].start() if index + 1 < len(starts) else len(text)
        block = text[start_match.start() : block_end]
        if not re.search(rf'(?m)^name="{re.escape(preset_name)}"[ \t]*$', block):
            continue
        options = re.search(r"(?m)^\[preset\.\d+\.options\][ \t]*$", block)
        metadata_end = start_match.start() + (options.start() if options else len(block))
        return start_match.start(), metadata_end, text[start_match.start() : metadata_end]
    raise SystemExit(f"export preset {preset_name!r} not found")


def rewrite_demo_export_filter(export_presets: pathlib.Path, patterns: tuple[str, ...]) -> None:
    text = _read(export_presets)
    start, end, block = _preset_metadata(text, STEAM_DEMO_PRESET)
    match = re.search(r'(?m)^exclude_filter="([^"]*)"[ \t]*$', block)
    if match is None:
        raise SystemExit(f"{export_presets}: Steam Demo preset has no exclude_filter")

    existing = {item.strip() for item in match.group(1).split(",") if item.strip()}
    required = set(patterns)
    required.update(f"{pattern}.uid" for pattern in patterns if pattern.endswith(".cs"))
    required.update(HELD_BACK_CATALOGUE_GLOBS)
    required.add(ROOM_DECORATOR_GLOB)
    merged = existing | required
    replacement = 'exclude_filter="' + ", ".join(sorted(merged)) + '"'
    new_block = block[: match.start()] + replacement + block[match.end() :]
    _write(export_presets, text[:start] + new_block + text[end:])


def delete_inventory(root: pathlib.Path, inv: ScopeInventory) -> list[str]:
    removed: list[str] = []
    for relative in (*inv.compile_files, *inv.held_back_catalogue, *inv.room_resources):
        path = root / relative
        if path.is_file():
            path.unlink()
            removed.append(relative)
        uid = pathlib.Path(str(path) + ".uid")
        if uid.is_file():
            uid.unlink()
            removed.append(uid.relative_to(root).as_posix())
    return sorted(set(removed))


def _demo_filter(export_presets: pathlib.Path) -> set[str]:
    text = _read(export_presets)
    _, _, block = _preset_metadata(text, STEAM_DEMO_PRESET)
    match = re.search(r'(?m)^exclude_filter="([^"]*)"[ \t]*$', block)
    if match is None:
        raise SystemExit(f"{export_presets}: Steam Demo preset has no exclude_filter")
    return {item.strip() for item in match.group(1).split(",") if item.strip()}


def check_applied(root: pathlib.Path) -> None:
    patterns = compile_patterns(root / "DesktopBuddy.csproj")
    failures: list[str] = []
    for pattern in patterns:
        survivors = _matches(root, pattern)
        if survivors:
            failures.append(f"compiled-out source survived: {pattern}: {survivors[0]}")
    for pattern in HELD_BACK_CATALOGUE_GLOBS:
        survivors = _matches(root, pattern)
        if survivors:
            failures.append(f"held-back catalogue resource survived: {survivors[0]}")
    room = _matches(root, ROOM_DECORATOR_GLOB)
    if room:
        failures.append(f"Room Decorator resource survived: {room[0]}")

    catalogue_text = _read(root / LAUNCH_CATALOGUE)
    for prefix in ("cosmetic_top_", "cosmetic_shoes_", "cosmetic_accessories_"):
        if prefix in catalogue_text:
            failures.append(f"launch catalogue still references held-back category: {prefix}")

    required_filter = set(patterns) | set(HELD_BACK_CATALOGUE_GLOBS) | {ROOM_DECORATOR_GLOB}
    current_filter = _demo_filter(root / "export_presets.cfg")
    missing = sorted(required_filter - current_filter)
    if missing:
        failures.append("Steam Demo export filter is missing: " + ", ".join(missing))

    project_text = _read(root / "project.godot")
    in_autoload = False
    for line in project_text.splitlines():
        stripped = line.strip()
        if stripped.startswith("["):
            in_autoload = stripped == "[autoload]"
            continue
        if not in_autoload:
            continue
        match = re.match(r'^([A-Za-z0-9_]+)="\*?res://(.+)"\s*$', stripped)
        if match and _is_compiled_out(match.group(2), patterns):
            failures.append(f"autoload still points at compiled-out source: {match.group(1)}")

    if failures:
        raise SystemExit("Steam Demo scope verification failed:\n- " + "\n- ".join(failures))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", nargs="?", default=".", type=pathlib.Path)
    parser.add_argument("--check", action="store_true", help="verify an already-scoped checkout")
    parser.add_argument("--report", type=pathlib.Path, help="write a non-secret JSON scope inventory")
    args = parser.parse_args()

    root = args.root.resolve()
    required = (
        root / "DesktopBuddy.csproj",
        root / "project.godot",
        root / "export_presets.cfg",
        root / LAUNCH_CATALOGUE,
    )
    for path in required:
        if not path.is_file():
            raise SystemExit(f"{path}: not found")

    if args.check:
        check_applied(root)
        print("Steam Demo physical scope verified.")
        return 0

    inv = inventory(root)
    dropped_autoloads = prune_autoloads(root / "project.godot", inv.compile_patterns)
    pruned_catalogue = prune_launch_catalogue(root / LAUNCH_CATALOGUE, set(inv.held_back_catalogue))
    rewrite_demo_export_filter(root / "export_presets.cfg", inv.compile_patterns)
    removed_files = delete_inventory(root, inv)
    check_applied(root)

    report = {
        "scope": "initial-steam-demo",
        "compile_patterns": list(inv.compile_patterns),
        "compiled_out_sources": list(inv.compile_files),
        "held_back_catalogue_resources": list(inv.held_back_catalogue),
        "room_decorator_resources": list(inv.room_resources),
        "dropped_autoloads": dropped_autoloads,
        "pruned_catalogue_resources": pruned_catalogue,
        "removed_files": removed_files,
    }
    if args.report:
        report_path = args.report if args.report.is_absolute() else root / args.report
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    print(
        "Applied Initial Steam Demo physical scope: "
        f"{len(inv.compile_files)} source file(s), "
        f"{len(inv.held_back_catalogue)} held-back catalogue resource(s), "
        f"{len(inv.room_resources)} Room Decorator resource(s) removed."
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
