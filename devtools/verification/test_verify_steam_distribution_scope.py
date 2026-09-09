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

    def _write_pck(
        self,
        path: pathlib.Path,
        entries: tuple[str, ...],
        *,
        payload: bytes = b"",
        pack_flags: int = 0,
        directory_offset: int = 40,
    ) -> None:
        header = (
            b"GDPC"
            + struct.pack("<IIII", 3, 4, 6, 1)
            + struct.pack("<IQQ", pack_flags, 0, directory_offset)
        )
        if directory_offset < len(header):
            path.write_bytes(header)
            return

        directory = bytearray(struct.pack("<I", len(entries)))
        for entry in entries:
            encoded = entry.encode("utf-8")
            directory += struct.pack("<I", len(encoded))
            directory += encoded
            directory += struct.pack("<QQ", 0, 0)
            directory += bytes(16)
            directory += struct.pack("<I", 0)

        path.write_bytes(header + bytes(directory_offset - len(header)) + directory + payload)

    @staticmethod
    def _write_demo_assembly(path: pathlib.Path) -> None:
        path.write_bytes(
            b"EnvironmentBackgroundEditor\0BuddyStudioWorkspace\0WorkshopPanel\0"
            b"EnvironmentDecoratorPreferences\0get_EnvironmentDecoratorPreferences\0"
        )

    def test_demo_accepts_shared_markers_and_rejects_decorator(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            assembly = pathlib.Path(tmp) / "DesktopBuddy.dll"
            self._write_demo_assembly(assembly)
            result = self._run("--target", "demo", "--assembly", str(assembly))
            self.assertEqual(result.returncode, 0, result.stdout)

            assembly.write_bytes(assembly.read_bytes() + b" EnvironmentDecorator")
            result = self._run("--target", "demo", "--assembly", str(assembly))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("physically excluded", result.stdout)

    def test_demo_pck_ignores_incidental_forbidden_strings_outside_file_table(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            assembly = root / "DesktopBuddy.dll"
            self._write_demo_assembly(assembly)
            pck = root / "DesktopBuddy.pck"
            self._write_pck(
                pck,
                ("res://project.godot", "res://assets/ui/workshop_panel.tscn"),
                payload=(
                    b"data/environment/ cosmetic_top_ cosmetic_shoes_ cosmetic_accessories_ "
                    b"EnvironmentDecorator.cs EnvironmentDecorationLayer.cs "
                    b"EnvironmentDecorationRegistry.cs"
                ),
            )

            result = self._run(
                "--target", "demo", "--assembly", str(assembly), "--pck", str(pck)
            )
            self.assertEqual(result.returncode, 0, result.stdout)
            self.assertIn("files=2", result.stdout)

    def test_demo_pck_rejects_actual_held_back_file_table_entries(self) -> None:
        forbidden = (
            "res://data/environment/lamp_arc.tres",
            "res://data/catalogue/cosmetic_top_hoodie.tres",
            "res://src/Environment/EnvironmentDecorator.cs",
        )
        for entry in forbidden:
            with self.subTest(entry=entry), tempfile.TemporaryDirectory() as tmp:
                root = pathlib.Path(tmp)
                assembly = root / "DesktopBuddy.dll"
                self._write_demo_assembly(assembly)
                pck = root / "DesktopBuddy.pck"
                self._write_pck(pck, ("res://project.godot", entry))

                result = self._run(
                    "--target", "demo", "--assembly", str(assembly), "--pck", str(pck)
                )
                self.assertNotEqual(result.returncode, 0)
                self.assertIn("physically excluded file-table entries", result.stdout)
                self.assertIn(entry.removeprefix("res://"), result.stdout)

    def test_pck_parser_fails_closed_on_malformed_or_encrypted_directory(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            assembly = root / "DesktopBuddy.dll"
            self._write_demo_assembly(assembly)

            malformed = root / "malformed.pck"
            self._write_pck(malformed, ("res://project.godot",), directory_offset=39)
            result = self._run(
                "--target", "demo", "--assembly", str(assembly), "--pck", str(malformed)
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("invalid PCK directory offset", result.stdout)

            encrypted = root / "encrypted.pck"
            self._write_pck(encrypted, ("res://project.godot",), pack_flags=1)
            result = self._run(
                "--target", "demo", "--assembly", str(assembly), "--pck", str(encrypted)
            )
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("encrypted PCK directories are not supported", result.stdout)

    def test_full_requires_room_decorator_markers_and_resource_entry(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            assembly = root / "DesktopBuddy.dll"
            assembly.write_bytes(
                b"EnvironmentBackgroundEditor BuddyStudioWorkspace WorkshopPanel "
                b"EnvironmentDecorator EnvironmentDecorationRegistry"
            )
            pck = root / "DesktopBuddy.pck"
            self._write_pck(
                pck,
                ("res://project.godot", "res://data/environment/launch_decorations.tres"),
            )
            result = self._run(
                "--target", "full", "--assembly", str(assembly), "--pck", str(pck)
            )
            self.assertEqual(result.returncode, 0, result.stdout)

            assembly.write_bytes(assembly.read_bytes().replace(
                b"EnvironmentDecorator ", b"EnvironmentDecoratorPreferences "
            ))
            result = self._run("--target", "full", "--assembly", str(assembly))
            self.assertNotEqual(result.returncode, 0)
            self.assertIn("missing required scope markers", result.stdout)


if __name__ == "__main__":
    unittest.main()
