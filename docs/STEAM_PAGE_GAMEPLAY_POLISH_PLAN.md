# Desktop Buddy — Steam Page Gameplay Capture Polish Plan

Status: **OWNER-LOCKED / IMPLEMENT NOW**  
Branch: `agent/steam-page-gameplay-polish`  
Baseline: `main` at `7e3ddad7cb4707c56fef14b0ffcf8f9bfed51d40` (Asset Forge v1 merged)  
Deadline: **Sunday, 2026-08-23**

## 1. Purpose

This pass exists to make the gameplay systems that will appear in Steam screenshots/gameplay capture look and feel production-ready. It does **not** implement the Steam store page itself.

Capture-critical showcase systems, in owner priority:

1. Pistol
2. Grab — already accepted; regression-only
3. Baseball Bat
4. Grenade
5. Boxing Glove
6. Paint Buddy
7. Buddy Studio
8. Work Mode
9. Shared Win98 UI motion/feedback used by the above
10. Showcase-system SFX integration/audit

The pass must favor visible, high-value polish over broad feature work. Anything not necessary for capture or explicitly locked below stays in the later Steam Demo polish milestone.

---

## 2. Explicit non-goals for this branch

Do not spend this deadline on:

- Steamworks store-page fields, copy, tags, pricing, questionnaire, publishing, or Valve review submission;
- trailer editing, capture editing, capsule art, logo/wordmark work;
- tool dropping/picking-up world mode;
- double-clicking a dropped tool to re-equip it;
- pullback trajectory/throw-arc redesign;
- demo onboarding/tutorial sequence;
- economy rebalance;
- character-slot economy;
- broader Buddy personality/happiness/autonomy work;
- Room Decorator feature expansion or demo-scope decision;
- full accessibility/key-rebinding pass;
- release-candidate performance/DPI/multi-monitor matrix;
- owner-authored replacement SFX files themselves;
- Work Mode replacement computer asset (owner supplies later).

These remain valid Demo/Full Release work; they are not capture blockers.

---

## 3. Architecture rules for this pass

1. **Tune existing authored profiles before creating special cases.** Existing `GunProfile`, `SwingToolProfile`, `GrenadeProfile`, `CursorToolProfile`, and Work milestone/domain seams remain the source of truth.
2. **Presentation polish must not bypass gameplay authority.** VFX may observe events (`ShotFired`, `Detonated`, `ImpactAccepted`, paint samples) but may not award pain, money, ownership, or save state on their own.
3. **Keep physics measurable.** Bat/glove impact changes should come from physical tuning or an explicitly authored physical shove, not a hidden visual-only damage multiplier.
4. **Cosmetic debris/paint droplets are non-physical.** No `RigidBody2D`, collision, registry admission, or persistence for capture particles.
5. **Accessibility always wins.** Existing `ReducedMotion`, `ReducedParticles`, `ScreenShake`, and `PhotosensitivitySafe` settings override visual polish. A new authentic-vs-modern Win98 preference is aesthetic, not an accessibility substitute.
6. **Do not change save/economy semantics for capture.** Buddy Studio purchase/equip behavior and Work milestone settlement stay transactionally identical.
7. **Compatibility renderer matters.** The project uses Godot compatibility rendering; do not rely on `GPUParticles2D.emit_particle()`, which Godot documents as Forward+/Mobile-only. Prefer small pooled `Node2D`/`Control` FX or compatible one-shot emitters.

---

## 4. Research guidance applied

### Godot motion

Godot `Tween` is appropriate for short dynamic UI/camera interpolation. Create a new tween for each retarget, kill the previous tween, and use short ease-out transitions. Do not tween container-owned minimum sizes or properties the layout system immediately overwrites.

Reference: Godot Tween documentation (`CreateTween`, `TweenProperty`, `Kill`, transition/ease behavior).

### Godot audio

`AudioStreamPlayer` supports `max_polyphony`; repeated combat/UI cues should not cut each other off just because one player is reused. Preserve the project's `AudioMix.Sfx` / UI bus routing and expose replaceable streams/hooks rather than synthesizing more final audio.

Reference: Godot `AudioStreamPlayer` / `AudioStreamPlayer2D` documentation.

### Godot particles

Godot particle systems are appropriate for non-physical presentation, but live-particle count directly affects cost and compatibility differs by renderer. This branch should use bounded, short-lived presentation pools for paint drops, grenade debris, muzzle smoke, and impact accents.

Reference: Godot `GPUParticles2D` documentation, especially `amount`, `lifetime`, `one_shot`, and Compatibility limitations of direct particle emission.

### Character-creator/catalogue UX

The Sims' official Create-a-Sim material reinforces a proven pattern already compatible with Desktop Buddy: choose a category, browse visual item options, and keep the selected appearance obvious. Sims updates also treat correct categorization, thumbnails, variants/swatches, and filtering as core usability concerns. Desktop Buddy should borrow the hierarchy, **not copy Sims visual design**: category -> visual grid -> unmistakable selected/owned/equipped/price state -> primary action.

Reference: EA's official The Sims 4 Create A Sim guidance and game-update notes.

---

# 5. Current code map

## 5.1 Pistol

Primary files:

- `data/tools/gun_pistol.tres`
- `src/Tools/GunProfile.cs`
- `src/Tools/CursorGunComponent.cs`
- `src/Presentation3D/CursorGunVisual3D.cs`
- existing pistol scenarios/journeys under `src/Testing` / `tests/journeys`

Important current behavior:

- muzzle flash already exists in both legacy and 3D presentation;
- recoil and camera shake already exist;
- casing ejection already exists in `CursorGunComponent.EjectCasing()` but pistol does not author `EjectsCasingOnShot = true`;
- projectile collision tuning assumes a validated maximum travel of 24 px/tick;
- current pistol speed 2400 px/s at 120 Hz = 20 px/tick.

Do not create a second firing/VFX pipeline.

## 5.2 Grenade

Primary files:

- `data/tools/grenade.tres`
- `src/Tools/GrenadeProfile.cs`
- `src/Tools/GrenadeComponent.cs`
- `src/Tools/GrenadeVisual2D.cs`
- `src/Tools/PullbackLauncherComponent.cs`
- `src/Objects/LooseObjectProfile.cs`
- `src/Objects/LooseObjectRegistry.cs`
- `src/Tools/GrenadeAudioComponent.cs`

Current structural blockers:

- `PullbackLauncherComponent.ReplaceWith()` calls the injected room-wide clear callback before every launchable spawn;
- launcher stores one `_spawned` reference;
- `GrenadeComponent` stores one tracked body / one fuse phase;
- therefore “every click spawns another grenade” requires real multi-instance state, not a visual duplicate.

## 5.3 Baseball Bat

Primary files:

- `data/buddy/lab_cursor_tool_baseball_bat.tres`
- `src/Tools/ChargedSwingComponent.cs`
- `src/Tools/SwingToolProfile.cs`
- `src/Tools/SwingHitLagComponent.cs`
- `src/Tools/CursorToolController.cs`
- `src/Presentation3D/CursorToolVisual3D.cs`
- `src/Tools/SwingAudioComponent.cs`

Current full-charge `TipSpeedFull = 6000`. Owner target is 1.5x full-power impact; first implementation target is `9000` while preserving the measured-contact damage path.

## 5.4 Boxing Glove

Primary files:

- `data/buddy/lab_cursor_tool_boxing_glove.tres`
- `src/Tools/CursorToolProfile.cs`
- `src/Presentation3D/CursorToolVisual3D.cs`
- `src/Interaction/InteractionDamageComponent.cs`
- `src/Tools/SwingHitLagComponent.cs` (pattern reference, not necessarily direct reuse)

Current visual issue is architectural and simple: `CursorToolVisual3DKind` only has `Capsule` and `LathedBat`; glove authors no special kind and has `Length = 0`, therefore presentation falls back to a red sphere.

`AcceptedImpact` already includes `ContentId`, Buddy part, raw impulse, relative speed, point, normal, pain, and knockout state. A glove-head punctuation component can therefore observe accepted impacts without changing the core contact detector.

## 5.5 Paint Buddy

Primary files:

- `src/CharacterEditor/PaintCanvasControl.cs`
- `src/CharacterEditor/PaintCanvasControl.LimbPose.cs`
- `src/CharacterEditor/CharacterEditorHost.Painting.cs`
- current Paint Buddy workspace / hit mapper / generated-paint seams

Current `ExpandedLimbPose` offsets hands and feet only. Head/neck are not part of the expanded pose, which explains the owner's missing stretched neck observation.

## 5.6 Buddy Studio

Primary files:

- `src/CharacterEditor/BuddyStudio/BuddyStudioWorkspace.cs`
- `src/CharacterEditor/BuddyStudio/BuddyStudioWorkspace.PreviewNavigation.cs`
- `src/UI/Win98/Win98CatalogGrid.cs` and related tile/panel controls
- `src/CharacterEditor/CharacterEditorHost.SettingsRows.cs`

The current session already knows preview/owned/equipped/price/balance states. Redesign presentation only; do not invent new commerce state.

The current camera zoom writes `Camera3D.Position` / `Size` immediately. This is the correct seam for a short retargetable tween.

## 5.7 Work Mode

Primary files:

- `src/Work/WorkCompanionView.cs`
- `src/Work/WorkCompanionCoordinator.cs`
- `src/Work/WorkMilestoneDefaults.cs`
- `src/Work/WorkFirstEntryRewardService.cs`

Milestone payout is already settled immediately and flushed safely. Current live presentation mainly shows action counts; earned credits are surfaced chiefly through the status text after exit. Improve feedback without changing settlement.

## 5.8 Settings / motion

Primary integration file:

- `src/CharacterEditor/CharacterEditorHost.SettingsRows.cs`
- existing `LocalSettingsSave` record and shell edit/save path resolved by IDE/build references
- reusable Win98 UI controls under `src/UI/Win98`

Existing accessibility settings already include Reduced Motion, Screen Shake, Reduced Particles, and Photosensitivity Safe. Add a distinct aesthetic preference such as `ModernUiMotion` (default true); when `ReducedMotion` is true, motion is suppressed regardless of this value.

---

# 6. Implementation sequence

## CAP-0 — Baseline and capture regression contract

Before tuning:

1. Build `DesktopBuddy.sln`.
2. Run domain tests.
3. Run the repository quick/focused validators that cover gun, grenade, bat, Paint Buddy, Buddy Studio, and Work Mode.
4. Record current authored values in affected `.tres` files in this plan/commit history; do not duplicate them into runtime constants.

Add one focused capture-polish validator script only if it saves repeated manual command entry; do not build a second testing framework.

Exit:

- branch builds from Asset Forge v1 main;
- no pre-existing red is misattributed to polish work.

---

## CAP-1 — Pistol capture feel

### Owner target

Faster bullets, faster firing/spam, stronger recoil, better Buddy impact read, shell ejection, muzzle flash retained/enhanced, smoke after repeated fire.

### First authored tuning pass

Keep the existing profile architecture. Initial values to test:

```ini
# data/tools/gun_pistol.tres
ShotIntervalTicks = 18          # 6.67 shots/sec at 120 Hz; currently 30
MuzzleSpeed = 2760.0            # 23 px/tick at 120 Hz; remains below 24 px/tick bound
FireShakeAmplitudePx = 2.25
FireShakeDecayTicks = 10
MuzzleFlashTicks = 4
RecoilKickPx = 4.5
RecoilTicks = 6
EjectsCasingOnShot = true
CasingPoolCapacity = 24
```

Do not exceed 2880 px/s unless the projectile travel/collision proof is deliberately revalidated because 2880 / 120 = 24 px/tick.

### Impact feedback

Use increased projectile momentum plus existing `ContactShove*` tuning first. If visual read still feels weak, add presentation-only hit punctuation subscribed to projectile/accepted-impact events:

- 1 short impact spark/ring at hit point;
- optional 1–2 frame brightness accent under Photosensitivity Safe cap;
- no new pain calculation;
- obey Reduced Particles and Screen Shake.

### Shell ejection

Enable the already-existing `EjectCasing()` path. Verify pool reuse under an entire magazine plus reload. Casings are presentation loose bodies already supported by the gun architecture; do not invent a second casing entity.

### Rapid-fire smoke

Add a small heat accumulator to `CursorGunVisual3D` or a dedicated presentation child:

```csharp
_heat = Mathf.Clamp(_heat + HeatPerShot, 0f, 1f);
_heat = Mathf.Max(0f, _heat - HeatDecayPerSecond * (float)delta);
if (_heat >= SmokeThreshold && effects.ParticlesAllowed)
    _smokePool.Emit(muzzleWorldPosition, aimDirection);
```

Requirements:

- smoke starts only after a short burst, not every first shot;
- bounded pool, e.g. <= 12 smoke puffs;
- 0.3–0.7 s lifetime;
- no collision/physics;
- reduced-particles mode thins or disables it.

### Tests

Extend existing pistol coverage to assert:

- authored projectile travel remains <= 24 px/tick at 120 Hz;
- cadence accepts repeated input at the new interval;
- casing count increments when enabled and pool remains bounded;
- muzzle flash/recoil counters still activate;
- no ammo/reload/economy regression.

Manual gate: one magazine spammed into Buddy must read immediately as faster/heavier without losing aim readability.

---

## CAP-2 — Multi-grenade + explosion punctuation

### Owner target

Every click spawns a grenade; many may coexist subject only to the existing object budget/performance policy. Each grenade has independent fuse feedback. Explosion: larger blast, stronger shove, flash, smoke, non-physical debris.

### 2A. Make spawn policy explicit

Do **not** special-case `ContentIds.Grenade` in input code. Add authored spawn policy to `LooseObjectProfile`, e.g.:

```csharp
public enum LooseObjectSpawnPolicy
{
    ReplaceExisting = 0,
    Additive = 1,
}

[Export] public LooseObjectSpawnPolicy SpawnPolicy { get; set; }
```

Existing content defaults to `ReplaceExisting`, preserving behavior. `grenade.tres` authors `Additive`.

Refactor `PullbackLauncherComponent.ReplaceWith()` into spawn/admission behavior:

```csharp
if (profile.SpawnPolicy == LooseObjectSpawnPolicy.ReplaceExisting)
    _clearExistingLooseObjects!();

LooseObjectBody body = Spawn(profile);
```

Stop treating `_spawned` as authority for all launchables. Keep `LastSpawned` only as convenience/diagnostic; registry owns the live set.

### 2B. Multi-instance fuse state

Replace the single tracked grenade state with per-body state, keyed by stable runtime identity:

```csharp
private sealed class ActiveGrenade
{
    public required LooseObjectBody Body { get; init; }
    public int FuseTicksRemaining { get; set; }
    public GrenadeFusePhase Phase { get; set; }
    public int LastThudTick { get; set; }
}

private readonly Dictionary<long, ActiveGrenade> _active = [];
```

Exact key type should match `LooseObjectBody.RuntimeId`.

Each physics tick:

1. remove invalid/despawned entries safely;
2. tick every active fuse independently;
3. update per-grenade visual fuse phase;
4. detect thud gating per grenade;
5. collect due detonations into a temporary list;
6. detonate after enumeration to avoid dictionary mutation during iteration.

Never let detonating grenade A cancel grenade B.

### 2C. Object budget

“Many as performance permits” still means the established `LooseObjectRegistry` admission cap remains authoritative. If capacity is full:

- use its existing safe-eviction policy;
- armed/hazardous grenades must remain protected from unsafe eviction;
- failed admission simply refuses the extra grenade; no unbounded hidden nodes.

### 2D. First explosion tuning pass

Start with roughly 20–30% larger/stronger presentation and owner-tune from there, for example:

```ini
EquivalentImpulseAtCenter = 1400.0
BlastFullRadiusPx = 60.0
BlastZeroRadiusPx = 220.0
ShoveImpulseAtCenter = 2400.0
KickAmplitudePx = 5.0
FlashTicks = 6
RingTicks = 26
FireballTicks = 22
EmberCount = 18
```

Do not equate visual radius with pain radius accidentally; keep profile fields semantically separate.

### 2E. Fuse feedback

For each live grenade:

- pin ejection remains immediate;
- visible blink/pulse accelerates during final fuse segment;
- optional subtle scale pulse; Photosensitivity Safe reduces brightness/frequency;
- do not add a global UI countdown.

### 2F. Smoke/debris

On `Detonated(center)`:

- one short flash/fire core;
- 3–6 expanding smoke puffs;
- 6–12 tiny debris sprites/quads with simple velocity/gravity integration in a presentation node;
- debris has **no physics bodies** and no collision;
- pool all effects and cap live count;
- Reduced Particles cuts counts by at least half.

### Tests

Add/extend grenade scenario:

- spawn >= 3 grenades without clearing predecessors;
- unique runtime IDs;
- independent fuse ages;
- staggered detonation order correct;
- A detonation does not delete B/C;
- each blast applies falloff from its own center;
- live grenade count never bypasses registry budget;
- hazardous/protected grenade eviction rules remain valid;
- VFX counters bounded and no physics node created for debris.

Manual gate: rapidly spawn several grenades around Buddy and observe overlapping independent fuses/explosions without input stalls or visual ambiguity.

---

## CAP-3 — Baseball Bat impact + swing-direction indicator

### Owner target

Full-power impact 1.5x, much stronger launch, clearer control through a small real-time swing-direction arrow in front of cursor.

### Physical tune

Change the authored full-charge endpoint first:

```ini
# data/buddy/lab_cursor_tool_baseball_bat.tres
TipSpeedFull = 9000.0
```

Leave uncharged speed unchanged initially so normal taps do not become excessive. Verify existing maximum-force/servo bounds are sufficient; if the body cannot physically reach the target, raise the authored bat-specific force cap just enough rather than multiplying pain after contact.

### Direction indicator

Expose the **same** intended direction used by `ChargedSwingComponent` / `CursorToolController`; do not duplicate aim math in the renderer.

Recommended seam:

```csharp
public Vector2 PlannedSwingDirection { get; }
public bool ShouldShowSwingGuide { get; }
```

Presentation node draws a small arrow roughly 24–36 px from cursor:

- points in actual upcoming sweep direction;
- updates every frame while bat is held/chargeable;
- hidden during committed swing/recovery and when another tool is selected;
- no mouse filter/input/collision;
- high contrast against room but visually subordinate to bat;
- Reduced Motion does not need to hide a static guide.

### Tests

- full-charge target speed is exactly 1.5x previous endpoint;
- charge interpolation still monotonic;
- guide direction matches controller's committed swing direction for representative cursor deltas;
- no guide for glove/other tools;
- existing hitlag resumes physics on every exit/recovery path.

Manual gate: player should be able to predict the bat's sweep before release and a full charge should visibly launch Buddy farther.

---

## CAP-4 — Boxing Glove visual + critical head-hit punctuation

### Owner target

Replace red sphere with recognizable glove. Hard head punches get a short “grit” beat: brief hitlag/slow-motion-style punctuation, higher physical impact, extra damage read; shorter than max bat hit.

### 4A. Dedicated trusted visual kind

Extend:

```csharp
public enum CursorToolVisual3DKind
{
    Capsule = 0,
    LathedBat = 1,
    BoxingGlove = 2,
}
```

Add `BoxingGloveMeshBuilder` under `src/Presentation3D` using the same trusted procedural-material idiom as existing tool builders.

Shape target:

- padded rounded fist volume;
- distinguish thumb bulge;
- short cuff/wrist opening;
- current red/dark-red authored colors;
- silhouette reads as a boxing glove at gameplay scale from front and 30-degree views.

Set `Visual3DKind = 2` in glove profile.

Do not change glove collision radius solely to match the visual unless owner feel testing shows a real hit mismatch.

### 4B. Head-hit punctuation

Create a small observer component rather than turning the glove into a charged swing:

```csharp
if (impact.ContentId == ContentIds.BoxingGlove &&
    impact.Part == BuddyPart.Head &&
    impact.RawImpulse >= profile.CriticalHeadImpulse)
{
    StartShortHitLag(profile.CriticalHeadHitLagTicks);
    CriticalHeadHit?.Invoke(impact.Point, impact.Normal);
}
```

Add authored glove-critical fields to the appropriate cursor-tool profile or a nested `ImpactPunctuationProfile`, not hard-coded global constants.

Targets:

- hitlag ~4–8 physics ticks; strictly shorter than home-run bat max punctuation;
- presentation ring/spark at head;
- stronger physical shove can be authored, but keep damage routed through measured impact whenever possible;
- if extra pain is still needed after physical tuning, introduce an explicit, tested critical-contact rule rather than writing directly to mood/health from presentation.

### Tests

- glove uses dedicated visual kind;
- hard head impact triggers critical punctuation;
- body/hand/foot hit at same speed does not;
- weak head tap does not;
- hitlag always releases;
- no glove behavior contaminates bat swing epoch logic.

---

## CAP-5 — Paint Buddy capture polish

### Owner target

Cosmetic paint-drop particles while painting; Show Limbs stretches neck/head connector consistently with arms/legs. Owner supplies final paint SFX later.

### 5A. Paint visual samples

`PaintCanvasControl` is the correct source because it already knows successful stroke movement. Emit presentation-only samples only when a gesture actually paints:

```csharp
public event Action<PaintVisualSample>? PaintVisualSampled;

public readonly record struct PaintVisualSample(
    Vector2 CanvasPosition,
    Color Color,
    PaintTool Tool);
```

Rate-limit by both time and distance. Suggested ceiling: <= 30 samples/sec and <= 24 live droplets.

`CharacterEditorHost.Painting` owns the FX overlay/pool. Each droplet:

- uses active paint color;
- 2–5 px visual radius;
- gets tiny outward velocity + downward drift;
- fades/scales over ~0.2–0.4 s;
- ignores mouse input;
- no physics/collision/persistence;
- disabled/thinned by Reduced Particles.

Do not generate particles for eyedropper, view navigation, or failed/unmapped paint samples.

### 5B. Expanded neck

Extend expanded pose to include Head displacement away from Torso and make the visual connector span the new gap.

Important persistence rule: do **not** create a seventh/new paint surface merely for this capture fix. If neck connector painting is not already represented by a trusted surface, keep the neck connector presentation-only for this pass and preserve existing body paint schema. The owner request here is that the connector visibly stretches like other limbs.

### 5C. Paint sound hook

Add/retain one replaceable stroke-loop or dab cue seam, but do not block this branch on final audio. Owner will supply the painting sound.

### Tests

- paint samples occur only during successful paint gesture;
- bounded live FX count;
- Reduced Particles path works;
- Show Limbs changes head/neck visual pose and returns exactly on disable/exit;
- body paint bytes/save format unchanged by the neck visual change.

---

## CAP-6 — Buddy Studio capture UX: smooth view + store clarity

### Owner target

Fast smooth zoom transitions, toggleable; clearer store-like hierarchy inspired by proven character creators; price and ownership/equipped state should be immediately legible.

### 6A. Retargetable preview tween

In `BuddyStudioWorkspace.PreviewNavigation.cs`, preserve `_viewFocus`/`_viewZoom` as canonical target state and animate only camera presentation.

Pattern:

```csharp
private Tween? _previewTween;

private void ApplyOrAnimateView(Vector3 targetPosition, float targetSize)
{
    _previewTween?.Kill();

    if (!MotionPolicy.AllowsUiMotion)
    {
        _previewCamera.Position = targetPosition;
        _previewCamera.Size = targetSize;
        return;
    }

    _previewTween = CreateTween().SetParallel(true)
        .SetTrans(Tween.TransitionType.Quad)
        .SetEase(Tween.EaseType.Out);
    _previewTween.TweenProperty(_previewCamera, "position", targetPosition, 0.14);
    _previewTween.TweenProperty(_previewCamera, "size", targetSize, 0.14);
}
```

Use actual Godot C# property paths/constants as required by the project version; code above describes the intended shape.

Rules:

- 120–180 ms target duration;
- repeated wheel input kills/retargets from current visual state;
- category framing and Reset View use same path;
- initial construction may snap once before first frame to avoid an unwanted entrance travel;
- Reduced Motion or authentic Win98 mode snaps immediately.

### 6B. Catalogue hierarchy

Do not change purchase/equip semantics. Recompose existing known state:

**Tile**

- thumbnail is largest region;
- item name directly under/over thumbnail;
- one persistent state chip: `Equipped` > `Owned` > price;
- equipped uses active-title-blue border even when selected;
- selected preview gets a distinct thicker preview outline without replacing equipped state;
- price green/red only for affordability text, not whole tile;
- no duplicate `Owned`, `Equipped`, `Preview` labels in multiple places.

**Inspector**

Top section becomes a purchase/status card:

```text
<Item name>
<price / Owned / Equipped>
Balance: 123
[ Buy ] / [ Equip ] / [ Equipped ]
```

Then color/transform controls. Save/Exit remain anchored at inspector bottom.

**Category strip**

- preserve existing category order;
- current category unmistakable;
- optional `owned/total` count only if it remains visually quiet;
- do not introduce search/filter complexity for the current small catalogue.

### 6C. Motion toggle integration

See CAP-8. Buddy Studio consumes shared motion policy, not its own private setting.

### Tests

- commerce/session state unchanged;
- owned/unowned/equipped/preview combinations render exactly one primary state;
- insufficient funds remains obvious;
- rapid zoom retarget cannot leave camera in NaN/out-of-bounds state;
- Reduced Motion/authentic mode snaps;
- Save/Cancel semantics and generated cosmetics unaffected.

Manual gate: at a glance, a capture viewer can tell what is selected, what costs money, and what is already owned/equipped.

---

## CAP-7 — Work Mode earning/reward clarity

### Owner target

Work Mode remains optional but rewarding. Capture should make passive earning understandable. Reward feedback is in scope; replacement PC art is not.

### Preserve domain settlement

Do not modify `WorkSessionState.Evaluate`, `WorkProgressState`, wallet settlement order, or crash-safe flush semantics just to improve presentation.

### Live earning display

Extend `WorkCompanionView` with a small CRT/status readout that can receive both actions and earned credits:

```csharp
public void SetProgress(
    long sessionActions,
    long lifetimeActions,
    long sessionMilliCredits,
    WorkMilestoneView? latestMilestone)
```

Preferred visual hierarchy:

- CRT primary number remains actions;
- small secondary line: `Earned: +12.5`;
- brief floating/CRT pulse on milestone payout: `+5 credits`;
- optional `Next reward: 2,500 actions` only if current milestone catalogue can expose it cheaply and truthfully.

Coordinator already has `_sessionSettledMilliCredits`; update view immediately after settlement.

### Reward punctuation

On `newlyEarned.Count > 0`:

- short title-blue/green Win98 notification near CRT;
- 0.8–1.5 s, no modal/input blocking;
- optional replaceable UI reward SFX hook;
- Reduced Motion uses static/fade-only or status text;
- no reward animation may delay wallet settlement.

### Tests

- displayed session earnings equals settled milli-credits;
- multiple milestones in one drain aggregate correctly;
- leaving Work still gives existing summary;
- no duplicate deposits caused by presentation callbacks;
- lifetime/session counter toggle unaffected.

---

## CAP-8 — “Modern Win98” motion system + authentic toggle

### Owner target

Visually Windows 98, behaviorally modern/responsive. Players who prefer authentic rigid Win98 can disable modern motion.

### 8A. One persisted preference

Add machine-local setting:

```csharp
bool ModernUiMotion = true;
```

Settings row under Accessibility or Display/Interface:

```text
Modern UI Motion     [On]
Smooth short transitions while keeping the Windows 98 visual style.
Turn off for rigid/authentic Windows 98 transitions.
```

Policy:

```csharp
AllowsUiMotion = settings.ModernUiMotion && !settings.ReducedMotion;
```

`ReducedMotion` always overrides this preference.

### 8B. Shared helper, not dozens of arbitrary tweens

Create a small `Win98Motion`/`UiMotionPolicy` helper in `src/UI/Win98` with a few named operations:

- `Open(Control)` — 100–140 ms opacity + 0.98 -> 1.0 scale where safe;
- `Close(Control, Action after)` — 80–120 ms;
- `Reveal(Control)` — 80–120 ms fade/4 px settle for menus/panels;
- `Pulse(Control)` — purchase/reward acknowledgement;
- camera/view interpolation consumed by Studio separately.

Do not animate native Windows position/size, transparent hit regions, or layout minimum sizes. Those paths have historically been regression-prone.

### 8C. Capture-critical targets only

Apply modern motion first to:

- Inventory catalogue open/selection/purchase acknowledgement;
- Buddy Studio category/selection and preview camera;
- Paint Buddy palette/tool confirmations where safe;
- Work milestone feedback;
- Win98 modal appearance;
- dropdown/popup reveal.

Do not blanket-animate every control before capture.

### Motion language

- most interactions 80–160 ms;
- ease-out for entrances/selection response;
- no bouncy overshoot by default;
- button press retains classic recessed Win98 visual immediately (input acknowledgment must not wait for tween);
- animations never block clicks;
- authentic mode uses current immediate behavior.

### Tests

- settings persist/reload;
- Reduced Motion overrides modern motion;
- hidden/closing controls cannot remain mouse-active accidentally;
- repeated open/close kills old tween cleanly;
- focus and keyboard activation unchanged.

---

## CAP-9 — Showcase SFX inventory and replacement-ready seams

Owner is responsible for selecting/providing final audio files. Engineering responsibility is to ensure every showcase interaction has a clear event hook and replaceable stream slot without duplicate playback.

### Current audit

| Showcase | Current code/audio seam | Capture action |
|---|---|---|
| Pistol | `CursorGunComponent` has shot/reload events; no dedicated gun audio component is present in `src/Tools` | Verify current composition hook; add replacement-ready shot/reload/dry-fire streams if not already elsewhere; support rapid-fire polyphony |
| Grenade | `GrenadeAudioComponent`: provisional synthesized `Boom` + `Thud` | Keep events/counters, replace synthesis path with authored stream slots when owner files arrive; consider fuse cue hook |
| Baseball Bat | `SwingAudioComponent`: synthesized charge start/complete, swing, home-run impact | Keep event semantics; make four cues replaceable; owner replaces mediocre clips |
| Boxing Glove | shared contact/impact path; no dedicated critical-head cue in current tool-specific code | Add critical-head audio event/slot; normal hits may continue shared impact cue |
| Paint Buddy | no final owner-supplied paint cue yet | Add stroke/dab audio seam; owner supplies sound; avoid one sound per raw mouse sample |
| Buddy Studio | existing `UiFeedbackAudioBootstrap` handles standard UI feedback | Audit Buy/Equip/Save; avoid duplicate generic + custom cue |
| Work Mode | UI/exit feedback exists; reward payout lacks strong capture feedback | Add milestone reward cue slot/event; respect Mute While Working |

### Audio implementation rules

- route gameplay cues through `AudioMix.Sfx`, UI through UI bus;
- use `max_polyphony` or a small player pool for fast pistol shots so new shots do not truncate every prior transient;
- optional 2D positioning for world explosions/impacts only if it matches existing mix conventions;
- never change global bus volume from a tool component;
- retain counters/events as deterministic test oracles;
- missing owner asset must degrade to current provisional cue or silence, never break gameplay.

---

## CAP-10 — Capture acceptance gate

### Automated

At minimum before owner capture:

1. `dotnet build DesktopBuddy.sln -c Debug`
2. domain tests
3. relevant gun/projectile scenario(s)
4. grenade scenario with simultaneous grenades
5. bat/home-run scenario
6. glove critical-head scenario
7. Paint Buddy focused validator
8. Buddy Studio validator
9. Work Mode validator
10. quick validation suite

Use the repository's documented Godot resolver/fixed-120-Hz conventions. Do not weaken existing oracles just because feel values changed; update expected authored values where the owner deliberately changed them.

### Manual owner capture checklist

Clean normal game launch, capture settings = Modern UI Motion on, Reduced Motion off, Reduced Particles off unless testing accessibility.

**Pistol**
- fire whole magazine quickly;
- bullet travel visibly faster;
- recoil feels heavier but controllable;
- muzzle flash obvious;
- casings visible;
- smoke appears only after burst/spam;
- hits read strongly on Buddy.

**Grab**
- regression-only: grab/stretch/throw still feels accepted.

**Bat**
- arrow clearly predicts swing direction;
- low charge remains controllable;
- full charge launches Buddy substantially farther;
- home-run punctuation still releases correctly.

**Grenade**
- click several times -> several grenades remain live;
- fuses readable independently;
- explosions feel larger/heavier;
- smoke/debris remain cosmetic and bounded;
- overlapping explosions do not stall the game.

**Glove**
- silhouette is unmistakably a boxing glove;
- normal punches still feel familiar;
- hard head punch gets short extra punctuation, shorter than max bat hit.

**Paint Buddy**
- paint droplets visibly reinforce strokes without obscuring artwork;
- no lag increase at normal/max brush use;
- Show Limbs stretches visible neck connector as well as arms/legs;
- save/use/reopen unchanged.

**Buddy Studio**
- zoom/category framing transitions are fast/smooth;
- repeated zoom input stays responsive;
- selected vs owned vs equipped vs unowned/price is obvious in a screenshot;
- authentic/Reduced Motion mode snaps cleanly.

**Work Mode**
- current actions remain clear;
- passive earnings/reward moment is visible without leaving Work Mode;
- reward does not interrupt typing/click capture;
- exit summary still correct.

**UI**
- modern motion reads as polish, not a web-app skin over Win98;
- buttons still depress immediately like Win98;
- no animation steals input/focus;
- authentic toggle returns rigid behavior.

---

# 7. Recommended execution order for one agent

The deadline order is intentional:

```text
CAP-0 baseline
  -> CAP-1 pistol
  -> CAP-3 bat
  -> CAP-4 glove
  -> CAP-2 grenade (largest structural risk)
  -> CAP-5 Paint Buddy
  -> CAP-6 Buddy Studio
  -> CAP-7 Work Mode
  -> CAP-8 shared Modern Win98 motion
  -> CAP-9 SFX hook/audit cleanup
  -> CAP-10 full capture gate
```

Why grenade is after the smaller weapon wins: pistol/bat/glove provide high-value capture polish quickly, while additive grenade spawning changes object-lifetime assumptions and deserves an isolated verification block.

Buddy Studio should land before the broad Win98 motion helper is applied everywhere so its camera tween can establish the motion policy without coupling native window behavior to generic UI animation.

---

# 8. Commit discipline

Prefer small reviewable commits:

```text
1. docs: capture-polish plan
2. pistol: cadence/recoil/casing/smoke
3. bat: full-charge feel + direction guide
4. glove: visual + critical head punctuation
5. grenade: additive spawn + independent fuse state
6. grenade: explosion presentation polish
7. paint: droplet FX + expanded neck
8. studio: smooth view + commerce hierarchy
9. work: live earnings/reward feedback
10. ui: modern Win98 motion preference/helpers
11. audio: showcase replacement-ready seams
12. tests/docs: capture closure
```

Do not combine the multi-grenade lifetime refactor with unrelated UI changes.

---

# 9. Deferred Steam Demo polish after capture

Carry forward explicitly:

- tools become droppable/pick-up-able world objects;
- double-click world tool to re-equip to cursor;
- improved throw/trajectory UX;
- broader tool/model pass: food, drink, baseball, Nerf and other weak assets;
- first-session guided onboarding;
- economy rebalance so purchases require meaningful saving while active grinding remains viable;
- character slots: first three free, later slots purchasable without fixed upper limit;
- default-only free Buddy Studio content, non-default paid;
- all paint tools remain free;
- broader Work session/lifetime milestone set and Steam achievement mapping;
- Buddy happiness readability and richer environmental curiosity/favorite-color behavior;
- Room Decorator demo-scope/hide decision;
- key rebinding/colorblind/final accessibility audit;
- final release performance, soak, DPI and multi-monitor gates.

This branch may expose seams that make those later tasks easier, but it must not absorb them before the Sunday capture deadline.