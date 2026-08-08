# Desktop Buddy — Implementation Roadmap

Status: Agent handoff roadmap. Milestones are sequential gates, not parallel feature buckets. Each milestone also lands the end-to-end journeys mapped to it in `docs/AGENT_VERIFICATION_AND_E2E.md` Section 7.

## Milestone 0 — Foundation

Deliver:

- Generate the Godot 4.6.1 .NET solution/project and pin nullable C# configuration plus the .NET SDK via `global.json`; document exact editor/export-template versions in `README.md`.
- Commit a `.gitignore` covering `.godot/`, `bin/`/`obj/`, export output, logs, and development `steam_appid.txt`; keep `.import` files versioned.
- Apply the baseline engine configuration from `ARCHITECTURE.md` Section 20: 120 Hz tick, explicit max physics steps per frame, physics interpolation, transparency-allowed flag, window defaults, stretch disabled, custom user directory name, named collision layers, and removal of the Jolt-3D/D3D12 template leftovers.
- Split assemblies per `ARCHITECTURE.md` Section 22: Godot game project, Godot-free domain library, xUnit domain tests, with nested-project excludes and export filters for test/laboratory content.
- Add bootstrap, sandbox, buddy-lab, and test-runner scenes with thin composition roots.
- Establish typed Resource definitions, collision layers, input actions, structured logging, and debug-build guards.
- Add pure C# and headless Godot test entrypoints using the `--headless -- --scenario=<id> --seed=<n>` runner protocol, plus the `--journey=<id>` automation entrypoint and `AutomationDriver` skeleton per `docs/AGENT_VERIFICATION_AND_E2E.md`.
- Commit the pinned `.mcp.json` Godot MCP configuration and the boot smoke journey.
- Stand up CI: `dotnet build`, domain unit tests, headless editor import, and one smoke scenario on every push, with no proprietary Steam binaries.
- Configure Windows export scaffolding without bundling proprietary Steam SDK files.

Exit criteria:

- Editor import and C# build complete without errors.
- Empty bootstrap and headless smoke test run successfully.
- The boot smoke journey passes headless in CI.
- CI is green from a clean clone with no locally installed Steam SDK.
- No legacy branch is merged wholesale; reused code is reviewed and ported deliberately.

## Milestone 1 — Physics Laboratory

Deliver only the high-risk core:

- Six `RigidBody2D` circles and data-driven spring/damper/max-stretch controller.
- Upright drive, autonomous walk/jump impulses, passive/unconscious drive profiles, self-righting, and safe recovery.
- Box boundaries, resizing hooks, zoom hooks, debug telemetry, and direct rendering on each body.
- Elastic grab tether for every part and loose-object prototype.
- Seeded physics scenarios and side-by-side reference tuning workflow.
- Injectable seeded RNG service, manual knockout/unconscious toggle, pause/single-tick/slow-motion laboratory controls, and telemetry export for tolerance-envelope extraction.
- Milestone 1 journeys (spawn/settle, grab-throw, walk/jump, accelerated idle soak) and input-trace recording for the record-and-promote workflow.
- A minimal throwaway standalone transparent-window spike sufficient for the `TEST_PLAN.md` Section 8 pointer-mapping bullet; the production shell remains Milestone 2 work.

Exit criteria:

- Satisfy the complete physics-lab gate in `TEST_PLAN.md`.
- Lock an initial accepted tuning Resource before building payouts or content.

## Milestone 2 — Windows Desktop Shell

Deliver:

- First task: validate per-pixel transparency, MSAA 2D, and V-sync together against the Compatibility renderer on Windows 10/11 hardware; record the renderer decision per `ARCHITECTURE.md` Section 20 before building HUD features.
- Transparent borderless movable/resizable window with simple box borders.
- Work/Play input modes, dynamic buddy/menu hit regions, outside-click focus transition, global hotkey, and tray recovery.
- Multi-monitor/DPI placement, first-launch lower-right placement, off-screen recovery, always-on-top, anti-aliasing, V-sync, and zoom settings.
- Opaque fallback when transparency is unavailable.

Exit criteria:

- Standalone Windows matrix passes at minimum/default/ultrawide sizes.
- The user can always recover control without terminating the process.

## Milestone 3 — Core Interaction and Damage Slice

Deliver:

- Pet, Tickle, Boxing Glove, and Grab.
- Collision attribution, rolling pain window, four-second knockout, region/unconscious payout multipliers, and contact deduplication.
- Hidden mood, transient reaction state, face emoticons, nonverbal sounds, and fear-based grab resistance.
- Minimal money HUD and debug-only tuning panels.

Exit criteria:

- Starting interactions are stable and satisfy all unit/physics requirements.
- Payouts arise from physical pain, never from merely pressing a tool button.

## Milestone 4 — Personality, Care, and Persistence

Deliver:

- Autonomous approach/flee/catch/hold/consume/toss decisions.
- Persistent tool history and the mood-60 trust reset.
- Per-save ambient jump propensity sampled only when starting anew, combined with obstacle/situation evidence so ordinary jumping is reduced and predictable across reloads.
- Passive-income service, mood decay, care gains/cooldowns, and hidden-to-tray low-cost clock.
- Versioned atomic saves, backup/quarantine recovery, one save slot, safe-pose resume, and no catch-up across close/sleep.

Exit criteria:

- Mood/trust, suspend/hidden timing, and save-failure suites pass.
- The buddy visibly differentiates fearful, wary, neutral, content, and delighted behavior without a mood meter.

## Milestone 5 — Shop and Full Tool Catalogue

Implementation status (2026-08-02): **complete and owner-accepted.** All twelve ordered
slices plus Power Grab are implemented, the catalogue is the confirmed sixteen entries, the
economy is calibrated (Task 12), and Task 13 landed Reset Progress, the composition audit,
and the `m5_shop_progression` journey. The dock moved out of scope on the same day.

Historical status (2026-07-27): the first ordered slice, **Baseball**, was in progress.
The atomic permanent-purchase boundary, locked selection rule, immediate purchase save,
shared pullback launcher, typed provisional Baseball/launcher tuning, and real-input
scenario are implemented. In the development laboratory, key `5` only spawns/replaces one
Baseball at the cursor; Grab acquires it, and holding secondary while grabbed previews the
launch. Full pull tuning now produces positive pain and visible pushback; real new saves
keep Baseball locked. Its catalogue price and final physical preset remain deliberately
uncalibrated until the documented M5 economy/feel pass, and no unfinished shop entry is
shown.

Deliver tools in the confirmed progression order:

1. Baseball
2. Baseball Bat
3. Meal
4. Nerf Blaster
5. Pistol
6. Soccer Ball
7. Grenade
8. Fire Sprayer
9. Power Grab
10. Repair Kit
11. Shotgun
12. Drink

Also deliver the pullback trajectory launcher, cursor-direction guns, object budget,
permanent purchases, unrestricted save-for-preference shopping, and the confirmed
full-gameplay reset service. The retractable tool/shop/settings panel was **moved out of
this milestone** by owner decision 2026-08-02, and its draft plan was **withdrawn by the
owner 2026-08-08**; FR-003.2 stays a requirement with no active plan, and it still carries
the reset's player-facing button with it.

Power Grab supersedes the unimplemented passive Strength Upgrade concept. Milestone 5 must
deliver it as a separately selectable, one-time permanent tool while preserving Normal Grab:

- Append `ToolId.PowerGrab` and stable content ID `tool.power_grab`; never repurpose the
  deprecated hidden `upgrade.strength` placeholder, and migrate any development save that
  owns it.
- Reuse the one Grab acquisition/tether pipeline. Select Normal versus Power settings at
  acquisition, keep the same safe stretch limit, preserve visible fear resistance, and
  suppress only Power Grab's forced snap/release.
- Apply Resource-authored higher pull/force authority and a separately capped stronger
  intentional release to buddy parts and eligible loose objects, with no direct economy or
  damage multiplier.
- Validate the exact 16-entry selectable catalogue, the FR-013.4 `3…209` completionist
  schedule, and unrestricted skipping strategies.

Exit criteria:

- [x] Every tool has automated behavior/error tests and clean-room presentation. Sixteen
  interactions, twelve of them purchasable; each has its own scenario and `m5_*` journey, and
  `m5_shop_progression` walks one save through the whole catalogue.
- [x] Economy simulation meets the 3-to-209-minute completionist target schedule,
  unrestricted skipping strategies, the casual 120-active/89-background benchmark, and the
  active/passive ratio (Task 12, `economy_calibration`, five seeds × seven strategies).
- [x] Owner gates: Repair Kit feel, Power Grab feel, the economy pacing report, and the
  catalogue — all accepted 2026-08-02 (`DECISIONS.md`, "M5 owner gates accepted").
- [x] External gate: the Windows 10/11 standalone matrix (`TEST_PLAN.md` §5), accepted by
  owner attestation 2026-08-02, minus the reset row.

**Milestone 5 is COMPLETE (owner-accepted 2026-08-02).** The FR-003.2 retractable
tool/shop/settings dock was moved out of the exit criteria by owner decision the same day.
Its draft plan (`docs/UI_FLOATING_DOCK_PLAN.md`) was **withdrawn and deleted by owner
decision 2026-08-08**: the requirement stands, no design is approved, and no implementation
is scheduled. Until some settings surface carries it, Reset Progress has no player-facing
route, which remains an accepted known gap.

## Milestone 5.5 — Character Editor (Phase A)

Implementation status (2026-08-03): **complete and merged.** Tasks A0–A9 are present on
`main`, including the engine-free schema/compiler, trusted shared visual rig, parameterized
feature renderers, face/accent compositors, failure-safe character store, fixed-tick active
selection, editor window lifecycle, working-copy editor UI, and the Phase A exit
scenario/journey. Real-application fixes and the merged Work/Play redesign preserve editor
window restoration and input ownership.

Originally scheduled by the owner 2026-08-02, after the Milestone 5 exit gate and before
Milestone 6. Full plan and owner decisions:
`docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` (Phase A, Tasks A1–A9).

Deliver:

- Versioned `CharacterDocument` schema with per-part colors and four parametric feature
  slots (eyes, brows, mouth, one body accent), plus the document→visual-profile compiler.
- Parametric feature atlas and part compositor extending the M3.6 face compositor, and the
  `FaceExpressionMap` extended so every reaction state resolves per character.
- Uncapped local character library with the Section 12 atomic write/backup/quarantine
  discipline, lazy enumeration, and the active-character GUID in `progress.json`.
- Editor UI, free from launch, opened by temporarily resizing the shell opaque and
  restoring its geometry and transparency on exit.

Exit criteria:

- [x] Phase A scenarios and journey pass, including the physics invariant across a
  character swap and the window-restore path.
- [x] Customization remains visual-only by construction: no schema field or editor control
  reaches rig, drive, mass, collision, or tuning.

## Milestone 5.6 — Character Painting (Phase B)

Scheduling status (2026-08-03): **active. Task B0 is complete; Task B1 is next.** The
normative scope, requirements, architecture, budgets, task order, and verification are in
`docs/M5_5_PHASE_B_PAINTING_SOURCE_ALIGNMENT.md`.

Design target:

- A simplified, original, clean-room behavioral analogue of MECCHA CHAMELEON's direct
  body-painting interaction, without copying code, assets, UI layout, branding, or
  distinctive presentation.
- A zoomed locked-frontal paint view of the current character working copy, with panning,
  visible zoom controls, and Reset View.
- A color wheel, one circular brush, mouse-wheel and visible-button brush-size controls,
  eraser, Undo, and confirmed undoable Erase All.
- Direct surface-under-brush targeting across the six trusted body parts.
- Six optional 512×512 RGBA8 CPU-authoritative paint surfaces bound below face/accent decals.
- Sequential character-schema migration and atomic whitelisted PNG persistence.
- A 64 MiB CPU editing budget, 8 MiB active GPU paint budget, 2 MiB encoded cap per PNG,
  and 12 MiB aggregate paint payload cap.

Ordered tasks:

1. B1 — frontal ray-to-UV mapping and paint-view camera/pose/zoom/pan.
2. B2 — CPU surfaces, circular brush, eraser, bounded Undo, and undoable Erase All.
3. B3 — underlay preview/runtime binding and upload invalidation.
4. B4 — production painting UI and working-copy integration.
5. B5 — schema migration and atomic PNG persistence/recovery.
6. B6 — full automated, real-input, performance, Windows, and owner feel/fidelity gate.

Exit criteria:

- All required domain tests, headless scenarios, and
  `character_paint_save_use_restart` journey pass.
- Painting remains visual-only and does not rebuild or mutate the physics rig.
- Face and torso-accent decals remain above paint.
- Pixel-identical undo, save/reload, and editor-to-runtime fidelity gates pass.
- CPU/GPU/file budgets and at-most-one-upload-per-dirty-part-per-rendered-frame hold.
- Owner accepts brush feel, frontal framing, pan/zoom, Undo/Erase All behavior, and runtime
  fidelity on real Windows.

Phase C Steam Workshop remains deferred and still requires Milestone 6 and its own C0 policy
gate.

## Milestone 5.7 — Work Mode Typing Companion

Scheduling status (2026-08-08): **active on branch `work-mode-revamp`.** Promoted out of the
deferred list by owner decision; the normative scope, locked owner decisions, and slice
gates are in `docs/WORK_MODE_TYPING_COMPANION_PLAN.md`.

Design target: an opt-in, nonintrusive corner companion. The buddy works at a miniature
retro PC while the player works, a CRT counts accepted key presses and mouse clicks
(session or lifetime), milestones pay credits into the normal ledger, and the first ever
entry grants the Work glasses cosmetic once.

Slice status against plan Section 16:

- [x] WM0 — counters, milestone catalogue, repeat policy, schema migration, idempotent
  claim identities, and Reset Progress integration.
- [x] WM1 — Windows global activity source with held-key suppression and privacy-safe
  diagnostics: no raw key identity ever reaches the domain, logs, or saves.
- [x] WM2 — transparent compact companion, supplied retro PC art, CRT counter, native
  window shape, stored position, and semantic hit regions.
- [x] WM3 — coalesced alternating typing/click reactions, hover-only motion toggle, CRT
  Session/Lifetime toggle, drag, double-click exit, and a hover-only `X` exit control.
- [x] WM4 — milestone settlement into the passive ledger, first-entry glasses ownership
  grant plus one-time auto-equip, and the exit payout summary.
- [ ] WM5 — checkpointing, suspend/resume, and legacy-mode normalization are implemented;
  the four-hour Work soak and the standalone Windows monitor/DPI matrix are outstanding
  owner-run manual gates.

Known gaps, both accepted for now by owner decision 2026-08-08:

- The Work companion renders the active character's compiled appearance but not its
  parametric feature layers, so equipped glasses are owned and equipped yet invisible in
  Work Mode. The fix is to run the Phase A feature renderers on the Work rig; the earlier
  hand-drawn glasses overlay was deleted rather than kept as a stand-in.
- First-entry onboarding copy (plan Sections 13.1/13.3) is unwritten: the privacy sentence
  and the "double-click your buddy to return" teach do not exist in the build.

Exit criteria:

- Counters stay exact under rapid input even when animation cannot render every action, and
  animation-off still counts and rewards.
- No milestone pays twice across restart, crash, or re-entry, and the glasses grant happens
  once.
- Normal shell geometry, transparency, and input ownership restore exactly on exit, and
  global capture always unregisters.
- Work Mode is measurably cheaper than normal gameplay and survives the soak and the
  monitor/DPI matrix.

## Milestone 6 — Steam and Release Systems

Deliver:

- Local and Steam platform implementations behind the same interface; the Steam side is built on Steamworks.NET.
- Cloud-safe progress payload, local-only machine settings, queued offline stats/achievements, and the ten confirmed achievements.
- Windows launch-with-login option, final tray integration, release export preset, SteamPipe/depot instructions, and clean install checks.

Exit criteria:

- Steam acceptance matrix passes from an installed depot.
- Direct non-Steam launch remains fully playable.

## Milestone 7 — Polish and Release Candidate

Deliver:

- Final original vector visuals, VFX, status icons, robot SFX, responsive UI layouts, accessibility settings, and tutorial/help copy.
- Performance optimization without reducing the 120 Hz tick.
- Clean-room content audit, save migration rehearsal, crash/failure-path review, and full regression pass.

Exit criteria:

- Four-hour active and eight-hour hidden soaks pass.
- Performance, Windows, Steam, accessibility, and clean-room gates are green.
- No current-scope feature is represented by a placeholder that can be selected but does not work.

## Deferred Roadmap

Do not implement these during the milestones above:

- Optional blood/bleeding.
- Catalogue, economy, and Work/Play UX research described in
  `docs/UX_CATALOGUE_AND_MODE_RESEARCH_BACKLOG.md`: investigate a unified Shop/Tools
  catalogue where owned entries can be equipped from the same menu; rebalance prices
  together with current active/passive earnings; and prototype smoother mode/layout
  transitions, including the corner cage/room drag concept and clean-room research into
  other desktop-companion interaction patterns. This design backlog is non-blocking for
  Phase B and does not authorize production UX changes yet.
- Painting extensions outside the locked Phase B scope: eyedropper, material sliders,
  pattern/gradient/spray/smudge tools, layers/blend modes, custom brushes, tablet pressure,
  3D orbit/back-side painting, and cosmetic paint progression.
- Cosmetic progression.
- Steam Workshop and custom buddy packages (Phase C, requires M6 and C0).
- Multiple buddies, profiles, multiplayer, Linux, or macOS.

Architecture may leave explicit seams for future custom buddy definitions, but must not add a speculative mod loader or generalized scripting framework now.