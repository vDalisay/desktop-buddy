# Signing and release-policy owner gate

Owner decision, 2026-09-09: **do not purchase or require production code signing.** The owner accepts the resulting Windows reputation/SmartScreen tradeoff. Authenticode remains a verifier capability, not a release gate. Revisit only if platform policy or user trust requirements change.

The owner also declined GitHub release-environment protection and plans to make the repository private. The pipeline must still keep credentials and candidate bytes private and require manual dispatch for releases.

Superseded planning checklist (retained for provenance):

- signing provider and account (Azure Artifact Signing or approved OV certificate), access method, and release operator;
- approved Authenticode subject and certificate thumbprint, RFC 3161 timestamp service, renewal/rotation owner, and private evidence location;
- which project-owned PE files must be signed (at minimum `DesktopBuddy.exe`, plus project-owned native DLLs when present);
- release-environment approvers, allowed deployment branches/tags, and protected-path review policy;
- whether the public repository/source history is intentional;
- where the signed manifest/attestation and retained exact candidate live privately.

Implementation order is fixed: reduce scope → accepted optional transforms → sign → generate/hash and authenticate the manifest → verify exact signatures, timestamps, identity, manifest, and payload → upload the same bytes. `devtools/release/verify_authenticode.ps1` is the fail-closed final-PE verifier; its Windows self-check covers valid, unsigned, tampered, and wrong-thumbprint inputs. It is intentionally not enabled in SteamPipe until the identity and signing mechanism above are approved, because unsigned builds must not be mislabeled as signed and placeholder identities must not become release policy.
