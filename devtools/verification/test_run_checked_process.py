import contextlib
import io
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

from devtools.verification import run_checked_process


class RunCheckedProcessTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name) / "path with spaces"
        self.root.mkdir()
        self.log = self.root / "process log.txt"

    def run_process(self, script: str, timeout: float = 2) -> tuple[int, float]:
        script_path = self.root / "child script.py"
        script_path.write_text(script, encoding="utf-8")
        started = time.monotonic()
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            result = run_checked_process.run(
                [sys.executable, str(script_path), str(self.root / "argument with spaces.txt")],
                timeout,
                self.log,
            )
        return result, time.monotonic() - started

    def process_exists(self, pid: int) -> bool:
        if os.name == "nt":
            completed = subprocess.run(
                ["tasklist", "/FI", f"PID eq {pid}", "/FO", "CSV", "/NH"],
                check=False,
                capture_output=True,
                text=True,
            )
            return f'"{pid}"' in completed.stdout

        try:
            os.kill(pid, 0)
        except ProcessLookupError:
            return False
        except PermissionError:
            return True
        return True

    def wait_for_process_exit(self, pid: int, timeout: float = 2) -> bool:
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            if not self.process_exists(pid):
                return True
            time.sleep(0.05)
        return not self.process_exists(pid)

    def test_delayed_success_runs_once_and_preserves_spaced_arguments(self):
        result, _ = self.run_process(
            "import pathlib, sys, time\n"
            "time.sleep(0.1)\n"
            "path = pathlib.Path(sys.argv[1])\n"
            "path.write_text(str(int(path.read_text()) + 1) if path.exists() else '1')\n"
            "print(path)\n"
        )
        self.assertEqual(0, result)
        self.assertEqual("1", (self.root / "argument with spaces.txt").read_text())
        self.assertIn("argument with spaces.txt", self.log.read_text())

    def test_nonzero_exit_is_returned_and_output_is_retained(self):
        result, _ = self.run_process("print('failed', flush=True)\nraise SystemExit(7)\n")
        self.assertEqual(7, result)
        self.assertIn("failed", self.log.read_text())

    def test_timeout_is_bounded_without_assuming_startup_output(self):
        result, elapsed = self.run_process(
            "import time\ntime.sleep(10)\n",
            timeout=0.5,
        )
        self.assertEqual(124, result)
        self.assertLess(elapsed, 3)

    def test_timeout_terminates_descendant_that_inherits_output(self):
        descendant_pid = self.root / "descendant.pid"
        result, elapsed = self.run_process(
            "import pathlib, subprocess, sys, time\n"
            f"pid_path = pathlib.Path({str(descendant_pid)!r})\n"
            "child = subprocess.Popen(\n"
            "    [sys.executable, '-c', 'import time; time.sleep(30)'],\n"
            "    stdout=sys.stdout,\n"
            "    stderr=sys.stderr,\n"
            ")\n"
            "pid_path.write_text(str(child.pid), encoding='utf-8')\n"
            "print('descendant-started', flush=True)\n"
            "time.sleep(30)\n",
            timeout=2,
        )

        self.assertEqual(124, result)
        self.assertLess(elapsed, 6)
        self.assertTrue(descendant_pid.exists(), "child never reached descendant startup")
        pid = int(descendant_pid.read_text(encoding="utf-8"))
        self.assertTrue(self.wait_for_process_exit(pid), f"descendant PID {pid} survived timeout cleanup")
        self.assertIn("descendant-started", self.log.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
