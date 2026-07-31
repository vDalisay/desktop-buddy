# M5 Task 5 Refinement — Gun Feel, Nerf Blaster, and Real Pistol Plan

**Status:** Drafted 2026-07-31 from the owner's feel feedback on the engineering-complete
Task 5 cursor-gun platform, after a code audit of the shipped implementation
(`CursorAimModel`, `GunModel`, `CursorGunComponent`, `ProjectileBody`, `GunProfile`,
`LabPointerGrabComponent`). The audit found **identified root causes** for two of the three
reported defects (§3); they are stated as findings to verify first, not guesses.
**Owner SIGNED OFF 2026-07-31** — the plan and the §5 defaults are approved: proceed with the
`tool.nerf_blaster` catalogue split (item 1), near-zero nerf pain via tuning (item 2), the
cosmetic-only magazine (item 3), and the no-round-without-aim rule (item 5). The §4.1 aim
constants remain provisional until the Task F co-tuning session; the final feel gate at
Task H is still the owner's. Implementation may start at Task A.
**Owner intent (2026-07-31):** The current gun reads as a toy — embrace that: it becomes the
**Nerf Blaster**, the first gun the player can own. A **real Pistol** ships alongside it in
this same slice, sharing the whole platform and differing only in model, ammo, power, speed,
and presentation punctuation (screenshake, muzzle flash, dropped magazine). Both guns get
doubled, 3D-looking visuals and a floaty, relaxed aim.
**Baseline (updated through Task C, 2026-07-31, all green and must stay green):** build 0/0 ·
domain **979/979** · quick suite 26/26 · scenario `pistol_fire` (**17 checks**, seeds 1/7/13,
both presentations) · journey `m5_pistol` (10 assertions, seeds 1/7/13, both presentations).
The `979` figure is current; `971/836/648/429` in older logs are historical baselines — do not
check against them.

---

## 1. Owner specification (verbatim requirements, 2026-07-31)

1. The gun looks like a nerf gun. **This is fine** — it becomes the very first gun type the
   player can own, with a more violent gun next. The coming changes implement it *as* a nerf
   gun.
2. **All gun types:** the gun is too small — **at least double the size** — and it should
   **look 3D like the bat**. (Owner allows deferring to a later task if needed; this plan
   keeps it in scope because the Nerf/real split forces a visual pass anyway.)
3. The ammunition **does not line up with the direction the gun is facing**; it looks
   semi-random, and **the ammo rotates while flying forward**.
4. The gun **has trouble shooting to the left after shooting to the right** — it takes a few
   clicks before ammo comes out to the left. **Figure out why.**
5. Aiming is **choppy, as if locked to different axes**. The gun should be **floatier behind
   the cursor** — relaxed, easy to steer, following the direction the pointer has last been
   travelling. The owner wants to **co-tune this** ("let's refine this a bit more together").
6. The Nerf gun is **green with an orange tip**, like the water-gun emoji.
7. The **real gun ships now**, since it shares almost everything: standard-gun look, bullets
   that actively hurt the buddy, **very small screenshake** on firing, a **small blast flare**
   at the barrel mouth, a bit more realistic physics/handling overall, and **reloading drops a
   magazine on the floor**. When the gore implementation lands later, its hits should make the
   buddy **bleed where the bullets strike**.

## 2. What exists today (do not rediscover)

- `domain/DesktopBuddy.Domain/Tools/CursorAimModel.cs` — pure aim: forward = the latest
  per-tick pointer motion of length ≥ `MinimumMotion` (1 px), normalized; wheel pitches the
  aim up/down (`ApplyPitch` is side-aware); the next non-trivial motion clears the offset.
  `CursorAimState.Initial` has **no forward at all** until a non-trivial motion arrives.
- `domain/DesktopBuddy.Domain/Tools/GunModel.cs` — pure cadence/magazine/reload machine.
  One shot per **press edge** (the model stores the previous trigger state); a press inside
  `ShotIntervalTicks` is silently consumed; a press on empty dry-fires and starts the
  automatic reload.
- `src/Tools/CursorGunComponent.cs` — thin driver on the routed 120 Hz tick: feeds both
  models, launches pooled projectiles, draws the current gun as a **line + dot** at the
  cursor (that line is the whole "nerf gun" the owner is seeing). Key behaviors:
  drawing/holstering resets aim to `Initial`; **a fired round with `AimForward == zero` is
  spent without launching anything** (deliberate: the model owns the magazine); trigger
  latching exists in `SetTriggerHeld(true)`.
- `src/Tools/ProjectileBody.cs` — pooled `RigidBody2D` on the Projectiles layer. CCD is
  **deliberately disabled** (it destroys the measured impulse — see
  `GunProfile.MaximumTravelPerTickPx`, measured 2026-07-31: pain 85 disabled vs 0 with
  `CastRay`); tunneling is prevented **geometrically** by capping travel at 24 px/tick
  (`24 × 120 Hz = 2 880 px/s` hard muzzle-speed ceiling, enforced by profile validation).
  On contact the body stays live for `ContactSettleTicks` so the solver's real impulse
  resolves, then lingers `SpentLingerTicks` for attribution.
- `src/Tools/GunProfile.cs` + `data/tools/gun_pistol.tres` — one authored resource per gun
  (`ContentId = "tool.pistol"`, magazine 8, 0.25 s cadence, 1.2 s reload, muzzle 2 400 px/s,
  2.5 px round, mass 0.3). **Adding a gun is a `.tres` plus a content ID, not new input code.**
- `src/Laboratory/LabPointerGrabComponent.cs` — the only pointer path. `_cursor` tracks all
  mouse motion; `ResolvePendingInput` (physics tick) forwards
  `MoveCursor`/`SetTriggerHeld(IsPrimaryHeld)`/reload/wheel to the gun when it drives the
  selected tool. `NotifyPointerExitedPlayArea` (`NotificationWMMouseExit`) calls
  `GunTool.ClearCursor()`. `J` selects the Pistol. **The `_pendingPress` edge is *not*
  forwarded to the gun** — only the level `IsPrimaryHeld` is (see §3.2, H3).
- Visual precedent: `src/Presentation3D/BatMeshBuilder.cs` (lathed vertex-colored
  `SurfaceTool` mesh, every vertex inside the collider envelope),
  `CursorToolVisual3D`/`CursorToolVisualFactory` (profile-driven mesh/material, `PerPixel`
  shading, `VertexColorUseAsAlbedo`, roughness 0.7), `Body2DVisual3D` (`SetVisual` injection
  seam), `BuddyLookLightingRig` (key+fill, shadowless — **no new lights**),
  `WorldPlaneMapping` + `WorldCamera3D` (orthogonal, 2D→3D mapping < 0.5 px error).
- `src/App/CollisionLayers.cs` — layers 1-6; `MaskProjectiles = RoomBounds | BuddyParts |
  LooseObjects`; no projectile-projectile, no projectile-tool.
- Content spine: `ToolId` (append-only enum, next free value **14**),
  `ContentIds.ForTool`/`TryParseTool` (total mappings — extending the enum without both is a
  throw), `ToolCatalog.CategoryOf`, `CataloguePolicy`, `data/catalogue/*.tres`,
  `launch_catalogue.tres`. Tests that enumerate tools: `ContentIdsTests`,
  `ToolCatalogueTests`, `PurchaseTests`.
- Feedback precedent: `ImpactFeedbackPresenter` (ring/jolt/slow-time; the bat's whole-game
  freeze suppresses its slow-time), `ChargedSwing.ShakeOffset` (deterministic two-frequency
  presentation wobble — reuse this idiom for screenshake), and the DECISIONS entry
  "Hit-Lag Shake Gets Its Own Offset Lane".

**Sacred rule (DECISIONS.md):** pain comes only from the measured solver impulse through the
shared curve — **no per-gun damage multiplier anywhere**. "Nerf darts barely hurt" and
"bullets actively hurt" must both be achieved with authored mass/speed (muzzle speed is the
lever that moves pain; mass mostly decides shove), and both must be **measured** in the
laboratory, not asserted.

## 3. Defect diagnosis — verify, then fix

Run the verification steps before touching the fixes; if a finding does not reproduce,
record what actually happened in this section and stop for review rather than shipping a fix
for a bug that is not there.

### 3.1 "Ammo doesn't line up with the gun and rotates while flying" (§1.3)

**VERIFIED 2026-07-31 (Task A).** Both findings reproduced with measurements, in
`pistol_fire` on the real composition, before any fix was written. Finding A: the visible
bullet's body rotated **120.9°** during a flight the player can see, and the drawn streak
ended up pointing `-0.871` (about 150°) away from the direction of travel — the reported
"semi-random, rotating ammo", quantified. H1: one click after a pointer exit/re-entry took
the magazine from **6 rounds to 5 and launched nothing** (`spent_without_aim=1`) — the
reported "a few clicks before ammo comes out", exactly. H2/H3 were not separately
reproducible at 120 Hz and are addressed structurally (H2 by §4.1, H3 by routing the press
edge). Both fixes are in; the checks that caught them are permanent
(`the_bullet_visual_stays_glued_to_its_flight_path`,
`pointer_reentry_click_without_motion_spends_no_round`, `right_then_left_first_click_fires_left`).

**Finding A — stale body rotation is never cleared (identified root cause).**
`ProjectileBody.Launch` resets position, velocities, and interpolation, but **never resets
`Rotation`**, and `Configure` does not lock rotation. A projectile that grazes a body during
its `ContactSettleTicks` window picks up angular velocity while still visible (spin in
flight); when it is later re-pooled and relaunched, the leftover orientation persists. The
trail in `_Draw` is drawn **in local space** along `-_launchVelocity`, so any nonzero body
rotation rotates the drawn streak away from the true flight path — the shot *flies* correctly
but *renders* pointing somewhere else: "semi-random" and "rotating".
*Verify:* fire several shots that graze the buddy, then fire into open space; log
`Rotation` at launch (expect nonzero) and compare the drawn trail angle with
`LinearVelocity.Angle()`.
*Fix:* set `LockRotation = true` in `Configure` (a dart/bullet has no meaningful spin
gameplay); in `Launch`, set `Rotation = velocity.Angle()` and draw the projectile as an
axis-aligned streak along local +X from actual `LinearVelocity`, not the cached launch
velocity. Visual only — mass, impulse, and the pain path are untouched.

**Finding B — the aim itself is noisy (shared root cause with §3.3).** The shot direction is
whatever the last ≥ 1 px *per-tick* pointer delta happened to be — at click time that is
often a terminal micro-correction, not the direction the player perceives the gun pointing.
The §4.1 aim model fixes this; no separate work.

**Finding C — muzzle gap.** Shots are born 14 px ahead of the cursor and move 20 px/tick;
with the doubled visual the authored `MuzzleOffsetPx` must be re-derived from the **visible
muzzle tip** of the new mesh so rounds visibly leave the barrel (§4.5).

### 3.2 "Trouble shooting left after shooting right — takes a few clicks" (§1.4)

Three hypotheses, ordered by likelihood; instrument first (H1 telemetry is one counter).

**H1 — rounds are silently spent with no projectile (identified defect, very likely the
bug).** `CursorGunComponent.PhysicsTick`: when `shot.Fired` is true but `AimForward` is zero,
the round is spent and **nothing is launched, with zero feedback**. Aim is zero whenever the
runtime was reset — which happens on tool re-draw *and* whenever the cursor is lost and
reacquired. Sweeping the mouse from the right side of the play area to the left is exactly
the motion that brushes the window edge: `NotificationWMMouseExit` →
`NotifyPointerExitedPlayArea` → `ClearCursor` → `_hasCursor = false` → the runtime deactivates
→ on re-entry aim is `Initial` (zero) until a fresh ≥ 1 px/tick motion lands. Every click in
that state eats a round invisibly; the eighth eats the magazine and the next one dry-fires
into a 1.2 s auto-reload — "a few clicks before the ammo comes out" is this signature
exactly, and it is inherently direction-asymmetric (it depends on which edge the sweep
brushed).
*Verify:* add a `ShotsSpentWithoutAim` telemetry counter (component-level, beside
`DryFireCount`) and a lab readout; reproduce with fire-right → synthetic pointer exit/
re-enter → click-left. A scenario check pins the reproduction (§6 Task C).

**H2 — stale rightward aim survives a slow leftward move.** Aim only updates on ticks whose
pointer travel ≥ 1 px; at 120 Hz that is a **120 px/s** floor. A slow, deliberate leftward
aim never crosses it, so the gun keeps firing along the old rightward forward — rounds *do*
come out, but to the right of the cursor where the player is not looking. Fixed structurally
by §4.1 (the smoothed velocity accumulates sub-pixel motion, so slow travel still steers).

**H3 — swallowed press edges.** `ResolvePendingInput` forwards only the level
`IsPrimaryHeld`; a click whose press and release both land between two routed ticks never
reaches the gun (`_pendingPress` exists but is not routed). Rare at 120 Hz, but the latch API
(`SetTriggerHeld(true)`) exists precisely for this — route the edge. One-line hardening.

**Behavior change (record in DECISIONS.md):** a trigger press while the gun has no valid aim
must **not consume a round**. Gate the trigger input into `GunMachine` on `aim.IsValid`
(mask `trigger` to false when the aim is invalid). Consequence worth keeping: press-and-hold
before aiming fires the moment aim is established (the model sees a fresh edge). Dry-fire
and reload behavior on an empty magazine are unchanged. The current "the round is still
spent" comment describes a rule this plan deliberately retires — a round the player never saw
leave the gun is a bug report, not an ownership principle.

### 3.3 "Choppy aim, locked to different axes" (§1.5)

**Identified root cause.** Forward = `normalize(per-tick integer-ish pixel delta)`. Small
deltas like `(1,0)`, `(1,1)`, `(2,1)` quantize the aim to a handful of angles (0°, 45°,
26.6°…) — literally "locked to different axes" — and the aim teleports between them every
tick. No smoothing exists anywhere in the chain. Fixed by §4.1.

## 4. Design

### 4.1 Aim v2 — smoothed pursuit (pure domain, the heart of this slice)

Evolve `CursorAim` in place (same file, same wheel-pitch rules, same immutable-state house
style) rather than adding a parallel model — every cursor weapon (Fire Sprayer and Shotgun
later) inherits the fix through `CursorAimConstants`.

State grows to `(SmoothedVelocity, Forward, OffsetDegrees)`; the tick becomes:

1. **Smooth:** `SmoothedVelocity ← α·SmoothedVelocity + (1−α)·motion`, with
   `α = 2^(−1/SmoothingHalfLifeTicks)`. Sub-pixel and inter-tick jitter average out;
   slow travel accumulates instead of being discarded (kills §3.2 H2 and the quantization).
2. **Gate with hysteresis:** only while `|SmoothedVelocity| ≥ MinimumAimSpeed` does the
   target direction update. Below the gate the aim **holds** — it never flips on release
   jitter, and it never decays back toward anything.
3. **Slew:** `Forward` rotates toward `normalize(SmoothedVelocity)` by at most
   `MaxTurnDegreesPerTick` (shortest arc). This is the owner's "floaty, relaxed, follows
   where the pointer has last been going": the gun visibly *steers* instead of snapping.
4. **Wheel pitch unchanged:** offset accumulates while below the motion gate, is applied via
   the existing side-aware `ApplyPitch`, and clears on the tick the smoothed speed first
   rises **above** the gate (the "next non-trivial movement" of the spec, restated against
   the smoothed signal).
5. `IsValid` stays false until the first gated motion establishes a forward.

**LANDED 2026-07-31 (Task B), as designed.** `CursorAimState` is now
`(SmoothedVelocity, Forward, OffsetDegrees)`, the tick is smooth → gate → slew → pitch, and
`MinimumMotion` is gone from the constants and from `gun_pistol.tres` (nothing needed it for
resource compatibility). `CursorAimResult` gained `IsSteering` and `SmoothedSpeed`, surfaced
as `CursorGunComponent.AimIsSteering`/`AimSmoothedSpeed` and shown live in the laboratory
panel — the readouts Task F's co-tuning session needs. Two details the tests forced out:

- The wheel offset **accumulates whether or not the aim is steering** and is dropped only on
  the gate's rising edge. Refusing a notch scrolled mid-sweep would read as a broken wheel;
  the clearing rule is unchanged.
- `Slew` returns the aim **unchanged** when it is already on target, and `ApplyPitch` treats
  any `|forward.X|` under `1e-4` as vertical. Rebuilding a vector from its own angle is not
  the identity — `cos(-pi/2)` is `-4.4e-8` — so an aim held exactly vertical drifted a
  rounding error off it, gained a horizontal "side", and the next wheel notch pitched it by
  the full offset in a direction nothing chose. Caught by
  `AVerticalAimIsLeftAloneByTheWheel`.

New `CursorAimConstants` fields (authored per profile, all validated finite/positive):

| Field | Nerf default | Pistol default | Notes |
|---|---|---|---|
| `SmoothingHalfLifeTicks` | 10 | 14 | ≈ 83 ms / 117 ms; the pistol is deliberately heavier in the hand |
| `MinimumAimSpeed` | 0.35 px/tick | 0.35 | on the **smoothed** magnitude (≈ 42 px/s), far below the old 120 px/s floor |
| `MaxTurnDegreesPerTick` | 9° | 6° | 1 080°/s vs 720°/s — a full reversal costs ~20 vs ~30 ticks |

`MinimumMotion` (the old raw gate) is retired from the constants; keep the field with an
"unused, retained for `.tres` compatibility" note **only if** removing it breaks resource
loading — otherwise delete it and update both `.tres` files in the same change.

**These three numbers are the owner co-tuning surface** (§1.5 "refine this a bit more
together"): expose all three as live-tunable in the laboratory panel
(`LaboratoryControlComponent`) with the current aim angle and smoothed speed as readouts, so
the tuning session is turning dials, not rebuilding.

**Unit tests** (`CursorAimModelTests` grows; keep 971 green, add on top):
alternating `(1,0)/(1,1)` deltas converge to ≈ 22.5° (quantization dead);
sustained slow 0.5 px/tick leftward travel turns the aim left (old model provably never
does); a rightward aim plus 3 ticks of tiny reversal jitter does not flip; a sustained
reversal completes within `ceil(180/MaxTurnDegreesPerTick) + slack` ticks and every
intermediate forward stays unit-length; slew takes the shorter arc (test at ±170°);
half-life honored (after `SmoothingHalfLifeTicks` ticks of zero motion the magnitude halved);
gate hysteresis holds the forward through decay; wheel offset carries before first aim,
clears exactly on the gate's rising edge, clamps at `MaximumOffsetDegrees`; non-finite
inputs inert; determinism (same sequence twice, identical states).

### 4.2 Round-spending and trigger routing fixes

- Mask the trigger fed to `GunMachine` with `aim.IsValid` (§3.2 behavior change).
  `ShotCount` increments only on real launches from now on; scenario expectations updated.
- Add `ShotsSpentWithoutAim` telemetry (should be structurally zero after the fix; keep the
  counter so a regression shows up as telemetry, not as a mystery).
- Route the press edge: `ResolvePendingInput` calls `GunTool.SetTriggerHeld(true)` when
  `_pendingPress` was seen this tick even if `IsPrimaryHeld` is already false again, then the
  ordinary level call. The existing latch (`_triggerLatched`) does the rest (§3.2 H3).
- `PistolFireScenario` currently establishes aim with a single 8 px `MoveCursor` step; under
  v2 that seeds the EMA but the slewed forward needs a few ticks to converge. Add one shared
  scenario helper — `AimGunOver(gun, direction, ticks)` sweeping the cursor across ~12 ticks
  — and use it everywhere a scenario aims a gun. Do not sprinkle magic tick counts.

### 4.3 Projectile alignment (§3.1 fixes) — LANDED 2026-07-31, one deviation

Shipped as a **drawing-only** fix, and the plan's `LockRotation = true` was deliberately
**not** taken: it is not the visual-only change this section assumed. Measured A/B on
identical seeds, changing nothing else, the lock **halved the contact impulse the shared
pain pipeline scores** — seed 1 `1187.4 → 597.8` (pain `41.32 → 14.16`), seed 7
`1206.9 → 605.6` (pain `42.18 → 14.61`). A projectile's spin-up is part of the impulse this
project measures pain from, so locking it silently cuts every gun's damage in half, and
"pain comes only from the measured solver impulse" makes that a product change, not a
cleanup. What shipped instead:

- rotation stays **free**, with the measurement recorded in `ProjectileBody.Configure` so
  nobody re-locks it as tidying;
- `Rotation = 0` at launch, so a recycled pool slot starts every shot square;
- the streak is drawn from the velocity the body has **right now** (deflections included)
  and **undoes the body's rotation**, because a canvas item draws in local space. Any future
  projectile visual — dart, tracer mesh — must be oriented from velocity the same way.

Re-measured point-blank head shot, unchanged from the pre-fix baseline as required:
**seed 1 impulse `1187.4`, pain `41.32`, `49 587` milli-credits, Head**; seed 7 `1206.9` /
`42.18`. (Task B's aim then moved these to `1168`–`1208` across seeds 1/7/13 by aiming
accurately on all of them.) The A/B also explains the known 6–100 per-shot pain spread: a
bullet that hits square gets no spin channel and scores about half as much as a glancing one
— seed 13's journey head shot measures `574.9` where seeds 1 and 7 measure `1178`. Worth
having in hand for Task D and for M5 Task 12 calibration.

### 4.4 Catalogue split — Nerf Blaster and the real Pistol (owner decision, recommendation below)

**Recommendation:** keep `tool.pistol` as the **real** gun and add the starter as a new
tool. `ToolId.NerfBlaster = 14`, `ContentIds.ToolNerfBlaster = "tool.nerf_blaster"`. The
alternative (repurposing `tool.pistol` to mean the Nerf gun and minting a new ID for the
real one) contradicts the ID's plain meaning, the RAGDOLL §9.2 pistol cadence, and the
existing priced `tool_pistol.tres` — and content IDs are never silently repurposed
(ARCHITECTURE §5). The current *soft tuning* of `gun_pistol.tres` migrates to the new
`gun_nerf_blaster.tres`; `gun_pistol.tres` is re-authored as the real gun (§4.7).

Mechanical checklist for the new tool (each is a compile-time or test-enforced total
mapping — missing one throws):
`ToolId` append · `ContentIds` const + `ForTool` + `TryParseTool` ·
`ToolCatalog.CategoryOf` (**Damage**, by mechanism — its darts are authored to score nothing) · catalogue entry
`data/catalogue/tool_nerf_blaster.tres` (cheap starter price; `Visible = false` until the
owner's feel gate, same as the pistol today) · `launch_catalogue.tres` (which grows to sixteen entries, and every progression slot from
the Pistol onward shifts by one) ·
`CursorGunComponent.Profiles` gets the second profile in both scenes (`sandbox.tscn`,
`buddy_lab.tscn` — the component already validates duplicate content IDs and drives
selection per-tool with per-gun session magazines; **no component code changes for the
second gun**) · lab key `N` selects it (`H` is taken: it hides the laboratory panel; `J` stays Pistol) · dev catalogue unlock ·
`ContentIdsTests` / `ToolCatalogueTests` / `PurchaseTests` enumeration updates.

**Nerf darts and the pain floor:** the Nerf Blaster should barely matter physically — that
is its identity. Author dart mass/speed so a point-blank head shot scores **at or near zero
pain through the unmodified curve** (measured, Task D). If the owner wants strictly zero,
lower muzzle speed until measured zero — never touch the curve or add a multiplier.

### 4.5 Visuals — doubled size, 3D like the bat, both presentation modes

The gun has **no physical body** (nothing to `Attach` a `Body2DVisual3D` to), so add a
focused cursor-following presenter instead of forcing the slot abstraction:

- **`src/Presentation3D/GunMeshBuilder.cs`** — clean-room procedural meshes via
  `SurfaceTool`, vertex-colored, every dimension derived from new authored profile fields
  (§4.7), built like `BatMeshBuilder` (rings/boxes, `GenerateNormals`, capsule-envelope-style
  bounds helper for tests):
  - `NerfBlaster`: chunky toy silhouette — fat rectangular body + cylindrical barrel with a
    **wide orange tip ring**, simple grip; body `#3fa64b`-ish green, tip/accents orange
    (water-gun-emoji palette, authored as profile colors, not hard-coded). Deliberately
    rounded and oversized — the toy look is the point.
  - `RealPistol`: standard semi-auto silhouette — slide, frame, trigger guard, grip; dark
    gunmetal + near-black grip. **Generic**: no real-world model's trade dress.
- **`src/Presentation3D/CursorGunVisual3D.cs`** — a `Node3D` presenter owned by the roots
  beside `CursorToolVisual3D`: maps the gun's cursor through `WorldPlaneMapping.To3D`,
  yaw-rotates to the component's `AimForward` (already slewed by §4.1 — no second smoothing
  layer), and **mirrors vertically when the aim points left** so the grip stays down and the
  gun is never upside-down. Visible only while `CursorGuns.IsActive`. `PerPixel` material,
  existing lighting rig, no new lights.
- **Doubled size:** authored `VisualLengthPx = 56` (Nerf 64 — toys are chunky) versus the
  current 14 px line. `MuzzleOffsetPx` is **re-derived per profile to the mesh's actual
  muzzle-tip distance** so rounds are born at the visible barrel mouth (§3.1 Finding C) —
  validation asserts `MuzzleOffsetPx ≈ VisualLengthPx × MuzzleTipFraction` within 2 px so
  the two cannot drift apart.
- **Legacy 2D mode** keeps a `_Draw` fallback, upgraded from line+dot to a flat shaded
  silhouette (2-3 polygons + tip accent) at the same doubled dimensions — the two modes must
  agree on where the muzzle is, proven by a scenario check.
- **Projectile appearance per profile:** Nerf dart = fat orange capsule streak
  (radius 4 px); pistol bullet = thin 2 px yellow tracer. Both velocity-aligned (§4.3).

### 4.6 Real-pistol presentation punctuation

All presentation-only; none of it may touch the routed tick, physics bodies, or the pain
path. Each is authored per profile (Nerf authors zero/off for all three).

- **Fire screenshake:** `FireShakeAmplitudePx = 1.5`, `FireShakeDecayTicks = 8` — a
  deterministic two-frequency decaying offset (reuse the `ChargedSwing.ShakeOffset` idiom)
  applied to a new dedicated offset lane on `WorldCamera3D`'s position (and the 2D canvas
  transform in legacy mode). **Non-stacking: a shot during a live shake restarts the
  envelope's amplitude, never sums.** "Very small" is the spec: at 1.5 px it is felt, not
  seen. Keep the lane its own component (`CameraKickComponent`) so it cannot entangle with
  `ImpactFeedbackPresenter`'s slow-time or the bat's whole-game freeze (during the freeze,
  ticks don't advance, shots don't fire, so no interaction exists by construction — assert
  it anyway).
- **Muzzle flash:** additive unshaded quad-star at the muzzle tip for
  `MuzzleFlashTicks = 3`, scale-popping like the bat's glint (which is the implementation to
  crib); 2D-mode fallback draws a 3-ray star in `_Draw`. Fires only on real launches, never
  on dry fire.
- **Magazine drop on reload:** on `ReloadStarted` (real pistol only), spawn a small pooled
  cosmetic `RigidBody2D` (pool of 3, pre-allocated like the projectile pool) at the gun's
  grip point with a modest downward-backward ejection velocity and spin. **Collision:**
  `CollisionLayer = 0`, `CollisionMask = RoomBounds` only — it falls, bounces once, and lies
  on the floor, but *nothing can hit it and it can hit nothing but the floor*: it cannot
  touch the buddy, cannot enter the pain path, and is deliberately **not** a
  `LooseObjectRegistry` object (it must never consume one of the 24 slots — same rule as
  projectiles, RAGDOLL §10). Fades out and re-pools after `MagazineLingerTicks = 600`
  (5 s). Visual: tiny dark box mesh / 2D rect. **Flagged assumption (§5):** cosmetic-only;
  if the owner wants pickable/throwable magazines, that is a loose-object design with slot
  and attribution consequences — new decision required.
- **Handling weight:** the pistol's "more realistic handling" comes from data already in the
  plan: slower `MaxTurnDegreesPerTick` (§4.1), longer smoothing half-life, plus a small
  presentation-only recoil kick of the gun mesh along `-AimForward` for ~4 ticks after each
  shot (offset lane on the presenter, never on the aim itself — recoil must not degrade the
  next shot's accuracy; the aim model is the single source of truth for direction).

### 4.7 Authored data (both `.tres` files, all laboratory-tunable)

| Field | `gun_nerf_blaster.tres` (new) | `gun_pistol.tres` (re-authored) |
|---|---|---|
| `ContentId` | `tool.nerf_blaster` | `tool.pistol` |
| Magazine / cadence / reload | 6 / 30 ticks / 120 ticks | 8 / 30 ticks / 144 ticks (spec §9.2) |
| `MuzzleSpeed` | **1 100** (visible dart flight) | ~~2 760~~ **2 400** — 23 px/tick made close shots graze the rim; measured and reverted at Task D |
| `ProjectileRadius` / `Mass` | 4.0 / 0.02 (foam) | 2.0 / 0.3 |
| `ProjectileGravityScale` | 0.15 (darts droop a little — toy identity) | 0 (ballistically flat across the room) |
| Aim v2 constants | 10 / 0.35 / 9° | 14 / 0.35 / 6° (§4.1) |
| `VisualLengthPx` / `MuzzleTipFraction` | 64 / ~0.95 | 56 / ~0.95 |
| Colors | body green `#3fa64b`, tip+dart orange `#ff8c1a` | gunmetal `#3a3f4b`, tracer `#ffe08a` |
| `FireShakeAmplitudePx` / decay | 0 / — | 1.5 / 8 |
| `MuzzleFlashTicks` | 0 (off) | 3 |
| `DropsMagazineOnReload` | false | true |
| `Visual3DKind` | `NerfBlaster` | `RealPistol` |

Validation additions: `Visual3DKind` must be authored (no default silhouette);
shake/flash/magazine fields finite and non-negative; the muzzle-offset/mesh-tip agreement
rule (§4.5); everything existing (pool ≥ magazine, muzzle ceiling, spread rules) unchanged.
The dart's droop plus 1 100 px/s must be checked against `ProjectileLifetimeTicks`/
`ProjectileMaxTravelPx` so darts still cross the room before expiring.

### 4.8 Gore hook (explicitly future, zero implementation now)

Bullet hits already carry everything a bleed system needs: firing tool content ID
(attribution), the struck part, and the solver contact point through the accepted-impact
path. The only requirement on this slice: **do not discard the contact position** when
wiring the pistol — confirm `AcceptedImpact` (or the router's event) still exposes the
world-space hit point per impact, and note in code where the future gore consumer keys in.
If it is not exposed today, add the field now while the contract is already being touched —
widening `AcceptedImpact` is a deliberate contract change; update every construction site.

### 4.9 Deliberately unchanged

`GunMachine` cadence rules (except the aim-gated trigger input); the pooled-projectile
architecture, CCD-off decision, and the 24 px/tick ceiling; pain only from measured impulse
(no multipliers, no curve edits); projectiles never in the loose-object registry;
per-session magazine persistence; catalogue entries stay `Visible = false` until the owner's
feel gate; the glove/bat/care tools byte-for-behavior unchanged; `m5_baseball_bat`,
`homerun_bat_feel`, and every other green scenario stays green.

## 5. Owner gate — decisions and flagged assumptions

Confirmed by the 2026-07-31 feedback: nerf-first progression; green/orange nerf; doubled 3D
visuals; floaty aim (co-tuned); real pistol now with screenshake, flash, magazine drop.

Needs an owner answer before the affected task (each has a stated default so work can start):

1. **Catalogue split** (§4.4): recommendation is new `tool.nerf_blaster` + `tool.pistol`
   stays the real gun. *Default: recommendation.* Affects Task B naming only — the platform
   work is identical either way.
2. **Nerf pain**: exactly zero, or "near-zero is fine"? *Default: near-zero via tuning,
   measured.* (Strict zero is a muzzle-speed reduction, nothing else.)
3. **Magazine is cosmetic-only** (§4.6). *Default: cosmetic.* Pickable mags are a new
   loose-object decision.
4. **Aim constants** (§4.1) are the co-tuning surface; the defaults are starting points for
   the session with the owner, not accepted values. Schedule the session at Task F.
5. **Reading chosen where the spec was silent:** trigger presses with no established aim no
   longer consume rounds (§3.2). Flagged because it changes an (accidental) shipped
   behavior; it is also simply the bug fix.

Per the Planning Rule in `docs/DECISIONS.md`: anything beyond these defaults that turns out
to be product behavior requires a new owner decision rather than an implementation guess.

## 6. Implementation tasks (in order, each gated)

**Task A — DONE 2026-07-31.** Findings verified and recorded in §3/§4.3 (including the
`LockRotation` deviation). Landed: `ShotsSpentWithoutAim` telemetry plus a laboratory gun
readout; the aim-gated trigger (`ShotCount` now counts only shots that left the barrel); the
routed press edge (`CursorGunComponent.LatchTrigger`); the drawing-only projectile alignment;
and the shared `M4ObjectScenarioSupport.AimGunOver` aim helper, now used by every scenario
that aims a gun. Verified: build 0/0 · domain **971/971** · quick suite 26/26 · `pistol_fire`
(14 checks) seeds 1/7/13 and both presentations · `m5_pistol` seeds 1/7 both presentations ·
point-blank pain unchanged at `1187.4`/`41.32`.

**Task A (original text) — Diagnosis verification + projectile alignment.** Reproduce §3.1/§3.2 with the
telemetry counter and a throwaway lab session; record findings in §3. Land the §4.3
projectile fixes and the §4.2 trigger/round fixes (they are small and unblock everything).
*Accept:* `ShotsSpentWithoutAim` telemetry exists and reads 0 after the fix in the
right-then-left reproduction; drawn trail angle equals `LinearVelocity.Angle()` in a
scenario probe; re-measured point-blank pain unchanged and recorded; `pistol_fire` +
`m5_pistol` green (updated only where `ShotCount` semantics changed); domain 971 untouched.

**Task B — DONE 2026-07-31.** §4.1 landed in `CursorAimModel.cs` (see that section for the
two behaviours the tests forced out). `CursorAimModelTests` rewritten around the Pistol's own
constants: 24 tests covering quantization death, slow travel steering, release jitter,
bounded and shortest-arc slewing, the authored half-life, the wheel lifecycle, determinism,
and inert inputs. **Domain baseline moves 971 → 979.** Test-side consequence worth knowing:
under v2 a scenario or journey cannot aim a gun by teleporting its cursor. The jump to the
start of an approach is pointer travel of its own, and the aim turns at a bounded rate, so
every aim now goes through `M4ObjectScenarioSupport.AimGunOver` /
`JourneyRunner.AimAtPointAsync`, which jump, let the aim come to rest, then sweep long enough
to come round from any previous direction, standing off on whichever side of the target has
room behind it. A journey miss also taught the pipeline something worth recording: a
horizontal chest shot **grazes the hands** that hang beside the chest, reporting impulses of
`157`–`185` — under the curve's `350` floor — and the bullet spends itself on the graze, so
the buddy is hit and unhurt. Aimed shots go at the head, from close in.
*Verified:* build 0/0 · domain **979/979** · quick suite 26/26 · `pistol_fire` seeds 1/7/13 ·
`m5_pistol` seeds 1/7/13 · both presentations for each. Point-blank pain now `1168`–`1208`
impulse (pain `40.5`–`42.3`) across seeds 1/7/13, tighter than before because v2 aims
accurately on every seed — the old seed-13 outlier at `592.9` was a mis-aimed glancing hit,
not a property of the seed.

**Task B (original text) — Aim v2 domain model.** §4.1 in `CursorAimModel.cs` + full unit-test list. No
Godot references; allocation-free.
*Accept:* all new tests green (quantization, slow-travel, jitter-hold, reversal-slew,
hysteresis, wheel edge cases, determinism); 971 baseline untouched; `pistol_fire` green
using the new `AimGunOver` helper.

**Task C — DONE 2026-07-31.** `pistol_fire` was extended rather than split: the two
firing-side checks landed with Task A's fixes, and the aim-feel checks belong beside them —
one lab load, one place to read the whole reported defect. **17 checks now** (was 14). The
three new ones fire no shots, because all three are properties of the aim rather than of the
gun: `slow_leftward_travel_steers_the_aim_left` (0.49 px/tick, derived from the authored gate
and deliberately under the retired 1 px/tick floor, turns the aim right round in **82 ticks**),
`aim_never_flips_on_release_jitter` (a pixel of backward slop as the hand lets go, then 90
still ticks: worst alignment **0.999**, and the aim is below the steering gate at the end), and
`sustained_reversal_completes_within_expected_ticks` (**39 ticks** at 3 px/tick, pinned from
both sides — never under `ceil(180/MaxAimTurnDegreesPerTick)` = 30, so the aim cannot have
snapped, and never over that plus three smoothing half-lives = 72). The reversal pin is the one
Task F re-records after the co-tuning session.

Added beyond the accept list, and worth the ten minutes: **each new check was confirmed to
bite**, by mutating the model and watching exactly one of them fail. Smoothing removed
(`smoothed = motion`) → only the jitter check fails at `0.978`. Slew removed
(`forward = target`) → only the reversal check fails at 10 ticks. The retired raw gate restored
(`MinimumAimSpeedPxPerTick = 1.0`) → only the slow-travel check fails, with the aim never
turning at all. All three mutations were reverted; a pin that cannot fail is not a pin.
*Verified:* build 0/0 · domain **979/979** · quick suite 26/26 · `pistol_fire` seeds 1/7/13 ·
`m5_pistol` seeds 1/7/13 · both presentations for each, and the three aim measurements are
identical across every seed and both modes, as a deterministic pointer path should be.

**Task C (original text) — Left-shot regression scenario.** Extend `pistol_fire` (or add `gun_aim_feel`
and register it in `ScenarioCatalog` + `TEST_PLAN.md`): fire right → synthetic pointer
exit/re-enter → aim left → single click fires left.
*Accept:* `right_then_left_first_click_fires_left`,
`pointer_reentry_click_without_motion_spends_no_round`,
`slow_leftward_travel_steers_the_aim_left`, `aim_never_flips_on_release_jitter`.

**Task D — DONE 2026-07-31.** The split landed as recommended: `ToolId.NerfBlaster = 14` /
`tool.nerf_blaster` is the starter, `tool.pistol` keeps its plain meaning. It takes the
FR-013.2 launch catalogue to **sixteen entries** (fifteen interactions plus the upgrade) at
progression slot 7, ahead of the Pistol, which pushed every later slot along by one.
`gun_nerf_blaster.tres` is authored per §4.7; `gun_pistol.tres` is the real gun. New scenario
`nerf_versus_pistol` (6 checks) proves the split through the unmodified pain pipeline.

Measured, seeds 1/7/13 (both presentations on 1/7):

| | dart | bullet |
|---|---|---|
| point-blank head impulse | `20.2`–`22.4` | `574`–`603` |
| pain | **`0.00`**, milli `0` | `12.8`–`14.4`, milli > 0 |
| remembered as harmful | no | yes |
| level flight over ~36 ticks | droops `4.5 px` | `0.00 px` |

Separation is `26`–`28×` on measured impulse with no multiplier anywhere, which is the whole
claim. (The `574`–`603` here and the `1168`–`1208` in `pistol_fire` are the same gun: a square
hit gets no spin channel and scores about half of a glancing one — §4.3.)

**Two deviations from the plan, both forced and both measured:**

- **Muzzle speed stays `2400`, not the planned `2760`.** 2 760 px/s is 23 px per tick, and
  the geometric no-tunneling argument only guarantees that *some* sample of the flight overlaps
  the target — not that it overlaps it squarely. At 23 px/tick the close shot in `pistol_fire`
  landed on the head's rim on seed 13, spun `150°`, and delivered `198` impulse: under the
  curve's floor, so a visibly perfect head shot did nothing. A/B on that seed, changing one
  field at a time: `2760`/r2.5 fails, `2400`/r2.0 and `2400`/r2.5 both score `1168.6`. So the
  speed was the cause, the radius was not, and the pistol keeps the muzzle speed it shipped
  with while the bullet takes the planned thinner `2.0` radius. This is the measurement
  `GunProfile.MaximumTravelPerTickPx` asks for before anyone raises that number again: the
  `24 px` ceiling is where tunneling starts, not where reliable hits end.
- **Lab key `N`, not the planned `H`.** `H` already hides the laboratory panel and journeys
  press it.

*Verified:* build 0/0 · domain **981/981** (two enumeration cases) · quick suite 26/26 ·
`nerf_versus_pistol` seeds 1/7/13 · `pistol_fire` seeds 1/7/13 unchanged at `1168`–`1208` ·
`m5_pistol` seeds 1/7/13 · `boot_smoke` green with the sixteen-entry catalogue.

**Task D (original text) — Catalogue split + real-pistol tuning.** §4.4 mechanical checklist; author both
`.tres` per §4.7; measure and record: nerf point-blank head-shot pain (≈ 0), pistol
point-blank pain (comparable to today's 85-class measurement), pistol vs nerf measured
impulse separation.
*Accept:* enumeration tests updated and green; both guns selectable in the lab (`H`/`J`),
per-gun magazines persist across swaps (already component behavior — assert it for two guns:
`swapping_guns_preserves_each_magazine`); `nerf_dart_scores_no_meaningful_pain` and
`pistol_bullet_hurts_the_buddy` with recorded numbers; darts visibly droop, bullets fly flat.

**Task E — DONE 2026-07-31 (engineering; the look itself is the owner's Task H gate).**
`GunMeshBuilder` builds both silhouettes from vertex-coloured boxes — the Nerf Blaster chunky
with a wide orange tip ring, the Pistol a compact slide/frame/grip in gunmetal, neither
carrying any real model's trade dress — and `CursorGunVisual3D` follows the cursor and the
slewed aim with no second smoothing layer. The gun is **four times** the old 14 px barrel:
`VisualLengthPx` 64 (nerf) and 56 (pistol), with `MuzzleOffsetPx` re-derived to the drawn
barrel mouth (60.8 and 53.2) and profile validation refusing any pair that drifts more than
2 px apart. Legacy 2D draws the same silhouette flat, and both modes put the muzzle in the
same place — measured gap `0.00 px`. New scenario `gun_visuals` (5 checks), run in both modes.

Three things worth recording:

- **The gun is held, not centred.** The grip sits at the cursor and the barrel runs forward,
  so a round is now born 53–61 px ahead of the pointer instead of 14. Every aimed test had to
  learn that: `pistol_fire`, `nerf_versus_pistol`, and `JourneyRunner.AimAtBuddyAsync` now
  stand off by *target distance plus the barrel*, or the shot is born past the head it is
  aimed at. `ProjectileBody.LaunchPosition` was added so a check can ask the body where it was
  born instead of walking its current position backwards.
- **A left-facing gun is mirrored, not rotated.** Rotating a side-on gun past vertical stands
  it on its head; `gun_is_never_upside_down` pins the grip pointing down (screen +Y) on both
  sides. The mirror is a negative scale, so the material disables backface culling.
- **`nerf_versus_pistol` now fires a three-shot volley per gun.** One aimed shot was measuring
  the buddy's pose as much as the gun: it walks, it leans, and on a 17 px head the difference
  between a square hit and a rim graze is the difference between `1180` impulse and `0`. Three
  independent shots make the claims "a bullet that lands hurts" (best of three) and "not one
  dart does" (all three), which is the stronger reading of both. Recorded: dart `21.5`–`22.7`
  with `0.00` pain on 9 of 9 shots across seeds; bullet best `592`–`1180`, pain `13.9`–`41.0`,
  separation `27`–`52×`.

*Verified:* build 0/0 · domain 981/981 · quick suite 26/26 · `gun_visuals` both presentations ·
`nerf_versus_pistol` seeds 1/7/13 + legacy · `pistol_fire` seeds 1/7/13 · `m5_pistol` seeds
1/7 · `presentation_3d`, `presentation_look`, `m3_presentation` green.

**Task E (original text) — 3D visuals.** `GunMeshBuilder`, `CursorGunVisual3D`, legacy 2D fallback, doubled
sizes, muzzle-offset agreement, left-flip mirroring.
*Accept:* scenario checks in **both** presentation modes:
`gun_visual_faces_the_slewed_aim`, `gun_is_never_upside_down` (mirror at left-facing),
`rounds_are_born_at_the_visible_muzzle` (launch position vs mesh tip within tolerance),
mesh-bounds helper proves no vertex outside the authored envelope; existing 3D-presentation
regressions green.

**Task F — Feel pass + owner co-tuning session.** Lab panel dials/readouts for the three
aim constants; run the session; commit the accepted values into both `.tres` files and
record them here.
*Accept:* owner accepts the aim feel in the lab; accepted constants recorded in §4.1's
table; the `sustained_reversal_completes_within_expected_ticks` scenario pin (Task C, in
`pistol_fire`) re-measured against the accepted turn rate — its bounds derive from the
authored constants, so only the recorded tick count in §6 and `TEST_PLAN.md` moves.

**Task G — Real-pistol punctuation.** §4.6: `CameraKickComponent`, muzzle flash, magazine
drop pool.
*Accept:* `screenshake_decays_and_never_stacks` (rapid fire: amplitude bounded by one
envelope), `muzzle_flash_fires_only_on_real_launches` (dry fire: none),
`dropped_magazine_lands_and_cannot_touch_the_buddy` (mask proof + a contact probe),
`dropped_magazine_never_registers_as_a_loose_object` (registry count unchanged),
magazines re-pool after linger; nerf authors all three off and shows none.

**Task H — Promotion + bookkeeping.** Extend `m5_pistol` journey with the left-shot
reproduction and one nerf leg (or add `m5_nerf_blaster` if the journey grows past its
budget); update `quick_validate.bat`, `TEST_PLAN.md`, `CHECKLIST.md`; DECISIONS entries:
the no-round-without-aim rule, the catalogue split, the cosmetic magazine, the accepted aim
constants. Full validation sweep (build, domain, quick suite, scenario seeds, both
presentations) with numbers recorded here.
*Accept:* everything green; owner feel gate on the whole slice; catalogue visibility flips
remain a separate owner call.

## 7. Validation commands

The standard three (see `tools/quick_validate.bat` and the toolchain notes): .NET build +
domain test suite (baseline 971 + new), the quick scenario suite, and the targeted
scenario/journey runs (`pistol_fire`, the new/extended aim scenario, `m5_pistol`) across
seeds 1/7/13 and both presentation modes. Any baseline movement is stated in the commit
message with the new number, never silently absorbed.
