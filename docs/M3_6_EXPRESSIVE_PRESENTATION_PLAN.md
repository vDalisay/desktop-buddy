# Milestone 3.6 — Expressive 3D Presentation (Orientation, Activities, Dynamic Face)

Status: **SCHEDULED 2026-07-18** — M3.5 gate closed (Task 8 flip done), all four open
owner decisions resolved into `docs/DECISIONS.md` (very subtle amplitude; face-art
mockup gate before Task 5; blink/glance defaults delegated; LegacyCircles retained as
dev view). Implementation may begin at Task 1. Original pre-plan follows.

Pre-plan written 2026-07-14 on owner direction (friendly stylized expressiveness:
the buddy turns and walks sideways, looks at things while walking, performs simple
charming activities such as eating/sitting/jumping, and the face is composed features,
not a text glyph). Scheduled intent: **after the M3.5 exit gate, before Milestone 4**;
renumbers nothing. Implementation may not begin until M3.5 is accepted and the owner
decisions in the last section are resolved into `docs/DECISIONS.md`. Baseline
dependencies: every M3.5 seam — the socket hierarchy `PresenterRoot → BodyYaw →
{TorsoSocket, HeadSocket, HandSocketL/R, FootSocketL/R}`, the injectable pose/transform
source, the interpolation snapshot layer, `BuddyVisualProfile`, the `--presentation=`
runner argument, and the replaceable `Label3D` parity face.

This slice is what Milestone 4 spends: M4's exit criteria require the buddy to "visibly
differentiate fearful, wary, neutral, content, and delighted behavior without a mood
meter," and M4's autonomy delivers approach/flee/catch/hold/**consume**/toss decisions.
This slice builds the presentation vocabulary those behaviors speak; M4 provides the
reasons. M5's consumables (Meal, Drink) ride the item socket with zero animation work.

**Prime invariants, every task:**

1. The 2D physics simulation stays the sole authority. Presentation reads state and
   never writes it; no new `_PhysicsProcess` registrations (ARCHITECTURE §23); no
   change to any physics profile, drive tuning, scenario expectation, or envelope
   bound. Facing, animation, and look-at are all pure view.
2. **Bounded pose offsets.** In performance mode every visual part's final pose =
   its tracked physics-body pose ⊕ a clamped authored offset (profile cap per part;
   starting bound ≤ 0.5 × part radius, tuning data). The physics bodies remain the
   interaction truth — cursor picking and glove contact target bodies — so visuals may
   decorate the truth but never stray from it.
3. `Reactions.CurrentFace` strings remain the semantic face contract.
   `BuddyReactionComponent` and its priority rules do not change.
4. Headless-neutral: every scenario asserts semantic state (mode, clip id, yaw state,
   pose id, offset bounds), never pixels. All M3.5 global constraints apply, including
   zero per-frame managed allocation, `PhysicsInterpolationMode = Off` on created 3D
   nodes, display-rate independence (time-based animation, never frame-count-based),
   and seeded randomness from the §23 presentation stream only.

## Design intent

The accepted friendly stylized charm comes from three cheap ingredients, not from
animation complexity:
**orientation** (the buddy faces what it does), **tiny authored motions** layered over
the physics (steps, bobs, glances — subtle, never busy), and an **expressive face**.
The ragdoll stays the star for interactions: hits, grabs, throws, and knockouts drop
instantly back to raw physics tracking, which is already the game's charm. Performance
animation exists only for the calm in-between moments.

## The core mechanism — two presentation modes

| Mode | When | What the sockets do |
| --- | --- | --- |
| `Tracking` | Physics-dominated states | Exactly M3.5: socket global transforms written 1:1 from the mapped bodies. |
| `Performance` | Calm states | Sockets posed = tracked body pose ⊕ clamped activity/look-at offsets; `BodyYaw` may turn. |

Mode arbitration reads only existing semantics, on the rendered frame: `Tracking` is
forced while `CurrentConsciousness == Unconscious`, `Recovery.State` is not idle, a
grab context is active, a reaction/defend/flee window is active, `Standing.Snapshot.
IsStable` is false (airborne or tumbling), or within a short post-impact cooldown
(profile). `Performance` is allowed otherwise. Transitions: ease into performance over
a profile time (~0.2 s); snap instantly back to tracking on impact/grab/knockout, with
an interpolation-snapshot snap so the cut cannot smear. A scenario witnesses that
accepted pain is identical whichever mode was active when struck.

## Design seams

| Worker | Home | Responsibility |
| --- | --- | --- |
| Pose pipeline | `src/Buddy/Presentation3D/BuddyPosePipeline.cs` | Mode arbitration, tracking↔performance blend, offset clamping, camera-space depth lanes. |
| Facing controller | `src/Buddy/Presentation3D/FacingController.cs` | `BodyYaw` three-quarter left/right states (about ±30°), eased turns, hysteresis, interaction bias. |
| Activity animator | `src/Buddy/Presentation3D/ActivityAnimator.cs` | `ActivityId` → clip playback on socket offset tracks; item socket; walk-cycle dressing. |
| Head look-at | `src/Buddy/Presentation3D/HeadLookAtComponent.cs` | Interest-target priority, cone clamp, easing, suppression rules; feeds pupil offsets. |
| Face compositor | `src/Buddy/Presentation3D/FaceCompositor.cs` | Composed eyes/brows/mouth features rendered to a texture on state change only. |
| Expression map | `src/Buddy/Presentation3D/FaceExpressionMap.cs` | Authoritative face-state list (exported beside the resolver) → feature pose. |
| Expression profile | `data/buddy/lab_buddy_expression.tres` | All tuning: caps, blend/turn/look times, cones, blink cadence, cycle amplitudes. |

## Tasks

### Task 1 — Pose pipeline: modes, blend, bounded offsets (integration; gate for all below)
`BuddyPosePipeline` between the M3.5 snapshot layer and the sockets. Tracking mode
reproduces M3.5 output bit-for-bit (regression: the M3.5 scenario suite must stay green
with the pipeline inserted and performance disabled). Performance mode applies clamped
offsets. **Depth lanes go camera-space:** the M3.5 `DepthOffset` painter lanes must be
applied as world-Z additions *after* the yawed pose is resolved, never baked into
socket-local positions — the Variant C spike showed a visible sideways shift even at
30° when a lane was local. M3.5 Task 7.5 establishes and tests this transform order;
M3.6 consumes it without changing the accepted lane values.
Scenarios: `pose_mode_arbitration` (drive each forcing state through real semantics →
expected mode), `pose_offset_bounded` (soak: max |visual − body| ≤ cap on every part),
`mode_blend_physics_invariant` (strike during each mode and mid-blend; accepted pain
equal — extends the M3.5 toggle scenario).

### Task 2 — Facing controller (integration)
Stable yaw states `ThreeQuarterLeft`/`ThreeQuarterRight` use the owner-accepted roughly
60-degree three-quarter read (about ±30° from dead-frontal), with eased turns through
zero using the profile duration/curve around `BodyYaw`. Sources, in priority order:
active interaction (pet/tickle/tool cursor engaged → bias toward the nearer readable
three-quarter side), drive walk direction with hysteresis (a direction must persist a
profile number of ticks before a turn commits, so autonomy jitter cannot flip-flop the
model), seeded idle variety (occasional side change while idle). Walking left/right
commits the matching three-quarter state; locomotion physics is untouched. This
2026-07-15 Variant C decision supersedes the earlier full-profile 90° plan. Scenario:
`facing_follows_walk` (sustained drive left/right → matching ±30° committed state
within bounds; jitter seed → no commit; transition crosses zero without overshoot).

### Task 3 — Activity animator, item socket, walk dressing (integration/presentation)
`ActivityId` enum + one `AnimationPlayer` playing typed clips that animate **socket
offset tracks only** (Godot `Animation` resources; authored in-editor; processed in
idle time; presentation-only). Two activity classes, and the distinction is
architectural:
- **Class P — pure presentation**, this slice triggers them itself: idle micro-motion
  (breathing bob, weight shifts), walk dressing, wave, chew loop. Walk dressing derives
  its cycle phase from the measured torso horizontal speed (via the pose source — never
  a physics write), so step rate always matches travel and feet cannot moonwalk; zero
  speed stops the cycle.
- **Class B — behavior-backed**, the gameplay layer owns the state and duration and
  pushes it through a semantic `SetActivity(...)` API (the `SetToolReactionIntent`
  pattern). Owner-resolved slice set: **eat ships now** (triggerable from lab keys and
  scenarios; M4's consume decisions wire the real reasons, and drink/hold later reuse
  the same item-socket clips); **sit/sleep ship with their M4 behaviors** as new clips
  on this system, not in this slice. Jump is already physical — the animator only adds
  anticipation (pre-liftoff squash from `JumpRequested`) and landing accents around the
  real flight, which stays `Tracking`.
**Item socket:** `ItemSocket` under the hand socket. Consume/hold clips are
item-agnostic: whatever item *visual* is socketed rides the clip — one eat clip, any
food. Only the visual attaches; the item's physics body stays wherever gameplay says.
Scenarios: `activity_clip_mapping` (every `ActivityId` resolves to a clip and reports
it), `walk_cycle_speed_match` (phase rate ∝ measured speed; zero at rest),
`eat_clip_item_agnostic` (two different item meshes, same clip, both ride the socket).

### Task 4 — Head look-at and idle glances (integration/presentation)
Interest provider with profile priorities: engaged tool cursor within range → held/
target item → recent impact source (brief) → seeded ambient idle glances → rest pose.
Owner-resolved: **the cursor is watched only while an interaction is engaged**
(tool/pet/tickle) — plain idle never tracks the cursor, and idle glances are ambient
and seeded, never cursor-driven. Cone clamp (yaw/pitch limits), eased; suppressed while
`Tracking`, while unconscious, and during high-priority reaction faces (profile list). Rotation-only on `HeadSocket`, so
it is physics-free by construction and composes with any activity clip (look while
walking). Feeds the face compositor a quantized pupil offset. Scenario:
`lookat_priority_and_cone` (targets resolve by priority, angles stay clamped,
deterministic per seed; suppression states verified).

**Handoff detail (pinned 2026-07-20 against the Task 1–3 codebase):**

- **Domain model first — `domain/DesktopBuddy.Domain/Presentation/LookAtModel.cs`,
  `FacingModel` pattern.** Pure `LookAtModel` with an inputs record (`LookAtInputs`:
  interaction engaged + cursor world point, item target valid + point, ticks since
  last accepted impact + impact point, face-suppressed flag, head world position), a
  parameters record (`LookAtParameters`), and
  `Update(in inputs, ticksElapsed, deltaSeconds)` returning current yaw/pitch degrees.
  The model owns: priority arbitration (engaged cursor within range → item target →
  impact memory → seeded ambient glance → rest), cone clamping of the target angles
  *before* easing, the monotonic smoothstep start→target ease (same shape as
  `FacingModel` — it must cross zero without overshoot), and the seeded glance timer
  (interval range + hold range; a glance is an angle pair sampled uniformly inside the
  cone, never a world point). Constructor throws on invalid parameters, exactly the
  `FacingModel` guard style. xUnit coverage: priority order including the range
  cutoff, cone clamp at extreme targets, glance determinism per seed, suppression
  easing to rest, tick/time independence.
- **Target→angle convention.** Desired yaw = `atan2(dx, GazeDepth)`, pitch =
  `atan2(dy, GazeDepth)`, where `(dx, dy)` = target − head position in 2D world units
  and `GazeDepth` is a profile virtual distance along the camera axis. The scenario
  oracle recomputes these angles with independent math.
- **Tuning joins `ExpressionTuningData`** (with a `ToLookAtParameters()` subset like
  facing's): cone yaw limit degrees (validation max 60), cone pitch limit degrees
  (max 45), look ease seconds (max 1.0), gaze depth px (finite, positive), engagement
  range px (finite, positive), impact memory ticks (max 600), glance interval
  min/max ticks, glance hold min/max ticks, pupil quantization steps (2–8). Ambient
  glance cadence defaults are owner-delegated: pick values inside the validation
  bounds and record them in `DECISIONS.md`. Mirror the fields on
  `BuddyExpressionProfile` and `data/buddy/lab_buddy_expression.tres`.
- **Godot node `HeadLookAtComponent`** (`src/Buddy/Presentation3D/`),
  `FacingController` pattern: exports `Buddy`, `DamagePipeline`, `CareStroke`,
  `Glove`, `Activities`, `Reactions` (`BuddyReactionComponent`), `Profile`;
  `Initialize()` validates dependencies + profile; reseeds from
  `BuddyRoot.AutonomyReseeded` with its own stream salt (never the facing salt);
  composed and initialized in both scene roots; `Evaluate(double)` allocation-free.
  Per-frame sampling: engaged cursor exactly as `FacingController.Evaluate` (Pet/
  Tickle via `CareStroke.IsHeld && LastContactValid`, glove via `Glove.HasCursor`)
  plus the head-distance engagement-range check; item target = `ItemSocket` global
  position while the eat activity is active with an attached visual (M4 widens this
  to real held items later); impact memory = subscribe `ImpactAccepted`, stamp
  `impact.Point` + the physics frame; face suppression = `Reactions.CurrentFace` in
  the profile string list (default `">_<"`, `"x_x"`, `">:("`) → target rest through
  the normal ease.
- **Application seam — the presenter composes; the component never rotates a
  socket.** The presenter overwrites every socket's `GlobalRotation` each frame in
  `ApplyPartTransform`, so a component rotating `HeadSocket` directly would be
  silently overwritten. Follow the facing pattern: optional
  `[Export] HeadLookAtComponent? HeadLookAt` on `BuddyVisualPresenter`;
  `UpdateVisuals` evaluates it once per rendered frame; `ApplyPartTransform` adds
  (pitch X, yaw Y) × performance weight into the **head socket's** rotation only —
  body yaw and the physics Z rotation unchanged, every other socket untouched.
  Weight scaling makes the Tracking/unconscious suppression automatic and
  snap-safe, identical to facing yaw. Presenter oracles for scenarios:
  `AppliedHeadYawDegrees` / `AppliedHeadPitchDegrees`.
- **Pupil seam for Task 5.** The component exposes a quantized `PupilOffset`
  (Vector2): applied angles normalized by the cone limits to [−1, 1], then quantized
  to the profile step count. Task 5 consumes it; the Task 4 scenario asserts it
  directly (no face required).
- **Scenario `lookat_priority_and_cone`** (registered in the scenario runner and
  `TEST_PLAN.md`), seeds 1 + 7: pet-stroke engagement → head angles track the cursor
  side within the cone; cursor beyond engagement range → ambient behavior resumes;
  `SetActivity(Eat)` with a socketed item → item wins over ambient; controlled
  strike → impact point watched, decays after memory ticks; idle → glance sequence
  deterministic per seed with every applied angle inside the cone; forced Tracking
  (post-strike cooldown) → applied head angles exactly zero; pain face → eased to
  rest. Full regression rerun: `pose_pipeline`, `facing_follows_walk`,
  `activity_clips`, `presentation_look`, `presentation_3d`, toggle journey, quick
  suite, build 0/0.

### Task 5 — Composed dynamic face (integration/presentation)
Replaces the M3.5 `Label3D` parity face at this slice's gate. `FaceCompositor` draws
eyes + brows + mouth as simple procedural features (`CanvasItem` draw into a small
offscreen `SubViewport` → `ImageTexture`), mounted on a head-front quad parented to
`HeadSocket` at surface + epsilon and remaining readable at both accepted ±30° states;
whole-head albedo compositing stays with the character editor plan, which extends this
same compositor. Re-render **on change only**: expression state, blink edge, pupil
quantum, chew frame. `FaceExpressionMap` translates the authoritative face-state list —
exported beside `BuddyReactionComponent`, currently ten strings — into feature poses;
the strings and the resolver do not change. Overlays: seeded blink timer (suppressed
for closed/special-eye states per profile), chew loop during eat, pupils from Task 4.
The sideways-emoticon quarter-turn hazard from M3.5 disappears with the glyph.
Scenarios: `expression_map_coverage` (all states → pose; bounded re-render count),
`face_semantic_roundtrip` (`StrikePart(head)` → `">_<"` → compositor's last-composited
pose is the pain pose), `blink_suppression`. Headless discipline: all checks are
semantic; the compositor render is GPU-only and guards headless exactly as the editor
plan's A4 rule prescribes.

### Task 6 — Composition, regression, documentation, gate (integration/testing)
Lab keys for activity/facing triggers (dev-guarded raw keys beside `V`); journeys
extended (walk with turn-around; eat with an item). Reruns with expressive presentation
enabled via `--presentation=`: the full M3.5 rerun list plus `m3_glove_strike` and
`lab_spawn_settle`; verdicts match baselines. Amend `ARCHITECTURE.md` §14 (presentation
modes, bounded offsets, activity/face systems), `TEST_PLAN.md`, and record in
`DECISIONS.md`: the mode-arbitration rules, the offset cap, the face-mounting choice,
and the `Label3D` retirement. MCP interactive pass with screenshot evidence before the
gate.

## Exit gate (owner-manual)

On real Windows with the transparent shell, at 60 Hz and one high-refresh monitor when
available: idle (breathing, glances, blinks), walk with committed three-quarter turns
both ways, jump with anticipation/landing accents, eat with two different socketed
items, a wave, pet/tickle with look-at engagement (and confirmation that plain idle
ignores the cursor), glove hit → instant ragdoll cut, knockout collapse and recovery
easing back into performance. The judgment: **alive but never busy** — subtle,
charming, cute; the ragdoll cut must feel like the same buddy. The owner accepts feel
and confirms no physics behavior changed.

## Owner-resolved scope (2026-07-14; amended 2026-07-15)

- **Facing: Variant C three-quarter.** Walking and stable idle facing use about ±30°
  yaw from dead-frontal, producing the accepted roughly 60-degree three-quarter read.
  This supersedes the earlier full-profile (90°) direction.
- **Look-at: interactions only.** The buddy watches the cursor only while an
  interaction is engaged; idle glances are ambient and seeded, never cursor-tracking.
- **Slice activity set: idle, walk dressing, jump accents, eat, wave.** Sit/sleep are
  deferred to Milestone 4 and arrive as new clips on this system when their behaviors
  exist.

These answers move into `docs/DECISIONS.md` when the slice is scheduled.

## Owner decisions still open before scheduling

Move into `docs/OPEN_QUESTIONS.md` when scheduled; resolve into `docs/DECISIONS.md`
before implementation:

1. Motion amplitude direction: very subtle (recommended; offsets well inside the
   0.5 × radius cap) versus cartoonish.
2. Face feature art direction (shared with character-editor decision 1 — the same
   features become parametric there).
3. Blink cadence and ambient idle-glance frequency ranges.
4. Fate of the `LegacyCircles` debug view if it survived the M3.5 gate.

## Effort estimate

| Task | Focused effort |
| --- | --- |
| 1 — Pose pipeline | 4–6 days |
| 2 — Facing | 2–3 days |
| 3 — Activities + item socket | 5–8 days |
| 4 — Look-at | 2–3 days |
| 5 — Composed face | 4–6 days |
| 6 — Integration + gate | 3–4 days |

Roughly 3–5 focused weeks. Ships alone: yes — the buddy is visibly more alive even
before M4 gives it reasons.

## Progress

Pre-plan written 2026-07-14 at owner request, same day as the owner's expressiveness
direction; baseline `m3-sol` `80fb22b` analysis worktree. Not scheduled; no tasks
started. Near-term obligations this plan creates inside M3.5: the socket hierarchy and
replaceable face (already amended into M3.5 Task 4), the pose-source seam, and
camera-space application of `DepthOffset` lanes (Task 1 here consumes all four). The
character-editor plan's face compositor and expression map now build here first and are
parameterized there later.

Owner resolved three scope decisions on 2026-07-14: walk facing, cursor look-at only
during interactions, and the idle/walk/jump/eat/wave activity set with sit/sleep
deferred to M4. On 2026-07-15 the accepted Variant C look superseded the original 90°
walk choice with ±30° three-quarter states. The task text carries the answers and the
open-decision list is down to four.

**Task 1 (pose pipeline: modes, blend, bounded offsets) DONE (2026-07-18).** Engine-free
core in `domain/DesktopBuddy.Domain/Presentation/PosePipelineModel.cs`: `PoseModeArbiter`
(the plan's forcing-state list verbatim), time-based `PerformanceBlend` (ease-in over the
profile seconds, instant snap to zero on Tracking), `BoundedOffset` magnitude clamp, and
`ExpressionTuningData` validation — 38 new xUnit tests (domain 268/268). Godot side:
`BuddyExpressionProfile` + `data/buddy/lab_buddy_expression.tres` (blend 0.2 s, cooldown
60 ticks, cap fraction 0.5), `BuddyPosePipeline` node (samples consciousness, recovery
assistance, the live grab, the tool-reaction window, standing stability, and an
`ImpactAccepted` physics-frame stamp; composed and validated in both scene roots), and the
presenter applies weight x cap-clamped offsets *before* yaw per the Task 7.5 transform
contract (offsets zero in normal composition; `SetDevelopmentOffset` is the dev drive until
Tasks 3–4 provide real sources). Tracking output is bit-identical to M3.5 by construction
(zero offset, identity yaw). New `pose_pipeline` scenario (registered, in `TEST_PLAN.md`):
`pose_mode_arbitration` drives every forcing state through real semantics (tether grab,
`SetConsciousness`, controlled strike + cooldown recovery, learned-harm glove guard),
`pose_offset_bounded` proves requested 10x-cap offsets sit exactly at the cap at full blend,
`mode_blend_physics_invariant` lands saturated strikes in Performance/Tracking/mid-blend
(launch weight 0.042) with identical accepted pain 10.000. Scenario notes: the saturating
pain profile makes glove-hover episodes max-pain, so the glove check runs last or the real
rolling window KOs the buddy; strikes run at 2000 px/s so a recoiling torso stays above the
saturation anchor. Verified: seeds 1 + 7, `presentation_look`/`presentation_3d`/toggle
journey unchanged, quick suite 9/9, build 0/0.

**Task 2 (facing controller) DONE (2026-07-19).** Engine-free `FacingModel`
(`domain/.../Presentation/FacingModel.cs`): priority arbitration (engaged interaction
side immediately; walk direction only after the hysteresis streak so autonomy jitter
never flip-flops; seeded idle variety flips the side on its own salted stream), plus a
monotonic smoothstep ease start-to-target that crosses zero and cannot overshoot — 27
new xUnit tests (domain 295/295). `ExpressionTuningData` gained the six facing fields
(accepted 30 degrees, turn 0.5 s, commit 36 ticks, deadband 0.05, idle flip 720–1920
ticks) with validation. Godot: `FacingController` node (samples the care/glove cursor
side and `CurrentDriveIntent.WalkDirection`; reseeds from the new
`BuddyRoot.AutonomyReseeded` event with a facing-stream salt), composed in both scene
roots; the presenter applies development yaw + facing yaw x performance weight, so a
Tracking cut snaps the displayed yaw to zero while the committed side survives —
`AppliedYawDegrees` is the scenario oracle input. The `pose_pipeline` offset check went
yaw-aware (rotation preserves offset magnitude about the torso pivot). New
`facing_follows_walk` scenario (registered, in `TEST_PLAN.md`) green on seeds 1 + 7;
full rerun green: `pose_pipeline`, `presentation_look`, `presentation_3d`, toggle
journey, quick suite 9/9, build 0/0.

**Task 3 (activity animator, item socket, walk dressing) DONE (2026-07-20).** Engine-free
`ActivitySelector` (`domain/.../Presentation/ActivityModel.cs`): priority Eat > Wave >
JumpAnticipation > WalkCycle > IdleBreathe, Tracking suppression to None (behavior
countdowns keep running so a punched buddy never resumes a stale eat), and walk-cycle
phase advanced by MEASURED horizontal travel (freezes at rest — feet cannot moonwalk);
`ActivityTuningData` validates the selector timing plus the very-subtle clip amplitudes
(≤6 px hard bound; the offset cap still clamps on top) — 21 new xUnit tests (domain
316/316). Godot `ActivityAnimator`: one manual-mode `AnimationPlayer` whose value tracks
animate six offset-proxy nodes (never a socket or body); the presenter reads proxy
positions as authored offsets through the existing weight+cap clamp. Five clips —
idle_breathe, walk_cycle (phase-seeked), jump_anticipation (squash from the real
`JumpRequested`), wave, eat (looping hand-to-mouth + chew nod) — are built once at
initialization from the typed profile amplitudes. **Plan deviation, recorded:** clips are
authored programmatically from typed Resource data rather than hand-authored in-editor;
same Animation/AnimationPlayer machinery, and amplitudes stay owner-tunable data.
`SetActivity(ActivityId, duration)` is the semantic Class B seam (Eat ships now; None
cancels; ambient ids rejected); `ItemSocket` under the right-hand socket carries any item
VISUAL through any hand clip while the item's physics stays gameplay-owned; lab keys
arrive with Task 6. New `activity_clips` scenario (registered, in `TEST_PLAN.md`) green
seeds 1 + 7 — clip mapping complete, walk phase/travel ratio 1.000 with freeze outside
walk, sphere and box items both rode the one eat clip. Full regression green:
`pose_pipeline`, `facing_follows_walk`, `presentation_look`, `presentation_3d`, toggle
journey, quick suite 9/9, build 0/0.

**Task 4 (head look-at and idle glances) DONE (2026-07-20).** Engine-free `LookAtModel`
(`domain/.../Presentation/LookAtModel.cs`): priority arbitration (engaged cursor inside
the engagement range > valid item target > impact memory > seeded ambient glance > rest,
with a suppressed reaction face standing everything down to rest), target angles from the
pinned convention `yaw = atan2(dx, GazeDepth)` / `pitch = atan2(dy, GazeDepth)` clamped
into the cone BEFORE the smoothstep ease, and a seeded glance timer that alternates a rest
interval with a held glance — a glance is an ANGLE PAIR sampled inside the cone, never a
world point, so ambient idling can never be mistaken for cursor tracking. The ease restarts
only on ACQUISITION (source change or a fresh glance): while a source is held the target
keeps updating, so the head follows a moving cursor instead of stalling on a smoothstep
that restarts every frame, and because both endpoints are in-cone the eased value provably
never leaves the cone or overshoots. 47 new xUnit tests (domain 363/363), including the
range cutoff, clamped extremes, zero-crossing side switches, glance determinism per seed,
eased (not cut) suppression, pupil quantization, and tick/time step independence.
`ExpressionTuningData` gained the eleven look fields with validation (cone yaw ≤ 60, cone
pitch ≤ 45, ease ≤ 1 s, impact memory ≤ 600 ticks, pupil steps 2–8) and
`ToLookAtParameters()`. Godot: `HeadLookAtComponent` (samples the care/glove cursor exactly
as facing does, the `ItemSocket` while Eat is active with an attached visual, an
`ImpactAccepted` point+frame stamp, and `Reactions.CurrentFace` against the profile
suppression list; reseeds from `BuddyRoot.AutonomyReseeded` on its own salt, never the
facing salt), composed and initialized in both scene roots after the animator. Application
seam per the pinned handoff: the component rotates nothing — the presenter adds
(pitch X, yaw Y) x performance weight into the HEAD socket's rotation only, so body yaw,
the physics Z rotation, and every other socket are untouched and Tracking/unconscious
suppression is automatic and snap-safe; `AppliedHeadYawDegrees`/`AppliedHeadPitchDegrees`
are the scenario oracles and `PupilOffset` is the Task 5 seam. Owner-delegated glance
cadence resolved and recorded in `DECISIONS.md` + `lab_buddy_expression.tres` (cone 28/18,
ease 0.25 s, gaze depth 120 px, engagement range 220 px, impact memory 240 ticks, glance
every 480–1200 ticks held 72–168, 4 pupil steps). `WorldPlaneMapping` gained the `To2D`
inverse so the 3D item socket is read back in simulation units through the one mapping
authority. New `lookat_priority_and_cone` scenario (registered, in `TEST_PLAN.md`) green on
seeds 1 + 7 — the oracle recomputes every expected angle with independent `atan2` math.
Scenario notes: the strike must run at 2000 px/s (900 never registers an accepted impact),
and the glance-determinism check compresses the cadence to a scenario-local 24–60/24–48
ticks and restores it afterwards, so proving the seeded stream costs seconds rather than
the two minutes the shipping 4–10 s cadence would need. Full regression green:
`pose_pipeline`, `facing_follows_walk`, `activity_clips`, `presentation_look`,
`presentation_3d`, toggle journey, quick suite 9/9, build 0/0. (The `ObjectDB instances
leaked at exit` warning on `presentation_3d`/`facing_follows_walk` was verified against a
stashed tree to predate this task.)

**Task 4 owner inspection follow-ups (2026-07-20).** The owner ran the lab and reported the
buddy "frozen in place, only its head moving", plus too-busy ambient motion. Four outcomes:

1. **Pause regression, FIXED (the M3.6 layer animated through a laboratory pause).** The
   performance layer is driven by the RENDERED frame; the lab pause freezes the bodies and
   stops routing gameplay ticks but rendering continues, so blend, facing, clips, and gaze
   kept running behind a frozen ragdoll — and because the M3.6 timers counted
   `Engine.GetPhysicsFrames()`, which ignores the routing gate, idle-variety flips and
   glances fired while paused. Root fix: **`BuddyRoot.RoutedTicks`** is now the simulation's
   own clock (incremented only on a routed tick, no behaviour change), and
   `FacingController`, `HeadLookAtComponent`, and `BuddyPosePipeline` count in it instead of
   engine frames — so a pause holds every timer, a single step advances each by exactly one,
   and the post-impact cooldown can no longer burn while paused. The seconds clock is held
   by `BuddyVisualPresenter.SetPresentationHeld(bool)`, called from
   `LaboratoryControlComponent.SetPaused` (optional export; the sandbox has no pause and
   never calls it). Tracking still renders every frame, so a single step shows the new pose.
   New `pause_holds_presentation` check in `pose_pipeline` guards it (yaw, head angles, and
   activity offsets all still for 600 frames while paused; motion resumes on release).
2. **Presentation calmed** in `lab_buddy_expression.tres` — facing walk commit 36 → 90 ticks
   (short autonomy walk bursts no longer trigger a turn), idle flips 720–1920 → 1440–3600,
   glances 480–1200 → 720–1800, breathing 3.2 s/1.2 px → 4.4 s/0.8 px, walk bob 1.5 → 1.0.
3. **The "darting" was ambient autonomy, not this slice.** `lab_autonomous_motion.tres`
   amended with explicit owner approval (recorded in `DECISIONS.md`): idle 60–120 → 240–600,
   walk 120–240 → 240–480, jump interval 240–480 → 960–1800, idle weight 2 → 6. No envelope
   re-baseline was needed — `autonomous_motion`, `repeat_envelope`, and `idle_soak` stayed
   green as-is.

4. **Ambient jumping disabled (2026-07-20, owner: "a bit too random").** New
   `AmbientJumpsEnabled` switch on `AutonomousMotionTuning`/`AutonomousMotionProfile`
   (default `true` in code, `false` in the shipped `.tres`) gates only the ambient jump
   timer — when off it never requests a jump and draws nothing from the seeded stream.
   Jump actuation is untouched: tool-reaction hops, M4 behaviour jumps, and the M3.6
   jump-anticipation activity all still ride `DriveIntent.JumpRequested`. Three new domain
   tests; `autonomous_motion` keeps covering jump actuation via a scenario-local
   jumps-enabled profile (differing from shipped ONLY by the flag, so the seeded goal
   stream is unchanged) and asserts the shipped datum is off.
   **Fallout worth knowing:** turning ambient jumps off shifted where the buddy is standing
   when `facing_follows_walk` fires its controlled strike, and the strike started missing
   entirely (no contact scored at all, on both seeds). The cause is the shared
   `ScenarioSteps.StrikePart` helper — it spawns a probe body a fixed offset from the
   target and launches it, which assumes a clear line, so whether it connects depends on
   where autonomy has put the limbs. Fixed in the scenario by retrying the strike up to
   five times, and by capturing the committed facing side AT THE CUT rather than before the
   retry window (the buddy walks between attempts and may legitimately re-commit, so the
   old comparison proved nothing). Other scenarios' single strikes still land; if another
   one starts missing after a tuning change, this is the reason.

Also noted for Task 6: its rerun list names a scenario id `m3_glove_strike` that does not
exist in the catalog; the real M3 ids are `m3_presentation` and `tool_feel_reactions`.
Full rerun after all three: domain 363/363, `pose_pipeline` (with the new guard),
`facing_follows_walk`, `activity_clips`, `lookat_priority_and_cone`, `presentation_look`,
`presentation_3d`, `m3_presentation`, `tool_feel_reactions`, `knockout_window`,
`autonomous_motion`, `repeat_envelope`, `idle_soak`, toggle journey, quick suite 9/9,
build 0/0.
