# Re-upload response runbook

Use this only for a suspected unauthorized or altered Desktop Buddy distribution. Human review is required before any report or takedown request.

## Preserve evidence

1. Record the UTC time, public URL, storefront/account identity, reporter, and access method. Capture the listing and download response headers without signing in through an untrusted page.
2. Keep the downloaded bytes read-only in private evidence storage. Record SHA-256 for the original archive and every extracted file; do not add them to this repository or a public CI artifact.
3. Record the official comparison candidate: distribution, Git SHA, workflow/run or Steam Build ID, release-manifest SHA-256, payload hashes, Authenticode subject/thumbprint/timestamp, and private owner evidence location.
4. Compare hashes, manifest identity, PE signature status, signer identity, timestamp, filenames, and player-facing claims. A mismatch is evidence to review, not proof of who uploaded it.

## Decide and respond

- If bytes and official identity match, record the authorized channel or escalate the ownership question to the release owner.
- If content differs, isolate it; never execute it on a normal workstation. Security-review suspicious binaries before contacting a platform.
- Confirm the official publisher/store identities from private owner records. Do not rely on names or logos shown by the suspected copy.
- Have the owner or counsel approve the report, evidence disclosure, and requested remedy. Use the platform's official reporting route and retain its case ID.
- After resolution, record the outcome and recheck official downloads. Do not automate monitoring or reporting without a separate owner decision.

## Incident record

```text
Incident ID / UTC:
Reporter / reviewer:
Suspected URL and account:
Capture location:
Downloaded archive SHA-256:
Extracted manifest and file hashes:
Signature status / subject / thumbprint / timestamp:
Official distribution / Git SHA / run or Build ID:
Official manifest SHA-256 / candidate evidence location:
Material differences:
Ownership evidence checked by:
Security review:
Owner/counsel decision:
Platform report / case ID:
Outcome and post-resolution check:
```
