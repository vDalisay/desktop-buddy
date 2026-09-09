# Phase 0 closeout — Initial Steam Demo RC

Status: Astra-reviewed implementation handoff; Phase 0 is not yet accepted. Audited branch:
`feature/nativeaot-steam-demo-spike`, HEAD `5e56cdfd`, PR #60, 2026-09-09.
This is an execution checklist for the master release plan's Phase 0, not a new
product authorization. Existing source precedence and owner decisions still apply.

Source-controlled closeout status on 2026-09-09: initial fixes for steps 1–3 are implemented on this
branch. The NativeAOT workflow is manual-only; main currently has no branch
protection requiring its former PR check. A single-process wrapper now waits for
completion with a bounded timeout and covers delayed success, nonzero exit, timeout
and spaced paths. SteamPipe verifies distribution, commit and build identity before
upload. Its pre-export build now targets `DesktopBuddy.csproj` because the solution
does not define an `ExportRelease` configuration. The setup guide now describes the
Linux managed export and unretained build-only payload accurately. A fresh manual
AOT dispatch and steps 4–6 remain pending; none of those external gates is inferred
complete here.

## Final instructions for the next implementation agent — Astra review

Astra reviewed the uncommitted implementation diff and process-wrapper tests on
2026-09-09. The managed project build correction, upload distribution check,
manual-only AOT workflow and removal of overlapping export retries are sound in
direction. This is source review, not independent reproduction of Sol's test runs
or acceptance of a Windows export. Do not mark steps 1–3 fully closed until the
follow-ups and final-commit CI below pass.

Continue from the current working tree; do not recreate Sol's changes or overwrite
the uncommitted plan. Follow AGENTS.md and applicable source documents. Work in this
order:

1. **Finish process timeout ownership.** `run_checked_process.py` uses
   `subprocess.run(..., stdout=PIPE)`. Its timeout terminates the direct process,
   not necessarily Godot's spawned dotnet/MSBuild/compiler descendants. Inherited
   output handles can also keep pipe cleanup waiting. Make the Windows export
   timeout terminate the owned process tree and finish within a bounded interval;
   retain diagnostics. Use the smallest platform-appropriate implementation and
   no retry. Add a runnable test where the child spawns a longer-lived descendant
   that inherits output; verify timeout returns and no owned descendant survives.
   Keep PR regression checks working on Linux as well as Windows.
2. **Remove timeout-test startup flakiness.** The current test gives a fresh Python
   interpreter 100 ms to start and print `started`, then requires that output.
   A slow CI runner can time out before the print. Test partial-output retention
   with deterministic readiness/timeout control, or separate it from the real
   timeout test; do not fix this with repeated blind retries. Cover delayed success,
   one invocation, paths/arguments with spaces, nonzero exit and bounded timeout.
3. **Validate and prepare the final source commit.** Run the focused release-tooling
   tests, relevant managed build/scope checks, YAML parsing and diff checks. Sol
   reported 13 focused verification tests, six manifest tests with one Windows
   symlink-permission skip, 1,565 domain tests, and a zero-warning managed Demo
   build; these are prior-agent results, not final-commit CI evidence. Keep generated
   caches/binaries out of commits. Commit/push only within the user's granted scope,
   then require all applicable PR workflows on that exact commit. Do not use the
   earlier green `5e56cdfd` checks to certify new changes.
4. **Run the managed Demo preflight and retain a candidate.** Follow steps 4–5
   below. A build-only hosted run discards its payload; make a separately identified
   local retained build with the same pinned tooling and checks, or use an explicitly
   authorized private depot upload. Record hashes for the bytes actually tested.
   A local rebuild is a different candidate, even when its source SHA matches.
5. **Optional AOT verdict.** Once the workflow changes are available to dispatch,
   run one manual spike if authorized. Record its first real exporter/compiler or
   runtime failure, or its passed gates. Stop AOT investigation at a broad rewrite
   requirement; continue the managed candidate. A process remaining alive for 15
   seconds is only smoke evidence. It does not authorize production NativeAOT.
6. **Complete the candidate matrix below.** Perform available Windows verification
   and promote concrete regressions into automation. Record unavailable hardware,
   Steam accounts/configuration, owner feel approval and release actions as explicit
   pending gates. Do not claim Phase 0 complete until its release stop rule is met.

Do not change product scope, production AOT settings, public artifact policy,
branch protection or Steam configuration to make a check pass. Do not publish,
upload or promote a Steam build without explicit authorization. Ask the owner only
for a specific missing decision/access/action after completing independent work.
Final handoff must list changed files, actual commands/results, exact candidate
identity, completed gates and remaining external actions.

## Conclusion

Finish Phase 0 on the existing managed SteamPipe path. NativeAOT is an optional
distribution experiment until its complete acceptance matrix passes; it is not
listed as a Phase 0 requirement in the master plan. Do not turn an export experiment
into an indefinite prerequisite for the demo. Do not enable production AOT merely
because compilation succeeds.

Keep Initial Demo scope frozen: no Room Decorator, new achievement system, Next Fest
systems, or new package types. Keep the already accepted physical build exclusions,
manifest validation and CI hardening. Do not reopen retired integration branches.

## Evidence and diagnosis

- PR #60 at the audited HEAD passed full CI, Asset Forge, GodotSteam native offline
  smoke, Steam distribution scope, and achievement build-scope checks.
- NativeAOT run [34323615195](https://github.com/vDalisay/desktop-buddy/actions/runs/34323615195)
  failed before obtaining a completed export. At 07:26:20 it reported an empty exit
  code, launched a retry, printed two Godot startup sequences, and threw while both
  processes were still initializing. This is evidence of faulty process supervision,
  not evidence that NativeAOT compilation is incompatible.
- Before the fixes, the workflow selected the Windows GUI executable and immediately read
  `$LASTEXITCODE`. Its retry can delete output while the first exporter still owns it.
- SteamPipe uses a separate Linux-hosted managed export path. Recent main runs passed,
  but they do not certify this branch's final release payload.
- The spike's 15-second process-alive check proves neither gameplay readiness nor
  persistence, rendering, Workshop, or acceptable performance.
- `STEAM_PIPE_SETUP.md` previously described public binary artifacts and older build
  steps. Sol updated it to match current SteamPipe's no-public-binary-artifact policy.

## Ordered work and exit criteria

| Order | Required action / fix | Exit evidence |
| --- | --- | --- |
| 1 | Separate optional AOT research from release acceptance. Make the spike manual-only; keep failure reporting strict when dispatched. Check branch protection before changing any required-check configuration. | Ordinary PR gates still run; no AOT production settings change; optional spike is explicitly unaccepted. |
| 2 | Fix spike process ownership once: invoke the exporter through Python `subprocess.run` with an argument list, `check=True` and a bounded timeout, or an equivalently explicit Windows process wait. Remove the unconditional bootstrap retry. Capture completed export logs and real return code. | Runnable regression check covers delayed success, nonzero exit and timeout; paths with spaces survive; no second exporter starts or clears a live export's output. One Windows dispatch gives a real verdict. |
| 3 | Close branch hardening. Keep recovered manifest tests and CI safeguards; pass `--expect-distribution` as well as SHA/build ID at the SteamPipe upload verification boundary. Correct SteamPipe setup instructions for the current Linux export and private payload handling. | Relevant manifest/scope/preparation tests pass; all applicable PR workflows green on the final commit; quick CI remains fast and Linux-hosted. |
| 4 | Produce the managed Initial Demo candidate using SteamPipe with `target=demo`, `upload=false`. Audit the exact exported assembly/PCK and complete payload, identity, required native libraries and manifest. Arrange private retention or a local equivalent build for Windows testing; a discarded runner payload cannot serve as the tested RC. | Candidate SHA, run/build ID, runtime AppID, Workshop owner, payload hashes and manifest recorded together. No source/debug/dev-AppID leakage; Room Decorator and achievements physically excluded. |
| 5 | Test the retained candidate on Windows, then install the prospective depot through Steam for platform acceptance. Fix only reproducible blockers and add the corresponding automated coverage. | Completed matrix below, with evidence tied to candidate hashes and installed Steam Build ID. |
| 6 | Finish owner feel/content and store acceptance, then publish through the existing authorized release process. | Explicit acceptance, matching store/build versions, released candidate identified, and post-release launch/save/Workshop sanity check. |

Step 2 is bounded optional research and need not delay steps 3–6. After one correctly
supervised AOT run, record either the first concrete compiler/runtime blocker or the
passed export gates. If a blocker requires broad reflection/serialization rewrites,
leave AOT deferred and continue managed RC closure. If the export passes, AOT still
requires its exact-export gameplay, save, painting, Studio, Work, Workshop and
performance acceptance before a separate production enablement change.

## Exact-candidate acceptance matrix

| Area | Required result | Owner / environment |
| --- | --- | --- |
| Core regression | Full existing scenarios/journeys, scope checks, hostile Workshop validation and offline fallback pass on the final source commit. | Engineering / hosted CI |
| Player flow | Fresh-save onboarding/progression, tools, Paint Room save/reopen, Buddy Studio create/paint/select/restart, Work enter/exit and window restoration work. Maximum-brush responsiveness/continuity is explicitly reviewed. | Engineering and owner / Windows export |
| Persistence | Clean install/reinstall, supported migration, corrupt-primary recovery and authored-state retention pass using isolated test data. | Engineering / Windows |
| Windows shell | Windows 10/11; 100/125/150/200% DPI; monitor removal/recovery; minimum/default/maximized/fullscreen; tray/hidden restore; overlay and online/offline transitions. | Windows test matrix |
| Performance | Four-hour active and four-hour Work soaks; hidden/tray soak under the existing test contract; memory, CPU, paint/upload budgets hold. | Windows test matrix |
| Workshop | Confirm published file-transfer/depot configuration, tags and legal agreement; two accounts publish both package types, subscribe/download/import, explicitly apply, update, and reuse offline. Validate runtime Demo access to the base game's Workshop. | Owner/test accounts / Steamworks and installed depot |
| Cloud | Ordinary Demo Auto-Cloud restores progress, character documents, Buddy paint and room background across machines; settings/recovery/cache remain local. | Owner/test accounts / two Windows machines |
| Presentation/store | Owner accepts progression, tutorial, models/SFX and overall readability/feel; captures describe only Initial Demo features; final clean-room and store/build sanity checks pass. | Owner / exact RC |

Current Cloud documents and the observed spike environment use Demo runtime
`5228990`; Workshop owner remains `5114950`. Verify the final candidate and published
cross-app permissions rather than using a base-AppID development boot as proof.
Do not silently rewrite conflicting historical identity documentation; align it
with the recorded owner decision before any configuration change.

Per `STEAM_CLOUD_DECISION_2026-09-06.md` and `STEAM_CLOUD_SETUP.md`, ordinary Demo Cloud
uses shared Cloud AppID `0` while the full game is unreleased. Demo-to-full cross-App
Cloud activation/testing belongs to full-game release, not Initial Demo closure.
Official itch embed/rehost acceptance remains an itch distribution gate; track it
separately rather than conflating it with Steam Demo acceptance.

## Stop rule and evidence record

Use this checklist as the single Phase 0 closeout queue. Historical owner checklists
may supply applicable test cases but cannot restore Room Decorator or add Potion
Shop/new achievements to Initial Demo. Optional checklist recommendations are not
new release requirements without an owner decision.

For each gate record: candidate commit, payload manifest/hash, workflow or Steam
Build ID, environment, pass/fail, evidence location, and any concrete blocker.
Unknown is pending, never pass. A blocker fix creates a new candidate; rerun affected
Windows checks and the required final-source CI gates. Do not repeat unrelated
experiments without new evidence.

Phase 0 is complete only when the required managed candidate gates pass, owner
acceptance is recorded, the Initial Demo is shipped, and its post-release sanity
check passes. Merging PR #60 alone closes branch work, not Phase 0.
