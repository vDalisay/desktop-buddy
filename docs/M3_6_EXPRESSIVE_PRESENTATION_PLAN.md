# Milestone 3.6 — Expressive 3D Presentation (Orientation, Activities, Dynamic Face)

Status: pre-plan written 2026-07-14 on owner direction ("Nintendo-like" expressiveness:
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

Mii-like charm comes from three cheap ingredients, not from animation complexity:
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
| Facing controller | `src/Buddy/Presentation3D/FacingController.cs` | `BodyYaw` states (CameraFront/Left/Right), eased turns, hysteresis, interaction pull-to-camera. |
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
socket-local positions — a lane in local space becomes a visible sideways shift at
±90° yaw — and lane magnitude fades toward zero as yaw approaches ±90°, where true 3D
depth ordering (near hand in front) takes over naturally.
Scenarios: `pose_mode_arbitration` (drive each forcing state through real semantics →
expected mode), `pose_offset_bounded` (soak: max |visual − body| ≤ cap on every part),
`mode_blend_physics_invariant` (strike during each mode and mid-blend; accepted pain
equal — extends the M3.5 toggle scenario).

### Task 2 — Facing controller (integration)
Yaw states CameraFront/Left/Right with eased turns (profile duration/curve) around the
`BodyYaw` socket. Sources, in priority order: active interaction (pet/tickle/tool
cursor engaged → pull toward CameraFront), drive walk direction with hysteresis (a
direction must persist a profile number of ticks before a turn commits, so autonomy
jitter cannot flip-flop the model), seeded idle variety (occasional turn while idle).
Walking left plays as the model yawed left, walking "forward" — locomotion physics is
untouched. Owner-resolved: walking turns **full profile (90°)**. Scenario:
`facing_follows_walk` (sustained drive left/right → committed yaw state within bounds;
jitter seed → no commit).

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

### Task 5 — Composed dynamic face (integration/presentation)
Replaces the M3.5 `Label3D` parity face at this slice's gate. `FaceCompositor` draws
eyes + brows + mouth as simple procedural features (`CanvasItem` draw into a small
offscreen `SubViewport` → `ImageTexture`), mounted on a head-front quad parented to
`HeadSocket` at surface + epsilon (edge-on at full profile yaw is correct Mii behavior;
whole-head albedo compositing stays with the character editor plan, which extends this
same compositor). Re-render **on change only**: expression state, blink edge, pupil
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
available: idle (breathing, glances, blinks), walk with committed full-profile turns
both ways, jump with anticipation/landing accents, eat with two different socketed
items, a wave, pet/tickle with look-at engagement (and confirmation that plain idle
ignores the cursor), glove hit → instant ragdoll cut, knockout collapse and recovery
easing back into performance. The judgment: **alive but never busy** — subtle,
charming, cute; the ragdoll cut must feel like the same buddy. The owner accepts feel
and confirms no physics behavior changed.

## Owner-resolved scope (2026-07-14)

- **Walk facing: full profile.** Walking turns the model fully sideways (90°).
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

Owner resolved three scope decisions on 2026-07-14: full-profile (90°) walk facing;
cursor look-at only during interactions; slice activity set idle/walk/jump/eat/wave
with sit/sleep deferred to M4. The task text carries the answers and the open-decision
list is down to four.
