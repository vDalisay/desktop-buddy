# Hardening finish plan — audited handoff

Audited 2026-09-09 at `70f01700`; main baseline `5a0c0eb2`. **Not complete.**
Continue only on `feature/nativeaot-steam-demo-spike`. Keep one open hardening PR
against main; PR #60 is already merged. Do not create branches for individual steps.
The separate `feature/master-release-plan-2026-09-08` / PR #61 owns product expansion.

This is the current hardening execution queue. It replaces stale implementation
status in HARDENING_STATUS and the Phase 0 closeout notes, not owner requirements.
Follow AGENTS.md source precedence. The older H0–H8 planning reference is preserved
at `3acddb46:docs/DISTRIBUTION_BUILD_HARDENING_PLAN_2026-09-08.md`; its old priorities
are historical proposals, not permission to change product, repository or release policy.
Use [Phase 0's candidate matrix](PHASE_0_CLOSEOUT_PLAN_2026-09-09.md#exact-candidate-acceptance-matrix)
for Windows/platform acceptance. Hardening completion and publishing the game are separate gates.

## Audit: implemented versus accepted

| Area | Actual state | Remaining closure |
| --- | --- | --- |
| H0 / H6 release isolation and dependency integrity | SteamPipe builds/verifies/uploads in one job without public depot artifacts. Action SHAs, tool hashes, restricted permissions, checkout credentials and timeouts have checks. Butler and SteamCMD bootstrap are pinned. | Repository policy is incomplete: API reports `steam-release` with no protection rules or deployment branch policy; main has no classic protection and the only listed ruleset is `no delete main`. Confirm owner policy, then configure/recheck authorized settings. SteamCMD self-updates after its pinned bootstrap; record resolved runtime identity rather than claiming the entire tool is immutable. |
| H1 physical scope | Demo/itch compile removal, disposable export reduction and negative assembly/PCK checks exist; PRs #56, #59 and #62 merged. | Fresh exact shipping candidates, full-scope positive controls, and regression evidence. Final Steam assembly check currently reads the build directory, not demonstrably the delivered assembly. Bind the check to shipped bytes. |
| H2 Steam launch | `SteamLaunchGuard` runs before initialization. It redirects only with Steam already running; closed client/missing addon/errors preserve local boot. Native probe checks API availability. | Exact Windows export/depot tests: direct launch with running Steam, Steam launch without loop, closed client, offline client and unavailable addon. Capability existence is not behavior acceptance. Preserve the fail-open policy. |
| H3 native PCK encryption | Only `prepare_encrypted_pck_spike.py` exists; no focused test or workflow caller found. Production Steam presets are unencrypted; SteamPipe requires a loose PCK. | Custom pinned Windows .NET keyed template, helper tests, bounded manual spike, encrypted/embedded artifact validation and exact-export acceptance. Existing Steam PCK parser explicitly rejects encrypted directories. |
| H4 code protection / NativeAOT | Manual AOT workflow and process-tree timeout fix/tests exist. No successful AOT run after the fix. Obfuscation is not adopted; earlier node-name/member transforms broke runtime behavior. | One properly supervised AOT verdict, then accept with complete evidence or record a bounded deferral. Do not restart broad obfuscation/reflection refactors automatically. |
| H5 authenticity/provenance | Both distribution pipelines generate and verify per-file SHA-256 manifests with source/build identity. | No signing/attestation integration found. `authenticode_required` and certificate fields are metadata only: verifier never checks signatures. Template/tool/key-ID and timestamp evidence is incomplete. Select owner-approved signing identity/access, implement actual verification and retain evidence. |
| H7 itch | Encrypted Web template/export, scope/disclosure audits, exact-artifact browser smoke, pinned Butler, preview and manifest recheck before push exist. Latest audited build succeeded. | Official embed and explicit unrelated-host/referrer acceptance for the selected artifact; current hardened publish workflow has no run returned. Preserve implemented encryption; do not restart the old proposal to build it. |
| H8 operations | No reupload response runbook found in tracked files. | Short runbook and private release-evidence checklist; no monitoring service or automatic reports needed. |

**CI policy drift:** Steam distribution scope, achievement scope and supply-chain
workflows currently have push triggers beyond `CI / quick`. Reconcile with AGENTS.md's
quick-only push contract; keep required slow coverage on PR/manual workflows.

## Finish in this order

1. **Restore one reliable managed baseline.** Reproduce SteamPipe with `target=demo`,
   `upload=false` on the hardening SHA; capture the first actual failure and fix only
   reproduced blockers. Latest SteamPipe run failed; old green runs do not certify
   current code. Verify the shipped assembly/PCK/payload and expected AppID as one
   candidate. Expand loose-file rejection to the documented source/debug/map/dev-AppID
   policy where current checks are narrower. Reuse existing scope/manifest tools.
   Test Demo exclusions and Full inclusions. Reconcile CI triggers without weakening PR checks.
   **Exit:** relevant tooling/build tests and required PR workflows green on the exact SHA.

2. **Finish H3 as a contained compatibility task.** Test the existing helper for
   missing/duplicate keys/sections, idempotency and preservation of other presets.
   Build a pinned Godot 4.6.1 .NET Windows x86_64 keyed template in an isolated manual
   job; retain only non-secret source/tool/template/key identity. Keep key material
   out of logs, ordinary caches and public artifacts. Reuse the supervised exporter.
   Replace loose-PCK assumptions with verification that inspects the actual final
   encrypted/embedded content in a protected context, retaining the same negative
   scope assertions. A separate plaintext build is not proof of encrypted payload scope.
   **Exit:** exact export boots and passes Paint/Studio/Work/Workshop/save checks;
   no plaintext fallback, secret leak or verifier bypass. Apply to Demo and Full only
   after compatibility acceptance and approved production enablement; otherwise record
   the concrete blocker and request an explicit disposition. No browser-encryption rewrite.

3. **Close H4 without an endless experiment.** Dispatch one manual AOT run using the
   corrected process owner. Record exporter exit, PE/native entry-point evidence,
   payload scope and startup result. If successful, run exact-export functionality,
   persistence and performance acceptance before proposing production enablement.
   If it needs broad reflection/serialization changes, document the blocker and leave
   production managed per the Phase 0 plan. Fifteen seconds alive is smoke only.
   **Exit:** accepted evidence or explicit recorded deferral, never an unresolved retry loop.

4. **Finish H5 and policy controls.** Prepare the signing/manifest integration and
   policy checklist for review; owner must supply/approve signing identity/access,
   release approvers/allowed branches, and public-source policy. Do not change those
   account settings or buy a certificate under this planning request. Implement actual
   final PE signature/timestamp verification and authenticated manifest provenance;
   test tampered, unsigned and wrong-identity rejection. Order: reduce → optional accepted
   transforms → sign → hash/attest → verify → upload identical bytes. Preserve private
   Steam payload handling; explicitly solve retained-candidate testing/promotion without
   substituting a rebuild. **Exit:** enforced controls and reproducible evidence, or
   named pending owner gates; a boolean in JSON cannot count as signing.

5. **Accept the actual distributions.** Retain a privately identified Windows candidate
   and a selected itch artifact. Run the Phase 0 Windows/DPI/save/paint/Studio/Work,
   performance/soak and Steam/Workshop/Cloud matrix on the applicable candidate; record
   hardware/account gaps separately. H2 must include Steam-closed local play. Verify
   Demo runtime `5228990` and Workshop owner `5114950` against current owner identity
   records/configuration; do not use a base-AppID development boot as Demo proof.
   Test official itch embed and copied-host rejection with clear non-itch referrer,
   preserving documented ambiguous-referrer fail-open behavior. Do not mutate real saves.
   **Exit:** hashes, environments and pass/fail evidence recorded for each required gate.

6. **Hand off and close.** Add `docs/release/REUPLOAD_RESPONSE_RUNBOOK.md` with evidence
   capture, hash/signature comparison, official identities, private ownership records
   and an incident template. Human review precedes any report. Update this plan/status
   with final SHA, candidate hashes and remaining decisions. Merge only through the
   authorized review flow; uploads, public promotion and release acceptance are separate
   owner actions. Stop when required controls pass and optional work has an explicit
   disposition. Pending signing/account/live tests mean external closure is still pending.

## Reproduced checks and evidence

At `70f01700`, local Windows Python 3.11 audit checks:

```powershell
python -m unittest discover -s devtools/release -p 'test_*.py'
python -m unittest devtools.verification.test_run_checked_process devtools.verification.test_prepare_nativeaot_spike devtools.verification.test_apply_steam_demo_scope devtools.verification.test_verify_steam_distribution_scope devtools.verification.test_verify_ci_supply_chain
python devtools/verification/verify_ci_supply_chain.py
```

Results: release tests **5 passed, 1 symlink-permission skip**; focused tests **22 passed**;
CI policy scanner passed. No new game export, live Steam test or signing test was run
for this documentation audit. These commands do not cover the untested H3 helper.

- [CI 34357139706](https://github.com/vDalisay/desktop-buddy/actions/runs/34357139706): `70f01700`, quick passed; build-test/full-soak skipped.
- [Scope 34357139753](https://github.com/vDalisay/desktop-buddy/actions/runs/34357139753), [supply chain 34357139690](https://github.com/vDalisay/desktop-buddy/actions/runs/34357139690), [achievement scope 34357139661](https://github.com/vDalisay/desktop-buddy/actions/runs/34357139661): same SHA, passed.
- [SteamPipe 34335438872](https://github.com/vDalisay/desktop-buddy/actions/runs/34335438872): `f9f18a84`, failed; logs contain import/autoload errors and exit 1. Root cause is not established by this audit; rerun current source before prescribing a fix.
- [AOT 34323615195](https://github.com/vDalisay/desktop-buddy/actions/runs/34323615195): latest listed AOT run, failed on pre-fix `5e56cdfd`.
- [itch 34279120252](https://github.com/vDalisay/desktop-buddy/actions/runs/34279120252): `50a96c45`, hardened build passed; not proof of live publication/official-host acceptance.

For every completed step record: source SHA, workflow/build ID, payload manifest hash,
platform/tool identity, exact test/result and evidence location. Fixes create a new
candidate; rerun affected acceptance plus required final-source CI. Never call old
branch CI, a source-only check or an unavailable external gate a final release pass.

## Continuation record — 2026-09-09

- `2f6d14d9`, SteamPipe run `34359734784`, `target=demo`, `upload=false`: passed. Runtime AppID `5228990`, physical Demo scope, PCK, required native files, and a 192-file release manifest passed. The runner-local payload was not retained, so this is preflight evidence only.
- `2f6d14d9`, NativeAOT run `34360174738`: the supervised export, native x86_64 PE, `godotsharp_game_main_init`, and Demo PCK scope passed. Audit rejected two project-reference PDBs before startup/manifest. The workflow now strips PDBs as the managed pipeline does and applies the expanded source/project/debug/map/dev-AppID rejection list; rerun required.
- Source continuation adds shipped-assembly verification, quick-only push enforcement, focused H3 helper/embedded-pack tests, a manual pinned keyed-template compatibility workflow, and the H8 runbook/evidence checklist. Production PCK encryption, AOT, signing, upload, and promotion remain unchanged.
- Pending owner gates are unchanged: signing identity/access, release approvers and allowed branches, public-source policy, repository/environment protection, retained-candidate authorization, and live Windows/Steam/itch acceptance.
