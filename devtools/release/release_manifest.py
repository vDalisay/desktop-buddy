#!/usr/bin/env python3
"""Generate and verify Desktop Buddy release provenance manifests.

The manifest is intentionally a release-time artifact. It adds no runtime code or polling and has
no gameplay performance cost. The manifest file itself is excluded from the file hash set to avoid
self-referential hashing.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import sys
from typing import Any

SCHEMA = "desktop-buddy.release-manifest.v1"
DEFAULT_MANIFEST_NAME = "release-manifest.json"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def normalized_files(root: Path, manifest_path: Path) -> list[dict[str, Any]]:
    files: list[dict[str, Any]] = []
    manifest_resolved = manifest_path.resolve()
    for path in sorted((p for p in root.rglob("*") if p.is_file()), key=lambda p: p.as_posix()):
        if path.resolve() == manifest_resolved:
            continue
        relative = path.relative_to(root).as_posix()
        files.append(
            {
                "path": relative,
                "size": path.stat().st_size,
                "sha256": sha256_file(path),
            }
        )
    return files


def build_manifest(args: argparse.Namespace) -> dict[str, Any]:
    root = Path(args.root).resolve()
    output = Path(args.output).resolve()
    if not root.is_dir():
        raise SystemExit(f"Release root does not exist or is not a directory: {root}")
    try:
        output.relative_to(root)
    except ValueError as exc:
        raise SystemExit("Manifest output must be inside the release root.") from exc

    identity: dict[str, Any] = {
        "product": "Desktop Buddy",
        "publisher": args.publisher,
        "distribution": args.distribution,
        "version": args.version,
        "git_sha": args.git_sha,
        "build_id": args.build_id,
    }
    platform: dict[str, Any] = {
        "godot_version": args.godot_version,
    }
    if args.steam_app_id:
        platform["steam_app_id"] = args.steam_app_id

    signing: dict[str, Any] = {
        "authenticode_required": bool(args.authenticode_required),
    }
    if args.signing_subject:
        signing["subject"] = args.signing_subject
    if args.signing_thumbprint:
        signing["thumbprint"] = args.signing_thumbprint

    return {
        "schema": SCHEMA,
        "identity": identity,
        "platform": platform,
        "signing": signing,
        "files": normalized_files(root, output),
    }


def write_manifest(args: argparse.Namespace) -> int:
    output = Path(args.output)
    output.parent.mkdir(parents=True, exist_ok=True)
    manifest = build_manifest(args)
    output.write_text(json.dumps(manifest, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"Wrote {output} with {len(manifest['files'])} hashed files.")
    return 0


def verify_manifest(args: argparse.Namespace) -> int:
    root = Path(args.root).resolve()
    manifest_path = Path(args.manifest).resolve()
    data = json.loads(manifest_path.read_text(encoding="utf-8"))
    if data.get("schema") != SCHEMA:
        raise SystemExit(f"Unsupported manifest schema: {data.get('schema')!r}")

    expected_entries = data.get("files")
    if not isinstance(expected_entries, list):
        raise SystemExit("Manifest files entry is not a list.")

    expected: dict[str, tuple[int, str]] = {}
    for entry in expected_entries:
        if not isinstance(entry, dict):
            raise SystemExit("Manifest contains a non-object file entry.")
        path = entry.get("path")
        size = entry.get("size")
        digest = entry.get("sha256")
        if not isinstance(path, str) or not isinstance(size, int) or not isinstance(digest, str):
            raise SystemExit(f"Malformed manifest file entry: {entry!r}")
        if path.startswith("/") or ".." in Path(path).parts or "\\" in path:
            raise SystemExit(f"Unsafe manifest path: {path!r}")
        expected[path] = (size, digest)

    actual_entries = normalized_files(root, manifest_path)
    actual = {entry["path"]: (entry["size"], entry["sha256"]) for entry in actual_entries}

    missing = sorted(set(expected) - set(actual))
    unexpected = sorted(set(actual) - set(expected))
    changed = sorted(path for path in set(expected) & set(actual) if expected[path] != actual[path])

    if missing or unexpected or changed:
        if missing:
            print("Missing files:", *missing, sep="\n  ", file=sys.stderr)
        if unexpected:
            print("Unexpected files:", *unexpected, sep="\n  ", file=sys.stderr)
        if changed:
            print("Changed files:", *changed, sep="\n  ", file=sys.stderr)
        return 1

    identity = data.get("identity", {})
    print(
        "Verified release manifest:",
        identity.get("distribution", "unknown"),
        identity.get("git_sha", "unknown"),
        f"({len(actual)} files)",
    )
    return 0


def parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description=__doc__)
    sub = p.add_subparsers(dest="command", required=True)

    generate = sub.add_parser("generate", help="Generate a release manifest")
    generate.add_argument("--root", required=True)
    generate.add_argument("--output", required=True)
    generate.add_argument("--distribution", required=True, choices=("itch-web", "steam-demo", "steam-full"))
    generate.add_argument("--version", required=True)
    generate.add_argument("--git-sha", required=True)
    generate.add_argument("--build-id", required=True)
    generate.add_argument("--godot-version", required=True)
    generate.add_argument("--steam-app-id", default="")
    generate.add_argument("--publisher", default="VVoidDev")
    generate.add_argument("--authenticode-required", action="store_true")
    generate.add_argument("--signing-subject", default="")
    generate.add_argument("--signing-thumbprint", default="")
    generate.set_defaults(func=write_manifest)

    verify = sub.add_parser("verify", help="Verify a release directory against its manifest")
    verify.add_argument("--root", required=True)
    verify.add_argument("--manifest", required=True)
    verify.set_defaults(func=verify_manifest)
    return p


def main() -> int:
    args = parser().parse_args()
    return args.func(args)


if __name__ == "__main__":
    raise SystemExit(main())
