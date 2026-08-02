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
this milestone** by owner decision 2026-08-02 and is tracked by
`docs/UI_FLOATING_DOCK_PLAN.md`; it carries the reset's player-facing button with it.

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
tool/shop/settings dock was moved out of the exit criteria by owner decision the same day
and is the next scheduled work item; until it ships, Reset Progress has no player-facing
route, which is an accepted known gap. See `docs/UI_FLOATING_DOCK_PLAN.md` — its clean-room
design direction still needs owner approval before implementation starts.

## Milestone 5.5 — Character Editor (Phase A)

Scheduled by the owner 2026-08-02, after the Milestone 5 exit gate and before Milestone 6 —
it has no Steam dependency. Full plan and owner decisions:
`docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md` (Phase A, Tasks A1–A7).

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

- Phase A scenarios and journey pass, including the physics invariant across a character
  swap and the window-restore path.
- Customization remains visual-only by construction: no schema field or editor control
  reaches rig, drive, mass, collision, or tuning.

Painting (Phase B) and Steam Workshop (Phase C) remain deferred with their own gates.

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
- Buddy painting (freehand paint — Phase B of `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`).
  The parametric character editor, Phase A of the same plan, was scheduled 2026-08-02 as
  Milestone 5.5 and is no longer deferred.
- Cosmetic progression.
- Steam Workshop and custom buddy packages (Phase C, requires M6).
- Work Mode typing companion: an optional nonintrusive corner activity where the buddy
  wears glasses, works at a miniature PC, reacts to the player's keypresses by typing,
  and displays a keypress counter. While active, it also provides extra passive earnings
  and periodically awards a bonus based on the keypresses recorded in that session.
- Multiple buddies, profiles, multiplayer, Linux, or macOS.

Architecture may leave explicit seams for future custom buddy definitions, but must not add a speculative mod loader or generalized scripting framework now.
