from pathlib import Path
import tempfile
import unittest

from devtools.verification.verify_ci_supply_chain import audit_workflows


SECURE_WORKFLOW = """name: Secure\n\non:\n  pull_request:\n\npermissions:\n  contents: read\n\njobs:\n  verify:\n    runs-on: ubuntu-latest\n    timeout-minutes: 10\n    steps:\n      - uses: actions/checkout@11d5960a326750d5838078e36cf38b85af677262\n        with:\n          persist-credentials: false\n      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9\n"""


class VerifyCiSupplyChainTests(unittest.TestCase):
    def audit(self, content: str) -> list[str]:
        with tempfile.TemporaryDirectory() as directory:
            workflow_dir = Path(directory)
            (workflow_dir / "test.yml").write_text(content, encoding="utf-8")
            return audit_workflows(workflow_dir)

    def test_secure_workflow_passes(self) -> None:
        self.assertEqual([], self.audit(SECURE_WORKFLOW))

    def test_only_ci_workflow_may_run_on_push(self) -> None:
        content = SECURE_WORKFLOW.replace("  pull_request:\n", "  push:\n")
        errors = self.audit(content)
        self.assertTrue(any("only ci.yml may run on push" in error for error in errors), errors)

    def test_mutable_action_tag_is_rejected(self) -> None:
        content = SECURE_WORKFLOW.replace(
            "actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9",
            "actions/setup-dotnet@v4",
        )
        errors = self.audit(content)
        self.assertTrue(any("full 40-character commit SHA" in error for error in errors), errors)

    def test_checkout_credentials_must_be_disabled(self) -> None:
        content = SECURE_WORKFLOW.replace(
            "        with:\n          persist-credentials: false\n", ""
        )
        errors = self.audit(content)
        self.assertTrue(any("persist-credentials: false" in error for error in errors), errors)

    def test_runner_job_requires_timeout(self) -> None:
        content = SECURE_WORKFLOW.replace("    timeout-minutes: 10\n", "")
        errors = self.audit(content)
        self.assertTrue(any("must set timeout-minutes" in error for error in errors), errors)

    def test_explicit_permissions_are_required(self) -> None:
        content = SECURE_WORKFLOW.replace("permissions:\n  contents: read\n\n", "")
        errors = self.audit(content)
        self.assertTrue(any("missing explicit top-level permissions" in error for error in errors), errors)

    def test_local_action_does_not_need_remote_sha(self) -> None:
        content = SECURE_WORKFLOW.replace(
            "      - uses: actions/setup-dotnet@67a3573c9a986a3f9c594539f4ab511d57bb3ce9\n",
            "      - uses: ./tools/local-action\n",
        )
        self.assertEqual([], self.audit(content))


if __name__ == "__main__":
    unittest.main()
