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
11. **13B composition audit**
12. **13C full progression journey**
13. **13D regression and performance**
14. **13E docs and owner exits**

Each lettered packet is a separate handoff: it ends with a green build, its own tests, and the
report in §7. 11A→11B→11C→11D→11E is strictly sequential. 12A can start once 11D is merged.

Do not calibrate prices before the Power Grab catalogue entry and schema migration are present. Do not close M5 before the reset failure-path tests and full-catalogue journey pass.

Before the first code edit, capture the current verdict of:

```text
dotnet test
dotnet build DesktopBuddy.sln -c Debug
tools\quick_validate.bat
```

The expected baseline as of 2026-08-02 is **1114/1114 domain tests** and **37/37 quick-suite steps**.
Record any pre-existing failure separately before touching anything. Do not weaken an assertion or
exclude a test to make a packet green; a count that drops is a deleted test and must be explained.

## 2.5 Corrections to this plan after code inspection (2026-08-02)

The product contract in §1 is unchanged. The following implementation instructions were written
against seams that do not look the way the plan assumed. Where they conflict, **§3–§5 below win**;
this list explains why so nobody "restores" the removed machinery.

1. **`GrabReleaseReason` is not needed.** `GrabTetherController.Release(bool countsAsThrow)` already
   exists, and every cancel path already passes `countsAsThrow: false`
   (`SandboxRoot.cs:600,608,614,638,660`, `BuddyLab.cs:647,656,798,820`,
   `LabPointerGrabComponent.cs:385`, `PullbackLauncherComponent.cs:371`). Intentional throw is
   `countsAsThrow: true` at `LabPointerGrabComponent.cs:443` only. That bool **is** the release-intent
   model. A six-value enum would be a rename of a working flag plus eleven call-site edits.
2. **`GrabResolvedSettings` / `IGrabSettingsSource` / `GrabSettingsResolver` are not needed.** Power
   differs from Normal by four numbers. They travel as one nullable `PowerGrabProfile` argument on
   `TryGrab`, stored in one field. No resolver type, no interface with one implementation.
3. **`GrabVariant` is not needed.** `_power is null` is the variant. Adding a two-value enum that is
   only ever read as "is it Power" is the same branch with more files.
4. **"Never read mutable Resources during a physics tick" contradicts shipped code.**
   `GrabTetherController.PhysicsTick` already reads `Profile.Stiffness/Damping/MaximumForce` every
   tick (`GrabTetherController.cs:151-156`). Power Grab follows the existing pattern. Do not rewrite
   the Normal path to satisfy an invariant Normal never had.
5. **"Identical maximum stretch cannot drift" is free.** `PowerGrabProfile` carries no stretch field
   at all; the limiter is always built from `GrabTetherProfile.StretchLimitHandWidths`. There is
   nothing to keep in sync.
6. **`IProgressResetService` + `ResetConfirmation` token are not needed, and preference preservation
   is already free.** Progress and settings are two separate payloads in two separate load calls
   (`Bootstrap.cs:135-141`, `ProgressSave` vs `LocalSettingsSave`). Reset never touches the settings
   store, so §1.4's entire preserve column requires **zero** copying code. First-run construction
   already exists as `Bootstrap.CreateNewProgress` (`Bootstrap.cs:206`). The "typed confirmation
   token" is the dialog's own callback; a token type that only the dialog can mint, handed to a
   service only the dialog calls, is ceremony.
7. **§5.2's "machine-readable launch inventory" already exists.** `CataloguePolicy.LaunchContentIds`
   plus `CataloguePolicy.ValidateLaunchCatalogue` (`CataloguePolicy.cs:41,136`) are that inventory
   and that audit. Extend them; do not generate a second one.
8. **The plan missed a required data fix.** The shipped `.tres` `ProgressionOrder` values do **not**
   match the §1.1 order, and five purchasables are `Visible = false`, so `CataloguePolicy.ShopEntries`
   cannot offer them and `EvaluatePurchase` returns `NotAvailable`. Packet 11D-2 fixes both. Without
   it Task 12 cannot buy half the catalogue.
9. **`IEconomyBenchmarkRunner` is not needed.** One implementation, called from one scenario and the
   tests. A `static class EconomyBenchmark` with a `Run(...)` method is the same thing without the
   indirection. Keep the records — they are data, and they are what the report serializes.
10. **There is no shop UI, no dock, and no achievements subsystem in this repo.** `src/UI/` contains
    exactly one file, `MoneyHudPresenter.cs`. `CataloguePolicy.SelectableEntries` and `ShopEntries`
    have **no production caller** — only tests, `BootSmokeScenario`, and `JourneyRunner`. Tools are
    selected today by lab keyboard shortcuts (`LaboratoryControlComponent.cs:216-240`) and by
    `InteractionDamageComponent.SelectTool`. Grepping for "Achievement" across `src/` and `domain/`
    returns nothing, and `ProgressStatisticsSave` has no achievement fields. Consequences, applied
    throughout §3–§5 below:
    - Task 11 cannot "show Power Grab in the dock". It proves selectability through the catalogue
      policy and the journey runner, which is where every other M5 tool proved it.
    - Task 13's Reset Progress hangs off the existing `TrayCommandComponent` event pattern, not off
      a dialog that does not exist. The modal ships with `docs/UI_FLOATING_DOCK_PLAN.md` Task 7.
    - The reset confirmation copy still names achievement progress (§1.4 is owner-locked product
      text and the counters will exist later), but there is **no achievements code to reset and no
      adapter to avoid calling**. Assert the absence rather than writing a preservation path.

    If the floating dock lands before these packets do, the dock-facing steps become real UI work —
    check before starting 11D-5 and 13A-2 and report which world you are in.

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

### 3.2 Runtime contract (summary; the steps are in §3.4–§3.8)

Power Grab is Normal Grab with four numbers changed and one branch disabled:

| Behaviour | Normal | Power |
|---|---|---|
| Stretch limit | `GrabTetherProfile.StretchLimitHandWidths` | identical (same field, same object) |
| Clamp + hysteresis + buzz | authored values | identical |
| Sustained-strain snap | snaps after `StretchShakeTicks` | never snaps; buzzes at peak indefinitely |
| Pull force | `Stiffness` / `Damping` / `MaximumForce` | each multiplied by a `PowerGrabProfile` factor |
| Intentional release (`countsAsThrow: true`) | velocity capped at `ThrowSpeedCap` | velocity × `ReleaseVelocityMultiplier`, then capped at `PowerReleaseSpeedCap` |
| Any cancel (`countsAsThrow: false`) | existing path | **identical to Normal** — no multiplier, no raised cap |
| Damage / payout / mood / stats | — | unchanged, by construction (Power touches no code on those paths) |

Power is carried as one nullable `PowerGrabProfile` field on `GrabTetherController`, set at
acquisition and cleared at release. `_power is null` means Normal. Exact force numbers are Resource
tuning: pick safe provisional values, record them in `docs/DECISIONS.md`, present a side-by-side at
the owner feel gate. Keep both release caps below the speed that tunnels the current fixed step.

### 3.3 Removed from this task

Do not create `GrabVariant`, `GrabResolvedSettings`, `GrabReleaseReason`, `IGrabSettingsSource`, or
`GrabSettingsResolver`. See §2.5 items 1–5 for why. If you believe one is genuinely required, stop
and report the seam conflict instead of adding it.

---

### 3.4 Packet 11A — identity and save migration

Domain only. No Godot files. Build must be green at the end of this packet on its own.

**11A-1** `domain/DesktopBuddy.Domain/Tools/ToolSelection.cs`
- Append `PowerGrab = 15,` to `ToolId`, after `NerfBlaster = 14`. Change no other ordinal.
- Add a doc comment: appended, not inserted, because ordinals persist.
- In `ToolCatalog.CategoryOf`, add `ToolId.PowerGrab` to the **existing `ToolId.Grab` arm** so it
  reads `ToolId.Grab or ToolId.PowerGrab => ToolCategory.Grab`. This one edit is what makes the
  pointer gate in 11C-1 work by category.

**11A-2** `domain/DesktopBuddy.Domain/Content/ContentIds.cs`
- Add `public const string ToolPowerGrab = "tool.power_grab";` next to `ToolRepairKit`.
- Add `ToolId.PowerGrab => ToolPowerGrab,` to `ForTool` (line ~98).
- Add `case ToolPowerGrab: tool = ToolId.PowerGrab; return true;` to `TryParseTool` (line ~158).
- Change nothing else. `IsTool`, `IsKnown`, and `IsCatalogueEntry` all derive from `TryParseTool`
  and pick the new ID up for free. Leave `UpgradeStrength` exactly as it is — it stays a known,
  non-tool catalogue ID for migration only.

**11A-3** `domain/DesktopBuddy.Domain/Content/CataloguePolicy.cs`
- In `LaunchContentIds`, replace `ContentIds.UpgradeStrength` (last element) with
  `ContentIds.ToolPowerGrab`, and move it into §1.1 position: after `ToolFireSprayer`, before
  `ToolRepairKit`. Reorder the whole purchasable block to match §1.1 exactly:
  grab, pet, tickle, boxing_glove, baseball, baseball_bat, meal, nerf_blaster, pistol, soccer_ball,
  grenade, fire_sprayer, power_grab, repair_kit, shotgun, drink.
- Update the XML doc above it: sixteen selectable interactions, no upgrade.
- `ValidateLaunchCatalogue` needs no change — it counts against `LaunchContentIds`.

**11A-4** `domain/DesktopBuddy.Domain/Persistence/ProgressSave.cs`
- `CurrentSchemaVersion` 5 → 6.

**11A-5** `domain/DesktopBuddy.Domain/Persistence/ProgressSavePolicy.cs`
- Add `private static ProgressSave MigrateV5(ProgressSave save)` next to the existing `MigrateV4`,
  following its exact style (`save with { ... }`).
  - If `UnlockedToolIds` contains `ContentIds.UpgradeStrength`: produce a list with that ID removed
    and `ContentIds.ToolPowerGrab` added **only if not already present**.
  - Always set `SchemaVersion = ProgressSave.CurrentSchemaVersion`.
  - Touch nothing else: balance, selection, statistics, times, extensions, harmful IDs pass through.
- In the `Decode` switch (line ~56): add `MigrateV5(...)` to the outside of every existing arm
  (`1`, `2`, `3`, `4`), and add a new arm `5 => MigrateV5(JsonSerializer.Deserialize<ProgressSave>(...))`.
  The `ProgressSave.CurrentSchemaVersion` arm stays a plain deserialize.

**11A-6** Tests, `tests/DesktopBuddy.Domain.Tests/`
| File | Test |
|---|---|
| `Tools/ToolSelectionTests.cs` (or nearest existing) | every `ToolId` member still has its documented ordinal, `PowerGrab == 15`; `CategoryOf(PowerGrab) == ToolCategory.Grab` |
| `Content/ContentIdsTests.cs` | `ForTool(PowerGrab)` → `"tool.power_grab"` and back; add `"tool.power_grab"` to whatever uniqueness set the file already builds (line ~56) |
| `Content/ContentIdsTests.cs` | `"upgrade.strength"` is still `IsKnown` and still **not** `IsTool` (the existing `[InlineData]` at line ~94 must keep passing) |
| `Persistence/ProgressSavePolicyTests.cs` | v5 JSON owning `upgrade.strength` → owns `tool.power_grab`, does **not** own `upgrade.strength`, schema 6 |
| same | v5 JSON **not** owning it → does not gain `tool.power_grab` |
| same | v5 owning both → owns `tool.power_grab` exactly once (idempotence) |
| same | balance, selection, statistics, times survive the migration byte-for-byte |
| same | serialising a schema-6 save never emits the string `upgrade.strength` |

**11A-7** Existing tests that will now fail and must be *updated, not weakened*:
`Content/CataloguePolicyTests.cs:20-33,82`, `Content/ContentIdsTests.cs:56`,
`Content/TestCatalogues.cs:51,71`, `Content/ToolCatalogueTests.cs:170`,
`src/Testing/BootSmokeScenario.cs:40`. Each asserts the upgrade is present-and-unselectable; each
becomes the Power Grab assertion (present **and** selectable). Report every one you touched.

**Done when:** `dotnet test` green, and a schema-5 save file with `upgrade.strength` loads into a
build that reports Power Grab owned.

---

### 3.5 Packet 11B — pure stretch/release policy

Domain only.

**11B-1** `domain/DesktopBuddy.Domain/Physics/GrabStretchLimiter.cs`
- Add one field to `GrabStretchTuning`, last position, **with a default so no existing call site
  breaks**: `bool AllowSnap = true`. Add it to `Default` implicitly (it defaults to true).
- In `Tick`, change the strain accounting so a non-snapping grab cannot run its counter up forever:
  ```csharp
  _strainTicks = _tuning.AllowSnap
      ? _strainTicks + 1
      : Math.Min(_strainTicks + 1, _tuning.ShakeTicks);
  ```
- Change the terminal branch from `if (remaining > 0)` to `if (remaining > 0 || !_tuning.AllowSnap)`
  so a Power hold keeps returning `Straining` forever. `RampFactor(0)` already returns the peak, so
  a held Power Grab buzzes at maximum escalation — that is the intended visible struggle, not a bug.
- Add a `ponytail:` comment on the clamp naming the ceiling: strain ticks saturate at `ShakeTicks`
  for a non-snapping grab, so the shake phase is stable and the counter cannot overflow.

**11B-2** `tests/DesktopBuddy.Domain.Tests/Physics/GrabStretchLimiterTests.cs`
| Test | Assertion |
|---|---|
| same clamp | `AllowSnap: false` clamps `ClampedTarget` to the identical point as `AllowSnap: true` at the same overpull |
| same hysteresis | both enter `Straining` and ease off to `Slack` at identical distances |
| normal still snaps | `AllowSnap: true` reaches `Snapped` on tick `ShakeTicks` (existing test, must still pass unmodified) |
| power never snaps | `AllowSnap: false` ticked `ShakeTicks * 10` times is still `Straining`, `SnapImpulse == 0` |
| counter saturates | after that run, `StrainTicks == ShakeTicks` |
| reset | `Reset()` returns a non-snapping limiter to `Slack` |

**11B-3** `tests/DesktopBuddy.Domain.Tests/Physics/GrabTetherTests.cs`
- `CapReleaseVelocity(v * multiplier, powerCap)` for a representative `v`: result magnitude
  `> CapReleaseVelocity(v, normalCap)` magnitude, and `<= powerCap`.
- direction preserved to within float epsilon after the multiply-then-cap ordering.
- `Vector2.Zero`, `NaN`, and `Infinity` inputs: no throw, no NaN out (this is the existing
  `CapReleaseVelocity` guard — assert it, do not modify it).

**11B-4** No test is needed proving Power leaves `PainCurve`/`RewardLedger`/mood/statistics alone —
Power touches none of those files. If you find yourself editing one of them, stop: that is a signal
the design drifted, and it is a reportable seam conflict.

**Done when:** `dotnet test` green.

---

### 3.6 Packet 11C — Godot wiring

**11C-1** `src/Grab/PowerGrabProfile.cs` (new, ~30 lines). Copy the shape of `GrabTetherProfile`:
`[GlobalClass] public partial class PowerGrabProfile : GameResource`, four exports, a `Validate()`
that adds an error string per non-finite or out-of-range value.

| Export | Provisional | Range | Why this value |
|---|---|---|---|
| `StiffnessMultiplier` | `2.5f` | `1,10,0.1` | secondary knob — see note 3 |
| `DampingMultiplier` | `1.58f` | `0.5,10,0.1` | `√2.5`; see note 2 |
| `MaximumForceMultiplier` | `3.0f` | `1,10,0.1` | the dominant knob — see note 3 |
| `ReleaseVelocityMultiplier` | `1.6f` | `1,5,0.05` | throw feel |
| `ReleaseSpeedCap` | `1300.0f` | `0.1,100000,1,or_greater` | see note 1 — **hard ceiling 1900** |

Every multiplier must be `>= 1` and finite; `ReleaseSpeedCap` finite and positive. These five are
the owner feel gate's knobs — expect to change them and nothing else during calibration.

Measured constraints behind those numbers (verified 2026-08-02 against shipped data; re-derive if
any of them changes):

1. **Release cap ceiling is 1900 px/s, not "some large number".** Room walls are 16 px thick
   (`scenes/sandbox.tscn:74,77`) and the tick is 120 Hz (`project.godot:77`), so a body moving
   1920 px/s travels a full wall thickness per tick. Buddy parts run `CcdMode.Disabled` — only
   projectiles and the swinging bat enable CCD (`ProjectileBody.cs:148`, `CursorToolController.cs:279`).
   The provisional 1300 px/s is 10.8 px/tick, 68% of a wall. The room is only 480×360, so 1300 px/s
   already crosses it in 0.37 s versus 0.53 s at the Normal 900 cap; going much higher stops reading
   as a throw and starts reading as a teleport. **Do not raise this above 1900 at the feel gate**
   without adding CCD to the grabbed part, which is a different task.
2. **Damping must scale as `√(stiffness multiplier)`, not linearly.** The damping ratio of the PD
   tether is `c / (2√(k·m))`. Scaling `k` by 2.5 while scaling `c` by 1.5 makes Power *less* damped
   than Normal (ratio 0.43 → 0.40 on the 2.5-mass torso), i.e. more overshoot and oscillation — the
   opposite of "controllable". `√2.5 = 1.58` holds the ratio constant. If the owner asks for more
   pull at the feel gate, move `StiffnessMultiplier` and `DampingMultiplier` together by this rule.
   Part masses are 0.7 (hand) to 2.5 (torso) (`data/buddy/lab_puppet_rig.tres`), so the ratio
   already differs per part in Normal; the rule preserves that spread rather than flattening it.
3. **`MaximumForceMultiplier` is the knob the player actually feels.** `Stiffness = 220` against a
   50 px error already demands 11 000 units of force, well past the `MaximumForce = 6000` clamp
   (`GrabTetherProfile.cs:16-18`), so on any real drag the tether is force-clamped and stiffness is
   inert. At ×3 the clamp is 18 000, giving the 2.5-mass torso ~7200 px/s² — it crosses the room
   height from rest in 0.31 s. Tune `MaximumForceMultiplier` first; touch stiffness only if the
   *approach* to the cursor feels wrong rather than the strength.
4. **Solver stability is not a concern at these values.** `ω = √(k/m) = 14.8 rad/s` even at ×2.5
   stiffness, against a 120 Hz step — three orders of margin. No integrator change is needed.

**11C-2** `data/grab/power_grab_profile.tres` (new; match the directory the existing
`lab_grab_tether.tres` lives in) with the provisional values above.

**11C-3** `src/Grab/GrabTetherController.cs`
- Add `private PowerGrabProfile? _power;` and `private GrabStretchLimiter _powerStretch = new();`
- In `Initialize()` (line ~67), after building `_stretch`, build `_powerStretch` from the **same**
  `Profile` fields with `AllowSnap: false`. Two prebuilt limiters, so acquisition allocates nothing.
- Change the signature to
  `public bool TryGrab(RigidBody2D target, Vector2 worldPoint, PowerGrabProfile? power = null)`.
  The default keeps all ~15 existing scenario call sites compiling untouched.
- In `TryGrab`: reject a non-null `power` whose `Validate()` is non-empty by falling back to
  `null` (Normal) and logging **once**; set `_power = power`; point the active limiter at
  `_power is null ? _stretch : _powerStretch` and `Reset()` it.
- In `PhysicsTick`, multiply the three tether inputs when `_power is not null`:
  `Profile.Stiffness * _power.StiffnessMultiplier`, likewise damping and maximum force. Keep reading
  from `Profile` as the code does today (§2.5 item 4).
- In `Release(bool countsAsThrow)` (line ~188), replace the cap call with:
  ```csharp
  bool powered = countsAsThrow && _power is not null;
  NumericsVector2 velocity = ToNumerics(_target.LinearVelocity);
  if (powered) velocity *= _power!.ReleaseVelocityMultiplier;
  NumericsVector2 capped = GrabTether.CapReleaseVelocity(
      velocity, powered ? _power!.ReleaseSpeedCap : Profile.ThrowSpeedCap);
  ```
  Clear `_power = null;` alongside `_leashedPart = null;` at the end of `Release`.
- `SnapBack` calls `Release(countsAsThrow: false)` and is unreachable for Power anyway. No change.

**11C-4** `src/Laboratory/LabPointerGrabComponent.cs`
- Add `[Export] public PowerGrabProfile? PowerProfile { get; set; }`.
- Line ~419: `tool == ToolId.Grab` → `ToolCatalog.CategoryOf(tool) == ToolCategory.Grab`.
- Line ~422: `Grab.TryGrab(body!, cursor, tool == ToolId.PowerGrab ? PowerProfile : null)`.
- Nothing else. `TryPick`, the layer mask, `MoveCursor`, `_ownsGrab`, and `ReleaseIfGrabbing` are
  variant-agnostic and must stay that way.

**11C-5** `src/App/SandboxRoot.cs`
- Line ~614: `previous == ToolId.Grab` → `ToolCatalog.CategoryOf(previous) == ToolCategory.Grab`.
  This is the same root cause as 11C-4; both sites hard-coded the enum where they meant the category.
  Grep for any other `== ToolId.Grab` comparison and fix every one that means "is a grab tool".
- Line ~623 `Objects.MarkPlayerThrown(loose, ContentIds.ToolGrab)`: pass the **selected** tool's
  content ID, so a Power Grab throw is attributed to `tool.power_grab`. Default decision, not an
  owner question: the per-tool statistics dictionaries are keyed by tool, and attributing a Power
  throw to Normal would put a real event under the wrong key. Flag it in your packet report.
- Line ~600/608 and the rest of the cancel sites: unchanged, they already release non-powered.

**11C-6** Scene wiring: set the `PowerProfile` export on the `LabPointerGrabComponent` node in
`scenes/sandbox.tscn`, `scenes/buddy_lab.tscn`, and `scenes/dual_profile_lab.tscn` to
`res://data/grab/power_grab_profile.tres`. Same resource in all three roots.

**Done when:** `dotnet build DesktopBuddy.sln -c Debug` and `tools\quick_validate.bat` green, and
every pre-existing grab scenario still passes unchanged (they call the 2-argument `TryGrab`).

---

### 3.7 Packet 11D — catalogue data

**11D-1** `data/catalogue/tool_power_grab.tres` (new). Copy `tool_repair_kit.tres` verbatim and change:
`resource_name = "CataloguePowerGrab"`, `ContentId = "tool.power_grab"`, `Kind = 0` (the same Kind
`tool_grab.tres` uses — a Grab-category tool), `PriceCredits = 105` (provisional, Task 12D owns the
final number), `ProgressionOrder = 12`, `Visible = true`, name/description keys
`shop.tool.power_grab.name` / `.description`.

105 is derived, not guessed: Power Grab's §1.1 slot spans 104→120 min (16 min) and the Repair Kit's
spans 120→138 min (18 min) at 120 credits, so at a locally constant earn rate
`120 × 16/18 ≈ 107`. Round to 105.

**11D-2** Fix the §1.1 ordering and visibility across the existing `.tres` files. The shipped data
does **not** currently match §1.1 and five entries cannot be sold at all. Set exactly:

| File | `ProgressionOrder` | `Visible` |
|---|---:|---|
| `tool_grab.tres` | 0 | true |
| `tool_pet.tres` | 1 | true |
| `tool_tickle.tres` | 2 | true |
| `tool_boxing_glove.tres` | 3 | true |
| `tool_baseball.tres` | 4 | true |
| `tool_baseball_bat.tres` | 5 | true |
| `tool_meal.tres` | 6 | true |
| `tool_nerf_blaster.tres` | 7 | true |
| `tool_pistol.tres` | 8 | true |
| `tool_soccer_ball.tres` | 9 | **true** (was false) — Task 8 accepted 2026-08-01 |
| `tool_grenade.tres` | 10 | true |
| `tool_fire_sprayer.tres` | 11 | **true** (was false) — Task 7 accepted 2026-08-01 |
| `tool_power_grab.tres` | 12 | true |
| `tool_repair_kit.tres` | 13 | **see below** — Task 10 feel gate still open |
| `tool_shotgun.tres` | 14 | **true** (was false) — Task 9 accepted 2026-08-01 |
| `tool_drink.tres` | 15 | **true** (was false) — Task 8 accepted 2026-08-01 |

`Visible = false` makes `CataloguePolicy.ShopEntries` skip the entry and `EvaluatePurchase` return
`NotAvailable`, so Task 12 cannot buy them. This table is a hard prerequisite for Task 12.

**Repair Kit is the one conditional flip.** Soccer Ball, Fire Sprayer, Shotgun, and Drink are all
owner-accepted, so making them shop-visible is the normal post-acceptance step and is overdue —
the same step Meal, Bat, Nerf, Pistol, and Grenade already took. Repair Kit is implemented but its
owner feel gate has not been recorded. Flip it with the rest so Task 12 can price the full twelve,
and note in the packet report that Repair Kit is shop-visible ahead of its feel gate; if the owner
would rather it stay hidden until then, Task 12 must run with a documented placeholder price and
12D must be re-run after acceptance. Do not silently leave it hidden — that breaks the twelve-item
schedule without anyone noticing.

**11D-3** `data/catalogue/launch_catalogue.tres`
- Swap the `ext_resource` at `id="17"` from `upgrade_strength.tres` to `tool_power_grab.tres`.
- Leave `Entries` at sixteen elements. Order inside the array does not matter (`ProgressionOrder`
  drives display), but keep it readable.
- Delete `data/catalogue/upgrade_strength.tres`. Nothing references it after this step — confirm
  with a grep before deleting, and report if anything still does.

**11D-4** Localization: add `shop.tool.power_grab.name` / `.description` wherever the other
`shop.tool.*` keys live. Follow the Repair Kit's entry exactly (it was the most recent addition).

**11D-5** Inventory/selection: **no UI exists to change** (§2.5 item 10). Power Grab is
`IsSelectable` by virtue of being a Tool kind, and that is all any future dock will need. Prove it
two ways instead of by eye:
- a unit test that `CataloguePolicy.SelectableEntries(launchCatalogue)` returns sixteen entries
  including `tool.power_grab`, and that `ShopEntries` returns the twelve purchasables in §1.1 order;
- the 11E-2 journey, which selects Power Grab through `InteractionDamageComponent.SelectTool` — the
  same path every shipped M5 tool uses.

Add a lab keyboard shortcut for Power Grab in `LaboratoryControlComponent` (line ~216 area, next to
the existing `ToolId.Grab` binding) so the owner can actually reach it at the 11E-4 feel gate. Pick
an unused key and record which one in the packet report. **Without this the feel gate cannot be
run** — there is no other way to select the tool by hand.

**11D-6** Purchase-selection behaviour: whatever the existing purchase policy already does for the
Repair Kit (auto-select or not), Power Grab does the same. Do **not** add a Power Grab branch.

**11D-7** Extend `CataloguePolicy.ValidateLaunchCatalogue` with two asserts only (the count and
starting-set checks already exist): every entry's `ProgressionOrder` is unique, and the ordered
purchasable IDs equal the §1.1 sequence. Test in `Content/CataloguePolicyTests.cs`.

**Done when:** `tools\quick_validate.bat` green and `BootSmokeScenario` passes with its updated
assertion from 11A-7.

---

### 3.8 Packet 11E — scenario, journey, feel evidence

**11E-1** `src/Testing/PowerGrabScenario.cs` (new), registered as scenario id `power_grab`,
modelled on `src/Testing/GrabReleaseScenario.cs`. Committed seeds 1 and 7. It runs the **same**
sequence twice against an identically reset pose — once `TryGrab(target, point)` and once
`TryGrab(target, point, powerProfile)` — and writes both result sets to one artifact.

Per run, record and assert:

| # | Measurement | Assertion |
|---|---|---|
| 1 | buddy-part acquisition | both succeed |
| 2 | loose-object acquisition | both succeed |
| 3 | median `Telemetry.Extension` while dragging a fixed cursor path | Power materially lower (tracks the cursor harder) |
| 4 | `_stretch.LimitFor(radius)` | byte-identical between runs |
| 5 | hold `StretchShakeTicks + 120` ticks | Normal: `IsGrabbing == false`, `SnapCount == 1`. Power: `IsGrabbing == true`, `SnapCount == 0`, `StretchState == Straining` |
| 6 | fear/struggle signal during the Power hold | still active at the last tick |
| 7 | `Release(countsAsThrow: true)`, then `LastReleaseSpeed` | Power > Normal, and Power <= `ReleaseSpeedCap` |
| 8 | `Release(countsAsThrow: false)` from a Power hold | `LastReleaseSpeed <= Profile.ThrowSpeedCap` |
| 9 | tool switch away mid-Power-hold | `IsGrabbing == false`, speed under the Normal cap |
| 10 | 10 000-tick Power hold | `StrainTicks == StretchShakeTicks` (saturated), no body outside the room bounds, no NaN in any telemetry field |

Rows 8 and 9 are the safety core: they prove the raised cap cannot be reached by any path except a
deliberate throw. Do not let them be dropped for time.

**11E-2** `m5_power_grab` journey (register alongside the existing M5 journeys): buy Power Grab from
the shop, assert balance decreased exactly once and ownership persists, select it, grab and throw,
switch back to Normal Grab, save, reload, assert both tools still owned and the selection round-trips.

**11E-3** Run both in `mii3d` and `legacy`, seeds 1 and 7, and keep the artifact paths.

**11E-4** Owner feel gate. Produce a side-by-side capture (Normal then Power, same target, same
drag path) plus the row-3 and row-7 numbers, and record the provisional `PowerGrabProfile` values in
`docs/DECISIONS.md`. The owner judges "dramatic but controllable". **Stop here and hand back** —
only the five `PowerGrabProfile` numbers may change in response, never solver code.

**Task 11 is complete when:** 11A–11D automated evidence is green, both 11E runs pass in both
presentation modes, and the owner has accepted the feel.

## 4. Task 12 — Economy simulation and calibration

**Prerequisite: packet 11D-2 must be done.** Five purchasables are `Visible = false` in shipped data
and cannot be bought at all. Verify before starting: every entry in `LaunchContentIds` returns
something other than `PurchaseStatus.NotAvailable` from `EvaluatePurchase` at infinite balance.

### 4.1 Packet 12A — the domain runner

New folder `domain/DesktopBuddy.Domain/Economy/Benchmark/`. Pure C#: no Godot, no `DateTime.Now`,
no unseeded RNG, no file IO. One static class, not an interface (§2.5 item 9).

**12A-1** `BenchmarkEvent.cs` — the trace element. One record, closed set of kinds:
```csharp
public enum BenchmarkEventKind { Contact, Care, ActiveStart, BackgroundStart }
public readonly record struct BenchmarkEvent(
    double AtSeconds, BenchmarkEventKind Kind, string ContentId, float Magnitude, int BodyRegion);
```
No prices, no payouts, no tool costs — a trace is *what the player did*, so a Resource change never
regenerates behaviour. `Magnitude` feeds the existing impact path; it is not a credit amount.

**12A-2** `BenchmarkStrategy.cs` — purchase intent as data:
```csharp
public sealed record BenchmarkStrategy(string Id, IReadOnlyList<string> PurchaseOrder);
```
The runner buys the first still-unowned entry in `PurchaseOrder` whenever the balance allows. No
`switch` on strategy id anywhere in the runner — that is the whole point of it being data.

**12A-3** `BenchmarkResult.cs` — one record holding: seed, strategy id, running/active/background
seconds, active income, passive income, duplicate-contact rejections, the ordered list of
`(ContentId, PurchasedAtSeconds, PriceMilliCredits)`, ending balance, ending ownership, and the
largest single-event payout. Add nothing the report does not print.

**12A-4** `EconomyBenchmark.cs` — `public static BenchmarkResult Run(IReadOnlyList<BenchmarkEvent>
trace, BenchmarkStrategy strategy, ToolCatalogue catalogue, <existing settings types>)`.
It walks the trace in timestamp order and, per event, calls the **real production types**:
`ImpactRouter` → `PainCurve` → `RewardLedger` for contacts, `PassiveIncome` for elapsed background
intervals, and `CataloguePolicy.EvaluatePurchase` + the existing atomic spend for purchases. If you
find yourself writing an arithmetic payout expression, you have forked the economy — stop and use
the real type. After each event that increases the balance, attempt the strategy's next purchase.

**12A-5** `BenchmarkTraceGenerator.cs` — `public static IReadOnlyList<BenchmarkEvent> Generate(int
seed)` using the existing `SeededRandomSource`, producing ~209 running minutes: ~120 active
(contacts across varied regions and magnitudes, some care, deliberate misses and duplicate contacts,
short pauses) and ~89 background. Generate, do not commit, giant fixture files — the seed *is* the
fixture. Closed/suspended time simply is not in the trace, so no catch-up can occur.

**12A-6** Unit tests, `tests/DesktopBuddy.Domain.Tests/Economy/EconomyBenchmarkTests.cs`, with a
tiny hand-built 5-event trace and synthetic settings (not the real catalogue):
- insufficient funds: balance unchanged, not owned, run continues;
- affordable: balance decreases by exactly the price, owned once;
- a second purchase attempt of an owned entry: no second charge;
- a duplicate contact (same shot/contact id) scores zero and increments the rejection count;
- `Generate(1)` twice returns identical traces; `Generate(1)` != `Generate(2)`;
- `Run` with the same inputs twice returns an identical `BenchmarkResult`.

### 4.2 Packet 12B — Godot scenario adapter

**12B-1** `src/Testing/EconomyCalibrationScenario.cs`, scenario id `economy_calibration`. It loads
the real `CatalogueLoader.Catalogue` plus the real pain/payout/mood/passive Resources, validates
them once (fail the scenario on any validation error), computes a fingerprint over the resource
values plus the content ids, calls `BenchmarkTraceGenerator.Generate(seed)` and `EconomyBenchmark.Run`
for each strategy, and writes the artifacts. All file IO lives here; the domain returns values only.

**12B-2** Fingerprint: a stable hash over the ordered `(ContentId, PriceCredits, ProgressionOrder)`
tuples plus the sampled economy Resource values. It must change when a price changes and must not
change when a trace seed changes.

### 4.3 Packet 12C — strategies, traces, report

**12C-1** Commit five seeds: 1, 7, 13, 29, 101. Every strategy runs against all five.

**12C-2** Strategy list, all built as `BenchmarkStrategy` data in one file:

| Id | `PurchaseOrder` |
|---|---|
| `completionist_in_order` | the exact §1.1 twelve — **the only strategy judged on target times** |
| `save_for_pistol` | pistol first, then §1.1 order |
| `save_for_grenade` | grenade first, then §1.1 order |
| `save_for_fire_sprayer` | fire sprayer first, then §1.1 order |
| `save_for_shotgun` | shotgun first, then §1.1 order |
| `skip_regulars` | §1.1 order with baseball, meal, and soccer ball omitted entirely |
| `power_grab_preference` | power grab first, leaving at least one earlier regular unowned at the end |

**12C-3** Report writer: deterministic JSON **and** Markdown, same data, stable key order, invariant
culture numeric formatting (`CultureInfo.InvariantCulture`, fixed decimal places) so diffs are
readable. Contents: report version, fingerprints, seed, strategy id, running/active/background
minutes, active/passive/total income, duplicate rejections, every purchase attempt with its
cumulative timestamp, ending balance and ownership, largest single-event payout, and pass/fail per
proof obligation below.

**12C-4** Proof obligations, asserted in the scenario (fail the run, do not just print):
1. active income > passive income for a representative completionist run;
2. peak-mood passive rate is 20–30% of the benchmark active rate;
3. no single ordinary accepted event pays enough to skip more than one §1.1 milestone;
4. a positive → duplicate-zero → later-positive contact sequence produces `+x, 0, +y` through the
   real router and ledger;
5. all twelve purchasables are bought in `completionist_in_order`, and every `save_for_*` strategy
   buys its target **before** at least one cheaper earlier item — proving no prerequisite graph;
6. changing one price changes the fingerprint while the trace hash stays fixed.

### 4.4 Packet 12D — calibration

Change **only** these Resource values: `PriceCredits` in the twelve purchasable `.tres` files, the
cash-per-pain / payout curve values, and the passive-income settings. Not damage, not physics, not
the trace generator, not a threshold in a test.

**Expect to move most of the twelve prices, not a few.** The shipped prices cannot produce the §1.1
schedule under any single earn curve: the Fire Sprayer slot spans 28 minutes for 50 credits
(1.8 cr/min) while the Repair Kit slot spans 18 minutes for 120 credits (6.7 cr/min) — a 3.7×
inconsistency between two adjacent regions. Those prices were each set when their own tool shipped,
against no schedule. Budget for a full re-pricing pass, and do not treat a large diff here as a
sign you did something wrong.

1. run `completionist_in_order` across all five seeds;
2. take the median cumulative purchase time per item;
3. find the **earliest** row outside ±15% of its §1.1 target — fix that one first, since every later
   row inherits its error;
4. change the single smallest authoritative value that moves it (usually that item's price; the
   payout curve only when a whole span drifts together);
5. rerun all five seeds **and** all seven strategies;
6. repeat until every median is in band;
7. record final prices, curve values, artifact paths, and the reasoning in `docs/DECISIONS.md`.

Expect quick early unlocks and widening gaps, with the exceptional grind sitting immediately before
Pistol, Grenade, Fire Sprayer, and Shotgun. Do not smooth the price curve for its own sake — the
measured schedule wins.

**Task 12 is complete when:** every completionist median is within ±15%, all seven strategies pass
their accounting invariants, all six proof obligations pass, two runs of the same seed produce
byte-identical reports, and no final price literal appears anywhere outside the `.tres` files.

## 5. Task 13 — Reset, integration, and M5 exit

### 5.1 Packet 13A — Reset Progress

**Preferences are preserved for free.** Progress and settings are two independent payloads loaded by
two independent calls (`Bootstrap.cs:135-141`): `ProgressSave` and `LocalSettingsSave`. Reset writes
only the progress store. Language, audio, controls, accessibility, comfort, presentation, window,
zoom, and dock preferences are preserved because **nothing touches them** — do not write copy-forward
code for them, and do not add them to `ProgressSave` to "make the reset explicit".

**13A-1** `src/App/ProgressReset.cs` (new, ~40 lines). One `static async Task<bool> ResetAsync(...)`,
no interface, no token type (§2.5 item 6). In order:
1. build fresh progress with the **existing** first-run factory — move `Bootstrap.CreateNewProgress`
   (`Bootstrap.cs:206`) to a shared place and call it from both sites, so a new player and a reset
   player cannot drift;
2. write and flush it through the normal `SaveCoordinator` path with `force: true`;
3. **only on write success**, swap the in-memory progress reference and re-point `EconomyService`,
   the dock, the mood presenter, and the statistics presenter at the new instance;
4. on write failure, return `false` having mutated nothing — the old state stays in memory and on
   disk. Do not delete the save file, ever, on either path.

Do not issue any platform achievement call. Awarded achievements are untouched because reset never
speaks to that adapter; local counters are zero because they live in the fresh `ProgressSave`.

**13A-2** The trigger, **not a dialog** — there is no dock to put one in (§2.5 item 10). Extend
`src/Platform/TrayCommandComponent.cs`, which already owns exactly this pattern
(`SaveAndQuitRequested` + `RequestSaveAndQuit()` + a request counter):
- add `public event Action? ResetProgressRequested;`
- add `public void RequestResetProgress()` that raises it and increments a counter;
- `SandboxRoot` subscribes once and calls `ProgressReset.ResetAsync`; disconnect on teardown.

The two-step confirmation lives here, testably, without any UI: `RequestResetProgress()` **arms**
the request and returns; a second `ConfirmResetProgress()` within the arming window performs it;
anything else — a timeout, `CancelResetProgress()`, another unrelated tray command — disarms it and
mutates nothing. That is the "Cancel is the default, two affirmative actions" contract in §1.4,
expressed where it can actually be asserted today.

**13A-2b** Record in `docs/DECISIONS.md` that the confirmation *modal* — the copy naming money,
purchased tools, mood and buddy memory and traits, gameplay statistics, achievement progress, and
play timers, plus the "settings and platform achievements are kept" line, with Cancel focused and
Escape equal to Cancel — ships with `docs/UI_FLOATING_DOCK_PLAN.md` Task 7 and binds to the armed
event above. Do not write the copy into a placeholder dialog nobody opens; do add the localization
keys so the dock task has them waiting.

**13A-3** Reset matrix, asserted in tests:

| Category | After a confirmed reset |
|---|---|
| Balance, ownership, selection | fresh; `tool.grab` selected |
| Mood, fullness, memories, novelty, traits | fresh (traits resampled by the first-run factory) |
| Local stats and timers (`ProgressStatisticsSave`, `Times`) | zero |
| Achievement counters | **do not exist yet** — assert absence, see 13A-3b |
| Language/audio/controls/accessibility/comfort | untouched — different file |
| Presentation/window/zoom/dock preferences | untouched — different file |
| Platform-awarded achievements | untouched — no adapter exists to call |
| Live physics/transients | reinitialized by the normal state refresh |

**13A-3b** There is no achievements subsystem (§2.5 item 10). Do not build one, do not add fields to
`ProgressStatisticsSave` for it, and do not write a "preserve awarded achievements" code path — a
preservation path for a system that does not exist is untestable and will rot. Instead leave one
guard so the promise cannot be broken silently later: a test asserting that
`grep -r "Achievement"` over `src/` and `domain/` still returns nothing, or equivalently that
`ProgressStatisticsSave` has no member whose name contains "Achievement". When achievements land,
that test fails and forces whoever adds them to revisit this matrix row. Note it as a `ponytail:`
comment on `ProgressReset` naming the ceiling.

**13A-4** Tests, `tests/DesktopBuddy.Domain.Tests/Persistence/` plus a scenario for the UI path:
- confirmed reset produces a state equal to a brand-new save except for resampled traits;
- armed-but-not-confirmed, then disarmed: progress on disk and in memory byte-identical;
- confirm **without** a prior arm: returns `false`, mutates nothing;
- confirm after the arming window lapses: returns `false`, mutates nothing;
- injected save failure (fake store that throws on write): returns `false`, in-memory balance and
  ownership unchanged, on-disk file unchanged — assert full equality, not just balance;
- the settings file's bytes are unchanged across a confirmed reset;
- after reset, `CataloguePolicy.SelectableEntries` filtered by ownership yields exactly the four
  starting tools and `EconomyService.BalanceMilliCredits` reads zero **through the same instances
  the scene is holding** — this is what proves the presenters re-bound to the new progress object
  rather than a stale one. `MoneyHudPresenter` is the one real presenter today; assert against it
  directly.

### 5.2 Packet 13B — composition audit

The inventory already exists: `CataloguePolicy.LaunchContentIds` and `ValidateLaunchCatalogue`
(§2.5 item 7). Extend, do not rebuild.

**13B-1** Add to `ValidateLaunchCatalogue` (beyond the count/starting checks already there and the
order/uniqueness checks from 11D-7): every entry id is `IsCatalogueEntry`; every selectable entry
maps to exactly one `ToolId` via `TryParseTool`; no entry has `ContentIds.UpgradeStrength`.

**13B-2** One test asserting `ForTool` is total over `ToolId`: iterate `Enum.GetValues<ToolId>()`,
call `ForTool`, assert no throw and sixteen distinct strings. This is the check that catches a
future appended tool that was never wired.

**13B-3** Grep for hand-maintained tool lists and delete them, deriving from the catalogue instead:
```bash
rg -n "ToolId\.(Grab|Pet|Tickle)" --glob '!domain/**' --glob '!tests/**'
```
Any array, switch, or dock list that enumerates tools by hand is a drift source. Report each one you
found and whether you removed it. `ToolCatalog.CategoryOf` and `ContentIds.ForTool` are the two
legitimate total switches — leave them.

**13B-4** Assert all three composition roots (`BuddyLab`, `SandboxRoot`, production main) reference
the same `power_grab_profile.tres` and the same launch catalogue. A scene test or a startup
assertion, whichever the repo already does for the grab tether profile.

**13B-5** `upgrade.strength` must appear **only** in `ContentIds`, the v5→v6 migration, and migration
tests after this packet. Grep to confirm; the `.tres` was deleted in 11D-3.

### 5.3 Packet 13C — full progression journey

Journey id `m5_shop_progression`, seeds 1 and 7, both presentation modes. Model it on the existing
`m5_*` journeys in `src/Testing/` and register it in `JourneyRunner`. This is **not** the 209-minute
calibration run — inject deterministic earnings through `EconomyService.AcceptDamage` /
`DepositPassive` (the production ledger, never a balance poke) and let Task 12 own real timing.

**13C-1 Main branch**, in order, asserting at each step:

| Step | Action | Assertion |
|---|---|---|
| 1 | fresh save | owned set is exactly the four §1.1 starters; `SelectableEntries` ∩ owned has 4 members; balance 0 |
| 2 | earn to the first price | balance rose only through the ledger |
| 3–14 | buy the twelve in §1.1 order via `EconomyService.Purchase` | per item: balance drops by exactly `PriceCredits`, a second `Purchase` of the same id returns `AlreadyOwned` and charges nothing, ownership count increments by one |
| 15 | select and exercise each purchased tool | `SelectTool` succeeds for all sixteen; each tool's characteristic effect fires at least once |
| 16 | Normal ⇄ Power Grab switch while holding | grab, switch, assert released non-powered (11C-5), re-grab with the new selection |
| 17 | at the Drink purchase | `ValidateLaunchCatalogue` returns no errors and all sixteen are owned |

**13C-2 Checkpoints.** Save and reload after Nerf (step 6) and after Power Grab (step 12). Each
reload asserts: ownership set identical, selection identical, balance identical, schema is 6.

**13C-3 Skip branch** (separate run, same journey id, different phase): from a fresh save, earn past
Baseball/Meal/Soccer Ball without buying them, buy Shotgun directly, assert it is owned while all
three cheaper items are not. This is the no-prerequisite-graph proof; it must not share state with
13C-1.

**13C-4 Reset branch**: from the completed 13C-1 state, arm and confirm reset (13A-2), assert the
full 13A-3 matrix, reload from disk, assert first-run gameplay state and byte-identical settings.

**13C-5 Cancel branch**: from the completed 13C-1 state, arm and disarm, assert the save is
byte-identical to before. Run this **after** 13C-4 in a fresh copy, so a passing cancel test can
never be an artefact of the reset having already run.

### 5.4 Packet 13D — regression and performance

**13D-1** Run and retain the verdict of each, in this order — stop and fix on the first red:

| # | Command | Expected |
|---|---|---|
| 1 | `dotnet test` | 1114 + the new tests, 0 failed |
| 2 | `dotnet build DesktopBuddy.sln -c Debug` | 0 warnings introduced |
| 3 | `tools\quick_validate.bat` | 37 + the new steps |
| 4 | every milestone scenario and journey, `mii3d` **and** `legacy` | all pass |
| 5 | `power_grab` + `m5_power_grab`, seeds 1 and 7, both modes | all pass |
| 6 | `economy_calibration`, all 5 seeds × 7 strategies | all in band |
| 7 | `m5_shop_progression`, seeds 1 and 7, both modes | all pass |
| 8 | standalone Windows 10 and 11 run | owner/external gate |
| 9 | the established 30-minute soak capture | no regression vs the last recorded run |

Compare counts against the pre-flight baseline captured in §2. A count that went **down** is a
deleted test, not a win — explain every decrease.

**13D-2** Power Grab performance assertions, measured in the domain and in Godot separately:
- zero per-tick heap allocation on the Power path (it is the Normal path times four floats — if a
  profiler shows an allocation, something boxed a `Vector2` or a nullable and must be fixed);
- `TryGrab`/`Release` allocate nothing beyond what Normal already allocates (the two limiters are
  built once in `Initialize`, per 11C-3);
- exactly one selection-change subscription after ten scene loads (no duplicate handler);
- no additional physics query per tick versus Normal — `TryPick` is untouched;
- after a 10 000-tick Power hold plus teardown, zero orphaned `RigidBody2D` in the tree.

**13D-3** Reset performance/lifetime assertion: after a confirmed reset, no service or presenter
still holds the pre-reset `BuddyProgressState`. Assert by mutating the new state's balance and
reading it back through `MoneyHudPresenter` and `EconomyService` — a stale binding shows up as a
value that does not move.

### 5.5 Packet 13E — documentation and external gates

Document what shipped, not what was planned. Each row names the specific edit, so this is a
checklist rather than a reading assignment:

| File | Edit |
|---|---|
| `docs/ARCHITECTURE.md` | `GrabTetherController` row (line ~87) gains the Power variant; note the nullable-profile mechanism and that no resolver type exists |
| `docs/PRODUCT_REQUIREMENTS.md` | FR-019.9 migration wording matches the shipped v5→v6; FR-019 stops describing a passive upgrade and describes a selectable tool |
| `docs/TEST_PLAN.md` | add the 11B/11E/12C/13A test inventories; update the schema-5 migration line |
| `docs/DECISIONS.md` | final `PowerGrabProfile` values with the note-1/note-2 derivations; final prices and curve from 12D; the throw-attribution default (11C-5); the Repair Kit visibility call (11D-2); the confirmation-modal deferral (13A-2b) |
| `docs/ROADMAP.md` | tick the M5 exit criteria |
| `docs/M5_SHOP_AND_TOOL_CATALOGUE_PLAN.md` | mark Tasks 11–13 complete; correct its `upgrade.strength` prose |
| `docs/UI_FLOATING_DOCK_PLAN.md` | Task 7 now binds to `TrayCommandComponent.ResetProgressRequested`; the localization keys already exist |
| `docs/OPEN_QUESTIONS.md` | close the Power Grab / economy / reset entries |
| `CHECKLIST.md` | update the "suggested next step" block and the baseline counts |
| localization catalogue | `shop.tool.power_grab.*` plus the reset-modal keys |

**13E-2** Owner/external gates — evidence to present, not questions to ask:

| Gate | Evidence |
|---|---|
| Power Grab feel | the 11E-4 side-by-side capture plus the row-3/row-7 numbers |
| Windows 10/11 | overlay, input, presentation, and the tray reset path (**not** the dialog — it does not exist yet) |
| Economy | the 12C report for `completionist_in_order`, all five seeds |
| Catalogue | sixteen interactions, twelve purchasable, final prices |
| Clean-room/art | already owned by the dock plan; unchanged by these packets |

**13E-3** Confirm no "Strength Upgrade" behaviour survives outside migration: `upgrade.strength`
appears only in `ContentIds`, `MigrateV5`, and migration tests (13B-5 already greps for this —
re-run it as the final check, since 12D and 13C both touch catalogue data after 13B ran).

**Task 13 is complete when:** 13A–13D are green, every 13E-1 row is edited, every 13E-2 gate is
recorded with its evidence, and the M5 exit criteria are ticked in the roadmap.

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
