# Desktop Buddy — Godot C# Architecture

Status: Decision-complete handoff architecture, subject only to empirically tuned values identified by the approved physics/economy laboratories.

## 1. Architectural Drivers

- Godot 4.6.1 .NET/C#, Windows 10/11 x86_64, Steam release.
- A transparent desktop shell must never own or leak gameplay rules.
- Godot `RigidBody2D` owns collision and integration; custom six-body spring/drive components own puppet actuation.
- Physics feel is gated before economy/content expansion.
- Scene composition and focused services replace deep inheritance, service locators, global mutable state, and root-script God objects.
- Typed Godot Resources own static definitions/tuning; versioned JSON owns user state.
- Local play, saving, and achievements/stat accrual continue when Steam is unavailable.

## 2. Runtime Layers

### Platform Layer

Owns Windows window flags/hit testing, tray, global hotkey, launch-at-login, monitor/DPI queries, filesystem implementation, Steam connection, and lifecycle notifications. It exposes interfaces and never references buddy/tool components.

### Application Layer

Owns startup/shutdown, service construction, semantic commands, save coordination, clock mode, platform selection, and routing between gameplay/UI/platform boundaries. It does not calculate physics, pain, mood, prices, or AI decisions.

### Gameplay Layer

Owns the sandbox, buddy composition, active puppet, behavior arbitration, tools/objects, collision attribution, pain/knockout, mood/memory, statuses, rewards, inventory, and statistics. Gameplay references platform abstractions only through application commands/events.

### Presentation Layer

Owns original vector drawing, effects, audio playback, HUD, retractable panels, trajectory preview, responsive layout, and accessibility transforms. It consumes read-only view state and emits application commands; it does not mutate domain fields directly.

## 3. Scene Composition

Proposed scene ownership:

```text
AppRoot (Node; composition only)
├── ApplicationServices (Node)
│   ├── GameClock
│   ├── ProgressCoordinator
│   ├── PlatformCoordinator
│   └── LifecycleCoordinator
├── SandboxRoot (Node2D)
│   ├── BoundaryController
│   ├── BuddyRoot (Node2D; composition only)
│   │   ├── PuppetRig
│   │   │   ├── Torso (RigidBody2D)
│   │   │   ├── Head (RigidBody2D)
│   │   │   ├── LeftArm (RigidBody2D)
│   │   │   ├── RightArm (RigidBody2D)
│   │   │   ├── LeftLeg (RigidBody2D)
│   │   │   └── RightLeg (RigidBody2D)
│   │   ├── PuppetConstraintComponent
│   │   ├── ActiveDriveComponent
│   │   ├── BehaviorArbiter
│   │   ├── ObjectInteractionComponent
│   │   ├── GrabResistanceComponent
│   │   ├── PainKnockoutComponent
│   │   ├── MoodMemoryComponent
│   │   ├── StatusEffectComponent
│   │   └── BuddyPresentation
│   ├── GrabTetherController
│   ├── ToolWorld
│   ├── LooseObjectRegistry
│   └── EffectsWorld
└── OverlayUi (CanvasLayer)
    ├── MoneyHud
    ├── RetractablePanel
    ├── TrajectoryPreview
    └── SettingsUi
```

Names may change, but ownership may not collapse into `AppRoot`, `BuddyRoot`, or a universal gameplay manager. Node references are typed `[Export]` fields assigned by scenes/factories and validated in `_Ready`; components must not search arbitrary parents or depend on sibling order.

## 4. Buddy Component Contracts

| Component | Owns | Must not own |
| --- | --- | --- |
| `PuppetRig` | Six typed bodies, part IDs, collision setup, measurements, tuning reference | Behavior selection, mood, money |
| `PuppetConstraintComponent` | Equal/opposite spring/damping, stretch correction, strain telemetry in fixed integration | AI goals, transform teleporting |
| `ActiveDriveComponent` | Bounded upright/balance/walk/jump/recovery/object-action forces from an intent | Choosing intent, saving state |
| `BehaviorArbiter` | Priority resolution and immutable actuation/object intents | Applying forces or changing money |
| `ObjectInteractionComponent` | Candidate sensing and catch/hold/inspect/consume/discard/toss action lifecycle | General locomotion or store ownership |
| `GrabTetherController` | Acquisition, elastic cursor force, strain, release/cancel, velocity cap | Fear decision or damage calculation |
| `PainKnockoutComponent` | Pain events/window, knockout timer and consciousness events | Physics contact discovery or payout |
| `MoodMemoryComponent` | Persistent mood, bands, transient emotions, harmful records, crossing reset | Physics, inventory, audio playback |
| `StatusEffectComponent` | Burning/status timers and semantic tick events | Direct UI/VFX or persistence writes |
| `ImpactRouter` | Contact sampling, attribution, episode/debounce state, accepted impact events | Tool prices, mood bands |
| `RewardLedger` | Formula, currency mutation, income windows, reward/stat events | Measuring collision impulse |

The buddy root connects component events and passes commands. It contains no per-frame game logic beyond routing the fixed-tick call order.

## 5. Core Types and Interfaces

Stable IDs must not depend on scene node names.

```csharp
public enum BuddyPartId { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg }
public enum PayoutRegion { Head, Torso, Limb }
public enum InputMode { Work, Play }
public enum MoodBand { Fearful, Wary, Neutral, Content, Delighted }
public enum Consciousness { Conscious, Unconscious }
public enum ToolUseMode { DirectStroke, GrabTether, SwingBody, CursorWeapon, PullbackSpawn }

public readonly record struct ImpactSample(
    ulong InteractionId,
    StringName ToolId,
    BuddyPartId Part,
    Vector2 Point,
    Vector2 Normal,
    float NormalImpulse,
    float RelativeNormalSpeed,
    double PhysicsTime,
    bool IsBuddyGrabbed,
    bool IsBuddyUnconscious);

public readonly record struct AcceptedPainEvent(
    ulong InteractionId,
    StringName ToolId,
    BuddyPartId Part,
    double Time,
    double Pain,
    bool IsUnconscious);

public readonly record struct RewardEvent(
    StringName SourceId,
    BuddyPartId Part,
    long AmountMinorUnits,
    double Pain,
    double Time);
```

Required platform/application seams:

```csharp
public interface IDesktopWindowService
{
    InputMode InputMode { get; }
    Rect2I UsableMonitorRect { get; }
    void ApplyWindowSettings(WindowSettings settings);
    void SetInputMode(InputMode mode, IReadOnlyList<Rect2I> workModeHitRegions);
    void RestoreBottomRight(int marginPixels);
    event Action<Rect2I> ClientBoundsChanged;
    event Action WindowFocusLost;
}

public interface IProgressStore
{
    Task<LoadResult<ProgressSave>> LoadProgressAsync(CancellationToken token);
    Task<LoadResult<LocalSettingsSave>> LoadSettingsAsync(CancellationToken token);
    Task SaveProgressAsync(ProgressSave data, CancellationToken token);
    Task SaveSettingsAsync(LocalSettingsSave data, CancellationToken token);
}

public interface IPlatformService
{
    bool IsAvailable { get; }
    Task InitializeAsync(CancellationToken token);
    void SetStat(StringName id, long value);
    void UnlockAchievement(StringName id);
    void PumpCallbacks();
    Task FlushAsync(CancellationToken token);
}
```

Tool behaviors implement a focused lifecycle rather than a giant tool switch:

```csharp
public interface IToolBehavior
{
    StringName ToolId { get; }
    void Select(ToolContext context);
    void HandleInput(in ToolInputFrame input);
    void PhysicsTick(double delta);
    void Cancel();
    void Deselect();
}
```

Concrete behavior families are shared only where control semantics genuinely match: direct stroke, elastic grab, physical swing body, cursor weapon, and pullback spawner. Tool-specific rules live in a focused behavior plus its typed definition.

## 6. Typed Resource Model

Static data is represented by C# `Resource` subclasses and `.tres` assets:

- `BuddyDefinition`: stable buddy ID, six part visual/physics definitions, connector visuals, default face palette, and rig/drive profiles. It is the future custom-buddy seam but only the built-in asset is loaded now.
- `PuppetRigProfile`: part masses/radii/materials, spring link graph, anchors/rest offsets, damping/stretch bounds, collision configuration.
- `ActiveDriveProfile`: drive gains/limits for normal, fearful resistance, unconscious, and recovery modes.
- `PainProfile`: accepted source categories and empirical impulse/effect-to-pain curves.
- `MoodEconomyProfile`: bands, drift, care values, passive curve, cash-per-pain, and calibrated price table.
- `ToolDefinition`: stable ID, display metadata, unlock price/order, use mode, PackedScene references, cooldown/ammo/fuse/status data.
- `StatusDefinition`: duration/refresh cap and semantic tick policy.
- `AchievementDefinition`: stable Steam API ID and local trigger ID.

Resources are immutable at runtime. Mutable counters/timers belong to component state or saves. Startup validation rejects duplicate IDs, missing PackedScenes, invalid cooldowns, incomplete six-part definitions, and catalog references to unknown assets.

## 7. Fixed-Tick Data Flow

At each 120 Hz physics step:

1. Input collector produces one immutable `ToolInputFrame` in sandbox coordinates.
2. Tool behavior updates cursor actor/tether/spawn state and applies bounded physical commands.
3. Behavior arbiter reads a snapshot of consciousness, recovery, hazards, grab state, mood/memory, object candidates, and support state; it emits immutable intents.
4. Active drive consumes intents and applies bounded forces/torques.
5. Puppet constraint integration applies equal/opposite structural forces and records telemetry.
6. Godot integrates bodies and resolves contacts.
7. Impact router accepts/deduplicates contact samples and emits pain events.
8. Pain/status/mood components update semantic state; the ledger applies reward/stat events.
9. Application marks progress dirty and publishes a read-only view snapshot to UI/audio/Steam adapters.

Godot signals or C# events carry local semantic events. Cross-system mutation occurs through explicit application commands; do not add an untyped global event bus.

`_Process` may update interpolation-aware drawing, UI, cursor presentation, and audio only. It may not apply authoritative forces, damage, mood, purchases, or timers.

## 8. Time and Lifecycle

- Physics, contact debounce, knockout, statuses, weapon cadence, and care cooldowns use a monotonic simulation clock.
- Visible simulation is fixed at 120 Hz with physics interpolation.
- Hidden-to-tray mode stops SceneTree physics/render processing and uses a low-frequency monotonic application clock for mood drift and passive income only.
- On close, suspend, or a discontinuity beyond the lifecycle threshold, elapsed time is discarded. Resume clears the physics accumulator and resumes from the frozen visible state or safe session state.
- Wall-clock timestamps may be logged for diagnostics but never calculate gameplay income.

## 9. Windows Desktop Adapter

`DesktopWindowController` depends on `IWindowsDesktopAdapter`; editor/headless runs use an emulated adapter.

Use Godot APIs first for transparent background, borderless, topmost, size, position, and usable monitor rectangles. Check transparency availability and fall back to an opaque bordered box without changing gameplay.

Work/Play hit testing requires native Windows behavior because Godot's passthrough polygon can also constrain drawing on Windows. The Windows adapter must:

- obtain the real native window handle;
- subclass/restore the window procedure safely and keep the delegate alive;
- return normal client hit testing for the buddy, menu/HUD controls, and border/resize handles in Work Mode;
- return `HTTRANSPARENT` for other transparent client points in Work Mode;
- return normal client hit testing for the complete box in Play Mode;
- convert screen/client coordinates using the current monitor DPI;
- restore the original procedure during shutdown and handle window recreation;
- never use `SetWindowRgn` to implement passthrough because it clips presentation as well as input.

Interaction with an accepted Work Mode region requests focus and Play Mode while preserving the selected tool. Focus loss/outside click while Play Mode requests Work Mode. Do not install a global mouse hook merely to detect outside clicks.

The same adapter owns global hotkey registration/conflict reporting, tray actions, launch-at-login, and recovery when focus is unavailable. The default mode hotkey is `Ctrl+Shift+B`. Failure to register a user-selected hotkey must retain the prior working binding and present an error in settings; tray recovery remains available.

Window resize events enqueue boundary changes for the next physics boundary. The boundary controller rebuilds walls/floor, corrects newly outside objects without explosive impulses, and calls `ResetPhysicsInterpolation()` after any forced correction.

## 10. Input and Coordinate Mapping

`InputCollector` is the only component reading Godot input. It maps OS/client coordinates through DPI, viewport, responsive layout, and world zoom into sandbox coordinates, then emits immutable frames.

- Mouse movement stores a normalized non-trivial direction for cursor weapons.
- Mouse wheel applies a temporary angular offset; the next non-trivial movement clears it.
- Pullback start captures a spawn point and drag origin; preview and launch use the same ballistic configuration.
- Pistol and Shotgun consume only a newly pressed primary action, never held-repeat. `R` requests manual reload and an attempted empty shot requests automatic reload.
- UI consumes input before gameplay.
- Right mouse sends cancel/drop without changing selection.
- Work/Play transitions never synthesize primary input.

The trajectory preview uses the same gravity/launch parameters as the spawned body and may query the physics world for collision hints. It is visual only and cannot move the future object.

## 11. Pain, Economy, and Personality Boundaries

`ImpactRouter` is the only contact-to-pain entry point. Calibrated room-boundary, loose-object, projectile, and physical-weapon impacts may enter it, retaining originating tool/throw attribution where available. Status effects submit attributed semantic pain ticks through the same pipeline. `PainKnockoutComponent` owns the rolling five-second window and four-second timer; it clears the window on knockout and does not carry unconscious hits into the next conscious window. `RewardLedger` consumes accepted pain and applies only the documented region/consciousness/cash-per-pain formula.

`MoodMemoryComponent` is the sole writer of persistent mood and harmful memory. It exposes commands such as `ApplyCare`, `ApplyHarm`, and `RecordHazard`, then emits snapshots/band changes/trust reset. Accepted harm applies `min(10, pain x 0.1)` mood loss, including Burning pain ticks, with no separate knockout penalty. UI cannot set mood.

Care-item behavior emits its successful-use event only after consumption/application. Cooldowns begin from that event, so cancel, miss, drop, or rejected spawn cannot consume the cooldown.

`EconomyService` owns currency and permanent ownership. Currency is a signed 64-bit count of milli-credits (`1000` minor units per displayed credit); no floating-point value enters persistence or purchase arithmetic. `ShopPresenter` requests `Purchase(toolId)` and renders the returned result; it never edits balances or unlock sets. Prices and cash-per-pain are calibrated together against the target-time benchmark.

## 12. Save Architecture

Use two versioned files under `user://`:

### `progress.json` — Steam Cloud eligible

- schema version and monotonic save revision;
- currency minor units and permanent unlock IDs;
- selected tool ID;
- persistent mood and harmful-history/per-tool fear records;
- tracked statistics, achievement state, and queued platform operations;
- cumulative running/active/hidden time.

### `settings.json` — local machine only

- window position/size/monitor/DPI context;
- zoom, audio, Work Mode mute, motion/particles/photosensitivity, AA, V-sync, topmost;
- global hotkey binding and launch-at-login preference;
- last input/window mode where safe to restore.

The save coordinator is the single writer. It snapshots state on the main thread, serializes off-thread without Godot objects, writes a temp file, flushes, rotates one backup, and atomically replaces the primary. It serializes concurrent requests and coalesces the 30-second dirty autosave. Purchases, unlocks, focus loss, and clean exit request an immediate flush.

Load order is primary, backup, defaults. A malformed file is renamed with a `.corrupt-<timestamp>` suffix before fallback. Stable catalog IDs are validated; unknown future IDs are preserved in an extension bucket where safe but are not activated. Migrations are sequential, tested functions from version N to N+1.

Never serialize nodes, Resources, RID/instance IDs, transforms, velocities, loose actors, projectiles, pain events, knockout, or temporary statuses. Load always constructs a safe standing buddy.

## 13. Steam Adapter

The main assembly references only `IPlatformService`. `LocalPlatformService` is always available. Steamworks-specific types live in an optional adapter assembly/module built from authorized dependencies; a factory loads it when present and otherwise returns local mode. Development and CI must build/test without proprietary native binaries.

Achievements and stats are emitted as idempotent semantic operations with stable IDs. The local queue records pending operations before attempted submission, deduplicates achievement IDs, keeps maximum/total semantics per stat definition, and removes an operation only after confirmed flush. Steam callbacks are pumped by the platform coordinator on the main thread.

Steam Cloud synchronizes `progress.json` only. `settings.json`, backups, quarantined files, logs, and `steam_appid.txt` are excluded. Steam initialization, overlay, stat, or Cloud failure is non-fatal and visible in diagnostics.

## 14. Presentation and Audio

Buddy visuals attach directly to each physical body; do not depend on the experimental `SkeletonModification2DPhysicalBones`. Limb connector drawing reads body positions but never drives them. The face presenter resolves consciousness/acute reaction above persistent mood and draws the resulting emoticon on the head.

HUD/panels use Godot Control containers, minimum sizes, anchors, and theme scaling. Responsive layouts are verified at the documented aspect ratios and zooms. Presentation settings alter rendering only and must not affect physics results.

The HUD and shop render whole-credit balances/prices. A reward presenter groups damage rewards over `0.25` seconds and renders brief `+$N.N` feedback without exposing pain. Default presentation settings are V-sync On, `2x` MSAA, Master/SFX `50%`, Work Mode mute On, Screen Shake On, Reduced Motion/Particles Off, and Photosensitivity-Safe Effects On. AA choices are Off/`2x`/`4x`/`8x`; V-sync choices are On/Off. Camera shake moves only game content, never the native window.

Audio consumes semantic events through an `AudioPresenter`, applies master/SFX/Work-Mode mute policy, and never participates in gameplay timing.

## 15. Object and Projectile Lifecycle

`LooseObjectRegistry` assigns a monotonic spawn sequence and tracks held, hazardous, protected, and safe-to-evict flags. Before a spawn that would exceed 24, it evicts the oldest safe/unheld/unprotected object. If none exists, the spawn request fails cleanly and does not consume a purchase, cooldown, fuse, or ammunition action.

Projectiles use pools separate from the loose-object budget, CCD, maximum lifetime/distance, and one authoritative interaction ID per shot/pellet. Grenades and launched care/toy objects participate in the loose-object registry. VFX particles never register as gameplay bodies.

## 16. Failure Handling and Diagnostics

- Missing required scene reference or invalid Resource: fail fast in development; disable the affected content with a clear logged error in release.
- NaN/infinite/out-of-bounds buddy state: record telemetry; release grabs/held actors; clear unstable velocities, pain, knockout, Burning, and temporary statuses; preserve persistent progress; perform the centralized safe recovery; and reset interpolation.
- Save failure: retain dirty state, keep running, surface non-blocking status, and retry at the next flush.
- Window/native adapter failure: restore the prior window procedure/state and fall back to an opaque/full-capture window with tray recovery.
- Hotkey collision: preserve the previous valid binding and report the conflict.
- Steam failure: queue operations and continue local mode.
- Pool/object exhaustion: reject the new action cleanly; never delete protected gameplay state.

Development telemetry must expose spring strain/force, body speed/rotation, support state, drive intent/force, tether strain, contact episode IDs, accepted pain, pain-window sum, mood/band, behavior priority, object count, and physics step time. Debug telemetry is excluded from release UI.

## 17. Testing Boundaries

- Pure C# tests: formulas, timers, state machines, economy, saves/migrations, stats/achievements, queue idempotency.
- Headless Godot scenarios: scene validation, spring/drive behavior, tools, contacts, resize/zoom, recovery, soak, tolerance envelopes.
- Standalone Windows: native handle/hit test, transparency, focus, tray, global hotkey, DPI/multi-monitor, Steam overlay.
- Performance: allocations, 120 Hz budget, projectile/object pools, visible/hidden targets.

The authoritative scenarios and gates are defined in `TEST_PLAN.md`. Bit-exact replay is neither required nor used as a substitute for behavior envelopes.

## 18. Future Workshop Seam

`BuddyDefinition` deliberately isolates built-in buddy identity, visuals, and approved rig profiles from runtime controllers. Do not load arbitrary external Resources, scripts, DLLs, or PackedScenes in the current scope. Future Workshop work requires a separately versioned package schema, validation/sandbox policy, compatibility contract, moderation/content policy, and migration plan.

No current system may assume multiple buddies, but it may assume exactly one active `BuddyDefinition` and one active buddy instance for launch.

## 19. Suggested Repository Layout

```text
res://
├── scenes/          # bootstrap, sandbox, buddy, tools, UI, test scenes
├── src/
│   ├── App/         # composition roots, commands, lifecycle
│   ├── Buddy/       # rig, drive, behavior, mood, pain, status
│   ├── Tools/       # behavior families, actors, projectiles
│   ├── Economy/     # ledger, inventory, shop, stats
│   ├── Platform/    # interfaces, local, Windows, optional Steam adapter
│   ├── Persistence/ # DTOs, migrations, stores, coordinator
│   └── UI/          # presenters and Control scripts
├── data/            # typed .tres definitions and themes
└── tests/           # pure test project plus Godot scenario scenes/scripts
```

Create directories only when their milestone introduces code; do not scaffold empty abstractions for deferred features.
