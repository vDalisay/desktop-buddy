# Signing and release-policy owner gate

No production signing or repository-policy mutation is authorized yet. Before H5 can be wired into SteamPipe, the owner must record:

- signing provider and account (Azure Artifact Signing or approved OV certificate), access method, and release operator;
- approved Authenticode subject and certificate thumbprint, RFC 3161 timestamp service, renewal/rotation owner, and private evidence location;
- which project-owned PE files must be signed (at minimum `DesktopBuddy.exe`, plus project-owned native DLLs when present);
- release-environment approvers, allowed deployment branches/tags, and protected-path review policy;
- whether the public repository/source history is intentional;
- where the signed manifest/attestation and retained exact candidate live privately.

Implementation order is fixed: reduce scope → accepted optional transforms → sign → generate/hash and authenticate the manifest → verify exact signatures, timestamps, identity, manifest, and payload → upload the same bytes. `devtools/release/verify_authenticode.ps1` is the fail-closed final-PE verifier; its Windows self-check covers valid, unsigned, tampered, and wrong-thumbprint inputs. It is intentionally not enabled in SteamPipe until the identity and signing mechanism above are approved, because unsigned builds must not be mislabeled as signed and placeholder identities must not become release policy.
