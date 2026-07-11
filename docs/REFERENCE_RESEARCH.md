# Desktop Buddy — Reference Research

Status: Development-only clean-room behavior notes. This document is not player-facing content.

## 1. Canonical Reference Policy

- The primary mechanical reference is the author-uploaded [Interactive Buddy v1.01 Newgrounds page](https://www.newgrounds.com/portal/view/218014).
- The [archived v1.02 build](https://archive.org/details/interactive_buddy_v_1_02_by_shock_value_d6ma8m) is a secondary motion/feel reference when it does not conflict with v1.01.
- [MobyGames screenshots](https://www.mobygames.com/game/211056/interactive-buddy/screenshots/) may be used to identify broad screen states and interaction categories.
- Community wikis and hosted copies are cross-checks only; they may mix versions and must not override the canonical policy.

The project must never commit or ship an original SWF, decompiled source, original assets, dialogue, audio, skins, UI copy, or trademarked/parody likenesses. Reference analysis informs behavior; all shipped expression is original.

## 2. Verified Original Behavior

Direct ActionScript inspection of the Newgrounds v1.01 artifact identified these mechanically important traits:

- The buddy is a six-circle active puppet: torso, head, two arm/hand circles, and two leg/foot circles.
- The five satellite bodies are spring-driven toward rotating offsets around the torso with bounded stretch. It is not a conventional articulated skeleton.
- Alive behavior applies upright torso stabilization. Knockout removes/reverses that drive, and recovery restores it rather than playing a skeletal get-up animation.
- Walking/running use horizontal whole-body impulses plus oscillating leg impulses. Jumping applies upward impulses across the body.
- Whole-body force propagation, soft bounded limb separation, simple circular collisions, damping, and fast stabilization create the recognizable feel.
- Open Hand is positive, Tickle causes playful motion, and hard interactions lower a saved mood scalar.
- The buddy may investigate/catch a first grenade and flee later grenades after learning they explode.
- Scared behavior flees and trembles; burning causes panic, jumping/pacing, and dropping held objects.
- The original mood range is `-100..100` and strongly affects happy/sad reactions.
- The inspected build uses hard-event knockout logic, not Desktop Buddy's confirmed rolling pain window.

The author page corroborates the weapon-to-money loop, discoverable happiness, simple graphics chosen for frame rate, and an intended Flash frame rate near 36 FPS. A secondary [Flash game overview](https://flashgaming.fandom.com/wiki/Interactive_Buddy) corroborates the six-circle presentation and broad store/mode categories but is not authoritative for version-specific rosters.

## 3. Intentional Desktop Buddy Differences

These are approved modern additions, not claims of reference parity:

- Dragging any buddy part with a fearful resistance tether.
- Cumulative 100-pain/five-second knockout and fixed four-second unconsciousness.
- Explicit body-region payout multipliers.
- Pistol, shotgun, baseball bat, Fire Sprayer, food/drink/Repair Kit launch catalogue.
- Passive desktop-idler income tied to mood.
- Persistent per-tool harmful history with a mood-60 trust reset.
- Autonomous object consumption/tossing and a Windows desktop overlay shell.
- Modern accessibility, Steam Cloud, stats, achievements, responsive ultrawide UI, and tray operation.

## 4. Godot 4.6.1 Technical Findings

Authoritative documentation:

- [Project settings](https://docs.godotengine.org/en/4.6/classes/class_projectsettings.html)
- [DisplayServer](https://docs.godotengine.org/en/4.6/classes/class_displayserver.html)
- [Window](https://docs.godotengine.org/en/4.6/classes/class_window.html)
- [RigidBody2D and physics guidance](https://docs.godotengine.org/en/4.6/tutorials/physics/physics_introduction.html)
- [Physics interpolation](https://docs.godotengine.org/en/4.6/tutorials/physics/interpolation/physics_interpolation_introduction.html)
- [C# platform support](https://docs.godotengine.org/en/4.6/tutorials/scripting/c_sharp/index.html)
- [Windows export](https://docs.godotengine.org/en/4.6/tutorials/export/exporting_for_windows.html)

Key conclusions:

- Stock Godot supports a transparent, borderless, always-on-top Windows window and screen usable-rectangle queries.
- Transparency, focus, and pointer passthrough are separate concerns and require a dedicated Windows adapter plus standalone testing.
- Physics forces/torques belong in the fixed physics path; rigid-body transforms must not be rewritten every render frame.
- Physics interpolation must be reset after any emergency teleport/recovery correction.
- Small fast projectiles require CCD or an equivalent swept query.
- C# Windows export is supported with the matching .NET editor/export templates.

## 5. Why Built-In Pin Motors Are Excluded

Godot's public tracker contains unresolved 2D joint-limit/motor concerns relevant to 4.6.1:

- [PinJoint2D angular limit does nothing](https://github.com/godotengine/godot/issues/91691)
- [Erratic 2D joint limit behavior](https://github.com/godotengine/godot/issues/88481)
- [PinJoint2D motor behavior/units issue](https://github.com/godotengine/godot/issues/87513)
- [Open proposed joint fix](https://github.com/godotengine/godot/pull/104539)

Desktop Buddy therefore uses Godot rigid bodies and collision solving while implementing its own spring/damping, stretch cap, upright drive, and locomotion forces. This both avoids the risky motor path and better matches the reference puppet.

## 6. Steam Sources

- [Steamworks API setup](https://partner.steamgames.com/doc/sdk/api)
- [SteamPipe uploading](https://partner.steamgames.com/doc/sdk/uploading)
- [Steam overlay](https://partner.steamgames.com/doc/features/overlay)

Steam functionality is isolated behind an application interface. The local implementation is always available; proprietary SDK binaries must come from an authorized Steamworks account and must not be sourced from untrusted mirrors.

## 7. Reference-Tuning Workflow

For every accepted physics tuning revision:

1. Capture a short development-only reference clip for one behavior: idle, drag/throw, hard impact, walk, jump, knockout, or recovery.
2. Run the equivalent Desktop Buddy scripted scenario at the fixed 120 Hz tick.
3. Compare response delay, body silhouette, spring stretch, rotation, damping, floor bounce, whole-body propagation, and time to recovery.
4. Change only typed tuning Resources; do not hide tuning constants in controllers.
5. Run the complete headless regression suite and Windows performance check.
6. Record the accepted Resource revision and scenario metrics in the implementation PR/commit notes.

“Feels exact” is accepted through repeatable side-by-side review plus numerical stability gates; it is not used as an untestable substitute for requirements.
