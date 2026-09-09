from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name("verify_encrypted_embedded_pck.py")


class EncryptedEmbeddedPckVerifierTests(unittest.TestCase):
    def run_verifier(self, path: Path) -> subprocess.CompletedProcess[str]:
        return subprocess.run(
            [sys.executable, str(SCRIPT), str(path)],
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )

    @staticmethod
    def write_export(path: Path, *, flags: int = 1, payload: bytes = b"") -> None:
        pck = b"GDPC" + struct.pack("<IIIII", 3, 4, 6, 1, flags) + payload
        path.write_bytes(b"MZ\0fixture" + pck + struct.pack("<Q", len(pck)) + b"GDPC")

    def test_accepts_encrypted_embedded_pack(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "DesktopBuddy.exe"
            self.write_export(path)
            result = self.run_verifier(path)
            self.assertEqual(0, result.returncode, result.stdout)

    def test_rejects_plaintext_directory_loose_fallback_and_visible_content(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            path = root / "DesktopBuddy.exe"
            for flags, payload, loose, expected in (
                (0, b"", False, "directory is not encrypted"),
                (1, b"", True, "loose PCK fallback"),
                (1, b"tool.pistol", False, "plaintext resource sentinels"),
            ):
                with self.subTest(expected=expected):
                    self.write_export(path, flags=flags, payload=payload)
                    pck = path.with_suffix(".pck")
                    if loose:
                        pck.write_bytes(b"fallback")
                    elif pck.exists():
                        pck.unlink()
                    result = self.run_verifier(path)
                    self.assertNotEqual(0, result.returncode)
                    self.assertIn(expected, result.stdout)

    def test_rejects_missing_or_invalid_footer(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "DesktopBuddy.exe"
            path.write_bytes(b"MZ" + bytes(64))
            result = self.run_verifier(path)
            self.assertNotEqual(0, result.returncode)
            self.assertIn("footer", result.stdout)


if __name__ == "__main__":
    unittest.main()
