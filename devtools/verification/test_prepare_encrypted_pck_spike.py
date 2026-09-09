from pathlib import Path
import tempfile
import unittest

from devtools.verification.prepare_encrypted_pck_spike import prepare, verify


BASE = """[preset.0]

name="Windows Steam Demo"
platform="Windows Desktop"
encrypt_pck=false

[preset.0.options]

binary_format/architecture="x86_64"

[preset.1]

name="Windows Full Release"
custom="preserved"
"""


class PrepareEncryptedPckSpikeTests(unittest.TestCase):
    def test_workflow_keys_template_and_exporter_and_proves_project_load(self) -> None:
        workflow = Path(".github/workflows/encrypted-pck-steam-smoke.yml").read_text(encoding="utf-8")
        self.assertIn('"GODOT_SCRIPT_ENCRYPTION_KEY=$key"', workflow)
        self.assertIn('"SCRIPT_AES256_ENCRYPTION_KEY=$key"', workflow)
        self.assertIn("$exe --headless --quit-after 120", workflow)
        self.assertNotIn("Start-Process $exe", workflow)

    def test_prepare_is_idempotent_and_preserves_other_presets(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            template = str(Path(directory) / "custom template.exe")
            once = prepare(BASE, template)
            self.assertEqual(once, prepare(once, template))
            self.assertEqual([], verify(once, template))
            self.assertIn('[preset.1]\n\nname="Windows Full Release"\ncustom="preserved"', once)

    def test_missing_or_duplicate_sections_fail_closed(self) -> None:
        for text in (
            BASE.replace("[preset.0.options]", "[missing.options]"),
            BASE + "\n[preset.0]\n",
            BASE + "\n[preset.0.options]\n",
        ):
            with self.subTest(text=text), self.assertRaises(ValueError):
                prepare(text, "template.exe")

    def test_duplicate_keys_fail_closed(self) -> None:
        duplicate = BASE.replace("encrypt_pck=false", "encrypt_pck=false\nencrypt_pck=true")
        with self.assertRaises(ValueError):
            prepare(duplicate, "template.exe")

        prepared = prepare(BASE, "template.exe")
        errors = verify(prepared.replace("encrypt_pck=true", "encrypt_pck=true\nencrypt_pck=true"), "template.exe")
        self.assertTrue(any("encrypt_pck" in error for error in errors), errors)


if __name__ == "__main__":
    unittest.main()
