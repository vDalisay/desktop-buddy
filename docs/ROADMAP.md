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
- Passive-income service, mood decay, care gains/cooldowns, and hidden-to-tray low-cost clock.
- Versioned atomic saves, backup/quarantine recovery, one save slot, safe-pose resume, and no catch-up across close/sleep.

Exit criteria:

- Mood/trust, suspend/hidden timing, and save-failure suites pass.
- The buddy visibly differentiates fearful, wary, neutral, content, and delighted behavior without a mood meter.

## Milestone 5 — Shop and Full Tool Catalogue

Deliver tools in the confirmed progression order:

1. Baseball
2. Meal
3. Baseball Bat
4. Pistol
5. Grenade
6. Fire Sprayer
7. Soccer Ball
8. Drink
9. Shotgun
10. Repair Kit

Also deliver the retractable tool/shop/settings panel, pullback trajectory launcher, cursor-direction guns, object budget, permanent purchases, and reset confirmation.

Exit criteria:

- Every tool has automated behavior/error tests and clean-room presentation.
- Economy simulation meets the 3-to-120-minute target schedule and active/passive ratio.

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
- Buddy painting and coloring.
- Cosmetic progression.
- Steam Workshop and custom buddy packages.
- Multiple buddies, profiles, multiplayer, Linux, or macOS.

Architecture may leave explicit seams for future custom buddy definitions, but must not add a speculative mod loader or generalized scripting framework now.
