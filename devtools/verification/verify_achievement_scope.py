#!/usr/bin/env python3
"""Verify that compiled artifacts contain (or physically omit) Desktop Buddy achievements."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys

# Keep these sentinels unique to production implementation. Do not use names that are also
# mentioned by developer smoke-test assertion strings, otherwise a Debug profile can false-positive
# even when the production type itself was removed from compilation.
SENTINELS = (
    "DesktopBuddy.Achievements",
    "DesktopBuddy.Domain.Achievements",
    "ACH_FIRST_IMPRESSION",
    "AchievementCoordinator",
    "AchievementReconciler",
    "GodotSteamAchievementRemote",
)


def contains(raw: bytes, text: str) -> bool:
    return text.encode("utf-8") in raw or text.encode("utf-16-le") in raw


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--expect", choices=("included", "excluded"), required=True)
    parser.add_argument("artifacts", nargs="+", type=Path)
    args = parser.parse_args()

    missing_files = [path for path in args.artifacts if not path.is_file()]
    if missing_files:
        for path in missing_files:
            print(f"missing artifact: {path}", file=sys.stderr)
        return 2

    blobs = {path: path.read_bytes() for path in args.artifacts}
    found = {
        sentinel: [str(path) for path, raw in blobs.items() if contains(raw, sentinel)]
        for sentinel in SENTINELS
    }

    if args.expect == "excluded":
        leaked = {key: paths for key, paths in found.items() if paths}
        if leaked:
            print("achievement implementation leaked into an excluded build:", file=sys.stderr)
            for sentinel, paths in leaked.items():
                print(f"  {sentinel}: {', '.join(paths)}", file=sys.stderr)
            return 1
        print("achievement scope: excluded; no first-party achievement namespaces or implementation sentinels found")
        return 0

    absent = [sentinel for sentinel, paths in found.items() if not paths]
    if absent:
        print("achievement-enabled build is missing expected sentinels:", file=sys.stderr)
        for sentinel in absent:
            print(f"  {sentinel}", file=sys.stderr)
        return 1

    print("achievement scope: included; all first-party achievement namespaces and implementation sentinels found")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
