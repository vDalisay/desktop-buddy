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
│   │   ├── BehaviorActivityComponent
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
| `PuppetConstraintComponent` | Equal/opposite spring/damping, stretch correction, strain telemetry in fixed integration; typed passive topology retention while an unsupported grab translates the rig | AI goals, active upright/locomotion drive, transform teleporting |
| `ActiveDriveComponent` | Bounded upright/balance/walk/jump/recovery/object-action forces from an intent | Choosing intent, saving state |
| `BehaviorActivityComponent` | Fixed-tick duration and gameplay intent for behavior-backed activities (Eat now) | Visual clips, applying forces |
| `BehaviorArbiter` | Priority resolution and immutable actuation/object intents | Applying forces or changing money |
| `ObjectInteractionComponent` | Candidate sensing and catch/hold/inspect/consume/discard/toss action lifecycle | General locomotion or store ownership |
| `GrabTetherController` | Acquisition, elastic cursor force, strain, release/cancel, velocity cap; both grab variants, Normal and Power | Fear decision or damage calculation |
| `PainKnockoutComponent` | Pain events/window, knockout timer and consciousness events | Physics contact discovery or payout |
| `MoodMemoryComponent` | Persistent mood, bands, transient emotions, harmful records, crossing reset | Physics, inventory, audio playback |
| `StatusEffectComponent` | Burning/status timers and semantic tick events | Direct UI/VFX or persistence writes |
| `CursorGunComponent` | Feeding the pure aim/cadence models, launching and recycling pooled projectiles, per-gun magazine state | Aim or cadence rules, pain, payout, projectile registration in the loose-object budget |
| `ImpactRouter` | Contact sampling, attribution, episode/debounce state, accepted impact events | Tool prices, mood bands |
| `RewardLedger` | Formula, currency mutation, income windows, reward/stat events | Measuring collision impulse |

The buddy root connects component events and passes commands. It contains no per-frame game logic beyond routing the fixed-tick call order.

**Power Grab is the same controller, not a second one.** `GrabTetherController.TryGrab` takes an optional `PowerGrabProfile`: passing one grabs with the stronger authored numbers, passing nothing grabs normally. There is deliberately **no** resolver, strategy, or variant type — the selected tool decides which profile the caller passes, every composition root wires the same `res://data/buddy/power_grab_profile.tres`, and the boot smoke gate asserts they still do.

**Reset Progress** (`ProgressReset`) rewrites the one `BuddyProgressState` in place through `BuddyProgressState.Adopt`, using the same first-run factory a brand-new player gets, and writes it through the normal `SaveCoordinator`. Because the instance identity never changes, nothing composed at startup is re-bound and no service can be left holding a pre-reset state; a failed write is rolled back to the exact prior snapshot. Local settings are a separate payload and are never written by this path.

## 5. Core Types and Interfaces

Stable IDs must not depend on scene node names. Stable content IDs (tools, statuses, achievements, attribution sources) cross every domain seam as plain `string` values. `StringName`, `Rid`, `GodotObject`, and other native-backed Godot types are banned from domain records and domain-facing interfaces: they require a running engine, so they would crash the pure-test assembly (Section 22). Managed Godot structs such as `Vector2` are acceptable in domain payloads.

```csharp
public enum BuddyPartId { Head, Torso, LeftArm, RightArm, LeftLeg, RightLeg }
public enum PayoutRegion { Head, Torso, Limb }
public enum InputMode { Work, Play }
public enum MoodBand { Fearful, Wary, Neutral, Content, Delighted }
public enum Consciousness { Conscious, Unconscious }
public enum ToolUseMode { DirectStroke, GrabTether, SwingBody, CursorWeapon, PullbackSpawn }

public readonly record struct ImpactSample(
    ulong InteractionId,
    string ToolId,
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
    string ToolId,
    BuddyPartId Part,
    double Time,
    double Pain,
    bool IsUnconscious);

public readonly record struct RewardEvent(
    string SourceId,
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
    void SetStat(string id, long value);
    void UnlockAchievement(string id);
    void PumpCallbacks();
    Task FlushAsync(CancellationToken token);
}
```

Tool behaviors implement a focused lifecycle rather than a giant tool switch:

```csharp
public interface IToolBehavior
{
    string ToolId { get; }
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
- `ToolDefinition`: stable ID, entry kind, display metadata as translation keys plus icon references, unlock price (authored in whole credits) and progression order, shop/tool-grid visibility, PackedScene references, cooldown/ammo/fuse/status data.
- `CatalogueDefinition`: the one explicitly referenced list of `ToolDefinition` entries (`res://data/catalogue/launch_catalogue.tres`). `CatalogueLoader` turns it into the immutable engine-free `ToolCatalogue` snapshot the domain rules (`CataloguePolicy`) and `EconomyService.Purchase(contentId)` read; the catalogue, never the caller, resolves purchasability and price. An entry whose slice is unfinished stays `Visible = false` and cannot be shown or bought.
- `GunProfile`: one cursor gun's magazine, shot interval, reload/pump duration, projectiles per shot and seeded spread band, aim feel (smoothing half-life, steering-speed gate, maximum turn per tick, wheel offset), distance-scaled contact shove, casing ejection, and projectile physics/pool tuning. Cadence is authored in routed ticks. `CursorGunComponent` holds an authored array of them and activates whichever matches the selected tool, so a second gun is a `.tres` plus a content ID rather than new input code — the same shape the cursor-tethered tools and the pullback launcher take.
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
- Pistol and Shotgun consume only a newly pressed primary action, never held-repeat. The Shotgun's post-shot press cycles its pump and cannot fire. `R` requests manual reload and an attempted empty shot requests automatic reload.
- UI consumes input before gameplay.
- Right mouse sends cancel/drop without changing selection.
- Work/Play transitions never synthesize primary input.

The trajectory preview uses the same gravity/launch parameters as the spawned body and may query the physics world for collision hints. It is visual only and cannot move the future object.

## 11. Pain, Economy, and Personality Boundaries

`ImpactRouter` is the only contact-to-pain entry point. Calibrated room-boundary, loose-object, projectile, and physical-weapon impacts may enter it, retaining originating tool/throw attribution where available. Status effects submit attributed semantic pain ticks through the same pipeline. `PainKnockoutComponent` owns the rolling five-second window and four-second timer; it clears the window on knockout and does not carry unconscious hits into the next conscious window. `RewardLedger` consumes accepted pain and applies only the documented region/consciousness/cash-per-pain formula.

`MoodMemoryComponent` is the sole writer of persistent mood and harmful memory. It exposes commands such as `ApplyCare`, `ApplyHarm`, and `RecordHazard`, then emits snapshots/band changes/trust reset. Accepted harm applies `min(10, pain x 0.1)` mood loss, including Burning pain ticks, with no separate knockout penalty. `ImpactRouter` attaches an immutable mood-response instruction only after an impact has produced positive pain: ordinary harm, Nerf enjoyment, or transient Nerf annoyance. The focused `NerfMoodToleranceModel` owns only the routed-clock hit count and ten-second reset; it is cleared with other transient interaction state and never enters persistence. Physical pain, knockout, payout, and statistics are independent of that mood instruction. Presentation consumes the same accepted-impact event, using delight for enjoyed Nerf hits and a pain-then-sad sequence for real-Pistol hits. UI cannot set mood.

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

The save coordinator is the single writer. It snapshots state on the main thread, serializes off-thread without Godot objects, writes a temp file, flushes, rotates one backup, and atomically replaces the primary. On Windows this means .NET file APIs: a durable flush (`FileStream.Flush(true)`) on the temp file before `File.Replace` performs the atomic swap and backup rotation in one call. Godot `FileAccess` does not guarantee durable flush semantics and is not used for save writes. It serializes concurrent requests and coalesces the 30-second dirty autosave. Purchases, unlocks, focus loss, and clean exit request an immediate flush.

Load order is primary, backup, defaults. A malformed file is renamed with a `.corrupt-<timestamp>` suffix before fallback. Stable catalog IDs are validated; unknown future IDs are preserved in an extension bucket where safe but are not activated. Migrations are sequential, tested functions from version N to N+1.

Never serialize nodes, Resources, RID/instance IDs, transforms, velocities, loose actors, projectiles, pain events, knockout, or temporary statuses. Load always constructs a safe standing buddy.

## 13. Steam Adapter

The main assembly references only `IPlatformService`. `LocalPlatformService` is always available. Steamworks-specific types live in an optional adapter assembly built on Steamworks.NET, the authorized binding; a factory loads it when present and otherwise returns local mode. Development and CI must build/test without proprietary native binaries.

Achievements and stats are emitted as idempotent semantic operations with stable IDs. The local queue records pending operations before attempted submission, deduplicates achievement IDs, keeps maximum/total semantics per stat definition, and removes an operation only after confirmed flush. Steam callbacks are pumped by the platform coordinator on the main thread.

Steam Cloud synchronizes `progress.json` only. `settings.json`, backups, quarantined files, logs, and `steam_appid.txt` are excluded. Steam initialization, overlay, stat, or Cloud failure is non-fatal and visible in diagnostics.

## 14. Presentation and Audio

Buddy visuals attach directly to each physical body; do not depend on the experimental `SkeletonModification2DPhysicalBones`. Limb connector drawing reads body positions but never drives them. The face presenter resolves consciousness/acute reaction above persistent mood; since M3.6 Task 5 that resolved expression is composed from typed features onto a face plate rather than drawn as an emoticon glyph (see 14.1).

### 14.1 Presentation modes and the expressive layer (M3.5 / M3.6)

Two presentation modes render the same physics truth: `Mii3D` (the shipping default since the M3.5 Task 8 gate) and `LegacyCircles` (a development view kept behind the laboratory `V` key and `--presentation=legacy`). Mode selection is a rendering choice only — every scenario and journey verdict must be identical in both modes.

Dynamic cursor tools use one `CursorToolVisual3D` presenter around the reusable
`Body2DVisual3D` slot. The composition root passes the selected `CursorToolProfile`; an
internal factory resolves its authored `Visual3DKind`. The default kind retains the original
unshaded sphere/capsule scalar path. Focused kinds inject a mesh/material through
`Body2DVisual3D.SetVisual` without changing the 2D collider. The Baseball Bat's clean-room
lathed mesh is vertex-coloured from the profile and lit by the same shadowless rig as the
buddy; roots never branch on content IDs or construct its render Resources.
Asymmetric 2D-authored meshes flip their local Y coordinate at build time: Godot 2D +Y points
down while the frontal 3D plane +Y points up. This keeps semantic ends such as the bat's
barrel/glint (`local 2D -Y`) and wrapped handle (`local 2D +Y`) aligned with the collider.

The M3.6 expressive layer decorates that truth; it never replaces it. `BuddyPosePipeline` arbitrates the pose mode (`Performance` while the buddy behaves, `Tracking` while physics owns the read — grabs, knockouts, hard recoveries) and blends a performance weight between them. Every expressive contributor emits a **bounded offset**, and the presenter clamps the combined offset to `0.5 x part radius` before applying it, so a performance can never move a part far enough to misreport the physics pose. A Tracking cut sets the weight to zero, which snaps all display-only rotation (body yaw, head look-at) to zero in one frame while the committed semantic state (facing side) is remembered.

Contributors, all engine-free models under `DesktopBuddy.Domain.Presentation` with thin Godot nodes:

- **Facing** (`FacingModel` / `FacingController`) — a committed three-quarter side (about `+/-30` degrees yaw) arbitrated as engaged-interaction side > sustained walk direction (hysteresis streak) > seeded idle variety, eased on a monotonic smoothstep that cannot overshoot. Eat temporarily overrides only the presented target to frontal so the buddy faces the food; the committed side remains intact and eases back after Eat.
- **Activities** (`BehaviorActivityComponent` / `ActivitySelector` / `ActivityAnimator`) —
  gameplay requests route through `BuddyRoot.SetBehaviorActivity` into a fixed-tick semantic
  activity; presentation observes its change event. One manual-mode `AnimationPlayer`
  animates six offset proxies, never sockets or bodies. Ordinary clip changes snapshot the
  outgoing proxy pose and cross-fade it into the newly sampled pose, which works equally for
  time-advanced and phase-seeked clips while clearing channels the incoming clip does not own.
  Tracking-mode cuts remain immediate. Priority
  `Eat > Wave > JumpAnticipation > WalkCycle > IdleBreathe`; walk phase derives from
  measured torso travel, so steps match speed and freeze at rest. Eat's typed fixed-tick
  sequence emits exactly five bite events. `ActiveDriveComponent` holds both physical
  hands around a shared upper-chest-to-mouth target while a presentation-only item socket
  follows their midpoint and shrinks once per authoritative bite. The fifth cycle lowers
  the shared hand target to the ordinary limb-rest height and holds it for `30` routed
  ticks so the physical hands arrive before reach releases. Grounded
  zero-walk intent applies a bounded whole-rig horizontal brake; airborne momentum is not
  affected by this idle-stop path.
- **Look-at** (`LookAtModel` / `HeadLookAtComponent`) — priority engaged cursor > item target > impact memory > seeded ambient glance > rest. The component rotates nothing: the presenter adds pitch/yaw into the head socket only.
- **Face** (`FaceComposer` / `FaceCompositor`) — features composed procedurally into a `SubViewport` and mounted as a plate on the head front, inheriting the socket transform. The `Label3D` emoticon glyph is retired and survives only as a fallback for hosts that compose no compositor.

All expressive clocks count `BuddyRoot.RoutedTicks` (the simulation's own routed-tick clock), never engine frames, and the presenter honours `SetPresentationHeld` so a paused laboratory shows a visually still buddy. Presentation code never reads or writes gameplay state.

HUD/panels use Godot Control containers, minimum sizes, anchors, and theme scaling. Responsive layouts are verified at the documented aspect ratios and zooms. Presentation settings alter rendering only and must not affect physics results.

The HUD and shop render whole-credit balances/prices. A reward presenter groups damage rewards over `0.25` seconds and renders brief `+$N.N` feedback without exposing pain. Default presentation settings are V-sync On, `2x` MSAA, Master/SFX `50%`, Work Mode mute On, Screen Shake On, Reduced Motion/Particles Off, and Photosensitivity-Safe Effects On. AA choices are Off/`2x`/`4x`/`8x`; V-sync choices are On/Off. Camera shake moves only game content, never the native window.

Audio consumes semantic events through an `AudioPresenter`, applies master/SFX/Work-Mode mute policy, and never participates in gameplay timing.

The operating-system cursor is never hidden or replaced; cursor-attached tool actors render beneath it. All player-facing text resolves through Godot translation resources with stable keys — no display literals in code, scenes, or typed definitions. The first release ships English only; adding a locale must require only a new translation resource.

## 15. Object and Projectile Lifecycle

`LooseObjectRegistry` assigns a monotonic spawn sequence and tracks held, hazardous, protected, and safe-to-evict flags. Before a spawn that would exceed 24, it evicts the oldest safe/unheld/unprotected object. If none exists, the spawn request fails cleanly and does not consume a purchase, cooldown, fuse, or ammunition action.

Projectiles use pools separate from the loose-object budget, maximum lifetime/distance, and one authoritative interaction ID per shot/pellet (re-minted on every launch, so a reused pool slot can never inherit an earlier shot's contact episode). Grenades and launched care/toy objects participate in the loose-object registry. VFX particles never register as gameplay bodies.

A projectile must not pass through what it is fired at, and from M5 Task 5 that is guaranteed by bounding its per-tick travel inside the smallest target's diameter rather than by `RigidBody2D.ContinuousCd`: the engine's continuous collision prevents tunneling by replacing the body's velocity with the reduced velocity that reaches the surface, which destroys the momentum the shared pain pipeline scores from. `GunProfile` validates the bound and rejects a faster muzzle speed. See `DECISIONS.md`, "Cursor-Gun Platform and Pistol", for the measurements.

A projectile's **rotation is also left free**, and its visual is what compensates: a round body drawn along its own velocity looks identical whether or not it is spinning, while locking rotation halves the contact impulse a hit reports and so halves every gun's damage. Orient a projectile's visual from its velocity, never from its body transform (`DECISIONS.md`, "Gun Feel Refinement").

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

- Pure C# tests: formulas, timers, state machines, economy, saves/migrations, stats/achievements, queue idempotency. These compile against the Godot-free domain assembly (Section 22) and run with plain `dotnet test`.
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

## 20. Godot Engine and Project Configuration

Baseline `project.godot` requirements, established in Milestone 0:

- `physics/common/physics_ticks_per_second = 120`.
- `physics/common/max_physics_steps_per_frame` set explicitly (recommend `4`–`6`; final value is tuning data). This bounds post-stall catch-up: after a modal window move/resize loop, driver stall, or resume, at most that many ticks run in one rendered frame and excess accumulated time is dropped by the engine. Combined with tick-counted gameplay timers this implements the no-simulation-burst rule; brief slow-motion under heavy stall is the accepted trade-off, never a burst.
- `physics/common/physics_interpolation = true`.
- `display/window/size/viewport_width = 480`, `viewport_height = 360`, `borderless = true`, `transparent = true`, `always_on_top = true`, and `display/window/per_pixel_transparency/allowed = true`. The `per_pixel_transparency/allowed` flag cannot be enabled at runtime; a build missing it silently loses transparency in release.
- Stretch mode `disabled`. The game owns zoom and responsive layout (Section 21); engine stretch must not fight them.
- Renderer: `gl_compatibility` is the primary choice for the UHD 630-class budget. The template leftovers `physics/3d/physics_engine="Jolt Physics"` (no 3D physics is used) and `rendering_device/driver.windows="d3d12"` (inapplicable under Compatibility, misleading to readers) are removed. Per-pixel transparency, `msaa_2d`, and V-sync are validated together on Windows 10/11 hardware at the start of Milestone 2; if Compatibility fails that spike, Forward+ (Vulkan) is the fallback and the decision is recorded before HUD work begins.
- `application/config/custom_user_dir_name` is chosen in Milestone 0 and never changed after release; save paths and Steam Auto-Cloud file patterns depend on it.
- Named 2D physics layers. Starting proposal, validated by the collision-layer tests (the buddy-never-self row is contractual; the rest is lab-adjustable):

| # | Layer | Collides with |
| --- | --- | --- |
| 1 | RoomBounds | 2, 3, 4, 5 |
| 2 | BuddyParts | 1, 3, 4, 5 — never 2 |
| 3 | LooseObjects | 1, 2, 3, 4, 5 |
| 4 | Projectiles | 1, 2, 3 (no projectile–projectile, no projectile–tool) |
| 5 | PhysicalTools | 1, 2, 3 |
| 6 | InteractionSense | detection-only areas; scans 3 |

## 21. Zoom, Room Size, and View Ownership

Zoom is a view transform, never a physics change. `Camera2D` zoom scales world rendering; Control theme scale handles UI. RigidBody2D shapes, masses, springs, and all accepted tuning are zoom-invariant; rescaling physics bodies per zoom level would invalidate the physics laboratory results.

The sandbox room size in world units is derived: `window client size / zoom`. Window resize and zoom change both rebuild boundaries through the same boundary-controller path; nothing else may resize the room.

The sandbox floor is `360x270` world units — the minimum window at `100%` zoom — so zoom introduces no room smaller than the smallest already-supported window. Zoom levels whose room would fall below the floor for the current window are unavailable: the stored preference is retained, the effective zoom clamps to the largest supported level, and settings present unsupported levels as disabled for the current window. Stability validation therefore covers rooms from `360x270` world units upward.

## 22. Assembly Layout, Test Harness, and CI

Four C# projects under one solution:

- `DesktopBuddy.Domain` — plain .NET class library, no `Godot.NET.Sdk` reference. Owns formulas, pain window, knockout timing, mood/trust rules, economy, statistics windows, save DTOs/migrations, platform-operation queue logic, and tick-count timers.
- `DesktopBuddy` — the Godot game assembly (root `.csproj`), references Domain. Because `Godot.NET.Sdk` globs `**/*.cs` under the project directory, the root project excludes the nested project folders via `DefaultItemExcludes`.
- `DesktopBuddy.Domain.Tests` — xUnit against Domain only; runs with `dotnet test` and no Godot runtime.
- `DesktopBuddy.Steam` — the optional Steam adapter assembly (Milestone 6), loaded through the platform factory.

Headless Godot scenarios use a dedicated runner scene invoked as `godot --headless -- --scenario=<id> --seed=<n>`, emitting machine-readable JSON verdicts and envelope metrics for CI. That invocation protocol is the contract CI depends on; a framework such as gdUnit4 may be adopted later behind it.

CI from Milestone 0: `dotnet build`, Domain unit tests, headless editor import, and one smoke scenario on every push, with no proprietary Steam binaries required. The standalone Windows matrix in `TEST_PLAN.md` Section 5 remains manual. Toolchain is pinned: `global.json` for the .NET SDK; the exact Godot 4.6.1 editor and export-template versions documented in `README.md`.

Release export presets exclude test scenes, laboratory scenes, and debug telemetry content via export filters; the no-selectable-placeholder rule applies to shipped builds only because those scenes never ship.

## 23. Physics Integration Details

- One fixed-tick entry point. `SandboxRoot` owns the only gameplay `_PhysicsProcess`; it drives the Section 7 order through explicit method calls (`BuddyRoot` routes the buddy-internal portion). Components do not register their own `_PhysicsProcess`: Godot offers no useful cross-sibling ordering guarantee, and 120 Hz × N marshaled callbacks is measurable overhead.
- Buddy parts set `CanSleep = false` — the spring solver must never fight the sleep heuristic. Loose objects keep sleeping enabled; the 24-object CPU budget assumes settled objects sleep, and registry/eviction logic must handle sleeping bodies.
- Contact reporting is explicit configuration: the six parts (and anything else the `ImpactRouter` samples) need `ContactMonitor = true` and `MaxContactsReported` sized generously (≥ 8), because both contact signals and `PhysicsDirectBodyState2D` contact queries require it. Forgetting this yields silent zero-contact behavior, so a startup validation check asserts it.
- Contact data visible during `_IntegrateForces`/direct state reflects the previously completed solver pass; accepted pain therefore trails physical contact by one 120 Hz tick. This is accepted reality — tolerance and exact-timer assertions already treat one tick as the base uncertainty; implementations must not fight it with same-tick hacks.
- Exact durations (4 s knockout, 4/8 s Burning, 0.15 s debounce, weapon cadence, care cooldowns) count integer ticks at 120 Hz rather than accumulating floats.
- Allocation policy: steady-state physics ticks allocate zero managed heap. Hot-path events are plain C# delegates/interfaces carrying `readonly record struct` payloads; Godot signals are reserved for low-frequency semantic/UI events. The published view snapshot is double-buffered, not rebuilt per tick. LINQ, closures, boxing, and `params` arrays are banned from tick paths. A performance test measures allocation deltas across a scripted active scene.
- Seeded randomness: components never call engine or global RNG directly. An injectable random source is provided per consumer family, with the behavior/decision stream isolated from presentation-only streams so envelope repeatability never depends on VFX. Headless scenarios inject fixed seeds; production seeds from entropy.
- Behavior arbiter cadence: intents recompute every tick, but goal switches pass a hysteresis/commitment rule (tuning data) so autonomy cannot flip-flop at 120 Hz.
- Non-contact damage entry: explosions and fire produce no solver contact. The grenade explosion applies its radial impulses physically and submits synthetic attributed `ImpactSample`s through the same `ImpactRouter` thresholding; the Fire Sprayer detects buddy contact through a cone/area query owned by its tool behavior and applies Burning, whose pain then flows through attributed status ticks. VFX particles remain non-gameplay in both cases.

## 24. Windows Lifecycle Messages, Tray, and Hidden Mode

The Windows adapter observes and forwards, at minimum: `WM_ENTERSIZEMOVE`/`WM_EXITSIZEMOVE` (modal move/size loop; the post-loop frame must obey the Section 20 no-burst bound), `WM_DPICHANGED`, `WM_DISPLAYCHANGE` and work-area `WM_SETTINGCHANGE` (monitor topology and taskbar changes feed the same clamping path as startup restore), `WM_POWERBROADCAST` (suspend/resume drives the lifecycle discontinuity rule), and session lock/unlock via `WTSRegisterSessionNotification`. Session lock continues accrual as normal running time and is never a clock discontinuity; while locked, the game may drop into the hidden-style low-cost mode below and restore the prior presentation state on unlock.

Tray: Godot's `DisplayServer` status-indicator API covers the icon and click callback; the menu is either a Godot popup positioned at the cursor or a native adapter menu. Validate on Windows 10/11 during Milestone 2 and keep the choice inside the adapter.

Hidden-to-tray is implemented concretely as: hide the window, `SceneTree.Paused = true`, `RenderingServer.RenderLoopEnabled = false`, and `Engine.MaxFps` throttled to roughly 10 iterations per second. A single lifecycle-coordinator node with `ProcessMode.Always` performs mood drift and passive-income accrual from the monotonic application clock at that low cadence. Showing the window reverses the settings, resets physics interpolation, and resumes the frozen visible state. Foreground play never uses `OS.low_processor_usage_mode`; it would jitter the 120 Hz loop.
