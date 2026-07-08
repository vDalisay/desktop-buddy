# Desktop Buddy Project Plan

## Project Summary

Desktop Buddy is a Windows-first 3D desktop idler and physics sandbox inspired by the playful interaction loop of old Flash desktop toy games. The player keeps a small transparent game window in a corner of the screen while doing normal PC work. The core experience is a hybrid of ragdoll sandbox, desktop pet, idle progression, and toybox experimentation.

The first release should focus on one simple buddy character, but the systems must support future character expansions. The tone should mix cartoony slapstick with a darker sandbox toybox edge. Gore or blood effects must be optional and controlled by a user-facing toggle.

## Core Player Experience

- A small always-on-top desktop window contains a 3D buddy and interactive toys.
- The buddy should feel physically responsive, funny, and expressive.
- The player can poke, grab, throw, paint, damage, reward, and decorate the buddy.
- The player earns idle currency over time and from interactions.
- Currency unlocks toys, skins, rooms, buddy variants, idle generators, effects, and quality-of-life upgrades.
- The game should stay usable while the player works in other apps.
- Steam achievements and cloud-save support are expected from day one.

## Primary Technical Direction

The project should use a web-native 3D stack packaged as a desktop app:

- Desktop shell: Electron
- Build tooling: Vite
- Language: TypeScript
- UI/render app: React
- 3D renderer: Three.js through `@react-three/fiber`
- Three.js helpers: `@react-three/drei`
- Physics: Rapier 3D through `@react-three/rapier`
- State management: Zustand
- Local persistence: Electron main process using app `userData`
- Steam integration: Steamworks from day one, likely via `steamworks.js` or another maintained Node/Electron-compatible binding
- Asset format: glTF/GLB
- Target platform: Windows first

## Why This Stack

Electron is the right desktop shell for this project because it supports transparent, frameless, always-on-top windows and click-through style behavior while preserving a strong Chromium/WebGL development environment. It also gives future LLM agents a familiar JavaScript/TypeScript ecosystem with excellent debugging and examples.

React Three Fiber is preferred over raw Three.js for project maintainability. It allows the scene to be organized into explicit components such as `Buddy`, `Toy`, `Tool`, `Room`, `PaintLayer`, and `PhysicsRig`. This structure is easier for LLM agents to inspect, modify, and extend without turning the scene into a large imperative script.

Rapier 3D is the preferred physics engine because the feel of the physics is the highest-risk and highest-value part of the game. Rapier provides rigid bodies, colliders, joints, collision events, sensors, scene queries, snapshots, and WASM performance.

## Product Constraints

- Windows is the first supported platform.
- The game must be acceptable for Steam distribution.
- Steamworks integration should be part of the initial architecture, not bolted on later.
- The initial buddy should be simple, but the data model and content pipeline must allow additional buddy characters.
- Gore/blood effects must be optional and default-safe.
- The game should avoid direct copying of Interactive Buddy art, name, characters, sounds, UI, and exact presentation.
- The project can borrow broad mechanics and genre ideas, but must develop original expression.

## Design Pillars

1. Physics First

   The buddy and toys must feel good before the economy or content volume grows. Prototype physics rigs, joints, drag behavior, impact response, and toy interactions early.

2. Desktop Friendly

   The game must not obstruct normal work. It should support small-window play, always-on-top mode, work mode, click-through behavior, mute/low-audio settings, and quick hide/show controls.

3. Expressive Buddy

   The buddy should read as a character, not only a crash-test dummy. Add mood, reactions, idle animations, facial states, and simple memory over time.

4. Expandable Toybox

   Toys, tools, effects, paints, and buddy variants should be data-driven enough to add content without rewriting core systems.

5. Idle Progression Without Pressure

   The idle economy should reward checking in, experimenting, and decorating. Avoid aggressive timers, microtransaction-style pacing, or mechanics that punish the player for focusing on work.

## MVP Scope

The first playable vertical slice should include:

- Transparent Electron window.
- Three.js scene rendered through React Three Fiber.
- One simple 3D buddy.
- Physics-driven buddy rig using Rapier rigid bodies and joints.
- Grab, drag, throw, and poke interactions.
- At least five basic toys or tools.
- Paint-on-buddy brush using raycast UV painting or decals.
- Idle currency generation.
- Save/load.
- Steam initialization path, achievement test, and cloud-save-ready save location.
- Settings for always-on-top, click-through/work mode, audio, blood/gore toggle, and window position.

## Suggested Milestones

1. Desktop Window Prototype

   Transparent frameless Electron window, always-on-top controls, resize/position persistence, and a simple rendered 3D scene.

2. Physics Feel Prototype

   Buddy placeholder rig, drag interaction, throwing, collisions, impulses, damping, and toy impacts. This milestone decides whether the stack feels viable.

3. Toybox MVP

   Tool registry, toy spawning, currency rewards, object cleanup, and basic shop unlocks.

4. Character Layer

   Buddy reactions, mood, facial states, idle behaviors, audio cues, and optional gore/blood effects behind a setting.

5. Paint And Customization

   Brush tool, decals or UV texture painting, color picker, clear/undo where feasible, and saved customization.

6. Steam-Ready Vertical Slice

   Steamworks init, achievements, cloud-save path, packaging, installer/build scripts, crash logging, and Steam test branch workflow.

## Notes For Future LLM Agents

- Treat this document as the source of truth for product direction unless the user changes it.
- The workspace may contain older Godot project files. Do not assume Godot is the target stack unless the user explicitly redirects the project back to Godot.
- Favor small, testable vertical slices over large speculative systems.
- Preserve the Windows-first desktop overlay requirements when making architecture choices.
- Prioritize physics feel and input correctness over visual polish in early work.
- Keep character and toy systems extensible for future buddy variants.
- Keep mature content optional and isolated behind settings.

