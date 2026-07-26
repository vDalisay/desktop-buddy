# Desktop Buddy — Product Requirements

Status: Baseline requirements derived from `docs/DECISIONS.md`  
Target: First Steam release  
Platform: Windows 10/11 x86_64  
Runtime: Godot 4.6.1 .NET with C#

## 1. Overview

Desktop Buddy is a transparent desktop-idler sandbox containing one autonomous, immortal, six-body robot/mannequin. The player can distract, care for, throw objects to, grab, and slapstick-attack the buddy while doing other work. Physical interaction earns money; positive care improves the buddy's mood and increases passive income. Money permanently unlocks a launch catalogue of tools over an approximately two-hour progression horizon.

The product is a clean-room spiritual successor to *Interactive Buddy*. Newgrounds v1.01 is the primary reference for mechanical feel; archived v1.02 behavior is a secondary comparison only when it does not conflict with v1.01. No original art, audio, dialogue, skins, branding, or other expressive content may be copied.

The first delivery milestone is a physics laboratory that proves the complete buddy behavior and repeatable mechanical feel before economy and shop implementation begins. Empirical coefficients such as spring tuning, impulse-to-pain conversion, base passive income, cash-per-pain, and catalogue prices are calibration outputs. They must be measured against the confirmed acceptance targets in this document rather than invented as new product rules.

## 2. Roles

- **Player:** Uses tools, buys permanent unlocks, cares for or annoys the buddy, and observes its reactions and progression.
- **Desktop user:** Keeps the sandbox present while working in other applications and needs predictable click passthrough, low resource use, and rapid mode recovery.
- **Returning player:** Expects semantic progression and preferences to survive restarts while each session begins from a safe simulation state.
- **Steam player:** Uses the same fully local-playable product with optional Steam Cloud, achievements, and statistics synchronization.

## 3. User Stories

- **US-01:** As a desktop user, I want a small transparent sandbox with reliable click passthrough so that the buddy can coexist with my other work.
- **US-02:** As a player, I want a responsive spring-driven buddy that stands, walks, reacts, falls, and recovers consistently so that physical play feels expressive and fun.
- **US-03:** As a player, I want direct physical tools with distinct controls so that attacks, throws, care, and grabbing feel tactile rather than scripted.
- **US-04:** As a player, I want the buddy to remember harmful treatment and respond according to mood so that it feels like a simple virtual pet.
- **US-05:** As a player, I want both active damage earnings and mood-scaled passive earnings so that short interactions remain the strongest source of progress without making care irrelevant.
- **US-06:** As a player, I want permanent tool unlocks paced across roughly two hours so that the sandbox gains variety over time.
- **US-07:** As a returning player, I want resilient saves that retain meaningful progress but discard unsafe live physics state so that every session resumes reliably.
- **US-08:** As a desktop user, I want scalable presentation, accessibility settings, and low-cost tray operation so that the game fits my monitor and comfort needs.
- **US-09:** As a Steam player, I want achievements, statistics, and progression cloud sync without making Steam availability a requirement for play.

## 4. Functional Requirements and Acceptance Criteria

### FR-001 — Transparent Sandbox Window

**Linked stories:** US-01, US-08

1. **FR-001.1:** WHEN the game displays the sandbox THEN it SHALL render the non-content background transparently and SHALL render simple, clearly visible borders that make the play area read as a box.
2. **FR-001.2:** WHEN the application starts for the first time THEN it SHALL create a `480x360` logical-pixel window positioned `16` pixels from the lower-right edge of the current monitor's usable work area.
3. **FR-001.3:** WHEN the player resizes the window THEN it SHALL allow free-form aspect ratios at or above `360x270` logical pixels and no larger than the usable work area of the monitor containing the window.
4. **FR-001.4:** WHEN the window is resized THEN the sandbox boundaries and available room SHALL change, while the buddy, objects, effects, and UI SHALL retain their proportions and SHALL NOT stretch to the new aspect ratio.
5. **FR-001.5:** WHEN the responsive layout is evaluated at standard `4:3`, `16:10`, `16:9`, or `21:9` window shapes THEN the HUD, retractable panel, sandbox border, and interactive play area SHALL remain visible and usable.
6. **FR-001.6:** WHEN the player selects zoom `75%`, `100%`, `125%`, `150%`, `175%`, or `200%` THEN all UI elements and world objects SHALL scale proportionally without changing window dimensions and without requiring an application restart.
7. **FR-001.7:** WHEN no prior zoom preference exists THEN the game SHALL use `100%` zoom.
8. **FR-001.8:** WHEN a saved position, monitor, size, or DPI context is no longer valid THEN the game SHALL clamp the window into a usable monitor area.
9. **FR-001.9:** WHEN no prior topmost preference exists THEN Always on Top SHALL be enabled; WHEN the player changes it THEN the game SHALL apply and retain the chosen value.
10. **FR-001.10:** WHEN the current window size cannot support a zoom level without reducing the sandbox below `360x270` world units THEN that zoom level SHALL be unavailable and the effective zoom SHALL be clamped to the largest supported level, while the stored zoom preference is retained.

### FR-002 — Work Mode, Play Mode, and Pointer Routing

**Linked stories:** US-01, US-03

1. **FR-002.1:** WHILE the game is in Work Mode, transparent-area pointer input SHALL pass through to desktop applications behind the game.
2. **FR-002.2:** WHILE the game is in Play Mode, the bordered sandbox SHALL capture pointer input so that the selected tool can target buddy parts, objects, or empty space as applicable.
3. **FR-002.3:** WHEN the player interacts with the buddy, interacts with an in-game menu, selects a tool, or uses the global mode toggle THEN the game SHALL enter Play Mode.
4. **FR-002.4:** WHEN the player clicks outside the sandbox, presses `Escape`, uses the global mode toggle while in Play Mode, or selects the tray Work Mode action THEN the game SHALL enter Work Mode.
5. **FR-002.5:** WHEN input mode changes THEN the game SHALL preserve the selected tool.
6. **FR-002.6:** WHEN the player next interacts with the buddy after Work Mode THEN the game SHALL apply the already-selected tool rather than substituting a safe or default interaction.
7. **FR-002.7:** WHEN no input occurs THEN the game SHALL NOT change input mode solely because of inactivity.
8. **FR-002.8:** WHEN ordinary pointer access is unavailable because of passthrough or focus state THEN a global hotkey and a system-tray command SHALL remain available to restore game interaction.
9. **FR-002.9:** WHEN the selected global hotkey binding is changed THEN the new binding SHALL be used for subsequent mode toggles.
10. **FR-002.10:** WHEN no custom global mode hotkey exists THEN the game SHALL use `Ctrl+Shift+B`.

### FR-003 — HUD and Menus

**Linked stories:** US-01, US-06

1. **FR-003.1:** WHILE the sandbox is visible, the current money total SHALL remain visible in a compact HUD.
2. **FR-003.2:** WHEN the player opens tools, shop, or settings THEN the game SHALL present those controls in a retractable in-window panel.
3. **FR-003.3:** WHILE mood changes, the game SHALL communicate it through the buddy's head-circle emoticons, posture, movement, and reactions and SHALL NOT expose a permanent numeric mood meter.
4. **FR-003.4:** WHEN damage rewards occur within a `0.25`-second interval THEN the game SHALL coalesce them into brief `+$N.N` feedback while keeping the pain value hidden.
5. **FR-003.5:** WHEN a Boxing Glove impact reaches maximum pain or triggers knockout THEN the game SHALL present the confirmed brief hit-stop with a visibly slow early portion, an impact flash/ring centered on the solver-reported world contact point, canvas-only jolt, glove recoil, face, and nonverbal sound feedback without moving the operating-system window.

### FR-004 — Buddy Identity, Safety, and Physical Form

**Linked stories:** US-02, US-04

1. **FR-004.1:** WHEN the buddy is rendered THEN it SHALL use an original minimalist robot/mannequin design with a readable six-circle silhouette and simple emoticons, including expressions such as `:)` and `:(`, drawn directly on the head circle.
2. **FR-004.2:** WHILE the physical head rotates THEN its emoticon SHALL remain upright in world/screen space without constraining the head body's rotation.
3. **FR-004.3:** WHILE physical interactions occur, the buddy SHALL remain immortal and SHALL NOT die, dismember, or display blood or realistic wounds.
4. **FR-004.4:** WHEN the buddy is struck, burned, grabbed, knocked down, or knocked unconscious THEN feedback SHALL remain non-graphic and slapstick.
5. **FR-004.5:** WHEN the buddy interacts autonomously THEN it SHALL NOT directly attack the player.

### FR-005 — Active-Puppet Simulation and Agency

**Linked stories:** US-02, US-04

1. **FR-005.1:** WHILE active physics is running, the six buddy bodies SHALL be simulated by Godot `RigidBody2D` physics using equal-and-opposite spring/damper forces, maximum-stretch correction, upright torque, and locomotion impulses.
2. **FR-005.2:** WHILE the buddy is simulated, its body parts SHALL NOT collide with one another and SHALL collide with room boundaries, tools, projectiles, and loose objects.
3. **FR-005.3:** WHEN no player command overrides its immediate reaction THEN the buddy SHALL autonomously be capable of idling, approaching, fleeing, walking sideways, jumping, catching, holding, inspecting, consuming, tossing according to mood, and recovering to standing.
4. **FR-005.4:** WHEN the buddy receives a safe object and its current behavior selects a catch or inspection response THEN it SHALL be capable of catching, holding, and inspecting that object.
5. **FR-005.5:** WHEN the buddy receives food or drink and its current behavior selects consumption THEN it SHALL be capable of consuming the item.
6. **FR-005.6:** WHEN the buddy has learned that an object is hazardous THEN it SHALL attempt to drop, discard, or flee that object rather than treating it as safe.
7. **FR-005.7:** WHEN the buddy has been unable to stand for `2` continuous seconds THEN assisted self-righting SHALL begin and SHALL ramp to full strength over `1` second.
8. **FR-005.8:** WHEN self-righting has failed for `10` seconds THEN the game MAY hard-reposition the buddy into a safe state.
9. **FR-005.9:** WHEN a buddy physics state is invalid or outside the sandbox THEN the game MAY hard-reposition it immediately into a safe state.
10. **FR-005.10:** WHEN neither FR-005.8 nor FR-005.9 is true THEN the game SHALL NOT use hard repositioning as normal recovery.
11. **FR-005.11:** WHEN hard recovery occurs THEN the game SHALL release the active grab and held object, clear unstable velocities, rolling pain, knockout, Burning, and temporary statuses, preserve money, unlocks, persistent mood, harmful history, and lifetime statistics, and resume from the safe pose.

### FR-006 — Grab and Physical Manipulation

**Linked stories:** US-02, US-03, US-04

1. **FR-006.1:** WHEN Grab targets any buddy body part or loose object THEN the game SHALL connect that target to the pointer using a damped elastic tether.
2. **FR-006.2:** WHEN the buddy is fearful while grabbed THEN it SHALL visibly oppose the player's pull and move away, causing observable tether stretch.
3. **FR-006.3:** WHEN fear resistance is applied THEN resistance alone SHALL NOT break the grab tether.
4. **FR-006.4:** WHEN the player releases a grabbed target THEN the game SHALL preserve its release velocity subject to a calibrated safe maximum.
5. **FR-006.5:** WHEN a held buddy part receives an otherwise-valid damaging impact THEN the game SHALL award the full normal conscious payout; the grab tether SHALL NOT itself reduce that payout.
6. **FR-006.6:** WHILE a leashed buddy part (any part except the torso) is grabbed THEN its extension from the torso SHALL be clamped to `5` hand widths, where one hand width is twice the grabbed part's radius.
7. **FR-006.7:** WHILE a grabbed limb is held at its stretch limit THEN it SHALL visibly strain and vibrate for `3` seconds, and the vibration SHALL escalate in amplitude and rate over the final `1` second so the release is telegraphed.
8. **FR-006.8:** WHEN the `3`-second strain elapses THEN the limb SHALL snap back to the body, the grab SHALL release, and the buddy SHALL be launched along the stretch direction with an impulse that scales with the peak distance the player pulled beyond the limit, subject to a calibrated maximum.
9. **FR-006.9:** WHEN the player eases the cursor back inside the stretch limit by the confirmed hysteresis margin before the strain elapses THEN the strain countdown and stored launch energy SHALL reset and no snap SHALL occur.
10. **FR-006.10:** WHEN the grabbed target is the torso or a loose object THEN no stretch limit, strain, or snap SHALL apply, and the plain tether behavior of FR-006.1 SHALL govern.
11. **FR-006.11:** WHEN FR-006.3 forbids resistance from breaking the tether THEN that prohibition SHALL apply to buddy-generated resistance only; the FR-006.8 snap is player-caused overpull and is the sole sanctioned tether break.

### FR-007 — Mood, Transient Emotion, and Harmful Memory

**Linked stories:** US-04, US-05

1. **FR-007.1:** WHILE a save exists, the game SHALL maintain one hidden persistent mood value bounded to `-100..+100`.
2. **FR-007.2:** WHEN mood is in `-100..-61`, `-60..-21`, `-20..20`, `21..60`, or `61..100` THEN the buddy SHALL use the fearful, wary, neutral, content, or delighted mood band respectively.
3. **FR-007.3:** WHILE the application is running, mood SHALL drift toward neutral at `0.5` points per minute.
4. **FR-007.4:** WHILE the application is closed, mood SHALL NOT drift.
5. **FR-007.5:** WHEN fear, pain, delight, curiosity, or unconsciousness is produced THEN the game SHALL track it as a transient state independently of persistent mood and SHALL allow it to decay or end according to its own state rules.
6. **FR-007.6:** WHEN a tool harms or threatens the buddy THEN the game SHALL be able to record per-tool harmful experience and learned hazard recognition in persistent progress.
7. **FR-007.7:** WHEN mood crosses upward from below `60` to `60` or higher THEN the game SHALL clear all harmful-history and per-tool fear records.
8. **FR-007.8:** WHEN mood later falls below `60` and again crosses upward to `60` or higher THEN FR-007.7 SHALL trigger again.
9. **FR-007.9:** WHEN mood remains at or above `60` without a new upward crossing THEN the game SHALL NOT repeatedly emit the trust-reset event solely because time passes.

### FR-008 — Gentle Interactions and Care

**Linked stories:** US-03, US-04, US-05

1. **FR-008.1:** WHILE Pet or Tickle is selected, held click-and-drag strokes over buddy body parts SHALL constitute the interaction input.
2. **FR-008.2:** WHEN Pet satisfies its distance/time gate OR friendly Tickle reaches its `3`-second cadence THEN it SHALL grant `+1` mood no more than once per `3` seconds of valid interaction; Angry Tickle follows FR-008.13 instead.
3. **FR-008.3:** WHEN the buddy completes a catch of a safely thrown object THEN it SHALL grant `+1` mood once for that throw/catch event.
4. **FR-008.4:** WHEN the buddy successfully uses a Meal outside its reuse cooldown THEN the Meal SHALL grant `+10` mood and SHALL have a `60`-second reuse cooldown.
5. **FR-008.5:** WHEN the buddy successfully uses a Drink outside its reuse cooldown THEN the Drink SHALL grant `+5` mood and SHALL have a `60`-second reuse cooldown.
6. **FR-008.6:** WHEN the buddy successfully uses a Repair Kit outside its reuse cooldown THEN the Repair Kit SHALL grant `+20` mood, clear transient pain and harmful statuses, and SHALL have a `120`-second reuse cooldown.
7. **FR-008.7:** WHEN a Repair Kit is used during an active knockout THEN it SHALL NOT shorten the fixed knockout duration.
8. **FR-008.8:** WHEN Pet, Tickle, catch, Meal, Drink, or Repair Kit grants care benefits THEN it SHALL award no immediate money.
9. **FR-008.9:** WHEN a mood-granting action would exceed `+100` or a mood-loss action would fall below `-100` THEN the stored mood SHALL remain clamped to its confirmed bounds.
10. **FR-008.10:** WHEN a Meal, Drink, or Repair Kit is cancelled, dropped without successful use, or otherwise fails to be consumed/used THEN its reuse cooldown SHALL NOT begin; successful consumption/use SHALL begin the cooldown.
11. **FR-008.11:** WHILE Pet is held over the buddy THEN satisfaction SHALL accumulate from cursor distance travelled over buddy bodies; WHEN the hidden satisfaction threshold is full AND at least `3` valid-contact seconds have elapsed since the previous Pet reward THEN Pet SHALL grant `+1` mood and reset satisfaction.
12. **FR-008.12:** WHEN Pet becomes selected THEN one of the six body parts SHALL become the favorite spot for that selection; distance travelled over it SHALL contribute `1.2x` Pet satisfaction while the favorite remains hidden and is communicated through expression.
13. **FR-008.13:** WHEN cumulative valid Tickle contact reaches `6` seconds THEN Tickle SHALL enter Angry, cease positive mood awards, apply `-1` mood per further `3` valid-contact seconds, and request angry expression/flee behavior; WHEN `8` seconds elapse without valid Tickle contact THEN tolerance and anger SHALL reset.
14. **FR-008.14:** WHILE friendly Tickle contact continues THEN the buddy MAY hop away from the pointer no more often than once per `1.5` seconds; WHILE Angry Tickle contact continues THEN it MAY hop away no more often than once per `0.75` seconds.
15. **FR-008.15:** WHILE the held Pet hand is rubbing the current favorite spot THEN small original sparkle particles SHALL appear around the Pet hand and SHALL stop when favorite contact, held input, or Pet selection ends.

### FR-009 — Tool Input Conventions

**Linked stories:** US-03

1. **FR-009.1:** WHEN Boxing Glove or Baseball Bat is active THEN it SHALL exist as a cursor-tethered physical collider whose measured swing speed and collision impulse determine its physical impact.
2. **FR-009.2:** WHILE Pistol, Shotgun, or Fire Sprayer is active THEN the tool SHALL remain attached to the cursor and its forward direction SHALL follow the current non-trivial mouse-motion vector.
3. **FR-009.3:** WHEN mouse-wheel input occurs while a cursor weapon is aimed THEN the weapon SHALL rotate upward or downward relative to its current forward direction.
4. **FR-009.4:** WHEN non-trivial cursor movement follows a wheel angle adjustment THEN the game SHALL clear that adjustment and realign the weapon to the new mouse-motion vector.
5. **FR-009.5:** WHEN the player presses primary input with Baseball, Soccer Ball, Meal, Drink, Repair Kit, or Grenade selected THEN the game SHALL request the selected object for pullback launching subject to the loose-object budget in FR-014.
6. **FR-009.6:** WHILE the player holds primary input and drags backward during a pullback launch THEN the game SHALL display a predicted trajectory.
7. **FR-009.7:** WHEN the player releases a pullback launch THEN the object SHALL launch opposite the drag vector.
8. **FR-009.8:** WHEN the player presses secondary input during a held or aimed interaction THEN the game SHALL cancel or drop that interaction without changing the selected tool.
9. **FR-009.9:** WHILE any tool is selected, the operating-system cursor SHALL remain visible; tool actors SHALL render beneath it and SHALL NOT hide or replace it.
10. **FR-009.10:** WHILE primary input is held with Pet or Tickle selected THEN an original animated hand actor SHALL follow the pointer without physical lag beneath the visible operating-system cursor.
11. **FR-009.11:** WHEN the Boxing Glove has been learned as harmful AND approaches a conscious buddy THEN the buddy SHALL flee away from the pointer while placing its physical hand bodies between the threat direction and head/torso; the guard direction SHALL follow the pointer with bounded lag, its targets SHALL remain body-relative rather than attaching to the physical glove, and guard actuation SHALL NOT pull the puppet toward the pointer. WHEN the glove contacts an actively guarding hand THEN the guarded event SHALL use the confirmed `0.5x` absorption factor, while a bypassing strike SHALL remain unmodified.

### FR-010 — Firearms, Grenade, Fire, and Burning

**Linked stories:** US-03, US-04

1. **FR-010.1:** WHEN the Pistol fires THEN it SHALL create a physical continuous-collision-detection projectile, consume one of its `8` magazine rounds, and enforce at least `0.25` seconds before the next shot; WHEN a Pistol reload is initiated THEN it SHALL take `1.2` seconds; reserve ammunition SHALL be unlimited.
2. **FR-010.2:** WHEN the Shotgun fires THEN it SHALL create `6` physical continuous-collision-detection pellets, consume one of its `5` loaded shells, and enforce at least `0.9` seconds before the next shot; WHEN a Shotgun reload is initiated THEN it SHALL take `2` seconds; reserve ammunition SHALL be unlimited.
3. **FR-010.3:** WHEN a Grenade is launched THEN its `2.5`-second fuse SHALL begin.
4. **FR-010.4:** WHEN a buddy without harmful grenade history encounters a launched grenade THEN its available reactions MAY include investigation or catching.
5. **FR-010.5:** WHEN a buddy with harmful grenade history encounters a launched grenade THEN it SHALL attempt to flee or discard it.
6. **FR-010.6:** WHILE primary input is held with Fire Sprayer selected THEN it SHALL spray continuously using the cursor-weapon aiming convention.
7. **FR-010.7:** WHEN fire contacts the buddy THEN it SHALL apply Burning for `4` seconds.
8. **FR-010.8:** WHEN fire contacts an already-burning buddy THEN it SHALL refresh Burning without allowing its remaining/effective duration to exceed `8` seconds.
9. **FR-010.9:** WHILE Burning is active THEN it SHALL produce panic, calibrated periodic pain, mood loss through FR-011.13, and the dropping of held items.
10. **FR-010.10:** WHEN a Repair Kit is successfully used during Burning THEN it SHALL clear Burning immediately.
11. **FR-010.11:** WHEN primary input is newly pressed with Pistol or Shotgun selected THEN the weapon SHALL attempt exactly one shot; holding primary input SHALL NOT repeat fire automatically.
12. **FR-010.12:** WHEN the player presses `R` with Pistol or Shotgun selected THEN that weapon SHALL begin its configured reload when eligible; WHEN the player attempts to fire an empty eligible weapon THEN it SHALL begin reloading automatically.

### FR-011 — Pain, Knockout, and Damage Attribution

**Linked stories:** US-02, US-03, US-05

1. **FR-011.1:** WHEN an impact or harmful effect is accepted as damage THEN the game SHALL represent it as transient pain and SHALL NOT subtract it from a mortal health pool.
2. **FR-011.2:** WHEN the conscious buddy's accepted pain totals `100` or more within a rolling `5`-second window THEN the buddy SHALL enter unconsciousness.
3. **FR-011.3:** WHEN unconsciousness begins THEN it SHALL last exactly `4` seconds before natural, physics-driven recovery begins.
4. **FR-011.4:** WHEN additional pain occurs during unconsciousness THEN it SHALL NOT restart or extend the active `4`-second knockout timer.
5. **FR-011.5:** WHEN a valid damage event hits the head, torso, or an arm/leg THEN its payout calculation SHALL use body-region multiplier `1.2x`, `1.0x`, or `0.8x` respectively.
6. **FR-011.6:** WHEN a valid damage event occurs while the buddy is unconscious THEN its payout calculation SHALL use an unconscious multiplier of `0.5x`.
7. **FR-011.7:** WHEN a valid damage event occurs while the buddy is conscious THEN its unconscious multiplier SHALL be `1.0x`.
8. **FR-011.8:** WHEN money is awarded for damage THEN the amount SHALL be calculated as `accepted pain x body-region multiplier x unconscious multiplier x cash-per-pain` and SHALL NOT include a hidden per-tool payout multiplier.
9. **FR-011.9:** WHEN a source remains in continuous contact with the same buddy body THEN that contact episode SHALL pay once, with a `0.15`-second source/body debounce suppressing duplicate physics callbacks.
10. **FR-011.10:** WHEN the same tool is reused in a later valid contact episode THEN it SHALL have no diminishing-return modifier beyond contact deduplication and the tool's normal cadence.
11. **FR-011.11:** BEFORE launch economy implementation is accepted, impulse-to-pain conversion SHALL be calibrated and documented through physics-laboratory playtesting against the v1.01 reference feel and the numeric knockout behavior in FR-011.2 through FR-011.4.
12. **FR-011.12:** WHEN a calibrated impact with a room boundary, loose object, projectile, or physical weapon exceeds its pain threshold THEN it SHALL enter the pain pipeline and SHALL retain the originating tool/throw attribution when available.
13. **FR-011.13:** WHEN an accepted harmful event produces pain THEN mood SHALL decrease by `min(10, pain x 0.1)`; Burning pain ticks SHALL use the same rule and knockout SHALL add no separate mood penalty.
14. **FR-011.14:** WHEN knockout begins THEN the rolling pain window SHALL clear; WHILE unconscious, accepted hits SHALL still pay and affect mood but SHALL NOT accumulate toward the next knockout; WHEN the buddy wakes THEN the rolling window SHALL be empty.
15. **FR-011.15:** WHEN currency is stored THEN it SHALL use signed 64-bit milli-credits with `1000` minor units per displayed credit; fractional rewards SHALL accumulate, while the HUD, shop prices, and balances SHALL display whole credits.
16. **FR-011.16:** WHEN a launched or thrown object impacts the buddy before it has first come to rest and before any reassigning interaction THEN the impact SHALL attribute to the originating tool/throw; WHEN it impacts after coming to rest or after reassignment THEN it SHALL attribute to the generic loose-object source; boundary bounces alone SHALL NOT clear attribution.

### FR-012 — Passive Income and Economy Rules

**Linked stories:** US-05, US-06

1. **FR-012.1:** WHILE the application is running, it SHALL accrue passive income using a multiplier derived from persistent mood.
2. **FR-012.2:** WHEN mood is `-100` THEN the passive-income multiplier SHALL be `0.25x`; WHEN mood is neutral THEN the multiplier SHALL pass through `1.0x`; WHEN mood is `+100` THEN the multiplier SHALL be `2.0x`, with piecewise-linear interpolation between the approved anchors.
3. **FR-012.3:** WHEN passive-income calibration is evaluated at its peak THEN expected passive earnings SHALL be approximately `25%` of the expected earnings of an actively attacking player under the approved benchmark session.
4. **FR-012.4:** WHILE the application is closed, sleeping, suspended, or traversing an excluded clock discontinuity THEN it SHALL award no passive-income catch-up.
5. **FR-012.5:** WHEN damage money is calculated THEN cash-per-pain SHALL be the shared economy coefficient across tools and SHALL be calibrated together with catalogue prices against FR-013's progression targets.
6. **FR-012.6:** WHEN care raises mood THEN its economic effect SHALL occur through mood-scaled passive income rather than immediate care payout.
7. **FR-012.7:** WHEN the player earns or spends currency THEN the game SHALL use a single earnable currency and SHALL offer no real-money microtransactions.

### FR-013 — Catalogue, Ownership, and Progression

**Linked stories:** US-03, US-06

1. **FR-013.1:** WHEN a new save is created THEN money SHALL be `0`, Grab SHALL be selected, and Grab, Pet, Tickle, and Boxing Glove SHALL be available immediately.
2. **FR-013.2:** WHEN the launch catalogue is complete THEN it SHALL contain Grab, Pet, Tickle, Boxing Glove, Baseball, Soccer Ball, Baseball Bat, Pistol, Shotgun, Grenade, Fire Sprayer, Meal, Drink, Repair Kit, and the Strength Upgrade of FR-019 (`15` entries).
3. **FR-013.3:** WHEN the player purchases an item THEN ownership SHALL become immediate and permanent, use SHALL be unlimited subject to confirmed cooldowns/cadence, and the item SHALL NOT be sellable or refundable.
4. **FR-013.4:** WHEN prices and income coefficients are calibrated using the approved cumulative-play benchmark THEN the expected purchase sequence SHALL target Baseball at `3` minutes, Meal at `6`, Baseball Bat at `20`, Pistol at `30`, Grenade at `40`, Fire Sprayer at `50`, Soccer Ball at `65`, Drink at `80`, Shotgun at `100`, and Repair Kit at `120` minutes. The Strength Upgrade has no confirmed slot in this schedule; its position is a Milestone 5 calibration decision (see FR-019.7).
5. **FR-013.5:** WHEN progression calibration is accepted THEN the complete current interaction catalogue SHALL be affordable in approximately `2` hours of cumulative play under the approved active/passive play mix.
6. **FR-013.6:** WHEN the player requests progression reset THEN the game SHALL require explicit confirmation before erasing progress.

### FR-014 — Loose-Object Budget

**Linked stories:** US-02, US-03, US-08

1. **FR-014.1:** WHILE the sandbox is active, no more than `24` loose physics objects SHALL exist.
2. **FR-014.2:** WHEN admitting a new loose object would exceed `24` and an eligible object exists THEN the game SHALL remove the oldest safe object that is neither held nor otherwise protected.
3. **FR-014.3:** WHEN selecting an object for cap enforcement THEN the game SHALL NOT remove a hazardous, held, or otherwise protected object under the confirmed eviction rule.

### FR-015 — Save, Load, and Session Resume

**Linked stories:** US-07

1. **FR-015.1:** WHEN progress is saved THEN the semantic save state SHALL include money, unlocks, selected tool, persistent mood, harmful-history/per-tool memory, statistics, and user settings.
2. **FR-015.2:** WHEN progress is saved THEN it SHALL NOT include live body pose, loose objects, active projectiles, recent pain window, knockout state, or temporary statuses.
3. **FR-015.3:** WHEN a valid save is loaded THEN the game SHALL restore semantic progress and SHALL start the buddy in a safe standing pose.
4. **FR-015.4:** WHEN save data is written THEN it SHALL use versioned JSON, atomic replacement, and one rolling backup.
5. **FR-015.5:** WHEN a save file is detected as corrupt THEN the game SHALL quarantine it before attempting fallback recovery.
6. **FR-015.6:** WHEN semantic progress is dirty and `30` seconds have elapsed since the prior dirty flush THEN the game SHALL autosave.
7. **FR-015.7:** WHEN a purchase, unlock, focus loss, or clean exit occurs THEN the game SHALL immediately flush dirty semantic progress.
8. **FR-015.8:** WHEN the game records window position, size, monitor, DPI context, or other machine-specific settings THEN those values SHALL remain local and SHALL be excluded from Steam Cloud progression data.
9. **FR-015.9:** WHEN the application is closed, Windows sleeps/suspends, or a large clock discontinuity is detected THEN loading or resuming SHALL NOT synthesize missed mood drift or income.
10. **FR-015.10:** WHEN the application resumes from suspension or an excluded clock discontinuity THEN it SHALL clear accumulated physics time so that no simulation burst occurs.

### FR-016 — Tray and Application Lifecycle

**Linked stories:** US-01, US-07, US-08

1. **FR-016.1:** WHEN the tray menu is opened THEN it SHALL provide Show/Hide, Work/Play Mode, Always on Top, Return to Bottom-Right, Reset Buddy, Settings, and Save & Quit actions.
2. **FR-016.2:** WHEN the application is hidden to the tray THEN it SHALL suspend rendering and ragdoll physics while continuing mood timers and passive income.
3. **FR-016.3:** WHEN the application is shown after hidden operation THEN it SHALL resume foreground presentation without simulating the skipped ragdoll interval.
4. **FR-016.4:** WHEN the player selects Return to Bottom-Right THEN the window SHALL move to the confirmed lower-right placement relative to the current monitor's usable work area.
5. **FR-016.5:** WHEN the player selects Reset Buddy THEN the live buddy SHALL return to a safe state without implying a progression reset.
6. **FR-016.6:** WHEN the player selects Save & Quit THEN the game SHALL flush dirty progress before a clean exit.
7. **FR-016.7:** WHEN no Windows-startup preference exists THEN Launch with Windows SHALL be disabled; WHEN enabled by the player THEN the game SHALL retain that preference.
8. **FR-016.8:** WHEN the Windows session is locked THEN mood drift and passive income SHALL continue as running time without a clock-discontinuity exclusion; WHEN the session unlocks THEN the game SHALL restore its prior presentation state.

### FR-017 — Settings and Feedback

**Linked stories:** US-01, US-08

1. **FR-017.1:** WHEN Settings is opened THEN it SHALL expose master volume, SFX volume, mute while in Work Mode, reduced motion, screen-shake, reduced particles, photosensitivity-safe effects, zoom, anti-aliasing, V-sync, Always on Top, and global mode-hotkey controls.
2. **FR-017.2:** WHEN mute while in Work Mode is enabled and Work Mode is active THEN game SFX SHALL be muted without requiring a restart.
3. **FR-017.3:** WHEN reduced motion, screen-shake, reduced particles, photosensitivity-safe effects, anti-aliasing, or V-sync is changed THEN subsequent presentation SHALL honor that setting.
4. **FR-017.4:** WHILE the buddy communicates in the first release, it SHALL use head-circle emoticons, body language, status icons, and original nonverbal robot sounds and SHALL NOT use written dialogue or voice acting.
5. **FR-017.5:** WHEN launch input coverage is evaluated THEN every required interaction SHALL be operable with mouse and keyboard; no controller or touch support is required.
6. **FR-017.6:** WHEN no saved presentation settings exist THEN V-sync SHALL be On, anti-aliasing SHALL be `2x` MSAA, Master and SFX volume SHALL each be `50%`, Mute in Work Mode SHALL be On, Screen Shake SHALL be On, Reduced Motion and Reduced Particles SHALL be Off, and Photosensitivity-Safe Effects SHALL be On.
7. **FR-017.7:** WHEN the anti-aliasing setting is opened THEN it SHALL offer Off, `2x`, `4x`, and `8x` MSAA; WHEN the V-sync setting is opened THEN it SHALL offer On and Off.
8. **FR-017.8:** WHEN screen shake occurs THEN it SHALL affect only rendered game content and SHALL NOT move the operating-system window.
9. **FR-017.9:** WHEN player-facing text is implemented THEN it SHALL resolve through externalized translation resources with stable keys; the first release SHALL ship English as the only locale.

### FR-018 — Steam Integration, Statistics, and Achievements

**Linked stories:** US-09

1. **FR-018.1:** IF Steam initialization is absent or fails THEN local play and local saves SHALL remain fully available.
2. **FR-018.2:** WHEN Steam Cloud is available THEN the game SHALL synchronize progression data and SHALL exclude machine-specific window and local settings data.
3. **FR-018.3:** WHEN Steam is unavailable and a stat or achievement update is earned THEN the game SHALL queue that update locally; WHEN Steam reconnects THEN the game SHALL synchronize the queued update.
4. **FR-018.4:** WHILE a save is active, the game SHALL track total money earned; best earnings over `1`, `3`, and `10` second intervals; total running time; active-interaction time; hidden-passive time; total pain; knockouts; successful catches; highest and lowest mood; and per-tool uses and pain caused.
5. **FR-018.5:** WHEN the player earns damage money for the first time THEN the game SHALL unlock **First Impression**.
6. **FR-018.6:** WHEN the player causes the first knockout THEN the game SHALL unlock **Lights Out**.
7. **FR-018.7:** WHEN the player buys the first tool or care item THEN the game SHALL unlock **Retail Therapy**.
8. **FR-018.8:** WHEN the player unlocks the complete launch catalogue THEN the game SHALL unlock **Full Toybox**.
9. **FR-018.9:** WHEN mood reaches `+100` THEN the game SHALL unlock **Best Friends**.
10. **FR-018.10:** WHEN harmful history is cleared by the upward mood crossing at `60` THEN the game SHALL unlock **Forgiven**.
11. **FR-018.11:** WHEN the successful-catch total reaches `25` THEN the game SHALL unlock **Nice Catch**.
12. **FR-018.12:** WHEN the player has used every launch interaction THEN the game SHALL unlock **Variety Hour**.
13. **FR-018.13:** WHEN a Repair Kit clears Burning THEN the game SHALL unlock **Fire Drill**.
14. **FR-018.14:** WHEN total running time reaches `2` hours THEN the game SHALL unlock **Desktop Shift**.

## 5. Non-Functional Requirements

### FR-019 — Strength Upgrade (Milestone 5 shop item)

**Linked stories:** US-03, US-06

Owner-requested 2026-07-25. This is the catalogue's first **passive permanent upgrade** rather
than a selectable tool: it is purchased once and then modifies how Grab behaves, so it occupies
a shop slot but never appears in tool selection.

1. **FR-019.1:** WHEN the player owns the Strength Upgrade THEN the grab tether's pull force and force ceiling SHALL increase by a calibrated factor, giving the player more control over the buddy.
2. **FR-019.2:** WHEN the player owns the Strength Upgrade THEN the FR-006.6 stretch limit SHALL increase by a calibrated factor, so a limb can be pulled visibly further than an unupgraded grab allows.
3. **FR-019.3:** WHEN the player owns the Strength Upgrade THEN the FR-006.8 snap-back SHALL NOT occur: a strained limb SHALL continue to strain for as long as the player holds it, and the buddy SHALL NOT be able to break free of the grab by snapping its limb back.
4. **FR-019.4:** WHEN the player owns the Strength Upgrade THEN the FR-006.4 release velocity SHALL be scaled by a calibrated yank factor, subject to its own calibrated safe maximum, so throws are stronger.
5. **FR-019.5:** WHEN the Strength Upgrade is owned THEN buddy fear resistance (FR-006.2) SHALL still be generated and still be visible; the upgrade increases the player's authority over the outcome and SHALL NOT remove the buddy's reaction.
6. **FR-019.6:** WHEN the Strength Upgrade is owned THEN it SHALL grant no damage, payout, or mood modifier of its own; its economic effect SHALL arrive only through the stronger manipulation it enables.
7. **FR-019.7:** WHEN Milestone 5 economy calibration is performed THEN the Strength Upgrade's price and its slot in the FR-013.4 progression schedule SHALL be set, and the FR-013.5 two-hour affordability target SHALL be re-validated against the enlarged `15`-entry catalogue.

**Open questions for the owner (do not infer):**

- **Name.** "Strength Upgrade" is a working label, not a confirmed product name.
- **Tiers.** One purchase, or several escalating tiers? FR-019 currently specifies exactly one.
- **Snap immunity scope.** FR-019.3 removes snap-back entirely. The alternative is a longer strain
  window rather than true immunity, which keeps the mechanic alive at high upgrade levels.
- **Magnitudes.** Every "calibrated factor" above is deliberately unset; they are Milestone 5
  tuning, judged against FR-013.4/13.5.

### NFR-001 — Platform and Release Compatibility

1. **NFR-001.1:** WHEN a launch build is produced THEN it SHALL target Windows 10/11 x86_64 through Godot 4.6.1 .NET and C#.
2. **NFR-001.2:** WHEN the first Steam release is accepted THEN no other operating system, profile system, multiplayer mode, or multi-buddy mode SHALL be required.

### NFR-002 — Performance

1. **NFR-002.1:** WHILE active simulation is running, physics SHALL execute at a fixed `120 Hz` and SHALL NOT dynamically lower its tick rate.
2. **NFR-002.2:** WHILE foreground play is active, physics interpolation SHALL be enabled and rendered output SHALL target at least `60 FPS`, subject to the player's V-sync setting.
3. **NFR-002.3:** WHEN benchmarked at `480x360` with `24` loose objects on an Intel i5-8400/UHD 630-class PC THEN foreground play SHALL target less than `5%` CPU utilization and less than `300 MB` RAM.
4. **NFR-002.4:** WHEN hidden to the tray on the reference PC THEN operation SHALL target less than `0.5%` CPU utilization.

### NFR-003 — Physics Quality and Repeatability

1. **NFR-003.1:** BEFORE economy or shop implementation begins, the physics laboratory SHALL demonstrate idle, approach, standing, sideways walking, jumping, falling, fear/flee movement, tether resistance, unconsciousness, natural recovery, catch/hold/inspect/consume/toss behavior, and self-righting with the six-body puppet.
2. **NFR-003.2:** WHEN seeded headless physics scenarios are repeated THEN maximum spring stretch, recovery timing, damage attribution, and outcome stability SHALL remain within documented tolerance envelopes.
3. **NFR-003.3:** WHEN physics acceptance is reviewed THEN responsive control, fun, consistency, and recognizable v1.01-style active-puppet behavior SHALL be assessed through a documented reference-comparison playtest in addition to automated limits.
4. **NFR-003.4:** WHEN replay behavior differs below bit-exact precision but remains within approved tolerance envelopes THEN the build MAY pass; bit-exact deterministic replay is not required.

### NFR-004 — Reliability and Data Integrity

1. **NFR-004.1:** WHEN a write is interrupted before atomic replacement completes THEN the prior valid save or rolling backup SHALL remain available for recovery.
2. **NFR-004.2:** WHEN invalid live physics state is encountered THEN the recovery rules in FR-005 SHALL prevent that state from being persisted into a later session.
3. **NFR-004.3:** IF Steam services are unavailable THEN failure SHALL be isolated from local simulation, purchases, progress, and saving.

### NFR-005 — Presentation, Comfort, and Originality

1. **NFR-005.1:** WHEN launch art is reviewed THEN it SHALL use crisp anti-aliased vector/shape art, flat colors, dark outlines, simple circular forms, restrained shading, and an original modernized Flash-era panel style.
2. **NFR-005.2:** WHEN launch content is reviewed THEN it SHALL contain no copied reference art, audio, dialogue, skins, branding, or other expressive content.
3. **NFR-005.3:** WHEN visual comfort options are enabled THEN reduced motion, screen-shake, reduced-particle, and photosensitivity-safe settings SHALL affect the corresponding feedback without changing economy or damage outcomes.

### NFR-006 — Architecture and Testability Constraints

1. **NFR-006.1:** WHEN gameplay scenes are implemented THEN scene roots SHALL remain thin orchestrators; single-purpose C# components SHALL receive typed scene references, communicate upward through signals/events, and receive downward behavior through explicit methods or commands.
2. **NFR-006.2:** WHEN physics tuning, tools, mood profiles, economy values, or content metadata are authored THEN typed Godot `Resource` assets SHALL contain those definitions; versioned JSON SHALL be reserved for runtime save state.
3. **NFR-006.3:** WHEN domain rules are implemented THEN pure C# unit tests SHALL cover rules that do not require the Godot runtime.
4. **NFR-006.4:** WHEN simulation-dependent behavior is implemented THEN headless Godot scenarios with seeded inputs and tolerance envelopes SHALL cover it.
5. **NFR-006.5:** WHEN an implementation choice is absent from this document and the confirmed decision log THEN implementation SHALL pause for product-owner clarification rather than inventing product behavior.

## 6. Numeric Rules Traceability

| Rule | Confirmed value | Requirement coverage |
|---|---:|---|
| Default / minimum window | `480x360` / `360x270` logical px | FR-001.2–FR-001.3 |
| Initial desktop offset | `16 px` from lower-right usable edge | FR-001.2 |
| Default mode hotkey | `Ctrl+Shift+B` | FR-002.10 |
| Zoom choices | `75/100/125/150/175/200%`; default `100%` | FR-001.6–FR-001.7 |
| Minimum sandbox room | `360x270` world units | FR-001.10 |
| Fixed physics frequency | `120 Hz` | NFR-002.1 |
| Foreground render target | `>=60 FPS` | NFR-002.2 |
| Foreground reference budget | `<5% CPU`, `<300 MB RAM` | NFR-002.3 |
| Hidden reference budget | `<0.5% CPU` | NFR-002.4 |
| Self-righting | starts after `2 s`, ramps up to `5 s`, hard recovery after `10 s` failure | FR-005.7–FR-005.10 |
| Mood domain / bands | `-100..+100`; five confirmed bands | FR-007.1–FR-007.2 |
| Mood drift | `0.5 points/min` toward neutral while running | FR-007.3–FR-007.4 |
| Trust reset | upward crossing from `<60` to `>=60` | FR-007.7–FR-007.9 |
| Pet / Tickle | `+1 mood / 3 s` valid contact | FR-008.2 |
| Safe catch | `+1 mood` per completed catch | FR-008.3 |
| Meal | `+10 mood`, `60 s` cooldown | FR-008.4 |
| Drink | `+5 mood`, `60 s` cooldown | FR-008.5 |
| Repair Kit | `+20 mood`, `120 s` cooldown | FR-008.6–FR-008.7 |
| Care cooldown activation | successful consumption/use only | FR-008.10 |
| Pistol | `8` rounds, `0.25 s` cadence, `1.2 s` reload | FR-010.1 |
| Shotgun | `6` pellets, `5` shells, `0.9 s` cadence, `2 s` reload | FR-010.2 |
| Firearm input | one shot per press; `R` or empty auto-reload | FR-010.11–FR-010.12 |
| Grenade fuse | `2.5 s` from launch | FR-010.3 |
| Burning | `4 s`, refreshable up to `8 s` | FR-010.7–FR-010.10 |
| Knockout threshold | `100 pain / rolling 5 s` | FR-011.2 |
| Knockout duration | exactly `4 s`, never extended by hits | FR-011.3–FR-011.4 |
| Region payout | head `1.2x`, torso `1.0x`, limbs `0.8x` | FR-011.5 |
| Unconscious payout | `0.5x` | FR-011.6–FR-011.8 |
| Contact deduplication | `0.15 s` source/body debounce | FR-011.9 |
| Harmful-event mood loss | `min(10, pain x 0.1)` | FR-011.13 |
| Knockout pain history | clear on knockout; unconscious hits do not carry forward | FR-011.14 |
| Currency precision | signed 64-bit; `1000` minor units per displayed credit | FR-011.15 |
| Reward coalescing | `0.25 s`, displayed as `+$N.N` | FR-003.4 |
| Passive mood anchors | `0.25x` at `-100`, `1.0x` neutral, `2.0x` at `+100` | FR-012.2 |
| Passive/active target | peak passive approximately `25%` of active attack earnings | FR-012.3 |
| Progression targets | `3, 6, 20, 30, 40, 50, 65, 80, 100, 120 min` | FR-013.4–FR-013.5 |
| Loose-object cap | `24` | FR-014 |
| Dirty autosave interval | `30 s` plus event saves | FR-015.6–FR-015.7 |
| Presentation defaults | V-sync On; `2x` MSAA; Master/SFX `50%` | FR-017.6–FR-017.8 |
| Achievement thresholds | `25` catches; `2 h` running time | FR-018.11, FR-018.14 |

## 7. Calibration Requirements and Unfixed Coefficients

The following are intentionally not assigned guessed launch values. Each SHALL be stored as typed tuning data, measured in the physics laboratory or economy benchmark, documented with its test protocol, and approved against the linked requirements:

- Spring stiffness/damping, maximum stretch, upright torque, locomotion impulses, and the safe maximum grab-release velocity: calibrate against FR-005, FR-006, and NFR-003.
- Per-source impulse/effect-to-pain conversion, including Burning tick pain: calibrate against FR-011's knockout, attribution, and repeatability rules.
- Base passive-income rate, exact piecewise-linear mood curve knots within the confirmed neutral range, cash-per-pain, and item prices: calibrate against FR-012 and FR-013.
- Trajectory preview sampling and launch-force scale: calibrate for readable agreement between the preview and actual physics without changing the confirmed pullback direction convention.

No calibration may introduce a hidden per-tool money multiplier, change a confirmed numeric rule, or substitute time-gating for permanent currency purchases.

## 8. Launch Scope

The first Steam release includes:

- One original six-body buddy in one transparent, resizable Windows sandbox and one save slot.
- Autonomous idle, approach, flee, walk, jump, catch, hold, inspect, consume, toss, unconscious, and self-recovery behavior.
- Work/Play input modes, click passthrough, global recovery hotkey, tray controls, responsive HUD/panel, window persistence, zoom, and confirmed presentation settings.
- The fourteen launch interactions listed in FR-013.2, permanent shop ownership, one earnable currency, damage earnings, mood, care, passive income, and the two-hour target progression.
- Versioned resilient local saving, Steam Cloud progression, Steam stats, and the ten launch achievements.
- Mouse and keyboard input, original nonverbal audio, non-graphic slapstick presentation, and the confirmed accessibility/performance options.

## 9. Explicitly Out of Scope

- Blood, bleeding, realistic injury, death, or dismemberment.
- Copied *Interactive Buddy* art, audio, dialogue, skins, branding, or other expressive content.
- Written dialogue or voice acting.
- Multiple buddies, multiple save slots/profiles, multiplayer, controller requirements, or touch requirements.
- Offline earnings or mood/income catch-up after application closure, Windows sleep/suspend, or large clock gaps.
- Real-money microtransactions, consumable ammunition reserves, item resale, or refunds.
- Non-Windows launch platforms.
- Bit-exact deterministic replay.
- Buddy coloring/painting, cosmetic progression, and Steam Workshop/mod support.

## 10. Future Scope (Not a Launch Commitment)

- Optional non-realistic bleeding, subject to a separate product and presentation specification.
- Buddy coloring and paint interactions as content padding.
- Cosmetic progression capable of extending the progression curve beyond the current two-hour catalogue target.
- Steam Workshop support for custom buddies; architecture may avoid needless lock-in, but no Workshop API, mod format, compatibility contract, or custom-buddy tooling is required now.

## 11. Acceptance Gates

1. **Physics gate:** The NFR-003 physics laboratory passes before economy or shop implementation begins.
2. **Rules gate:** Pure C# tests pass for mood bounds/bands, trust reset, care cadence/cooldowns, rolling pain, knockout duration, payout multipliers/formula, purchase permanence, save selection, and achievement triggers.
3. **Simulation gate:** Seeded headless scenarios pass documented tolerance envelopes for spring stretch, standing and recovery timing, grab resistance, collision attribution, contact deduplication, projectiles, Burning duration, object catching, and repeated-run stability.
4. **Overlay gate:** Manual Windows 10/11 tests pass for transparency, pointer passthrough, outside-click Work Mode, global/tray recovery, Always on Top, lower-right placement, multi-monitor clamping, DPI persistence, and layouts at `4:3`, `16:10`, `16:9`, and `21:9` across all zoom levels.
5. **Persistence gate:** Automated fault tests pass for atomic replacement, rolling-backup recovery, corrupt-file quarantine, semantic/non-semantic field boundaries, event autosaves, safe standing resume, and no sleep/clock-gap catch-up.
6. **Economy gate:** A documented playtest benchmark demonstrates the ordered affordability targets and approximately two-hour full-catalogue target while peak passive income remains approximately `25%` of active attack earnings.
7. **Performance gate:** Reference-hardware benchmarks report the NFR-002 foreground and hidden budgets with the fixed `120 Hz` simulation and `24` loose objects.
8. **Steam gate:** Achievement/stat queueing, reconnection sync, progression-only Cloud behavior, and full local fallback pass with Steam available, offline, and initialization-failed.
