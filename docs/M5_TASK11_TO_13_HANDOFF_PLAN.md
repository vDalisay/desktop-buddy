# M5 Tasks 11–13 — Power Grab, Economy, and Closeout Handoff

**Status: IMPLEMENTATION-READY PLAN — owner decisions resolved 2026-08-02.** This is the detailed handoff for Tasks 11–13 of `docs/M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md`. It supersedes the former passive “Strength Upgrade” proposal and the former 120-minute completion target.

A small implementation agent should follow the packets in order, use the named existing seams, and stop only at the explicitly identified owner feel/Windows gates. Product choices in this document are closed. If implementation reveals a contradiction with current code, preserve the invariants here and report the exact seam conflict instead of inventing a new behavior.

## 1. Locked product contract

### 1.1 Launch catalogue

There are exactly 16 selectable interactions at launch.

Starting inventory:

1. Normal Grab
2. Pet
3. Tickle
4. Boxing Glove

Purchasable order:

| # | Stable content ID | Display name | Cumulative target |
|---:|---|---|---:|
| 1 | `tool.baseball` | Baseball | 3 min |
| 2 | `tool.baseball_bat` | Baseball Bat | 7 min |
| 3 | `tool.meal` | Meal | 13 min |
| 4 | `tool.nerf` | Nerf | 21 min |
| 5 | `tool.pistol` | Pistol | 41 min |
| 6 | `tool.soccer_ball` | Soccer Ball | 52 min |
| 7 | `tool.grenade` | Grenade | 76 min |
| 8 | `tool.fire_sprayer` | Fire Sprayer | 104 min |
| 9 | `tool.power_grab` | Power Grab | 120 min |
| 10 | `tool.repair_kit` | Repair Kit | 138 min |
| 11 | `tool.shotgun` | Shotgun | 184 min |
| 12 | `tool.drink` | Drink | 209 min |

The catalogue order is display/calibration order, not a prerequisite graph. A player may save for and purchase any visible item once affordable.

### 1.2 Power Grab

Power Grab is a one-time permanent purchase and a separate selectable inventory tool. Normal Grab remains permanently selectable.

Power Grab:

- can acquire buddy parts and the same eligible loose objects as Normal Grab;
- feels dramatically stronger but controllable;
- uses the same maximum limb-stretch distance as Normal Grab;
- retains visible fear and struggling while a buddy part is held;
- never lets the buddy force an escape through the normal sustained-stretch snap rule;
- gives intentional release a stronger launch and a separate, higher safe speed cap;
- does not power-launch on cancel, input loss, invalid target, recovery, or scene teardown;
- changes no damage, payout, mood, statistics, passive income, or other economy multiplier.

Exact force and release numbers are Resource tuning, not hard-coded product constants. The implementation agent chooses safe provisional values, records them in `docs/DECISIONS.md`, and presents Normal-versus-Power comparison evidence at the owner feel gate.

### 1.3 Economy benchmark

The official schedule is the 209-minute completionist schedule in §1.1. Each median cumulative purchase time must be within ±15%.

Only Pistol, Grenade, Fire Sprayer, and Shotgun are high-value items. Their larger grind occurs immediately before each purchase. The representative trace spans about 120 active-interaction minutes plus 89 running background/passive minutes. Active play includes experimentation, care, misses, pauses, and non-optimal attacks.

The completionist strategy is the only strategy judged against target times. Separate save/skip strategies prove that the shop allows free choice without prerequisite purchases.

### 1.4 Reset Progress

A confirmed Reset Progress returns gameplay progression to a first-run state. It clears currency, ownership, selected tool, mood/fullness, learned/harmful memory, novelty/fun memory, traits, local gameplay statistics, local achievement-progress counters, and cumulative gameplay/economy timers. Normal Grab becomes selected.

It preserves language, audio, controls, accessibility, comfort, presentation, window, zoom, and dock preferences. Already-awarded platform achievements remain awarded.

The confirmation dialog must explicitly list the categories erased, use Cancel as the safe/default action, and require a second affirmative action. Cancel, dismissal, missing confirmation, validation failure, or save failure changes nothing.

## 2. Dependency graph and implementation order

1. **11A identity and migration**
2. **11B pure grab policy**
3. **11C Godot adapter and selection routing**
4. **11D catalogue/UI/data**
5. **11E automated scenarios and owner feel evidence**
6. **12A simulation contracts**
7. **12B production-path replay adapter**
8. **12C traces, strategies, report**
9. **12D calibrate Resources**
10. **13A Reset Progress transaction and UI**
11. **13B composition audit and progression journey**
12. **13C full regression, performance, docs, owner exits**

Do not calibrate prices before the Power Grab catalogue entry and schema migration are present. Do not close M5 before the reset failure-path tests and full-catalogue journey pass.

Before the first code edit, capture the current verdict of:

```text
dotnet test
dotnet build DesktopBuddy.sln -c Debug
tools\quick_validate.bat
```

Record pre-existing failures separately. Do not weaken an assertion or exclude a test to make a packet green.

## 3. Task 11 — Power Grab

### 3.1 Existing seams to extend

Use the existing implementation rather than parallel substitutes:

- `domain/DesktopBuddy.Domain/Tools/ToolSelection.cs`: stable `ToolId` values and tool categories.
- `domain/DesktopBuddy.Domain/Content/ContentIds.cs`: stable string IDs, tool mapping, known/catalogue predicates.
- `domain/DesktopBuddy.Domain/Physics/GrabTether.cs`: tether force evaluation and direction-preserving release cap.
- `domain/DesktopBuddy.Domain/Physics/GrabStretchLimiter.cs`: stretch clamp, hysteresis, strain/buzz, and normal snap policy.
- `src/Grab/GrabTetherController.cs`: Godot sampling/application boundary.
- `src/Laboratory/LabPointerGrabComponent.cs`: pointer acquisition currently keyed directly to `ToolId.Grab`.
- `src/Grab/GrabTetherProfile.cs` and existing launch `.tres`: authored Normal Grab values.
- `BuddyProgressState`, `ProgressSave`, and `ProgressSavePolicy`: ownership and explicit schema migration.
- existing catalogue purchase/selection services: atomic spend, ownership, selection, and immediate save flush.

Domain code remains Godot-free. Godot Resources are sampled and validated at the adapter boundary. UI code does not calculate physics.

### 3.2 Runtime contract

Add a domain identity such as:

```csharp
public enum GrabVariant
{
    Normal = 0,
    Power = 1,
}
```

Add an immutable resolved settings value. Exact field names may match existing conventions, but the information boundary must be equivalent to:

```csharp
public readonly record struct GrabResolvedSettings(
    GrabVariant Variant,
    GrabTetherSettings Tether,
    float MaximumStretch,
    bool AllowSustainedStretchEscape,
    float IntentionalReleaseVelocityMultiplier,
    float IntentionalReleaseSpeedCap);
```

Rules:

- resolve settings once when acquisition succeeds;
- store the resolved value in the active-grab state;
- never read mutable Resources during a physics tick;
- never change a live tether when the selected tool changes;
- a selection change while held cancels safely, then the next acquisition uses the new selection;
- Normal uses the existing authored values and existing escape deadline;
- Power derives from the Normal profile plus Power modifiers, uses the identical maximum stretch, and sets `AllowSustainedStretchEscape = false`;
- both variants keep the same clamp/ease-off hysteresis and strain feedback;
- disabling sustained-stretch escape does not disable invalid-state, out-of-bounds, teardown, hard-recovery, or input-loss releases;
- counters in an indefinitely held Power Grab saturate or reset safely and cannot overflow;
- no per-tick allocation or string/ID parsing is allowed.

Model release intent explicitly. Add a small enum or equivalent typed reason:

```csharp
public enum GrabReleaseReason
{
    Intentional,
    SelectionChanged,
    InputLost,
    TargetInvalid,
    Recovery,
    SceneExit,
}
```

Only `Intentional` plus `GrabVariant.Power` applies the Power release multiplier and Power cap. All other reasons use the current safe non-powered release path. Apply the multiplier to the sampled release velocity first, then use the existing direction-preserving cap. A zero or invalid velocity remains zero/safe.

### 3.3 Resource boundary

Keep `GrabTetherProfile` as the Normal baseline. Add `src/Grab/PowerGrabProfile.cs` (or a clearly equivalent typed Resource) with only Power-specific modifiers:

- pull/max-force multiplier;
- optional damping/control multiplier if required for controllability;
- intentional-release velocity multiplier;
- intentional-release speed cap.

Add `src/Grab/GrabSettingsResolver.cs` or an `IGrabSettingsSource` adapter that validates finite positive values, combines the Normal profile with the Power modifiers, and returns `GrabResolvedSettings`. Invalid Resource data fails closed to validated Normal-safe values and emits one actionable diagnostic at composition time, not every physics tick.

Do not duplicate an entire Normal profile into a Power Resource. The identical stretch maximum must come from the same validated Normal field so the two variants cannot drift.

Provisional tuning guidance is intentionally bounded but not owner-fixed: start with a clearly perceptible pull-force increase, add damping only to suppress oscillation, and keep both release caps below velocities that tunnel through the current fixed-step collision setup. Change Resources, not solver code, during feel calibration.

### 3.4 Identity and save migration packet (11A)

Implement these exact identity rules:

- append `ToolId.PowerGrab = 15`; never renumber existing ordinals `0..14`;
- add `ContentIds.ToolPowerGrab = "tool.power_grab"`;
- update `ForTool`, `TryParse`, `IsKnown`, and catalogue predicates;
- map Power Grab to the existing Grab category so pointer behavior is category-driven;
- never repurpose `upgrade.strength`.

Bump the progress schema from 5 to 6. In the explicit v5→v6 migration:

1. clone/normalize the v5 payload through the existing migration policy;
2. if owned IDs contain `upgrade.strength`, add `tool.power_grab`;
3. remove `upgrade.strength`;
4. preserve balance, all other ownership, selection, statistics, timestamps, settings, and unknown-ID handling exactly as current policy requires;
5. make the transform idempotent;
6. serialize only schema 6 and never write `upgrade.strength`.

The deprecated string may remain as a read-only migration alias/comment. It must not be a launch catalogue entry or selectable identity.

Tests:

- every existing ToolId retains its numeric value;
- PowerGrab round-trips through content mapping;
- v5 with legacy ownership migrates to Power Grab exactly once;
- v5 without legacy ownership does not gain Power Grab;
- schema-6 round-trip emits no legacy ID;
- repeated normalization is unchanged.

### 3.5 Pure policy packet (11B)

Extend `GrabStretchLimiter` through typed policy input rather than copying it. At the same stretch samples:

- Normal and Power clamp at the same maximum;
- both enter/leave strain with the same hysteresis;
- Normal snaps at the existing deadline;
- Power remains held beyond that deadline and continues bounded strain feedback;
- hard safety release remains available for Power.

Extend release tests:

- Normal intentional release uses the current cap;
- Power intentional release is faster for the same valid input and never exceeds its higher cap;
- cancellation reasons never receive the Power multiplier;
- cap preserves direction;
- NaN/infinity/zero inputs fail safely.

Add invariance tests proving Power selection alone does not alter `PainCurve`, `RewardLedger`, mood, or statistics for an identical accepted downstream impact.

### 3.6 Godot routing packet (11C)

Replace the hard-coded `tool == ToolId.Grab` acquisition gate in `LabPointerGrabComponent` with a typed category/variant resolver:

- Normal Grab selection → Grab category + Normal variant;
- Power Grab selection, when owned → Grab category + Power variant;
- every non-grab tool → no pointer-grab acquisition;
- unowned Power Grab cannot be selected; corrupted selection normalizes to Normal Grab.

On successful acquisition, pass the resolved immutable settings into `GrabTetherController.TryGrab`. Store release reason explicitly on every exit. Subscribe to the existing selection-change signal/service once; when it changes during an active grab, cancel with `SelectionChanged`. Disconnect during teardown.

Preserve existing target eligibility. Power Grab must not introduce a new collision query, broader physics layer mask, or a second controller. Buddy-part and eligible-loose-object acquisition should differ only by the resolved variant.

Composition roots (`BuddyLab`, `SandboxRoot`, and production main composition) must inject the same resolver/profile data. Scenarios may inject deterministic profiles but cannot bypass the production controller.

### 3.7 Catalogue/UI/data packet (11D)

Create `data/catalogue/tool_power_grab.tres` using the existing selectable-tool entry type. Replace the hidden `upgrade_strength.tres` entry in the launch catalogue; do not increase the catalogue beyond 16 total interactions.

The entry must have:

- stable ID `tool.power_grab`;
- Tool kind/category, not PassiveUpgrade;
- `ToolId.PowerGrab`;
- one-time ownership;
- shop order between Fire Sprayer and Repair Kit;
- icon/name/description/localization keys following current catalogue conventions.

The dock derives owned/selectable tools from `CataloguePolicy.SelectableEntries`; it must show Power Grab after purchase and retain Normal Grab. Purchase does not auto-replace Normal Grab unless the existing general purchase policy selects newly bought tools for every tool. Whichever general behavior is already authoritative must be consistent for all items; do not special-case Power Grab.

Update catalogue validation to assert:

- 16 unique launch interactions;
- four free starters and twelve purchasables;
- exact purchasable order in §1.1;
- every selectable entry maps to exactly one ToolId;
- no `upgrade.strength` entry;
- Power Grab appears in shop while unowned and inventory only while owned.

### 3.8 Scenario and feel packet (11E)

Add headless scenario `power_grab` and journey `m5_power_grab`. Use committed deterministic seeds, including 1 and 7.

The scenario must measure Normal and Power against the same starting pose/target:

- successful buddy-part acquisition;
- successful eligible loose-object acquisition;
- peak/median target-distance error or equivalent pull-strength metric showing a material Power increase;
- same maximum stretch;
- visible fear/struggle signal remains active;
- hold beyond the normal snap deadline: Normal releases, Power does not;
- intentional release speed: Power > Normal and <= Power cap;
- selection change, input loss, hard recovery, and scene exit: safe non-powered release;
- long hold: finite state, bounded counters, no escaped bodies.

The journey buys Power Grab, selects it, switches back to Normal Grab, and reloads the save to prove ownership/selection persistence.

Task 11 is complete when unit tests, both presentation modes of the scenario/journey, catalogue validation, v5 migration, and owner side-by-side feel acceptance pass. The owner gate judges “dramatic but controllable”; automation guards safety and relative behavior.

## 4. Task 12 — Economy simulation and calibration

### 4.1 Architecture

Create a pure domain runner; suggested surfaces:

```csharp
public sealed record EconomyBenchmarkTrace(...);
public sealed record EconomyStrategy(...);
public sealed record EconomyRunResult(...);
public interface IEconomyBenchmarkRunner
{
    EconomyRunResult Run(
        EconomyBenchmarkTrace trace,
        EconomyStrategy strategy,
        EconomyResolvedSettings settings);
}
```

The trace contains timestamped behavior, never prices: accepted/missed contact candidates, care actions if they affect mood, active/background state changes, and elapsed running intervals. The strategy contains only purchase intent/order. The resolved settings contain immutable values sampled from actual Resources and the real `ToolCatalogue`.

Replay the production path:

- contact candidate → `ImpactRouter` → `PainCurve` → `RewardLedger`;
- running passive interval → `PassiveIncome`;
- purchase intent → existing atomic catalogue purchase policy/service.

Do not call UI controllers, physics bodies, wall-clock time, random APIs without a supplied seed, or filesystem APIs from the domain runner. Do not create a “simplified payout” formula.

Add a Godot scenario adapter `economy_calibration` that loads the real launch catalogue plus pain, payout, mood, and passive Resources, validates them once, fingerprints the inputs, and calls the domain runner. Unit tests use synthetic settings only for boundary cases.

### 4.2 Trace contract

Commit at least five fixed seeds. Each representative completionist trace spans 209 running minutes, approximately:

- 120 active-interaction minutes;
- 89 background/passive minutes.

Include experimentation, care, misses, pauses, varied regions/intensities, and non-optimal contacts. Trace generation must be independent of price and payout values. A Resource change may alter results but never regenerate player behavior.

Keep active/background classification explicit so the report can calculate both income sources. Closed/suspended time is absent or marked non-running and produces no catch-up.

### 4.3 Strategies

Implement strategy IDs as data, not conditionals embedded in the runner:

- `completionist_in_order`: attempt each §1.1 item at the first affordable timestamp; this is the only target-time strategy;
- `save_for_pistol`, `save_for_grenade`, `save_for_fire_sprayer`, `save_for_shotgun`;
- at least one regular-item skip strategy;
- `power_grab_preference`: save for Power Grab while leaving at least one earlier regular item unowned.

A failed insufficient-funds attempt charges nothing and does not own the item. A successful purchase charges exactly once. Earlier ownership is never synthesized to satisfy display order.

### 4.4 Report and acceptance

Emit deterministic JSON and Markdown with:

- schema/report version;
- Resource/content fingerprints;
- seed and strategy ID;
- active/background/running minutes;
- active/passive/total income;
- duplicate-contact rejection count;
- each purchase attempt and successful cumulative timestamp;
- ending balance and ownership;
- maximum payout from one ordinary event;
- pass/fail per proof obligation.

Stable ordering and invariant numeric formatting are required for useful diffs. Output paths are supplied by the scenario runner; domain code returns values only.

Official completionist median targets are exactly the table in §1.1, each ±15%. Additional strategies pass by proving unrestricted purchases and accounting invariants, not by matching the completionist schedule.

Proof obligations:

1. active income dominates total representative income;
2. peak-mood passive rate is approximately 25% of benchmark active rate (documented validation band 20–30%);
3. no ordinary accepted event skips multiple intended milestones;
4. a positive / duplicate-zero / later-positive sequence passes the real router and ledger;
5. all twelve catalogue entries are observed once, with no hidden prerequisite;
6. report fingerprints change when an economy Resource changes, while trace identity stays fixed.

### 4.5 Calibration packet (12D)

Tune only typed Resource values: catalogue prices, cash-per-pain/payout curve, and passive-income settings. Keep damage, physics, or benchmark behavior unchanged unless a separately documented bug requires it.

Calibration loop:

1. run all committed completionist seeds;
2. compute the median timestamp per item;
3. identify the earliest out-of-band row;
4. adjust the smallest authoritative Resource surface that controls the miss;
5. rerun every seed and strategy;
6. record the final values, report artifact paths, and rationale in `docs/DECISIONS.md`.

The shape should give quick early unlocks and increasingly larger gaps. The four high-value items receive the exceptional immediately-preceding grind. Do not force prices to be mathematically smooth if the measured target schedule requires otherwise.

Task 12 is complete when every official median is in band, every additional strategy passes, all proof obligations pass, outputs are deterministic, and no final launch price is duplicated in test/runner source.

## 5. Task 13 — Reset, integration, and M5 exit

### 5.1 Reset transaction architecture (13A)

Keep confirmation UI separate from mutation. Add or extend a domain/application service with a contract equivalent to:

```csharp
public interface IProgressResetService
{
    ResetResult ResetConfirmed(ResetConfirmation confirmation);
}
```

The service accepts a typed confirmation token produced only by the affirmative dialog action. It must:

1. reject absent/stale confirmation without mutation;
2. snapshot the current persisted and in-memory state;
3. construct fresh gameplay progress using the same first-run factory used by a new player;
4. explicitly copy the preserved preference payload from the snapshot;
5. preserve the platform-achievement adapter's awarded state while zeroing local counters;
6. validate/normalize the candidate;
7. atomically write and flush through the normal save repository;
8. publish/swap the new in-memory state only after write success;
9. notify catalogue, dock, mood, and statistics presenters from the committed state;
10. return a typed success/failure result.

Never implement reset as field-by-field UI mutation, file deletion, or “write defaults and hope.” If the save fails, retain the exact old state in memory and on disk. Do not issue platform achievement-revocation calls.

The confirmation dialog copy must name: money, purchases/tools, mood and buddy memory/traits, gameplay statistics, achievement progress, and play timers. It must also state that settings/window preferences and already-unlocked platform achievements are kept. Cancel receives initial focus; Escape/close equals Cancel; only the destructive button creates the confirmation token.

Automated reset matrix:

| Category | Confirmed reset |
|---|---|
| Balance, ownership, selection | Fresh; Normal Grab selected |
| Mood, fullness, memories, novelty, traits | Fresh |
| Local gameplay stats/achievement counters/timers | Zero/fresh |
| Language/audio/controls/accessibility/comfort | Preserved exactly |
| Presentation/window/zoom/dock preferences | Preserved exactly |
| Platform-awarded achievements | Untouched |
| Live physics/transients | Reinitialized through normal scene/state refresh |

Test cancel, dismissal, stale/missing confirmation, candidate-validation failure, and injected atomic-save failure with complete before/after equality.

### 5.2 Composition audit (13B)

Build a machine-readable launch inventory from the actual catalogue. Assert:

- 16 selectable interactions;
- four starting, twelve purchasable;
- exact IDs/order/ToolId mapping;
- unique IDs and valid Resource references;
- every ToolId reachable from intended composition roots;
- every purchasable has localization, icon, price, shop row, save round-trip, and scenario/journey coverage;
- `upgrade.strength` exists only in migration coverage;
- all roots inject the same catalogue and grab-settings resolver.

Search for and remove stale hand-maintained tool lists from UI, tests, analytics, and composition. Where an ordered view is required, derive it from catalogue data.

### 5.3 Full progression journey

Update/create `m5_shop_progression` using the actual catalogue and save repository. It must:

1. start with only Normal Grab, Pet, Tickle, and Boxing Glove selectable;
2. earn through production reward/passive paths;
3. purchase the exact twelve-item order;
4. verify balance decreases once and ownership persists after each purchase;
5. select/use every purchased tool, including Normal/Power Grab switching;
6. reload at multiple checkpoints, including after Nerf and Power Grab;
7. execute a separate branch that skips affordable regular items and buys a preferred later item;
8. confirm final catalogue completeness at the Drink purchase;
9. run Reset Progress confirmation, verify the reset matrix, then reload and verify first-run gameplay plus preserved preferences;
10. separately prove Cancel leaves the completed save intact.

Do not make this journey the 209-real-minute calibration run. It may inject deterministic sufficient earnings through the production ledger while Task 12 owns time calibration.

### 5.4 Regression and performance

Run:

- all .NET tests;
- Debug build;
- quick validation;
- every milestone headless scenario and journey in `mii3d` and `legacy`;
- Power Grab seeds 1 and 7;
- economy calibration for every committed seed/strategy;
- full progression seeds 1 and 7;
- standalone Windows 10/11 matrix;
- the established 30-minute soak/performance capture.

Performance evidence must keep domain-allocation measurements separate from Godot frame/process measurements. Power Grab adds no per-tick allocation, duplicate subscription, extra query, or orphaned physics body. Reset leaves no stale presenters or services bound to the pre-reset progress instance.

### 5.5 Documentation and external gates

After implementation, update actual architecture documentation to match shipped code, not planned names. At minimum update:

- `docs/ARCHITECTURE.md`;
- `docs/PRODUCT_REQUIREMENTS.md`;
- `docs/TEST_PLAN.md`;
- `docs/DECISIONS.md`;
- `docs/ROADMAP.md`;
- `docs/M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md`;
- `docs/UI_FLOATING_DOCK_PLAN.md`;
- `docs/OPEN_QUESTIONS.md`;
- catalogue authoring/localization documentation affected by final Resources.

Owner/external gates remain evidence gates, not design questions:

- Normal vs Power Grab side-by-side feel: dramatic, controllable, safe;
- Windows 10/11 overlay, input, dock, reset dialog, and presentation checks;
- final economy report review;
- final catalogue/interaction acceptance;
- clean-room/art direction gate already required by the dock plan.

Task 13 is complete only when all automated evidence is green, Windows and owner gates are recorded, no stale “Strength Upgrade” behavior remains outside migration/history, and M5 exit criteria are checked in the roadmap.

## 6. Required validation commands

Use repository-standard commands and preserve verdicts/artifacts:

```text
dotnet test
dotnet build DesktopBuddy.sln -c Debug
tools\quick_validate.bat
<godot> --headless --fixed-fps 120 --path . -- --scenario=<id> --seed=<n> --presentation=<mode> --artifacts=<dir>
<godot> --headless --fixed-fps 120 --path . -- --journey=<id> --seed=<n> --presentation=<mode> --artifacts=<dir>
```

Run `mii3d` and `legacy`. Close any Godot editor before headless runs. Revert any known tool-induced blank-line-only change to `project.godot` before committing. Never state that a command passed without retaining its verdict.

## 7. Agent handoff response template

Each implementation packet reports:

1. packet ID and scope;
2. files changed;
3. contracts/invariants implemented;
4. migration behavior, if applicable;
5. exact validation commands and verdicts;
6. artifact/report paths;
7. remaining owner/external gate;
8. next packet.

A packet is not complete if it leaves a product TODO, hard-coded duplicate data, untested migration/failure path, or undocumented Resource default.
