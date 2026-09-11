# Desktop Buddy — Future Buddy Agency, Personality, Large Scenes, and Collaborative Play Plan

Status: **OWNER-APPROVED FUTURE DIRECTION / PLANNING ONLY**  
Recorded: **2026-09-12**  
Planning branch: `plan/future-buddy-agency-social-scenes`  
Base: `main` at `46ebee5136982062505b5b5542e83d8a0e9c95c6`  
Scope: **future work after the currently approved release program unless a later owner decision explicitly promotes a slice earlier**

This document records the owner's intended long-term direction for making Buddies substantially more autonomous, expressive, controllable, social, and useful inside larger player-authored Scenes.

It does **not** replace `docs/DESKTOP_BUDDY_MASTER_RELEASE_PLAN_2026-09-08.md` for the current Initial Demo / Next Fest Demo / Full Release program. In particular, current-release exclusions around Buddy-to-Buddy social AI and real-time multiplayer remain in force unless the owner explicitly promotes a slice into that release plan later.

The purpose of this document is to prevent these future systems from being implemented as disconnected features. They share important foundations: Buddy identity, personality, behavior arbitration, object use, large Scene navigation, expressive feedback, Workshop-safe Scene documents, and eventually network-safe semantic commands.

---

# 1. Owner direction recorded here

The future game should move beyond passive ragdolls toward **Buddies that behave like small characters living inside the player's sandbox**.

The owner wants the following long-term systems:

1. **More reactionary Buddies / Buddy-to-Buddy awareness**
   - Buddies notice other Buddies in the active Scene.
   - They can become scared, flee, run, hide, defend themselves, pick up useful objects or weapons, threaten, attack, help, socialize, or otherwise react to what is happening.
   - Their choices should be strongly influenced by their personality rather than every Buddy responding identically.

2. **Configurable personality system**
   - The player can configure personality through approachable sliders/buttons in a Sims-like spirit.
   - Personality must have visible behavioral consequences rather than being cosmetic text.
   - Example owner-confirmed consequences:
     - a more violent/aggressive Buddy is more willing to pick up a weapon, threaten another Buddy, or attack;
     - a highly social Buddy seeks conversations and spends more time interacting with other Buddies.

3. **Expressive Buddy voice/sound language**
   - Buddies should communicate emotion through short, original, speech-like game sounds in the broad tradition of text-driven games such as Undertale.
   - The goal is expressive non-verbal character voice, not realistic speech or copied audio.
   - Different emotions/personality states should visibly and audibly read differently.

4. **Direct Buddy control**
   - The player can take direct control of a selected Buddy.
   - WASD-style movement is the intended baseline.
   - The mouse controls/aims the Buddy's hand or held item in a side-scrolling-action style.
   - Buddies can physically pick up and use compatible objects/weapons while directly controlled.
   - The same underlying held-item/use system should also be usable by AI, rather than creating separate fake player-only weapon behavior.

5. **Larger / scrollable Scenes**
   - Scenes should eventually grow beyond one fixed desktop-sized room.
   - The player should be able to move/scroll through larger levels and construct more substantial spaces.
   - Larger Scenes must still use the same declarative Scene model rather than becoming arbitrary Godot scene files.
   - Players should be able to save and export complete playable Scenes to Workshop for other players.

6. **Very-future multiplayer / collaborative Scene play**
   - Another player may eventually connect to the host's Scene.
   - Both players can see one another's mouse/cursor.
   - Both can manipulate and play in the same Scene, including building together.
   - Initial multiplayer intent is collaborative sandbox play, not competitive matchmaking.

These points are product direction. The architecture and sequencing below are implementation guidance designed to make those product goals achievable without destabilizing the current game.

---

# 2. Market rationale captured from the 2026-09-11 research pass

The preceding market review of *People Playground* and *Mutilate-a-Doll 2* communities found a consistent pattern: popular extensions are not limited to adding more weapons. Players repeatedly extend these games toward **more alive characters, easier direct control, stronger reactions/emotions, poses/actions, repair/medical consequences, larger scenario construction, and multiplayer/social play**.

Useful public reference points for future product comparison include:

- People Playground — Active Humans: `https://steamcommunity.com/sharedfiles/filedetails/?id=2163278857`
- People Playground — Quick Right Click Options: `https://steamcommunity.com/sharedfiles/filedetails/?id=2516131949`
- People Playground — Human Dynamic Emotions 2: `https://steamcommunity.com/sharedfiles/filedetails/?id=2856823986`
- People Playground — Extra Poses: `https://steamcommunity.com/sharedfiles/filedetails/?id=3736408475`
- People Playground Workshop trending/browse surface: `https://steamcommunity.com/workshop/browse/?appid=1118200`
- Mutilate-a-Doll 2 Workshop browse surface: `https://steamcommunity.com/workshop/browse/?appid=665370`

The strategic opportunity for Desktop Buddy is therefore not to compete on raw weapon count. The stronger differentiated direction is:

```text
living-character sandbox
+ physics toybox
+ dollhouse / room / character customization
+ approachable creation and sharing
```

That direction aligns naturally with the already planned Multi-Buddy + Scene architecture.

---

# 3. Existing foundations on `main` — extend these, do not replace them

This future work has several useful foundations already present or planned.

## 3.1 Behavior arbitration already exists

`BehaviorArbiter` / `BehaviorArbiterModel` already provide the single behavioral selection seam for a Buddy.

Future social/threat/direct-control systems must continue the same rule:

> behavior systems produce semantic intents; only the approved active-drive / object-use / activity components turn those intents into movement or physics.

Do not create new AI scripts that independently write body transforms or velocities.

## 3.2 Persistent personality already exists in small form

`BuddyTraits` currently stores persistent per-Buddy-style data such as obstacle-hop propensity and fun preferences.

That type is the seed of the future personality system, but it is not large enough to become the permanent public/personality schema unchanged.

The future system should migrate from the current compact traits record into a versioned personality profile while preserving old values exactly during migration.

## 3.3 Buddies already physically interact with carried objects

`ObjectInteractionComponent` and the related domain model already support catch/hold/carry/consume/toss flows using the Buddy's physical hands.

That is valuable because future AI weapon use and Direct Control should reuse genuine Buddy-held objects rather than spawning a second visual-only weapon in a hand.

## 3.4 Multi-Buddy and Scene architecture are already planned

`docs/FULL_RELEASE_MULTI_BUDDY_SCENES_SOURCE_ALIGNMENT_2026-09-07.md` already establishes:

- independent Buddy identity state;
- `BuddyActorRuntime`-style live actor composition;
- a Scene-owned Buddy roster;
- only one live Scene at a time;
- stable per-Buddy runtime ordering;
- a future seam for social/relationship data.

This plan depends on that separation.

Do not implement social AI while the project still treats all Buddies as one shared progress/personality object.

## 3.5 Expressive text/audio architecture already exists

The expressive tutorial system already provides:

- punctuation-aware typewriter presentation;
- synthetic speech-like chirps;
- a pooled UI audio path;
- `SpeakingChanged` presentation signals;
- semantic presentation roles.

Future Buddy speech should reuse the architectural ideas and pooled audio infrastructure, but live Buddy speech needs its own gameplay-facing semantic layer. Tutorial UI must not become the owner of in-world Buddy conversation.

## 3.6 Workshop is intentionally declarative and hostile-data-safe

All future Scene sharing continues the existing security model:

- Workshop content is hostile data;
- no downloaded `.tscn`, `.tres`, `.res`, script, DLL, native library, shader, executable PCK, arbitrary mesh, or arbitrary resource path;
- import into a validated local owned copy;
- no silent auto-activation;
- explicit schema versioning and bounded values.

Large Scenes and multiplayer must not weaken this boundary.

---

# 4. Product principles for the future systems

## 4.1 Personality must change decisions, not just animations

A personality slider only earns its place if it alters observable behavior.

Examples:

```text
Aggressive Buddy
  sees armed hostile Buddy
  → more likely to posture / arm itself / fight

Non-aggressive Buddy
  sees same threat
  → more likely to flee / hide / surrender / keep distance

Highly social Buddy
  idle near another Buddy
  → more likely to approach / greet / chat / initiate shared activity

Low-social Buddy
  idle near another Buddy
  → more likely to keep doing its own activity
```

The player should learn personality by watching the Buddy, not by reading a hidden stat sheet.

## 4.2 AI and Direct Control must use the same capabilities

If a Buddy can fire a pistol under Direct Control, an AI Buddy should eventually fire that same pistol through the same semantic use API.

Likewise:

- pickup;
- drop;
- aim;
- primary use;
- secondary use where supported;
- reload/recover where supported;
- melee swing;
- throw;
- consume;
- activate device.

This prevents four incompatible implementations for cursor tools, Buddy AI, Direct Control, and future multiplayer.

## 4.3 Buddies remain physics characters, not animation puppets

The six-body active puppet remains authoritative.

Future running, hiding, aiming, carrying, fighting, social gestures, and Direct Control should submit bounded goals into the existing physical actuation system.

Do not replace the Buddy with a conventional CharacterBody controller during Direct Control.

## 4.4 Decisions run slower than physics

The game can keep physics at the established routed fixed rate while expensive perception/decision work runs at a lower semantic cadence.

Conceptual flow:

```text
120 Hz physics/actuation
    ↑ consumes latest stable intent

10–20 Hz perception + tactical/utility decisions
    ↑ consumes bounded world snapshot

low-frequency social planning / idle choices
    ↑ consumes relationships, personality and scene context
```

Exact rates are engineering tuning, not product promises.

## 4.5 Personality, emotion, relationship and current goal are separate concepts

Do not collapse all character state into one "mood" number.

Recommended separation:

```text
Personality
  mostly stable player-configured tendencies

Needs / emotion
  short- or medium-term current state

Relationship
  persistent Buddy-A ↔ Buddy-B history, if/when added

Memory
  remembered harms/help/events

Current goal / action
  what the Buddy is doing right now
```

A friendly Buddy can currently be angry. A fearful Buddy can still defend itself when cornered. Two normally social Buddies may become rivals because of history.

---

# 5. Shared future architecture: perception → decision → intent → actuation

Introduce a richer, engine-light AI pipeline instead of adding special cases inside `BehaviorArbiter` forever.

Suggested conceptual split:

```text
ScenePerceptionService
  builds bounded semantic observations

BuddyPerceptionSnapshot
  nearby buddies
  nearby usable items
  immediate threats
  hazards
  cover/hide candidates
  conversation candidates
  current owner/player interaction
  navigation/path hints

BuddyDecisionModel
  personality + mood + memory + relationships + observations
  → scored candidate goals

BuddyGoal
  Flee
  Hide
  Defend
  ArmSelf
  Threaten
  Attack
  ApproachBuddy
  Chat
  HelpBuddy
  Explore
  Eat
  Play
  Idle
  ...

BehaviorArbiter
  resolves current high-level goal against existing hard priorities
  → locomotion intent
  → hand/object intent
  → presentation/emotion intent

Existing actuation/components
  ActiveDrive
  ObjectInteraction
  Activity
  held-item use
  face/look-at
```

Important invariant:

> a decision model decides **what** the Buddy wants; the physical components decide **how** to achieve it safely inside the approved puppet constraints.

## 5.1 Bounded perception

Do not scan the entire Scene for every Buddy every tick.

Use a Scene-owned spatial query/index that can return bounded nearby candidates by semantic category.

Examples:

```text
NearbyBuddies(max N, radius R)
NearbyUsableItems(max N, radius R)
ImmediateHazards(max N, radius R)
HideCandidates(max N, radius R)
```

The specific data structure can be chosen after profiling; the public semantic API should not expose Godot scene-tree traversal to domain code.

## 5.2 Threat model

Threats should be semantic, not merely "another Buddy exists nearby".

Potential threat evidence includes:

- another Buddy aiming a compatible weapon;
- recent damage caused by another Buddy;
- a fast incoming dangerous object;
- nearby fire/explosion/hazard;
- the player actively attacking this Buddy;
- a threatening social action;
- an armed Buddy with hostile intent.

Threat evaluation should produce a bounded threat level + source identity + evidence flags. Personality then changes the selected response.

## 5.3 Utility/score-based goal selection

A utility-style model is preferable to a giant fixed decision tree because many systems should influence the same choice.

Conceptual example:

```text
FightScore =
    ThreatIntensity
  + AggressionWeight
  + AngerWeight
  + WeaponConfidence
  - InjuryPenalty
  - DistancePenalty

FleeScore =
    ThreatIntensity
  + FearWeight
  + InjuryWeight
  + SelfPreservationWeight
  - AggressionWeight

SocializeScore =
    SociabilityWeight
  + RelationshipAffinity
  + Boredom
  - ImmediateThreat
  - CurrentNeedUrgency
```

Do not expose these raw formulas to Workshop content. Tunings remain trusted first-party data.

## 5.4 Commitment/hysteresis

Buddies must not flip between "fight" and "flee" every few frames.

Extend the existing behavior commitment/hysteresis idea:

- choose a goal;
- commit for a short bounded window;
- immediately allow genuinely higher-priority danger/player overrides;
- re-evaluate when evidence meaningfully changes.

---

# 6. Future System A — reactionary and social Buddies

## 6.1 First target: reactive awareness before full social simulation

Implement in layers.

### Layer A — awareness

A Buddy can identify:

- other Buddies;
- which Buddy caused a recent event;
- whether another Buddy is holding/aiming a dangerous item;
- whether another Buddy is approaching quickly;
- nearby hazards;
- safe/unsafe directions.

No conversation or relationship system is required yet.

### Layer B — threat responses

Add clear actions:

- look toward threat;
- startled reaction;
- step/back away;
- run;
- flee to distance;
- hide behind/in an authored hide point when available;
- surrender/cower if no escape/weapon response is chosen;
- pick up a nearby compatible defensive item;
- threaten/aim;
- attack.

The personality system chooses among these rather than hard-coding one response.

### Layer C — peaceful Buddy-to-Buddy interaction

Add simple social goals:

- notice/greet;
- approach;
- face each other;
- short chat exchange;
- laugh / react;
- leave conversation;
- idle near another Buddy;
- simple shared activity where an existing object supports it.

The first social slice should be deliberately small and expressive rather than attempting a full Sims relationship simulator immediately.

### Layer D — relationships and history

Only after social actions are stable, consider persistent pairwise relationship state.

Tentative engine-free model:

```text
BuddyRelationshipState
  BuddyAId
  BuddyBId
  Familiarity
  Affinity
  Fear
  Grievance
  LastMeaningfulInteraction
```

This is a future seam, not an approved initial data schema. Exact axes require an owner gate before implementation.

## 6.2 Buddy-to-Buddy damage attribution

Every damaging event must identify its semantic source where possible:

```text
player
BuddyIdentityId
world/environment
unknown
```

This enables:

- retaliation;
- fear of a specific Buddy;
- relationship effects later;
- correct threat persistence;
- meaningful social reactions.

Do not derive social responsibility merely from collision ownership after the fact.

## 6.3 Hiding

Large Scenes make hiding more useful.

Support trusted authored hide candidates such as:

- behind a large furniture/structure volume;
- inside an explicit hiding place;
- around a corner / out of direct line-of-threat;
- authored Scene cover marker.

The Buddy asks the navigation service for a safe reachable candidate; the AI does not teleport into hiding.

---

# 7. Future System B — configurable personality

## 7.1 Personality profile evolution

Migrate from today's narrow `BuddyTraits` into a versioned, engine-free profile.

Conceptual shape:

```text
BuddyPersonalityProfile
  SchemaVersion
  Aggression
  Sociability
  ...future approved axes...
  ExistingObstacleHopPropensity
  ExistingFunPreferences
  BehavioralToggles[]
```

All scalar axes should use deterministic bounded integer buckets, for example `0..100`, unless a future implementation audit provides a stronger reason otherwise.

This maintains exact persistence and simple migration.

## 7.2 Owner-confirmed initial personality axes

The two axes explicitly confirmed in this owner direction are:

### Aggression / violence tendency

Higher values increase willingness to:

- escalate threats;
- seek/keep a weapon when threatened;
- intimidate;
- retaliate;
- initiate harmful action under appropriate circumstances.

Lower values increase preference for:

- avoidance;
- de-escalation;
- fleeing/hiding;
- dropping conflict after danger passes.

### Sociability

Higher values increase willingness to:

- approach other Buddies;
- initiate conversation;
- continue conversations longer;
- seek company during idle time;
- join compatible shared activities.

Lower values increase preference for solitary activity and shorter/rarer conversations.

## 7.3 Candidate future axes — not yet owner-locked

The architecture should leave room for axes such as:

- courage / fearfulness;
- empathy / selfishness;
- curiosity / caution;
- playfulness / seriousness;
- patience / impulsiveness.

These are **not automatically approved gameplay requirements by this document**. Add them only after the owner confirms they create sufficiently distinct behavior.

## 7.4 Buttons/toggles

Sliders describe continuous tendency. Buttons/toggles can express strong behavioral rules that are easier for players to understand than another number.

Possible future examples, owner-gated before implementation:

```text
Avoids firearms
Protects friends
Starts fights
Never starts fights
Likes crowds
Prefers solitude
```

Do not let contradictory toggles silently coexist. The editor should either prevent invalid combinations or define deterministic precedence.

## 7.5 Personality UI

The natural home is Buddy Studio / character management, but personality should remain conceptually separate from visual appearance.

Recommended UX:

```text
Buddy Studio
  Appearance
  Personality
```

The personality page should use:

- a small number of clearly named sliders;
- plain-language endpoints;
- tiny behavior examples/previews;
- reset to default;
- optional presets only if they are composed from the same sliders, not hidden special AI classes.

Example display:

```text
Aggression
Peaceful  [====|------]  Confrontational

Sociability
Reserved  [-------|===]  Social
```

Do not expose internal utility weights, thresholds, timers or AI implementation terms.

## 7.6 Personality and identity lifecycle

Personality belongs to `BuddyIdentityState`, not the live Scene placement.

Therefore:

- moving the Buddy to another Scene keeps the same personality;
- changing clothes/paint keeps the same personality;
- deleting a Scene does not delete the Buddy identity;
- duplicating appearance may create a new Buddy identity with either copied or default personality according to an explicit future UX decision.

---

# 8. Future System C — expressive Buddy sound / "voice"

## 8.1 Goal

Create an original non-verbal voice language that lets players understand:

- greeting;
- happiness;
- interest;
- confusion;
- fear;
- pain;
- anger;
- threat/intimidation;
- laughter;
- refusal;
- conversation turn-taking.

It should feel like a stylized game character voice, not like realistic spoken dialogue.

## 8.2 Reuse the existing synthetic/chirp infrastructure

Do not create hundreds of recorded voice lines as the base architecture.

Build a `BuddyVoiceProfile` / `BuddyVoiceEmitter` concept over the existing pooled synthetic audio path.

Possible profile parameters:

```text
base pitch
pitch variance
chirp interval
chirp length
wave/timbre family
attack/decay
volume envelope
```

Emotion then modifies the profile within safe bounds.

Example:

```text
Fear
  faster cadence
  slightly higher pitch
  shorter unstable bursts

Anger
  lower pitch
  harder attack
  fewer but heavier bursts

Happy chat
  brighter pitch movement
  relaxed cadence
```

These are implementation/presentation directions, not final tuning values.

## 8.3 Stable identity

A Buddy should sound recognizably like itself across moods.

The Buddy identity may therefore own a stable generated/selected voice seed/profile independent of current emotion.

Do not rerandomize the core voice every launch.

## 8.4 Conversation presentation

Initial Buddy-to-Buddy conversation need not generate natural-language prose.

A strong first slice can be:

```text
Buddy A approaches
→ expressive face + chirp sequence
→ Buddy B responds with its own chirp sequence
→ both show context-appropriate short reaction bubbles/icons/gestures
```

If readable text is later added, it must use original authored/systemic content and the existing safe expressive-text conventions.

## 8.5 Audio limits

- no copied Undertale audio;
- no voice cloning;
- no dependency on online TTS;
- no unbounded procedural audio generation on the physics thread;
- respect master SFX/UI accessibility and mute settings;
- avoid overlapping every Buddy at full volume in crowded Scenes.

Large multi-Buddy Scenes need a voice concurrency/priority budget.

---

# 9. Shared held-item and use-capability refactor

This is the most important technical dependency for both AI and Direct Control.

Today many tools are designed around the player's cursor/tool controller, while Buddy object interaction is primarily catch/carry/consume/toss.

Future work should separate **what an item does** from **who is currently operating it**.

## 9.1 Semantic item capabilities

Introduce trusted first-party semantic capabilities/interfaces such as:

```text
PickupCapability
AimCapability
PrimaryUseCapability
SecondaryUseCapability
ReloadCapability
MeleeUseCapability
ThrowCapability
ConsumeCapability
DeviceActivateCapability
```

Exact type names can be chosen during implementation.

A weapon/tool exposes bounded semantic commands, for example:

```text
TryAim(direction)
TryPrimaryUse(context)
TrySecondaryUse(context)
TryReload()
```

The implementation remains the single authoritative weapon implementation.

## 9.2 Operators

The same item can then be operated by:

```text
Player cursor/tool mode
Direct-controlled Buddy
AI-controlled Buddy
Device/contraption trigger
Future remote player command
```

Operator context identifies source attribution without duplicating the weapon.

## 9.3 Hand binding

Held items remain physically/presentationally attached to the real Buddy hand/socket.

Aiming submits a hand/arm target to the existing physical pose/drive system rather than directly rotating the Buddy body into a scripted animation.

## 9.4 Creator Studio compatibility

Future player-created weapons may use only the same whitelisted semantic capabilities already allowed by Creator Studio.

AI and Direct Control must not execute arbitrary user scripts.

---

# 10. Future System D — Direct Control Mode

## 10.1 Player experience

The player selects/focuses a Buddy and enters a Direct Control mode.

Intended baseline:

```text
WASD / configured movement inputs
  → locomotion/run/crouch-or-context movement as supported

Mouse world position
  → aimed hand / held-item target

Primary use input
  → use held item / fire / swing / interact

Pickup/drop input
  → pick up or release compatible nearby item
```

Only WASD-style movement and mouse-controlled hand/aim are owner-confirmed here. Exact secondary bindings should remain configurable and be locked during the implementation UX pass.

## 10.2 AI handoff

Direct Control temporarily owns selected behavior channels without deleting the Buddy's personality or state.

Conceptual arbiter priority:

```text
hard safety / knockout / forced recovery
player Direct Control
critical scripted committed activity where appropriate
AI threat/need/social/autonomy
```

When Direct Control ends:

- release player movement/aim intent cleanly;
- preserve the physically held object if valid;
- do not snap to a safe pose unless required;
- AI resumes from current position/state;
- personality remains unchanged.

## 10.3 Aim model

Mouse position should resolve into a bounded world-space hand/aim target.

The Buddy body decides how far the limb can physically reach. The cursor must not stretch the arm beyond accepted limits or inject teleport forces.

A held firearm aligns its trusted grip/muzzle geometry to the hand target through the existing presentation/physics bridge.

## 10.4 Running

Direct Control needs a stronger deliberate locomotion state than ambient walking.

Running should therefore become a reusable semantic locomotion intent available to both:

- player-controlled Buddy;
- AI fleeing/chasing Buddy.

Do not create a player-only run motor.

## 10.5 Multi-Buddy focus

Only one local Buddy is directly controlled by one local player at a time initially.

Other Buddies continue AI.

Future multiplayer may allow each connected player to Direct Control a different Buddy, but that is not required for the first collaborative-network slice.

---

# 11. Future System E — larger, scrollable, playable Scenes

## 11.1 Evolve Scene coordinates beyond the viewport

Current room-sized assumptions should be replaced with a canonical Scene world coordinate space.

A Scene should define a bounded playable area substantially larger than the current visible window.

The camera/window is then a viewport into that space.

## 11.2 Camera/navigation UX

Support a modern sandbox camera while preserving the Win98 presentation shell.

Likely capabilities:

- follow currently focused/direct-controlled Buddy;
- edge/drag/keyboard pan in edit/build mode;
- optional zoom within safe readability limits;
- quick focus selected Buddy/object;
- minimap only if later proven necessary rather than assumed.

Exact controls require a dedicated UX pass.

## 11.3 Scene navigation

Large Scenes require explicit navigation support for AI.

Introduce a Scene-owned navigation abstraction that can answer:

```text
CanReach(start, goal)
FindPath(start, goal)
FindEscapePoint(threat)
FindHidePoint(threat)
FindApproachPoint(targetBuddy)
```

Navigation output is waypoint intent only. The active puppet still moves itself physically.

Dynamic construction complicates navigation. The first implementation can rebuild/update navigability at bounded low frequency or rely on authored walkable regions plus local obstacle avoidance; exact approach requires profiling against the systemic build system.

## 11.4 Scene persistence

Extend the existing planned `SceneDocument` rather than creating a separate "level" file format.

Future conceptual shape:

```text
SceneDocument
  SchemaVersion
  SceneId
  Name
  WorldBounds
  Environment
  BuddyPlacements
  SandboxDocument
  AuthoredNavigationMetadata?   // only trusted declarative data if needed
  ScenePresentationMetadata
```

Do not persist transient ragdoll limb transforms, bullets, current AI goals, current conversation state, or other unstable runtime physics.

## 11.5 Player-authored playable Scenes

A saved Scene should eventually be playable by another user as a complete setup.

Potential shareable content includes:

- environment/background;
- placed decorations;
- safe systemic objects;
- constraints/devices;
- Blueprints expanded/referenced through safe canonical data;
- Scene bounds/layout;
- optional Buddy cast templates/slots;
- optional objective/prompt metadata only if a later owner-approved declarative scenario system exists.

Do not turn Scene sharing into arbitrary scripting.

---

# 12. Workshop Scene sharing

## 12.1 New future package type

After the local Scene schema is stable and the current Workshop safety architecture is proven, add a future declarative package type:

```text
Scene
```

It must use the same staging → validate → local-owned-copy pipeline as existing Workshop content.

## 12.2 Scene package security

A Workshop Scene may contain only specifically whitelisted data/assets.

Never allow:

- `.tscn` / `.scn`;
- `.tres` / `.res`;
- arbitrary Godot paths;
- NodePath references;
- GDScript/C#;
- DLL/native libraries;
- shaders;
- executable logic;
- arbitrary external URLs loaded by gameplay;
- direct references to another player's local filesystem;
- transient runtime IDs.

Every content reference must resolve through a trusted canonical content ID or an explicitly bundled validated data asset.

## 12.3 Dependencies

A Scene may reference content created through safe first-party editors such as Blueprints/Creator Studio.

Before implementation, define one explicit policy for dependencies:

- embed validated dependent packages;
- declare Workshop dependencies;
- or replace missing content with placeholders.

Do not silently fetch arbitrary dependencies during Scene activation.

## 12.4 Buddy identities in shared Scenes

Never export the player's live Buddy identity history/account state by accident.

A shared Scene must distinguish:

```text
local persistent Buddy identity
vs
shareable Buddy template/cast definition
```

If a Scene ships with authored Buddies, export only explicitly approved appearance/personality template data. Exclude private/local history such as lifetime statistics, player trust history, account progression, and unrelated Scene memories.

Exact cast-bundling UX is an owner decision required before implementation.

---

# 13. Future System F — multiplayer collaborative Scene play

This is intentionally the last major phase.

## 13.1 Product target

Initial long-term multiplayer target:

- one player hosts a Scene;
- another player joins;
- both see each other's cursor/mouse;
- both can grab/place/build/use compatible tools in the same Scene;
- both see the same Buddies and physics outcome;
- collaborative building/play is the primary use case.

Do not begin with ranked play, matchmaking complexity, competitive anti-cheat, dedicated servers, or MMO-like persistence.

## 13.2 Host-authoritative simulation

Do **not** attempt deterministic lockstep for Godot rigid-body physics.

Use a host-authoritative model:

```text
Host
  owns authoritative physics
  owns AI
  owns damage/economy/session mutation
  validates semantic client commands
  publishes state snapshots/events

Guest
  sends input/semantic commands
  predicts/interpolates presentation where safe
  receives authoritative corrections
```

This avoids requiring two Windows machines to produce bit-identical rigid-body simulation.

## 13.3 Network commands should mirror local semantic commands

The shared input architecture should already have commands such as:

```text
GrabStart(targetId, worldPoint)
GrabMove(worldPoint)
GrabEnd()
PlaceObject(contentId, transform)
MoveObject(objectId, transformIntent)
UseHeldItem(...)
DirectControlMove(...)
DirectControlAim(...)
```

A remote client sends the same type of semantic request that a local operator uses; it does not remotely mutate arbitrary nodes/properties.

This is another reason to build the common held-item and command layer before networking.

## 13.4 Visible remote cursors

Remote cursor state is lightweight presentation data:

```text
PlayerId
WorldPosition
CurrentPointerMode
Optional display name/color identity
```

Cursor interpolation can run independently of the authoritative physics snapshot rate.

## 13.5 Collaborative build conflicts

Two players must not simultaneously drag/edit the same object without a policy.

Start with simple host-issued short-lived interaction ownership/leases:

```text
Player A grabs object
→ host grants manipulation lease
→ Player B cannot concurrently move/delete it
→ lease releases on drop/timeout/disconnect
```

This is preferable to last-writer-wins transforms during physics interaction.

## 13.6 Workshop and network content safety

Connecting to another player must **not** become a bypass around Workshop validation.

Before joining simulation:

- negotiate protocol/game version;
- identify required canonical/bundled Scene content;
- validate all shared Scene data through the same safe schemas;
- reject unsupported content cleanly;
- never receive or execute arbitrary code from the host.

## 13.7 Failure/disconnect

Host disconnect ends the first implementation's live session cleanly unless host migration is explicitly added later.

Guest disconnect:

- releases manipulation leases;
- releases Direct Control ownership;
- leaves authoritative Scene/save state valid;
- cannot corrupt local persistence.

---

# 14. Persistence model additions

Future identity state may conceptually evolve toward:

```text
BuddyIdentityState
  BuddyIdentityId
  CharacterId
  Mood / needs / memory
  BuddyPersonalityProfile
  VoiceProfileSeed / selection
  future relationship references/history
```

Scene state remains separate:

```text
SceneDocument
  BuddyPlacement[]
  world/environment/build data
```

Relationship data, if added, should be stored in a separate bounded structure keyed by Buddy identity pairs rather than duplicating it inside both identities.

Direct Control state, AI current goals, held transient input state, active conversation phase, current paths, remote player cursors and network leases are runtime state and should not be persisted as long-term identity data.

---

# 15. UI/UX direction

## 15.1 Keep the Win98 visual language; use modern interaction design

The shell may look retro, but these systems should follow modern usability principles:

- direct labels;
- few high-value controls;
- progressive disclosure;
- immediate previews/feedback;
- keyboard/mouse discoverability;
- no giant list of opaque simulation values.

## 15.2 Personality editor

Use a dedicated personality surface with a small set of meaningful sliders and optional simple toggles.

Show consequences in plain language, e.g.:

```text
Aggression: High
More likely to confront threats and use weapons.

Sociability: Low
Usually prefers independent activities over starting conversations.
```

## 15.3 Direct Control state

The UI must make it obvious when the player is controlling a Buddy rather than using the ordinary cursor tool sandbox.

Show:

- controlled Buddy focus;
- current held item;
- relevant input hints;
- clear exit control.

Do not permanently hide normal Scene editing/tool access behind Direct Control; transition in/out quickly.

## 15.4 Large Scene navigation

Scene tabs still represent complete saved Scenes. Large scrollable geography is **inside** one Scene, not another nested room-tab system.

---

# 16. Recommended implementation sequence

The ordering below is dependency-driven, not a release commitment.

## Phase 0 — prerequisite: complete/stabilize current Multi-Buddy + Scene foundations

Required first:

- independent `BuddyIdentityState`;
- multiple `BuddyActorRuntime` instances;
- stable Scene roster/persistence;
- correct per-Buddy damage/mood/object routing;
- stable local Scene schema;
- current Workshop security architecture remains green.

No social AI should be built on a globally singular Buddy state.

## Phase 1 — shared item-use capability layer

Refactor compatible current tools/items so cursor, Buddy and devices can invoke one semantic use implementation.

Deliver:

- trusted held-item capability descriptors;
- operator/source attribution;
- Buddy hand grip/aim seam;
- primary-use seam;
- at least one firearm + one melee + one benign object proven through it;
- no player-visible AI yet required.

This phase reduces risk for every later phase.

## Phase 2 — Buddy perception + reactive threat behavior

Deliver:

- bounded Scene perception;
- Buddy source attribution;
- threat scoring;
- run/flee behavior;
- basic hide-point support;
- arm-self behavior;
- aim/threaten/attack behavior with one firearm and one melee option;
- personality influence using the existing trait seam plus provisional internal profile.

## Phase 3 — Personality v2 + player editor

Deliver:

- versioned `BuddyPersonalityProfile` migration;
- owner-confirmed `Aggression` and `Sociability` axes;
- Buddy Studio personality UI;
- deterministic save/load;
- visible behavior tests at low/high extremes;
- presets only if they are transparent compositions of the same profile.

## Phase 4 — expressive Buddy voice + social interaction slice

Deliver:

- stable Buddy voice identity/profile;
- emotion-driven chirp presentation;
- greet/approach/chat/respond/leave flow;
- social utility influenced by Sociability;
- conversation concurrency limits;
- no requirement for persistent friendships yet.

## Phase 5 — Direct Control

Deliver:

- focus/enter/exit Direct Control;
- configured WASD-style movement;
- reusable Run intent;
- mouse hand/aim targeting;
- pickup/drop;
- fire/use compatible held item;
- melee use;
- clean AI handoff;
- multi-Buddy scene remains active around controlled Buddy.

## Phase 6 — larger Scene world + navigation

Deliver:

- Scene bounds larger than viewport;
- camera pan/follow/focus;
- AI path/navigation provider;
- Build/Edit support outside current viewport;
- persistence round-trip of large coordinates/layout;
- performance budgets for larger object/Buddy counts.

## Phase 7 — Workshop Scene packages

Deliver only after local Scene format is stable:

- declarative `Scene` package type;
- strict schema/path/count/byte validation;
- preview/capture metadata;
- explicit import/play flow;
- dependency policy;
- hostile package/fuzz corpus;
- no arbitrary resources/code.

## Phase 8 — deeper social/relationship simulation

Optional expansion after the first social slice proves fun:

- persistent relationships;
- help/protect behavior;
- grudges/fear/affinity;
- shared activities;
- richer social gestures;
- social consequences from combat/care.

Do not build this merely because the schema can support it; promote only behaviors that materially improve play.

## Phase 9 — multiplayer collaborative Scene prototype

Deliver in a private/dev feature branch first:

- host/join lifecycle;
- one guest;
- remote cursors;
- host-authoritative grabbing/build actions;
- session content/version handshake;
- object manipulation leases;
- state snapshot/interpolation;
- disconnect recovery.

Only after that works should Direct Control replication or larger player counts be considered.

---

# 17. Performance budgets / engineering rules

Exact numbers need profiling, but the future implementation should preserve these principles.

## 17.1 No expensive AI work on every physics tick

- physics/actuation remains routed and allocation-free on the hot path;
- perception and utility planning run at bounded lower rates;
- pathfinding is cached/invalidated rather than recomputed continuously;
- social planning is lower priority than immediate threats and direct player interaction.

## 17.2 Stable multi-Buddy ordering

AI updates and semantic event routing should use stable Buddy IDs/placement order rather than scene-tree discovery order.

## 17.3 Bounded candidate sets

Every query has caps:

- nearby Buddies;
- items;
- threats;
- navigation candidates;
- simultaneous conversations;
- simultaneous voices;
- remote replicated objects/events.

A large Scene should degrade gracefully rather than allowing one crowded area to create unbounded O(N²) work.

## 17.4 LOD for inactive/far Buddies

For very large Scenes, consider behavior simulation LOD only after correctness is stable.

Possible future tiers:

```text
Near/visible Buddy
  full physics + normal AI

Far but active Scene Buddy
  full physics, lower-frequency expensive social queries

Inactive Scene Buddy
  no live simulation, existing Scene suspension semantics
```

Never silently advance damage/hunger/social events in inactive Scenes unless an explicit future product decision changes the current suspension model.

---

# 18. Verification plan

## 18.1 Domain tests

Cover:

- personality migration and bounds;
- utility scoring at trait extremes;
- goal commitment/hysteresis;
- threat source attribution;
- relationship symmetry/direction if later added;
- voice profile deterministic identity;
- network command validation models when applicable.

## 18.2 Headless scenarios

Required future scenarios should include:

### `buddy_threat_personality_response`

Two otherwise equivalent Buddies observe the same armed threat.

- high aggression chooses confront/arm response within expected bounded conditions;
- low aggression chooses flee/hide/de-escalation within expected bounded conditions;
- neither oscillates goals every planner tick.

### `buddy_social_personality_response`

Two idle Buddies are available.

- high sociability reliably produces an approach/chat opportunity;
- low sociability remains mostly independent under the same seeded conditions.

### `buddy_ai_weapon_use`

AI Buddy:

- identifies compatible weapon;
- reaches/picks it up physically;
- aims through the shared hand/weapon system;
- uses the same authoritative firearm implementation as the player;
- damage source is attributed to that Buddy.

### `direct_control_weapon_flow`

Player:

- enters Direct Control;
- moves Buddy;
- picks up firearm;
- aims hand with mouse/world target;
- fires;
- drops/exits control;
- AI resumes without snap/duplication.

### `large_scene_navigation`

Buddy traverses a path larger than one viewport, avoids a blocked route, reaches a target and survives Scene save/reload.

### `scene_workshop_hostile_package`

Malformed Scene packages cannot:

- escape staging root;
- exceed count/byte limits;
- inject Godot resources/scripts;
- reference runtime node IDs;
- overwrite local Scenes silently.

### `multiplayer_two_cursor_build`

Later network test:

- two connected players see remote cursor updates;
- concurrent object manipulation follows lease rules;
- host remains authoritative;
- disconnect releases ownership cleanly.

## 18.3 Performance scenarios

Measure at minimum:

- 1 Buddy baseline;
- 4 Buddies;
- 8+ Buddies or measured safe hardware target;
- many idle social candidates;
- many nearby loose objects;
- large Scene navigation rebuild/update;
- remote snapshot serialization with a representative collaborative Scene.

Do not lock product caps before measurements. Use safety budgets that scale with hardware where practical.

---

# 19. Decision gates required before implementation reaches each area

The owner direction in §1 is enough to plan architecture, but some product details remain intentionally uncommitted.

Before Personality v2 implementation:

- confirm whether the first public version has only `Aggression` + `Sociability` or additional axes;
- confirm whether players may change personality freely at any time or pay/unlock/use a special edit flow;
- confirm whether personality edits affect an existing Buddy immediately or only newly created Buddies.

Before social relationships:

- confirm whether relationship history should persist across Scenes;
- confirm which relationship dimensions are player-visible, if any;
- confirm whether Buddies can permanently hate/fear another Buddy or naturally decay toward neutral.

Before Workshop Scene sharing:

- confirm whether shared Scenes can bundle Buddy appearance/personality templates;
- confirm dependency packaging policy;
- confirm whether subscribed Scenes import as editable local copies, read-only templates, or both.

Before multiplayer:

- confirm Steam-only networking vs any non-Steam transport requirement;
- confirm host save ownership and whether guests may permanently edit the host's Scene;
- confirm whether guests may Direct Control Buddies in the first multiplayer slice;
- confirm whether sessions are invite/friends-only initially.

These gates should be recorded in a later owner source-alignment document before implementation.

---

# 20. Explicit non-goals / safeguards

This future direction does **not** authorize:

- arbitrary executable Workshop mods;
- Workshop C#/GDScript;
- arbitrary downloaded Godot scenes/resources;
- replacing the six-body active puppet with a standard character controller;
- online LLM dependency for Buddy decisions or conversations;
- copied Undertale/Mii/Sims art/audio/text/UI;
- voice cloning;
- AI that directly mutates transforms/physics outside the approved drive/use components;
- running inactive Scenes invisibly in the background;
- peer clients authoritatively mutating host physics;
- deterministic lockstep as the assumed multiplayer architecture;
- building multiplayer before local semantic commands/large Scenes are stable.

The references are inspiration for broad interaction principles only. All shipped behavior, audio, UI, copy, assets and implementation remain original.

---

# 21. Long-term target experience

A successful version of these systems should support a player story like this:

```text
The player opens a large custom Workshop Scene.

Three Buddies live there with different personalities.
One is highly social and immediately walks over to greet another.
A reserved Buddy stays across the room interacting with an object.

The player fires a weapon nearby.
The social Buddy panics and runs behind furniture.
The aggressive Buddy looks for a weapon, picks one up, aims and threatens the source.
The reserved Buddy backs away instead of joining the fight.

Each reacts with its own recognizable synthetic voice and face.

The player switches to Direct Control of the aggressive Buddy,
uses WASD to run across the Scene,
aims the Buddy's held gun with the mouse,
and fires using the exact same firearm behavior the AI used.

The player exits Direct Control.
The Buddy resumes AI from the current physical state.

Later, the player edits the Scene, saves it and publishes a safe declarative Scene package to Workshop.
A friend can import/play that Scene.

In the much later multiplayer version, the friend joins live instead:
both players see each other's cursors and collaboratively build/play in the same host-authoritative Scene.
```

That is the north-star integration target: **Buddies are characters with agency inside a player-authored physics sandbox, rather than independent features for AI, personality, controls, levels, Workshop and multiplayer.**
