import contextlib
import io
from pathlib import Path
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

    def test_nonzero_exit_is_returned(self):
        result, _ = self.run_process("print('failed', flush=True)\nraise SystemExit(7)\n")
        self.assertEqual(7, result)
        self.assertIn("failed", self.log.read_text())

    def test_timeout_stops_the_process(self):
        result, elapsed = self.run_process(
            "import time\nprint('started', flush=True)\ntime.sleep(10)\n",
            timeout=0.1,
        )
        self.assertEqual(124, result)
        self.assertLess(elapsed, 2)
        self.assertIn("started", self.log.read_text())


if __name__ == "__main__":
    unittest.main()
