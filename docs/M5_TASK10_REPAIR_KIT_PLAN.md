# M5 Task 10 — Repair Kit Plan

**Status: PLAN — written 2026-07-31, not yet implemented.** Refines the master plan's
Task 10 stub to handoff fidelity. Authoritative contracts: FR-008.6/.7/.8/.10,
FR-010.10, RAGDOLL §8.2/§9.2 Repair Kit rows **as amended below**, and the owner
decision of 2026-07-29 (DECISIONS ~line 891): **no cooldown and no appetite gate** —
"it is not food, so nothing rations it."

**Spec debt this slice must settle (grenade-fuse precedent):** RAGDOLL §8.2's care
table ("+20 per 120 seconds"), §9.2's tool row ("enforces a 120-second cooldown"), and
the older DECISIONS 2026-07-25 bullet still carry the superseded 120 s cooldown.
PRODUCT_REQUIREMENTS FR-008.6 is already amended. Amend the RAGDOLL rows at this
slice's bookkeeping task; the older DECISIONS bullet stays as history with the newer
entry superseding it, exactly how the fuse supersession was recorded.

**Dependency:** Task 7 (Burning) must land first — FR-010.10's "clears Burning" leg
and the scenario's burning-buddy proof need `BurningStatusModel.Clear()` to exist. If
Task 7 slips, everything else here can ship with the Burning leg explicitly deferred
and flagged, but the slice is not *done* until that leg runs.

---

## 1. What exists today (do not rediscover)

- **Identity minted (M5 Task 0):** `tool.repair_kit`, Kind = Care,
  `data/catalogue/tool_repair_kit.tres` (120 credits provisional, order 14,
  `Visible = false`).
- **Consume machinery is already generic:** `ObjectInteractionComponent` routes any
  `Consumable` profile through appetite → two-phase token → `ApplyCareMood` →
  `ConsumeSucceeded` event; the code comment names the Repair Kit as intended data.
  `WouldEat(0)` is always true, so `ConsumeHungerFill = 0` **is** the no-appetite-gate,
  and `ConsumeCooldownTicks = 0` **is** the no-cooldown (`CareConsumableModel.Complete`
  skips `StartCooldown` at 0). FR-008.10's fail-safety (cancel/drop applies nothing)
  is the existing token rule.
- **`PainKnockoutModel.ClearRollingPain()`** clears the rolling pain events **only**
  — the knockout end timestamp is separate state it never touches. FR-008.7 ("never
  shortens an active knockout") is already true of the primitive; **wire, don't
  re-implement** (master plan's words).
- **`BurningStatusModel.Clear()`** — ships in Task 7 precisely for this caller.
- **Launcher platform:** spawn key → content id → profile; keys `5/6/7` taken, `8/9`
  claimed by Task 8. The kit takes `0`.
- **Priority ladder facts that force this design (§2.1):** `Unconscious = 1` and
  `Hazard = 3` both outrank `ObjectAction = 5`, so a KO'd buddy *cannot* eat and a
  burning buddy *drops food and flees* — the two buddies that need a Repair Kit most
  are exactly the two that can never consume one.
- **Fire Drill achievement** (Burning cleared by Repair Kit) is M6 stats work — out
  of scope, don't build hooks for it.

## 2. Design

### 2.1 The application problem, and the resolved answer

FR-008.7 says the kit can be "used during an active knockout"; FR-010.10 says it
clears Burning. Neither state permits the buddy-driven consume path (§1, ladder
facts). So the spec *requires* a second application route, and this plan resolves it:

**Player contact-application.** A player-thrown Repair Kit — pullback-launched or
grab-flung — **applies on its first buddy-part contact**: `+20` mood
(`ApplyCareMood`), `ClearRollingPain()`, `BurningStatusModel.Clear()`, kit despawns
(registry slot freed). The knockout end time is untouched by construction. This is
the primary route and the one the tool's name describes: you patch the buddy up
*because* it's down.

**Buddy consumption stays too.** A conscious, calm buddy may still pick the kit up
and eat it through the untouched shared machinery (it is `Consumable` data — the
"full buddy still accepts one" check rides this path). Both routes converge on the
same effect application (§2.2), so they cannot drift.

Flagged prominently as §3 default 1 — it is the one place this plan goes beyond the
written spec, because the written spec is unsatisfiable without it.

### 2.2 One effects seam, two callers

`LooseObjectProfile` gains `[Export] ClearsHarmfulStatuses` (default false; Meal and
Drink stay false; validated only meaningful when `Consumable`). One root-level
handler applies a care item's full effect — mood gain, hunger fill, and (when
flagged) rolling-pain + Burning clear — invoked from:

1. `ConsumeSucceeded` (buddy ate it — existing event, existing mood/hunger already
   applied there; the handler adds only the flagged clears), and
2. the new contact-application (player-thrown), which applies mood **through the same
   one-success discipline**: `CareConsumableModel.TryBegin`/`Complete` executed
   atomically at contact with the kit's 0-cooldown tuning, so statistics, logging,
   and any future cooldown authoring flow through the same model as every other
   care item, and a double-contact on the same tick cannot double-apply.

A **missed throw applies nothing** (FR-008.10): the kit lands, becomes an ordinary
loose object, and waits — throw it again or let the buddy eat it. Eviction, despawn,
or a cancelled pullback likewise apply nothing.

### 2.3 The kit's own impact never hurts (§3 default 2)

A physical object thrown hard enough to help should not *also* score pain — a medkit
that bruises is a contradiction, and worse, its pain would enter `tool.repair_kit`
into **harmful memory** and teach the buddy to flee the thing that heals it. On
contact-application the kit's contact is consumed by the care path and excluded from
the impact pipeline (route-around, grenade-pin style, plus a scenario probe asserting
zero accepted impacts attributed to `tool.repair_kit`). While resting as a loose
object it is inert cargo like any ball — the buddy tossing it around scores through
the ordinary object rules unchanged (those attribute the *thrower's* interaction,
not the kit).

### 2.4 Authored data — `data/objects/repair_kit.tres`, spawn key `0`

| Field | Value | Why |
|---|---|---|
| `ContentId` | `tool.repair_kit` | |
| `Consumable` | true | buddy route stays open |
| `ConsumeMoodGain` | 20.0 | FR-008.6 |
| `ConsumeCooldownTicks` | 0 | owner 2026-07-29 |
| `ConsumeHungerFill` | 0.0 | not food — never refused |
| `ClearsHarmfulStatuses` | true | §2.2 |
| `Radius` / `Mass` | 10 / 1.2 | satchel heft |
| `LinearDamp` / `AngularDamp` | 1.6 / 2.6 | Meal-class, stays where thrown |
| Colors | white fill, red-cross-red outline | placeholder; art is M7 |

Launcher preset: Meal-like arc (empirical, measured at Task A). Presentation: the
standard loose-object seams, colors only; a brief green-cross sparkle on successful
application via the existing care-sparkle idiom (honoring `ReducedParticles` through
Task 7's `EffectsSettings` seam). No new mesh.

## 3. Owner gate — **ACCEPTED in full (owner, 2026-07-31)**, pre-implementation

All four defaults below are owner decisions — contact-application (default 1) is now
the approved FR-008.7/FR-010.10 mechanism, not a plan proposal. Record them in
`DECISIONS.md` at Task E's bookkeeping alongside the RAGDOLL cooldown amendments.

1. **Contact-application** (§2.1): a player-thrown kit applies on touching the buddy
   — this is the route that makes FR-008.7/FR-010.10 reachable at all. (Alternative:
   consume-only, and those two requirements go back to the owner as unsatisfiable.)
2. **The kit's own impact never scores pain or harmful memory** (§2.3).
3. **Applying to a healthy, calm buddy still works and still pays +20 mood** — there
   is no "nothing to repair" refusal. Simple, and consistent with no gating of any
   kind. (Alternative: refuse when nothing is wrong.)
4. **Buy-once, spawn-forever** like every other launchable (spawn key `0`).

## 4. Implementation tasks (in order, each gated)

**Task A — Data + effects seam.** §2.2 flag + handler on the `ConsumeSucceeded`
route, `.tres` + launcher preset + spawn key + lab grant. Measure the arc; record it.
*Accept:* profile-validation unit rows; `buddy_eats_a_repair_kit_for_twenty_mood`
(conscious calm buddy, mood +20, no cooldown started, `fun.treat` engaged);
`a_full_buddy_still_accepts_one` (hunger pinned full, no refusal);
`meal_and_drink_do_not_clear_statuses` (flag stays false — rolling pain survives a
Meal). Existing consume scenarios untouched.

**Task B — Contact-application.** §2.1 player-thrown route + §2.3 impact exclusion,
one-success discipline through `CareConsumableModel`.
*Accept:* `a_thrown_kit_applies_on_buddy_contact` (mood +20 exactly once,
kit despawned, registry slot freed), `a_missed_throw_applies_nothing_and_waits`
(FR-008.10 leg), `kit_contact_scores_zero_impacts_and_no_harmful_memory`,
`double_contact_cannot_double_apply` (token proof). Seeds 1/7/13.

**Task C — The healing itself.** Wire `ClearRollingPain` + `Burning.Clear` behind
the flag on both routes.
*Accept:* the master plan's floor — `burning_buddy_is_cured_and_cheered` (burning →
apply → Burning cleared immediately + 20 mood; FR-010.10),
`knockout_is_never_shortened` (KO'd buddy → thrown kit applies → rolling pain
cleared, KO end tick **unchanged**, buddy wakes on its original schedule; FR-008.7),
`clears_do_not_touch_money_stats_or_history` (payout ledger, pain statistics, and
existing harmful memories unmoved by an application). Seeds 1/7/13.

**Task D — Scenario + journey + registration.** `repair_kit` scenario bundling
A–C's checks per the master plan's accept list + `m5_repair_kit` journey (catalogue
leg per current visibility — grenade precedent — spawn by key `0`, deliberate miss
as the cancel path, real throw onto a hurt buddy, application observed, then the
burning-cure leg). Register in `ScenarioCatalog`, `TEST_PLAN.md`, quick suite
(+2 steps).
*Accept:* journey green seeds 1/7, both presentations.

**Task E — Feel gate + bookkeeping.** Owner plays it. Then: DECISIONS entry (the
four §3 defaults, the contact-application resolution recorded as the FR-008.7
mechanism), **RAGDOLL §8.2 + §9.2 cooldown-row amendments** (header debt),
TEST_PLAN §2's care-values line re-verified against the amended numbers,
`CHECKLIST.md`, full sweep recorded here. `Visible = true` only on the owner's word.

## 5. Validation commands

The standard three: build + domain suite, quick scenario suite, targeted runs
(`repair_kit`, `m5_repair_kit`, plus `meal_consume`, `consume_care_cooldown`,
`burning_status`, `knockout_window`, `object_toss_discard` as neighbours) across
seeds 1/7/13 and both presentation modes. Any baseline movement stated in the commit
message, never silently absorbed.
