# First Vertical Slice Plan

## Goal

Build the smallest playable Desktop Buddy prototype that proves the chosen Electron, React Three Fiber, and Rapier stack can support the core desktop overlay experience.

## Implemented Scope

- Replace the prior Godot project surface with an Electron/Vite/TypeScript scaffold.
- Use pnpm for dependency management.
- Create a transparent, frameless, resizable, always-on-top Electron window.
- Add a draggable top strip and corner pinning controls.
- Add a click-through work mode with a global `Ctrl+Shift+B` escape shortcut.
- Render a React Three Fiber scene into the transparent window.
- Add Rapier 3D physics.
- Build a placeholder segmented mannequin buddy from primitives.
- Keep buddy data separate from the mannequin implementation so future GLB characters can replace the visual layer.
- Add basic tools: grab, poke, paint, spawn, and reset/clear toys.
- Add initial toys: rubber ball, heavy cube, and spring pad.
- Add idle currency ticks and interaction rewards.
- Add save/settings IPC through Electron preload.
- Add a Steamworks abstraction stub for achievements/stats/status.
- Add optional mature-content setting placeholder.

## Validation Targets

- `pnpm build` must pass.
- `pnpm preview` must launch an Electron window titled `Desktop Buddy`.
- The window must be transparent over the desktop.
- The HUD and mannequin must render.
- Steam calls must route through the stub without crashing.
- Settings and save files must be written through Electron IPC.

## Known Follow-Ups

- Replace primitive mannequin visuals with a GLB-driven buddy while preserving the physics rig.
- Improve joint limits and mass tuning for better ragdoll feel.
- Add proper grab constraints instead of the current impulse-follow prototype.
- Implement real UV painting instead of per-part color toggling.
- Add a tray menu for hide/show and work-mode recovery.
- Replace the Steam stub with a real Steamworks binding once the prototype loop is stable.
- Add automated renderer tests and save-schema tests.

