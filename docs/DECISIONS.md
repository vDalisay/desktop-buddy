# Desktop Buddy — Confirmed Decisions

Status: Living decision log for requirements and architecture planning.

This file records only decisions explicitly confirmed by the project owner. Unresolved details belong in the requirements process and must not be inferred by implementation agents.

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
  Lambert-wrap diffuse, toon specular at `0.08`, roughness `1.0`, warm key/cool fill
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

## Accepted Milestone 1 Feel Tuning (2026-07-12)

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
- **Pullback launcher:** Balls, care items, and grenades spawn on primary press. Holding and dragging backward displays a predicted trajectory; release launches the object opposite the drag vector in an Angry Birds-style interaction.
- **Secondary action:** Right mouse cancels or drops the current held/aimed interaction without changing the selected tool.
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

## Planning Rule

When a requirement or implementation choice is not covered here or in an approved specification, the implementation agent must stop and ask the project owner rather than inventing product behavior.
