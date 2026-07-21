# Owner Feedback 2026-07-20 — Fix Plan (post-M3.6 Task 6 lab pass)

Status: **COMPLETE — OWNER ACCEPTED (2026-07-21).** The first implementation
passed automated gates but did not meet the owner's hands-on expectations for Eat,
airborne grab passivity, forward wall stopping, or head-righting speed. The corrections
recorded in `DECISIONS.md` supersede the affected implementation notes below. After the
follow-up frontal Eat, final hand-lowering, and walk-stop refinements, the owner confirmed
the result “looks better” and requested this plan be checked off and committed.

**Invariants carried from M3.6 (apply to every task below):**

1. The 2D physics simulation stays the sole authority; presentation reads state and never
   writes it. Where a fix genuinely needs the *body* to move (eat reach, dangle, head
   righting), the fix is a bounded physics drive on the authoritative clock — never a
   visual offset pretending to be one.
2. Bounded pose offsets: visual ⊕ clamped authored offset, cap 0.5 × part radius.
3. `Reactions.CurrentFace` strings and the resolver priority rules stay the semantic face
   contract. Feature-pose *art* behind a string may change; the strings may not.
4. Headless-neutral scenarios, semantic asserts only, seeded randomness per salted stream,
   routed-tick clocks (never engine frames), `--fixed-fps 120` on pacing-sensitive runs.

## Implementation progress (2026-07-20)

- **A1 complete:** per-part `LaneYawFade` is validated typed visual data. Hands/feet fade
  their fixed lanes across the committed yaw; head/connectors retain theirs. The
  `presentation_3d` scenario proves identity-lane parity plus mirrored far-hand sorting.
- **A2 complete:** `">:("` no longer suppresses look-at. Defending owns an independent
  eased gaze weight while body yaw/offsets remain in Tracking. Pain eases the gaze to
  rest and knockout cuts it to zero; `lookat_priority_and_cone` covers the full sequence.
- **A3 complete:** `:3` maps to happy-arc eyes plus a larger CatSmile. The owner confirmed
  calm/default `:)`, active Pet-rub `:3`, and no new completion string.
- **A4 stopgap complete:** wave and chew amplitudes are `6 px`, the existing typed-data
  maximum. The 0.5 x radius visual cap is unchanged. The real physical reach remains B4.
- **B2 complete:** the room owner injects authoritative inner bounds into autonomy;
  blocked directions are excluded and an active into-wall goal is cut immediately.
  `WallAvoidMarginPixels = 42` is typed laboratory tuning. Domain tests plus the new
  `autonomy_respects_walls` scenario cover both walls, idle, and away-walk choices.
- **B1 complete:** accepted head impacts and live head grabs re-arm a `240` routed-tick
  calm window; conscious-only bounded head torque then rights gently. The real-grab,
  impact re-arm, and unconscious cases are covered by `head_rights_after_disturbance`.
- **B3 complete:** unsupported buddy grabs disable active drive and resistance while the
  ordinary passive structural springs preserve the six-part topology, matching unconscious
  ragdoll behavior without changing consciousness. Grounded grabs retain standing and
  conscious reactions. `grab_dangle` covers all six grab targets, knockout, and release.
- **B4 complete (Eat):** a gameplay-owned fixed-tick activity pauses autonomy, applies a
  bounded stationary brake, and drives both physical hands through the accepted five-bite
  chest-to-mouth sequence plus the final lower-and-hold. Accepted impacts cut it immediately.
  `activity_clips` covers frontal facing, reach, standstill, bite scaling, depth/clearance,
  final physical lowering, release, and impact interruption. Physical Wave reuse remains deferred.
- **Owner gates resolved:** OQ-7 through OQ-10 are recorded in `DECISIONS.md`.

Verification: build is warning-free; 407 domain tests and the 9/9 quick suite pass.
`activity_clips`, `head_rights_after_disturbance`, `grab_dangle`, `face_composition`,
`pet_tickle_mood`, and `m36_expressive` pass seeds 1 and 7 in both Mii3D and LegacyCircles.
Grab release/hold/resistance/hard-recovery regressions pass both modes. The Group B
regression set passes seeds 1 and 7 in both modes for `autonomous_motion`,
`repeat_envelope`, the full 216,000-tick `idle_soak`, and `knockout_window`.
`standing_recovery` remains its documented pre-existing red at 228 ticks versus the
180-tick bound in every run (unchanged, not worsened). The configured Godot integration
also launches the composed Buddy Lab on the RTX 3070 GL Compatibility renderer with no
debug errors.

## Owner feel-pass correction implementation (2026-07-21)

- **Eat:** `BehaviorActivityComponent` now owns a typed, fixed-tick chest hold followed
  by exactly five bite cycles. Both physical hands share a chest-to-mouth target;
  presentation adds only the small phase-locked head bob. The item socket follows the
  two-hand midpoint, scales `0.8 / 0.6 / 0.4 / 0.2 / 0` on the five bite events, and is
  removed on bite five. Eat pins both hand presentation lanes beyond the face plate,
  spaces their inner edges around the food, and keeps their upper edges just below the
  chew mouth; the food receives its own upward/front readability offset. Windowed
  `owner_feedback_visual` captures this mouth pose for human inspection. In physics,
  the chest-hold reaction sends only `25%` to the head (half the original `50%` share),
  rising to `50%` only at the mouth. `activity_clips/eat_exact_five_bites` is the gate.
- **Airborne grab:** the no-support grab branch sets active outputs off before any
  resistance, standing, recovery, locomotion, or gesture force can run. The existing
  passive structural springs remain active, exactly as in unconscious ragdoll state;
  the rejected limit-only mode is removed because it let every unheld part slide to the
  lowest permitted point. A typed `5x` stiffness / `2x` damping passive multiplier while
  airborne lets the tether translate the connected puppet instead of stretching one
  endpoint; active drive remains off, so gravity still rotates and sags the whole pose.
  Ground support keeps the ordinary conscious drive.
  `grab_dangle` compares conscious airborne telemetry and topology against the passive
  unconscious state across all six grab targets and requires zero resistance. Its worst
  measured rest-topology error is `14.5 px`; the windowed evidence run captures a settled
  hand-lift pose.
- **Forward walls:** wall flags use the foremost edge across all six body circles rather
  than the torso center, with `0.3 s` velocity projection and a `12 px` requested
  clearance. Into-wall momentum routes through the bounded stationary brake.
  `autonomy_respects_walls` now drives both walls and proves a minimum measured clearance
  above `13 px`, as well as zero into-wall choices.
- **Head rotation:** the calm delay remains `240` ticks. The bounded torque data is tuned
  to `24000` stiffness, `2200` damping, and `48000` maximum torque;
  `head_rights_after_disturbance` measures `52` ticks (`0.433 s`) from torque start to
  the accepted angle threshold on the fixed 120 Hz run.
- **Final owner-review refinements:** Eat now supplies a temporary frontal target to the
  pure facing model, without overwriting its committed three-quarter side; the prior side
  restores when Eat ends. The fifth bite cycle also blends both hand targets down to the
  ordinary `y=-5 px` limb-rest height and holds there for `30` routed ticks so the physical
  bodies arrive before releasing them sideways. Grounded zero-walk
  intent invokes the bounded whole-body stationary brake even outside an explicit activity;
  deterministic seeds 1 and 7 each measure under `0.2 px` center-of-mass travel over the
  four-tick stop window and end below `2 px/s`. Airborne and throw momentum bypass this path.

## Owner recheck — complete (2026-07-21)

- [x] Eat stands still, uses both hands, faces the food frontally, and completes exactly five bites.
- [x] Eat hands render in front of the face with readable mouth clearance.
- [x] The fifth bite lowers the physical hands before their horizontal return to the sides.
- [x] Unsupported grabs feel limp while preserving the buddy's body-part topology.
- [x] Forward wall detection prevents continuous into-wall walking.
- [x] Grounded walk-to-idle transitions stop without visible coasting.
- [x] Head righting completes within the owner-approved `0.5 s` maximum after its calm delay.
- [x] Windowed screenshot inspection and owner hands-on recheck accepted.

---

## Group A — Presentation-layer fixes (no physics behavior change)

### A1 — Far hand renders in front of the body at ±30° yaw

**Implementation:** COMPLETE (2026-07-20).

**Owner report:** walking left at the three-quarter angle, the viewer-side left hand
should be partly hidden behind the body; instead it draws fully in front. Same both ways.

**Root cause (confirmed):** `lab_buddy_visual.tres` gives both hands a fixed painter lane
`DepthOffset = +48` (feet −48, head +96), and per the M3.5 transform contract the
presenter adds lanes as a *global camera-axis Z after* the yawed pose is resolved
(`BuddyVisualPresenter.ResolveLanePosition`). The natural depth the ±30° yaw produces on a
hand is only ≈ ±(hand x-offset ≈ 40 px) × sin 30° ≈ ±20 px — always smaller than the
+48 lane, so the far hand can never sort behind the torso.

**Fix:** fade the authored lane as |yaw| grows so yaw geometry takes over sorting exactly
when it becomes meaningful. Per-part data, not code constants: add
`PartVisualDefinition.LaneYawFade` (0 = lane always full, 1 = lane fades to zero at the
committed ±30°), applied as `lane × (1 − LaneYawFade × smoothstep(0, FacingYawDegrees,
|appliedYaw|))` inside `ResolveLanePosition`. Shipped data: hands and feet fade 1.0, head
fades enough to keep it in front of the torso at all yaws (its natural depth at 30° is
near zero — keep head fade at 0), connectors keep their −24 lanes untouched.
At identity yaw the multiplier is exactly 1, so the M3.5 projection stays bit-for-bit —
the existing transform-contract scenario must stay green unmodified.

**Scenarios:** extend the presenter transform checks in `presentation_3d`: at a committed
ThreeQuarterLeft the *far* hand socket's global Z < torso socket Z (and mirrored for
right); at zero yaw the lane values match M3.5 exactly. MCP screenshot pass both sides.

**Effort:** 0.5–1 day.

### A2 — Defend stance: head/gaze must track the boxing glove

**Implementation:** COMPLETE (2026-07-20).

**Owner report:** while angry and defending against the glove, the buddy stares straight
ahead instead of following the glove.

**Root cause (confirmed, two independent suppressors):**
1. `">:("` — the defend/angry face — is in `LookSuppressionFaces`
   (`BuddyExpressionProfile`, shipped `.tres`), so the look-at model stands down to rest
   the moment the defend face shows.
2. The defend window is a pose-pipeline forcing state (reaction/defend → Tracking), which
   drives the performance weight to 0, and the presenter multiplies the head look-at
   angles by that weight — so even without (1) the gaze would be zeroed.

**Fix:**
- Remove `">:("` from `LookSuppressionFaces` (keep `">_<"`, `"x_x"` — pain and knockout
  still stand the gaze down). Data change + default in `BuddyExpressionProfile`.
- Give the gaze its own weight instead of borrowing the body's blend wholesale:
  `gazeWeight = max(performanceWeight, defendWindowActive ? defendGazeBlend : 0)`, where
  `defendGazeBlend` eases with the same profile blend seconds and `defendWindowActive`
  comes from the tool-reaction window the pose pipeline already samples. All other
  forcing states (unconscious, grab, airborne, post-impact cooldown) still zero the gaze
  because they don't set the defend flag. The engaged-glove-cursor priority already
  exists in `LookAtModel` (`Glove.HasCursor` path in `HeadLookAtComponent.Evaluate`), so
  once the weight is nonzero the head tracks the glove with no model change.
- Presenter change is confined to the `_headLookYaw/Pitch` scaling in `UpdateVisuals`;
  body yaw and offsets keep using the plain performance weight (the ragdoll-cut snap on
  the body must not change).

**Scenarios:** extend `lookat_priority_and_cone`: enter the defend window with the glove
cursor held to one side → applied head yaw tracks the glove side within the cone while
the face is `">:("`; strike to a pain face → gaze eases to rest; knockout → zero.

**Effort:** 1 day.

### A3 — Pet smile confirmation is not readable on the composed face

**Implementation:** ART PASS COMPLETE; OWNER LEGIBILITY CHECK PENDING (2026-07-20).

**Owner report:** petting no longer produces the visible smile confirmation the Label3D
glyph gave.

**Root cause (probable — verify first):** the semantics are intact (the
`m3_tool_feel` journey still asserts `:3` → `CatSmile` compose), so this is a legibility
regression from the glyph swap: `SoftOvalFacePainter`'s CatSmile is two ~2-px arcs on a
40×40 plate — near-indistinguishable from neutral at desktop size — and the pet
*completion* face `:)` is the identical Smile pose the Content mood band already shows
ambiently, so completion produces no visible change at all.

**Fix:**
1. Verify in the lab first (telemetry panel shows `CurrentFace`) that `:3` and `:)`
   actually fire during/after a pet stroke — rule out a CareStroke regression before
   touching art.
2. Strengthen the care feedback inside the face contract (strings unchanged; feature
   poses are M3.6 data): map `:3` to Happy (closed-arc) eyes + a larger CatSmile mouth in
   `FaceExpressionCatalog`, and enlarge the CatSmile arcs in `SoftOvalFacePainter` so the
   cat mouth reads at desktop scale. If completion still doesn't read (because ambient
   `:)` matches), raise it with the owner: either accept rub-feedback-only, or approve a
   resolver-level distinct completion face — that is a contract change and is *not* done
   without an explicit owner decision recorded in `DECISIONS.md`.

**Scenarios:** `face_composition` map-coverage check picks up the new `:3` pose
automatically; journey predicate `pet_3d_face_composed` updated to the new expected pose.
MCP screenshot evidence of a pet stroke for the owner.

**Effort:** 0.5–1 day + owner check.

### A4 — `Q` (wave) shows nothing / `E` (eat) hands don't visibly move

**Implementation:** 6 px STOPGAP COMPLETE; physical reach remains B4 (2026-07-20).

*(Presentation half of the eat/wave complaints; the gameplay half is B4.)*

**Root cause (confirmed):** both keys are wired and both clips play — the motion is
simply invisible. Shipped amplitudes: `ActivityWaveAmplitude = 3.0` px,
`ActivityChewAmplitude = 1.0` px (the eat clip moves the hand by chew×1.5–2.0 = 1.5–2 px).
The offset cap for a 15 px hand allows 7.5 px, and `ActivityTuningData` hard-bounds
amplitudes at 6 px, so even at maximum the hand can travel 6 px — a real hand-to-mouth or
a raised wave needs tens of pixels. The "very subtle" owner amplitude decision was made
for *ambient* motion (breathing, walk bob); performed gestures were never separately
sized.

**Fix (short-term, this task):** raise wave/chew amplitudes to the 6 px bound and verify
on screen — this makes the wave *perceptible* but still small. **Real fix is B4** (drive
the physics hand). Do not raise the 6 px bound or the 0.5 × radius cap for offsets — the
bound protects the "visuals never stray from the interaction truth" invariant.

**Effort:** 0.25 day (data + screenshot).

---

## Group B — Gameplay/physics fixes (owner-gated; envelope + feel reruns mandatory)

Every task in this group changes what the bodies do, so each one ends with: domain tests,
its own scenario, and the physics regression set (`autonomous_motion`, `repeat_envelope`,
`idle_soak`, `knockout_window`, `standing_recovery` (known-red baseline — must not get
*worse*), quick suite, seeds 1 + 7, `--fixed-fps 120`). Owner feel pass at the end of the
group, not per task.

### B1 — Head stays upside down after grab / glove hits

**Implementation:** COMPLETE (2026-07-20).

**Owner report:** grabbing or punching the head can leave it rotated (even fully upside
down) indefinitely; wanted: after ~2 s of calm the head rights itself. Future wish
(record, don't build): a cute animation where the buddy grabs and straightens its own
head with its hands (M4+ behavior candidate).

**Root cause (confirmed):** `ActiveDriveComponent.ApplyUprightTorque` drives the *torso*
only. The head body has `AngularDamp = 1.5` and a pin-style link — nothing ever references
its absolute rotation, so whatever spin an interaction leaves settles wherever damping
stops it.

**Fix:** a bounded head-righting torque in `ActiveDriveComponent`, same shape as the torso
upright drive (error = wrapped −rotation, stiffness/damping, clamped maximum), gated by a
calm timer: it arms only after `HeadRightingDelayTicks` (default `240` ticks = 2 s at
120 Hz) have
passed since the last head disturbance — a grab releasing the head part or an accepted
impact re-stamps the timer. Conscious mode only (the unconscious profile has
`ActiveDriveEnabled = false`, so a knocked-out head stays ragdoll — correct). New
`ActiveDriveProfile` fields: `HeadUprightStiffness`, `HeadUprightDamping`,
`MaximumHeadUprightTorque`, `HeadRightingDelayTicks`; tuned gentle — the righting should
read as deliberate, not a snap.

**Scenarios:** new `head_rights_after_disturbance`: rotate the head via a real grab
(tether spin, release upside down) → head |rotation| stays high for the delay window,
then decays under the threshold within a bound; strike during the delay re-arms it;
unconscious → never rights. Record the M4+ self-righting animation wish in the M4 notes.

**Effort:** 1–1.5 days.

### B2 — Stop walking into the wall at room corners

**Implementation:** COMPLETE (2026-07-20).

**Owner report:** at a screen/room edge the buddy keeps walking into the wall; near a
corner the choices should be walk the other way or idle — never grind into the wall.

**Root cause (confirmed):** `AutonomousMotionPlanner` samples goals from fixed weights
with zero knowledge of position; a WalkLeft goal at the left wall pushes into it for the
goal's full tick duration (240–480 ticks ≈ 4–8 s of wall-grinding).

**Fix (domain-first, planner stays the decision authority):**
- Extend the planner input: `Tick(enabled, canWalk, canJump, blockedLeft, blockedRight)`.
- Planner rules: a *new* goal draw excludes blocked directions (weights renormalized over
  the remaining choices — idle absorbs the rest); a *current* walk goal whose direction
  becomes blocked ends immediately and reselects. No steering, no pathing — just "never
  choose into a wall".
- Godot side: `AutonomousMotionComponent` computes the flags from torso X against the
  walkable extent minus a profile margin (`WallAvoidMarginPixels`, suggested ≈ 1.5 ×
  torso radius). The walkable extent is injected at `Initialize` by the scene root that
  owns wall placement (lab and sandbox both know their bounds; use the same authority
  that positions the wall bodies — do not duplicate numbers).

**Determinism note:** extra random draws happen only when a wall is near, so open-floor
seeded traces are unchanged; but any scenario that parks the buddy near a wall may shift.
Run the full envelope set; if `repeat_envelope`/`idle_soak` move, the change is
behavioral-by-design — re-baseline only with the owner's nod, recorded in `DECISIONS.md`.

**Scenarios:** new `autonomy_respects_walls`: seed the buddy adjacent to each wall → over
a long soak, zero ticks of walk intent pointing into the blocked side while inside the
margin; goals still vary (idle and away-walks both occur). Domain tests for the
renormalization and the mid-goal cutoff.

**Effort:** 1.5–2 days.

### B3 — Grab should dangle the buddy from the grab point

**Implementation:** COMPLETE (2026-07-20).

**Owner report:** grabbed by the feet, the buddy stays standing upright in mid-air, and
can even rise while unconscious; expected: hangs limp from wherever he's held.

**Root cause (confirmed):** the tether (`GrabTetherController`) only pulls the grabbed
body toward the cursor; while conscious the drive keeps running underneath —
`ApplyUprightTorque`, `ApplyBalanceForce`, locomotion/gait, and during recovery
`ApplySelfRightForce` — all of which fight gravity and fake a mid-air stand. Nothing
tells `ActiveDriveComponent` a grab is live (only the *pose pipeline* samples the grab,
and that's presentation).

**Fix:** route the live grab into the drive decision on the authoritative clock:
`BuddyRoot`'s tick passes `grabActive` (the same `Grab.CurrentGrab.Active && Target is
PuppetPartBody` predicate the pose pipeline uses) into `ActiveDriveComponent.PhysicsTick`.
While a buddy part is grabbed **and** the buddy is not supported
(`Standing.Snapshot.SupportContactCount == 0` — feet on the floor while held by the hand
should still stand): suppress upright torque, balance, locomotion/gait, jump, and the
recovery assistance contribution. Keep the fear-driven `ApplyResistance` struggle — a
scared buddy wriggling while dangled is the charm, and it's horizontal so it doesn't fake
a stand. Unconscious rising fixes itself through the same gate (assistance suppressed
while held aloft).

**Implementation discovery (2026-07-20):** drive suppression alone was insufficient:
the custom passive links still applied their upright rest-offset springs and held the
torso above a grabbed foot with torso angle `0`. During an unsupported buddy-part grab,
the same routed dangle gate therefore puts those links in limit-only limp mode
(`Stiffness = 0`, existing damping/distance limit/max-force retained). This preserves the
approved six bodies and bounded custom-force solver while allowing gravity to arrange the
chain below the held part. Grounded grabs keep the normal structural springs.

**Owner decision to confirm before building:** grabbed-but-grounded behavior (keep
standing, as specced above?) and whether the struggle stays during dangle. Default: yes
and yes.

**Scenarios:** new `grab_dangle`: tether-grab a foot, raise the cursor well above the
floor → within a settle window the torso hangs *below* the grabbed foot, upright-torque
telemetry reads zero, and the parts chain roughly along gravity; release → normal
recovery. Unconscious variant: no rise while held. Existing grab/fear scenarios rerun.

**Effort:** 1.5–2 days.

### B4 — Eat must stand still and really use the hand (and wave, same mechanism)

**Implementation:** COMPLETE for Eat (2026-07-20); physical Wave reuse remains deferred.

**Owner report:** eat only shows the mouth moving; the hand doesn't participate, and the
buddy keeps wandering while eating. (Wave's invisibility is the same amplitude story —
A4 covers the stopgap.)

**Root cause (confirmed):**
- *Hands:* the eat clip is an offset-track decoration capped at ~2 px (A4) — but no
  offset can ever fake a hand-to-mouth reach, because invariant 2 caps visuals at 0.5 ×
  radius (7.5 px) from the physics hand. The *hand body* has to travel.
- *Walking:* `Eat` is presentation-selected only; the autonomy planner has no idea an
  activity is running, so walk goals keep firing and the offset clip rides a moving body.

**Fix (two seams, both gameplay-owned — this is the Class B contract M4 will reuse):**
1. **Physical reach:** during the Eat activity, drive the real right hand toward a
   mouth-relative target using the existing bounded hand-drive mechanism
   (`DriveHandToGuardTarget` generalizes: target = head position + profile offset,
   stiffness/damping/max already proven by the guard). The `ItemSocket` already rides the
   hand socket, which tracks the hand *body* — so the item visibly travels to the mouth
   with zero presentation change, and cursor picking/glove contact stay truthful. The
   chew nod and mouth overlay stay presentation. Wave reuses the same seam later (raised
   target + presentation beats); ship eat first, wave physical reach optional this pass.
2. **Stand still:** while a behavior-backed activity that declares "stationary" is
   active (Eat now; sit/sleep in M4), `BuddyRoot`'s intent arbitration holds autonomy:
   call the planner with `enabled = false` (its documented suppression semantics — the
   seeded stream pauses rather than burning draws) and zero the walk intent. The
   suppression lives in `BuddyRoot`, not the animator — M4's consume behavior gets it
   for free.

**Scenarios:** extend `activity_clips` / new `eat_reaches_and_stands`: trigger eat →
walk intent is zero for the duration and torso travel below a small bound; right-hand
body comes within a profile distance of the head and returns after; punched mid-eat →
Tracking cut still wins instantly (existing suppression already covers presentation; the
hand-drive must also drop with the reaction window). Item-agnostic check stays green.

**Effort:** 2–3 days.

---

## Suggested order and batching

| Order | Task | Why this order |
| --- | --- | --- |
| 1 | A2 defend gaze | Highest charm-per-effort; pure presentation. |
| 2 | A3 pet smile | Verification first; art data change. |
| 3 | A4 amplitudes | 15-minute data change riding A3's screenshot session. |
| 4 | A1 depth lanes | Contained; needs its own transform-contract care. |
| 5 | B4 eat/stand-still | Unblocks the M3.6 exit-gate eat item properly. |
| 6 | B1 head righting | Small, self-contained physics drive. |
| 7 | B2 wall avoidance | Domain planner change + envelope reruns. |
| 8 | B3 grab dangle | Broadest feel change; owner decision first. |

Group A total ≈ 2.5–3.5 days; Group B ≈ 6–8.5 days. One owner feel pass after A (gate
re-run candidate) and one after B.

## Owner decisions (resolved into `DECISIONS.md` on 2026-07-20)

1. **B3:** yes — grabbed-but-grounded still stands; yes — struggle stays during dangle.
2. **A3:** calm/default is `:)`; active rub is `:3`; no distinct completion string.
3. **B1:** approved two-second (`240` routed-tick) calm delay and gentle bounded tuning.
4. **B4/A4:** approved real-hand physics targets; visual-offset caps stay unchanged.

## Recorded for the future (not this plan)

- Cute self-righting animation: the buddy straightens its own head with its hands
  (replaces B1's plain ease; M4+ behavior + new clip on the activity system).
- Physical wave reach via the B4 hand-target seam.
- `standing_recovery`, `impact_dedup`, `desktop_shell_modes` pre-existing reds are
  separate follow-ups (see M3.6 Task 6 notes) — untouched here.
