#!/usr/bin/env python3
from __future__ import annotations

import pathlib
import struct
import subprocess
import sys
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).with_name("verify_steam_distribution_scope.py")


class SteamDistributionScopeVerifierTests(unittest.TestCase):
    def _run(self, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), *args],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )

    def test_demo_accepts_shared_markers_and_rejects_decorator(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            assembly = pathlib.Path(tmp) / "DesktopBuddy.dll"
            assembly.write_bytes(
                b"EnvironmentBackgroundEditor\0BuddyStudioWorkspace\0WorkshopPanel\0"
                b"EnvironmentDecoratorPreferences\0get_EnvironmentDecoratorPreferences\0"
            )
            result = self._run("--target", "demo", "--assembly", str(assembly))
            self.assertEqual(result.returncode, 0, result.stdout)

            assembly.write_bytes(assembly.read_bytes() + b" EnvironmentDecorator")
            result = self._run("--target", "demo", "--assembly", str(assembly))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("physically excluded", result.stdout)

    def test_demo_pck_rejects_held_back_resource_path(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            assembly = root / "DesktopBuddy.dll"
            assembly.write_bytes(b"EnvironmentBackgroundEditor BuddyStudioWorkspace WorkshopPanel")
            pck = root / "DesktopBuddy.pck"
            header = b"GDPC" + struct.pack("<IIII", 3, 4, 6, 1)
            pck.write_bytes(
                header
                + b" EnvironmentBackgroundEditor.cs WorkshopPanel.cs data/environment/lamp_arc.tres"
            )
            result = self._run(
                "--target", "demo", "--assembly", str(assembly), "--pck", str(pck)
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("data/environment/", result.stdout)

    def test_full_requires_room_decorator_markers(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            assembly = pathlib.Path(tmp) / "DesktopBuddy.dll"
            assembly.write_bytes(
                b"EnvironmentBackgroundEditor BuddyStudioWorkspace WorkshopPanel "
                b"EnvironmentDecorator EnvironmentDecorationRegistry"
            )
            result = self._run("--target", "full", "--assembly", str(assembly))
            self.assertEqual(result.returncode, 0, result.stdout)

            assembly.write_bytes(assembly.read_bytes().replace(
                b"EnvironmentDecorator ", b"EnvironmentDecoratorPreferences "
            ))
            result = self._run("--target", "full", "--assembly", str(assembly))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("missing required scope markers", result.stdout)


if __name__ == "__main__":
    unittest.main()
