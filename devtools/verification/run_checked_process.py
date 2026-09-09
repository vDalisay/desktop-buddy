#!/usr/bin/env python3
"""Run one process to completion, retaining its combined output in a log."""

from __future__ import annotations

import argparse
import os
from pathlib import Path
import signal
import subprocess
import sys


_TREE_KILL_TIMEOUT_SECONDS = 5.0
_CLEANUP_WAIT_SECONDS = 5.0


def _text(output: str | bytes | None) -> str:
    if isinstance(output, bytes):
        return output.decode("utf-8", errors="replace")
    return output or ""


def _merge_output(partial: str, final: str) -> str:
    if not partial:
        return final
    if not final:
        return partial
    if final.startswith(partial):
        return final
    return partial + final


def _terminate_process_tree(process: subprocess.Popen[str]) -> None:
    """Terminate the process and descendants owned by this invocation."""
    if os.name == "nt":
        try:
            subprocess.run(
                ["taskkill", "/PID", str(process.pid), "/T", "/F"],
                check=False,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=_TREE_KILL_TIMEOUT_SECONDS,
            )
        except (OSError, subprocess.TimeoutExpired):
            pass

        if process.poll() is None:
            try:
                process.kill()
            except OSError:
                pass
        return

    try:
        os.killpg(process.pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    except OSError:
        if process.poll() is None:
            try:
                process.kill()
            except OSError:
                pass


def run(command: list[str], timeout: float, log_path: Path) -> int:
    log_path.parent.mkdir(parents=True, exist_ok=True)

    popen_kwargs: dict[str, object] = {}
    if os.name == "nt":
        popen_kwargs["creationflags"] = subprocess.CREATE_NEW_PROCESS_GROUP
    else:
        popen_kwargs["start_new_session"] = True

    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        text=True,
        encoding="utf-8",
        errors="replace",
        **popen_kwargs,
    )

    try:
        stdout, _ = process.communicate(timeout=timeout)
    except subprocess.TimeoutExpired as exc:
        output = _text(exc.output)
        _terminate_process_tree(process)

        try:
            final_output, _ = process.communicate(timeout=_CLEANUP_WAIT_SECONDS)
            output = _merge_output(output, _text(final_output))
        except subprocess.TimeoutExpired as cleanup_exc:
            output = _merge_output(output, _text(cleanup_exc.output))
            if process.stdout is not None:
                process.stdout.close()
            if process.poll() is None:
                try:
                    process.kill()
                except OSError:
                    pass
            try:
                process.wait(timeout=1.0)
            except subprocess.TimeoutExpired:
                pass

        log_path.write_text(output, encoding="utf-8")
        print(output, end="")
        print(f"Process timed out after {timeout:g} seconds; log: {log_path}", file=sys.stderr)
        return 124

    output = _text(stdout)
    log_path.write_text(output, encoding="utf-8")
    print(output, end="")

    if process.returncode:
        print(f"Process exited with code {process.returncode}; log: {log_path}", file=sys.stderr)
        return process.returncode

    print(f"Process exited with code {process.returncode}; log: {log_path}")
    return process.returncode


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
