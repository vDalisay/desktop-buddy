# M5 Task 6 — Grenade Plan

**Status:** **COMPLETE and owner-accepted 2026-07-31.** Drafted from the owner's refinement
decisions (below), implemented through Task E, refined at Task G against the owner's
feel-gate feedback (3D grenade and pin, a fierier explosion, doubled knockback — §4), then
accepted at Task F with `tool_grenade.tres` going `Visible = true`.
Measured results are recorded in §2.2 and §6. This plan **supersedes the fuse rule** in the master plan's Task 6 and in
RAGDOLL §9.2 ("2.5-second fuse starts on launch"): the owner replaced it with a pin
mechanic and a 3-second post-release fuse. That is a spec amendment — record it in
`DECISIONS.md` and amend RAGDOLL §9.2 + the TEST_PLAN fuse assertion at this slice's
bookkeeping task (DECISIONS.md is being edited by the gun wrap-up agent right now; do not
touch it before those commits land).

**Prerequisite:** the gun-refinement working tree (Task G + owner follow-up + wrap-up)
is committed. This slice reuses two of its new components (`CameraKickComponent`,
the `MagazineBody` cosmetic-body idiom) and must not fork uncommitted files.
**Deviation, 2026-07-31:** that tree was *still uncommitted* when this slice was
implemented. Nothing was forked — `CameraKickComponent` is used in place, and `PinBody` is a
sibling of `MagazineBody` written against its stated rules rather than a copy of it — and
`DECISIONS.md` was left alone until the gun entries were already in the working tree, then
appended to rather than edited. The gun slice's own Task F/H remain its to finish.

**Owner decisions (2026-07-31, verbatim intent):**

1. Damage ≈ **5 pistol bullets**.
2. **Small explosion, medium screenshake**, placeholder sound on explosion **and on
   touching the ground**.
3. Should **feel strong**; controls like the Baseball — **LMB grab, RMB pullback with the
   same arc** — but **heavier in the hand**.
4. **On the initial RMB click a pin drops** from the grenade. With the pin out the player
   can **hold it with LMB without it exploding**. It **explodes 3 seconds after the player
   lets go** with the pin loose.
5. A buddy that **hasn't seen a grenade in a while is curious** and picks it up like a
   ball; **after being hit by a blast it remembers** and avoids/discards the next one.
6. **Simple 3D model.**

**Defaults chosen where the spec is silent (§5 — owner can veto each in one word):**
re-grabbing a live grenade does **not** stop the fuse; the "5 bullets" reference is a
**solid aimed shot** (so a point-blank blast KOs); grenades are **buy-once, spawn-forever**
like the Baseball.

---

## 1. What exists today (do not rediscover)

- **Identity already minted (M5 Task 0):** `ToolId.Grenade = 8`, `tool.grenade`,
  category **Damage**, catalogue entry `data/catalogue/tool_grenade.tres`
  (price 40 provisional, progression order 9, `Visible = false`). No enum or
  catalogue-enumeration work; no test-count movement from identity.
- **`src/Tools/PullbackLauncherComponent.cs`** — the shared Baseball launcher:
  `RequestSpawn(contentId, pos)` / `RequestBegin` / `RequestRelease` / `RequestCancel`,
  `PredictAimedWorldPosition`, `LastLaunchVelocity`; per-object empirical
  `PullbackLauncherProfile` presets (15 px/s → cap 1800). Baseball chord: LMB = Grab,
  RMB = pullback, release = launch. **The grenade is a second profile + spawn id on this
  component**, not a new launcher.
- **`src/Objects/LooseObjectRegistry.cs`** — cap 24, oldest-safe evict, protected flags
  from real state. Master plan: protected while the fuse is live.
- **Pain machinery** — `InteractionDamageComponent.ApplyAcceptedImpact` is the single
  scoring path: shared `PainCurve.PainFor(impulse)` → `PainKnockoutModel.RegisterPain`
  → `EconomyService.AcceptDamage` (payout + harmful memory + stats move together;
  ARCHITECTURE §11). It is **contact-driven**; a blast produces no solver contact, so the
  blast needs a sibling entry on the same component (§4.2) — *not* a parallel pipeline.
- **`src/Sandbox/CameraKickComponent.cs`** (gun Task G) — `Kick(amplitudePx, decayTicks)`,
  deterministic two-frequency wobble, non-stacking, drives both cameras. The grenade
  calls the same lane with bigger numbers; zero new camera code.
- **`src/Tools/MagazineBody.cs` idiom** (gun Task G) — pooled cosmetic `RigidBody2D`,
  layer 0, mask `RoomBounds` only, cannot touch the buddy or the pain path, never a
  loose object, fades and re-pools. The dropped **pin** is this idiom at pin size
  (generalize or sibling `PinBody` — decide at implementation, after Task G commits).
- **Audio precedent — `src/Tools/SwingAudioComponent.cs`**: clean-room synthesized PCM
  clips (22 050 Hz, no sampled files), one authored `AudioStreamPlayer`, per-cue counters
  as scenario oracles. The grenade's placeholder boom/thud follow this exact idiom.
- **Mesh precedent** — `GunMeshBuilder`/`BatMeshBuilder` (vertex-colored `SurfaceTool`,
  envelope-bounds helper); the grenade **has a physical body**, so the standard
  `Body2DVisual3D.SetVisual` attach seam applies (simpler than the guns, which had none).
- **Buddy behavior** — object approach/catch (M4, priority 5), fun/novelty meters
  (`fun.catch` fires on clean catches; novelty recovering over time *is* "hasn't seen it
  in a while"), harmful-history memory keyed by content id, priority-3 flee/discard.
  **No new behavior systems** — the grenade rides all four.
- **Measured bullet reference (gun plan §4.3/Task D):** a pistol bullet's head-shot pain
  is `12.8–14.4` on a square hit and `40.5–42.3` when the spin channel engages. "Five
  bullets" therefore spans ~68–205 total pain; §5 default anchors to the solid hit.

## 2. Design

### 2.1 Pin and fuse (pure domain — `Domain/Tools/GrenadeFuseModel.cs`)

A small immutable state machine in routed ticks, engine-free like `GunModel`:

`Pinned → PinPulled → Live(countdown) → Detonated`

- **Pinned:** inert forever. A grenade grab-flung with LMB only never explodes — it is
  a ball, including for the buddy. (RMB is the only way to pull the pin, and RMB's first
  press is also the pullback begin — so **every pullback-launched grenade is live**, and
  every inert one was thrown by plain grab. No separate arming input exists.)
- **PinPulled:** pin is out (one-way), grenade is player-held (grab or pullback) — safe
  indefinitely. Cancelling the pullback keeps it in this state while held.
- **Live:** entered the tick player control ends (launch release *or* grab release/drop)
  with the pin out. Counts `FuseTicks = 360` (3.0 s at 120 Hz), then detonates.
  **Nothing pauses or resets it**: not buddy catch, not player re-grab (§5 default 1),
  not the laboratory's routed-tick gate (it counts routed ticks, so the lab pause holds
  it by construction, like every other clock).
- **Detonated:** terminal; the body despawns (registry slot freed, "nothing left to
  hold").

Unit tests: full transition table, pin one-way, held-forever safety, 360-tick precision
from either release path, re-grab non-pause, determinism, non-finite inert.

### 2.2 Blast damage — through the shared curve, no new damage scale

The blast applies, to each buddy part within range, an **equivalent impulse** shaped
only by distance falloff, and feeds it through the *existing* chain
(`PainCurve.PainFor` → `RegisterPain` → `AcceptDamage`, attributed `tool.grenade`,
region multipliers and consciousness rules unchanged). The falloff curve is the only
authored blast quantity — the sacred rule ("pain comes only from impulse through the
shared curve, no per-tool multiplier") holds because the blast *is* an impulse source,
same as a collision; the curve still owns impulse→pain.

- New entry on `InteractionDamageComponent` (sibling of `ApplyAcceptedImpact`) taking
  `(contentId, part, equivalentImpulse, worldPoint)` with no `RawPartContact`; it reuses
  the zero-pain floor, knockout window, payout, harmful-memory, and `AcceptedImpact`
  event (world-space hit point included — the gore hook from gun plan §4.8 applies to
  blasts too).
- **Physical shove:** radial impulse with the same falloff to every dynamic body on
  `BuddyParts | LooseObjects` (a physics query on those layers — cosmetic layer-0 pins
  and magazines are excluded by construction). Objects get shove only; only the buddy
  feels pain.
- **Falloff (provisional, lab-measured at Task C):** full effect within `48 px` of
  center, linear to zero at `180 px`. No occlusion — the room is one open box; noted,
  not modelled.
- **Tuning target (§5 default 2):** point-blank total pain ≈ **5 × a solid aimed
  bullet ≈ 170–205** — which crosses the 100-pain knockout window, so **a point-blank
  grenade KOs the buddy**.

**MEASURED 2026-07-31 (Task C), `EquivalentImpulseAtCenter = 1150` unchanged from the
starting guess.** Seeds `1/7/13`, both presentation modes, in `grenade_fuse`:

| Blast point | Total pain | Parts scored | Knockout |
|---|---|---|---|
| At the head (point blank) | `186.21`–`190.65` | 6 of 6 | yes, every run |
| At the buddy's hand | `223.31`–`225.16` (hand alone `73.64`–`73.96`) | 6 of 6 | yes, every run |
| `155 px` away | `4.39` | 1 | no |

Against the gun plan's solid aimed bullet at `40.5`–`42.3` pain, the point-blank blast is
`4.6`–`4.7` bullets and the hand blast is `5.3`–`5.6`. Both cross the `100`-pain knockout
window, so a point-blank grenade knocks the buddy out, and a buddy holding one at detonation
takes the close-range result — which the `m5_grenade` journey then shows for real. The
falloff is the only authored quantity that moved any of this; no multiplier exists anywhere.

Two measurement notes worth keeping, because both cost a debugging pass:

- **A grenade placed inside a buddy part is ejected hard in a single solver step.** A test
  that places it once and then waits measures the blast wherever the ejection flung it —
  seed 1 read `4.16` pain over one part that way. `grenade_fuse` re-places it on each of the
  last four ticks instead.
- **A blast cannot trigger a knockout that is already running**, so a measurement taken on an
  unconscious buddy reads as a weak blast. Every blast measurement waits out the knockout
  window first, *after* clearing the room.

### 2.3 Controls and heft

- LMB grab and RMB pullback exactly as Baseball; **the first RMB press additionally
  pulls the pin** (spawns the cosmetic pin body, transitions the fuse model).
- **Heavier, same arc:** author grenade mass above the Baseball's (read the Baseball's
  authored value at implementation; start ~2×) and tune the grenade's own
  `PullbackLauncherProfile` so the flight arc *reads* like the Baseball's despite the
  mass. Grab carry sags more under the fixed grab strength automatically — that *is*
  the heavier feel. Both provisional until the Task F feel gate.

### 2.4 Presentation (all presentation-only; nothing touches the routed tick or pain path)

- **3D model:** `GrenadeMeshBuilder` following the established builder idiom — lathed
  olive-drab body (`#4a5d33`-class, authored not hard-coded), darker cap and lever,
  light ring detail; every vertex inside the envelope helper; attached via the standard
  `Body2DVisual3D` seam. **Legacy 2D** draws the same silhouette flat. Both modes in
  every scenario, per house rule.
- **Pin drop:** cosmetic pooled body at the grenade's position on pin-pull, small
  ejection + spin, `MagazineBody` collision/lifecycle rules verbatim.
- **Explosion (small):** additive flash (3 ticks, muzzle-flash idiom) + one expanding
  ring (~20 ticks, `ImpactFeedbackPresenter` ring idiom) sized to read the real
  full-effect radius; 2D fallback star + circle.
- **Screenshake (medium):** `CameraKickComponent.Kick(4.0 px, 14 ticks)` — the pistol's
  "very small" is 1.5/8; non-stacking by the component's construction. Authored on the
  grenade's data, provisional.
- **Placeholder audio:** a `GrenadeAudioComponent` on the `SwingAudioComponent` idiom,
  two synthesized cues — **boom** (low burst + noise decay, ~0.4 s) on detonation,
  **thud** on ground contact, gated by impact speed ≥ `250 px/s` and ≥ `12` ticks
  between thuds so a rolling grenade does not machine-gun. Per-cue counters as oracles;
  headless-safe like the bat's.

### 2.5 Buddy behavior — reaffirmed, zero new systems

Registered as a catchable loose object → existing approach/catch path with novelty
("hasn't seen one in a while" = the meters recovering; a mood-60 trust reset also
re-familiarizes it, RAGDOLL §4). `fun.catch` fires on a clean catch — including of a
live one, after which §2.1 applies. Blast pain lands in harmful history via the shared
economy path, so the next grenade triggers the existing flee/discard (priority 3).
Verify each with scenario checks; build nothing.

### 2.6 Budget and lifecycle

Registry-protected from pin-pull until detonation (live fuse must never be evicted);
slot freed at detonation. Inert grenades are ordinary oldest-safe-evictable objects.
Pins are cosmetic, never registered. Buy-once, spawn-forever like Baseball (§5
default 3); spawn key assigned at implementation from the free lab-key map (`5` is
Baseball; collision-check the current map in `LabPointerGrabComponent`).

### 2.7 Authored data (`data/tools/grenade.tres`-class resource, all lab-tunable, validated finite/positive)

| Field | Provisional value |
|---|---|
| `FuseTicks` | 360 |
| `EquivalentImpulseAtCenter` | tuned to the §2.2 target (start ~1150/part) |
| `BlastFullRadiusPx` / `BlastZeroRadiusPx` | 48 / 180 |
| `ShoveImpulseAtCenter` | tuned so close bodies visibly scatter, measured |
| Mass / body radius | ~2× Baseball / ball-class, from the Baseball's authored values |
| `KickAmplitudePx` / `KickDecayTicks` | 4.0 / 14 |
| `ThudMinImpactSpeed` / `ThudMinIntervalTicks` | 250 / 12 |
| Colors | body `#4a5d33`, cap/lever dark, pin ring light |

## 3. Owner gate — flagged defaults (veto any in one word)

1. **A thrown live grenade stays live.** Catching or re-grabbing it does not stop the
   3 seconds — it goes off in whoever's hand holds it. (Alternative: player re-grab
   pauses the fuse again. The default is simpler and reads "dangerous".)
2. **"5 pistol bullets" = 5 solid hits → a point-blank grenade knocks the buddy out.**
   A bullet's measured damage varies with hit quality (~13 on a flush hit, ~41 with
   spin); the default anchors to the strong reading, which fits "should feel strong".
   (Alternative: the gentle reading, ~68 total — hurts a lot, never KOs alone.)
3. **Buy once, throw forever** — no per-grenade cost, exactly like the Baseball.

## 4. Implementation tasks (in order, each gated)

**Task A — DONE 2026-07-31.** `domain/DesktopBuddy.Domain/Tools/GrenadeFuseModel.cs` ships
`GrenadeFuseStage`/`GrenadeFuseConstants`/`GrenadeFusePhase`/`GrenadeFuseInput`/
`GrenadeFuseResult`/`GrenadeFuseMachine` on the `GunMachine` idiom — engine-free,
allocation-free, immutable phase in and out. `GrenadeFuseModelTests` adds **12 tests**
covering the whole transition table, the one-way pin, a pin request on an airborne grenade
being refused, held-forever safety over twice the fuse, `360`-tick precision from both
release paths, a re-grab every third tick changing nothing, `Detonated` being terminal and
idempotent, an ill-formed fuse leaving the phase alone, and determinism.
**Domain baseline moves 987 → 999.**

**Task A (original text) — Fuse domain model.** §2.1 + full unit list. Engine-free, allocation-free.
*Accept:* all transition/timing/determinism tests green; existing domain baseline
untouched.

**Task B — DONE 2026-07-31.** `data/objects/grenade.tres` is the launchable
(radius `10`, mass `2.0` against the Baseball's `1.0`, damping unchanged so the flight arc
reads the same). **The heft needed no launcher-profile fork:** `PullbackLauncherComponent`
*assigns* the launch velocity rather than applying an impulse, so mass does not change the
arc at all — it only makes the grab carry sag and the impact land harder, which is exactly
the "heavier in the hand, same arc" the owner asked for. `GrenadeComponent` owns the fuse,
the pin, and the blast on the routed tick; it adopts a grenade from the launcher's spawn or
from the root's loose-object factory, so a grenade is a grenade however it reached the room.
The pin pull is routed as a queued intent from the same secondary press that begins the
pullback (`LabPointerGrabComponent`), and `PinBody` is a sibling of `MagazineBody` on its
exact collision rules. Lab spawn key is **`7`** (`5` Baseball, `6` Meal).

**Task B (original text) — Spawn, controls, pin.** Launcher profile + spawn id, RMB-press pin-pull
wired through the existing queued-input path, cosmetic pin body, registry protection,
heft authoring. *Accept:* scenario checks — `pin_in_grenade_never_explodes` (grab-fling,
wait 6 s), `pin_drops_on_first_rmb_press` (exactly one pin, cosmetic collision proven),
`held_live_grenade_never_explodes` (pin out, held 6 s),
`live_grenade_is_never_evicted`; Baseball scenarios untouched.

**Task C — DONE 2026-07-31.** `InteractionDamageComponent.ApplyBlastImpulse` is the sibling
entry: no `RawPartContact`, no router, and everything downstream identical — the curve's
zero-pain floor, the knockout window, the payout, harmful memory, and the `AcceptedImpact`
event with its world-space point for the future gore consumer. The radial shove is one
physics-shape query on `BuddyParts | LooseObjects`, so cosmetic pins and magazines are
excluded by construction rather than by a filter. Numbers recorded in §2.2.

**Task C (original text) — Blast.** §2.2 entry + radial shove + detonation despawn; measure and tune to
the §2.2 target; record numbers here. *Accept:*
`fuse_runs_360_ticks_from_either_release`, `re_grabbed_live_grenade_still_explodes`,
`close_blast_scores_about_five_solid_bullets` (recorded band, attributed
`tool.grenade`, KO observed), `blast_falloff_reduces_with_distance`,
`buddy_holding_at_detonation_takes_close_range_result`,
`blast_moves_objects_but_only_the_buddy_feels_pain`, registry count unchanged by
pin/blast, slot freed at detonation. Seeds 1/7/13.

**Task D — DONE 2026-07-31.** `GrenadeMeshBuilder` lathes the olive body with a darker cap
and lever and a pin ring that is simply absent from the pin-pulled mesh, every vertex inside
a stated `1.35 x radius` envelope; `GrenadeVisual3D` hangs it on the standard
`Body2DVisual3D` seam and owns the additive flash plus a torus ring that expands to the real
`48 px` full-effect radius; `GrenadeVisual2D` draws the same silhouette and the same blast
flat, taking over the body's own drawing while it is active. The kick is
`CameraKickComponent` at `4.0 px`/`14` ticks — zero new camera code. `GrenadeAudioComponent`
synthesizes two clean-room PCM cues on the `SwingAudioComponent` idiom. Measured: kick peak
`4.000 px` with four restarts inside one envelope, ring peak `45.60 px` in 3D and
`48.00 px` in legacy, boom count equal to detonation count, and no thud from a rolling
grenade.

**Task D (original text) — Presentation.** §2.4 complete: mesh + 2D fallback, explosion visuals, medium
kick, audio component, pin visual. *Accept:* both presentation modes —
`explosion_reads_at_the_blast_radius`, `kick_peaks_at_authored_medium_and_never_stacks`,
`boom_and_thud_cues_fire_with_counters` (thud gated: a rolling grenade stays quiet),
mesh envelope proof; existing presentation regressions green.

**Task E — DONE 2026-07-31.** `grenade_fuse` (13 checks, now 14 after Task G below) and the `m5_grenade` journey
(8 assertions) are registered in `ScenarioCatalog`, `TEST_PLAN.md`, and the quick suite
(now **28 steps**). §2.5 held as written — no new behavior systems. The journey shows a
buddy that has never met a grenade catching a pinned one like a ball, then leaving the next
one strictly alone for `600` ticks once the blast has taught it, at `200.91` pain and
`146067` milli-credits.

**One deviation from the plan's journey shape.** The "buy" leg cannot be a purchase yet:
`CataloguePolicy.EvaluatePurchase` refuses an entry with `Visible = false`, which the
Grenade is until Task F. The journey therefore asserts the refusal is `NotAvailable` — the
truthful current state, and a real check — and takes ownership from the development
laboratory catalogue, the same way every other unreleased M5 tool is granted. When the owner
flips the entry visible, that refusal becomes a real purchase.

**Task E (original text) — Buddy loop + journey.** §2.5 verified end-to-end; new `m5_grenade` journey
(buy → spawn → pin → throw → blast → next grenade fled/discarded), quick-suite and
`ScenarioCatalog`/`TEST_PLAN.md` registration. *Accept:* journey green seeds 1/7, both
presentations; `curious_buddy_catches_an_unfamiliar_grenade`,
`harmed_buddy_flees_or_discards_the_next_one`.

**Task F — bookkeeping DONE 2026-07-31; the owner's feel gate is outstanding.** Landed:
the `DECISIONS.md` entry "Grenade — Pin Mechanic, Post-Release Fuse, and Blast" (the fuse
supersession plus the three §3 defaults recorded as accepted-by-default); the RAGDOLL §9.2
tool table and §12 verification line amended off the `2.5`-second launch fuse; **FR-010.3
and the FR-010 tuning table amended too**, because leaving a requirement contradicting the
owner's amendment is worse than the extra edit — flagged here rather than done silently;
the TEST_PLAN fuse assertion replaced and the two new suites documented; and `CHECKLIST.md`
updated. `Visible = false` stands until the owner's word.

**Full validation sweep, 2026-07-31.** Build `0/0` (solution) · domain **999/999**
(was 987; +12 from `GrenadeFuseModelTests`) · quick suite **28/28** (was 26; the two new
steps are `grenade_fuse` and `m5_grenade`) · `grenade_fuse` seeds `1/7/13` in Mii3D and
seeds `1/7` in legacy, 13 checks each · `m5_grenade` seeds `1/7/13`, Mii3D and legacy ·
neighbours green on seeds `1/7`: `baseball_pullback` (also seed 13 and legacy),
`nerf_versus_pistol`, `gun_visuals`, `pistol_punctuation`, `object_toss_discard`,
`object_budget`, `presentation_3d`, `m3_presentation`, `boot_smoke`. No baseline moved
except the domain count stated above.

**Task G — owner feel-gate pass 1, DONE 2026-07-31.** The owner played it and sent back three
items. All three are landed; none of them touches the fuse machine, the pain path, or the
routed clock.

1. *"The grenade and pin is not in 3D yet."* The grenade **had** a 3D mesh; the dropped pin
   had none — `PinBody` drew flat canvas art in both presentation modes, which is exactly
   what "not in 3D" describes. That is now `GrenadePinVisual3D`: one mesh per pooled pin,
   built once, interpolated off the presenter's existing tick snapshot, faded on the pin's
   own linger, and taking the flat drawing over in `Mii3D` the same way the body slot does.
   The grenade itself was drawn to its `10 px` collider, which is a `20 px` lump in a `480 px`
   window — too small to carry a shape whatever geometry is under it. `VisualScale = 1.75` is
   now the drawn size against the collider radius, read by the mesh builder and by the flat
   fallback, so the two modes stay one grenade at one size; and the silhouette itself earns
   the pixels, with three moulded grooves down the body, a folded lever instead of a stuck-on
   tab, and a swept wire ring in place of the ring of beads. The stated envelope moves with
   it — still `1.35 x`, now of the **drawn** radius — so the check is against a stated bound
   rather than a discovered one.
2. *"The explosion looks a bit lame — more explosive and fiery."* Two layers became four:
   the white-hot core (`FireCoreColor`, `5` ticks), a fireball that swells on an ease-out to
   `1.15 x` the full-effect radius and cools flame → smoke over `18` ticks, `14` embers thrown
   to `2.1 x` that radius over `30` ticks, and the original shock ring, unchanged in meaning
   because it is the one layer that makes a claim about the physics. Both presentations draw
   all four. Ember directions and reaches are functions of the ember's index — the golden
   angle, so the fan does not read as a clock face — and never of a generator: presentation
   must not consume simulation randomness, and a replayed seed must produce the same
   explosion.
3. *"Double the knockback."* `ShoveImpulseAtCenter` `900 → 1800`. The shove and the pain
   impulse were authored as separate quantities for exactly this reason, so knockback moved
   and pain did not (point-blank `190.65`, unchanged).

**One test had to be replaced rather than re-baselined.** `blast_moves_objects_but_only_the_
buddy_feels_pain` measured how far a witness object had travelled `40` ticks after the blast.
That number went *down* when the shove doubled — `105.77 px → 25.09 px` — because the witness
was now reaching a wall and bouncing back inside the sample window, and because it had spent
the three-second fuse falling to the floor, so it was being shoved from wherever it rolled to.
It now sits at a known `35 px` from the centre on the detonation tick, inside the full-effect
radius where the falloff is `1`, and the check reads the speed it *leaves* at: `1750.8 px/s`
against `1800` impulse over `1.0` mass. How far it ends up is a story about which wall it met;
how hard it left is the authored number. A fourteenth check,
`the_dropped_pin_is_drawn_once_in_the_active_presentation`, pins the new pin handover in both
modes.

**Validation, 2026-07-31 (feel-gate pass 1).** Build `0/0` · domain **999/999** (unmoved —
the fuse model was not touched) · quick suite **28/28** · `grenade_fuse` green in `Mii3D` and
`legacy`, **14 checks** each (was 13).

**Task F — ACCEPTED 2026-07-31.** The owner played the post-feedback build on real Windows
and accepted the state it is in. `data/catalogue/tool_grenade.tres` is now `Visible = true`
and the Grenade is on sale at its authored `40` credits. Nothing else about the slice moved:
the three §3 defaults stand as recorded, and the provisional tuning stays provisional until
Task 12's economy calibration, like every other M5 tool's price.

The `m5_grenade` journey's first leg changes with it. It asserted the *refusal* — the only
truthful claim available while the entry was invisible — and now asserts the sale the slice
always owed: the entry is listed, appears in `CataloguePolicy.ShopEntries`, and a saveless
buyer holding exactly the price buys it, is unlocked, and is left with nothing
(`the_shop_sells_the_grenade_at_its_authored_price`). It is exercised against a fresh state
rather than the laboratory's, because the lab grants every implemented M5 tool at boot for
mechanical tuning and would answer `AlreadyOwned` to a real purchase.

**Milestone status:** M5 Task 6 (Grenade) is **complete and owner-accepted**.

**Task F (original text) — Feel gate + bookkeeping.** Owner plays it on real Windows. Then: DECISIONS
entries (fuse supersession, the three §3 defaults as accepted/vetoed), RAGDOLL §9.2 +
TEST_PLAN fuse-assertion amendments, `CHECKLIST.md`, full validation sweep recorded
here. `Visible = true` only on the owner's word.

## 5. Validation commands

The standard three (toolchain notes): build + domain suite, quick scenario suite, and
the targeted runs (`grenade_fuse`, `m5_grenade`, plus `baseball_pullback` and the gun
scenarios as neighbours) across seeds 1/7/13 and both presentation modes. Any baseline
movement stated in the commit message, never silently absorbed.
