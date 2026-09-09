"""Release integrity checks; run with python -m unittest discover -s devtools/release."""

import contextlib
import io
import json
from pathlib import Path
import tempfile
import unittest

import release_manifest as manifest


class ReleaseManifestTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.payload = self.root / "index.html"
        self.payload.write_text("original", encoding="utf-8")
        self.path = self.root / "release-manifest.json"
        args = manifest.parser().parse_args([
            "generate", "--root", str(self.root), "--output", str(self.path),
            "--distribution", "itch-web", "--version", "test", "--git-sha", "abc",
            "--build-id", "123", "--godot-version", "4.6.1",
        ])
        self.data = manifest.build_manifest(args)
        self.write()

    def write(self):
        self.path.write_text(json.dumps(self.data), encoding="utf-8")

    def verify(self, *extra):
        args = manifest.parser().parse_args([
            "verify", "--root", str(self.root), "--manifest", str(self.path), *extra,
        ])
        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(io.StringIO()):
            return manifest.verify_manifest(args)

    def test_exact_bytes_and_identity(self):
        self.assertEqual(0, self.verify("--expect-git-sha", "abc", "--expect-build-id", "123",
                                        "--expect-distribution", "itch-web"))
        for flag, value in [("--expect-git-sha", "wrong"), ("--expect-build-id", "124"),
                            ("--expect-distribution", "steam-full")]:
            with self.subTest(flag=flag), self.assertRaises(SystemExit):
                self.verify(flag, value)

    def test_changed_missing_and_extra_payloads_fail(self):
        self.payload.write_text("tampered", encoding="utf-8")
        self.assertEqual(1, self.verify())
        self.payload.unlink()
        self.assertEqual(1, self.verify())
        self.payload.write_text("original", encoding="utf-8")
        (self.root / "extra.dll").write_bytes(b"unexpected")
        self.assertEqual(1, self.verify())

    def test_duplicate_entries_fail(self):
        self.data["files"].append(self.data["files"][0].copy())
        self.write()
        with self.assertRaises(SystemExit):
            self.verify()

    def test_malformed_entries_fail(self):
        original = self.data["files"][0].copy()
        for field, value in [("path", "../outside"), ("path", "C:/outside"),
                             ("path", "/absolute"), ("path", "a\\b"),
                             ("path", "./index.html"), ("path", ""),
                             ("size", True), ("size", -1), ("sha256", "bad")]:
            with self.subTest(field=field, value=value):
                self.data["files"] = [dict(original, **{field: value})]
                self.write()
                with self.assertRaises(SystemExit):
                    self.verify()

    def test_empty_payload_fails(self):
        self.data["files"] = []
        self.payload.unlink()
        self.write()
        with self.assertRaises(SystemExit):
            self.verify()

    def test_links_fail_even_when_they_point_inside_root(self):
        link = self.root / "alias.html"
        try:
            link.symlink_to(self.payload)
        except OSError as exc:
            self.skipTest(f"Creating symlinks requires OS permission: {exc}")
        with self.assertRaises(SystemExit):
            self.verify()


if __name__ == "__main__":
    unittest.main()
