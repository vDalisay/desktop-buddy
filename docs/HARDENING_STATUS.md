# Distribution hardening — active handoff

Updated: 2026-09-09. The sole active hardening branch is
`feature/nativeaot-steam-demo-spike`, tracked by PR #60. `main` is the accepted
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

- NativeAOT compatibility is **unproven**. Run `34322499005` at `fca5d838` failed
  during Windows export with an empty exit code while Godot was still starting.
  Investigate process completion/exit-code handling before judging AOT compatibility.
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

Track current check results in PR #60. A green ordinary CI run alone does not close
the NativeAOT or external release gates above.
