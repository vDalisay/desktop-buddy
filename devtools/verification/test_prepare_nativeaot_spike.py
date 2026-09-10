from __future__ import annotations

import tempfile
import unittest
from pathlib import Path

from devtools.verification import prepare_nativeaot_spike


class PrepareNativeAotSpikeTests(unittest.TestCase):
    def _root(self, project_text: str = "<Project Sdk=\"Godot.NET.Sdk/4.6.1\">\n</Project>\n"):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        (root / "DesktopBuddy.csproj").write_text(project_text, encoding="utf-8")
        self.addCleanup(temp.cleanup)
        return root

    def test_prepare_is_idempotent_and_gated_on_the_nativeaot_opt_in(self):
        root = self._root()

        self.assertTrue(prepare_nativeaot_spike.prepare(root))
        self.assertFalse(prepare_nativeaot_spike.prepare(root))
        prepare_nativeaot_spike.check(root)

        text = (root / "DesktopBuddy.csproj").read_text(encoding="utf-8")
        self.assertEqual(text.count(prepare_nativeaot_spike.MARKER), 1)
        self.assertEqual(text.count("<PublishAOT>true</PublishAOT>"), 1)
        self.assertIn("'$(DesktopBuddyNativeAot)' == 'true'", text)
        self.assertIn("'$(GodotTargetPlatform)' != 'web'", text)

    def test_check_rejects_unprepared_project(self):
        root = self._root()
        with self.assertRaises(SystemExit):
            prepare_nativeaot_spike.check(root)

    def test_prepare_rejects_missing_project(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        with self.assertRaises(SystemExit):
            prepare_nativeaot_spike.prepare(Path(temp.name))

    def test_prepare_rejects_malformed_project(self):
        root = self._root("<Project>\n")
        with self.assertRaises(SystemExit):
            prepare_nativeaot_spike.prepare(root)


if __name__ == "__main__":
    unittest.main()
