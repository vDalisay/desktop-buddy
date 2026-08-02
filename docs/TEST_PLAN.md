# Desktop Buddy — Test Plan

Status: Handoff specification. Tests are implemented alongside the milestone that introduces each behavior.

## 1. Quality Gates

No milestone is complete until its automated tests pass, its required Windows checks pass, and its changed behavior is reflected in the project documentation. Physics feel is a release-blocking feature, not a polish task.

The test strategy has five layers:

1. **Pure C# unit tests** for mood, pain windows, payouts, economy, saves, statistics, unlocks, and state-transition policy.
2. **Headless Godot scenario tests** for rigid-body behavior, spring constraints, tools, containment, and scene wiring.
3. **End-to-end journey tests** that play the running game through the real input path via the first-party automation layer, per `AGENT_VERIFICATION_AND_E2E.md`; each milestone's journeys join its exit criteria.
4. **Standalone Windows tests** for transparency, input passthrough, focus, tray, DPI, monitor placement, and global hotkey behavior.
5. **Steam/depot tests** for startup fallback, Cloud boundaries, statistics, achievements, and the Steam overlay.

Interactive agent verification through the configured Godot MCP server is a required development workflow but is never a gate; its findings must be promoted into layers 1–3 (`AGENT_VERIFICATION_AND_E2E.md` Section 4).

The implementation must expose one command for pure tests, one command for headless Godot scenarios, and one command for end-to-end journeys, and document all three in `README.md` when the test runners exist.

## 2. Unit-Test Coverage

### Damage and Economy

- A rolling five-second pain window knocks the buddy out at `100` accumulated pain and not below it.
- Knockout lasts four seconds within one 120 Hz physics tick of tolerance.
- Hits during knockout pay `50%` and never extend the timer.
- Region multipliers are head `1.2x`, torso `1.0x`, and limbs `0.8x`.
- Held impacts pay normally; continuous contacts deduplicate per source/body with the `0.15`-second rule.
- Room-boundary, loose-object, projectile, and physical-weapon impacts enter the same thresholded/attributed pain pipeline.
- Each accepted harmful event applies `min(10, pain x 0.1)` mood loss.
- The rolling pain window clears on knockout; unconscious hits pay and affect mood but cannot trigger or pre-load the next knockout.
- Payout is derived only from pain, region, consciousness, and calibrated cash-per-pain.
- Income-window statistics produce correct best 1-, 3-, and 10-second totals.
- Currency never becomes negative; permanent purchases cannot be repeated, sold, or refunded.
- Milli-credit arithmetic, fractional accumulation, overflow guards, whole-credit display, and `0.25`-second `+$N.N` reward coalescing are exact and deterministic in pure C# tests.

### Mood, Trust, and Passive Income

- Mood clamps to `-100..100`, decays toward zero at `0.5` points per running minute, and does not decay while closed.
- Mood-band boundaries are tested at every endpoint.
- Passive multiplier passes through `0.25x` at `-100`, `1.0x` at neutral, and `2.0x` at `100`.
- Peak passive income remains approximately 25% of the approved active-play benchmark.
- Pet requires both the hidden distance threshold and three valid-contact seconds, resets without carrying excess, and applies `1.2x` distance only on the current per-selection favorite part.
- Tickle grants friendly mood at three/six valid-contact seconds, then applies negative mood every further three seconds while Angry; hop cadences and the eight-second no-contact reset match the confirmed values.
- Catch, Meal, Drink, and Repair Kit gains and cooldowns match the confirmed values.
- Care cooldowns start on successful consumption/use and remain available after cancel, miss, drop, or rejected spawn.
- Crossing upward to mood `60+` clears every harmful-history and per-tool fear record exactly once per crossing.
- Falling below 60 and crossing again permits another reset.
- Closed/suspended time produces no catch-up income or mood change; hidden-to-tray running time does.

### Persistence, Reset Progress, and Steam Abstraction

- Current save data round-trips without loss and older supported versions migrate explicitly.
- Schema-5 saves that own deprecated `upgrade.strength` migrate exactly once to ownership of `tool.power_grab`; new saves never emit the deprecated ID.
- Live pose, objects, projectiles, pain, knockout, and temporary statuses never enter progress data.
- Atomic replacement preserves the previous backup if a write fails.
- Corrupt primary data is quarantined, then backup/default recovery occurs without crashing.
- Reset Progress builds a fresh gameplay payload, validates it, writes it through the normal atomic-save path, and only then swaps the in-memory progress reference.
- A confirmed reset clears balance, purchases, selected tool, mood/fullness, harmful and novelty memory, traits, local gameplay statistics, local achievement-progress counters, and cumulative play/economy timers. The post-reset selection is Normal Grab.
- Reset Progress preserves language, audio, controls, accessibility, comfort, presentation, window, zoom, and dock preferences because those values are copied from the existing settings payload rather than reconstructed from defaults.
- Cancel, dialog dismissal, missing confirmation, validation failure, and save failure leave both memory and disk unchanged. Tests compare complete before/after snapshots, not a subset of fields.
- Cloud payload excludes machine-specific settings.
- Already-awarded platform achievements are never revoked by Reset Progress; only local progress counters are reset.
- Steam initialization failure selects the local implementation.
- Offline stat/achievement events queue once and synchronize idempotently after reconnection.

## 3. Headless Physics Scenarios

Every scenario uses seeded scripted inputs and asserts ranges/tolerances rather than bit-exact transforms.

### Puppet Stability

- Spawn and settle at each supported zoom level.
- Remain finite, connected, and contained during a 30-minute accelerated idle soak.
- Never exceed the configured maximum-stretch bound.
- Walk left/right, jump, land, and remain recognizable without self-collision instability.
- Near either room wall, ambient autonomy never emits walk intent into the blocked side;
  an active goal cuts immediately, while idle and away-walk choices remain reachable.
- Run the same scenario repeatedly and keep outcome metrics inside the approved envelopes.
- Buddy parts never enter physics sleep; settled loose objects do sleep, and eviction/registry behavior stays correct on sleeping bodies.

### Grab and Recovery

- Grab and release each of the six parts and at least one loose object.
- The tether stretches under fearful resistance but does not break solely from resistance.
- An unsupported buddy-part grab disables upright/balance/locomotion/recovery and fear
  resistance while retaining passive structural springs, so the conscious rig behaves like
  an unconscious ragdoll without its parts sliding to their link limits. Release restores
  normal drive. A supported grab retains standing and conscious reactions.
- Normal Grab and Power Grab share acquisition, tether, cancellation, safety recovery, and maximum limb-stretch limits.
- The active grab mode is resolved from the selected tool at acquisition and stored as immutable per-grab settings. Changing selection cancels the current grab; it cannot mutate a live tether.
- Power Grab accepts buddy parts and eligible loose objects, applies the tuned stronger force profile, and still visibly drives the buddy's fear/struggle presentation.
- A buddy cannot escape Power Grab from the normal stretch/snap timer. Saturating counters and a long-duration scenario prove that disabling voluntary escape cannot overflow or disable hard safety recovery.
- Intentional Power Grab release applies its separate release multiplier and higher safe speed cap. Cancel, invalid target, hard recovery, scene exit, and input loss use the non-powered safety release.
- Power Grab changes no damage, payout, mood, statistics, or economy multipliers; equivalent downstream collision samples produce equivalent downstream results.
- Release velocity is preserved and capped.
- A real head grab/release and an accepted head impact each start a two-second calm window;
  bounded head-righting torque begins only after it, restores a conscious head, re-arms on
  a new impact, and never runs while unconscious.
- After two seconds unable to stand, assisted recovery begins; hard recovery does not occur before ten seconds unless state is invalid/out of bounds.
- Assisted recovery reaches full strength in one second and restores standing within the owner-accepted `2.0`-second physical scenario bound.
- After knockout expires, active drive ramps back without a teleport.
- NaN, infinite, or escaped-body injection triggers immediate safe recovery.
- Hard recovery releases grabs/held objects, clears transient physics/pain/knockout/status state, and preserves persistent progress.

### Tools and Collision Attribution

- The `boot_smoke` push gate validates the complete authored launch catalogue and
  requires the owner-accepted Baseball Bat to be present in the filtered shop entries.
- Glove and bat damage comes from physical contact rather than tool activation.
- Boxing Glove cursor lag remains within the accepted response envelope; faster real strikes increase measured impulse/pain; the impact marker is centered on the solver contact point; maximum-pain/knockout hit-stop has a visibly slow early curve, is non-stacking, and restores the prior time scale.
- Learned Boxing Glove harm raises both real hand bodies into a body-relative guard while the buddy travels away from the pointer. The guard direction follows pointer changes with bounded lag, never attaches to the physical glove, and applies an equal reaction so guard actuation cannot pull the puppet toward the threat. Guard-hand contact applies `0.5x` accepted impulse plus a matching physical counter-impulse; strikes around the hands remain unmodified.
- After a learned-harm Boxing Glove pointer leaves the play area, its `o_o` threat face persists for exactly five seconds (`600` routed ticks) and then returns to the ordinary reaction/mood face without clearing harmful-tool memory.
- The physical head rotates while the emoticon remains upright; Pet/Tickle cursor hands follow real pointer input instantly beneath the visible OS cursor.
- Favorite-spot Pet contact emits small sparkles around the Pet hand only while held rubbing contact remains valid.
- Pistol/shotgun cadence, magazine, reload, pellet count, and no-tunneling behavior match the specification.
- A trigger press while a gun has no established aim is refused rather than spending a round
  (`DECISIONS.md`, "Gun Feel Refinement"), and `CursorGunComponent.ShotsSpentWithoutAim` stays
  at zero as the standing proof of it.
- Pistol/shotgun fire once per primary press, reload with `R`, and auto-reload after an attempted empty shot.
- The `pistol_fire` scenario is the cursor-gun platform gate. It proves that drawing the
  Pistol arms a full magazine, that aim follows pointer motion and a wheel notch offsets it
  until the next motion clears it, that eight shots empty the magazine without starting a
  reload, that the ninth pull dry-fires into the automatic reload, that mid-reload presses are
  ignored and the reload still completes on its authored tick, that a real projectile's
  measured impulse scores pain attributed to `tool.pistol` in harmful history and statistics,
  that a point-blank shot stops in the target instead of passing through it, that bullets never
  change `LooseObjectRegistry.Count` and peak inside their own pool before returning to it, and
  that a holstered gun cannot fire. It also pins the two defects the owner reported after the
  first Pistol build: a click with no established aim spends no round
  (`pointer_reentry_click_without_motion_spends_no_round`, reproduced through the same pointer
  exit and re-entry a sweep across the play area causes), a first click after that fires along
  the new aim (`right_then_left_first_click_fires_left`), and a bullet is drawn along the path
  it is really flying (`the_bullet_visual_stays_glued_to_its_flight_path` — the body is free to
  spin, since locking it halves the impulse the pain pipeline scores, so the claim is about the
  drawing). The `m5_pistol` journey repeats the slice through real pointer, wheel, button, and
  key input, including both the `R` reload and the dry-fire reload, and opens by confirming
  the shop **sells** both guns at their authored `12` and `30` credits now that the owner has
  accepted them (`the_shop_sells_both_guns_at_their_authored_prices`). That leg and the
  Grenade's share `JourneyRunner.BuysFromShop`, which runs the sale against a fresh progress
  state: the laboratory grants every implemented M5 tool at boot for mechanical tuning, so
  asking it to buy one answers `AlreadyOwned` and proves nothing about the shop.
- The same scenario pins the **aim feel** the owner reported as "choppy, as if locked to
  different axes", with three checks that fire no shots because they are properties of the aim
  rather than of the gun. `slow_leftward_travel_steers_the_aim_left`: pointer travel of under a
  pixel per tick — everything the retired raw gate discarded, up to 120 px/s of deliberate
  aiming — turns the aim all the way round (measured 82 ticks at 0.49 px/tick).
  `aim_never_flips_on_release_jitter`: a pixel of backward slop as the hand lets go, then
  stillness, leaves the aim within a degree of where it was (worst alignment `0.999`), where the
  old last-raw-delta aim turned completely round on it. `sustained_reversal_completes_within_expected_ticks`:
  a full reversal costs 39 ticks, pinned from **both** sides against the authored constants —
  never fewer than the slew allows (`ceil(180 / MaxAimTurnDegreesPerTick)` = 30, so the aim
  cannot have snapped) and never more than that plus three smoothing half-lives (72). The turn
  rate is the owner's co-tuning dial, so the measured count is re-recorded here whenever it
  moves. Each check was confirmed to bite by mutation: removing the smoothing fails only the
  jitter check (`0.978`), removing the slew fails only the reversal check (10 ticks), and
  restoring the retired 1 px/tick gate fails only the slow-travel check (the aim never turns).
- The `nerf_versus_pistol` scenario is the gate on the two guns being **one platform with two
  authored profiles**. Both are selectable through the real lab keys (`N` and `J`), each keeps
  its own magazine across a swap, darts droop under their authored gravity while bullets fly
  flat, and the Nerf retains the pre-split impact-driving `2400` px/s speed and `0.3` mass.
  A three-shot point-blank volley must connect and score positive physical pain/payout through
  the ordinary shared solver path while its accepted early hits each add `+0.25` mood and do
  not enter harmful history. The real-Pistol volley must lower mood, enter harmful history,
  and start the semantic sad reaction. Seeds `1/7/13` measured best dart impulse
  `1172.6`–`1194.0` and pain `40.68`–`41.61`. Pure domain coverage pins hit `20` as enjoyed,
  hit `21` as annoying with the shared pain-sized mood loss, misses as absent from the model,
  no reset at `9.999` seconds, and reset at exactly `10` seconds. Aimed shots re-derive their stand-off until the
  barrel really points at the head: the buddy walks over to an engaged cursor, and an aim
  takes most of a second of pointer travel to establish. Trajectory drop is measured relative
  to the actual launch vector so sub-degree aim pitch cannot hide the faster dart's `1.24 px`
  gravity bend across its 15-tick visible flight.
- The `gun_visuals` scenario holds the drawn gun to the gameplay underneath it, and runs in
  **both** presentation modes because the 3D presenter and the legacy 2D drawing are two views
  of one weapon: the barrel points where the aim points (`gun_visual_faces_the_slewed_aim`),
  the grip hangs downward whichever way the gun faces (`gun_is_never_upside_down`), and the
  rendered basis remains positive in both directions so lighting normals cannot invert
  (`gun_visual_keeps_a_positive_lighting_basis`, determinant `+1.000`). Rounds are born at the visible barrel
  mouth within 3 px (`rounds_are_born_at_the_visible_muzzle`, measured `0.00 px` for both
  guns), the two modes put that muzzle in the same place, and no mesh vertex escapes the
  authored envelope. The last one is the bat's rule restated for a box: presentation may not
  reach further than the dimensions the data says the weapon has. Note for anything aiming a
  gun in a test: the grip sits at the cursor, so a round is born 53–61 px **ahead** of the
  pointer and a stand-off has to include the barrel or the shot starts past its target.
- The `pistol_punctuation` scenario is the gate on the real Pistol's presentation-only fire
  feedback, and runs under seeds `1/7/13` in both presentation modes. Four rapid shots restart
  rather than add their camera envelopes (measured peak `1.500 px` against the one-envelope
  `2.175 px` magnitude bound), and the camera returns exactly to its layout position. A real
  launched round starts the three-tick muzzle flash; a dry fire starts none, and the 3D run
  observes the actual flash node. A reload ejects one preallocated cosmetic magazine which
  rebounds and remains quiet in the floor band for 30 consecutive ticks plus a 90-tick hold,
  stays on collision layer `0` with mask `RoomBounds`, produces zero buddy contacts/scored
  impacts when probed through the buddy, never changes `LooseObjectRegistry.Count`, and
  returns to its three-slot pool after 600 ticks. The Nerf Blaster authors and produces none
  of these effects. The magazine uses shape-cast CCD because it has no gameplay impulse to
  preserve and must not tunnel through the thin floor during a routed-gameplay pause.
- **Aiming a gun in a test means moving its pointer, and that is a contract, not a detail.**
  The aim follows the direction the pointer has lately been travelling and turns at a bounded
  rate, so a cursor that teleports into position aims at wherever the jump pointed. Every
  scenario goes through `M4ObjectScenarioSupport.AimGunOver` and every journey through
  `JourneyRunner.AimAtPointAsync`: jump, let the aim come to rest, then sweep long enough to
  come round from any previous direction, standing off on whichever side has room behind it.
  Aimed shots are taken at the head from close in — a horizontal chest shot grazes the hands
  hanging beside the chest for an impulse under the curve's floor, and the bullet spends
  itself on the graze, so the buddy is hit and unhurt.
- A gun's no-tunneling guarantee is asserted as an outcome, not as an engine setting:
  `RigidBody2D.ContinuousCd` is deliberately off because it destroys the momentum the pain
  pipeline scores from, and `GunProfile` instead validates that a projectile's per-tick travel
  stays inside the smallest buddy part's diameter (`DECISIONS.md`, "Cursor-Gun Platform and
  Pistol").
- A pinned grenade never detonates, however it is thrown or caught. The fuse begins on the
  routed tick player control of a **pin-pulled** grenade ends — launch release or grab release —
  and expires exactly `360` routed ticks later, unaffected by any catch or re-grab (owner
  amendment 2026-07-31, superseding the 2.5-second launch fuse; `DECISIONS.md`, "Grenade — Pin
  Mechanic, Post-Release Fuse, and Blast").
- The `grenade_fuse` scenario is the Grenade gate, run under seeds `1/7/13` in both
  presentation modes. It proves the pin rules on the real composition and through the real
  input chord: a grenade flung with primary alone is soaked for `720` ticks and never goes off
  (`pin_in_grenade_never_explodes`); the first secondary press drops exactly one pin and later
  presses drop none (`pin_drops_on_first_rmb_press`), and that pin is on collision layer `0`
  with mask `RoomBounds`, produces zero buddy contacts and zero scored impacts when probed
  through the buddy's chest, and never changes `LooseObjectRegistry.Count`; a pin-pulled
  grenade held for `720` ticks stays safe (`held_live_grenade_never_explodes`); and letting go
  runs the fuse for exactly `360` routed ticks even when the player picks it back up mid-count
  (`fuse_runs_360_ticks_from_release_and_a_regrab_does_not_stop_it`). A live grenade is
  registry-protected and survives two dozen fresh objects being piled onto a full registry,
  then frees its slot when it detonates.
- The same scenario measures the blast, because the falloff curve is the only authored blast
  quantity and everything else comes from the shared pain curve. Point-blank at the head it
  scores `186.21`–`190.65` total pain over all six parts on seeds `1/7/13` — about five solid
  aimed pistol bullets (`40.5`–`42.3` each) — crossing the `100`-pain knockout window, and at
  the buddy's hand `223.31`–`225.16`, which is what "it goes off in whoever's hand holds it"
  is worth. At `155 px` it scores `4.39` on one part and knocks nobody out. The blast shoves
  every dynamic body on `BuddyParts | LooseObjects` and scores **only** buddy parts, all
  attributed to `tool.grenade`; what a shoved object hits afterwards is ordinary physics, so
  the attribution check is narrowed to the detonation tick. The shove is measured as the
  speed a witness object *leaves* at rather than where it ends up: held at a known `35 px`
  from the centre — inside the full-effect radius, where the falloff is `1` — it leaves at
  `1750.8 px/s` against the authored `1800` impulse over its `1.0` mass, and how far it then
  travels is a story about which wall it met. That measurement replaced a settled-distance
  reading that moved *down* when the owner doubled the shove, because the object was
  bouncing back off a wall inside the sample window (owner feel gate, 2026-07-31). The camera
  kick peaks at the authored `4.000 px` and four back-to-back restarts stay inside one
  envelope, the expanding ring reaches the real `48 px` full-effect radius (measured
  `45.60 px` in 3D, `48.00 px` in legacy), the boom counter equals the detonation count, and
  a grenade rolling on the floor adds no thuds. No grenade mesh vertex escapes its stated
  envelope — now `1.35 x` the **drawn** radius, `17.50 px` for a `10 px` collider — pin in or
  pin out, and the dropped pin is drawn exactly once: as a mesh in `Mii3D` with the flat body
  dark, and as its own flat ring in legacy with no mesh
  (`the_dropped_pin_is_drawn_once_in_the_active_presentation`).
- The `m5_grenade` journey repeats the slice through real pointer, button, and key input on
  seeds `1/7`, in both presentation modes: the shop **sells** a grenade at its authored `40`
  credits to a saveless buyer holding exactly that — the leg asserted the refusal an invisible
  catalogue entry produces until the owner accepted the tool on 2026-07-31, and it is
  exercised against a fresh progress state because the laboratory grants every implemented M5
  tool at boot and would answer `AlreadyOwned` — a buddy that has never met one is curious and catches a **pinned** grenade like
  a ball, the pullback chord's first secondary press pulls the pin so the throw starts the
  three-second fuse, the blast hurts the buddy and pays for it (`200.91` pain, `146067`
  milli-credits), `tool.grenade` enters harmful memory, and the buddy then leaves the next
  grenade strictly alone for `600` ticks.
- The `soccer_and_drink` scenario is the Task 8 gate, run under seeds `1/7/13` in both
  presentation modes. Its centrepiece is a **measured** signature rather than an asserted one:
  both balls are dropped `240 px` above their own resting height, on the far side of the room
  and inside the registry's own ignore window so the buddy cannot fetch the fall, and the
  number of rebounds, the highest rebound, and the routed ticks to registry rest are recorded.
  The Baseball lands dead (`0` rebounds, `0.0 px`, `153` ticks) and the Soccer Ball does not
  (`6` rebounds, `82.1 px`, `417` ticks). The Baseball reading is the regression band for the
  restitution seam itself (`bounce_zero_objects_did_not_change`): a profile that authors no
  bounce is given no `PhysicsMaterial` at all, and the Baseball is expected to keep landing
  dead forever. The scenario also proves key `8` places, launches through the shared pullback
  chord using the ball's **own** authored preset (`VelocityPerPullPixel 11.5`, measured launch
  `1035 px/s`), and settles; that the Soccer Ball never enters the buddy's ordinary
  catch/scoop/hold lifecycle outside the explicit corner-rescue case;
  and the Drink's whole care contract — key `9` places one, a buddy at `200/200` fullness
  still accepts it (`ConsumeHungerFill = 0` means `TooFull` can never fire), abandoning it
  mid-drink starts no cooldown (FR-008.10), a Meal and a Drink taken back to back both
  succeed, the Drink's running `7200`-tick cooldown does not gate the Meal (per-content-id
  cooldown slots), and a second Drink inside the minute is refused `OnCooldown` for no mood.
- The same scenario covers the 2026-08-01 owner feedback. The **soccer loop** is driven through
  real physics — a ball spawned on the floor and given a real velocity, no component commanded
  directly — and measured end to end: it arrives rolling at `234 px/s`, is stopped dead under
  the foot (`fastest_while_trapped` `0.00`, so the trap writes the velocity to zero rather than
  merely slowing it), held for `119` of the authored `120` dwell ticks with arbiter priority 5
  owned throughout, and then kicked at exactly the authored `520 px/s` away from the buddy —
  verified twice over, by the sign of the commanded velocity and by the buddy-to-ball gap
  measured a beat later (`52 → 171 px`). The loft is one of three seeded choices and stays
  inside the authored `24°`; seed `1` picks `12°` and seeds `7`/`13` pick `24°`, which is the
  randomization the owner asked for. A companion check asserts that **no other** loose object
  profile opts into the beat. The **Drink gesture** is measured on the consume that actually
  completes: exactly `1` raise to the head and `244` ticks held there against the authored
  `240`, then the can is gone. The **models** check asserts both items are adopted by
  `LooseObjectVisual3D`, that no other object is, that every mesh vertex stays inside the
  builder's stated `1.80 x` envelope, and that the mesh and the flat circle are complements —
  exactly one silhouette per presentation mode, which makes the verdict identical in both.
- The follow-up owner correction makes the Soccer Ball foot-only. Registry provenance proves
  player Grab/launch contact enables trapping, floor contact preserves it, and either side wall
  or the ceiling clears it. A cleared ball rolling into foot range is kicked without a trap,
  while the ordinary object lifecycle never tracks or holds the ball.
- The good-mood follow-up is covered in the same real-physics scenario. A Content/Delighted
  buddy closes on a free ball and kicks it without ever picking it up; seeded pure-model checks
  cover straight and non-zero arc choices. A corner check requires the explicit exception:
  hand pickup, continuous held-ball gaze during inward carry/facing, placement in front, and
  an inward kick. While the player
  continuously holds the ball, the composed buddy keeps it as the semantic head/eye target,
  retreats for `600` routed ticks, pauses for `120`, resumes retreat, and leaves ownership with
  the player (`61 → 139 px` before the room boundary in the scenario). Release restores chase
  within ten ticks. The `m5_soccer_ball` journey repeats the full cadence through the real Grab
  path (`94 → 253 px`). The check now requires the **applied rendered head yaw** and the
  **composed visible pupil offset** to point toward the ball; a windowed post-draw capture at
  `.artifacts/soccer-windowed-visual4/soccer_receive_tracking.png` was inspected with the ball,
  open eyes, head turn, and both pupil highlights aligned right. The same scenario proves the
  obstacle ray rejects the football but accepts a Baseball, while the registry fallback also
  excludes the football.
- The receive check samples every travelling tick after a 30-tick gaze-acquisition allowance:
  the item source may never drop, head yaw must remain aligned, and every open-eye pupil frame
  must point at the player-held ball. The corner check applies the same per-tick rendered-gaze
  rule while the rescued ball is carried. Pure-model coverage proves player takeover aborts a
  committed rescue.
- The follow-up readable-gaze gate inspects a real windowed post-draw frame. The head must stay
  front-readable while looking back during retreat, with white eye shapes and distinct dark
  pupils visibly displaced toward the ball; semantic or head-angle evidence alone is rejected.
- Soccer-ball art is inspected in held and unobstructed post-draw frames. The mesh must read as
  a white ball with a centered black pentagon and evenly distributed surrounding panels, with
  no random dark facet blocks, while remaining inside the authored visual envelope.
- The Meal path is unchanged by the gesture refactor and is proven so rather than assumed:
  `meal_consume` still measures four meals to a full bar and its five-bite refusal choreography,
  `consume_care_cooldown` still lands care on bite five, and `activity_clips` is green.
- The `m5_soccer_ball` and `m5_drink` journeys repeat both slices through real pointer,
  button, and key input on seeds `1/7`, in both presentation modes. Each opens on the refusal
  an invisible catalogue entry produces — both entries stay `Visible = false` until the
  owner's feel gate, on the Grenade's precedent — and then exercises the happy path and its
  paired failure path: for the ball, spawn, player carry, good-mood receive cadence, a secondary
  tap with no pull that keeps the carry, a real pullback launch, and proof that the buddy never
  hand-attaches it; for the Drink, spawn to a
  completely full buddy, `+5` mood with no stomach filled, and a second can inside the minute
  refused for the timer without costing mood or restarting the wait.
- The `shotgun_spread` scenario is the M5 Task 9 Shotgun gate, run under seeds `1/7/13`. The
  cadence half of the slice is a profile table on the shared `GunMachine` — capacity `5`,
  `108`-tick (`0.9 s`) interval, `240`-tick (`2 s`) reload, `6` pellets — so the scenario spends
  most of its checks on what is not data. `six_pellets_leave_on_one_press_inside_a_randomized_cone`
  proves that every shot selects a fresh seeded-random half-angle inside the authored
  `12–20°` band and every pellet remains inside that shot's cone; five shots must produce
  more than one cone while a repeated seed reproduces the same sequence.
  `every_pellet_of_one_press_shares_one_interaction_id` reads the shared identity directly.
  `point_blank_one_part_scores_exactly_once` is the dedup consequence stated out loud — six
  pellets into one head are **one** accepted impact, not six — and
  `a_mid_range_burst_scores_once_per_covered_part` is the other half: a burst that covers `N`
  parts scores exactly `N` times, one per part, never twice on the same one. That second check
  fires a volley rather than one shot, for the reason `nerf_versus_pistol` gives — the buddy
  walks, and establishing a cursor aim costs the best part of half a second — and the dedup
  invariant is checked on **every** burst that landed while the coverage count comes from the
  best of them. Measured coverage is `2` parts at seeds `1/7/13` (head plus hand, or torso plus
  hand); it is reported, never assumed to be six.
- The same scenario records the Shotgun's **pain band**, which is the number the owner's feel
  gate tunes against: one solid pellet scores `7.2–9.1` pain (seeds `1/7/13`) against a
  point-blank pistol bullet's `13.8–13.9` on the same shared curve, and the best two-part burst
  totals `9.0–26.0`. There is no per-tool damage anywhere; the difference is authored muzzle
  speed and pellet mass only. It also pins `point_blank_pellets_never_tunnel_through_the_target`
  (the `pistol_fire` geometric test applied to six bodies at once),
  `every_shot_ejects_a_red_shell_that_cannot_touch_the_buddy` (the cosmetic casing lane's
  rules — red shell body, layer `0`, `RoomBounds` mask, a contact probe on the chest that must
  do nothing, and never a loose-object slot), the click-after-shot pump state and visible
  forend stroke, `shotgun_knockback_falls_with_travel_but_never_below_the_old_physical_hit`
  (six point-blank `600` impulses equal twice the Grenade's `1800`, middle distance is lower, and
  the extra reaches zero without subtracting the original contact), and
  `the_shotgun_kick_reads_bigger_than_the_pistol_and_never_stacks` (`3.0 px` against the
  Pistol's `1.5`, three shells back to back peaking inside one envelope).
- `ContactSettleTicks` is `4` for the Shotgun rather than the Pistol's `2`, and that is a
  correctness value rather than a taste one: at `2` a pellet that had connected was withdrawn
  from the world before the solver resolved the real impulse, so a burst the player watched
  land delivered nothing. Point-blank shots happened to survive it; everything past arm's
  length did not. Lowering it needs this scenario's coverage leg re-measured, not just a
  smaller number.
- The `m5_shotgun` journey repeats the slice through real pointer, button, and key input on
  seeds `1/7`: the shop still **refuses** a sale because the catalogue entry stays
  `Visible = false` until the owner's feel gate (the Grenade's leg had exactly this shape
  before acceptance), the `L` lab key draws a loaded five-shell magazine, pointer travel aims
  it, one primary press releases six pellets carrying one interaction identity and ejects a
  red shell, the next real-input click cycles the pump, a burst hurts the buddy and enters
  harmful memory as `tool.shotgun`, `R` reloads a partial tube, an emptied tube dry-fires into
  the two-second automatic reload, and Grab holsters it.
- Fire duration refreshes from four seconds up to the eight-second cap; Repair Kit clears it.
- The `burning_status` scenario is the Fire Sprayer and Burning gate (M5 Task 7), run under
  seeds `1/7/13` in both presentation modes. It proves on the real composition that the
  stream runs only while primary is held and stops on the tick it is released (`60` held
  ticks emit exactly `15` droplets at the authored `4`-tick interval, and none afterwards);
  that droplets are bounded by their own `48`-slot pool and never change
  `LooseObjectRegistry.Count` over a five-second spray; that contact grants `480` ticks and
  sustained contact pins the remaining duration at the `960`-tick cap without ever exceeding
  it; and that **a droplet never scores an impact** — every accepted `tool.fire_sprayer`
  event is a burn tick carrying the burn's own interaction id, so one stream cannot
  double-dip as both impact pain and burn pain.
- The same scenario measures the burn economy, because `BurnEquivalentImpulse` is the only
  authored damage quantity and everything after it is the shared curve. At the authored
  `430` it scores `4.57` pain per event against the shipped conversion profile: `36.6` over
  a full four-second burn, `73.1` over a sustained eight-second cap burn, and at most `45.7`
  inside any rolling five-second window — so `a_single_part_full_cap_burn_never_knocks_out`
  is proven rather than assumed. The owner-confirmed exception is also exact: all six parts
  alight together remain conscious through tick `599`, force unconsciousness on tick `600`,
  and release that hold only when the fire subsides. Mood loss is measured across the single routed tick
  one event lands on, not across the whole burn, because persistent mood also carries the
  shared passive drift; on that tick it is exactly `min(10, pain x 0.1)`.
- Per-part scorch presentation also covers the visual connectors: a hand darkens its arm, a
  foot its leg, and the head its neck. The same scenario asserts torso scorch does not fan out
  to every connector.
- Burning's panic is one snapshot bool, so the scenario asserts it through the *real* ladder:
  a buddy holding a ball is set alight, priority `3` takes actuation from `ObjectAction`, and
  the ball is released by the existing higher-priority abort. Burning survives a knockout
  (a KO'd buddy lies there and keeps taking attributed ticks) and is cleared by the
  centralized hard reposition, which is the sentence `DECISIONS.md` "Fail-safe cleanup"
  already promised.
- FR-017.3 is asserted as a *negative*: the same captured standing pose, pinned so ambient
  autonomy cannot move it, is sprayed twice under `EffectsSettings.Default` and
  `EffectsSettings.MostRestrictive`, measured over five whole pain intervals counted from
  ignition. Both runs produce the same `5` events, the same `22.857` pain, the same `75`
  droplets and the same burn at the cap; only the drawable-droplet count differs (`48` to
  `16` under reduced particles). The flicker cap is measured at `3.00 Hz` in both
  presentation modes while photosensitivity-safe effects are on — which is the shipped
  default, so the safe look is the normal look and the faster `8 Hz` flicker is the opt-out —
  and `ScreenShake = false` silences the whole `CameraKickComponent` lane, pistol and grenade
  kicks included, rather than only this tool's effects.
- The same scenario covers the scorch marks a burnt buddy carries (owner feedback
  2026-08-01), measured against the pinned pose because the hold and the fade are exact tick
  counts and a wandering buddy would be measuring the walk instead. A part in the stream
  darkens monotonically — sampled `0.002, 0.182, 0.362, 0.542` on the way to the authored
  `0.72` ceiling — and **only** that part is marked, so a stream that moves from a hand to
  the head leaves two marks at different strengths rather than darkening the whole buddy.
  The tint is asserted in both presentations at once: the legacy circle's drawn fill and the
  3D part's own lit material both leave the authored skin colour, and a part that was never
  in the stream keeps its exact authored albedo. Letting the fire go out naturally, the mark
  holds at full strength for the whole authored `1200`-tick hold and then fades to clean skin
  in `597` of its authored `600`, with the 3D albedo landing back on exactly the authored
  colour rather than near it. A hard reposition wipes a mark that has not finished fading,
  on the same `Clear()` entry point that puts the burn itself out.
- The Fire Sprayer presentation check requires a visible cylindrical canister and a live
  shader-backed cloud with positive upward vapor lift while spraying. These remain visual
  assertions; the settings-parity check continues to pin identical gameplay.
- The Burning presentation keeps two shader-cloud fire puffs on every part touched during the
  current episode and sends older puffs upward as smoke. The panic check also requires both
  panic-hand targets and an authored locomotion multiplier above `1.0` (`1.35` currently),
  while the ordinary burn pain and settings-parity assertions remain unchanged.
- The `m5_fire_sprayer` journey repeats the slice through real pointer, button, and key input
  on seeds `1/7`, in both presentation modes: the catalogue carries `tool.fire_sprayer` at
  its authored price but does **not** advertise it (the entry stays `Visible = false` until
  the owner's feel gate passes, so the leg asserts today's real promise and flips to a sale
  by editing one authored flag); the `S` key draws it — `S` rather than the plan's suggested
  `H`, which already toggles the laboratory telemetry panel; pointer motion aims it on the
  shared cursor-weapon convention; holding primary sprays a stream that sets the buddy
  alight; the burn hurts and pays (`4.57` pain per event) and enters harmful memory; the
  burning buddy panics at priority `3`; releasing primary stops the stream on the tick it is
  let go, which is this tool's whole cancel path; and holstering the sprayer does not put the
  fire out.
- The `repair_kit` scenario is the M5 Task 10 Repair Kit gate, run under seeds `1/7/13`. The
  kit is authored data on the shared consume machinery — `20` mood, cooldown `0`, hunger fill
  `0`, which together are the owner's "it is not food, so nothing rations it" — so the scenario
  spends its checks on the one new field and the one new route.
  `meal_and_drink_do_not_clear_statuses` pins `ClearsHarmfulStatuses` to the kit alone, and
  `clearing_statuses_is_rejected_on_something_that_cannot_be_taken` proves the flag is
  validated as meaningful only on a consumable. `a_full_buddy_still_accepts_one` feeds a buddy
  pinned to a full stomach, because appetite gates food and this is not food.
- The same scenario covers the **player-thrown route**, which exists because the two buddies a
  Repair Kit is for are the two that can never eat one: a knocked-out buddy is priority `1` and
  a burning buddy flees at priority `3`, and both outrank the object action that picks food up.
  `a_thrown_kit_applies_on_buddy_contact` (mood once, kit despawned, registry slot freed),
  `a_missed_throw_applies_nothing_and_waits` (FR-008.10 — the kit lands and stays a loose
  object), `double_contact_cannot_double_apply` (the `CareConsumableModel` token, shared with
  the eating route so the two cannot drift), and
  `kit_contact_scores_zero_impacts_and_no_harmful_memory` — a medkit that bruised would enter
  itself into harmful memory and teach the buddy to flee the thing that heals it.
- The healing legs are `burning_buddy_is_cured_and_cheered` (FR-010.10; mood is asserted as a
  range because the fire charges its own loss every tick right up to the moment the kit lands),
  `clears_do_not_touch_money_stats_or_history` (healing is not a payout, not a statistic, and
  not forgiveness — the sprayer stays in harmful memory), `a_kit_empties_the_rolling_pain_window`,
  and `knockout_is_never_shortened` (FR-008.7). Two measured facts shape those last two:
  **rolling pain during a knockout is always zero** — `EnterKnockout` empties the window and
  unconscious hits never enter it — so the clear is proved on a conscious buddy, and the knockout
  leg proves only that the kit is not a wake-up call; that the model cannot move the end time is
  a domain fact with its own unit test and is not re-proved here. Those two legs run in their own
  controlled-impact laboratory, sequentially, because measured pain needs an authored curve and
  two laboratories at once share the 2D space. That laboratory's buddy hangs where it can never
  stand, so its recovery clock always runs out mid-knockout; the watch on "still out" stops at
  that fail-safe reposition rather than counting it as an early wake.
- The `m5_repair_kit` journey repeats the slice through real pointer, button, and key input on
  seeds `1/7` in both presentation modes: the shop still refuses a sale because the catalogue
  entry stays `Visible = false` until the owner's feel gate (the Grenade's leg had this shape
  before acceptance), key `0` places one owned kit, a kit flung at the far wall heals nobody and
  waits where it lands, and a kit thrown at a buddy that has just been set on fire puts the fire
  out and pays its authored `+20`. The curing throw is solved onto the chest rather than dragged
  there with the pointer: a burning buddy is running away, and chasing it measures the aim of
  the throw instead of what the kit does when it lands.
- Pullback launch direction is opposite the drag vector and its preview matches the resulting ballistic path within the configured tolerance.
- The `baseball_pullback` scenario drives Baseball through the real pointer input path and
  verifies key-`5` cursor spawning without selection changes, single-ball replacement,
  normal Grab acquisition, the buddy's player-ownership guard, secondary-held trajectory
  aiming, opposite-drag launch with automatic Grab release, Baseball attribution, positive
  pain, and measurable whole-buddy pushback.
- The `bat_swing` scenario drives Baseball Bat on the shared cursor-tethered tool
  mechanism and verifies its own elongated collider and content ID, that a bat parked
  against the buddy scores nothing over a full second (proved non-vacuous by measuring
  the surface gap, not merely the absence of pain), that a real swing scores pain
  attributed to `tool.baseball_bat` with the barrel held square to its travel, that
  harmful history and pain statistics record the bat and never the glove, and that
  selecting another tool replaces the collider and its identity together. The
  `m5_baseball_bat` journey repeats the slice through real pointer and key input.
- The `homerun_bat_feel` scenario runs at `--fixed-fps 120` and is the cumulative
  charged-bat host. Task B proves the weak FOLLOW anchor bounds high-speed pointer
  input without suppressing positive physical pain, GRIPPED contacts cannot score,
  the directed servo holds the barrel upright from the derived handle point, release
  returns to centre-anchored weak FOLLOW, solver contacts retain their immutable
  swing context across the one-tick observation delay, and the Boxing Glove keeps
  its accepted response envelope. Task C proves the exact `599/600/601` five-second
  charge boundary, eased render-only shake cap, ordered `7/12/18` px geometric tip
  glimmers at exact `120/360/600` milestones in both presentation modes,
  cursor-travel direction with sub-threshold hysteresis,
  release-time direction locking, mirrored charge leans, and grip-release cancel
  without swing or pain. Task D proves absolute/non-overlapping low/mid/full tip-speed
  targets, the full-charge pivot-drift bound and CastShape CCD, laboratory-set impulse
  and whole-buddy-travel ratios, up-and-away launch angle, weak-vs-full separation,
  modest tap, point-blank/one-radius tunneling coverage, charge-scaled loose-object
  travel, no-multiplier tip/barrel/handle evidence, exactly one positive hit across
  several buddy parts, zero-pain-graze epoch retention, immutable post-release
  pivot/direction/charge, and stale-charge rejection after a whiff. Task E proves exact
  `6/60` whole-game hit-lag endpoints and non-stacking, preserved/resumed launch velocity,
  a stopped unrelated loose object, held gameplay/knockout/recovery clocks, the exact
  `599/600` object-freeze boundary, one cancel/resume transition, suppression of the
  glove slow-time writer, and victim-only shake through the ungated offset lane while
  pose mode is Tracking. Task E2 proves exact one-shot charge-start, charge-complete,
  release, and accepted-home-run audio edges; a tap omits completion, a charged whiff
  omits impact, and the provisional component owns one player with four generated PCM
  streams, a valid existing bus, profile-authored volume, and no Master-volume mutation.
  Task F extends `presentation_3d` with the lathed mesh's full vertex-in-capsule
  envelope, authored packed wood/grip colours, rough PerPixel material, accepted
  shadowless lighting rig, generic root slot, unchanged glove sphere route, and
  the mapped end invariant: wood at the physical barrel/glint end, black wrap at
  the physical handle end. The Task C glint check requires both the live source
  and the Mii3D counterpart when that presentation mode is active.
- The Task H owner-feedback checks additionally prove a charging cursor reaches
  the floor while the physical bat stays finite and collision-blocked, full charge
  targets the owner-boosted `6000` px/s physical tip speed, and one accepted
  home-run produces one compact impact burst at the solver contact point. The
  generic glove feedback path stays green in `tool_feel_reactions`.
- The `m5_homerun_bat` journey runs at `--fixed-fps 120` through the queued real
  key, pointer, primary, and secondary input paths. It selects the bat with `K`,
  grips the physical handle, holds charge for exactly `600` routed physics ticks,
  releases into one attributed home-run epoch, observes the whole-game freeze,
  then proves launch resumes and the bat reaches recovery. Run it in both Mii3D
  and legacy; keep `m5_baseball_bat` beside it as the weak free-swing journey.
- Contacts attribute the correct source, buddy region, pain, mood change, payout, and statistics.
- The `impact_dedup` loose-object probe must fall below `5 px/s` for `60`
  consecutive routed ticks within its `600`-tick settling window. Its verdict
  reports elapsed ticks, calm ticks, minimum/final linear speed, final angular
  speed, and whether physics sleep was observed; the bound must not be relaxed
  to hide missing rolling resistance.
- A thrown object that bounces off a boundary before striking the buddy still credits the originating throw; the same object striking after coming to rest, or after the buddy tosses/discards it, attributes to the generic loose-object source.
- Spawning object 25 removes the oldest eligible safe/unheld object and never removes a protected object.
- The `object_catch_hold` scenario drives a registered safe object through the real
  player grab/release bridge, then verifies layer-6→layer-3 sensing, bounded
  two-hand catch/hold forces, held collision exceptions, exactly-once catch care,
  protection from fixed-registry eviction, and zero managed allocation across a
  240-tick warmed live registry/object/arbiter route (`--fixed-fps 120`, seeds 1
  and 7).
- The `object_toss_discard` scenario verifies a held safe object receives one
  bounded toss impulse and clears buddy-held/throw attribution, while newly
  learned harmful memory instead produces one lower-energy discard, clears
  collision exceptions, and requests flee bias (`--fixed-fps 120`, seeds 1 and 7).
- The `fun_catch_laugh` scenario covers per-buddy fun interest end to end: a ball
  caught out of the air off a player throw makes the buddy laugh (`^_^` hold) and
  spends exactly the buddy's own catch drain; the same clean catch by a buddy whose
  catch interest is spent is still caught and still counted but produces no laugh;
  a spent meter stays boring while it recovers and is fun again once past the
  comeback level; and a dropped ball is marked as having reached the floor while a
  freshly thrown one is not (`--fixed-fps 120`, seed 1). Catch throws for this
  scenario use `SpawnCleanThrow`, not `SpawnCatchCandidate` — the latter's flat
  hard throw dips to the floor mid-flight, which is a legitimate catch but not a
  clean one.
- The `consume_care_cooldown` scenario proves a cancelled real-food Eat grants no
  care or cooldown, authoritative bite five applies `+10` mood exactly once and
  starts exactly `7200` routed ticks while final hand-lowering continues, and an
  immediate reuse is rejected without a second reward (`--fixed-fps 120`, seeds
  1 and 7).
- The `meal_consume` scenario proves appetite-sized admission and the refusal performance:
  the refused item remains in the original single hand; the frontal visual head performs
  exactly four alternating yaw lobes around the neck’s vertical axis with a first peak in
  `20–30°`, strictly diminishing peaks, no multi-frame center pause, no positional head
  translation, stable activity pitch/roll, and a neutral finish; the item is then dropped
  below the buddy at rest without a discard and remains ignored until appetite returns
  (`--fixed-fps 120`, seeds 1, 7, and 13).
- The `behavior_priority_ladder` scenario proves the complete §4 order `0–7`,
  immediate higher-priority preemption inside a commitment window, and runtime
  routing of fail-safe, unconscious, learned glove hazard, supported fearful
  grab, committed object, social, and ambient producers. The supported grab
  check retains the accepted walking resistance and deterministic panic hands
  (`--fixed-fps 120`, seeds 1 and 7).
- The `mood_band_behavior` scenario drives the per-run mood state through all
  five bands and verifies fearful flee, wary standoff, neutral ambient baseline,
  content approach, delighted approach, the exact voluntary-catch gate, and
  content-versus-delighted greeting cadence from the typed shared resources
  (`--fixed-fps 120`, seeds 1 and 7).
- The `jump_trait_gate` scenario places a real frozen layer-3 object in a stable
  committed walk path and proves a zero-propensity save never hops, a
  high-propensity save does hop on probe evidence, unconsciousness suppresses
  the request, and timer-driven ambient jumping remains disabled. Trait reload
  is verified by Task 4's `care_persistence` journey
  (`--fixed-fps 120`, seeds 1 and 7).
- The `hidden_clock_accrual` scenario advances an injected monotonic source while
  the gameplay tree is paused, proving the ragdoll pose stays frozen while mood,
  provisional passive income, hidden/run time, and the 30-second autosave advance.
- The `suspend_no_catchup` scenario simulates a one-hour suspend plus a separate
  over-five-second discontinuity, proving neither span awards time/income and
  resume leaves finite physics with no replay burst.
- The phased `care_persistence` journey writes care, damage earnings, harmful
  memory, selection, and the sampled trait to an artifact-local fixture, launches
  a fresh process, and verifies semantic restoration plus safe-pose/transient reset.

### Resize and Zoom

- Exercise minimum, default, and monitor-sized rooms at `4:3`, `16:10`, `16:9`, and `21:9`.
- Resizing rebuilds containment safely on a physics boundary and does not stretch assets or bodies.
- Zoom changes world/UI scale but not the OS window dimensions, and never rescales physics bodies or invalidates accepted tuning.
- Objects forced outside new bounds are corrected without an impulse explosion.
- An OS modal move/size loop followed by release produces no physics catch-up burst beyond the configured maximum physics steps per frame, and tick-counted gameplay timers do not jump.
- Exercise the smallest legal room (`360x270` world units) for stability, and verify zoom levels that would shrink the room below that floor are unavailable while the stored zoom preference survives clamping.

### Frontal 3D Presentation

- The `presentation_3d` scenario validates the visual profile through startup validation, builds exactly six part meshes plus the configured connector graph and face, round-trips semantic face changes, and holds 2D/3D camera alignment below `0.5 px` across every supported zoom plus a resized client.
- Runtime `LegacyCircles`/`Mii3D` toggles swap visibility without changing accepted pain, and the dynamic Boxing Glove counterpart attaches/despawns with its physical actor while preserving the accepted impact squash/recoil transform.
- The `presentation_look` scenario is the production-look gate: it confirms the accepted look profile validates (and named failure paths reject non-finite/negative/shadows/missing look), the six parts and five connectors carry cached soft-toon Lambert materials with the exact base albedos and specular/roughness, the transparent-safe rig has exactly two shadowless directional lights and no environment, the six inverted-hull outline shells share one front-face-culled grow-1.5 ink material with no connector outline, the camera-space depth lane leaves identity projection unchanged and adds under `0.5 px` screen-X at `±30°` yaw, mode/yaw/look changes do not change accepted pain or body transforms, and every socket/outline transform and material reference stays finite and constant through the 21,600-tick idle soak (`--fixed-fps 120`).
- The `pose_pipeline` scenario is the M3.6 Task 1 gate: pose-mode arbitration reaches Performance in a calm stable state and is forced back to Tracking by each real semantic (live buddy-part grab, unconsciousness, an accepted impact within the cooldown, and the learned-harm hand guard), the cooldown expiry re-allows Performance, every part's visual offset stays clamped to the profile fraction of its radius at full blend (and sits exactly at the cap when a larger offset is requested), and controlled strikes launched in Performance, Tracking, and mid-blend all accept identical saturated pain (`--fixed-fps 120`).
- The `facing_follows_walk` scenario is the M3.6 Task 2 gate: a sustained seeded walk direction commits the matching three-quarter side only after the hysteresis streak and eases to the accepted yaw with no overshoot, an engaged care cursor flips the side deterministically with a single monotonic zero crossing, and a controlled strike snaps the displayed yaw to zero on the next rendered frame while the committed side is remembered (`--fixed-fps 120`).
- The `activity_clips` scenario is the M3.6 Task 3 plus owner-fix B4 gate: every activity id resolves to a real clip in the animation library, walk-dressing phase advances proportionally to measured torso travel (ratio within 15% of 1.0) and freezes on every non-walk frame, and Eat pauses autonomy/settles horizontal travel while both authoritative hands move a shared item target from upper chest to mouth. Its fixed-clock gate requires a temporary frontal face-to-food turn that remembers and restores the committed side, exactly five bite events, item scales `0.8 / 0.6 / 0.4 / 0.2 / 0`, disappearance on bite five, both hands beyond the face depth with their upper edges just below the mouth, a final downward return that physically reaches normal hand-rest height during its `30`-tick hold before reach release, release on cancellation, and an immediate cut after a real accepted impact (`--fixed-fps 120`).
- The `autonomous_motion` scenario requires a grounded walk-to-idle transition to reduce whole-rig horizontal motion to at most `2 px/s` with no more than `1.25 px` center-of-mass travel over four routed ticks. Its jump-actuation regression uses the approved real obstacle plus high-propensity gate; it must never re-enable timer-driven ambient jumping. The stop check must not apply to airborne/throw motion and runs on seeds 1 and 7 (`--fixed-fps 120`).
- The `grab_dangle` scenario lifts the buddy independently by head, torso, both hands, and both feet. Every unsupported case must disable all active drive and resistance while retaining passive structural topology within the calibrated `48 px` loose-hang bound (42.8 px measured plus margin); the same bound applies while unconscious, grounded grabs retain standing drive, every body remains finite, and release resumes drive (`--fixed-fps 120`).
- The `grab_hang_orientation` scenario lifts both feet and one hand, then requires the grabbed part to become the highest body, a foot grab to place the head below the torso, and the torso to converge on the center-of-mass-derived hang frame while every ordinary active-drive output remains off. It also proves the bounded gravity-style alignment torque runs, crosses its target before settling, keeps all bodies finite, and resumes active drive and standing recovery on release (`--fixed-fps 120`).
- The `grab_swing_pendulum` scenario lifts one foot and drives the cursor through a fixed horizontal sine. The center of mass must follow with measurable nonzero phase lag, structural links must flex without exceeding their configured maximum distances beyond the calibrated margin, all ordinary drive must remain passive, all bodies must remain finite, and release must recover standing (`--fixed-fps 120`).
- The windowed `owner_feedback_visual` scenario captures `eat_hands_face_front.png`, `eat_final_hand_lower.png`, and `grab_hand_topology.png` as human-review evidence after the semantic gates pass. Screenshots never replace semantic assertions.
- The `lookat_priority_and_cone` scenario is the M3.6 Task 4 gate: an engaged pet stroke over either foot is acquired and aimed at with independently recomputed `atan2` angles, a held cursor beyond the engagement range is never watched (plain idle never tracks the cursor), a socketed item during the eat activity outranks ambient idling, ambient glances replay identically after reseeding with the run seed while every pupil quantum stays on a profile step, and one controlled strike proves all three suppressions — the pain face holds the gaze at rest, forced Tracking keeps the applied head angles exactly zero, and the impact point is watched until the profile memory expires. Every sampled angle stays inside the profile cone (`--fixed-fps 120`).
- The `face_composition` scenario is the M3.6 Task 5 gate: every semantic face string the reaction resolver can produce resolves through the authoritative expression map to a feature pose and the composed scene carries the accepted Soft Oval style, a calm idle buddy with blink and glance cadence stretched beyond the window requests ZERO re-renders across 400 frames (the change-only rule), the seeded blink closes the lids for the profile hold on blinkable faces and disarms entirely while the knockout `x_x` shows special eyes, the eat activity overlays alternating chew mouth frames and restores the semantic mouth when it ends, and a real controlled head strike round-trips `>_<` into the compositor's pain pose (scrunch eyes, squiggle mouth, no blink, no pupils) with a fresh render key (`--fixed-fps 120`).

- The `m36_expressive` journey is the M3.6 Task 6 composition gate and drives the expressive layer through the laboratory's own development keys, exactly as the owner does: the composed 3D presentation is active, the seeded autonomy walks and the walk dressing plays with an advancing phase, the facing keys commit a three-quarter turn to each side with an intermediate eased frame (a snap fails), releasing the override hands facing back to autonomy, the eat key attaches an item at the two-hand midpoint while the eat activity plays and a second press clears both, and the wave key plays the wave clip. Presentation-only assertions; no gameplay predicate moves (`--fixed-fps 120`, seeds 1 and 7).
- Every scenario and journey above is rerun under both presentation modes (`--presentation=mii3d` and `--presentation=legacy`). The verdicts must be identical: presentation mode is a rendering choice and can never change a gameplay outcome. The single exception is `m35_presentation_toggle`, whose whole subject is the mode itself — it asserts the shipping Mii3D-first order and is therefore run only in the default mode. Every other test, including `m36_expressive`, must be written mode-agnostically (assert that the expressive layer is composed and running, never which meshes are visible).

## 4. Economy Simulation

### 4.1 Production-path architecture

The calibration runner is deterministic domain code, not a second economy implementation. It must replay timestamped benchmark actions through the production seams in this order:

1. active `ContactSample` input through `ImpactRouter -> PainCurve -> RewardLedger`;
2. elapsed running intervals through `PassiveIncome`;
3. purchase attempts through the real `ToolCatalogue` and purchase service;
4. balance, ownership, and cumulative purchase timestamps into a structured result.

A Godot adapter scenario named `economy_calibration` loads the actual launch catalogue and the actual pain, payout, mood, and passive-income Resources, converts them to immutable domain settings, and invokes the pure runner. Pure unit tests may use deliberately synthetic catalogues to test edge cases, but no test or runner may duplicate the launch prices in source code.

The benchmark trace describes player behavior only. Its contacts, care actions, misses, pauses, active/background intervals, and purchase strategy must not be generated from or adjusted by prices. Fixed seeds make every run reproducible; use at least five committed seeds and report the median. The report writer emits stable-key-order JSON plus a human-readable Markdown summary so diffs expose all calibration changes.

### 4.2 Official completionist targets

The official acceptance strategy buys every item in order at the first affordable opportunity. The median cumulative purchase time for every row must be within ±15% of its target.

| # | Item | Target minute | Gap from prior | Pricing class |
|---:|---|---:|---:|---|
| 1 | Baseball | 3 | 3 | Regular |
| 2 | Baseball Bat | 7 | 4 | Regular |
| 3 | Meal | 13 | 6 | Regular |
| 4 | Nerf | 21 | 8 | Regular |
| 5 | Pistol | 41 | 20 | High value |
| 6 | Soccer Ball | 52 | 11 | Regular |
| 7 | Grenade | 76 | 24 | High value |
| 8 | Fire Sprayer | 104 | 28 | High value |
| 9 | Power Grab | 120 | 16 | Regular |
| 10 | Repair Kit | 138 | 18 | Regular |
| 11 | Shotgun | 184 | 46 | High value |
| 12 | Drink | 209 | 25 | Regular |

Only Pistol, Grenade, Fire Sprayer, and Shotgun use the high-value pricing treatment. The larger grind is immediately before the high-value purchase. The old 120-minute catalogue-completion target is superseded by this 209-minute schedule.

The representative casual trace contains approximately 120 minutes of active interaction and 89 minutes of running background/passive time across the 209-minute horizon. Active play includes experimentation, care, misses, pauses, and non-optimal hits; it is not a perfect damage loop.

### 4.3 Additional strategy coverage

The shop has no prerequisite chain. In addition to the judged completionist strategy, run deterministic save/skip strategies that:

- save directly for each high-value item;
- skip at least two regular items before buying a later preferred item;
- buy Power Grab before an earlier affordable regular item;
- leave an affordable item unbought for at least one purchase interval.

These runs are behavioral regressions, not target-time failures. They pass when the requested owned item can be purchased at sufficient balance, skipped items remain unowned, the balance is charged once, and later purchases remain possible. Never force-buy an earlier catalogue row merely to keep a simulation schedule.

### 4.4 Proof obligations and report contract

Every calibration report must include Resource/content fingerprints, seed, strategy ID, active minutes, background minutes, active income, passive income, purchase timestamps, ending balance, duplicate-contact rejects, and ordinary-event maximum payout. CI fails if any official completionist median is outside ±15%, a catalogue entry is missing/duplicated, or a proof obligation fails.

The benchmark must prove:

- active interaction remains the dominant income source over the complete representative trace;
- maximum-mood passive income is approximately 25% of benchmark active income; use a documented 20–30% validation band unless the owner later approves a narrower one;
- no single ordinary accepted event crosses more than one intended catalogue milestone;
- a positive / duplicate-zero / later-positive contact sequence passes through the real router and ledger, proving legitimate repeated use pays while duplicate physics callbacks do not;
- prices are monotonic positive integers in catalogue order only where explicitly required by the approved data; target times, not guessed price curves, are the calibration authority;
- changing a price or payout Resource changes the report fingerprint and result without changing the benchmark trace.

### 4.5 As implemented (M5 Task 12)

The runner is `domain/DesktopBuddy.Domain/Economy/Benchmark/`: `BenchmarkTraceGenerator`
(the seed is the fixture â€” no trace file is committed), `EconomyBenchmark.Run`,
`BenchmarkStrategies` (the seven strategies, as data), `BenchmarkObligations` (the six
proofs), `BenchmarkFingerprint`, and `BenchmarkReport` (JSON + Markdown). The runner drives
`BuddyProgressState`, which is what already composes the Â§4.1 seams, so no payout arithmetic
exists anywhere in the benchmark.

The `economy_calibration` scenario (quick-suite step 40) loads
`data/buddy/lab_pain_conversion.tres`, `data/buddy/m4_mood_economy.tres`, and the shipped
catalogue, validates all three, sweeps seeds `1/7/13/29/101` against all seven strategies,
runs the sweep a second time to prove the report is byte-identical, and writes
`economy_benchmark.json` / `economy_benchmark.md` to the artifacts directory. It fails the
run â€” not just prints â€” when a completionist median leaves Â±15%, when a seed never reaches
an item, or when any obligation fails. The calibrated values and the reasoning behind each
one are in `docs/DECISIONS.md` under "M5 Task 12 â€” Economy calibration".

Prices are deliberately **not** monotonic in catalogue order. The measured schedule is the
authority (Â§4.4), and the Drink's price is bounded by the slowest seed's total income
because its target lands at the very end of the trace.

## 5. Standalone Windows Matrix

Run outside the embedded Godot editor window.

- Windows 10 and Windows 11, x86_64.
- 100%, 125%, 150%, and 200% display scaling.
- Primary and secondary monitors, negative virtual-desktop coordinates, mixed DPI, and taskbars on different edges.
- Default/minimum/maximum sizes plus representative 16:9 and 21:9 windows.
- Transparency available and forced-unavailable fallback.
- Always-on-top on/off, V-sync on/off, anti-aliasing levels, and every zoom value.
- Work Mode passthrough over transparent pixels; buddy/menu interaction entering Play Mode; outside click returning to Work Mode.
- `Escape`, default `Ctrl+Shift+B`, remapped global hotkey, and tray recovery from every focus state.
- Window move/resize persistence and off-screen recovery after monitor removal.
- Show/hide, Windows-startup toggle, Reset Buddy, and Save & Quit tray actions.
- Session lock/unlock: mood/passive accrual continues while locked, no clock discontinuity is recorded, and the prior presentation state restores on unlock.
- First-run presentation defaults and AA Off/2x/4x/8x plus V-sync On/Off apply correctly; screen shake never moves the OS window.

## 6. Steam Acceptance

- Launch through Steam and directly without Steam.
- Verify Shift+Tab overlay behavior with transparency, topmost, focus, Work Mode, and Play Mode.
- Unlock each achievement through a controlled test profile.
- Verify stats are idempotent after reconnect.
- Confirm only progress data participates in Steam Cloud.
- Install from a clean Steam depot and verify no development `steam_appid.txt`, editor files, or unlicensed SDK artifacts ship.

## 7. Performance and Soak

On an i5-8400/UHD 630-class reference machine at `480x360` with 24 loose objects:

- Sustain at least 60 rendered FPS while physics remains fixed at 120 Hz.
- Target less than 5% total CPU and 300 MB resident memory during the representative active scene.
- Target less than 0.5% CPU while hidden to tray.
- Complete a four-hour visible soak and eight-hour hidden soak without unbounded memory, object, timer, save-queue, or Steam-event growth.
- Steady-state physics ticks allocate zero managed heap memory during a scripted active scene, measured through allocation-delta sampling; soak runs show no GC-driven frame-time spikes.

If the active budget is missed, optimize VFX, rendering, allocations, collision layers, and sleeping. Do not silently reduce the physics tick rate.

## 8. Physics-Lab Exit Gate

Economy and shop implementation may begin only when all of these are true:

- Six-body spawn, idle, walk, jump, drag, throw, fear resistance, knockout, and recovery scenarios pass.
- Thirty-minute stability and repeated-run envelopes pass.
- Standalone transparent window and pointer mapping work at default size on Windows 10/11.
- A side-by-side reference review accepts responsiveness, bounded stretch, whole-body impulse propagation, sideways knockout, and recovery feel.
- All physics parameters used by the accepted build are stored in typed Resources and have regression coverage.
