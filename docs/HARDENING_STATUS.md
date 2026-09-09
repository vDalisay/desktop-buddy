# Distribution hardening — active handoff

Updated: 2026-09-09. The sole active hardening branch is
`feature/nativeaot-steam-demo-spike`. PR #60 is merged; continue new hardening
on this same branch. `main` is the accepted
production baseline. Continue remaining hardening here; do not revive the retired
integration branches. This status does not supersede product scope or release gates.

## Consolidation

| Former branch / tip | Disposition |
| --- | --- |
| `feature/itch-hardening-integration` / `9b5b94a4` | Patch already on main through PR #56 (`f35af365`). |
| `feature/steam-demo-hardening-integration` / `c10ddb58` | File tree identical to main at `b8308c03`, merged through PR #59. |
| `codex/fix-steam-scope-ci` and `feature/distribution-build-hardening` / `d1a946b0` | Whole-identifier Steam verifier and tests already on main. Remaining manifest validation/tests and CI permissions, action pins, checksums and timeouts recovered here. |

The stale distribution branch's broader MSBuild metadata/comment-stripping policy
is intentionally not restored: keep the accepted itch-only scope. Its earlier AOT
preparation is superseded by this branch's newer preparation and tests. Historical
planning/research stays historical; the current master release plan remains authoritative.
The pre-retirement refs are preserved in a local Git bundle under the shared Git
directory (`hardening-retired-2026-09-09.bundle`) for recovery, without active branches.

## Remaining gates

The ordered closeout checklist is now
[`PHASE_0_CLOSEOUT_PLAN_2026-09-09.md`](PHASE_0_CLOSEOUT_PLAN_2026-09-09.md).
It separates the managed Initial Demo RC from optional NativeAOT acceptance and
records the confirmed overlapping-export failure in run `34323615195` at `5e56cdfd`.
The source-controlled fixes in steps 1–3 are applied on the active branch: the AOT
spike is manual-only, its exporter has one bounded process owner with regression
coverage, and SteamPipe checks manifest distribution identity at the upload boundary.
The managed candidate and external acceptance gates remain pending.

- NativeAOT compatibility is **unproven**. Earlier runs failed during faulty Windows
  export supervision. The retry has been removed; the next manual dispatch must run
  the single exporter to completion and report its real exit code before any AOT
  compatibility conclusion or setting change.
- Require native PE/entry-point verification, exported PCK scope, payload audit,
  exact-export startup and manifest verification to pass on the final candidate.
- Before production AOT enablement, complete gameplay, save/restart, Paint Room,
  Buddy Studio, Work Mode, Workshop and performance acceptance on the exact export.
- Windows 10/11 window/DPI/overlay, live Steam configuration, two-account Workshop,
  and official itch embed/rehost acceptance remain external release gates.
- Keep production SteamPipe on its existing managed path until AOT acceptance.
  No store upload or production transformation is authorized by this consolidation.

## Verification

Run `python -m unittest discover -s devtools/release -p 'test_*.py'` for release
manifest validation. These tests now also run in PR/manual `CI / build-test`, not
the push quick job. Existing scenario/journey checks remain intact. Asset Forge
has no push trigger; Phase A remains manual. Ordinary CI uses GitHub-hosted Linux.

PR #60 records the previous merged baseline; track subsequent checks on the same hardening branch. A green ordinary CI run alone does not close
the NativeAOT or external release gates above.

## Owner branch organization — 2026-09-09 reconsolidation

Keep exactly these two active work streams; do not create per-step, fix, spike,
or integration branches for either stream:

- Hardening: `feature/nativeaot-steam-demo-spike` (H0, H3, NativeAOT and release pipeline hardening).
- Master release plan: `feature/master-release-plan-2026-09-08` (existing PR #61).

The hardening branch now contains current main plus the latest H3 helper from
`feature/h3-encrypted-embedded-steam-pck`. The merge tree was verified identical
to that H3 tip before this documentation update. The older
`feature/steam-encrypted-pck-spike` helper is superseded by H3's revised version.
PRs #62–65 were already merged; their side branches and the redundant
`fix/nerf-pistol-active-profile-flake` branch are retired. The already-integrated
`integration/itch-hardening-validated`, `integration/itch-hardening-2026-09-08`,
and `plan/three-build-release-scope` refs are retired too.

Every pre-cleanup ref is recoverable from the verified local bundle
`.git/hardening-consolidation-2026-09-09.bundle`. Historical unrelated branches
are outside this cleanup. This organization changes no feature or release gates.
