#!/usr/bin/env python3
"""Run one process to completion, retaining its combined output in a log."""

from __future__ import annotations

import argparse
from pathlib import Path
import subprocess
import sys


def _text(output: str | bytes | None) -> str:
    if isinstance(output, bytes):
        return output.decode("utf-8", errors="replace")
    return output or ""


def run(command: list[str], timeout: float, log_path: Path) -> int:
    log_path.parent.mkdir(parents=True, exist_ok=True)
    try:
        completed = subprocess.run(
            command,
            check=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            encoding="utf-8",
            errors="replace",
            timeout=timeout,
        )
    except subprocess.CalledProcessError as exc:
        output = _text(exc.stdout)
        log_path.write_text(output, encoding="utf-8")
        print(output, end="")
        print(f"Process exited with code {exc.returncode}; log: {log_path}", file=sys.stderr)
        return exc.returncode or 1
    except subprocess.TimeoutExpired as exc:
        output = _text(exc.stdout)
        log_path.write_text(output, encoding="utf-8")
        print(output, end="")
        print(f"Process timed out after {timeout:g} seconds; log: {log_path}", file=sys.stderr)
        return 124

    log_path.write_text(completed.stdout, encoding="utf-8")
    print(completed.stdout, end="")
    print(f"Process exited with code {completed.returncode}; log: {log_path}")
    return completed.returncode


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--timeout", required=True, type=float)
    parser.add_argument("--log", required=True, type=Path)
    parser.add_argument("command", nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ["--"] else args.command
    if not command:
        parser.error("a command is required after --")
    return run(command, args.timeout, args.log)


if __name__ == "__main__":
    raise SystemExit(main())
