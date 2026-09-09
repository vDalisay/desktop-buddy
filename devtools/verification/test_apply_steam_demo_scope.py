#!/usr/bin/env python3
from __future__ import annotations

import pathlib
import subprocess
import sys
import tempfile
import textwrap
import unittest


SCRIPT = pathlib.Path(__file__).with_name("apply_steam_demo_scope.py")


class SteamDemoScopeTests(unittest.TestCase):
    def _fixture(self, root: pathlib.Path) -> None:
        (root / "src/Environment").mkdir(parents=True)
        (root / "data/catalogue").mkdir(parents=True)
        (root / "data/environment").mkdir(parents=True)

        (root / "DesktopBuddy.csproj").write_text(
            textwrap.dedent(
                """\
                <Project>
                  <ItemGroup Condition=" '$(DesktopBuddySteamDemoScope)' == 'true' ">
                    <Compile Remove="src/Environment/RoomDecorator.cs" />
                  </ItemGroup>
                </Project>
                """
            ),
            encoding="utf-8",
        )
        (root / "src/Environment/RoomDecorator.cs").write_text("sealed class RoomDecorator {}\n", encoding="utf-8")
        (root / "src/Environment/RoomDecorator.cs.uid").write_text("uid://demo\n", encoding="utf-8")
        (root / "project.godot").write_text(
            '[application]\nconfig/name="fixture"\n\n[autoload]\nRoom="*res://src/Environment/RoomDecorator.cs"\n',
            encoding="utf-8",
        )
        (root / "export_presets.cfg").write_text(
            textwrap.dedent(
                """\
                [preset.0]

                name="Windows Steam Demo"
                platform="Windows Desktop"
                exclude_filter="tests/*"

                [preset.0.options]
                binary_format/architecture="x86_64"
                """
            ),
            encoding="utf-8",
        )

        held = {
            "cosmetic_top_demo.tres": "2",
            "cosmetic_shoes_demo.tres": "3",
            "cosmetic_accessories_demo.tres": "4",
        }
        for name in held:
            (root / "data/catalogue" / name).write_text("[resource]\n", encoding="utf-8")
        (root / "data/environment/decor.tres").write_text("[resource]\n", encoding="utf-8")

        (root / "data/catalogue/launch_catalogue.tres").write_text(
            textwrap.dedent(
                """\
                [gd_resource type="Resource" load_steps=5 format=3]

                [ext_resource type="Script" path="res://src/Content/CatalogueDefinition.cs" id="1"]
                [ext_resource type="Resource" path="res://data/catalogue/cosmetic_top_demo.tres" id="2"]
                [ext_resource type="Resource" path="res://data/catalogue/cosmetic_shoes_demo.tres" id="3"]
                [ext_resource type="Resource" path="res://data/catalogue/cosmetic_accessories_demo.tres" id="4"]

                [resource]
                script = ExtResource("1")
                Entries = Array[Resource]([ExtResource("2"), ExtResource("3"), ExtResource("4")])
                """
            ),
            encoding="utf-8",
        )

    def _run(self, root: pathlib.Path, *args: str) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), str(root), *args],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )

    def test_apply_physically_removes_demo_only_content_and_verifies(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            self._fixture(root)
            applied = self._run(root, "--report", "scope.json")
            self.assertEqual(applied.returncode, 0, applied.stdout)
            self.assertFalse((root / "src/Environment/RoomDecorator.cs").exists())
            self.assertFalse((root / "src/Environment/RoomDecorator.cs.uid").exists())
            self.assertFalse((root / "data/environment/decor.tres").exists())
            self.assertFalse(any((root / "data/catalogue").glob("cosmetic_top_*.tres")))
            catalogue = (root / "data/catalogue/launch_catalogue.tres").read_text(encoding="utf-8")
            self.assertNotIn("cosmetic_top_", catalogue)
            project = (root / "project.godot").read_text(encoding="utf-8")
            self.assertNotIn("Room=", project)
            self.assertTrue((root / "scope.json").is_file())

            checked = self._run(root, "--check")
            self.assertEqual(checked.returncode, 0, checked.stdout)

    def test_check_fails_closed_when_forbidden_source_returns(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            self._fixture(root)
            applied = self._run(root)
            self.assertEqual(applied.returncode, 0, applied.stdout)
            resurrected = root / "src/Environment/RoomDecorator.cs"
            resurrected.write_text("sealed class RoomDecorator {}\n", encoding="utf-8")

            checked = self._run(root, "--check")
            self.assertNotEqual(checked.returncode, 0)
            self.assertIn("compiled-out source survived", checked.stdout)


if __name__ == "__main__":
    unittest.main()
