# Desktop Buddy — Confirmed Decisions

Status: Living decision log for requirements and architecture planning.

This file records only decisions explicitly confirmed by the project owner. Unresolved details belong in the requirements process and must not be inferred by implementation agents.

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
- **Save format:** Progress uses versioned JSON, atomic replacement, one rolling backup, and quarantine of corrupt files before fallback recovery.
- **Autosave:** Dirty progress flushes every `30` seconds and immediately after purchases, unlocks, focus loss, and clean exit.
- **Tray controls:** Show/Hide, Work/Play Mode, Always on Top, Return to Bottom-Right, Reset Buddy, Settings, and Save & Quit.
- **Windows startup:** Launch with Windows is optional and disabled by default.
- **Hidden operation:** While hidden to the tray, rendering and ragdoll physics are suspended; mood timers and passive income continue at low cost.
- **No catch-up:** Closing the app, Windows sleep/suspend, or a large clock discontinuity grants no mood or income catch-up. On resume, the physics accumulator is cleared to prevent a simulation burst.
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
