# Desktop Buddy — Confirmed Decisions

Status: Living decision log for requirements and architecture planning.

This file records only decisions explicitly confirmed by the project owner. Unresolved details belong in the requirements process and must not be inferred by implementation agents.

## Milestone 4 Owner Gate — Accepted (2026-07-27)

The owner accepted Milestone 4 in full after the repaired **Hide to Tray** path was
rechecked through the documented Buddy Lab workflow. The window disappeared, and an
engineering process check confirmed that Godot remained alive and responsive in hidden
low-cost operation rather than exiting. The notification-area icon and user-accessible
restore command remain the already-confirmed Milestone 6 scope.

## Milestone 4 Personality and Fun System — Owner Approved (2026-07-29)

The owner explicitly approved the implemented personality/fun system. Each new buddy
samples independent Catch, Pet, Tickle, and Treat tastes once from the dedicated
save-creation RNG stream. Taste is represented by an interest drain in the inclusive
`1–20` range; each activity has its own `0–100` novelty meter, recharging at
`0.5` points per accepted running second. An activity that reaches zero remains bored
until it recovers to `25`, preventing a one-tick comeback. Taste, interest, and the
bored/not-bored hysteresis state persist with the buddy.

Care mood rewards remain unconditional when care succeeds. The fun verdict controls
the buddy's delight/laugh response, so repeating one activity can stop landing without
silently removing the care effect. These rules and the existing implemented tuning are
accepted rather than provisional.

## Owner Feel Pass and Shop Addition (2026-07-25)

Recorded from a live owner review session in the buddy laboratory.

**Grab resistance feel — three defects confirmed and fixed.**

1. **Resistance was too weak.** The owner judged a fearful buddy's opposition insufficient.
   `GrabResistanceForce` raised `11000` → `17000`; measured fearful tether extension went
   `15.8` → `30.1` against an unchanged calm baseline of `13.4`. This also closed a
   pre-existing red: the `grab_resistance` scenario's `fearful_resists_more_than_calm` check
   had been failing on `main` (margin `2.4`, required `>5`) and now passes at `16.7`. The
   assertion was **not** relaxed — the game was genuinely too weak, as the owner said.
2. **Resistance slid instead of walking.** `ActiveDriveComponent` applied the resistance force
   and returned early, resetting the gait, so the buddy was shoved sideways as one lump with
   dead feet. Resistance now falls through into locomotion and the buddy walks away while
   straining. The M4 `BehaviorArbiterModel` priority-4 branch was corrected to match, so the
   arbiter integration cannot reintroduce the slide.
3. **Hands were not panicky.** Added a deterministic panic-flail reach. First attempt was
   rejected by the owner as "spamming and moving seemingly randomly": it used a third
   harmonic and shortened its cycle with fear. Corrected to one slow sweeping arc per axis
   (cycle `26` → `132` ticks) with fear scaling reach only, never rate. The free hand now
   anchors its arc toward the escape direction because the buddy is pulling itself free, and
   a grabbed hand is left to the tether rather than fought by a spring.

**Elastic limb — new mechanic, owner-specified.** A grabbed limb stretches to `5` hand widths,
strains and vibrates for `3` seconds with the vibration escalating over the final `1` second,
then snaps back, releases the grab, and launches the buddy along the stretch direction with an
impulse scaling from the peak overpull. Easing back inside the limit cancels it. Documented as
FR-006.6–FR-006.11. Delegated engineering choices: the mechanic applies to every non-torso part
(the owner said "the arm"; head and feet were generalized for consistency and the owner was
told), the torso and loose objects are exempt, and the impulse is applied to the torso so the
limbs trail through the passive constraints.

**Strength Upgrade — new shop item, owner-requested.** The catalogue gains a purchasable item
that increases player strength: more control over the buddy, a larger stretch limit, a stronger
yank, and immunity from the buddy snapping its limb back to escape. Specified as FR-019 and
added to Milestone 5. It is the catalogue's first passive permanent upgrade rather than a
selectable tool, which is a new shop category. **Still open and not to be inferred:** the
product name, whether it has tiers, whether snap immunity is absolute or merely a longer strain
window, and every magnitude — all deferred to Milestone 5 calibration.

## Development Spike Observations (2026-07-12)

- The Milestone 1 standalone transparency/pointer spike launches successfully on Windows 11 using Godot 4.6.1 .NET, the Compatibility/OpenGL renderer, and an NVIDIA RTX 3070; startup produced no renderer or scene errors.
- Per-pixel transparency appearance and client-to-sandbox pointer accuracy at 100% and high-DPI scale still require the documented visible manual check. At 100%, verify transparency, opaque shapes, topmost behavior, and pointer readout at all corners and center. At 150%, repeat pointer checks and record DPI, offset, blur, and renderer artifacts; then record the keep/delete decision. They are intentionally not recorded as accepted from the automated hidden launch alone; the Milestone 2 renderer decision remains open until that visual matrix is performed. On 2026-07-12, the owner postponed the 150% DPI pass; this is a scheduling decision, not acceptance of the unverified renderer behavior, and renderer-dependent HUD work remains gated until the pass is resumed and recorded.
- **2026-07-13 — 150% DPI pass ACCEPTED.** The `spike_transparent_window.tscn` scene was launched standalone (GUI, `gl_compatibility`, no `--headless`/`--editor`) on Windows 11 / Godot 4.6.1 mono / RTX 3070 with the display at 150% scale; startup log confirmed the Compatibility/OpenGL renderer with no renderer or scene errors. The owner visually confirmed the 150% pass "looks good" (per-pixel transparency, opaque shapes, topmost, and corner pointer readout). **This closes the Milestone 2 Task 0 renderer decision gate: `gl_compatibility` is the accepted renderer for the desktop shell, and renderer-dependent HUD work is unblocked.** The throwaway spike scene may now be kept or deleted at will.
- The physics laboratory deliberately has two development-only composition roots, `BuddyLab` and `DualProfileLab`. Both mirror pointer → grab → buddy fixed-tick routing; shared routing is deferred to Milestone 2 when `SandboxRoot` gains its gameplay tick, as tracked by the state-audit watch item.

### M3.5 Task 1 — Renderer spike (2026-07-14, partial)

- **Transparent-safe 3D configuration pinned.** `spike_transparent_window.tscn` /
  `TransparentWindowSpike.cs` now runs an orthographic `Camera3D` (`Size = 360`,
  `KeepAspect = Height`, at `(240, −180, +500)` looking −Z, matching the Task 2 mapping
  `(x, y) → (x, −y, 0)`) plus unshaded `SphereMesh`/`CapsuleMesh`/`QuadMesh` primitives in
  the same viewport as the 2D pass. The pinned transparent-safe recipe is: **no
  `WorldEnvironment` and no `Camera3D.Environment`** (any sky/opaque clear paints over the
  desktop and kills the shell), `Viewport.TransparentBg = true`, all materials
  `StandardMaterial3D` `ShadingMode = Unshaded`. 3D nodes live directly under the `Node2D`
  root and share the viewport `World3D` — no `SubViewport`, no scene restructure.
- **Automated confirmations (Godot MCP launch, Windows 11 / Godot 4.6.1 mono /
  `gl_compatibility` / OpenGL 3.3 / NVIDIA RTX A2000 Laptop GPU).** Launch produced no
  renderer or scene errors from the spike. 3D primitives composite in the 2D viewport and
  land on the mapping-predicted screen pixels (sphere ≈ screen (180,180), capsule ≈
  (300,180)). **Color parity PASS on `gl_compatibility`:** a reference color drawn as a 2D
  rect and as an adjacent 3D unshaded quad render as one seamless block with no color seam,
  confirming the exit-gate A/B assumption that 2D and 3D print identical profile colors.
  `forward_plus` was not exercised (project renders `gl_compatibility`); no tonemap/linear
  shift to record.
- **Owner real-hardware matrix PASS (2026-07-14).** The owner ran the spike standalone on
  real Windows (Godot 4.6.1 mono, `gl_compatibility`) and confirmed every matrix item:
  desktop visible through empty alpha with the 3D content composited over it; `Msaa3D` at
  Off/2×/4×/8× (keys `0/2/4/8`, mirrored onto `Msaa2D`) with progressively smoother edges
  and transparency intact at every level; V-sync both states (key `S`); DPI 100–200%; and
  the 480×360 default plus the 700×520 resize (key `R`). **Task 1 is complete — the
  transparent-safe orthographic-3D pass over the desktop shell is proven on the target
  renderer, and M3.5 Task 2 is unblocked.** The spike stays development-only and
  export-excluded.

### M3.5 Variant C look direction — ACCEPTED (2026-07-15)

- The owner reviewed the A/B/C render from `docs/M3_5_LOOKDEV_SPIKE_PLAN.md` and
  explicitly selected **Variant C** as the production 3D direction. This accepts the
  soft matte toon response, three-quarter silhouette, generic procedural face direction,
  and restrained inverted-hull outline as the target; it does not authorize Nintendo
  assets, likenesses, UI, or trade dress.
- Production shading uses built-in `StandardMaterial3D` on `gl_compatibility`:
  Lambert diffuse, toon specular at `0.08`, roughness `1.0`, warm key/cool fill
  energies `0.75`/`0.70`, no `WorldEnvironment`, and shadows off. The accepted outline
  uses ink `#183042`, front-face culling, and grow amount `1.5` on the six part meshes;
  connectors remain unoutlined.
- Visual-profile colors remain authoritative **base albedos**, but lit pixels may be
  paler in highlights and deeper in shade. Exact per-pixel 2D/3D color parity is
  superseded by the accepted art-directed shaded result. The legacy/unshaded control
  remains useful only for comparison while the M3.5 gate is open.
- The facing target is a roughly **60-degree three-quarter read**, implemented as about
  `30°` yaw off dead-frontal. This supersedes the 2026-07-14 M3.6 full-profile (`90°`)
  walking direction. Dynamic facing remains M3.6 scope; M3.5 first moves painter depth
  lanes to camera-space after pose/yaw so they cannot create sideways displacement.
- The lookdev dot face is illustrative, not the final face implementation. M3.5 keeps
  the replaceable semantic face; M3.6 owns the composed procedural face and preserves
  `Reactions.CurrentFace` as its semantic contract.
- M3.5 Task 8 remains paused. The production materials/look task in
  `docs/M3_5_MATERIALS_AND_LOOK_PLAN.md` and its real-game owner gate must pass before
  the default can flip to `Mii3D`; `LegacyCircles` remains the default meanwhile.

### M3.5 production look — L6 owner gate ACCEPTED, default flipped to Mii3D (2026-07-18)

- The owner reviewed the production Variant C implementation in the real laboratory
  (interactive session: idle, grab, glove, knockout) plus a 30° dev-yaw posed preview,
  and **accepted the look** ("looks good"): materials/lights/outline verified
  value-identical to the accepted spike; hand overlaps and limb junctions judged fine.
- **`Mii3D` is now the shipping default** in both the laboratory and the sandbox
  (M3.5 Task 8). The owner's legacy-view disposition: `LegacyCircles` is RETAINED as a
  development/comparison view behind the `V` toggle and `--presentation=legacy`;
  deleting it needs a new owner decision.
- Known and accepted at flip time (deferred, not defects): dead-frontal tracking and
  the placeholder semantic `Label3D` face remain until M3.6 delivers dynamic
  three-quarter facing and the composed procedural face. The flat frontal lighting
  read is a consequence of the identity pose, not of the light rig.

### M3.6 expressive slice — SCHEDULED, owner decisions resolved (2026-07-18)

- M3.6 (`docs/M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`) is scheduled now that the M3.5
  gate closed. The four open owner decisions resolve as:
  1. **Motion amplitude: very subtle.** Performance offsets stay well inside the
     0.5 × part-radius cap; "alive but never busy" is the acceptance bar.
  2. **Face feature art direction: mockup gate before Task 5.** The owner picks from
     2–3 rendered face variants (spike-style minimal ink dots among them) in a
     development-only preview before the composed face is implemented. Until then the
     compositor architecture may proceed; feature art may not.
  3. **Blink cadence and ambient glance frequency: delegated engineering defaults**
     (seeded, order of blink ~2–6 s / glance ~4–10 s) recorded in
     `lab_buddy_expression.tres`; the owner judges at the M3.6 exit gate.
  4. **`LegacyCircles` view: retained** as a development/comparison view behind the
     `V` toggle and `--presentation=legacy` (decided at the Task 8 flip, above).
- **Ambient idle-glance defaults (delegated, resolved at Task 4, 2026-07-20).** Recorded
  in `lab_buddy_expression.tres`, inside the plan's validation bounds: glance cone
  `28°` yaw / `18°` pitch, acquisition ease `0.25 s`, virtual gaze depth `120 px`,
  cursor engagement range `220 px`, impact memory `240` ticks (`2 s`), a glance every
  `480–1200` ticks (`4–10 s`) held `72–168` ticks (`0.6–1.4 s`), and `4` pupil
  quantization steps. Look-at is suppressed while the semantic face is `">_<"`,
  `"x_x"`, or `">:("`. Blink cadence stays open until Task 5. The owner judges the
  cadence at the M3.6 exit gate.

### Face feature art direction — mockup gate RESOLVED (2026-07-20)

- The owner ran the face-art mockup preview (`scenes/spike_face_mockup.tscn`, three
  variants x four expressions in the accepted look, judged frontal and at ±30° yaw)
  and picked: **Variant B "Soft Oval" is the shipping default face** — filled vertical
  oval eyes with a white highlight, subtle arc brows, rounded mouths, ink-only palette
  (`OutlineColor` from `lab_buddy_look.tres` plus white highlights).
- **Variants A "Ink Dots" and C "Bean + Blush" are retained as future shop items**, not
  discarded: face style becomes a purchasable cosmetic when the shop exists (ROADMAP
  M5 economy scope; no shop work now). Consequence for Task 5: the `FaceCompositor` /
  `FaceExpressionMap` seam must keep the feature-art painter selectable per style
  rather than hard-coding B — the same parameterization the character-editor plan
  already requires — but only B's art is built and tuned in this slice.
- Task 5 feature art is now UNBLOCKED. The mockup spike stays in the tree as the
  style reference until Task 5 lands.

### M3.6 Task 5 — composed dynamic face shipped, Label3D glyph retired (2026-07-20)

- **Face mounting:** the composed face renders procedurally (CanvasItem draw into a
  200x200 offscreen `SubViewport`, re-rendered ON CHANGE ONLY) onto a 40x40-world-unit
  head-front quad parented to `HeadSocket` at surface + `FaceDepthEpsilon`. The plate
  inherits the socket transform fully — a real face rotates with the head — so the M3.5
  sideways-glyph counter-rotation is retired with the glyph. Face ink =
  `lab_buddy_look.tres` `OutlineColor` (one ink authority with the outline shells).
- **`Label3D` parity face RETIRED from composed scenes.** `BuddyVisualPresenter` builds
  the compositor plate when a `FaceCompositor` is wired and keeps the `Label3D` only as
  the fallback for uncomposed hosts (scenario-built bare presenters). The semantic face
  contract (`Reactions.CurrentFace`, ten strings, resolver priorities) is unchanged;
  `FaceExpressionMap`/`FaceExpressionCatalog` only translate it to feature poses.
- **Blink cadence (delegated engineering defaults, judged at the M3.6 exit gate):** a
  seeded blink every `240-720` ticks (`2-6 s`) held `14` ticks (~`0.12 s`), counted in
  ROUTED ticks on its own salted stream (`0xB11B_FACE_2026_0720`), suppressed —
  disarmed completely — while the face's eye pose is not blinkable (happy arcs, pain
  scrunch, knockout crosses, startle wide). Chew overlay: `42` ticks per open/close
  cycle during the eat activity, standing down under reaction-priority faces (the
  look-at suppression list). Recorded in `lab_buddy_expression.tres`.
- **Style seam:** the painter is selectable per `FaceStyleId` (the shop decision above);
  only `SoftOvalFacePainter` exists in this slice.

### M3.6 Task 6 — expressive layer composed, contracts recorded (2026-07-20)

- **Pose-mode arbitration (the rule the whole layer hangs off):** the buddy performs only
  while physics is not the story. `Performance` requires a calm, stable, conscious buddy;
  any of a live buddy-part grab, unconsciousness, an accepted impact inside the cooldown,
  or the learned-harm hand guard forces `Tracking`. Tracking is a CUT, not a fade — the
  performance weight goes to zero immediately, taking body yaw, head look-at, and every
  activity offset with it, so a hit always reads as the ragdoll's own pose. Returning to
  Performance blends back over the profile blend time. Committed SEMANTIC state (the
  facing side) survives a cut; only displayed values snap.
- **Offset cap:** every expressive contributor emits a bounded offset and the presenter
  clamps the combined result to `0.5 x part radius` before applying it. The cap is the
  guarantee that a performance can never misreport the physics pose; the shipping
  amplitudes sit well inside it (owner-chosen "very subtle" direction).
- **One clock:** all expressive timers count `BuddyRoot.RoutedTicks`, never engine frames,
  and the presenter honours `SetPresentationHeld`, so a paused laboratory shows a visually
  still buddy (the Task 4 pause regression is a permanent rule, not a one-off fix).
- **Presentation-mode parity is a test rule:** `Mii3D` and `LegacyCircles` must produce
  IDENTICAL scenario and journey verdicts; the full catalogue is rerun under
  `--presentation=` in both modes as part of the slice gate.
- **Laboratory expressive keys (development builds only):** `E` eat with a throwaway
  socketed item (press again to clear), `Q` wave, `Z`/`X` force the facing side left/right,
  `C` release the override back to autonomy. The facing override stands in for an engaged
  cursor and feeds the real arbitration — no bypass path exists around the model. These
  join `V` (presentation toggle) and the M1/M3 lab keys, and are guarded by
  `BuildInfo.IsDebugBuild`.

### Ambient cadence calmed — supersedes part of the M1 autonomy tuning (2026-07-20)

- Owner direction during the M3.6 Task 4 inspection: **"the buddy needs to be more chill
  with less actions"**; walking "darted around too much". The cause was ambient autonomy,
  not the M3.6 presentation layer. `lab_autonomous_motion.tres` (still
  `AcceptedM1LabAutonomousMotion`, lineage kept) is **amended** with owner approval:
  idle `60-120` → `240-600` ticks, walk `120-240` → `240-480`, jump interval `240-480` →
  `960-1800`, idle selection weight `2` → `6` (walk weights unchanged at `3`/`3`). The
  buddy now stands still most of the time, walks in longer deliberate stretches, and
  jumps every ~8-15 s instead of every 2-4 s. Everything else in the 2026-07-12 accepted
  M1 tuning (damping, grab authority, gait, recovery) is **untouched**.
- Verified green without re-baselining `lab_envelope_bounds.tres`: `autonomous_motion`,
  `repeat_envelope` (position spread `331.97` within the `400` bound), `idle_soak`
  (216,000 ticks, zero hard recoveries), plus the full M3.5/M3.6 presentation list and
  the quick suite. The owner still judges the new cadence hands-on at the M3.6 exit gate.
- **Ambient timer-driven jumping is OFF (2026-07-20).** The owner judged the random
  timer jumps "a bit too random" and disabled them **for now** — a reversible data
  switch, not a removal. `AutonomousMotionTuning.AmbientJumpsEnabled` (default `true` in
  code, `false` in `lab_autonomous_motion.tres`) gates only the ambient jump timer: when
  off it never requests a jump and draws nothing from the seeded stream. The jump
  actuation path is untouched and still reachable — tool-reaction hops (tickle flee) and
  Milestone 4's behaviour-driven jumps use the same `DriveIntent.JumpRequested`, as does
  the M3.6 jump-anticipation activity. The interval range stays valid data so re-enabling
  is a one-flag change. The `autonomous_motion` scenario keeps covering jump actuation
  through a scenario-local jumps-enabled profile and separately asserts the shipped datum
  is off.
- Presentation-side calming applied at the same time in `lab_buddy_expression.tres`
  (M3.6-owned, not M1): facing walk commit `36` → `90` ticks, idle side flips
  `720-1920` → `1440-3600`, ambient glances `480-1200` → `720-1800`, breathing
  `3.2 s`/`1.2 px` → `4.4 s`/`0.8 px`, walk bob `1.5` → `1.0`.

### Owner runtime fixes — threat face and Work Mode routing (2026-07-24)

- **Learned Boxing Glove threat face:** persistent harmful-tool memory remains
  persistent, but it no longer pins the visible `o_o` face merely because Boxing
  Glove stays selected. An on-screen learned-harm glove refreshes a five-second
  routed-tick face tail; after the pointer leaves the play area, the startle face
  expires after exactly `600` ticks and presentation returns to the ordinary
  reaction/mood face. Fear memory and future defense behavior remain intact.
- **Work Mode interaction surface:** the six live buddy-body bounds, rather than
  the whole sandbox rectangle, are the current Work Mode hit regions. A buddy
  press is observed by the shell before gameplay consumes it, enters Play Mode,
  and preserves that same press for the selected tool. Play Mode captures drag
  motion; `Escape` restores Work Mode; transparent pixels outside the moving
  buddy regions pass through to desktop applications.

### Milestone 4 pre-plan — owner decisions resolved (2026-07-24)

Answers to the decision list at the end of
`docs/M4_PERSONALITY_CARE_PERSISTENCE_PLAN.md`. All six decisions are resolved;
implementation of every M4 task is unblocked.

1. **Band-visible behavior vocabulary — ACCEPTED as proposed.** Fearful — keeps
   maximum cursor distance, flees approach, guards; wary — keeps moderate
   distance, never approaches, does not catch thrown objects; neutral — current
   ambient behavior; content — occasional cursor approach, catches willingly,
   occasional wave; delighted — eager approach, eager catch, frequent
   waves/glances. Exact distances and cadences are delegated engineering tuning,
   judged at the M4 owner exit gate.
2. **Jump personality — CONFIRMED.** Per-save propensity sampled uniformly in an
   engineering-chosen range, mapped to obstacle-hop eagerness only. Pure-timer
   ambient jumps remain OFF (2026-07-20 decision stands).
3. **Approach target — BOTH cursor and objects.** Architectural resolution of
   relative priority: a committed object action is arbiter priority 5 and social
   cursor approach is priority 6, so an engaged object naturally outranks cursor
   approach; when no object action is committed, the social layer may approach
   either per band vocabulary. No extra product rule needed.
4. **M4 consumable scope — CONFIRMED.** Consume/cooldown machinery ships against
   the laboratory food item; Meal/Drink/Repair Kit arrive as M5 catalogue entries
   on that machinery. The M4 journey-map "meal consumption" row is satisfied by
   the M4 food item and re-verified in M5 with the real Meal.
5. **Provisional base passive rate — DELEGATED to engineering (2026-07-24).**
   The agent picks a sensible placeholder (order of ~1 credit/minute at neutral
   mood), ships it clearly marked provisional in the `MoodEconomyProfile`
   resource, and it is replaced during M5 calibration against the FR-012.3
   peak-passive ≈ 25%-of-active target. The owner is not asked again until the
   M5 calibration gate.
6. **Laboratory save policy — CONFIRMED.** Laboratories and scenario runs stay
   saveless (in-memory store); only the sandbox/standalone boot touches
   `user://`.

## Accepted Milestone 1 Feel Tuning (2026-07-12)

- **Assisted stand-up duration accepted (2026-07-24).** The owner hands-on checked
  the current assisted recovery after the fixed two-second unable-to-stand delay
  and accepted its duration as feeling right. The deterministic
  `standing_recovery` scenario measures `228` routed ticks (about `1.9 s`) from
  already-active assistance to stable standing; its regression ceiling is now
  `240` ticks (`2.0 s`). This records acceptance of the existing forces and
  motion—no recovery tuning changed.
- The owner performed the `TEST_PLAN.md` §8 side-by-side feel review of the tuning produced by `docs/M1_FEEL_AND_GAIT_PLAN.md` and **accepted it** ("this feels way better, I approve"). This satisfies the ROADMAP Milestone 1 exit criterion "lock an initial accepted tuning Resource." The accepted profiles are `data/buddy/lab_puppet_rig.tres`, `lab_grab_tether.tres`, `lab_active_drive.tres`, `lab_conscious_drive.tres`, `lab_unconscious_drive.tres`, `lab_autonomous_motion.tres`, and `lab_boundary.tres`, renamed from `Provisional*` to `AcceptedM1*` to mark the lock. Changing them now requires a new owner feel review.
- Accepted feel direction, established against the v1.01 reference: low body damping (responsive falls), grab strong enough to lift the whole buddy clear of the floor and whip it once airborne, a phase-driven **stepping** gait (feet visibly alternate and clear the floor) implemented with per-foot target forces on the existing six circles — **no inverse-kinematics chains and no added rigid bodies** — and prompt (~2 s ramp) assisted recovery. The six-circle constraint and the ban on skeletal ragdoll/joint motors are unchanged.
- `lab_envelope_bounds.tres` remains `Provisional`: it holds statistical regression tolerances, re-measured as later behaviors are tuned, not a feel-accepted profile.

## Milestone 3 Tool Feel and Reactions (2026-07-14)

- **World-upright face:** the head remains a freely rotating authoritative `RigidBody2D`, but its emoticon is counter-rotated so the face remains upright in world/screen space.
- **Boxing Glove response:** the physical cursor tether is retuned to lag materially less. Faster real swings produce larger measured impulses and therefore proportionally greater pain/payout through the shared pain curve; there is no second speed or glove damage multiplier.
- **Maximum-hit feedback:** an accepted Boxing Glove hit that reaches `100` pain or triggers knockout starts a non-stacking hit-stop envelope at `0.15x`, easing continuously back to the pre-impact simulation speed over `0.12` real-time seconds. The continuous curve must keep the early portion visibly slow rather than returning most of the speed immediately. The impact ring/flash is centered on the solver-reported world contact point. It also uses original non-graphic glove squash/recoil, canvas-only jolt, sound, and face feedback; the OS window never moves.
- **Pet satisfaction:** Pet progress is hidden and fills from cursor distance travelled while held over buddy bodies, not held time alone. A reward requires both a full distance bar and the confirmed three valid-contact seconds since the previous Pet reward; completion grants `+1` mood and resets the bar. While rubbing, the face may show `:3`; completion shows `:)` for `0.75` seconds. Every transition into Pet selects one of the six body parts as the favorite spot for that selection, and distance over that part contributes `1.2x` progress. While the Pet hand is actively rubbing that favorite part, small original sparkle particles appear around the hand; they reveal the favorite only through moment-to-moment feedback and do not expose a meter or persistent marker.
- **Tickle tolerance:** Tickle is friendly for the first `6` cumulative valid-contact seconds and can grant the normal `+1` mood at `3` and `6` seconds. It may request one playful hop away from the cursor at most every `1.5` seconds. After `6` seconds it becomes Angry: positive Tickle rewards stop, every further `3` valid-contact seconds applies `-1` mood, the face shows `>:(`, and the buddy flees with hops no more often than every `0.75` seconds. `8` seconds without valid Tickle contact resets tolerance and anger even if Tickle remains selected.
- **Care cursors:** Pet and Tickle show original animated hand actors beneath the still-visible OS cursor only while primary input is held; these presentation actors follow the cursor without physical lag.
- **Glove defense:** after the Boxing Glove has caused pain and is learned as harmful, a conscious buddy may physically brace its two real hand bodies between the approaching glove and its head/torso while also moving away. The buddy flees away from the pointer while the guard direction follows the pointer with a bounded lag; guard targets remain body-relative and never attach to or chase the physical glove body. Guard actuation must not inject net force that pulls the puppet toward the pointer. A glove contact with an actively guarding hand applies a documented `0.5x` guard-absorption factor to accepted impulse, pain, payout, and mood harm, retains the limb region multiplier, and uses bounded bracing forces to target about half the unguarded buddy displacement. A strike that gets around the hands uses the normal unmodified pipeline.
- **Faster self-righting:** keep the confirmed `2`-second unable-to-stand delay and `10`-second post-assistance hard-recovery threshold, but halve the assistance ramp from `2` seconds to `1` second and retune bounded recovery forces so observed assisted stand-up time is approximately half the pre-change baseline. This is an owner-authorized change to the accepted M1 feel profile and requires a fresh feel check.
- **Deferred jump personality:** the next autonomy/persistence slice will sample a buddy-specific ambient jump propensity when a new save is created and persist it for that save. It is regenerated only for a new game. This slice records the requirement but does not implement persistence or the broader obstacle-aware M4 behavior arbiter.

## Product and Platform

- **Engine and language:** Godot 4.6.1 .NET with C#.
- **Repository baseline:** Rebuild from the minimal current checkout. Existing `main`, `chat`, `codex`, and `threejs` branches are non-authoritative reference material only.
- **Launch platform:** Windows 10/11 x86_64 is the only required platform for the first Steam release.
- **Window:** The game runs in a movable and resizable transparent sandbox window, initially anchored to the lower-right of the usable desktop.
- **First implementation milestone:** A physics laboratory must prove the complete buddy behavior before economy or shop implementation begins.
- **Current session scope:** One buddy and one save slot. Profiles, multiplayer, and multiple simultaneous buddies are out of scope.

## Reference and Physics Direction

- **Primary mechanical reference:** The original Newgrounds `Interactive Buddy` v1.01 behavior.
- **Secondary comparison:** Archived v1.02 footage/build behavior may be used to compare feel where it does not conflict with v1.01.
- **Physics model:** A faithful two-dimensional, six-body, spring-driven active puppet rather than a conventional multi-bone hinged ragdoll.
- **Physics authority:** Godot `RigidBody2D` simulation is authoritative for the buddy, room objects, and physical tools.
- **Puppet constraints:** Custom equal-and-opposite spring/damper forces, maximum-stretch correction, upright torque, and locomotion impulses replace `PinJoint2D` motors and replace any custom whole-world solver.
- **Self-collision:** Buddy body parts do not collide with one another; they collide with room boundaries, tools, projectiles, and loose objects.
- **Reference boundary:** Desktop Buddy is a clean-room spiritual successor. It must not copy original art, audio, dialogue, skins, branding, or other expressive content.

## Presentation, Accessibility, and Performance

- **Art style:** Crisp anti-aliased vector/shape art with flat colors, dark outlines, simple circular forms, restrained shading, and an original modernized Flash-era interface.
- **Excluded presentation:** No pixel-art treatment, copied reference assets, realistic wounds, or current-scope blood effects.
- **Settings:** Master volume, SFX volume, mute while in Work Mode, reduced motion, screen-shake toggle, reduced particles, photosensitivity-safe effects, UI/world zoom, anti-aliasing, V-sync, and a remappable global mode hotkey.
- **Default presentation settings:** V-sync On, `2x` MSAA, Master volume `50%`, SFX volume `50%`, Mute in Work Mode On, Screen Shake On, Reduced Motion Off, Reduced Particles Off, and Photosensitivity-Safe Effects On.
- **Graphics choices:** V-sync exposes On/Off; anti-aliasing exposes Off/`2x`/`4x`/`8x` MSAA.
- **Shake boundary:** Screen shake moves only rendered game content and never the operating-system window.
- **Launch inputs:** Mouse and keyboard only.
- **Localization:** The first release ships English only. All player-facing text is externalized behind stable translation keys from the first implementation; no player-facing string literals live in code or scenes, and typed content definitions carry translation keys rather than display literals so additional languages can be added without code changes.
- **Physics frequency:** Active simulation runs at a fixed `120 Hz` and never dynamically lowers its physics tick rate.
- **Rendering:** Physics interpolation is enabled; foreground play targets at least `60` rendered FPS with user-configurable V-sync.
- **Reference performance budget:** At `480x360` with `24` loose objects, target less than `5%` CPU and `300 MB` RAM on an Intel i5-8400/UHD 630-class PC.
- **Hidden performance budget:** Hidden tray operation targets less than `0.5%` CPU.

## Buddy Identity and Agency

- **Visual identity:** An original minimalist robot/mannequin using the readable six-circle silhouette.
- **Face:** Simple emoticons such as `:)` and `:(` appear directly on the head circle.
- **Mortality:** The buddy is immortal. It may be hurt or knocked unconscious, but it cannot die or be dismembered.
- **Violence scope:** The current release uses non-graphic slapstick feedback. An optional bleeding system may be considered later and is explicitly out of scope now.
- **Object behavior:** The buddy can catch and inspect safe objects, consume food and drinks, and toss objects according to mood. It drops or flees hazardous objects and does not directly attack the player.
- **Autonomy:** The player does not directly control locomotion. The buddy autonomously idles, approaches, flees, walks, jumps, catches, holds, consumes, and tosses objects.
- **Experience memory:** Per-tool experience and learned hazard recognition persist across save files. Positive care can restore trust after harmful treatment.
- **Trust reset:** Whenever mood crosses upward from below `60` to `60` or higher, all harmful-history and per-tool fear records are cleared. The rule may trigger again after mood falls below `60` and subsequently recovers.
- **Grab resistance:** A fearful buddy actively resists being grabbed by moving away and opposing the player's pull.
- **Grab mechanism:** Any buddy body part or loose object may be grabbed through a damped elastic tether. Resistance stretches the tether but does not break it; releasing preserves a capped throw velocity.
- **Self-righting:** After `2` seconds unable to stand, assisted self-righting begins and ramps for up to `5` seconds.
- **Fail-safe recovery:** Hard repositioning is permitted only after `10` seconds of failed self-righting or immediately when physics state is invalid or outside the sandbox.
- **Fail-safe cleanup:** Hard recovery releases the active grab and any held object, clears unstable velocities, rolling pain, knockout, Burning, and other temporary statuses, and preserves money, unlocks, persistent mood, harmful history, and lifetime statistics.

## Overlay and Interface

- **Sandbox presentation:** The transparent play area has simple, clearly visible borders so it reads as a box.
- **Desktop passthrough:** Transparent pixels pass pointer input to applications behind the game.
- **Control recovery:** A global hotkey and system-tray command restore game interaction when passthrough or focus behavior prevents normal access.
- **Default global hotkey:** `Ctrl+Shift+B`, with user remapping supported.
- **HUD:** The money total remains visible in a compact overlay HUD.
- **Menus:** Tools, shop, and settings use a retractable in-window panel.
- **Mood display:** The game does not expose a permanent mood meter. Mood is communicated through the buddy's face, posture, movement, and reactions.
- **Input modes:** Work Mode passes transparent-area input to the desktop. Play Mode captures the bordered sandbox so tools may target empty space.
- **Entering Play Mode:** Interacting with the buddy or an in-game menu, selecting a tool, or using the global toggle enters Play Mode.
- **Returning to Work Mode:** Clicking outside the sandbox, pressing `Escape`, using the global toggle, or choosing the tray action returns to Work Mode.
- **Timeout policy:** Input mode never changes solely because of inactivity.
- **Tool persistence:** Entering or leaving Work Mode does not change the selected tool. Interacting with the buddy resumes the already selected tool.
- **Default size:** `480x360` logical pixels.
- **Minimum size:** `360x270` logical pixels.
- **Maximum size:** Limited by the usable size of the monitor containing the window rather than a fixed pixel ceiling.
- **Aspect ratios:** Window resizing is free-form. Responsive layouts must be validated at standard `4:3`, `16:10`, `16:9`, and `21:9` aspect ratios.
- **Resize semantics:** Resizing changes sandbox boundaries and available room area; it does not stretch the buddy, items, effects, or UI.
- **Zoom:** A separate setting scales all UI elements and world objects proportionally without changing the window dimensions.
- **Zoom values:** Supported live zoom levels are `75%`, `100%`, `125%`, `150%`, `175%`, and `200%`; the default is `100%`.
- **Zoom clamping:** The sandbox may never be smaller than `360x270` world units — the minimum window at `100%` zoom. Zoom levels that would produce a smaller room for the current window size are unavailable; the stored zoom preference is retained and the effective zoom is clamped to the largest supported level for the current window.
- **Initial placement:** First launch positions the window `16` pixels from the lower-right edge of the monitor's usable work area.
- **Window persistence:** Position, size, monitor, and DPI context are saved. Invalid or off-screen positions are clamped back into a usable monitor area.
- **Topmost behavior:** Always-on-top is enabled by default and may be disabled in settings.

## Tool Control Conventions

- **Gentle tools:** Pet and Tickle use held click-and-drag strokes over buddy body parts.
- **Swing tools:** Boxing Glove and Baseball Bat are cursor-tethered physical colliders; damage derives from measured swing speed and collision impulse.
- **Cursor guns:** Pistol and Shotgun remain attached to the cursor. Their forward direction follows the current mouse-motion vector.
- **Gun angle adjustment:** Mouse-wheel input rotates the cursor gun upward or downward from its current forward direction. The next non-trivial cursor movement resets the wheel adjustment and realigns the gun to the new movement vector.
- **Pullback launcher:** Baseball is spawned as a normal loose object, acquired through
  Grab, and aimed by holding secondary while the Grab tether owns it. Dragging backward
  displays a predicted trajectory; releasing secondary launches it opposite the drag vector.
  Control details for the remaining future launchables are confirmed with their ordered M5
  slices rather than inferred from Baseball.
- **Secondary action:** Right mouse cancels or drops the current held/aimed interaction
  without changing the selected tool, except that holding it while Grab owns a Baseball
  activates that ball's trajectory launcher.
- **Firearm trigger/reload:** Pistol and Shotgun fire once per primary press, reload manually with `R`, and automatically begin reloading when fired empty.
- **Cursor visibility:** The operating-system cursor is never hidden or replaced. In Play Mode it remains visible above cursor-attached tool actors.

## Weapon and Status Defaults

- **Pistol:** Physical CCD projectiles, `8`-round magazine, `0.25` seconds between shots, `1.2`-second reload, and unlimited reserve ammunition.
- **Shotgun:** `6` physical CCD pellets per shot, `5`-shell capacity, `0.9` seconds between shots, `2`-second reload, and unlimited reserve ammunition.
- **Grenade:** The `2.5`-second fuse begins on launch. An inexperienced buddy may investigate or catch it; a buddy with harmful grenade history attempts to flee or discard it.
- **Fire Sprayer:** Uses the cursor-gun direction and wheel adjustment. Holding primary fire sprays continuously.
- **Burning:** Fire contact applies a `4`-second burn, refreshable up to `8` seconds. Burning causes panic, periodic pain, mood loss, and dropping held items.
- **Fire recovery:** Repair Kit immediately clears Burning.

## Damage and Knockout

- **Damage representation:** Damage is transient `pain`; the buddy has no mortal health pool.
- **Knockout threshold:** Accumulating `100` pain within a rolling `5`-second window knocks the buddy unconscious.
- **Knockout duration:** Unconsciousness lasts exactly `4` seconds, followed by natural physics-driven recovery.
- **Recovery behavior:** Additional hits do not restart or extend the active knockout timer.
- **Unconscious payout:** Valid damage events during unconsciousness award `50%` of their normal money value.
- **Body-region payout multipliers:** Head `1.2x`, torso `1.0x`, and arms/legs `0.8x`.
- **Calibration:** The physics laboratory determines and documents impulse-to-pain thresholds through playtesting against the approved reference behavior.
- **Held impacts:** Valid impacts award full normal money even while the buddy is attached to the player's grab tether.
- **Contact deduplication:** Each continuous contact episode pays once, with a `0.15`-second source/body debounce to suppress duplicate callbacks and physics jitter.
- **Repeat policy:** Reusing the same tool has no additional diminishing-return rule beyond contact deduplication and the tool's normal cadence.
- **Payout formula:** Money derives from `pain x body-region multiplier x unconscious multiplier x cash-per-pain`. Tools have no hidden payout multipliers; their earning differences emerge from the pain they physically cause.
- **Economy calibration:** `cash-per-pain` is tuned against the approved unlock-time schedule.
- **Currency representation:** Currency is stored as signed 64-bit milli-credits (`1000` minor units per displayed credit), so fractional rewards accumulate without floating-point save drift. HUD and prices display whole credits.
- **Reward feedback:** Damage earnings are coalesced over `0.25` seconds and shown briefly as `+$N.N`; the pain value itself remains hidden.
- **Damage sources:** Calibrated impacts with room boundaries, loose objects, projectiles, and physical weapons may all cause pain; attribution follows the originating tool/throw when available.
- **Attribution expiry:** A launched or thrown object credits its originating tool/throw until it first comes to rest (physics sleep or sustained sub-threshold speed) or until a new interaction reassigns it (player grab-throw, buddy toss/discard). Boundary bounces alone never clear attribution. After expiry, impacts attribute to the generic loose-object source. Explosion damage always attributes to the grenade. Payout thresholds and contact deduplication are unaffected by attribution.
- **Mood loss from harm:** Each accepted harmful event reduces mood by `min(10, pain x 0.1)`. Burning pain ticks use the same rule and knockout adds no separate mood penalty.
- **Knockout-window reset:** The rolling pain window clears when knockout begins. Hits during unconsciousness still pay and affect mood but do not accumulate toward a later knockout; waking starts with an empty window.

## Mood, Care, and Passive Economy

- **Persistent mood:** A hidden scalar from `-100` to `+100` represents the buddy's long-term emotional state.
- **Transient emotions:** Short-lived states such as fear, pain, delight, curiosity, and unconsciousness are tracked independently and decay over time.
- **Passive income availability:** Passive income accrues only while the application is running; the first release has no offline earnings.
- **Income hierarchy:** Peak passive income targets approximately `25%` of the expected earnings of an actively attacking player.
- **Ownership model:** Shop purchases permanently unlock tools and care items for unlimited use.
- **Spam control:** Per-item cooldowns and interaction rules limit repeated use rather than consumable charges.
- **Healing semantics:** Healing items clear transient pain and harmful status effects; they do not restore a mortal health pool.
- **Mood bands:** `-100..-61` fearful, `-60..-21` wary, `-20..20` neutral, `21..60` content, and `61..100` delighted.
- **Passive mood multiplier:** The multiplier is piecewise linear from `0.25x` at mood `-100`, through `1.0x` at neutral, to `2.0x` at mood `+100`.
- **Mood decay:** While the game is running, persistent mood drifts toward neutral at `0.5` points per minute. Mood does not decay while the game is closed.
- **Communication:** The first release has no written dialogue and no voice acting. The buddy communicates with head-circle emoticons, body language, status icons, and original nonverbal robot sounds.
- **Pet/Tickle mood cadence:** A valid Pet or Tickle interaction grants `+1` mood at most once per `3` seconds.
- **Catch reward:** Catching a safely thrown object grants `+1` mood per completed throw/catch event.
- **Meal:** Grants `+10` mood and has a `60`-second reuse cooldown.
- **Drink:** Grants `+5` mood and has a `60`-second reuse cooldown.
- **Repair Kit:** Grants `+20` mood, clears transient pain and harmful statuses, has a `120`-second reuse cooldown, and does not shorten an active knockout.
- **Care payout:** Care interactions award no immediate money; their economic benefit comes through mood-scaled passive income.
- **Care cooldown start:** Meal, Drink, and Repair Kit cooldowns begin only after successful consumption/use. Cancelled or failed throws do not start a cooldown.

## Launch Interaction Catalogue

- **Direct interactions:** Grab, Pet, Tickle, and Boxing Glove.
- **Physics toys:** Baseball and Soccer Ball.
- **Melee:** Baseball Bat.
- **Firearms:** Pistol and Shotgun.
- **Explosive:** Grenade.
- **Elemental:** Fire Sprayer.
- **Care:** Meal, Drink, and Repair Kit.
- **Currency:** The game uses one earnable currency and has no real-money microtransactions.
- **Firearm resources:** Firearms have unlimited ammunition but enforce weapon-specific firing cadence and reload timing.
- **Loose-object budget:** At most `24` loose physics objects may exist. When the cap is exceeded, the oldest safe object that is not held or otherwise protected is removed.
- **Current progression horizon:** The current interaction catalogue targets approximately `2` hours of play to unlock completely.
- **Future progression:** Cosmetics may extend the progression curve later, but cosmetic implementation is outside the current scope.
- **Starting interactions:** Grab, Pet, Tickle, and Boxing Glove are available immediately.
- **Target unlock sequence:** Baseball at `3` minutes, Meal at `6`, Baseball Bat at `20`, Pistol at `30`, Grenade at `40`, Fire Sprayer at `50`, Soccer Ball at `65`, Drink at `80`, Shotgun at `100`, and Repair Kit at `120` minutes of cumulative play.
- **Price calibration:** Item prices are tuned against the target unlock times using the approved active/passive income mix.
- **New-save defaults:** `0` money with Grab selected.
- **Purchase finality:** Purchases are immediate, permanent, and cannot be sold or refunded.
- **Reset safety:** Resetting progression requires explicit confirmation.

## Explicit Future Content

- Buddy coloring and paint interactions are future content padding and are not part of the current implementation scope.
- Optional cosmetic progression may be designed later and is not required by the current catalogue.
- Steam Workshop support for custom buddies is a future architectural consideration and is not part of the current implementation scope.

## Persistence and Steam

- **Semantic save state:** Saves include money, unlocks, selected tool, persistent mood, harmful-history/tool memory, statistics, and user settings.
- **Non-persistent simulation state:** Live body pose, loose objects, active projectiles, recent pain window, knockout state, and temporary statuses are not saved.
- **Session resume:** A loaded session starts the buddy in a safe standing pose while restoring semantic progress.
- **Steam features:** The first Steam release includes Steam achievements, Steam stats, and Steam Cloud for progression data.
- **Local settings:** Machine-specific window position, monitor, size, DPI context, and local settings are excluded from Steam Cloud.
- **Steam fallback:** Failure or absence of Steam initialization never prevents local play or local saves.
- **Steam binding:** The optional Steam adapter uses Steamworks.NET as the authorized C# binding. Its native `steam_api64.dll` ships only through the release export path and never enters development commits.
- **Save format:** Progress uses versioned JSON, atomic replacement, one rolling backup, and quarantine of corrupt files before fallback recovery.
- **Autosave:** Dirty progress flushes every `30` seconds and immediately after purchases, unlocks, focus loss, and clean exit.
- **Tray controls:** Show/Hide, Work/Play Mode, Always on Top, Return to Bottom-Right, Reset Buddy, Settings, and Save & Quit.
- **Windows startup:** Launch with Windows is optional and disabled by default.
- **Hidden operation:** While hidden to the tray, rendering and ragdoll physics are suspended; mood timers and passive income continue at low cost.
- **No catch-up:** Closing the app, Windows sleep/suspend, or a large clock discontinuity grants no mood or income catch-up. On resume, the physics accumulator is cleared to prevent a simulation burst.
- **Session lock:** Locking the Windows session counts as normal running time: mood drift and passive income continue and no clock discontinuity is recorded. While locked, the game may enter the hidden-style low-cost mode (suspended rendering/ragdoll physics) and restore the prior state on unlock; locked time accrues as hidden-passive time when that mode engages.
- **Tracked stats:** Total money earned; best earnings over `1`, `3`, and `10` seconds; total running, active-interaction, and hidden-passive time; total pain; knockouts; successful catches; highest/lowest mood; and per-tool uses/pain.
- **Offline Steam queue:** Achievement and stat updates earned without Steam connectivity are queued locally and synchronized after reconnection.
- **Launch achievements:** First Impression (first damage money), Lights Out (first knockout), Retail Therapy (first purchase), Full Toybox (full launch catalogue), Best Friends (mood `+100`), Forgiven (harmful-history reset at mood `60`), Nice Catch (`25` catches), Variety Hour (all launch interactions used), Fire Drill (Burning cleared with Repair Kit), and Desktop Shift (`2` running hours).

## Code and Test Architecture

- **Composition:** Scene roots are thin orchestrators. Single-purpose C# components receive typed scene references; signals/events communicate upward and explicit methods/commands communicate downward.
- **Data assets:** Typed Godot `Resource` assets define physics tuning, tools, mood profiles, economy data, and content metadata. Runtime saves remain versioned JSON.
- **Determinism boundary:** Bit-exact deterministic replay is not required.
- **Automated verification:** Pure C# unit tests cover domain rules. Headless Godot scenarios use seeded inputs and tolerance envelopes to validate maximum stretch, recovery timing, damage attribution, repeated-run stability, and other physics behavior.

## Owner Feedback Fix Decisions (2026-07-20)

- **Grab dangle:** a grabbed buddy that still has floor support keeps active standing.
  Once a buddy-part grab has no support, standing/recovery actuation yields so the body
  hangs from the grab point; conscious fear-resistance struggle remains enabled.
- **Pet face:** the calm/default face is `:)`. Active valid Pet rubbing overrides it with
  `:3`; Pet completion returns to `:)`. No additional completion-face string is added.
- **Head righting:** after a head impact or head-grab release, a gentle bounded physical
  head-righting torque may begin after two seconds of calm. At the fixed 120 Hz domain
  clock this is `240` routed ticks (correcting the original draft's mistaken "120 ticks = 2 s"
  parenthetical). The owner judges final speed at the grouped feel pass. A future animation
  in which the buddy straightens its head with its hands remains an M4+ candidate.
- **Physical gestures:** Eat may move the authoritative right-hand body through a bounded
  physics target while the existing visual-offset cap remains unchanged. The same seam is
  reserved for a later physical Wave reach; Eat ships first.

### Owner feel-pass corrections (2026-07-21; supersede the affected bullets above)

- **Eat sequence:** Eat is a stationary five-bite action. Both authoritative hand bodies
  hold the food at the upper-chest center, lift it toward the head in a repeated bopping
  motion, and return it between bites. The head makes one small downward bob per bite.
  The centered item visual shrinks by one fifth at each bob and disappears on bite five.
  Both hands render in front of the torso throughout Eat, including at three-quarter yaw.
  During the initial chest hold, the head receives half the downward displacement of the
  first implementation before the repeated bite bobs begin.
  At the mouth, both hands also render in front of the face plate, with their upper edges
  sitting just below the mouth for readability; the food may be presentation-offset
  upward from the two-hand midpoint so it still meets the mouth.
  Eat temporarily eases the body presentation to a frontal face-to-food pose, preserving
  the committed walking side for restoration afterward. After bite five, the hands make
  one final downward return to their normal rest height before the reach releases and
  they move horizontally to their side positions.
- **Grab dangle:** an unsupported buddy-part grab is fully passive, matching unconscious
  ragdoll behavior while consciousness and facial state remain conscious. No fear-driven
  resistance force is applied in the air. A supported grab retains normal standing and
  conscious reactions. “Passive” disables active drive only: the ordinary structural
  springs remain enabled exactly as they do while unconscious, preserving each part's
  relative place instead of allowing every unheld part to slide to the lowest limit.
- **Wall stopping:** ambient locomotion senses the foremost body edge in its travel
  direction, includes bounded velocity look-ahead, cuts the into-wall goal, and brakes to
  a stop before contact. Torso-center-only avoidance is insufficient.
- **Walk-to-idle stopping:** when grounded autonomy changes from walking to no walk
  intent, bounded active braking stops horizontal motion without a visible coast. This
  does not erase airborne/throw momentum.
- **Head-righting speed:** the two-second calm delay remains, but once righting begins the
  head must settle within at most `60` routed ticks (`0.5 s` at 120 Hz).

### Grab-hang pendulum feel ACCEPTED (2026-07-22)

- The owner tested the v2 unsupported foot grab interactively, including whipping the
  cursor side to side, and accepted the result ("omg yes thanks way better"). Unsupported
  grabs therefore use the bounded gravity-style pendulum torque rather than the rejected
  overdamped angle servo. The accepted active-drive values are `HangGravityGain = 980`,
  `HangSwingDamping = 0`, and `MaximumHangAlignTorque = 48000`.
- Airborne-grab passive structure is accepted at `1.25x` spring stiffness and `1.0x`
  damping. The ordinary structural springs and maximum-distance limits remain active, so
  limbs lag and flex without returning to the rejected limit-only slide. The corresponding
  `grab_dangle` regression bound is `48 px` (measured `42.8 px` plus margin).

## M4 Delegated Engineering Defaults (2026-07-26)

These are provisional engineering choices delegated by the resolved M4 pre-plan
decisions. They are implemented and test-covered, but are not owner feel acceptance:

- Stable persisted IDs are `tool.grab`, `tool.pet`, `tool.tickle`,
  `tool.boxing_glove`, `object.loose`, `boundary.room`, and `care.lab_food`.
- The laboratory food borrows Meal's `+10` mood and `7200` routed-tick (`60 s`)
  cooldown. Reuse cooldowns are transient and intentionally not saved, so relaunch
  clears that window; revisit when the real purchasable Meal lands in M5.
- The clock-discontinuity exclusion threshold is `5 s`. Neutral passive income is
  provisionally `1` credit/minute in `MoodEconomyProfile`; M5 replaces it through
  FR-012.3 economy calibration.
- Obstacle-hop propensity is one uniformly sampled deterministic integer bucket in
  `0–100`, created once per new save and then persisted exactly.
- Five-band social tuning is: Fearful `260 px` standoff/`24 px` hysteresis; Wary
  `150/18`; Neutral `0/12`; Content approaches to `170 px`, catches, and greets
  every `900` ticks; Delighted approaches to `110 px`, catches, and greets every
  `360` ticks. These distances/cadences remain subject to the M4 owner feel gate.
- Object lifecycle defaults are a `220 px` sense/approach radius, `46 px` catch
  distance, `90`-tick catch timeout, `120`-tick hold, and `150`-tick inspection.
- The minimal M4 tray scope is Show/Hide plus Save & Quit. The complete FR-016.1
  tray menu remains Milestone 6 scope.

## M4 Review Fixes (2026-07-26)

Raised by the pre-acceptance implementation/code review of all six M4 tasks
(`docs/M4_REVIEW_FIXES_PLAN.md`). These correct plan deliverables that had not
landed and defects the automated gates did not catch.

- **Obstacle probe height is `64 px` below the torso centre**
  (`AutonomousMotionProfile.ObstacleProbeHeightOffset`). The probe used to fire at
  torso height, roughly `48 px` above the top of any loose object resting on the
  floor, so the persisted obstacle-hop trait could never fire in real play. The
  `jump_trait_gate` and `autonomous_motion` scenarios previously supplied a frozen
  torso-height prop; both now use ordinary floor-resting objects, so the gate
  exercises the shipped path.
- **Hidden mode throttles presentation**: `MoodEconomyProfile.HiddenMaxFps = 10`
  plus `RenderingServer.RenderLoopEnabled = false` while hidden. Pausing the tree
  never stopped the main loop, so the process kept rendering behind an invisible
  window. Show restores both and re-anchors physics interpolation across the rig and
  every registered loose object; the step accumulator itself stays bounded by the
  existing `physics/common/max_physics_steps_per_frame = 6` project setting, which is
  the FR-015.10 answer rather than a nonexistent engine accumulator API.
- **Restoring from hidden is a Milestone 6 dependency.** M4 ships the tray command
  surface — `toggle_hide_to_tray` (`Ctrl+Shift+H`) and `save_and_quit`
  (`Ctrl+Shift+Q`) through `TrayCommandComponent` — but Godot delivers no input to an
  invisible unfocused window, so the *restore* stimulus needs the native tray icon or
  OS-global hotkey that FR-016.1 already scopes to M6. The state machine and the
  command seam are complete and the native adapter binds the same events.
- **Suspend/resume/session lock travel through `IWindowsDesktopAdapter`**
  (`SystemSuspending`, `SystemResumed`, `SessionLockChanged`). The emulated adapter
  raises them deterministically for `suspend_no_catchup`; the native adapter declares
  them and its `WM_POWERBROADCAST`/session hooks join the M2 owner-manual Windows
  matrix. A locked session accrues as hidden **running** time with no clock reset and
  no discontinuity exclusion (FR-016.8).
- **Held objects can be physically lost**:
  `ObjectInteractionProfile.HoldReleaseDistance = 72 px`. Hold confirmation used to be
  unconditional, so nothing could knock an object out of the buddy's hands and the
  interrupted-meal drop path was unreachable. A lost grip drops and starts no
  cooldown (FR-008.10).
- **Candidate scoring prefers airborne over resting objects.** A thrown object is a
  moment the buddy can miss; distance alone let a nearer idle prop steal the
  commitment and lose the FR-008.3 catch.
- **Greeting owns one tick per cadence, not the whole approach envelope.** A content
  or delighted buddy inside `170`/`110 px` of the cursor used to hold priority 6
  continuously with no drive, freezing ambient autonomy between waves.
- **Save & Quit and clean exit force the flush.** Coalescing callers still join an
  in-flight write and leave newer state dirty; the quit paths run at most one extra
  pass, bounded so continuously advancing running-time revisions cannot trap the exit.
- **Legacy save corruption quarantines instead of crashing.** The v1 migration read
  integer fields with throwing accessors, and `Decode` caught neither
  `InvalidOperationException` nor `FormatException`, so a wrong-typed legacy field
  exited the app instead of recovering through the backup/defaults chain.
- **One arbitration ladder.** Object suppression is derived from
  `BehaviorArbiterModel.SuppressesVoluntaryAction`, replacing a hand-rolled copy of
  priorities 0–4 in the runtime arbiter that had to be kept in sync by hand.
- **The laboratory can spawn loose objects**: `O` drops one safe object at the cursor,
  `Shift+O` clears them all. Every object-interaction feature — approach, catch, hold,
  inspect, toss, discard, obstacle hop — was unreachable by hand because the only
  object the lab could create was the Eat key's food, which goes straight into the
  hand. The owner gate steps that judge those behaviours were not performable before
  this key existed; `laboratory_controls` now covers it.

## M4 Owner Tuning Corrections (2026-07-26)

Owner feel corrections made after hands-on play, overriding earlier delegated defaults
and part of owner decision 1. These are owner instructions, not engineering choices.

- **Jump impulse doubled, `1800` → `3600`** (`ActiveDriveProfile.JumpImpulse`). The old
  value produced roughly a `35 px` torso rise, which did not reliably carry the feet over
  a resting loose object. This only became visible once obstacle hops could fire at all.
- **Wary and Neutral now catch thrown objects**, revising owner decision 1's "wary — no
  approach or catch". Only Fearful refuses outright. A new save sits at mood `0`
  (Neutral), so declining there meant the buddy ignored everything thrown at it, which
  read as broken rather than as guarded. Keeping distance from the cursor and accepting a
  thrown ball are separate impulses: Wary still holds its `150 px` standoff and still
  never approaches the cursor, and neither Wary nor Neutral tosses an object back for fun.
- **Only a real player throw is a catch target.** A voluntary commitment now requires the
  candidate to be airborne *and* carry a throw token from `MarkPlayerThrown`; consumables
  are exempt. This was forced by the two changes above interacting: object action is
  priority 5 and the obstacle hop is priority 7, so once Neutral caught, every resting
  ball in the walking path was claimed for a pickup and hopping silently stopped working
  again — including balls the buddy had just kicked with its own foot, since "moving" is
  not "thrown". The split also matches the long-documented meaning of
  `ObjectCandidate.AtRest` and keeps food pickable off the floor.
- **Cooldown outranks hand state in the lab-food rejection reason.** A cooldown belongs to
  the content ID, not to what the hands are doing, so `OnCooldown` is reported whenever it
  applies instead of being hidden behind `UnknownConsumable`.
- **The laboratory `E` key removes the food it spawned when the consume is cancelled.**
  Otherwise a cancelled meal leaves a consumable on the floor that a neutral-or-better
  buddy immediately walks back to collect, overriding whatever the operator does next —
  it was preempting the wave gesture in `m36_expressive`.

## M4 Object Handling Feel (2026-07-26)

Owner instructions after seeing the buddy stretch both arms most of the room's width to
reach a ball. Full detail in `docs/M4_OBJECT_HANDLING_FEEL_PLAN.md`.

- **Reach is bounded and measured in 2D.** Candidate distance was horizontal only, so
  `CatchDistance = 46` admitted an object 46 px sideways and arbitrarily far above — the
  diagonal stretch the owner saw. Distance is now a true 2D reach from
  `ReachOriginOffset`, and every hand target is clamped into
  `ReachRadius + MaximumReachExtension` (`44 + 6 px`). `MaximumHandForce` drops
  `18000 → 6000`. `CatchDistance` above the reach limit is now a validation error.
- **Objects are never sprung toward the buddy.** The object spring is deleted. A catch
  confirms when the object physically touches a hand, and the object then **attaches**:
  frozen kinematic, placed on the hand socket each routed tick. This hard placement is the
  owner's explicit request ("the ball should stick to its hand", "relocate directly to the
  buddy's hand"). It does not breach ARCHITECTURE §23, which governs the buddy rig — those
  bodies are still driven only by bounded forces. A carried object is cargo while held.
- **Ground pickup is a scoop**, not a grab from range: walk to the object, dip the torso and
  head with a bounded force while the hands lower, then the object relocates into the hand.
  The runtime chooses scoop or catch from the registry's rest state, so the domain lifecycle
  gained no new phase.
- **The return throw goes toward the cursor**, reversing the earlier cursor-safe
  away-from-cursor toss. `ThrowWindupTicks` draws the hands back first so the release reads
  as a throw. `TossTicks` was added to the domain tuning because a two-beat gesture cannot
  live in a single tick. Discard keeps its low-energy away release and flee bias.
- **Obstacle detection has two independent sources.** `RayCast2D.HitFromInside` defaults to
  false, so once the buddy was touching a ball the probe origin sat inside it and reported
  nothing — precisely the case the hop exists for, which is why detection was intermittent.
  `HitFromInside` is now true, and a registry-backed check (resting object within
  `ObstacleForwardWindow` ahead and below the torso) is OR'd in.

## M4 Object Handling — Second Pass (2026-07-27)

The 2026-07-26 pass bounded the arm reach but did not make object handling work. Owner
report and full detail in `docs/M4_OBJECT_HANDLING_FEEL_PLAN.md`, "Second pass".

- **Carried objects ride the midpoint of both hands**, not the hand that made contact
  (`CarryLiftFraction`). Pinning to one hand put the Eat item off to the side.
- **Resting objects are pickup targets again.** Making them ineligible removed ground pickup
  entirely. The obstacle hop stays reachable through a `ReleaseIgnoreTicks` window on objects
  the buddy itself put down, not through a blanket refusal.
- **A ground pickup is gated horizontally** (`ScoopDistance` against
  `ObjectCandidate.GroundDistance`), because the floor is ~`66 px` below the shoulder line and
  a straight-line gate is unsatisfiable. **Collision exceptions apply from commitment**, so the
  buddy stops kicking away the object it is walking toward.
- **Catch capture succeeds anywhere inside the reach envelope**, not only within a hand radius.
  A thrown ball meets the `28 px` torso before it ever gets that close to a hand, so it simply
  rebounded and the buddy appeared not to react.
- **Obstacle hops carry forward momentum** (`ObstacleHopHorizontalRatio = 0.3`). The ambient
  branch passed a zero jump direction, so hops were purely vertical and landed back on the
  object.
- **Accepted-bound change, owner-visible:** `autonomous_motion`'s
  `grounded_walk_stops_without_coast` residual-speed bound moves `2.0 → 6.0 px/s` and skips a
  `150`-tick landing window. The travel bound stays `1.25 px` and measures `0.5 px`. The old
  bound predates any horizontal impulse source; the obstacle hop is now one.

## M4 Object Handling — Third Pass (2026-07-27)

- **One ball at a time.** A lab drop replaces the previous loose object rather than littering
  the room. This is a *spawn policy*; `LooseObjectRegistry` keeps its full capacity and
  eviction rules, which remain separately tested.
- **The buddy watches a ball while the player carries it.** Player-held objects used to be
  skipped entirely as candidates, so the buddy was blind to the ball until the instant of
  release — far too late to react to a close throw, which is why it seemed to ignore thrown
  balls. It now commits to a carried ball, holds the ready pose without timing out
  (`CatchTimeoutTicks` does not run while `PlayerHeld`), and never takes it out of the player's
  hand. `HeadLookAtComponent` feeds the committed object into the existing `Item` look-at
  source, so the head tracks the ball too.
- **Carry pose clears the head.** `HoldCenterOffset` moves `-24 → -8` and `CarryLiftFraction`
  drops to `0`. The head spans roughly `-26` to `-74` from the torso, so the old carry position
  put the ball inside it.
- **The throw leaves from the throwing hand**, after that hand has swung forward.

## M4 Object Carry and Throw Pose (2026-07-27)

- **One-handed carry in a natural pose, object resting on top of the hand**
  (`CarryHandOffset = (34, -2)`, `CarryLiftFraction = 1`). Carrying at the midpoint between
  both hands clutched the object into the torso, and lifting it from there pushed it into the
  head. On this rig the head's underside sits at `-26` and the torso's top at `-28`, so there
  is no gap above the body — the only clear space is out to the side at roughly the hand's own
  resting offset. The free hand mirrors the pose so it is never dragged across the body.
- **The throw is a three-beat gesture**: the carrying hand draws back
  (`ThrowWindupDistance`), swings forward past the carry pose (`ThrowForwardDistance` over
  `ThrowForwardTicks`) with the object still riding it, and lets go at the forward extent.
  `TossTicks` must exceed wind-up plus forward, which is now a validation error rather than a
  silent truncation. Aim is taken from the throwing hand, not the torso.
- **A released object stays non-colliding with the buddy for `ReleaseCollisionGraceTicks`
  (`60`)**, so a thrown ball cannot clip the hand that threw it or the body it just left.
  Collision exceptions therefore span the whole interaction: from commitment, through carry,
  to shortly after the release.

## M4 Throw Launch (2026-07-27)

The throw gesture played but the ball never left — it dropped at the buddy's feet.

- **The release assigns velocity instead of applying an impulse.** `EndHold` unfreezes the
  body on the same tick the release command runs, and an impulse queued against a body that
  has just left its frozen state is discarded by the physics server. Profile properties are
  renamed accordingly: `TossImpulse`/`TossLiftImpulse`/`DiscardImpulse`/`DiscardLiftImpulse`
  become `TossSpeed`/`TossLiftSpeed`/`DiscardSpeed`/`DiscardLiftSpeed`, since they are px/s
  and a silently-changed meaning behind an old name is worse than a rename.
- **The launch velocity is re-stated for `LaunchHoldTicks` (`3`).** A single assignment on the
  frame a body resumes simulation can still be overwritten before it integrates. Owner
  direction was explicitly to fake the throw rather than solve it through physics.
- **The swing has its own force budget** (`ThrowHandForce = 24000`). The carry force is
  deliberately gentle at `6000`, far too soft to move an arm in a handful of ticks, so the
  wind-up barely registered and the release read as a drop.

`object_toss_discard` previously asserted only that a release was *recorded* — an impulse
value and a drive count. It never checked that the object moved, which is exactly how a throw
that dropped straight down passed every gate. It now tracks the released body for 30 ticks and
requires real flight: `flight_speed=938`, `flight_travel=213`.

## M5 Baseball Input — Revised (2026-07-28)

- **Key `5` only spawns the Baseball at the cursor.** It does not change tool selection.
  The spawn uses the same one-ball replacement policy as the laboratory's `O` control:
  the prior loose object is removed, so repeated presses never accumulate balls.
- **Baseball pickup uses the normal Grab tool.** Select Grab with `G`, then acquire and
  carry the Baseball through the existing damped elastic left-button tether. The earlier
  dedicated short-click-follow behavior is explicitly reverted.
- **Holding secondary while Grab owns the Baseball enters pullback aiming.** Backward drag
  shows the trajectory preview; releasing secondary beyond the pull threshold launches
  opposite the pull and releases the Grab tether.
- **Player ownership is absolute.** The buddy may watch and ready for a player-held ball,
  but cannot attach or take it from the player's Grab. Watch interest also cannot install
  collision exceptions on player-held or airborne balls.
- **A full Baseball launch is deliberately forceful.** Provisional tuning is
  `15 px/s` per pull pixel, capped at `1800 px/s`, with a `1.0`-mass ball. A `1575 px/s`
  laboratory strike produced `4.2` pain and approximately `9.4 px` of whole-buddy
  displacement. Objects above the provisional `900 px/s` catch-speed ceiling are impacts,
  not automatic catches.

## M5 Baseball Accepted, Meal Chord Confirmed, Cornered Pickup (2026-07-29)

- **Baseball feel is owner-ACCEPTED.** The slice is shop-visible: `tool_baseball.tres`
  carries `Visible = true`. Its price stays provisional until Task 12 calibration.
- **The Meal uses the same launch chord as the Baseball** (owner confirmation): its key
  spawns/replaces one Meal at the cursor, Grab acquires it, hold-secondary and drag back
  previews, release launches. No new input contract for the slice.
- **A cornered object must be pickable** (owner report with the Baseball acceptance: the
  buddy could not pick up a ball sitting completely in a corner). Two rules changed, both
  engineering-delegated:
  - A committed object approach spends the ambient wall-avoid comfort margin. The margin
    exists so ambient wandering does not scuff the walls; a buddy walking over to fetch
    something has a reason to be at the wall. It now stops on real contact, measured from
    the **torso** rather than the widest part — a swinging hand reaches the wall roughly
    `23 px` before the body does, which is most of the gap that made the object unreachable.
    Ambient, hazard-flee, and social layers keep the original accepted M1 wall stop.
  - The ground-scoop gate measures the object's **near surface**, not its centre. A ball
    pinned against a wall stops the body about `29 px` from its centre and no amount of
    walking closes that, while the gate was `26 px`. The scoop is a timed dip that lifts
    the object into the hands, and the runtime already suspends collision with a committed
    object, so "the object is against my body" is the honest gate. `ScoopDistance` itself is
    unchanged at `26`.
  - Scenario `corner_scoop` covers both corners and is in the quick suite. It replaces the
    workaround in `object_catch_hold`, which deliberately spawned away from the walls
    because a cornered ball "can never be closed on".

## Hunger Replaces the Food Cooldown (2026-07-29)

Owner feedback on the Meal slice: a full buddy kept walking to food, picking it up, dropping
it, and repeating until its cooldown expired. The fix is a model, not a patch.

- **The buddy has a hidden `200`-point hunger bar** (FR-008.16). It is never displayed; the
  player reads it from behavior, like mood and the favorite Pet spot.
- **Acceptance is arithmetic, not a threshold** (FR-008.17). The buddy eats an item only when
  it fits: `fullness + fill <= 200`. The owner's example is the specification — at `160`
  fullness a `50`-point cake overshoots by `10` and is refused, while a `10`-point apple is
  eaten. Portion size is the whole decision, so a nearly full buddy still takes a snack.
- **Appetite burns at three rates** (FR-008.18): `20`/minute while the buddy is actively
  played with, `10`/minute during ordinary Play-mode presence, `2`/minute in Work mode or
  hidden. Hidden and Work mode are one case — the buddy is idling on someone else's desktop.
- **Every consumable reuse cooldown is gone.** Appetite replaces the Meal's and Drink's;
  FR-008.4/.5 are amended. The Repair Kit has **no cooldown at all** (owner, 2026-07-29) and
  no appetite gate either — it is not food, so nothing rations it; FR-008.6 is amended. The
  `ConsumeCooldownTicks` field stays in the object profile at `0`, unused by shipped content,
  rather than being deleted from a schema mid-milestone.
- **Refusal is a performance, not a silent drop** (FR-008.19). The buddy picks the item up
  once, shakes its head side to side, and puts it down; then it ignores *that* item until
  it has room for that portion again. Other food is still considered on its own size. The
  refusal is remembered per object, so the fetch-drop-fetch loop cannot recur.
- **The refusal is staged exactly as the owner described it** (correction 2026-07-29, after
  seeing the first cut): the item stays in the ONE hand that picked it up — the refusal does
  **not** share the eat reach, which is what made the food ride the midpoint between both
  hands like a meal being lifted to the mouth. The buddy turns frontal for it, because the
  "no" is aimed at the player who offered the food, and the refusal owns the head for its
  duration so an ambient glance cannot wander off mid-gesture. The “no” is a **head yaw**,
  not a sideways translation: rotate around the neck’s vertical axis as though looking over
  alternating shoulders, cross neutral continuously without a pause, use no more than four
  alternating extremes, damp the amplitude, keep pitch/roll substantially stable, and finish
  neutral. The first extreme is capped at `30°` (`ActivityRefuseYawDegrees`); the authored
  four-extreme shape is left `30°`, right `24.9°`, left `20.1°`, right `12°`, then neutral.
  The clip is seeked across the behavior-owned refusal window rather than advanced in real
  time, so the gesture fills the window however the two profiles are tuned. It ends in a
  plain **drop below the buddy**: the old discard impulse threw the food aside, which is what
  the owner saw as the food glitching away. Distance was never what stopped the fetch loop —
  the per-object refusal memory is.
- **Provisional magnitudes** (agent-tunable): Meal fills `50` points, the head-shake runs
  `96` ticks (`0.8` s), and a new save starts with an empty stomach. A schema-4 save loads
  empty too, so an upgrade never leaves a buddy mysteriously full.
- **Eating is two-handed.** The physical item now rides the midpoint between the hands while
  the eat reach is active, matching the drive that brings both hands to the mouth and the 3D
  item socket. It previously rode the single carrying hand's socket, which is what the owner
  saw. The one-handed pose remains correct for ordinary carrying (M4 decision 2026-07-27).

## M5 Meal Slice Accepted (2026-07-30)

- **The Meal feel gate passed** (owner, 2026-07-30: "meal feel is fine"), covering the
  hunger/appetite behavior and the corrected head-yaw refusal. `tool_meal.tres` flips to
  `Visible = true`, so the Meal is the second catalogue entry offered in the shop after the
  Baseball. Its price stays the provisional FR-013.4 placeholder (`6` credits) until Task 12
  calibrates the economy.
- **Lab food is kept as a dev-only spawn** (owner, 2026-07-30). The Task 3 plan left
  retire-or-keep open at this review; the answer is keep. `care.lab_food` stays on the `E`
  key, stays out of the catalogue so no player ever sees it, and the M3.6/M4 scenarios keep
  using it rather than being migrated onto the Meal. This is settled, not revisited at the
  Task 13 gate.

## M5 Baseball Bat and the Cursor-Tool Mechanism (2026-07-30)

- **The Boxing Glove mechanism is now a shared, data-authored one.** `BoxingGloveController`
  / `BoxingGloveBody` / `BoxingGloveProfile` became `CursorToolController` / `CursorToolBody`
  / `CursorToolProfile`, and the controller holds an authored array of profiles instead of a
  single one, exactly as the launcher was generalised for the Meal in Task 3. A
  cursor-tethered tool is now a `.tres` plus a content ID, not new input code. Each profile
  authors its own shape, mass, tether gains, alignment gains, and colours; the collider
  derives its attribution identity from the profile, so nothing keys on "the glove" any more.
  Facing, head look-at, and the pointer path ask `DrivesTool`; impact feedback asks
  `AttributesContent`. The one-collider-at-a-time rule is unchanged, and a tool swap is a
  despawn plus a respawn rather than a reconfigure, because shape, mass, and identity all
  belong to one profile.
- **An elongated tool holds square to its own swing.** New engine-free
  `Domain/Physics/AlignmentTorque` is the rotational counterpart of `GrabTether`: a bounded
  damped angular servo that takes the shortest way around and folds out the half-turn
  symmetry of a two-ended tool, so a bat never spins 180° to present its other end. The
  target angle comes from the cursor's own travel; below `MinimumAlignSpeed` the tool holds
  the angle it had. A stiffness of `0` disables alignment, which is how the round glove
  authors "never steer my rotation" without a branch. Without this an elongated collider
  tumbles off every contact and reads as a floating stick, so the profile validator requires
  the pairing.
- **Provisional Baseball Bat tuning** (agent-tunable, Task 12 calibrates): length `90` px,
  radius `7`, mass `6.0`, tether `4800`/`240` capped at `120000`, alignment `400000`/`66000`
  capped at `500000` above `60` px/s. Pain comes only from the measured impulse through the
  shared curve — there is **no** per-tool multiplier, so the bat hurts more only because a
  longer, heavier collider really does hit harder. The catalogue entry stays
  `Visible = false` until its own owner feel gate.
- **The buddy's learned defense stays glove-only for now.** `ToolReactionComponent` still
  guards against the Boxing Glove alone. Extending the guard to the bat is a feel decision
  about a tool with reach, and the Task 4 gate does not ask for it; the buddy does record
  the bat in harmful history today, so the memory is already correct when the owner decides.
- **`ProgressStatistics.ToolUses` has no runtime writer.** `BuddyProgressState.RecordContentUse`
  exists and is unreferenced; only the `ToolPainMilli` half of the stats seam is live. What
  counts as one "use" of a swung, fired, or thrown tool is an owner call, so the bat slice
  asserts the pain half and leaves the counter alone rather than inventing a rule.

## Hit-Lag Shake Gets Its Own Offset Lane (2026-07-30)

- **The charged-bat victim shake uses a second visual-offset lane that the performance
  weight does not gate** (owner, 2026-07-30). `BuddyVisualPresenter.ResolvePerformanceOffset`
  is the only path that nudges a part's visual off its physics body, and it returns zero
  whenever `_performanceWeight <= 0`. `PoseModeArbiter` forces Tracking — and so zero weight —
  while `TicksSinceImpact < PostImpactCooldownTicks` (60) or the buddy is not stable
  standing. Every scored bat hit sits inside that window for the whole hit-lag freeze, so
  routing the shake through the existing lane would silently multiply it by zero.
- **Why this is not a weakening of the M3.6 rule.** That gate exists so an animation offset
  can never draw the buddy somewhere the physics body is not *while the ragdoll is really
  moving*. During hit lag the struck bodies are frozen, so there is no motion for a ±2 px
  jitter to misrepresent. The collision was with the plumbing — one shared pipe, zeroed
  wholesale, with no notion of why a given offset exists — not with the reasoning. The
  invariant that actually matters is unchanged: the new lane still clamps through
  `BoundedOffset.Clamp` against the same `OffsetCapRadiusFraction * partRadius` cap, so the
  visual can never stray further from its body than it can today.
- **NOT A DEFECT — do not flag on review.** An offset contributor that deliberately ignores
  `_performanceWeight` is the accepted design here, not a missed gate. A reviewer or agent
  re-reading this code should treat "this offset bypasses the Tracking gate" as expected and
  move on. The lane must carry a comment at its definition pointing back to this entry.
- **Scope is the hit-lag shake alone.** Only `ImpactVisualOffsetComponent`, and only while a
  home-run hit lag is active, may use the ungated lane. Every other offset source —
  development/scenario offsets, activity clips, anything added later — still goes through the
  performance-weighted path. Widening the lane to a second consumer is a new owner decision.

## Home-Run Bat Interaction Gate — Resolved in Full (2026-07-30)

The nine open items in `docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md` §5 are answered. Task 4
implementation is unblocked. Five confirmed the drafted default; four changed the design.

- **Confirmed as drafted:** releasing LMB mid-charge cancels to the weak follow with no swing
  (a safe bail-out); an RMB tap performs the minimum-charge home-run arc rather than nothing;
  holding RMB past 5 s keeps full charge indefinitely until release; the post-release windup
  stays the provisional 14-tick snap; the bat is a clean-room classic wooden bat with no Smash
  black/gold trade dress.
- **Swing direction is cursor travel, not target proximity.** The bat swings whichever way the
  mouse is moving, so the swing always goes "in front of" the cursor — drag right, swing right;
  drag left, swing left. The drafted "nearest strikeable body outside an X dead zone" rule is
  withdrawn. This deletes the planned `SwingTargetResolver` component outright: it existed only
  to serve the target rule, and nothing else in the slice needs a proximity query. Delegated
  reading, flagged in the plan: direction tracks through the charge and commits at RMB release,
  so a player can wind up and change their mind; the charge lean flips sides when they do.
- **The hit-lag freeze stops every game element**, not just the bat and the struck buddy. This
  deliberately departs from the Smash reference (where unrelated actors keep moving). It is
  implemented as a wholesale suspension of the composition root's routed physics tick, which is
  *simpler* than the per-body freeze it replaces — nothing advances, so nothing needs a
  velocity snapshot or a transactional restore. `Engine.TimeScale` is still not used. Because
  no unrelated motion remains on screen, the victim shake stops being decorative and becomes
  the only thing distinguishing a hit-stop from a hitch — it is now mandatory, not deferrable.
- **Loose-object freeze is full-charge-only.** A scored buddy hit freezes at any charge with
  the duration scaled by charge. A loose object freezes only when the charge actually reached
  the cap, and below that its physics stays continuous. Delegated reading, flagged in the plan:
  "full charge" means normalized charge `== 1.0`, the same condition that fires the tip glint,
  so what the player sees glimmer is exactly what earns the object freeze.
- **Placeholder audio ships in this slice.** The drafted "defer sound entirely, leave a hook"
  default is withdrawn; the owner wants simple dummy sounds now, to be replaced later. They
  must be **procedurally generated** — no sampled audio enters the repo, and specifically
  nothing resembling the reference game's impact ping. Placeholder audio *existing* is the
  requirement; whether it sounds right is explicitly not a feel-gate criterion.
- **The bat has a black handle wrap** over the wooden barrel, authored as a second profile
  colour rather than hard-coded in the mesh builder.

## Home-Run Bat Task H Feel Feedback (2026-07-30)

The owner liked the first complete pass ("I love it") and requested four small revisions
before the catalogue visibility decision. These are confirmed behavior; the catalogue stays
hidden until the revised feel is reviewed.

- **Charging may be lowered to the floor.** While GRIPPED or CHARGING, the cursor/handle
  anchor may travel down to the room's ordinary wall clearance. The old symmetric
  swing-arc inset remains at the ceiling and side walls, but no longer creates an invisible
  lower-height limit. The bat capsule still collides with the floor, so placing and releasing
  a low swing is intentionally player skill rather than cursor rejection.
- **Full charge is physically stronger.** `TipSpeedFull` rises from `5500` to `6000` px/s.
  Sweep duration continues to derive from real tip speed (now `7` ticks at full charge);
  pain, payout, and launch still come from measured solver impulse with no damage or
  knockback multiplier.
- **Charge glints are staged.** The barrel tip glints at `120`, `360`, and `600` routed
  charge ticks: `7` px at one second, `12` px at three seconds, and `18` px at the
  five-second cap, each using the existing `0.35` s clean-room star. Only the cap retains
  the `ChargeCompleted` semantic/audio edge.
- **Home-run contact gets a small impact burst.** One accepted home-run epoch starts one
  `18` px, `0.20` s six-ray burst at the solver contact point. It is presentation-only,
  coexists with the existing impact ring and victim shake, and never mutates physics.

## Home-Run Bat Task H Accepted (2026-07-30)

The owner accepted the revised Home-Run Bat feel ("it's great"). Task H and the M5
Baseball Bat slice are closed. `data/catalogue/tool_baseball_bat.tres` now carries
`Visible = true`, so the accepted bat may appear in the shop. Its `20`-credit price
remains the provisional FR-013.4 placeholder until Task 12 economy calibration.

## Cursor-Gun Platform and Pistol (M5 Task 5, 2026-07-31)

Delegated defaults, all agent-tunable and all provisional until the Task 12 economy
calibration and the owner's feel gate:

- Pistol projectile: `2400` px/s muzzle speed, `2.5` px radius, `0.3` mass, no gravity,
  born `14` px ahead of the cursor, `24`-slot pool, `120`-tick maximum lifetime and
  `3000` px maximum path.
- Aim: pointer travel under `1` px per routed tick is jitter and does not re-aim; one
  wheel notch offsets aim by `5` degrees, clamped to `60` degrees either way.
- Laboratory tool key: `J` selects the Pistol (`P` is pause and `G` is Grab).
- The authored magazine `8`, `0.25` s minimum shot interval, `1.2` s reload, `R` reload,
  auto-reload on an empty pull, and unlimited reserve are RAGDOLL §9.2 requirements, not
  delegated choices.

**Engine finding — Godot's 2D continuous collision cannot be used for projectiles, and a
projectile's per-tick travel is bounded instead.** `RigidBody2D.ContinuousCd` avoids
tunneling by *replacing the body's velocity* with the reduced velocity that lands it on
the surface it was about to cross. The shot stops in the right place carrying almost no
momentum, so the solver reports a tiny contact impulse and the shared pain curve scores a
visibly perfect hit as nothing. Measured on one point-blank head shot: pain `85` with CCD
disabled, pain `0` with `CastRay`, and a clean pass straight through the head with
`CastShape`. Coverage is therefore geometric: `GunProfile` rejects any muzzle speed above
`24` px per routed tick (`2880` px/s), which keeps every shot inside the smallest buddy
part's `30` px diameter, so some sample of the flight always overlaps the target and the
ordinary solver resolves the contact at full speed. RAGDOLL §9.2's "physical CCD
projectile" is honored in substance — a shot cannot pass through the buddy — but not by
that engine setting, and `pistol_fire` asserts the outcome rather than the mechanism.

A second consequence, recorded for the Task 12 calibration: because the reported impulse
of a small fast body is dominated by how far it got in one step, **muzzle speed is the
lever on how much a gun hurts and projectile mass is nearly inert**. Mass still decides
how hard the buddy is shoved. Per-shot pain also varies widely with contact geometry
(observed `6`–`100` across hits), which is inherent to the shared impulse pipeline rather
than specific to guns.

## Gun Feel Refinement — Aim-Gated Trigger and Projectile Spin (M5 Task 5 Task A, 2026-07-31)

The owner rejected the engineering-complete Pistol on feel and signed off
`docs/M5_TASK5_GUN_FEEL_AND_REAL_PISTOL_PLAN.md`. Two rules from its Task A are decisions
rather than tuning.

**A trigger press with no established aim does not consume a round.** A cursor gun's aim
comes from pointer travel and is wiped whenever the pointer leaves the play area — which is
what sweeping across it does. The shipped behavior spent the round anyway and launched
nothing, silently: verified 2026-07-31 as a magazine going `6 → 5` rounds with no projectile,
which is the owner's report that "it takes a few clicks before ammo comes out to the left".
The trigger fed to `GunMachine` is now gated on the aim being valid, so the press is refused
instead. Dry fire, reload, and cadence are unchanged; press-and-hold before aiming fires the
moment an aim exists. `CursorGunComponent.ShotCount` therefore counts only shots that really
left the barrel, and `ShotsSpentWithoutAim` is kept as a permanent zero so a regression
announces itself. This retires the previous "the model owns the magazine, so the round is
still spent" reading: a round the player never saw leave the gun is a bug, not a principle.

**Engine finding — a projectile's rotation must stay free, and its visual must not follow
it.** Locking `RigidBody2D.LockRotation` on a projectile looks like a pure presentation
tidy-up and is not: measured A/B on identical seeds with nothing else changed, it **halved
the contact impulse the shared pain pipeline scores** — `1187.4 → 597.8` (pain
`41.32 → 14.16`) on seed 1, `1206.9 → 605.6` on seed 7. A small round body's spin-up is a
real part of the impulse this project measures pain from, so taking it away cuts every gun's
damage in half. Rotation therefore stays free, and the alignment defect the owner reported
("the ammo doesn't line up with the gun, and it rotates while flying") is fixed entirely in
the drawing: the streak is drawn along the velocity the body has at that instant and undoes
the body's own rotation, because a canvas item draws in local space. Any future projectile
visual must be oriented from velocity, never from the body transform. The same measurement
explains the `6`–`100` per-shot pain spread noted above: a bullet that hits square gets no
spin channel and scores about half as much as a glancing one.

## Gun Owner Follow-up — Left Lighting and Nerf Impact (2026-07-31)

- **Left-facing 3D guns use a proper rotation, never a negative-scale reflection.** The
  gun may roll 180 degrees around its barrel axis to keep the grip down, but its rendered
  mesh basis must retain a positive determinant so normals and lighting do not invert.
- **The Nerf Blaster restores the pre-split gun's impact-driving projectile values.** Its
  muzzle speed is `2400` px/s and projectile mass is `0.3`. This supersedes the delegated
  near-zero-pain default: a connected Nerf dart still scores physical pain, knockout, and
  payout through the ordinary shared solver-impulse curve, with no damage multiplier. Its
  mood response is governed by the later owner decision below. The Nerf keeps its chunky
  `4 px` orange dart, `0.15` gravity scale, toy model, magazine, cadence, and reload behavior.

## Gun Mood and Pistol Sadness (Owner decision, 2026-07-31)

- **Only accepted positive-pain Nerf hits count.** Misses and zero-pain contacts do not
  advance tolerance. Hits `1` through `20` in one barrage each grant `+0.25` mood and do
  not create harmful-tool memory. Physical pain, knockout, payout, and statistics remain
  unchanged.
- **The 21st and later Nerf hits are annoying.** Each applies the ordinary pain-sized mood
  loss `min(10, pain x 0.1)` without creating persistent harmful-tool memory. After exactly
  `10` routed gameplay seconds without an accepted Nerf hit, the transient counter resets
  and the next accepted hit is hit `1`. This tolerance is not saved.
- **Every accepted real-Pistol hit is harmful.** It applies the ordinary pain-sized mood
  loss, records `tool.pistol` in harmful memory, and starts a visible sad reaction. The
  authored sad-face window is provisionally `1.5` seconds; the immediate pain face may take
  priority before the sad face becomes visible.

## Gun Slice — Owner Gate ACCEPTED and Promoted (2026-07-31)

The owner played the refined guns on real Windows and accepted the slice, closing
`docs/M5_TASK5_GUN_FEEL_AND_REAL_PISTOL_PLAN.md`.

- **Both guns are on sale.** `tool_nerf_blaster.tres` and `tool_pistol.tres` are
  `Visible = true` at their authored `12` and `30` credits — provisional until Task 12's
  economy calibration, like every other M5 price.
- **The §4.1 aim constants are accepted as authored, not co-tuned.** `AimSmoothingHalfLife
  Ticks` / `MaxAimTurnDegreesPerTick` are `10.0` / `9.0` for the Nerf Blaster and
  `14.0` / `6.0` for the Pistol. Task F existed to build laboratory dials for a co-tuning
  session; the owner accepted the feel without needing one, so no dials were built for
  constants nobody turned. The values stop being provisional and become the authored
  baseline: a later change to them is a change, not a first decision.
- **A promoted tool's shop leg is a real sale, exercised against a fresh progress state.**
  The laboratory grants every implemented M5 tool at boot for mechanical tuning, so asking
  *it* to buy one answers `AlreadyOwned` and proves nothing. `JourneyRunner.BuysFromShop`
  is the shared idiom: entry listed and visible, present in `CataloguePolicy.ShopEntries`,
  and a saveless buyer holding exactly the price ends up owning it with nothing left.

## Grenade — Pin Mechanic, Post-Release Fuse, and Blast (Owner decision, 2026-07-31)

The owner replaced the specified launch-triggered fuse with a pin mechanic while approving
`docs/M5_TASK6_GRENADE_PLAN.md`. This **supersedes** the `2.5`-second launch fuse in
RAGDOLL §9.2, FR-010.3, and the FR-010 tuning table; those three are amended to match.

- **A pinned grenade is inert forever.** Thrown by hand, caught, dropped, or left alone, it
  never goes off — it is a ball, including for the buddy.
- **The pin comes out on the first secondary press**, which is also the pullback's begin.
  There is deliberately no separate arming input: every pullback-launched grenade is live and
  every inert one was thrown by plain grab. The pin is one-way, and a cancelled pullback
  leaves the grenade armed but still safe in the hand.
- **The fuse is `3.0` seconds (`360` routed ticks) and starts when player control ends** —
  launch release or grab release, which are the same event to the grenade. It counts routed
  ticks, so the laboratory's pause holds it exactly as it holds every other clock.
- **Nothing stops a live fuse.** Not a buddy catch, not a player re-grab: it goes off in
  whoever's hand is holding it. (Delegated default 1, taken; the alternative was a re-grab
  pausing it, which reads as safe rather than dangerous.)
- **The blast is an impulse source, not a damage source.** It applies an equivalent impulse
  to each buddy part, shaped only by distance falloff, and feeds it through the *same*
  `PainCurve.PainFor` → `RegisterPain` → `AcceptDamage` chain every collision uses. The
  no-per-tool-damage-multiplier rule is untouched: the curve still owns impulse→pain, and the
  falloff is the only authored blast quantity. Loose objects within reach are shoved and
  nothing else — only the buddy is ever scored.
- **"Five pistol bullets" is read as five solid aimed shots** (delegated default 2, taken), so
  a point-blank grenade crosses the `100`-pain knockout window and knocks the buddy out.
  Measured seeds `1/7/13`: `186.21`–`190.65` total pain at the head over six parts, and
  `223.31`–`225.16` at the hand, against a solid bullet's `40.5`–`42.3`.
- **Buy once, throw forever** (delegated default 3, taken), exactly like the Baseball.
- **The dropped pin is cosmetic**, on the ejected magazine's rules: collision layer `0`, mask
  `RoomBounds` only, never a `LooseObjectRegistry` object, pooled and re-used. A live grenade
  *is* registry-protected until it detonates — the player is owed the explosion they started —
  and its slot is freed when it goes off.

**Owner feel gate, first pass (2026-07-31).** Three changes, none of which touch the fuse or
the pain path:

- **The grenade and the pin are drawn as models.** `GrenadeProfile.VisualScale` (`1.75`) is
  the drawn size against the collider radius, read by the mesh builder and the flat fallback
  alike, on the guns' precedent: a collider sized for how a grenade should *throw* is too
  small to carry a silhouette. The dropped pin had no 3D presenter at all — it drew flat
  canvas art in both modes — and now has `GrenadePinVisual3D`, which takes that drawing over
  in `Mii3D` exactly as the body slot does.
- **The explosion is four layers, not two.** A white-hot core, a fireball that swells on an
  ease-out and cools through flame to smoke, embers thrown on fixed per-index directions, and
  the same shock ring, which still expands to the real full-effect radius because that is the
  one part of the explosion that is a claim about the physics. Ember directions come from the
  index and never from a generator: presentation may not consume simulation randomness.
- **Knockback is doubled, `ShoveImpulseAtCenter` `900 → 1800`.** The shove and the pain
  impulse are separate authored quantities precisely so this could happen: the room now gets
  thrown twice as hard and the buddy is hurt exactly as much as before, because pain still
  comes only from `EquivalentImpulseAtCenter` through the shared curve. Measured: a `1.0`-mass
  witness at `35 px` leaves at `1750.8 px/s`, and point-blank pain is unmoved at `190.65`.

**Owner gate — ACCEPTED (2026-07-31).** The owner played the post-feedback build on real
Windows and accepted the state it is in. `data/catalogue/tool_grenade.tres` is `Visible = true`
and the Grenade sells at its authored `40` credits, provisional until Task 12's economy
calibration like every other M5 price. The `m5_grenade` journey's opening leg changed with it:
it asserted the refusal an invisible entry produces, and now asserts the sale — listed, in
`CataloguePolicy.ShopEntries`, and bought by a saveless buyer holding exactly the price. The
sale is exercised against a fresh progress state rather than the laboratory's, because the lab
grants every implemented M5 tool at boot for mechanical tuning and would answer
`AlreadyOwned`.

## Soccer Ball and Drink — Authored Restitution and the Not-Food Consumable (Owner decision, 2026-07-31)

Accepted in full **before** implementation, from `docs/M5_TASK8_SOCCER_BALL_AND_DRINK_PLAN.md`
§3. The rules below are settled; the feel gate still owns the tuning numbers.

- **The Drink is never refused for a full stomach.** `ConsumeHungerFill = 0`, so a completely
  full buddy still accepts one. The Meal and the Drink are rationed by different things on
  purpose: appetite rations food (owner decision 2026-07-29, the hunger bar), and a 60 s
  per-item timer rations the Drink. The alternative — a small fill so chain-feeding drinks
  eventually meets the refusal performance — was rejected.
- **The Soccer Ball bounces at `0.65` and rolls long** (`LinearDamp` `0.3`, `AngularDamp`
  `0.8`, against the Baseball's `0.8`/`1.2`), tuned to "playground ball". The final number is
  the owner's to move at the feel gate.
- **Both are buy-once, spawn-forever**, like the Baseball and the Grenade: laboratory spawn
  keys `8` and `9`.

**The restitution seam.** `LooseObjectProfile.Bounce` (`0..1`, default `0.0`) is applied
through a `PhysicsMaterial` when the body takes its profile. A profile that authors no bounce
is given no material at all, so the Baseball, Meal, and Grenade are bit-identical to before
the field existed — pinned by the `bounce_zero_objects_did_not_change` check, which drops a
Baseball from `240 px` and requires it to land dead. Measured at implementation, same drop for
both: Baseball `0` rebounds, `0.0 px` peak, `153` ticks to rest; Soccer Ball `6` rebounds,
`82.1 px` peak, `417` ticks to rest.

**Per-launchable pullback tuning.** `LooseObjectProfile.Launch` optionally carries a
`PullbackLauncherProfile` of the item's own; `null` means the launcher's shared preset, which
is what every launchable authored before the Soccer Ball uses, so nothing that did not author
one changed. The Soccer Ball authors `VelocityPerPullPixel 11.5` and `MaxLaunchSpeed 1400`
against the shared `15.0`/`1800`, so a playground ball leaves the hand slower and loopier than
a baseball.

**Still owner-gated.** `data/catalogue/tool_soccer_ball.tres` and `tool_drink.tres` remain
`Visible = false` pending the owner's feel gate; the two entries may flip independently. The
`m5_soccer_ball` and `m5_drink` journeys assert the refusal an invisible entry produces, on
the Grenade's precedent, and become real purchases when the owner flips them.

## Soccer Ball Trap and Kick, the Drink's Single Raise, and Both 3D Models (Owner feedback, 2026-08-01)

Three owner instructions on the Task 8 slice, taken verbatim and implemented as data plus one
pure model each. All numbers below are provisional until the feel gate.

**1. The buddy plays football with it.** Owner: "it should kick the ball away from it, towards
the player. when the soccer ball is rolling towards the buddy, the buddy can stop the ball from
rolling with its foot. then after a second kicks the ball in a random way, so either straight
or angled a bit towards the player."

- The beat is a **sibling** of the catch lifecycle, not a phase of it:
  `Domain/Autonomy/SoccerPlayModel`. `ObjectInteractionModel` is catch → hold → inspect →
  outcome and every phase of it assumes the object ends up in the hands; a trap never picks the
  ball up. Both are priority 5 (RAGDOLL §4) and they never contend, because a ball the trap has
  **reserved** is marked `ObjectCandidate.Ignored` — the existing "leave that one alone"
  channel — so the pickup machinery is untouched.
- **Reservation has no distance term, deliberately.** A ball rolling in from across the room
  belongs to the foot from the moment it starts rolling, preserving the anti-kick collision
  exception until it arrives. The later foot-only correction below removes the Soccer Ball
  from ordinary pickup in every state; a ball above `TrapHeight` is simply not a foot action,
  and a ball rolling away belongs to nobody — which also stops the buddy re-kicking its own
  outgoing kick.
- A reserved ball also takes the existing **anti-kick collision exception**. Measured: without
  it the buddy's own shins knock the ball away at about `39 px`, before it can reach the
  `34 px` trap gate, and the trap never fires.
- The kick direction is **back the way the ball came**, which is away from the buddy and toward
  whoever sent it, lofted by one of `KickLoftChoices` evenly spaced angles from dead flat up to
  `MaximumKickLoftDegrees`. The choice comes from a salted `IRandomSource` stream off the run's
  autonomy seed, so a replayed seed replays the same kick and presentation randomness can never
  perturb it.
- Tuning is authored on the ball alone, `data/objects/soccer_play.tres`, referenced by
  `LooseObjectProfile.SoccerPlay`: trap distance `34`, trap height `30`, approach window
  `40`–`900 px/s`, dwell `120` ticks (one second), kick speed `520 px/s`, loft up to `24°` in
  `3` choices. **No other loose object opts in**, and the scenario asserts that.

**2. The Drink is raised once, not bitten five times.** Owner: raise to the head once, hold two
seconds, then it disappears. The Meal is unchanged.

- The Eat schedule moved to the engine-free `Domain/Presentation/ConsumeGesture`, which serves
  both styles. The `Bites` arithmetic is the M4 schedule restated exactly — same windows, same
  easing, same bite moment — so every measured meal signature is bit-identical, and
  `meal_consume`, `consume_care_cooldown`, and `activity_clips` all stay green.
- `SingleRaise` solves its windows from the authored durations rather than borrowing the bite
  cycle's, because the bite cycle's hold is a third of its length and a two-second hold would
  silently have become two thirds of one.
- Authored per item: `LooseObjectProfile.ConsumeStyle` / `ConsumeRaiseTicks` /
  `ConsumeHoldTicks`. The Drink authors `SingleRaise`, `60`, `240`. The consequence is
  identical either way — the authoritative final step is what completes the care transaction —
  so a Drink still cannot pay twice and a cancelled one still pays nothing (FR-008.10).

**3. Both items are drawn as models.** A general `Presentation3D/LooseObjectVisual3D` adopts any
loose object whose profile authors a `LooseObjectVisualKind`, on the standard
`Body2DVisual3D` attach seam and a pooled slot per object; `LooseObjectMeshBuilder` builds the
shapes on the `GrenadeMeshBuilder` idiom (no imported art, dimensions from the collider radius,
vertex colours, one normal per face, a stated envelope of `1.80 x` the radius). The Soccer Ball
is a smooth white sphere with twelve evenly distributed raised dark pentagons; the Drink is a
red can with a white belly
band and rolled rims. Clean-room: no crest, wordmark, script, or real product's trade dress.
An object authoring `None` — every object that predates this — keeps its flat circle in both
modes, and legacy presentation deactivates every slot, so exactly one silhouette is drawn per
mode.

## Soccer Ball Is Foot-Only and Player-Authored Traps (Owner feedback, 2026-08-01)

- **The buddy never ordinarily picks up the Soccer Ball.** It is excluded from the ordinary
  catch/scoop/hold lifecycle and grants no clean-catch mood reward. The sole exception is the
  explicit good-mood corner rescue below.
- **A trap requires an unbroken player-touch provenance.** Grabbing or launching the ball
  enables trapping. Floor contact preserves that permission. Contact with either side wall
  or the ceiling clears it; the player must touch the ball again before another trap is
  allowed.
- **Losing trap permission does not make the ball inert.** A low, reachable ball that cannot
  be trapped may still receive the existing one-shot soccer kick. This does not add pickup,
  attachment, or a second object-action lifecycle.

## Good-Mood Soccer Chase and Receive Stance (Owner feedback, 2026-08-01)

- **Good mood means the existing Content or Delighted bands.** In either band, a conscious,
  unsuppressed buddy treats a sensed free Soccer Ball as a priority-5 play goal, chases it,
  and kicks it without ever using its hands. Neutral, Wary, and Fearful buddies retain the
  passive foot-only behavior above.
- **Autonomous shots are seeded choices away from walls.** The buddy chooses a straight
  forward kick or a non-zero authored arc using the soccer model's dedicated seeded stream.
  A ball inside the authored wall-turn distance instead enters a deterministic corner rescue:
  pick it up, carry/turn inward away from the wall, place it on the floor in front, then kick
  inward. No other football state permits hand attachment.
- **A player-held ball requests a continuing receive stance.** A Content/Delighted buddy
  continuously keeps its head and rendered eyes on it while travelling and keeps increasing
  horizontal separation for as long as the player holds it,
  alternating `600` routed ticks (five seconds) of retreat with a provisional `120`-tick
  stationary pause. Releasing the ball immediately ends the retreat and restores chase/play.
  Player ownership remains absolute and no collision exception or attachment is installed.
- **Football never requests an obstacle hop.** It is filtered from the ambient obstacle-hop
  evidence only; every other loose object retains the existing hop behavior.
- **Football interest is visibly rendered, not merely selected.** An item look target applies
  its head yaw at full gaze weight even without an unrelated activity clip. Item yaw is applied
  world-relative instead of being added to retreat-facing yaw, which keeps the face readable
  while looking back at a held ball. Item attention uses the existing wide white eyes with dark
  pupils so direction remains visible at the `480x360` desktop scale.
- **Corner rescue retains the same gaze contract.** While carrying the rescued ball inward,
  the buddy continuously watches the held ball; its body movement/facing is inward, away from
  the wall, before the ball is placed and kicked.
- **Provisional feel values:** receive walk `600` ticks, receive pause `120` ticks, wall-turn
  distance `72 px`, turn hold `60` routed ticks. These are Resource-authored and remain part
  of the Task E feel gate.
## Fire Sprayer and Burning (M5 Task 7)

**Owner-accepted 2026-07-31, pre-implementation.** All four rules below were decided before
any code was written, so the implementation carried them out rather than proposing them. The
owner's feel gate still owns the *tuning* — the numbers — but these rules are settled.

1. **A single-part full burn never knocks the buddy out through pain alone.** Even a
   sustained eight-second cap burn peaks below the `100`-pain rolling window. Implemented and proven rather than
   assumed: `BurnEquivalentImpulse = 430` scores `4.57` pain per event against the shipped
   conversion profile, so a rolling five-second window holds at most ten events — `45.7`.
   A four-second burn totals `36.6` and a sustained cap burn `73.1`.
2. **The sprayer has no ammunition, heat, or duration limit.** Primary may be held forever.
   The specification is silent for the sprayer and a fuel gauge would be new UI for no
   requested reason, so the tool is authored with no magazine, no reload, and no press edge —
   which is also why it is *not* a `GunProfile`: `GunMachine` is a press-edge cadence and
   magazine machine, and forcing the sprayer through it would mean authoring a fake capacity
   nobody ever sees.
3. **Fire does not spread.** Only the buddy burns. Objects, walls, and the room are not
   flammable, and a burning buddy ignites nothing it touches.
4. **The stream pushes nothing.** Droplet mass is cosmetically tiny and droplets collide with
   `RoomBounds | BuddyParts` only, so they cannot disturb a loose object or each other. The
   sprayer harms through Burning and has no knockback lane at all.

**Burning is the only harm lane.** A droplet's buddy contact does exactly two things: it
refreshes the burn and it records which part is alight. It never reaches the contact pipeline
as an impact source, so a stream can never double-dip as both impact pain and burn pain — the
`burning_status` scenario asserts that every accepted `tool.fire_sprayer` event carries the
burn's own interaction id. Pain arrives only on the burn's own cadence, through the same
contact-free `ApplyBlastImpulse` entry the grenade blast uses, so the shared curve, the
knockout window, the payout, the harmful memory and the `min(10, pain x 0.1)` mood loss are
untouched machinery. One burn is one interaction id, re-minted when a lapsed burn reignites.

**Panic is one snapshot bool.** `BehaviorPriority.Hazard` was already reserved and plumbed, so
Burning sets `BehaviorSnapshot.HazardPresent` and the existing ladder does the rest: priority
`3` outranks `ObjectAction`, so a committed catch or eat aborts through the existing
higher-priority abort — which *is* "drops held items" — and stays below `Unconscious`, so a
knocked-out burning buddy lies there and burns. No new behavior system exists in this slice.

**Owner feel-gate rule 2026-08-01: all six parts alight for five continuous seconds forces
unconsciousness until the fire subsides.** The threshold is `600` routed ticks. It is a
separate status hold, not an invented pain multiplier: the shared rolling-pain model remains
unchanged, and the fire hold cannot wake the buddy early if its ordinary knockout is still
active. At `599` ticks the buddy remains conscious; tick `600` forces unconsciousness; natural
burn expiry or explicit fire cleanup releases only this hold.

**Scorch propagates from an endpoint to its own visual connector only.** A scorched hand
darkens its adjacent arm, a foot its leg, and the head its neck. Torso scorch alone does not
darken every connector, so there is no torso-to-whole-rig propagation.

**The FR-017.3 effects seam ships here, not in the M7 accessibility pass.** `ProgressSave`
already carried `ReducedMotion`, `ScreenShake`, `ReducedParticles` and `PhotosensitivitySafe`
with nothing reading them. This slice adds the `EffectsSettings` snapshot the composition root
hands to presentation components, because a shipped effect that ignores the setting is exactly
what FR-017.3 forbids. Two consequences worth recording:

- **Gameplay never reads it.** The `burning_status` scenario sprays one pinned pose twice
  under the permissive and the most restrictive settings and asserts identical events, pain,
  mood and droplets, with only the drawable-droplet count differing. Determinism must not vary
  with accessibility.
- **`ScreenShake = false` now silences the whole `CameraKickComponent` lane**, pistol and
  grenade kicks included. Shipping the seam while leaving the one existing shake setting dead
  would have been absurd; it is flagged here rather than done silently.

**Selection key `S`, not `H`.** The Task 7 plan suggested `H` from what it believed was the
free map. `H` already toggles the laboratory telemetry panel, and one key doing two unrelated
things is the kind of collision that only surfaces half-way through a tuning session.

**Owner feel gate, first pass (2026-08-01).** The owner played the slice in the laboratory
and accepted the mechanics and the timing as they stand; the feedback was entirely about how
it looks. Three changes, none of which touch the droplet physics, the fan geometry, the
ignition path, or the burn economy — `burning_status`'s measured numbers are unmoved:

- **A real flamethrower model.** `SprayerMeshBuilder` builds a clean-room silhouette on
  exactly the guns' vertex-coloured-box idiom, and `CursorSprayerVisual3D` follows the cursor
  and the aim the way `CursorGunVisual3D` does, including the determinant-positive roll for a
  left-handed aim. It reads apart from the two pistols by shape rather than by colour — a fat
  pressure canister slung behind and above the grip, a slim wand running well forward, a
  flared nozzle ring, and a pilot-light bead. The flat silhouette still carries legacy mode,
  and only one of the two is ever drawn.
- **The stream is a mist, not a row of pellets.** Each live droplet now carries a stack of
  soft, semi-transparent puffs in legacy and one additive billow in 3D, born small and hot at
  the nozzle and swelling and cooling toward `SmokeColor` as it ages, so overlapping droplets
  blend into one smoky column. `MistSpreadFactor` is a drawn size only: the collider is still
  the authored `1.5 px` circle, so the weapon looks like fire and hits exactly as it did.
- **Progressive per-part scorch.** A part in the stream darkens toward `ScorchColor`, and the
  longer it burns the darker it gets, up to an authored `MaxScorchDarkness` of `0.72` — below
  one on purpose, because a fully black limb reads as a hole in the buddy rather than a burnt
  one, and because the buddy cannot be permanently damaged. The mark then **holds for 10 s and
  fades over the following 5 s**, both authored. The rules are real state, so they live in
  `ScorchStateModel` in Domain with their own unit table, and `ScorchPresenter` is a thin
  driver that writes through the channels that already decide a part's skin colour: the
  per-part lit material the library gives every mesh its own instance of, and the legacy
  circle's drawn fill. It is per part, not per buddy — a stream that moves from a hand to the
  head leaves two marks at different strengths. Nothing gameplay reads it, the outline shell
  and the pose pipeline are untouched, and the fail-safe hard reposition wipes it on the same
  `Clear()` entry point that puts the burn out.

**Feel gate outstanding.** `data/catalogue/tool_fire_sprayer.tres` stays `Visible = false`
until the owner plays it on real Windows, so the `m5_fire_sprayer` journey's catalogue leg
asserts today's real promise — carried at its authored price, not advertised — and flips to a
sale by editing one authored flag.

**Owner feel gate, second pass (2026-08-01).** The owner kept the mechanics and requested a
presentation-only revision: the fuel canister must read clearly, and the emitted fire must be
a large foamy cloud rather than discrete puffs. The stream now uses a procedural shader to
blend a hot core into smoky breakup, follows the existing physical forward stream, and gains
presentation-only upward vapor lift. The model carries a separate cylindrical fuel canister.
Ignition geometry, droplet physics, burn timing, pain, payout, and panic remain unchanged.

**Owner feel gate, third pass (2026-08-01).** The separate fuel canister was oversized and
intersected the box-built tank, exposing a red blob only when aiming right. The duplicate tank
volume is removed; one smaller neutral rounded canister now sits on the weapon's zero-depth
plane and remains symmetric under the existing determinant-positive left roll.

Burning presentation now uses the stream's procedural fire/smoke shader on every part touched
during the current burn episode. Two hot puffs stay on each lit part while older puffs rise and
cool into a smoke trail. A touched part remains visually alight until the burn ends; this does
not change which most-recent part receives the burn's attributed pain event. While Burning owns
the hazard layer, locomotion is authored at `1.35x` and both free arms reuse the accepted
grab-resistance panic-flail arc at full strength. No new behavior or damage lane exists.
## Shotgun — Even Fan, Coverage Damage, and the Shared Shot Identity (M5 Task 9, 2026-07-31)

The owner accepted all three of `docs/M5_TASK9_SHOTGUN_PLAN.md` §3's defaults on 2026-07-31,
**before** implementation, so they are rules rather than proposals. The Shotgun's authored
contract is unchanged from §9.2 above: `6` pellets, `5` shells, `0.9 s` cadence, `2 s` reload,
unlimited reserve.

- **The pellet fan is even and deterministic, not random scatter.** The platform already fanned
  a multi-projectile shot across `SpreadHalfAngleDegrees` by index fraction, and that is now the
  recorded rule: a replayed seed reproduces a shot exactly, and a scenario can state where every
  pellet went. (The alternative considered and rejected: seeded per-shot jitter drawn from the
  simulation's random source.)
- **One shot into one part scores once.** Every pellet of one trigger pull carries **one**
  interaction identity, so the impact router's `(SourceInteractionId, TargetPartId)` episode key
  makes six simultaneous pellets on one part a single contact episode. This is the recorded
  interpretation of the §7.1–7.2 dedup rules for spread weapons, and it has a consequence worth
  stating plainly: **point-blank damage into a single part is one pellet's worth, not six.** A
  shotgun's damage comes from *coverage* — pellets across `N` parts open `N` episodes and score
  `N` times — so mid-range against a spread-eagled buddy out-damages a point-blank shot into a
  fingertip, and a knockout needs two committed bursts. (The alternative considered and
  rejected: per-pellet identities, which would make it a six-fold point-blank one-shot weapon.)
  Single-projectile guns are untouched: they pass no shared identity and mint one per launch
  exactly as they always have.
- **A reload ejects a cosmetic shell** on the existing dropped-magazine lane — pooled, on no
  collision layer, masked only against the room bounds, never a loose object, and never able to
  touch the buddy. It is authored as the magazine visual for now, which §3.3 permits.

Two engineering values were set by measurement during implementation and are recorded here
because both are load-bearing rather than taste:

- **`ContactSettleTicks` is `4`, not the Pistol's `2`.** At `2`, a pellet that had connected was
  taken out of the world before the solver resolved the real impulse, and a burst the player
  watched land delivered nothing at all. Point-blank shots happened to survive it; everything
  past arm's length did not.
- **`ProjectileMass` is `0.20` at `2200 px/s`,** tuned against the shared curve to the plan's
  §2.3 pain target and nothing else. Measured on seeds `1/7/13`: one solid pellet `7.2–9.1`
  pain against a point-blank pistol bullet's `13.8–13.9`, and a two-part burst `9.0–26.0`. There
  is still no per-tool damage multiplier anywhere.

`PoolCapacity` is `36` rather than the plan's suggested `24`: `GunProfile.Validate` already
requires the pool to cover a whole magazine in flight, which for this gun is `5 x 6 = 30`.

**Owner gate: NOT YET PLAYED.** `data/catalogue/tool_shotgun.tres` stays `Visible = false` at
its provisional `100` credits until the owner plays the slice, and the `m5_shotgun` journey
asserts that refusal — the shape the Grenade's leg had before its own acceptance.

## Shotgun Owner Feedback — Scatter, Pump, Shells, Stock, and Knockback (2026-08-01)

This owner feedback supersedes the Task 9 even-fan and reload-ejected-shell choices above.

- Every shot selects a new seeded-random spread half-angle between `12°` and `20°`; each of
  its six pellets independently selects an angle inside that shot's cone. Seeded runs remain
  replayable, but successive shots no longer repeat one fixed ladder.
- Every fired shot ejects one pooled cosmetic red shotgun shell. Reloading ejects no magazine.
- The primary click after a shot cycles the pump over `24` routed ticks and chambers the next
  shell; that click cannot fire. The forend follows the stroke in both presentation modes.
- Each pellet adds distance-falling knockback without changing pain. At point blank the six
  authored `600` impulses total `3600`, twice the Grenade's `1800` center shove, when all
  connect. The extra
  shove reaches zero by `260 px`, leaving the projectile's former physical contact response as
  the minimum, never reducing it.
- The procedural Shotgun stock is doubled lengthwise behind the cursor and gains restrained
  receiver/butt details; no imported model or new asset dependency is introduced.

The owner played this revision on 2026-08-01, liked the changes, and doubled only the maximum
point-blank knockback from `300` to `600` per pellet. Falloff distances and the physical-hit
floor are unchanged.

## Repair Kit — Contact Application, No Rationing, and What the Clears Touch (M5 Task 10, 2026-08-01)

Implemented from `docs/M5_TASK10_REPAIR_KIT_PLAN.md`, whose four §3 defaults the owner accepted
in full on 2026-07-31, before implementation:

- **A player-thrown kit applies on its first buddy-part contact** — pullback-launched or
  grab-flung. This is the mechanism FR-008.7 and FR-010.10 are satisfied by, not a plan
  proposal: a knocked-out buddy is priority `1` and a burning buddy flees at priority `3`, so
  the two buddies a Repair Kit exists for are exactly the two that can never pick one up and
  eat it. Buddy consumption stays available for a calm, conscious buddy, and both routes
  converge on the same application so they cannot drift.
- **The kit's own impact never scores pain or harmful memory.** Its contact is consumed by the
  care path before the impact pipeline resolves a source. A medkit that bruised would enter
  itself into harmful memory and teach the buddy to flee the thing that heals it.
- **Applying to a healthy, calm buddy still works and still pays `+20`.** There is no "nothing
  to repair" refusal.
- **Buy-once, spawn-forever**, on spawn key `0`, like every other launchable.

Two consequences settled during implementation:

- **The clears take scorch with them.** The kit calls `FireSprayerComponent.ClearBurning`, the
  same entry point the fail-safe reposition uses, which puts out the fire and wipes the soot by
  an explicit contract. Soot that survived being patched up would read as the repair not having
  worked. A second entry point that spares scorch is the alternative if the owner ever wants
  one; nothing else would have to change.
- **The knockout is untouched.** `ClearRollingPain` empties the pain events and cannot reach
  the knockout end timestamp. Measuring it also settled that rolling pain during a knockout is
  always zero — `EnterKnockout` empties the window and unconscious hits never enter it — so what
  the kit clears mid-knockout is nothing, and what it stops is the buddy accumulating toward
  the *next* one.

**Superseded:** the `120`-second Repair Kit cooldown carried by RAGDOLL §8.2's care table and
§9.2's tool row until this slice. Both rows are amended; the older DECISIONS bullet of
2026-07-25 stays as history, superseded by the 2026-07-29 owner decision recorded above, the
same way the grenade fuse supersession was recorded. FR-008.6 was already amended.

## Power Grab, 16-Tool Catalogue, 209-Minute Economy, and Full Progress Reset (Owner decision, 2026-08-02)

This entry supersedes the 2026-07-25 passive **Strength Upgrade** decision and resolves every
owner question that blocked M5 Tasks 11–13.

### Catalogue and identity

- **The Nerf Blaster remains launch content.** The launch catalogue contains exactly
  **16 selectable interactions**; there is no passive-upgrade-only entry.
- **Power Grab replaces the unimplemented passive Strength Upgrade concept.** It is a
  one-time permanent shop purchase and a separately selectable inventory tool. Normal Grab
  remains a starting tool and remains selectable after Power Grab is purchased.
- The shipped stable identity is new: `ToolId.PowerGrab` appended after every existing
  ordinal and content ID `tool.power_grab`. Do **not** repurpose `upgrade.strength`.
  That hidden pre-release placeholder becomes a deprecated migration alias only; a schema
  migration maps any development save that owns it to `tool.power_grab`, and new saves
  and new writes never emit it.
- The exact purchasable display/progression order is:
  `Baseball → Baseball Bat → Meal → Nerf → Pistol → Soccer Ball → Grenade →
  Fire Sprayer → Power Grab → Repair Kit → Shotgun → Drink`.

### Power Grab product contract

- Power Grab uses the same acquisition, pointer controls, target eligibility, secondary
  cancel, and limb-stretch maximum as Normal Grab.
- It applies its extra authority to buddy parts **and** eligible loose objects.
- It feels dramatically stronger but remains controllable: pull stiffness/authority and
  the force ceiling increase by Resource-authored, laboratory-calibrated factors.
- A limb held beyond the normal stretch limit remains bounded at that same safe limit and
  continues its visible strain response, but it never reaches the Normal Grab forced
  snap/release outcome. Only player release/cancel or centralized safety recovery ends it.
- Fear resistance remains physically generated and visibly expressed. Power Grab overpowers
  the outcome; it does not suppress the buddy's fear or struggling.
- Intentional release scales the held body's velocity and uses a separately calibrated,
  higher safe cap. This applies to buddy parts and loose objects. Cancel/recovery releases
  do not become powered throws.
- Power Grab has no direct damage, payout, mood, statistics, or hidden economy multiplier.
  Any extra pain or earnings arise only from later physical contacts through the shared
  impact/pain/reward pipeline.
- Exact force factors, release factor, and safe cap are delegated laboratory tuning. They
  are accepted only after quantitative regression and an owner side-by-side Normal/Power
  Grab feel pass.

### Economy order and cumulative targets

The completionist reference buys in the order above. Target times are cumulative running
minutes:

| Item | Target | Gap from prior | Class |
| --- | ---: | ---: | --- |
| Baseball | 3 | 3 | regular |
| Baseball Bat | 7 | 4 | regular |
| Meal | 13 | 6 | regular |
| Nerf | 21 | 8 | regular |
| Pistol | 41 | 20 | high value |
| Soccer Ball | 52 | 11 | regular |
| Grenade | 76 | 24 | high value |
| Fire Sprayer | 104 | 28 | high value |
| Power Grab | 120 | 16 | regular |
| Repair Kit | 138 | 18 | regular |
| Shotgun | 184 | 46 | high value |
| Drink | 209 | 25 | regular |

Pistol, Grenade, Fire Sprayer, and Shotgun are the four high-value tools. Their normal
escalating gap is doubled immediately **before** the purchase. The resulting completionist
target is `209` minutes; the former two-hour/120-minute catalogue close is superseded.

The shop has no prerequisite chain. A player may save for and buy any visible affordable
item, leaving cheaper entries unowned. Calibration therefore runs:

1. the completionist in-order strategy above, which alone is judged against each target;
2. save-for-preference strategies for high-value items and other later entries, which prove
   unrestricted skipping, correct spend, and stable ownership without pretending their
   actual purchase times must match the completionist table.

The representative session is a casual player across `209` running minutes:
approximately `120` minutes actively interacting and `89` minutes background/passive.
Active play includes experimentation, care/non-paying interactions, ordinary misses,
pauses, and non-optimal hits; it is not continuous optimized attacking. Completionist
median purchase times must land within `±15%` of each target across the approved fixed
seed set. Peak passive earnings remain approximately `25%` of benchmark active attack
earnings. Prices remain whole displayed credits and there is no per-tool payout factor.

### Reset Progress

- **Reset Progress means a complete fresh gameplay save.** It clears balance, all purchased
  ownership, current selection (back to Normal Grab), mood, fullness/hunger, harmful and
  learned buddy memory/state, fun/novelty state, generated buddy traits, local gameplay
  statistics/counters, achievement-progress counters, and cumulative run/active/hidden
  time. Fresh traits and other new-save values are produced through the existing new-save
  factory rather than zero-filled ad hoc.
- It preserves local preferences: language, audio, controls, accessibility/comfort,
  presentation, window position/size, zoom, and dock layout/state.
- Already-awarded platform achievements remain awarded. Only their local progress counters
  reset; the game does not call a platform-wide achievement clear.
- The operation is reachable only through a destructive confirmation dialog that names the
  erased categories, defaults focus to Cancel, and emits an explicit confirmed result.
  Dismissal, Escape, missing confirmation, or save failure leaves progress unchanged.
- A confirmed reset uses the normal atomic persistence boundary and immediate flush. The UI
  never mutates individual progress fields or calls a second reset path.

## M5 Tasks 11–13 implementation decisions (2026-08-02)

Delegated choices made while implementing packets 11A–11E of
`docs/M5_TASK11_TO_13_HANDOFF_PLAN.md`. Product rules are unchanged; these are the
implementation-level calls the plan left to the agent, plus three places the plan's
instructions did not match the shipped code.

### Provisional `PowerGrabProfile` values (`data/buddy/power_grab_profile.tres`)

| Export | Provisional | Why |
|---|---:|---|
| `StiffnessMultiplier` | `2.5` | secondary knob; the tether is force-clamped on any real drag |
| `DampingMultiplier` | `1.58` | `√2.5`, which holds the PD damping ratio constant |
| `MaximumForceMultiplier` | `3.0` | the knob the player actually feels |
| `ReleaseVelocityMultiplier` | `1.6` | throw feel |
| `ReleaseSpeedCap` | `1300.0` | 10.8 px/tick at 120 Hz, 68% of a 16 px wall |

These five are the owner feel gate's only knobs. Two constraints bound them:

- **Damping scales as `√(stiffness multiplier)`, not linearly.** The tether's damping ratio
  is `c / (2√(k·m))`. The shipped `lab_grab_tether.tres` is `Stiffness = 700`,
  `Damping = 35`, so the 2.5-mass torso sits at ratio `0.418`; `×2.5` stiffness with `×1.58`
  damping holds it at `0.418`. Scaling stiffness alone makes Power *less* damped than
  Normal — more overshoot, the opposite of "controllable". Move the two together.
- **`ReleaseSpeedCap` has a hard ceiling of 1900 px/s**, enforced in
  `PowerGrabProfile.Validate`. Room walls are 16 px and the tick is 120 Hz, so 1920 px/s
  clears a wall per step, and grabbed parts run with CCD disabled. Raising it past 1900
  requires adding CCD to the grabbed part, which is a different task.

**Correction to the handoff plan's note 3.** The plan derived its force numbers from the
`GrabTetherProfile` class defaults (`Stiffness = 220`, `MaximumForce = 6000`). Every shipped
scene loads `lab_grab_tether.tres`, which authors `700` and `18000`. The effective Power
clamp is therefore `54 000`, not `18 000` — 3× what the plan's arithmetic assumed. The
multiplier is left at `×3` as the provisional the owner judges, but the feel gate should
expect it to be strong; the note-2 damping rule is unaffected, since it is a ratio.

### Deviations from the plan's instructions

1. **`tool_power_grab.tres` is `Kind = 1`, not the plan's `Kind = 0`.** `Kind` is
   `CatalogueEntryKind`, where `0` is `StartingTool` — a free tool owned on every new save.
   `tool_grab.tres` uses `0` because Normal Grab *is* a starting tool. Power Grab is
   `PurchasableTool`; `0` would have made it free and broken the starting-set validation.
2. **`GrabTether.CapReleaseVelocity` gained a non-finite-velocity guard.** The plan's 11B-3
   said to assert an existing guard; the only guard was on the cap argument, so a NaN or
   infinite velocity propagated straight into the released body's position. It is now a dead
   drop (`Vector2.Zero`). This is a fix in the shared function, so it covers Normal Grab too.
3. **Throw attribution follows the selected tool (11C-5).** `SandboxRoot.OnGrabReleased`
   attributed every player throw to `tool.grab`; it now attributes to the selected grab
   variant's content ID, since the per-tool statistics dictionaries are keyed by tool and a
   Power throw filed under Normal would be a real event under the wrong key.

### Catalogue visibility (11D-2)

Soccer Ball, Fire Sprayer, Shotgun, and Drink were `Visible = false` despite owner
acceptance on 2026-08-01; making them shop-visible is the overdue post-acceptance step.
**Repair Kit is shop-visible ahead of its own feel gate** so Task 12 can price the full
twelve-item schedule. If the owner would rather it stay hidden until then, its journey leg
goes back to asserting the refusal and 12D must be re-run after acceptance.

Five journey legs that asserted "not on sale until the owner gates it" became sale legs
through the existing `BuysFromShop` helper. No assertion was deleted; each now asserts the
purchase the slice always owed.

### Lab shortcut

Power Grab is selected in the laboratory with **Shift+G** (`LaboratoryControlComponent` and
`LabPointerGrabComponent`). Every unshifted letter that could stand for "power" was already
bound, and Shift+G reads as "the same tool, more behind it". `BuddyLab` grants
`tool.power_grab` alongside the other implemented M5 tools so the key works at the feel gate.

### Deferred

The confirmation *modal* for Reset Progress (13A-2b) ships with
`docs/UI_FLOATING_DOCK_PLAN.md` Task 7 and binds to the armed tray event; there is no shop
UI or dock in this repo to put one in today.

## M5 Task 12 â€” Economy calibration (2026-08-02)

Implemented from `docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` Â§4. Evidence:
`.artifacts/quick/economy_calibration/economy_benchmark.{json,md}`, produced by the
`economy_calibration` scenario (quick-suite step 40) over seeds `1/7/13/29/101` and all
seven strategies. Economy fingerprint at acceptance: `928231c3ec21e973`.

### Final calibrated values

The three authoritative knobs, all Resources â€” no price or rate literal exists anywhere
else in the build:

| Resource | Value | Was |
|---|---:|---:|
| `lab_pain_conversion.tres` `CashPerPain` | `0.018` | `1.0` |
| `m4_mood_economy.tres` `NeutralCreditsPerMinute` | `0.64` | `1.0` |

| Purchasable | Price | Was | Target | Median | Deviation |
|---|---:|---:|---:|---:|---:|
| `tool.baseball` | 7 | 3 | 3 min | 3.36 | +11.8% |
| `tool.baseball_bat` | 20 | 20 | 7 min | 7.24 | +3.4% |
| `tool.meal` | 16 | 6 | 13 min | 13.59 | +4.5% |
| `tool.nerf_blaster` | 22 | 12 | 21 min | 21.69 | +3.3% |
| `tool.pistol` | 70 | 30 | 41 min | 43.26 | +5.5% |
| `tool.soccer_ball` | 27 | 65 | 52 min | 52.18 | +0.4% |
| `tool.grenade` | 80 | 40 | 76 min | 76.28 | +0.4% |
| `tool.fire_sprayer` | 80 | 50 | 104 min | 107.34 | +3.2% |
| `tool.power_grab` | 37 | 105 | 120 min | 120.01 | +0.0% |
| `tool.repair_kit` | 50 | 120 | 138 min | 137.25 | -0.6% |
| `tool.shotgun` | 133 | 100 | 184 min | 184.28 | +0.2% |
| `tool.drink` | 69 | 80 | 209 min | 200.68 | -4.0% |

The shipped prices were each set when their own tool landed, against no schedule, so the
plan's expected full re-pricing pass is what happened. `CashPerPain = 1.0` was never a
calibrated number either: at that coefficient one good hit paid 120 credits â€” more than the
whole shipped catalogue's twelfth item.

### How the numbers were derived

Prices are not guesses and were not hand-smoothed:

1. `CashPerPain` and `NeutralCreditsPerMinute` were solved together from the two rate
   obligations â€” peak-mood passive at 25% of the measured active rate, and a total
   209-minute income near the shipped catalogue's scale â€” then rounded to two authored
   figures (`0.018`, `0.64`).
2. Each price started as the median cumulative income between its Â§1.1 target and the one
   before it, measured by replaying truncated traces through the real ledger.
3. Two rows were then iterated to convergence, earliest-first, as Â§4.4 prescribes:
   `tool.baseball` (6 â†’ 7) and `tool.drink` (124 â†’ 69).

**Why the last item is the cheap one.** The Drink's slot ends exactly where the trace ends,
so it has no slack: at 124 credits only two of five seeds ever reached it. Its price is set
by the *slowest* seed's total income, not the median, because obligation 5 requires the
completionist to finish all twelve on every seed. Any item targeted at the end of the
session inherits this; it is a property of the 209-minute schedule, not of the Drink.

### Delegated implementation calls

- **The runner is `BuddyProgressState`-driven.** Â§4.1 lists `ImpactRouter` â†’ `PainCurve` â†’
  `RewardLedger`, `PassiveIncome`, and `CataloguePolicy.EvaluatePurchase` + the atomic
  spend. Those are exactly the parts `BuddyProgressState` already composes, so the benchmark
  drives that one aggregate rather than re-wiring the same five types beside it. There is no
  payout arithmetic in `Economy/Benchmark/` at all.
- **Contact source identity is the tool.** A `BenchmarkEvent` carries no instance id, so the
  router's episode key uses an FNV-1a hash of the content ID â€” a stable hash, because
  `string.GetHashCode` is randomized per process and the report must be byte-identical
  across runs.
- **Time advances in 1-second slices**, matching the shipped `ForegroundUpdateSeconds`
  cadence, so mood drift and the mood-scaled passive rate stay coupled the way
  `LifecycleCoordinator` couples them.
- **The trace alternates short segments** (3â€“9 active minutes against 2â€“7 background) and
  opens with 1â€“3 background minutes. Long segments made the median income curve lumpy and
  produced inverted opening prices; the short-burst shape is also what a desktop buddy
  actually gets.
- **`power_grab_preference` skips the Meal.** Â§4.3 requires it to leave an earlier regular
  unowned at the end. Re-ordering alone cannot do that â€” with the schedule calibrated, every
  permutation finishes all twelve by 209 minutes â€” so the strategy omits one earlier regular
  outright.
- **The trace is tool-agnostic.** Which tool a contact is attributed to changes harmful
  memory and per-tool statistics keys, never the payout, so one trace serves every strategy
  regardless of what that strategy owns.

## M5 owner gates accepted (2026-08-02)

The owner accepted four of the five M5 exit gates on 2026-08-02:

| Gate | Evidence accepted |
|---|---|
| Repair Kit feel (Task 10) | ACCEPTED |
| Power Grab feel (Task 11) | ACCEPTED — the five `power_grab_profile.tres` values are now final, not provisional |
| Economy pacing report (Task 12) | ACCEPTED — `.artifacts/quick/economy_calibration/economy_benchmark.{json,md}`, five seeds × seven strategies, fingerprint `928231c3ec21e973` |
| Catalogue (Task 13) | ACCEPTED — sixteen interactions, twelve purchasable, final calibrated prices |
| Windows 10/11 standalone matrix | ACCEPTED by owner attestation — signed off without the reset row, which is unreachable until the dock ships (see below). No matrix artifact was produced in the implementing session; this is the owner's own verification, recorded on their statement. |

**Milestone 5 is closed on these five gates.**

### The FR-003.2 dock does not block M5 exit (owner decision, 2026-08-02)

The retractable tool/shop/settings panel moves **out of the M5 exit criteria** and becomes
the next scheduled work item after M5, ahead of or alongside Milestone 5.5 (the owner's
ordering call). Rationale: every service the dock consumes — catalogue, purchase, selection,
reset — is implemented and tested; the dock is the presentation of them, and it is still in
design approval (its clean-room direction is unapproved, which would otherwise block M5 on a
design gate rather than on gameplay).

Consequences to hold onto:

- **Reset Progress has no player-facing route in the shipped build until the dock lands.**
  The service and the armed tray seam exist and are tested; nothing in the UI reaches them.
  This is a known, accepted gap at M5 exit, not an oversight.
- FR-003.2 remains a requirement in full; it is unimplemented and now tracked by the dock
  plan's own milestone.

### Reset Progress lives in the dock's Settings menu (owner decision, 2026-08-02)

The dock carries a **Settings** button; the settings menu it opens carries the **Reset
Progress** button. That is the only route to a reset. Consequences:

- **No debug hotkey and no tray menu item.** The `TrayCommandComponent` arm/confirm seam
  stays the mechanism, but the *player-facing trigger* is the settings button alone. Nothing
  reaches the reset in a build without a dock.
- **The Windows 10/11 gate is signed off without the reset row**, because that row is
  unreachable until the dock ships. Reset's Windows verification moves to the dock's own
  acceptance.
- This also discharges the M5 plan's standing note that the dock plan must be revised to
  include the required Settings surface.

## M5 Task 13 — Reset, composition audit, and M5 exit (2026-08-02)

Implemented from `docs/M5_TASK11_TO_13_HANDOFF_PLAN.md` §5. Delegated implementation calls:

### Reset rewrites the state in place instead of swapping the reference

The plan's 13A-1 said to build a fresh `BuddyProgressState` and re-point every holder of the
old one at it. Seven live objects hold that reference (`EconomyService`, `SaveCoordinator`,
`InteractionDamageComponent`, `LifecycleCoordinator`, `BehaviorArbiter`,
`ObjectInteractionComponent`, and the root itself), and a re-pointing pass has to be correct
in all seven forever — a re-bind that is forgotten shows up as a presenter quietly reading a
dead save.

`BuddyProgressState.Adopt(ProgressSnapshot)` installs a whole payload over the existing
instance instead, through the same private `Apply` the constructor uses, so construction and
reset cannot drift. The reset still builds its fresh state with the shared first-run factory
(`ProgressReset.CreateNewProgress`, moved out of `Bootstrap` and called by both) and adopts
that snapshot, so a reset player and a new player are made the same way. Consequences:

- No re-binding code exists, and the "no service holds a pre-reset state" assertion (13D-3)
  is structural rather than a test that has to keep up with the composition root.
- **Rollback is exact.** The prior snapshot is adopted back when the write throws, so a
  failed reset leaves memory and disk byte-identical. The save file is never deleted.
- The HUD needs one nudge, since a reset is neither a spend nor a deposit:
  `EconomyService.NotifyBalanceChanged()`, called only on the success path.

### The two-step confirmation lives on `TrayCommandComponent`

`RequestResetProgress()` **arms** and raises `ResetProgressRequested` (the dock modal's cue);
`ConfirmResetProgress()` inside a 30-second window raises `ResetProgressConfirmed` and returns
`true`. A lapsed window, `CancelResetProgress()`, or any other tray command disarms it. Cancel
is therefore the default with or without a dialog, and the contract is assertable today. The
modal copy itself remains deferred to `docs/UI_FLOATING_DOCK_PLAN.md` Task 7, as recorded
above.

### Composition audit findings (13B)

- `ValidateLaunchCatalogue` now also rejects a non-ownable entry, a non-selectable entry, two
  entries selling the same `ToolId`, and any appearance of `upgrade.strength`.
- One hand-maintained tool list existed: `BuddyLab` unlocked twelve content IDs by name for
  laboratory tuning. It now derives from `CataloguePolicy.SelectableEntries`. The remaining
  `ToolId.Grab/Pet/Tickle` references are per-tool behaviour switches and the laboratory's
  dev keymap — not catalogue lists, and left alone.
- `boot_smoke` gained two checks: all three composition roots wire the same
  `power_grab_profile.tres`, and the sandbox sells from the same catalogue instance the
  startup validator checked.
- `upgrade.strength` survives only in `ContentIds`, the v5→v6 migration, migration tests, and
  the two guards above.

### 13D-2 found a real allocation on the Power path

`GrabTetherController.TryGrab` called `PowerGrabProfile.Validate()` on every acquisition,
and `Validate` builds an error list: 120 bytes per Power grab, against 0 for Normal. The
verdict is now cached per profile instance (`IsUsablePowerProfile`), since an authored
Resource does not change under a running game; a different profile revalidates, and the
freed-instance check still runs every time. Measured by the new `power_grab` check: `0 B`
over 240 grab/drag/release cycles on both paths.

Two 13D-2 bullets are structurally true and deliberately not asserted: Power adds no
physics query (`TryPick` is untouched — the profile only changes force numbers), and the
tool-change subscription cannot duplicate, because it is subscribed in `_Ready` and
unsubscribed in `_ExitTree` of the same per-scene component instance. The orphaned-body
bullet **is** asserted, since that one can regress silently.

### Localization keys, not a localization catalogue

The 13E row asking for `shop.tool.power_grab.*` plus the reset-modal keys in "the
localization catalogue" has nothing to add them to: this build has no translation file at
all — no CSV, no `.po`, no `internationalization/locale/translations` setting. Keys are
declared where they are used: `tool_power_grab.tres` already authors
`shop.tool.power_grab.name` / `.description`. The reset-modal keys are named by
`docs/UI_FLOATING_DOCK_PLAN.md` Task 7 and are authored with the dialog that displays them.
Standing up a translation catalogue for two unshown strings would be a localization task
nobody has scheduled.

### What `m5_shop_progression` does not assert

Step 15 of 13C-1 asks that each purchased tool's characteristic effect fire at least once.
The journey asserts that all sixteen are owned and selectable through the production pipeline;
the effects themselves stay with the twelve `m5_*` journeys that already fire each tool for
real, rather than being re-implemented once more in a thirteenth.

## Character Editor Phase A — Scheduled and Decisions Resolved (2026-08-02)

The owner scheduled **Phase A of `docs/CHARACTER_EDITOR_WORKSHOP_PLAN.md`** — the
parametric character editor — as Milestone 5.5, to run after the Milestone 5 exit gate and
before Milestone 6. Phases B (painting) and C (Steam Workshop) **remain deferred** and keep
their own owner gates; Phase C additionally requires Milestone 6.

Six of the plan's seven owner decisions are resolved:

1. **Feature axes — lean set.** Four feature slots: eyes, brows, mouth, and one body
   accent, each with a type index into the shipped atlas, offset, scale, and color, plus
   per-part base color on all six parts. No per-feature rotation and no head/body shape
   modifiers. Adding a slot later is a schema migration, not a Phase A option.
2. **Editor window — temporary resize of the same window.** Entering the editor stores the
   shell's geometry, resizes it opaque to the editor working size, and restores size,
   position, and transparency on exit. No second window; the Milestone 2 focus,
   always-on-top, DPI, and off-screen-recovery paths stay single-window and are reused for
   the restore.
3. **Expressions always composite above paint.** Not user-suppressible. Knockout and pain
   faces can never be obscured; this is a fixed layer-order invariant with no setting.
4. **Character files are local + Workshop only.** `progress.json` remains the sole Steam
   Cloud file per ARCHITECTURE Section 13, carrying the active-character GUID and nothing
   else. Characters travel by Workshop or `.buddychar` export, never by Cloud.
5. **The editor is free from launch.** A settings-panel entry on every save, with no credit
   cost, catalogue prerequisite, or unlock flag. It is deliberately not an economy sink and
   does not touch the Milestone 5 balance. No editor achievements in Phase A; the confirmed
   ten remain fixed Milestone 6 scope.
7. **The local library is uncapped.** Because nothing bounds the count, two properties are
   requirements rather than optimizations: startup and library-open enumerate directory
   entries and each document's name field only — full parse, compile, and thumbnail happen
   for the active character and for a selected entry, never for the whole library — and the
   library list is paged or virtualized.

Decision 6 (Workshop content-rating stance and report/hide policy) is **not** resolved. It
is Phase C scope and moves to `docs/OPEN_QUESTIONS.md` when Phase C is scheduled.

Unchanged and still binding: customization is visual-only forever, enforced structurally by
the compiler's output type, and exactly one buddy is active at a time.

## Planning Rule

When a requirement or implementation choice is not covered here or in an approved specification, the implementation agent must stop and ask the project owner rather than inventing product behavior.
