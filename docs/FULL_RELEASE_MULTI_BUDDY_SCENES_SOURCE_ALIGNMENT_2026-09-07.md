# Desktop Buddy — Full Release Multi-Buddy Scenes Source Alignment

Status: **owner-approved direction / source alignment for Full Release planning**  
Recorded: 2026-09-07  
Planning branch: `plan/full-release-systemic-sandbox`  
Audited base: `main` at `3966648b7f9fb8c6a70f4cf6f8e586b9998a2641`  
Scope: **Full Steam game only**

This document records and source-aligns the owner's decision to raise **multiple simultaneous Buddies** and **fast-switchable room Scenes** in priority.

It supplements `docs/FULL_RELEASE_SYSTEMIC_SANDBOX_CODEBASE_AUDIT_2026-09-07.md` and `docs/FULL_RELEASE_EXPANSION_ROADMAP.md`. Where those documents defer multiple Buddies or place multiple rooms after the first systemic-sandbox slice, this document supersedes them for Full Release task order.

The Steam Demo and itch.io scope are unchanged.

---

## 1. Owner decision

The Full Release should support a more roleplay-friendly setup in which the player can:

- place **multiple Buddies they have created** in the same active room;
- choose which created Buddies belong to a room/scene;
- maintain several named room/scene setups with different backgrounds, decorations and Buddy rosters;
- switch between those setups quickly through a Win98-style tab/scene strip;
- return to a scene without rebuilding its cast or room setup.

**Buddy-to-Buddy social behavior is not part of the initial scope.**

The first implementation does not add:

- conversations between Buddies;
- friendships/rivalries/relationships;
- Buddy awareness of another Buddy;
- coordinated activities;
- fighting between Buddies;
- Buddy-to-Buddy care actions;
- Buddy-to-Buddy object sharing;
- multiplayer/networking.

Those may be considered later once multiple independent Buddy actors are stable.

---

# 2. Product model: Scene, not just Room

The existing Full Release roadmap already approves multiple local room profiles. The new direction should generalize that concept into a **Scene** instead of creating a second competing profile system.

A Scene is the player's complete roleplay setup:

```text
Scene
  Name
  Room/environment state
    painted background
    wallpaper
    placed decorations
    later: systemic-sandbox objects / contraptions
  Buddy roster
    Buddy A placement
    Buddy B placement
    Buddy C placement
  Scene-local presentation metadata
```

The player-facing mental model is therefore:

```text
[ Bedroom ] [ Lab ] [ Rooftop ] [ + ]
```

rather than separate systems called "room profiles" and "scene profiles".

A Scene tab selects which complete setup is active.

### Important runtime rule

Only **one Scene is live and simulating at a time**.

Inactive Scenes are persisted documents, not hidden physics worlds running in the background.

This provides:

- fast switching;
- predictable performance;
- no invisible hunger/damage/physics changes in another room;
- no need to maintain several complete Godot physics worlds;
- a clean future home for scene-local contraptions.

When a Scene is inactive, its simulation clock is paused.

---

# 3. Current-code audit

## 3.1 Multiple BuddyRoot instances are already proven

`src/Laboratory/DualProfileLab.cs` already runs two independent `BuddyRoot` compositions side-by-side under one routed fixed tick.

It proves several important points:

- two six-body Buddy rigs can exist in the same 2D physics world;
- each Buddy can run its own autonomy and active drive;
- one pointer/grab system can select which Buddy is currently manipulated;
- the owning root can route one fixed tick across both Buddy actors.

This is a development-only lab and is not production-ready multi-Buddy support, but it removes the largest physics uncertainty.

## 3.2 The reusable Buddy actor already exists

`scenes/buddy/puppet.tscn` is already a reusable six-body Buddy composition. The current shipping `scenes/sandbox.tscn` instances it once as `Buddy`.

The Full Release work should keep the reusable actor concept and stop treating the shipping scene's one Buddy instance as a permanent architectural invariant.

## 3.3 Character selection is globally singular today

`CharacterSelectionState` currently persists exactly one:

```text
ActiveCharacterId
```

`CharacterSelectionRuntime` and `CharacterSelectionCoordinator` compile that character and apply it to exactly one `BuddyVisualRigView`.

This machinery is useful but must be generalized. A Scene needs several character references applied to several Buddy visual rigs at once.

Do not create one global `CharacterSelectionState` per spawned Buddy as a shortcut. The existing state means "the single active character for this run" and is tied into the current save coordinator.

## 3.4 Environment state is globally singular today

`EnvironmentProgressState` currently owns one:

```text
EnvironmentLayout Layout
```

The existing Full Release roadmap already says this must become multiple named rooms. The new Scene model should perform that migration once rather than first building room profiles and then wrapping them in another Scene layer.

## 3.5 Painted background storage is already rootable

`EnvironmentPaintStore` accepts a resolved root directory and stores the whitelisted room PNG beneath it.

That is a strong fit for Scene-local assets.

Current conceptual path:

```text
user://environment/background.png
```

Full Release Scene path:

```text
user://scenes/<scene-guid>/environment/background.png
```

No PNG format change is required merely to support Scenes.

## 3.6 Persistent gameplay state is currently mixed together

`BuddyProgressState` currently contains both:

### Account/player-wide state

- balance;
- unlocked tools/content;
- selected tool;
- aggregate statistics;
- cumulative time.

### Buddy-specific state

- mood;
- fullness/hunger;
- harmful-content memory;
- generated traits/personality preferences;
- fun/novelty state.

That mixture was correct when one run had exactly one persistent Buddy, but it cannot be copied unchanged into a multi-Buddy product.

If two Buddies shared one `BuddyProgressState`, hurting Buddy A would also change Buddy B's mood/memory. That is unacceptable for the RP direction.

## 3.7 Damage/care is also singular today

`InteractionDamageComponent` is bound to one `BuddyRoot` and one `BuddyProgressState`.

For multi-Buddy production gameplay, each active Buddy needs its own Buddy-specific semantic state and damage/care transient workers while all of them feed the same account-wide economy/unlock ledger.

## 3.8 Buddy-to-Buddy collision is naturally absent today

`CollisionLayers.MaskBuddyParts` deliberately excludes the `BuddyParts` layer.

Therefore Buddy bodies do not collide with Buddy bodies. This already provides a useful first-version behavior:

- Buddies can coexist in one room;
- each still collides with room bounds, loose objects, projectiles and physical tools;
- separate Buddies do not shove, trip or damage one another;
- if their paths cross, they may visually overlap/pass through one another.

That is accepted for the initial no-interaction slice. Deliberate Buddy-to-Buddy collision/avoidance belongs to a later interaction design pass.

---

# 4. Required state split before production multi-Buddy

The existing `BuddyProgressState` should not simply be duplicated wholesale because the wallet/tool inventory must remain shared.

Introduce a separation with these semantics.

## 4.1 Player/account state

Tentative name:

```text
PlayerProgressState
```

Owns:

- balance;
- permanent tool/content ownership;
- selected tool;
- global lifetime statistics;
- cumulative run/work statistics;
- global achievement counters where appropriate.

There remains exactly one of these per save.

`EconomyService` eventually mutates this account state rather than a Buddy's emotional state.

## 4.2 Buddy identity state

Tentative name:

```text
BuddyIdentityState
```

Owns one Buddy's persistent roleplay/personality semantics:

- stable `BuddyIdentityId`;
- character/appearance reference;
- mood;
- fullness/hunger;
- harmful-content memory;
- generated traits/preferences;
- fun/novelty state;
- later structural damage if that feature is promoted to persistence;
- future relationship/social data, but **not in the initial implementation**.

A Buddy's identity is not the same thing as its live physics actor.

## 4.3 Scene placement state

Tentative record:

```text
BuddyPlacement
  PlacementId
  BuddyIdentityId
  CanonicalX
  CanonicalY
```

The placement says where a Buddy appears in one Scene.

Do **not** persist all six limb transforms or velocities. On Scene activation, the Buddy spawns in a safe authored pose whose root/origin is based on the saved placement anchor.

When switching away, the current safe anchor may be updated from the Buddy's current torso/origin position so returning to the Scene feels continuous without serializing unstable ragdoll state.

## 4.4 Appearance references

The existing Character Store remains the source of player-created Buddy appearance documents and paint files.

A Buddy identity may reference:

```text
CharacterId?   // null = built-in/default appearance
```

The character document remains appearance data. The new Buddy identity state owns persistent mood/personality semantics.

This separation allows later features such as:

- changing a Buddy's appearance without creating a new emotional identity;
- duplicating a Character appearance into a different Buddy identity;
- placing the same Buddy identity in different Scenes without losing its mood/history.

For the first version, prevent the **same BuddyIdentityId** from being placed twice in one Scene. If a player wants twins/clones, they can create/duplicate a separate Buddy identity later.

---

# 5. Scene persistence model

Introduce engine-free IDs:

```text
SceneId
BuddyIdentityId
BuddyPlacementId
```

Suggested top-level model:

```text
SceneLibraryState
  ActiveSceneId
  SceneSummary[]

SceneDocument
  SchemaVersion
  SceneId
  Name
  EnvironmentLayout
  BuddyPlacement[]
  later: SandboxDocument reference/data
```

Buddy identity data should be stored independently from a Scene because the same Buddy may appear in another Scene later.

Suggested storage layout:

```text
user://
  progress.json                 // account/global progress
  buddy-identities/
    <buddy-id>.json
  characters/
    ...existing Character Store...
  scenes/
    index.json
    <scene-id>/
      scene.json
      environment/
        background.png
      later:
        sandbox.json
```

This keeps large/mutable Scene graphs out of the account progress JSON and follows the existing project's preference for separately owned/versioned persistence boundaries.

---

# 6. Deterministic migration from the current one-Buddy Demo save

The Steam Demo remains one Buddy / one environment.

When a Demo save is opened by the Full Release for the first time:

1. create one default Scene, tentatively named `Room 1` or `Home`;
2. migrate the current `EnvironmentProgressState.Layout` into that Scene;
3. move/copy the current room painting into the new Scene asset root atomically;
4. create one Buddy identity representing the existing persistent Buddy state;
5. associate the existing `ActiveCharacterId` with that Buddy identity;
6. add one `BuddyPlacement` at the current/default spawn position;
7. make the migrated Scene active;
8. preserve wallet, unlocks, statistics, Work progress and other account data;
9. only mark migration complete after the new Scene/identity files and account schema commit successfully.

Failure must leave the old valid save recoverable.

The Demo never writes this Full Release-only schema back into its own build-specific behavior.

---

# 7. Production runtime architecture

Do not grow `SandboxRoot` into an array of every Buddy-specific component.

Introduce a focused Full Release host.

```text
SandboxRoot
  shared world services
    room boundaries
    pointer
    grab
    tools/guns/grenades/fire
    loose objects
    account economy
  SceneRuntimeHost
    ActiveScene
    BuddyActorRuntime A
    BuddyActorRuntime B
    BuddyActorRuntime C
```

## 7.1 BuddyActorRuntime

One `BuddyActorRuntime` should own/wrap the pieces that are semantically per Buddy, for example:

- one `BuddyRoot` / `puppet.tscn` instance;
- one visual presenter / `BuddyVisualRigView`;
- one compiled Character appearance binding;
- one paint-texture bridge;
- one Buddy identity state binding;
- per-Buddy damage/knockout state;
- per-Buddy reaction/face/pose/look-at presentation;
- per-Buddy object interaction state;
- per-Buddy gore/scorch presentation where appropriate.

Exactly which existing components move under the actor should be determined during the production-composition task, but the invariant is:

> if a component reads or mutates one specific Buddy, it must not remain a globally singular runtime service.

Global player tools and room services remain shared.

## 7.2 One routed fixed tick remains authoritative

The owning normal-play root still has one `_PhysicsProcess` entry.

Conceptual order:

```text
shared pointer/input
shared room/objects/tools
for each active BuddyActorRuntime in stable Scene order:
    prepare per-Buddy grab/tool context
    Buddy.PhysicsTick(...)
    Buddy damage/care tick
    Buddy presentation snapshots
shared projectiles/grenades/fire
shared scene services
```

No Buddy actor registers an independent gameplay `_PhysicsProcess` just because there are several of them.

## 7.3 Stable ordering

Scene Buddy actors must tick in deterministic stable order, such as `BuddyPlacementId` order, rather than scene-tree discovery order.

This prevents save/load or tab-switch ordering from perturbing deterministic behavior unnecessarily.

---

# 8. Shared input and Buddy focus

The player should be able to interact with any Buddy in the active Scene.

Initial rules:

- clicking/grabbing a body resolves the owning `BuddyActorRuntime`;
- only the grabbed Buddy receives `grabbedPart` / grab-resistance context that tick;
- other Buddies continue ordinary autonomy;
- Rope Suspender checks suspension against the correct Buddy's parts;
- projectiles/tools may physically hit any active Buddy;
- damage/reaction events route to the Buddy that owns the struck part;
- the player has one optional **focused Buddy** for UI commands that need a subject.

Focus is for UI, not for deciding whether physics exists.

Examples of focus-dependent actions:

```text
Edit Buddy...
Remove from Scene
Set as Work Buddy
Reset Buddy
View Status
```

Ordinary physical tools should not require focus before they can hit another Buddy.

---

# 9. Initial multi-Buddy behavioral scope

Every active Buddy in the initial production slice should retain the familiar independent baseline where technically applicable:

- standing/recovery;
- autonomous walking/jumping;
- grab resistance;
- tool hit reactions;
- knockout/recovery;
- care/mood/hunger state;
- character appearance + paint;
- room-boundary containment.

Explicitly excluded:

- seeing/avoiding other Buddies;
- intentional Buddy-to-Buddy collision;
- talking;
- touching/hugging/fighting;
- relationship scores;
- shared games;
- passing objects to one another;
- synchronized animation.

A Buddy may therefore walk through another Buddy in the initial version. This is preferable to accidentally creating emergent Buddy-vs-Buddy combat before that behavior has a design.

---

# 10. Win98 Scene tabs

## 10.1 User-facing strip

Add a compact Win98-styled Scene strip to normal Play Mode.

Conceptually:

```text
[ Home ] [ Garage ] [ Lab ] [ + ]
```

Each tab represents a named Scene.

Required initial actions:

- click tab to switch;
- `+` creates a new Scene;
- rename Scene;
- duplicate Scene;
- delete Scene with confirmation;
- reorder tabs if inexpensive; otherwise defer drag reordering.

Do not use a browser aesthetic. Use the project's existing Win98 theme/chrome.

## 10.2 Fast switching behavior

Clicking another Scene tab should:

1. reject/finish any incompatible open modal editor rather than switching underneath it;
2. capture safe placement anchors for the active Scene's Buddies;
3. persist dirty committed Scene state;
4. tear down current Buddy actor runtimes and scene-specific physical content;
5. switch background/decorations;
6. instantiate the target Scene's Buddy actors;
7. apply cached/compiled appearances and paint;
8. restore normal input;
9. mark the target tab active.

Normal small Scenes should not require a loading-screen workflow. Cache character compilation/paint resources where profiling shows it is worthwhile.

Do not promise a specific millisecond switch target until measured on the launch hardware matrix.

## 10.3 Inactive Scene semantics

Inactive Scenes:

- do not run physics;
- do not walk;
- do not become hungry;
- do not receive damage;
- do not accrue passive per-Buddy simulation events;
- do not keep projectiles/grenades alive.

They resume from persisted semantic state and safe placement anchors when activated.

---

# 11. Scene manager / cast editing

The player needs a simple way to compose the active cast.

Initial UI can live under a Full Release-only command such as:

```text
Scene
  Manage Scene...
  Add Buddy...
  Remove Focused Buddy
  Rename Scene...
  Duplicate Scene...
```

`Add Buddy...` browses the existing local Character/Buddy library and places the chosen Buddy into the active Scene.

Initial placement flow:

1. choose Buddy;
2. preview at pointer;
3. click room position;
4. spawn into a safe pose at that anchor.

Keep the first version simple:

- position only;
- no per-Buddy scale;
- no manually authored limb pose persistence;
- no spawn scripts;
- no social-role metadata.

---

# 12. Initial active-Buddy target

For the first vertical slice, use an engineering target of:

```text
4 simultaneously active Buddies
```

That is not yet a permanent product cap.

Four Buddies means 24 Buddy rigid bodies before loose objects/tools, which is a useful first performance envelope and fits the visual size of the normal room better than immediately promising an arbitrary cast size.

After the architecture is stable, benchmark 6 and 8 active Buddies and choose the final supported cap based on:

- 120 Hz simulation stability;
- presentation cost;
- character paint GPU memory;
- tool/projectile load;
- systemic-sandbox object load;
- common minimum-window sizes.

Do not expose an unlimited cast count.

---

# 13. Character paint / GPU budget

The current active GPU paint budget was designed around one Buddy.

Multi-Buddy therefore requires an explicit budget pass before promising four fully painted Buddies at current texture residency costs.

Preferred approach:

- share immutable/cached textures when two Scene placements reference the same Character paint;
- load only the active Scene's Buddy paint;
- release inactive Scene textures;
- retain existing 512×512 trusted paint surface rules;
- benchmark actual GPU usage before setting the cast cap.

Do not lower paint resolution silently just to hit the first multi-Buddy milestone.

---

# 14. Work Mode boundary

Initial Full Release multi-Buddy Work Mode behavior stays simple.

A Scene has one **focused / selected Work Buddy** when entering Work Mode.

On entry:

- only that Buddy appears in the existing Work companion presentation;
- the rest of the active Scene is suspended/hidden with normal Play Mode;
- Scene tabs are not separate live desktops during Work Mode;
- exiting Work Mode returns to the same active Scene and cast.

Multi-Buddy Work Mode, several typing Buddies, or assigning different Buddies to different monitors is outside the initial scope.

---

# 15. Systemic-sandbox integration

This feature now comes **before** the systemic-sandbox persistence layer.

The systemic sandbox should be Scene-owned from its first durable implementation.

Future Scene:

```text
Scene
  environment
  Buddy placements
  sandbox entities
  constraints
  devices/wires
```

Therefore the revised implementation order is:

```text
SCENE foundation
    ↓
multi-Buddy runtime
    ↓
Scene tabs / switching
    ↓
Scene-local room persistence
    ↓
Systemic Sandbox VS1
    ↓
Scene-local contraption persistence
```

Do not implement a new `active-room sandbox.json` at a global root and migrate it immediately afterwards.

---

# 16. Revised Full Release vertical-slice priority

## SCENE-0 — source alignment + account/Buddy state seam

- add Full Release-only Scene scope flag;
- define Scene/Buddy IDs and engine-free documents;
- split or facade-migrate account state vs Buddy-specific semantic state;
- preserve old one-Buddy APIs while migrating tests;
- add deterministic Demo-to-Full migration tests.

Exit gate:

- one legacy Buddy behaves identically;
- wallet/unlocks remain global;
- two Buddy identities can hold different mood/hunger/memory without duplicating the wallet.

## SCENE-1 — production Buddy actor host

- create `BuddyActorRuntime`/equivalent focused composition;
- instantiate two `puppet.tscn` actors in normal Play Mode;
- give each its own presentation/character binding;
- route one fixed tick across both;
- shared pointer/grab can resolve either actor;
- shared tools/projectiles can strike either actor;
- each actor has independent damage/reaction state;
- no Buddy-to-Buddy behavior/collision.

Exit gate:

- two visibly different created Buddies can coexist and independently react to the player.

## SCENE-2 — cast editor

- Add Buddy;
- remove focused Buddy;
- place at room anchor;
- persist roster;
- missing/deleted character reference degrades safely;
- initial 4-Buddy engineering cap.

## SCENE-3 — Scene library + Win98 tabs

- named Scenes;
- create;
- rename;
- duplicate;
- delete;
- active Scene selection;
- Win98 tab/scene strip;
- switch without restarting the application.

## SCENE-4 — environment migration

- move the current single `EnvironmentLayout` into the Scene document model;
- make painted background Scene-rooted;
- make decoration presentation/editor target the active Scene;
- migrate Demo single room deterministically.

## SCENE-5 — switch resilience / polish

- save-on-switch semantics;
- texture/character cache;
- modal-editor guard behavior;
- Work Mode focus rule;
- reset-progress semantics;
- character-delete handling;
- soak switching among several Scenes;
- 4-Buddy performance gate;
- DPI/window resizing/tab overflow polish.

## Then resume Systemic Sandbox VS1

The previously audited systemic-sandbox slice follows, but all room state is Scene-owned from the outset.

---

# 17. Things deliberately deferred after the first Scene slice

- Buddy-to-Buddy AI;
- collision/avoidance between Buddies;
- conversations;
- friendships/rivalries;
- relationship UI;
- group activities;
- Buddy-vs-Buddy combat;
- multi-Buddy Work Mode;
- per-Buddy authored poses saved as full ragdoll transforms;
- background simulation for inactive Scenes;
- several Scenes visible simultaneously;
- arbitrary cast size;
- networking/multiplayer;
- Workshop sharing of whole Scenes until the local format is stable.

---

# 18. Initial acceptance journey

The first complete multi-Buddy Scene program is accepted when a Full Release build can perform this journey:

1. launch a migrated one-Buddy save;
2. observe a default Scene containing the old room and old active Buddy;
3. create or select a second locally made Buddy;
4. add that Buddy to the same Scene;
5. place both Buddies at different room positions;
6. both independently walk/recover/react;
7. grab Buddy A without Buddy B receiving grab context;
8. strike Buddy B and observe Buddy B's reaction/state changing independently of Buddy A;
9. create a second Scene;
10. give it a different painted background / room layout;
11. place a different Buddy roster in it;
12. switch between the two Win98 Scene tabs without restarting;
13. return to the first Scene and recover its room/cast/placement anchors;
14. restart the game and recover both Scenes and their Buddy rosters;
15. verify the Steam Demo still has exactly its existing single-room/single-Buddy product scope.

At that point Desktop Buddy has a strong RP foundation without requiring Buddy-to-Buddy social AI, and the systemic-sandbox work can build on top of the correct multi-Scene persistence model.