#!/usr/bin/env python3
"""Fail closed on mutable GitHub Actions dependencies and unsafe workflow defaults."""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

FULL_SHA = re.compile(r"^[0-9a-fA-F]{40}$")
DOCKER_DIGEST = re.compile(r"@sha256:[0-9a-fA-F]{64}$")
USES_LINE = re.compile(r"^(?P<indent>\s*)-\s+uses:\s*(?P<target>[^\s#]+)")
JOB_LINE = re.compile(r"^  (?P<name>[A-Za-z0-9_.-]+):\s*$")


def _step_end(lines: list[str], start: int, indent: str) -> int:
    marker = re.compile(r"^" + re.escape(indent) + r"-\s+")
    for index in range(start + 1, len(lines)):
        if marker.match(lines[index]):
            return index
    return len(lines)


def _job_ranges(lines: list[str]) -> list[tuple[str, int, int]]:
    try:
        jobs_index = next(index for index, line in enumerate(lines) if line == "jobs:")
    except StopIteration:
        return []

    starts: list[tuple[str, int]] = []
    for index in range(jobs_index + 1, len(lines)):
        match = JOB_LINE.match(lines[index])
        if match:
            starts.append((match.group("name"), index))

    ranges: list[tuple[str, int, int]] = []
    for position, (name, start) in enumerate(starts):
        end = starts[position + 1][1] if position + 1 < len(starts) else len(lines)
        ranges.append((name, start, end))
    return ranges


def audit_workflow(path: Path) -> list[str]:
    text = path.read_text(encoding="utf-8")
    lines = text.splitlines()
    errors: list[str] = []

    if "permissions:" not in lines:
        errors.append(f"{path}: missing explicit top-level permissions block")
    if any(line.strip() == "permissions: write-all" for line in lines):
        errors.append(f"{path}: permissions: write-all is forbidden")

    for index, line in enumerate(lines):
        match = USES_LINE.match(line)
        if not match:
            continue

        target = match.group("target")
        if target.startswith("./"):
            continue
        if target.startswith("docker://"):
            if not DOCKER_DIGEST.search(target):
                errors.append(
                    f"{path}:{index + 1}: Docker action must use an immutable sha256 digest: {target}"
                )
            continue

        action, separator, ref = target.rpartition("@")
        if not separator or not action or not FULL_SHA.fullmatch(ref):
            errors.append(
                f"{path}:{index + 1}: external action must be pinned to a full 40-character commit SHA: {target}"
            )

        if action == "actions/checkout":
            end = _step_end(lines, index, match.group("indent"))
            block = lines[index:end]
            if not any(re.match(r"^\s+persist-credentials:\s*false\s*(?:#.*)?$", item) for item in block):
                errors.append(
                    f"{path}:{index + 1}: actions/checkout must set persist-credentials: false"
                )

    job_ranges = _job_ranges(lines)
    if not job_ranges:
        errors.append(f"{path}: missing jobs")
    for name, start, end in job_ranges:
        block = lines[start:end]
        # Reusable-workflow jobs do not accept timeout-minutes; runner-backed jobs do.
        if any(re.match(r"^    runs-on:\s*", line) for line in block):
            if not any(re.match(r"^    timeout-minutes:\s*[1-9][0-9]*\s*(?:#.*)?$", line) for line in block):
                errors.append(f"{path}:{start + 1}: runner job '{name}' must set timeout-minutes")

    return errors


def audit_workflows(directory: Path) -> list[str]:
    if not directory.is_dir():
        return [f"Workflow directory does not exist: {directory}"]

    workflows = sorted((*directory.glob("*.yml"), *directory.glob("*.yaml")))
    if not workflows:
        return [f"No workflow files found in: {directory}"]

    errors: list[str] = []
    for path in workflows:
        errors.extend(audit_workflow(path))
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("workflow_directory", nargs="?", default=".github/workflows")
    args = parser.parse_args()

    errors = audit_workflows(Path(args.workflow_directory))
    if errors:
        print("CI supply-chain policy violations:", file=sys.stderr)
        for error in errors:
            print(f"  - {error}", file=sys.stderr)
        return 1

    print("CI supply-chain policy verified: immutable actions, checkout credentials disabled, explicit permissions and job timeouts.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
