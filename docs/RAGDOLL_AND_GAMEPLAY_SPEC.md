# Desktop Buddy — Ragdoll and Gameplay Specification

Status: Implementation handoff specification derived from `docs/DECISIONS.md`.

This document defines the launch buddy simulation, behavior arbitration, interactions, damage, mood, care, and economy contracts. `docs/DECISIONS.md` remains authoritative if the two documents ever conflict. Values explicitly marked for empirical tuning are not approved gameplay constants and must be established in the physics laboratory before production content is balanced.

## 1. Design Intent and Boundaries

Desktop Buddy uses a responsive six-circle active puppet inspired mechanically by the feel of *Interactive Buddy* v1.01. It is not a conventional articulated skeleton and must not become a realistic multi-bone ragdoll. The buddy should feel physically present, playful, readable at small window sizes, and consistent under repeated interactions.

The buddy is an immortal original robot/mannequin. It may suffer transient pain, panic, Burning, and a fixed knockout, but it cannot die, dismember, or bleed in the current scope. Its face is drawn directly on the head circle using simple emoticons. Communication is limited to the face, body language, status icons, and original nonverbal robot sounds; there is no launch dialogue or voice acting.

The first implementation milestone is a physics laboratory. Economy and shop work must not be treated as production-ready until that laboratory proves standing, locomotion, jumping, grabbing, impacts, knockout, recovery, object handling, and repeated-run stability.

The following coefficients are intentionally established by the approved physics/economy laboratories rather than guessed during scaffolding:

- final body radii, masses, inertias, friction, bounce, spring constants, damping, drive gains, force limits, maximum link lengths, grab force, throw-speed cap, locomotion force, jump impulse, or collision-to-pain curve;
- explosion radius/impulse, firearm projectile speed/mass, flame emission rate, or burn tick cadence;
- exact tool prices, `cash-per-pain`, and passive base income;
- probabilities and timing for optional personality choices such as inspecting, tossing, jumping during idle, or reacting differently to Pet versus Tickle;
- any behavior not established by the confirmed decision log.

These values belong in typed tuning resources, must be validated against the documented acceptance targets, and must not be embedded as unexplained literals in C# or scenes.

## 2. Runtime Composition

The buddy scene root is a thin orchestrator. It owns typed references, connects component events, and sends explicit commands; it does not contain physics, damage, mood, or AI logic. Components are single-purpose and do not find one another through brittle scene-tree paths.

The implementation should preserve the following responsibility boundaries, regardless of final class names:

| Responsibility | Contract |
| --- | --- |
| Rig | Own six typed `RigidBody2D` references, passive structural links, collision layers, part identity, and physical measurements. |
| Active drive | Convert a requested pose/locomotion intent into upright torque, locomotion impulses, and self-righting forces. It does not choose behavior. |
| Behavior arbiter | Select goals and resolve priorities from consciousness, recovery, hazards, mood, memory, nearby objects, and player constraints. It does not apply physics directly. |
| Grab tether | Acquire a buddy part or loose object, apply a damped elastic pull, expose tether strain, and release with capped velocity. |
| Object interaction | Detect candidates and coordinate investigate, catch, hold, consume, discard, and toss actions. |
| Impact router | Convert authoritative physics contacts into deduplicated, attributed impact samples. |
| Pain/knockout | Maintain the rolling pain window, trigger and time unconsciousness, and clear transient pain when instructed. |
| Mood/memory | Clamp persistent mood, decay transient emotions, record harmful per-tool history, and emit the mood-`60` trust reset. |
| Status effects | Own temporary effects such as Burning and their pain/mood/action consequences. |
| Reward ledger | Apply the approved payout formula, update money/statistics, and emit semantic reward events. |
| Tool runtime | Read the selected tool definition, manage its input lifecycle/cooldowns/ammunition, and spawn or control physical actors. |
| Loose-object registry | Enforce the `24`-object cap and protect objects that are not safe to evict. |

Typed Godot `Resource` assets hold rig topology and tuning, active-drive tuning, tool definitions, pain conversion data, status definitions, mood/economy data, and content metadata. Versioned JSON is reserved for runtime persistence. Signals/events flow upward from workers; explicit methods or immutable command data flow downward.

Simulation time used for physics, contact deduplication, knockout, status ticks, and cooldowns must be monotonic runtime time rather than wall-clock time. Hidden-to-tray mood/passive progression uses a low-cost monotonic timer while ragdoll physics is suspended. Closing, sleep/suspend, or a large clock discontinuity must never create catch-up simulation or income.

## 3. Six-Body Active Puppet

### 3.1 Physical anatomy

The rig contains exactly six circular `RigidBody2D` parts:

1. Head
2. Torso
3. Left hand/arm endpoint
4. Right hand/arm endpoint
5. Left foot/leg endpoint
6. Right foot/leg endpoint

The visible limbs between torso and endpoints are presentation connectors, not extra rigid bodies. The Head maps to the head payout region, Torso to torso, hand endpoints to arms, and foot endpoints to legs. All parts expose stable part IDs so contacts, grabs, expressions, statistics, and saves never depend on node names.

Buddy parts never collide with one another. They do collide with sandbox boundaries, cursor-driven physical tools, projectiles, and loose objects. Collision-layer tests must prove this rule rather than relying on visual inspection.

The exact spring-link graph, local anchor points, rest offsets, and body dimensions are physics-laboratory data. The rig resource must describe them as data so alternate experimental layouts can be compared without code changes. Production must retain the six-body silhouette; adding hidden rigid bodies to solve tuning problems is not permitted without owner approval.

### 3.2 Passive structural solver

Godot `RigidBody2D` is authoritative. The rig applies custom forces through the direct body-state integration path; it must not teleport bodies each frame, replace the whole-world solver, or use `PinJoint2D` motors for puppet actuation.

Each configured structural link is evaluated once per `120 Hz` physics tick:

1. Transform each link's local anchors into world space.
2. Measure anchor separation and the relative anchor-point velocity.
3. Compute the spring term from displacement relative to the configured rest relationship.
4. Compute damping from relative velocity, opposing separation/compression motion rather than adding energy.
5. Apply the resulting force at one anchor and its equal-and-opposite force at the other anchor.
6. When configured maximum stretch is exceeded, apply the resource-defined correction/limit response while preserving equal-and-opposite treatment.
7. Record link strain and applied force for the laboratory overlay and regression telemetry.

Structural links remain active while unconscious so the six parts stay connected. Maximum-stretch handling is a physical correction, not routine transform snapping. A hard pose reset is reserved for the confirmed fail-safe conditions in section 5.

All solver coefficients and clamps are tuning values. A tuning profile must be internally consistent at `120 Hz`; changing the fixed tick rate at runtime is forbidden.

### 3.3 Active drive

The active drive is layered on top of passive structure and may apply:

- upright torque toward a requested torso orientation;
- balancing/centering impulses based on support contacts and center-of-mass error;
- sideways locomotion impulses for autonomous walking or fleeing;
- a jump impulse when the behavior arbiter chooses a valid autonomous jump;
- limb-target or grip-support forces needed for catch, hold, consume, discard, and toss actions;
- assisted self-righting forces after the confirmed delay.

The behavior arbiter requests intent; only the drive component translates intent into forces. Drive outputs must be bounded by tuning resources so AI decisions cannot inject arbitrary forces. The drive must never directly assign body transforms or velocities during ordinary behavior.

Unconsciousness disables active upright, locomotion, jump, resistance, and object-action drive. Passive structure, gravity, collisions, external impulses, grab forces, and room boundaries remain authoritative. When the fixed knockout ends, active behavior resumes and the buddy must recover through physics rather than being placed upright.

Window resizing moves the room boundaries but does not stretch the buddy or objects. The separate zoom setting uniformly changes UI and physical world-object scale at `75%`, `100%`, `125%`, `150%`, `175%`, or `200%`; every supported zoom requires stability validation rather than assuming the `100%` tuning remains safe.

## 4. Behavior Arbitration and State Priority

Behavior uses layered arbitration rather than one monolithic state machine. Physical constraints such as a player grab can coexist with an autonomous goal; expression can coexist with locomotion; a transient emotion can influence a decision without becoming the only state.

The arbiter resolves actuation in this strict priority order:

| Priority | State/layer | Required behavior |
| --- | --- | --- |
| 0 | Invalid/out-of-bounds fail-safe | Immediately restore a valid in-room pose. This is the only immediate hard reposition path. |
| 1 | Unconscious | Disable all active buddy drive and object decisions for exactly `4` seconds. Do not restart or extend the timer when hit. |
| 2 | Assisted self-righting | When eligible, prioritize getting a stable upright support pose over voluntary goals. |
| 3 | Immediate hazard response | Burning or a recognized nearby/held hazard causes panic behavior; drop/discard held hazards and flee when physically possible. |
| 4 | Player-constraint response | If conscious and afraid while grabbed, generate resistance intent opposing the tether. The tether itself remains physically active regardless of intent. |
| 5 | Committed object action | Complete or abort catch, inspect, hold, consume, discard, or toss according to safety and current higher-priority state. |
| 6 | Emotional/social goal | Approach, avoid, flee, or show friendly/curious behavior based on persistent mood, transient emotion, and tool memory. |
| 7 | Ambient autonomy | Idle, walk sideways, jump, and select non-urgent interactions. |

Priority 0 is a safety exception, not a normal behavior. Priorities 1–7 may not hard-set transforms. A higher-priority layer suppresses conflicting lower-priority drive but should not erase persistent mood, memory, statistics, or externally applied physics.

Expression is resolved separately with consciousness and acute danger above ambient mood. At minimum, unconscious feedback overrides other faces; acute pain/Burning/fear/delight can temporarily override the persistent mood-band face; otherwise the face and posture communicate the current band. The physical head may rotate freely, but the emoticon is counter-rotated to remain upright in world/screen space. Exact emoticon set and other transient-emotion durations are presentation tuning unless confirmed in `DECISIONS.md`.

The buddy never directly attacks the player. It may resist a grab, flee, discard a hazard, or toss a safe object, but those actions are defensive/autonomous rather than retaliatory targeting.

### 4.1 Object understanding and memory

Per-tool experience and learned harmful history persist in the save. A tool/object without harmful history may be investigated or caught when the surrounding state permits. Once that tool causes harm, its harmful/fear record may cause avoidance, fleeing, or discarding on future encounters.

Whenever mood crosses upward from below `60` to `60` or above:

1. Clear every harmful-history and per-tool fear record.
2. Emit one semantic trust-reset event for statistics/achievement handling.
3. Preserve unrelated lifetime statistics and non-harmful familiarity data.
4. Re-arm the crossing rule only after mood later falls below `60`.

Because harmful recognition is cleared, an object such as a grenade may again be treated as unfamiliar after the crossing. Merely remaining at or above `60` must not repeatedly emit resets.

## 5. Standing, Knockout, and Recovery

Standing detection must be based on physical measurements—support contacts, torso/head orientation, center-of-mass relationship, and motion—rather than a single body's rotation. Exact tolerances belong in the rig profile and laboratory fixtures.

While conscious, maintain an accumulated `unable-to-stand` duration:

- Reset it only after the configured stable-standing criteria are satisfied.
- At `2` continuous seconds unable to stand, begin assisted self-righting.
- Ramp self-righting assistance to full over `1` second; no routine teleport is allowed during this ramp.
- If self-righting has failed for `10` seconds, a hard reposition is permitted.
- Invalid numeric physics state or a body outside the sandbox permits immediate hard reposition without waiting.

The inability/recovery clock does not use unconscious time to bypass the required natural post-knockout recovery. During knockout, active recovery forces remain disabled. When the buddy wakes, standing/recovery evaluation resumes from the physical pose.

Hard repositioning is a centralized, auditable operation. It releases the active grab and any held object, restores all six bodies to a known safe standing pose inside the current boundaries, and clears unstable velocities, rolling pain, knockout, Burning, and other temporary statuses. It preserves money, unlocks, persistent mood, harmful history, and lifetime statistics.

## 6. Player Grab and Fear Resistance

Grab can target any of the six buddy bodies or any eligible loose object. It is a damped elastic tether between the cursor anchor and the selected body's acquired local point, not direct position assignment.

The grab lifecycle is:

1. On valid primary acquisition, capture the target body and local anchor.
2. Each physics tick, calculate tether displacement and relative velocity.
3. Apply a bounded spring/damper pull to the target while exposing extension/force for feedback and tests.
4. If the target is a conscious fearful buddy part, the behavior system requests motion away from the grab direction and the drive applies bounded opposing intent.
5. Resistance visibly increases tether stretch but never breaks the player's grab by itself.
6. On primary release or secondary cancel/drop, remove the tether and preserve the body's current motion subject to the configured throw-velocity cap.

Fear resistance must be physically expressed; it may not fake resistance by slowing the cursor, detaching the tether, or ignoring target movement. An unconscious buddy provides no active resistance. The exact relationship between mood/transient fear/tool history and resistance strength is empirical tuning.

Grab does not reduce damage rewards. A valid impact on a tethered buddy uses the normal impact/payout rules, including the `0.5x` unconscious multiplier when applicable.

Entering or leaving Work Mode never changes the selected tool. Interacting with the buddy resumes that selected tool rather than substituting a safe Grab. Secondary input cancels or drops the current interaction without changing tool selection.

## 7. Contact, Pain, Knockout, and Money Pipeline

### 7.1 Authoritative impact sample

Physics contact is captured from Godot's authoritative contact data during fixed-step integration. An impact sample contains, at minimum:

- monotonic physics timestamp;
- stable source instance/interaction ID and tool/content ID;
- target buddy part ID and derived payout region;
- measured contact impulse and relative contact velocity needed by the approved pain-conversion curve;
- contact point/normal for feedback;
- whether the buddy is unconscious and/or player-grabbed at acceptance time;
- status/source attribution for periodic effects such as Burning.

Configured room boundaries, loose objects, projectiles, and physical weapons enter the pain pipeline when their calibrated impact threshold is exceeded. Attribution follows the originating tool/throw whenever that relationship is available. That relationship expires when the object first comes to rest (physics sleep or sustained sub-threshold speed) or when a new interaction reassigns it (player grab-throw, buddy toss/discard); boundary bounces alone never clear it. Post-expiry impacts attribute to the generic loose-object source, and explosion samples always attribute to the grenade. Low-energy contacts below the shared configured threshold produce no accepted pain event.

### 7.2 Deduplication and pain conversion

Use `(source interaction ID, target buddy part ID)` as the contact-episode key. The first valid contact in an episode may produce one accepted pain/reward event. Repeated callbacks from resting/sliding physics contact are suppressed. A new episode cannot begin until that key has been separated/inactive for at least `0.15` seconds.

The accepted measured impulse is converted to non-negative pain by the configured empirical curve. Do not attach hidden payout multipliers to tools. Tool differences must arise from their actual physical contact and the shared pain conversion, aside from the explicit region and consciousness multipliers. The confirmed active glove-defense state is a documented target-side exception: contact with an actively guarding hand scales accepted impulse by `0.5` before pain/reward/mood handling, while bypassing strikes remain unmodified.

Accepted events update total pain, per-tool uses/pain statistics, visual/audio feedback, and harmful-history attribution. Each accepted harmful event reduces persistent mood by `min(10, pain x 0.1)`. Burning pain ticks use the same formula and entering knockout adds no separate mood penalty.

### 7.3 Rolling knockout window

Maintain timestamped accepted pain events over a rolling `5`-second window. When their sum reaches `100` while the buddy is conscious:

1. Enter Unconscious once.
2. Start an exact `4`-second monotonic timer.
3. Disable active drive but retain passive structural physics.
4. Increment knockout statistics and emit the semantic knockout event.
5. Ignore further knockout-trigger attempts until the timer completes; later hits neither restart nor extend it.
6. At timer completion, return control to the active physics-driven recovery path.

The rolling pain window clears when knockout begins. Hits during unconsciousness remain valid pain/reward/mood events at reduced payout but do not enter a future knockout window. Waking therefore begins with an empty rolling window.

Repair Kit clears transient pain/rolling pain state and harmful statuses but does not shorten an active `4`-second knockout.

### 7.4 Reward calculation

For every accepted damage event:

`money = pain × body-region multiplier × unconscious multiplier × cash-per-pain`

Approved multipliers are:

| Region/state | Multiplier |
| --- | ---: |
| Head | `1.2x` |
| Torso | `1.0x` |
| Arms/hands | `0.8x` |
| Legs/feet | `0.8x` |
| Conscious | `1.0x` |
| Unconscious | `0.5x` |

Being grabbed adds no modifier. Reusing a tool adds no diminishing return beyond its normal cadence and contact-episode deduplication. `cash-per-pain` is calibrated against the unlock schedule.

Currency is stored in signed 64-bit milli-credits (`1000` minor units per displayed credit), allowing fractional rewards to accumulate without floating-point save drift. HUD balances and prices display whole credits. Accepted damage rewards inside a `0.25`-second interval are coalesced into brief `+$N.N` feedback; raw pain remains hidden.

## 8. Mood, Care, Trust, and Passive Income

### 8.1 Persistent and transient emotion

Persistent mood is hidden from the HUD and clamped to `[-100, +100]`:

| Mood | Band |
| ---: | --- |
| `-100..-61` | Fearful |
| `-60..-21` | Wary |
| `-20..20` | Neutral |
| `21..60` | Content |
| `61..100` | Delighted |

Fear, pain, delight, curiosity, and unconsciousness are separate short-lived emotional/state channels and decay independently. Persistent mood biases behavior and face/posture, but does not replace acute state priority.

While the application is running, mood drifts toward `0` at `0.5` points per minute. This continues during hidden-to-tray operation. It does not run while the application is closed, and sleep/suspend or a large clock gap awards no catch-up drift.

### 8.2 Care rewards

Care gives no immediate money. It increases mood and therefore passive earning potential:

| Event | Mood | Reuse/cadence |
| --- | ---: | ---: |
| Valid Pet rubbing | `+1` | Hidden distance threshold filled and at most once per `3` valid-contact seconds |
| Friendly Tickle contact | `+1` | At `3` and `6` cumulative valid-contact seconds before Angry |
| Angry Tickle contact | `-1` | Every further `3` valid-contact seconds until the `8`-second no-contact reset |
| Completed safe throw/catch | `+1` | Once per completed throw/catch event |
| Meal successfully consumed | `+10` | `60` seconds |
| Drink successfully consumed | `+5` | `60` seconds |
| Repair Kit successfully applied | `+20` | `120` seconds |

Pet/Tickle cadence counts valid contact, not merely held input over empty space. Pet satisfaction counts cursor distance over buddy bodies, with a hidden `1.2x` favorite body part reselected whenever Pet is selected. Active rubbing contact with that favorite emits small presentation-only sparkles around the Pet hand; the particles stop immediately when the valid favorite contact ends. Tickle becomes Angry after `6` cumulative valid-contact seconds, stops granting positive care, and resets after `8` seconds without valid contact. A catch can reward only once for its originating throw. Repair Kit clears transient pain and harmful statuses, including Burning, but does not shorten knockout.

Meal, Drink, and Repair Kit cooldowns begin only after successful consumption/use. Cancelling, dropping, missing, or otherwise failing to use the item does not start its cooldown.

### 8.3 Passive economy

Passive income exists only while the application is running, including hidden-to-tray low-cost operation. There is no offline income.

The mood multiplier is piecewise linear through the approved anchors:

- `0.25x` at mood `-100`;
- `1.0x` at neutral mood `0`;
- `2.0x` at mood `+100`.

The base passive rate is calibrated so peak passive income at `+100` is approximately `25%` of expected active-attack earnings. Care's economic value comes only through this passive multiplier. The base rate and comparison benchmark must be measured in the economy lab; no final rate is approved here.

## 9. Launch Tool Contracts

Every purchased tool is permanently unlocked for unlimited use. There is one earnable currency, no microtransactions, no selling, and no refunds. A new save starts with `0` money and Grab selected.

### 9.1 Shared controls

- Pet and Tickle use held click-and-drag strokes over buddy bodies.
- Boxing Glove and Baseball Bat are physical colliders pulled by a cursor tether; real swing speed/contact impulse drives pain.
- Pistol, Shotgun, and Fire Sprayer remain attached to the cursor. Their forward direction follows the current non-trivial mouse-motion vector.
- Mouse-wheel input offsets cursor-weapon aim upward/downward. The next non-trivial cursor movement clears that offset and aligns forward to the new movement vector.
- Baseball, Soccer Ball, Grenade, Meal, Drink, and Repair Kit use the pullback launcher. Primary press spawns/arms the held preview, dragging backward shows a trajectory line, and release launches opposite the drag vector.
- Secondary input cancels or drops the current interaction without changing the selected tool.
- The predicted trajectory and actual launch must use the same initial-velocity calculation and world gravity/profile data; the line is a preview, not a different aiming model.

### 9.2 Tool-by-tool behavior

| Tool | Availability target | Required behavior |
| --- | ---: | --- |
| Grab | Starting | Acquire any buddy part or eligible loose object with the damped elastic tether in section 6. Fearful conscious buddies resist; release preserves capped throw motion. |
| Pet | Starting | Held rubbing stroke over valid buddy contact. Hidden distance satisfaction plus the `3`-second cadence gates `+1` mood; a per-selection favorite part contributes `1.2x`. `:3` rubbing and brief completion smile communicate progress; no immediate cash. |
| Tickle | Starting | Held stroke over valid buddy contact. Friendly for `6` cumulative seconds, then Angry with negative mood/flee behavior until `8` seconds without contact. Distinct hand animation, expression, sound, and away-hop timing are confirmed in `DECISIONS.md`. |
| Boxing Glove | Starting | Low-lag cursor-tethered physical collider. Real swing speed/impulse drives pain with no glove multiplier. Learned harm can trigger physical hand guarding while the buddy flees: the body-relative guard direction follows the pointer with bounded lag and does not attach to the glove or inject net pull toward it. Guarded-hand contact uses the documented target-side `0.5x` absorption factor. Maximum-pain/knockout strikes use the confirmed brief hit-stop with a visibly slow early portion and contact-centered impact feedback. |
| Baseball | `3` minutes | Pullback-launched loose physical object. It may be caught, held, inspected, or tossed; a completed safe catch grants `+1` mood. Final physical preset is empirical. |
| Meal | `6` minutes | Pullback-launched care object. Successful consumption grants `+10` mood, then enforces a `60`-second reuse cooldown. |
| Baseball Bat | `20` minutes | Cursor-tethered physical collider. Swing velocity and contact impulse determine pain through the shared pipeline. |
| Pistol | `30` minutes | Cursor gun with physical CCD projectile. Magazine `8`; minimum shot interval `0.25` seconds; reload `1.2` seconds; unlimited reserve ammunition. Fires once per primary press, reloads with `R`, and auto-reloads when fired empty. |
| Grenade | `40` minutes | Pullback-launched explosive. Its `2.5`-second fuse starts on launch, not initial press. An inexperienced buddy may investigate/catch it; harmful grenade memory causes flee/discard behavior. Blast tuning is empirical. |
| Fire Sprayer | `50` minutes | Cursor weapon using shared motion/wheel aim. Holding primary sprays continuously. Contact applies Burning as specified below. |
| Soccer Ball | `65` minutes | Pullback-launched loose physical object. It supports catch/hold/toss and the same `+1` completed-catch reward. Final physical preset is empirical and distinct data from Baseball. |
| Drink | `80` minutes | Pullback-launched care object. Successful consumption grants `+5` mood, then enforces a `60`-second reuse cooldown. |
| Shotgun | `100` minutes | Cursor gun firing `6` physical CCD pellets. Capacity `5`; minimum shot interval `0.9` seconds; reload `2` seconds; unlimited reserve ammunition. Fires once per primary press, reloads with `R`, and auto-reloads when fired empty. |
| Repair Kit | `120` minutes | Pullback-launched care object. Successful application grants `+20` mood, clears transient pain and harmful statuses, and enforces a `120`-second cooldown. It never shortens an active knockout. |

Target times are cumulative-play pacing goals, not hard time gates. Prices and income are tuned so a representative mixed active/passive player can buy in this sequence, with the complete current catalogue at approximately `2` hours.

### 9.3 Burning

Fire contact applies Burning for `4` seconds. Continued/repeated contact refreshes remaining duration, capped at `8` seconds. Burning:

- causes panic behavior;
- produces periodic pain through attributed status events;
- lowers mood;
- causes the buddy to drop held items;
- is cleared immediately by a successful Repair Kit.

Burn pain per tick, tick cadence, mood loss, visuals, particles, and audio are tuning values. Effects must honor reduced particles, reduced motion, screen-shake, and photosensitivity-safe settings.

## 10. Loose Objects and Cleanup

At most `24` loose physics objects may exist. The loose-object registry assigns each candidate a monotonic creation sequence and tracks whether it is safe and eligible for eviction.

Before admitting a new loose object at the cap:

1. Query objects from oldest to newest.
2. Select the oldest object marked safe and not protected.
3. Remove it through its normal cleanup path, then admit the new object.
4. Never evict an object grabbed by the player, held by the buddy, in a committed launch/consume action, or otherwise explicitly protected by its runtime state.
5. If no eligible object exists, reject/defer the new loose-object admission rather than exceed `24` or destroy an unsafe/protected object.

The registry owns the cap; individual tools may not perform ad hoc scene-tree searches. Removal must detach grabs/registrations/events cleanly and must not award damage, catch, care, or tool-use credit.

Active bullets/pellets and transient effect particles are not persistent loose toys. Their lifetime/pooling limits are separate performance-tuning data; spent projectiles must not silently accumulate as uncapped room objects.

## 11. Physics Laboratory and Empirical Tuning

### 11.1 Required laboratory controls and telemetry

The laboratory must run the real production rig/components at fixed `120 Hz` and provide repeatable, seeded scenarios. It must expose, without making these production HUD elements:

- pose presets and controlled impulses;
- per-body position, rotation, linear/angular velocity, contacts, and sleep state;
- link rest error, stretch ratio, force, and maximum-stretch activation;
- center of mass, support contacts, standing predicate, and active-drive intent;
- selected behavior layer and suppressed lower-priority goals;
- grab extension, grab force, resistance intent, and release speed;
- raw contact samples, deduplication decisions, converted pain, rolling pain sum, and payout attribution;
- mood, transient emotions, per-tool harmful memory, trust-reset crossings, and passive multiplier;
- active loose-object count/protection reason and cleanup choice;
- frame time, physics time, body/contact count, CPU, and memory measurements.

Clean-room reference observations from Newgrounds v1.01 are the primary feel comparison; archived v1.02 is secondary where it does not conflict. Record observations and measurements, never copy original expressive assets.

### 11.2 Tuning order

Tune in this order so downstream systems do not compensate for unstable upstream physics:

1. Body dimensions/mass distribution and collision behavior.
2. Passive structural links and maximum-stretch handling.
3. Upright balance, sideways locomotion, jump, and self-righting.
4. Player grab, resistance, and throw release.
5. Object catch/hold/toss and physical tool presets.
6. Shared impulse-to-pain curve and knockout response.
7. Individual tool physical parameters/status effects.
8. `cash-per-pain`, passive base rate, and shop prices against the unlock targets.

Each accepted profile becomes a versioned resource plus a seeded regression fixture. Bit-exact replay is not required; numerical tolerance envelopes are established from accepted repeated runs and checked in CI/headless Godot.

## 12. Acceptance Gates

The physics milestone is complete only when all applicable gates pass.

### 12.1 Rig and motion

- Exactly six circular authoritative `RigidBody2D` parts are present; internal buddy collisions produce zero contact events, while room/tool/object/projectile contacts work.
- Each structural-link calculation applies equal-and-opposite force within the accepted numerical tolerance and damping does not add energy to a passive settling test.
- Configured maximum stretch is respected under the seeded impulse/drag envelope without routine transform snapping.
- The buddy can stand, idle, walk sideways in both directions, autonomously jump, fall limp, and recover without direct locomotion control from the player.
- Unconsciousness disables active drive while passive structure and external physics remain active.
- At `2` continuous conscious seconds unable to stand, self-righting begins; assistance reaches its configured full level within `5` seconds; a valid-state hard reposition never occurs before `10` seconds of failed self-righting.
- Invalid/out-of-bounds state invokes the immediate centralized fail-safe.
- Resizing among representative `4:3`, `16:10`, `16:9`, and `21:9` windows changes boundaries without stretching objects. All six zoom levels remain physically stable.

### 12.2 Grab and behavior

- Every buddy part and an eligible loose object can be acquired/released by the same tether contract.
- Under an identical scripted pull, a fearful conscious buddy produces measurable opposing intent/tether stretch; resistance never breaks the tether.
- An unconscious buddy produces no active grab resistance, and released targets never exceed the configured throw-speed cap.
- Priority scenarios prove knockout overrules recovery/hazard/object/autonomy, self-righting overrules voluntary goals, and a recognized hazard overrules friendly object interaction.
- The buddy can catch, inspect, hold, consume, discard, and toss suitable objects, and never directly targets an attack at the player.
- Crossing mood upward from below `60` clears harmful/per-tool fear records exactly once; falling below and later recrossing can trigger it again.

### 12.3 Pain, status, and rewards

- Accepted impacts map Head to `1.2x`, Torso to `1.0x`, and hand/foot limb regions to `0.8x` with no tool-specific hidden payout multiplier.
- Repeated callbacks in one source/body contact episode produce one event; separation shorter than `0.15` seconds cannot create a second event.
- Exactly `100` accepted pain inside the rolling `5`-second window starts one `4`-second knockout; `99.x` does not.
- Hits during knockout neither restart nor extend its timer and award exactly `50%` of the otherwise identical payout. Grabbed impacts receive no grab penalty.
- Repair Kit clears transient pain and Burning while leaving the active knockout end time unchanged.
- Fire contact starts a `4`-second burn, refreshes only up to `8` seconds, causes attributed periodic pain/panic/mood loss/drop behavior, and is cleared by Repair Kit.
- Total/per-tool pain and reward statistics match the accepted event ledger.

Exact timers are asserted within one `120 Hz` physics tick where the check crosses a fixed-step boundary.

### 12.4 Mood, economy, tools, and cleanup

- Mood clamps to `[-100, +100]`, maps to the five approved bands, drifts toward `0` by `0.5` points per running minute, and does not advance while closed or across sleep/large-clock gaps.
- Pet/Tickle, catch, Meal, Drink, and Repair Kit apply exactly their approved mood amounts/cadences and no immediate cash.
- Passive multiplier reaches `0.25x`, `1.0x`, and `2.0x` at moods `-100`, `0`, and `+100`; peak passive earnings are calibrated to approximately `25%` of representative active-attack earnings.
- Pistol/Shotgun capacities, cadence, reload durations, pellet count, CCD, and unlimited reserve match section 9; grenade fuse starts only on launch; cursor aim/wheel reset and pullback launch controls match their shared contracts.
- Loose-object count never exceeds `24`. Cleanup always chooses the oldest eligible safe object and never removes a held/protected object.
- The final calibrated economy reaches the approved `3`, `6`, `20`, `30`, `40`, `50`, `65`, `80`, `100`, and `120` minute purchase targets in the required sequence for the documented representative play profile.

### 12.5 Stability and performance

- Seeded scenarios remain within accepted tolerance envelopes across repeated headless runs; bit-exact trajectories are not required.
- Active play at `480x360` with `24` loose objects runs at fixed `120 Hz`, renders at least `60 FPS`, and targets less than `5%` CPU and `300 MB` RAM on the i5-8400/UHD 630-class reference PC.
- Hidden-to-tray operation suspends rendering/ragdoll physics, continues mood/passive timers, and targets less than `0.5%` CPU.
- The physics tick rate never lowers dynamically to meet performance targets.

## 13. Persistence Boundary

Persist semantic progress: money, unlocks, selected tool, mood, harmful-history/per-tool memory, statistics, and settings. Do not persist live body transforms, loose objects, active projectiles, rolling pain events, knockout state, or temporary statuses. Every loaded session begins with a safe standing buddy while preserving its semantic mood and memory.

Future buddy coloring/painting, cosmetics, blood, multiple buddies, profiles, multiplayer, and Steam Workshop custom buddies are out of scope. Current code should avoid needless coupling that blocks future content data, but no current feature, abstraction, file format, or UI may be added solely to implement those future systems.
