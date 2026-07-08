# Desktop Buddy Architecture Plan

## Architecture Goals

Desktop Buddy should be implemented as an Electron desktop app with a React Three Fiber renderer and Rapier 3D physics simulation. The architecture must support a physics-first 3D sandbox, desktop-overlay behavior, idle progression, future character expansions, Steamworks features, and optional mature effects.

The first implementation target is Windows.

## Proposed Repository Shape

```text
desktop-buddy/
  package.json
  vite.config.ts
  tsconfig.json
  electron/
    main.ts
    preload.ts
    steam/
      steamClient.ts
    window/
      createGameWindow.ts
      windowState.ts
    persistence/
      saveStore.ts
  src/
    App.tsx
    main.tsx
    game/
      GameRoot.tsx
      loop/
        fixedStep.ts
      scene/
        Scene.tsx
        CameraRig.tsx
        Lighting.tsx
      physics/
        PhysicsWorld.tsx
        collisionGroups.ts
        physicsTuning.ts
      buddy/
        Buddy.tsx
        BuddyRig.tsx
        BuddyDefinition.ts
        BuddyReactions.ts
        buddyTypes.ts
      toys/
        ToyRegistry.ts
        ToySpawner.tsx
        definitions/
      tools/
        ToolRegistry.ts
        GrabTool.ts
        PaintTool.ts
        PokeTool.ts
      paint/
        PaintCanvas.ts
        DecalSystem.tsx
        uvPaint.ts
      economy/
        economyStore.ts
        idleProgress.ts
        unlocks.ts
      steam/
        achievements.ts
        stats.ts
        cloudSave.ts
      settings/
        settingsStore.ts
        matureContent.ts
      save/
        saveSchema.ts
        migrations.ts
      ui/
        Hud.tsx
        ToolBar.tsx
        ShopPanel.tsx
        SettingsPanel.tsx
  assets/
    models/
    textures/
    audio/
  docs/
```

This structure is a target, not a requirement for the first commit. Create it incrementally as real code lands.

## Process Boundaries

### Electron Main Process

Responsibilities:

- Create and manage the transparent game window.
- Persist window position, size, and user settings.
- Own filesystem access for saves.
- Initialize Steamworks.
- Expose safe IPC methods through preload.
- Handle app lifecycle, single-instance lock, tray/menu behavior, and quit/hide controls.

The renderer should not get direct Node.js access.

### Electron Preload

Responsibilities:

- Expose a narrow `window.desktopBuddy` API.
- Provide typed IPC wrappers for save/load, settings, window controls, and Steam calls.
- Avoid broad filesystem or process access.

Example API surface:

```ts
window.desktopBuddy = {
  save: {
    read(): Promise<SaveData | null>,
    write(data: SaveData): Promise<void>
  },
  window: {
    setAlwaysOnTop(enabled: boolean): Promise<void>,
    setClickThrough(enabled: boolean): Promise<void>,
    setIgnoreMouseEvents(enabled: boolean): Promise<void>,
    hide(): Promise<void>
  },
  steam: {
    isAvailable(): Promise<boolean>,
    unlockAchievement(id: string): Promise<void>,
    setStat(id: string, value: number): Promise<void>
  }
};
```

### Renderer Process

Responsibilities:

- Run React UI and Three.js scene.
- Run Rapier physics.
- Handle input, tools, toy spawning, painting, buddy reactions, economy, and UI.
- Request persistence and Steam operations through preload only.

## Window Model

The initial Windows overlay should use:

- `transparent: true`
- `frame: false`
- `alwaysOnTop: true`
- fixed or constrained dimensions for the MVP
- remembered screen position
- optional taskbar visibility setting
- work-mode click-through toggle

Per-pixel click-through should not be required for the first vertical slice. Start with a user-controlled work mode and later evaluate shaped windows or custom hit-test regions if needed.

## Render Model

Use React Three Fiber as the scene composition layer.

Top-level structure:

```tsx
<Canvas>
  <Suspense fallback={null}>
    <GameScene />
  </Suspense>
</Canvas>
```

Inside `GameScene`:

```tsx
<Physics>
  <RoomBounds />
  <Buddy />
  <ToySpawner />
  <ToolInteractionLayer />
</Physics>
```

Use a fixed visual scale early. For example, one Rapier unit can represent one meter, with the buddy around 1 to 1.5 units tall in simulation space. Keep scale consistent because physics feel depends heavily on mass, gravity, damping, collider size, and force magnitude.

## Physics Model

Physics feel is the highest priority.

Use Rapier rigid bodies and simplified colliders. Avoid dynamic triangle mesh colliders. Use cuboids, capsules, balls, convex hulls, or compound colliders for dynamic objects.

Recommended first buddy rig:

- head
- torso
- pelvis
- upper arms
- lower arms
- hands
- upper legs
- lower legs
- feet

Each body part should have:

- a rigid body
- one or more simple colliders
- mass properties tuned for stable motion
- damping and angular damping
- collision filtering where needed

Connect parts with joints:

- neck to torso
- torso to pelvis
- shoulders
- elbows
- hips
- knees
- ankles, if useful

Start with a simple visible segmented mesh before attempting a skinned character mesh. Once physics feels good, bind the final visual character to the physics rig.

## Input And Tools

Tools should be implemented through a registry:

```ts
type ToolDefinition = {
  id: string;
  displayName: string;
  icon: string;
  unlockId?: string;
  createController: () => ToolController;
};
```

Core tools:

- Grab: raycast, attach drag constraint, throw on release.
- Poke: raycast and apply impulse at hit point.
- Paint: raycast UV coordinate and draw to texture or place decal.
- Spawn toy: place selected toy into the scene.
- Cleanup: remove loose toys or reset the buddy.

Input should flow through one interaction manager so tools do not compete for pointer events.

## Toy System

Toys should be data-driven where practical:

```ts
type ToyDefinition = {
  id: string;
  category: "impact" | "projectile" | "utility" | "decoration" | "idle";
  modelPath?: string;
  collider: ColliderDefinition;
  mass: number;
  restitution: number;
  friction: number;
  unlockId?: string;
};
```

First toy candidates:

- rubber ball
- heavy cube
- spring launcher
- fan
- paint can or paint gun
- firework or popper
- magnet

Fast projectiles should use continuous collision detection. Toys that generate repeated forces should have capped strength and clear visual feedback.

## Buddy Character System

The first buddy can be simple, but the architecture should support more characters.

Use `BuddyDefinition` data:

```ts
type BuddyDefinition = {
  id: string;
  displayName: string;
  modelPath: string;
  rigPreset: string;
  materialSlots: string[];
  reactionSet: string;
  matureEffectProfile?: string;
};
```

Keep these separable:

- physics rig
- visual mesh
- reaction logic
- customization slots
- mature effects
- idle behavior

This avoids hard-coding the first buddy into every system.

## Painting And Decals

Support two paint paths:

1. UV painting for freeform brush strokes.
2. Decal placement for splats, stickers, bruises, scorch marks, and other effects.

MVP approach:

- Raycast from pointer into buddy mesh.
- Use hit UV coordinate.
- Draw brush stroke into an offscreen canvas.
- Use the canvas as a dynamic texture on the buddy material.
- Save paint layers as image data plus metadata.

Decals can be added after basic brush painting works.

## Mature Content Toggle

Blood/gore must be isolated behind settings.

Implementation rules:

- Mature effects default to off unless the user enables them.
- Core gameplay should not depend on gore.
- Effects should route through `matureContent.ts`.
- Store the toggle in settings.
- Do not mix mature assets into generic impact effects.

Example:

```ts
if (settings.matureContent.bloodEnabled) {
  matureEffects.spawnBloodSplat(hit);
} else {
  slapstickEffects.spawnStars(hit);
}
```

## Economy And Idle Progress

Use Zustand for runtime state, with serializable save data.

Economy systems:

- passive currency rate
- interaction rewards
- toy unlocks
- buddy customization unlocks
- room/background unlocks
- optional offline progress with a capped duration

Avoid tying rewards only to harming the buddy. Reward playful interaction, decorating, care, and idle time too.

## Save And Steam Cloud

Steam integration is expected from day one.

Save model:

- Renderer owns the current serializable `SaveData`.
- Electron main process writes save data to app `userData`.
- Steam Cloud should sync the save file path configured through Steamworks.
- Include schema version and migrations from the first save format.

Save data should include:

- currency
- unlocks
- settings
- window state
- buddy selected
- buddy customization
- paint layers metadata
- toy stats
- achievement-relevant counters
- last seen timestamp for offline progress

## Steamworks Integration

Create a small Steam abstraction early, even if the first implementation is stubbed for local development.

Responsibilities:

- initialize Steam API
- report achievements
- report stats
- expose availability state
- fail gracefully outside Steam
- support dev/test mode

Do not call Steam APIs directly from random gameplay files. Route through `src/game/steam/*` and Electron IPC.

## Testing Strategy

Early tests should focus on logic and data shape:

- save schema validation
- save migrations
- economy calculations
- offline progress caps
- unlock logic
- settings defaults
- Steam abstraction fallback behavior

Manual test checklist for physics:

- buddy remains stable at rest
- grab and throw feel responsive
- joints do not explode under normal force
- toys collide reliably
- projectiles do not tunnel
- reset recovers from bad states
- window stays usable while other apps are focused

## Performance Targets

Windows-first baseline:

- smooth 60 FPS when active
- low CPU/GPU usage while idle
- reduced simulation/render rate in work mode if possible
- cap loose spawned objects
- pool short-lived effects
- use simple colliders for dynamic objects
- use instancing for repeated toys or particles where practical

## First Implementation Sequence

1. Replace or supplement the current workspace with the Electron/Vite/TypeScript scaffold.
2. Add a transparent frameless Electron window.
3. Render a basic React Three Fiber scene.
4. Add Rapier and debug physics.
5. Build a placeholder segmented buddy rig.
6. Implement grab, drag, throw, and reset.
7. Add a small toy registry and spawn two toys.
8. Add save/load through Electron IPC.
9. Add Steamworks abstraction and local fallback.
10. Add paint raycast prototype.
11. Add idle currency and unlock data.
12. Package a Windows build and test launch behavior.

## Known Risks

- Physics feel may require substantial tuning.
- Transparent WebGL windows can have platform-specific rendering quirks.
- Electron click-through behavior may need custom UX compromises.
- Steamworks bindings can be sensitive to Electron, Node, and native module versions.
- UV painting depends on asset UV quality.
- Mature content settings must be handled carefully for store presentation and user expectations.

## Guidance For Future LLM Agents

- Read `PROJECT_PLAN.md` before changing architecture.
- Keep the implementation Windows-first unless the user expands platform targets.
- Do not migrate to Godot, Unity, or another engine without explicit user approval.
- Do not make the buddy a single hard-coded character class.
- Do not couple Steam calls directly to gameplay components.
- Do not build economy systems before the physics prototype feels good.
- Avoid adding complex content pipelines before a basic GLB buddy and toy loop works.
- Keep gore/blood optional and isolated.

