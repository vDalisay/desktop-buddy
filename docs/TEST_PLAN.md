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

### Persistence and Steam Abstraction

- Current save data round-trips without loss and older supported versions migrate explicitly.
- Live pose, objects, projectiles, pain, knockout, and temporary statuses never enter progress data.
- Atomic replacement preserves the previous backup if a write fails.
- Corrupt primary data is quarantined, then backup/default recovery occurs without crashing.
- Cloud payload excludes machine-specific settings.
- Steam initialization failure selects the local implementation.
- Offline stat/achievement events queue once and synchronize idempotently after reconnection.

## 3. Headless Physics Scenarios

Every scenario uses seeded scripted inputs and asserts ranges/tolerances rather than bit-exact transforms.

### Puppet Stability

- Spawn and settle at each supported zoom level.
- Remain finite, connected, and contained during a 30-minute accelerated idle soak.
- Never exceed the configured maximum-stretch bound.
- Walk left/right, jump, land, and remain recognizable without self-collision instability.
- Run the same scenario repeatedly and keep outcome metrics inside the approved envelopes.
- Buddy parts never enter physics sleep; settled loose objects do sleep, and eviction/registry behavior stays correct on sleeping bodies.

### Grab and Recovery

- Grab and release each of the six parts and at least one loose object.
- The tether stretches under fearful resistance but does not break solely from resistance.
- Release velocity is preserved and capped.
- After two seconds unable to stand, assisted recovery begins; hard recovery does not occur before ten seconds unless state is invalid/out of bounds.
- Assisted recovery reaches full strength in one second and restores standing within the accepted `1.5`-second physical scenario bound.
- After knockout expires, active drive ramps back without a teleport.
- NaN, infinite, or escaped-body injection triggers immediate safe recovery.
- Hard recovery releases grabs/held objects, clears transient physics/pain/knockout/status state, and preserves persistent progress.

### Tools and Collision Attribution

- Glove and bat damage comes from physical contact rather than tool activation.
- Boxing Glove cursor lag remains within the accepted response envelope; faster real strikes increase measured impulse/pain; the impact marker is centered on the solver contact point; maximum-pain/knockout hit-stop has a visibly slow early curve, is non-stacking, and restores the prior time scale.
- Learned Boxing Glove harm raises both real hand bodies into a body-relative guard while the buddy travels away from the pointer. The guard direction follows pointer changes with bounded lag, never attaches to the physical glove, and applies an equal reaction so guard actuation cannot pull the puppet toward the threat. Guard-hand contact applies `0.5x` accepted impulse plus a matching physical counter-impulse; strikes around the hands remain unmodified.
- The physical head rotates while the emoticon remains upright; Pet/Tickle cursor hands follow real pointer input instantly beneath the visible OS cursor.
- Favorite-spot Pet contact emits small sparkles around the Pet hand only while held rubbing contact remains valid.
- Pistol/shotgun cadence, magazine, reload, pellet count, and CCD behavior match the specification.
- Pistol/shotgun fire once per primary press, reload with `R`, and auto-reload after an attempted empty shot.
- Grenade fuse begins on release and expires after 2.5 seconds within one physics tick.
- Fire duration refreshes from four seconds up to the eight-second cap; Repair Kit clears it.
- Pullback launch direction is opposite the drag vector and its preview matches the resulting ballistic path within the configured tolerance.
- Contacts attribute the correct source, buddy region, pain, mood change, payout, and statistics.
- A thrown object that bounces off a boundary before striking the buddy still credits the originating throw; the same object striking after coming to rest, or after the buddy tosses/discards it, attributes to the generic loose-object source.
- Spawning object 25 removes the oldest eligible safe/unheld object and never removes a protected object.

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

## 4. Economy Simulation

Create deterministic benchmark input traces for a representative mixed active/passive player. Tune prices and cash-per-pain together until median target purchases occur near cumulative minutes `3`, `6`, `20`, `30`, `40`, `50`, `65`, `80`, `100`, and `120` in the approved order.

The benchmark must also prove:

- Active attacking remains the dominant income source.
- Maximum-mood passive income is approximately 25% of benchmark active income.
- No single ordinary event skips multiple intended unlock milestones.
- Repeated legitimate use remains rewarding; duplicate physics callbacks do not print money.

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
