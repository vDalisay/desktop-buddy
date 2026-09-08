#!/usr/bin/env python3
"""Apply the reduced itch.io scope to the disposable export project.

The itch build removes whole subsystems at compile time (see
``docs/ITCH_HARDENING_PLAN_2026-09-07.md``). Godot-authored data lives outside MSBuild and must be
reduced in the same disposable export copy:

* ``project.godot`` autoloads can still point at scripts the itch assembly no longer contains;
* the Godot exporter otherwise keeps 1-byte C# script placeholder resources in the PCK, exposing
  names/paths for implementation that was deliberately compiled out;
* full-release-only catalogue entries must be removed from both the catalogue's ext_resource table
  and its Entries array before their .tres files are excluded, otherwise the exported catalogue
  contains broken dependencies.

The source-code exclusion set is NOT repeated here. It is read from the
``<Compile Remove>`` entries in ``DesktopBuddy.csproj``'s itch ItemGroup and expanded against the
actual disposable project tree. This keeps the csproj as the source of truth. The small explicit
resource list below contains authored full-release-only catalogue entries, which are not MSBuild
compile items and therefore cannot be derived from the csproj.

A hardened release can additionally pass ``--pck-key-file``. That switches only the disposable
itch preset to full PCK + directory encryption and writes the key to Godot's ignored
``.godot/export_credentials.cfg``. The matching export template must have been compiled with the
same key through ``SCRIPT_AES256_ENCRYPTION_KEY``; this script never embeds the key in tracked
configuration or prints it.

Usage:
    python3 devtools/verification/apply_itch_scope.py <project-root> [--check]
    python3 devtools/verification/apply_itch_scope.py <project-root> --pck-key-file /tmp/key
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

ITCH_PRESET_NAME = "Web itch.io experimental"
ITCH_CATALOGUE = "data/catalogue/launch_catalogue.tres"

# Authored resources that are full-release-only but intentionally remain in the source catalogue so
# the full game can consume one shared data set. DemoScope remains a runtime defense-in-depth gate,
# but the itch client should not receive these resources at all.
ITCH_HELD_BACK_RESOURCES = {
    "data/catalogue/cosmetic_top_utility_bib.tres",
    "data/catalogue/cosmetic_shoes_soft_steps.tres",
}


def excluded_paths(csproj: pathlib.Path) -> set[str]:
    """Every path the itch scope removes from compilation, normalised to forward slashes."""
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
    """Whether a ``res://`` autoload target is compiled out."""
    for entry in excluded:
        if entry.endswith("/**/*.cs"):
            if script_path.startswith(entry[: -len("**/*.cs")]):
                return True
        elif script_path == entry:
            return True
    return False


def read_exact(path: pathlib.Path) -> str:
    with path.open("r", encoding="utf-8", newline="") as handle:
        return handle.read()


def write_exact(path: pathlib.Path, text: str) -> None:
    with path.open("w", encoding="utf-8", newline="") as handle:
        handle.write(text)


def rewrite_project_godot(project_godot: pathlib.Path, excluded: set[str]) -> list[str]:
    """Remove autoload lines whose scripts are compiled out. Returns dropped autoload names."""
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


def prune_held_back_catalogue(root: pathlib.Path) -> list[str]:
    """Remove held-back resource references from the disposable launch catalogue atomically.

    Excluding a referenced .tres alone is unsafe: Godot may either force the dependency back into
    the export or leave the catalogue with a missing external resource. We therefore remove the
    matching ext_resource declarations and every reference to their IDs from ``Entries`` first.
    """
    catalogue = root / ITCH_CATALOGUE
    if not catalogue.is_file():
        raise SystemExit(f"{catalogue}: launch catalogue not found")

    text = read_exact(catalogue)
    lines = text.splitlines(keepends=True)

    ext_pattern = re.compile(
        r'^\[ext_resource\s+type="Resource"\s+path="res://([^"]+)"\s+id="([^"]+)"\]\s*$'
    )
    held_back_ids: dict[str, str] = {}
    kept_lines: list[str] = []

    for line in lines:
        match = ext_pattern.match(line.strip())
        if match and match.group(1) in ITCH_HELD_BACK_RESOURCES:
            resource_path, resource_id = match.groups()
            if resource_path in held_back_ids:
                raise SystemExit(f"{catalogue}: duplicate held-back resource declaration for {resource_path}")
            held_back_ids[resource_path] = resource_id
            continue
        kept_lines.append(line)

    missing = sorted(ITCH_HELD_BACK_RESOURCES - held_back_ids.keys())
    if missing:
        raise SystemExit(
            f"{catalogue}: expected held-back catalogue resources are no longer referenced: "
            + ", ".join(missing)
            + ". Update the hardening inventory deliberately."
        )

    rewritten = "".join(kept_lines)

    entries_match = re.search(r'(?m)^Entries = Array\[Resource\]\(\[(.*)\]\)\s*$', rewritten)
    if entries_match is None:
        raise SystemExit(f"{catalogue}: could not locate the Entries resource array")

    entries_text = entries_match.group(1)
    removed_entry_count = 0
    for resource_id in held_back_ids.values():
        token = f'ExtResource("{resource_id}")'
        # Resource arrays are comma-separated on one line today. Remove the exact token and clean
        # separators afterwards; fail closed if the declaration existed but the array did not use it.
        if token not in entries_text:
            raise SystemExit(
                f"{catalogue}: held-back ext_resource id {resource_id!r} is not present in Entries"
            )
        parts = [part.strip() for part in entries_text.split(",")]
        before = len(parts)
        parts = [part for part in parts if part != token]
        removed_entry_count += before - len(parts)
        entries_text = ", ".join(parts)

    if removed_entry_count != len(held_back_ids):
        raise SystemExit(
            f"{catalogue}: expected to remove {len(held_back_ids)} Entries references, "
            f"removed {removed_entry_count}"
        )

    rewritten = (
        rewritten[: entries_match.start(1)]
        + entries_text
        + rewritten[entries_match.end(1) :]
    )

    load_steps_match = re.search(r'(?m)^(\[gd_resource[^\n]*\bload_steps=)(\d+)(\b[^\n]*\])$', rewritten)
    if load_steps_match is None:
        raise SystemExit(f"{catalogue}: could not locate load_steps in gd_resource header")
    old_steps = int(load_steps_match.group(2))
    new_steps = old_steps - len(held_back_ids)
    if new_steps <= 0:
        raise SystemExit(f"{catalogue}: invalid load_steps after pruning ({new_steps})")
    rewritten = (
        rewritten[: load_steps_match.start(2)]
        + str(new_steps)
        + rewritten[load_steps_match.end(2) :]
    )

    # The resource paths and IDs must be gone from the disposable catalogue before their files are
    # excluded from the PCK.
    for resource_path, resource_id in held_back_ids.items():
        if resource_path in rewritten or f'ExtResource("{resource_id}")' in rewritten:
            raise SystemExit(f"{catalogue}: held-back reference survived pruning: {resource_path}")

    write_exact(catalogue, rewritten)
    return sorted(held_back_ids)


def expand_compile_exclusions(root: pathlib.Path, excluded: set[str]) -> set[str]:
    """Expand MSBuild compile exclusions into exact project-relative resource paths."""
    expanded: set[str] = set()

    for entry in excluded:
        if "*" not in entry and "?" not in entry and "[" not in entry:
            candidate = root / entry
            if candidate.is_file():
                expanded.add(entry)
            continue

        for match in root.glob(entry):
            if match.is_file():
                expanded.add(match.relative_to(root).as_posix())

    return expanded


def _preset_block(text: str, name: str) -> tuple[int, int, int, str]:
    starts = list(re.finditer(r"(?m)^\[preset\.(\d+)\]\s*$", text))
    for index, match in enumerate(starts):
        end = starts[index + 1].start() if index + 1 < len(starts) else len(text)
        block = text[match.start() : end]
        if re.search(rf'(?m)^name="{re.escape(name)}"\s*$', block):
            options = re.search(r"(?m)^\[preset\.\d+\.options\]\s*$", block)
            metadata_end = match.start() + (options.start() if options else len(block))
            return int(match.group(1)), match.start(), metadata_end, text[match.start() : metadata_end]
    raise SystemExit(f"export preset {name!r} was not found")


def rewrite_export_exclusions(
    export_presets: pathlib.Path,
    resource_exclusions: set[str],
) -> tuple[int, int]:
    """Merge reduced-scope resource paths into the itch export preset."""
    text = read_exact(export_presets)
    _, start, end, block = _preset_block(text, ITCH_PRESET_NAME)
    match = re.search(r'(?m)^exclude_filter="([^"]*)"\s*$', block)
    if match is None:
        raise SystemExit(f"{export_presets}: {ITCH_PRESET_NAME!r} has no exclude_filter")

    existing = {value.strip() for value in match.group(1).split(",") if value.strip()}
    previous_count = len(existing)
    merged = existing | resource_exclusions | ITCH_HELD_BACK_RESOURCES

    for required in ITCH_HELD_BACK_RESOURCES:
        if not (export_presets.parent / required).is_file():
            raise SystemExit(
                f"{required}: configured as an itch-held-back resource but no longer exists; "
                "update the hardening inventory deliberately rather than silently dropping it"
            )

    replacement = 'exclude_filter="' + ", ".join(sorted(merged)) + '"'
    new_block = block[: match.start()] + replacement + block[match.end() :]
    write_exact(export_presets, text[:start] + new_block + text[end:])
    return previous_count, len(merged)


def _replace_preset_value(block: str, key: str, rendered_value: str) -> str:
    pattern = re.compile(rf"(?m)^{re.escape(key)}=.*$")
    if pattern.search(block) is None:
        raise SystemExit(f"itch export preset is missing required key {key!r}")
    return pattern.sub(f"{key}={rendered_value}", block, count=1)


def enable_pck_encryption(root: pathlib.Path, key_file: pathlib.Path) -> None:
    """Enable AES-256 PCK + directory encryption for the disposable itch preset only."""
    if not key_file.is_file():
        raise SystemExit(f"{key_file}: PCK encryption key file not found")
    key = key_file.read_text(encoding="ascii").strip()
    if re.fullmatch(r"[0-9a-fA-F]{64}", key) is None:
        raise SystemExit("PCK encryption key must be exactly 64 hexadecimal characters (256 bits)")

    export_presets = root / "export_presets.cfg"
    text = read_exact(export_presets)
    preset_index, start, end, block = _preset_block(text, ITCH_PRESET_NAME)
    block = _replace_preset_value(block, "encryption_include_filters", '"*"')
    block = _replace_preset_value(block, "encryption_exclude_filters", '""')
    block = _replace_preset_value(block, "encrypt_pck", "true")
    block = _replace_preset_value(block, "encrypt_directory", "true")
    write_exact(export_presets, text[:start] + block + text[end:])

    credentials_dir = root / ".godot"
    credentials_dir.mkdir(parents=True, exist_ok=True)
    credentials = credentials_dir / "export_credentials.cfg"
    write_exact(credentials, f'[preset.{preset_index}]\n\nscript_encryption_key="{key}"\n')
    try:
        credentials.chmod(0o600)
    except OSError:
        pass


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=pathlib.Path)
    parser.add_argument("--check", action="store_true")
    parser.add_argument(
        "--pck-key-file",
        type=pathlib.Path,
        help="64-hex-character AES key file for a hardened encrypted export",
    )
    args = parser.parse_args()

    if args.check and args.pck_key_file is not None:
        raise SystemExit("--check and --pck-key-file cannot be combined")

    root = args.root.resolve()
    csproj = root / "DesktopBuddy.csproj"
    project_godot = root / "project.godot"
    export_presets = root / "export_presets.cfg"
    for required in (csproj, project_godot, export_presets):
        if not required.is_file():
            raise SystemExit(f"{required}: not found")

    excluded = excluded_paths(csproj)

    if args.check:
        original = read_exact(project_godot)
        dropped = rewrite_project_godot(project_godot, excluded)
        write_exact(project_godot, original)
        for name in dropped:
            print(f"would drop autoload: {name}")
        return 1 if dropped else 0

    dropped = rewrite_project_godot(project_godot, excluded)
    pruned_resources = prune_held_back_catalogue(root)
    packed_source_exclusions = expand_compile_exclusions(root, excluded)
    before, after = rewrite_export_exclusions(export_presets, packed_source_exclusions)

    if args.pck_key_file is not None:
        enable_pck_encryption(root, args.pck_key_file.resolve())

    for name in dropped:
        print(f"dropped autoload: {name}")
    for resource in pruned_resources:
        print(f"pruned held-back catalogue resource: {resource}")
    print(
        "itch scope: "
        f"{len(dropped)} autoload(s) dropped, "
        f"{len(pruned_resources)} held-back catalogue resource(s) pruned, "
        f"{len(excluded)} compile exclusion rule(s) read, "
        f"{len(packed_source_exclusions)} excluded C# resource placeholder(s), "
        f"export exclusions {before} -> {after}, "
        f"pck_encryption={'enabled' if args.pck_key_file is not None else 'unchanged'}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
