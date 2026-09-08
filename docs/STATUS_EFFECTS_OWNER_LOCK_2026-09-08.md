# Desktop Buddy — Status Effects Owner Lock

Status: **OWNER-APPROVED DIRECTION**  
Recorded: 2026-09-08  
Branch: `plan/three-build-release-scope`

This document supersedes any earlier plan that proposed a full chemistry, circulation, oxygen, muscle, nervous-system, organ, liquid-mixture, pump, tubing, or similar physiology simulation for Desktop Buddy.

Desktop Buddy is **not** trying to match People Playground's physiology/liquid simulation. The approved direction is closer to Mutilate-a-Doll 2 and familiar RPG/action-game status design: a small reusable set of temporary effects that alter already-existing game systems.

## 1. Architecture rule

Implement one lightweight semantic effect layer rather than a separate simulation stack.

Conceptually:

```text
EffectDefinition
- EffectId
- Duration / lifetime policy
- Stacking policy
- Optional periodic action
- Existing gameplay modifiers
- Visual/audio/reaction presentation
- Clean removal/recovery rule
```

Effects compose through existing trusted systems such as damage, knockout, locomotion/active-puppet drive, gravity, restitution/bounce, recovery, structural integrity, reactions and VFX.

Do **not** introduce systems such as:

- circulation;
- oxygen/blood chemistry simulation;
- nervous system;
- muscle simulation;
- organs;
- reagent concentration;
- liquid mixing recipes;
- pumps/tubing/fluid networks;
- chemical reaction graphs.

Those are intentionally left to games such as People Playground.

## 2. Next Fest effect set

Keep the Next Fest catalogue small and immediately understandable.

### 2.1 Existing familiar effect

**Burning** already exists and remains its own established gameplay/gore interaction. It does not need to be rewritten merely to fit a new generic effect abstraction unless doing so clearly reduces duplication without destabilizing the current game.

### 2.2 New familiar effects

#### Poisoned

Player expectation: familiar RPG/action-game damage-over-time status.

Desktop Buddy behavior:

- applies small periodic trusted damage;
- gives clear sick/unwell reaction/presentation;
- may modestly reduce normal activity while active;
- expires or is removed by an authored cure/recovery action;
- does not simulate toxins, concentration or circulation.

#### Frozen / Chilled

Player expectation: familiar ice/freeze control effect.

Desktop Buddy behavior should favor fun physics over hard locking the entire simulation:

- strongly reduces active-puppet movement/drive;
- optional colder/frost presentation;
- may increase stiffness/damping through bounded trusted values;
- naturally expires or is explicitly thawed;
- implementation must preserve ragdoll recovery and must not permanently freeze individual Godot bodies into a broken state.

#### Stunned / Shocked

Player expectation: familiar short control interruption.

Desktop Buddy behavior:

- short interruption of active movement/drive;
- visible electric/stun reaction where appropriate;
- may briefly force a ragdoll/flinch state;
- does not create a new nervous-system simulation.

This can later be applied by authored electrical tools/devices.

#### Sleep / Knockout

Player expectation: familiar Sleep status.

Desktop Buddy should reuse the existing knockout/consciousness/recovery behavior rather than invent a parallel sleep-health system.

Possible distinction:

- ordinary damage can cause knockout through existing rules;
- a Sleep/Sedative effect requests a bounded temporary knockout/sleep state without requiring damage.

The exact presentation can be playful (sleep expression/snore SFX) while the underlying recovery remains trusted and deterministic.

#### Regeneration

Player expectation: familiar RPG positive status.

Desktop Buddy behavior:

- slowly restores appropriate ordinary/structural damage through existing repair/heal seams;
- visibly communicates healing;
- bounded duration/rate;
- must not mutate Paint Buddy or Buddy Studio documents;
- cannot create economy/reward exploits by repeatedly healing self-inflicted damage.

## 3. Next Fest unusual / playful effects

Ship **1–2 deliberately more unusual effects** that show why statuses are fun in a physics sandbox rather than merely reproducing RPG combat debuffs.

### 3.1 Float

Recommended unusual Next Fest effect.

A temporary personal low/zero-gravity state for the affected Buddy.

Why it fits:

- instantly readable visually;
- interacts directly with Desktop Buddy's physics identity;
- recognizable from RPG `Float` / levitation-style effects without requiring combat systems;
- can reuse the same bounded gravity-control concepts planned for Room Physics;
- creates fun combinations with tools, explosions, ropes and multi-Buddy Scenes.

Implementation rule:

- alter only trusted per-Buddy gravity/drive values;
- never mutate global room gravity merely because one Buddy is Floating;
- expiration must return every affected Buddy part to canonical gravity safely.

### 3.2 Rubberized / Bouncy

Recommended second unusual Next Fest effect.

Temporary cartoon/rubber physics:

- higher bounded restitution/bounce;
- optionally reduced blunt-impact damage while active;
- exaggerated but controlled impact reaction/SFX;
- synergizes with baseballs, bats, explosions, zero gravity and contraptions.

Why it fits Desktop Buddy better than another medical poison:

- strongly visual/physical;
- funny immediately;
- cheap to understand;
- reuses the systemic property architecture;
- differentiates the game from PPG's more serious physiology simulation.

Do not expose arbitrary physics-material mutation; use a trusted authored effect modifier with safe bounds.

## 4. Next Fest target catalogue

The intended visible status vocabulary for the Next Fest Demo is therefore approximately:

```text
Burning        // existing
Poisoned
Frozen
Stunned/Shocked
Sleep/Knockout
Regeneration
Float          // unusual
Rubberized     // unusual
```

This is a **target**, not permission to delay Next Fest if one of the new effects proves unexpectedly expensive or unstable. Scenes, multi-Buddy, construction, Blueprints and Creator Studio Lite are higher-priority release gates.

A status effect may be cut from Next Fest and retained for Full Release if it threatens the critical path.

## 5. Delivery / items

Do not build a general fluid or chemistry delivery simulation.

Next Fest may expose effects through a few authored delivery shapes such as:

- injector/syringe-style tool;
- projectile/ammunition rider;
- gas/spray/grenade effect where an existing trusted area-effect system can support it cheaply;
- Creator Studio safe effect selector for approved templates where appropriate.

The item owns how the effect is delivered; the effect definition owns what happens after application.

Examples:

```text
Poison Dart
    On accepted hit -> ApplyEffect(Poisoned)

Shock Sword
    On accepted sharp hit -> ApplyEffect(Stunned)

Float Injector
    On use -> ApplyEffect(Float)
```

No arbitrary script callback is involved.

## 6. Creator Studio integration

Creator Studio must consume the same safe effect registry.

Next Fest Creator Studio Lite may expose a small, curated effect selector where it naturally fits the approved templates (Prop, Gun, Sword, Explosive), for example:

```text
On Hit Effect
[ None ]
[ Poisoned ]
[ Stunned ]
[ Frozen ]
[ Burning ]
```

or an explosive payload selector such as:

```text
Blast Rider
[ None ]
[ Burning ]
[ Poisoned ]
[ Stunned ]
```

Exact UI/template exposure remains subject to balance and abuse/performance review.

Creator content cannot create arbitrary new runtime effect code in the Next Fest Demo. It may only reference effects/capabilities explicitly marked safe for declarative UGC.

## 7. Full Release effect pool

Full Release may expand the catalogue with familiar effects that add real sandbox value, without committing to all of them now.

Candidate familiar effects include:

- Slow;
- Haste / Hyper;
- Strength;
- Weakness / Vulnerable;
- Numb / Pain Resistance;
- Confused / Erratic;
- Curse / healing reduction or another simple authored interpretation;
- Shielded / temporary damage resistance;
- Leech / Drain;
- stronger Regeneration variants.

Candidate unusual/physics-focused effects include:

- Heavy;
- Lightweight;
- Magnetized, only if the device/material architecture makes it cheap and understandable;
- Explosive / volatile body effect;
- Slippery;
- Sticky;
- temporary Ghost/non-collision effect only if recovery/collision safety is proven.

These are **candidate content**, not a promised launch checklist. Add effects because they create a useful new toy/combination, not simply to grow a status list.

## 8. Explicitly rejected Full Release direction

Even in the Full Release, do not plan or implement a People Playground-style physiology/chemistry simulation unless the owner explicitly reverses this decision later.

Rejected by current product direction:

- circulation simulation;
- oxygen simulation;
- organs/brain/lungs/heart systems;
- blood composition;
- doses/concentrations;
- reagent chemistry;
- fluid containers as biological simulation;
- pumps/tubes/liquid-transfer gameplay as a required systemic feature;
- large medical/biological dependency graph.

Desktop Buddy's differentiation remains personality, customization, Win98 desktop presentation, physics creativity, multi-Buddy Scenes, creator tools and safe moddability.

## 9. Design reasoning / references

The selected common vocabulary follows widely recognizable game status conventions: Poison commonly means damage over time, Stun disables/interupts action, Freeze immobilizes/slows, Sleep/KO disables action, and Regeneration restores health over time. Familiar status vocabularies reduce teaching burden and let the same combat/physics framework produce differentiated items.

Mutilate-a-Doll 2 is the closer architectural reference for Desktop Buddy than a full People Playground physiology model. MaD2 exposes reusable item properties/effects such as Poisoning, Freezing, Shocking, Healing, Hyping, Concussing/Stasis-related behavior and effect application triggers rather than requiring every effect to emerge from a detailed body simulation. That capability-oriented approach matches Desktop Buddy's safe Creator/Workshop architecture.

Research references used for this decision:

- Game Mechanics Hoard — Status Effects: https://gamemechanicshoard.com/mechanic/status-effects/
- RPG Architect documentation — Status Effects: https://docs.rpg-architect.com/06-database/05-statistics/03-status-effects/
- Mutilate-a-Doll 2 Steam Community/announcement history covering Concussing, Hyped, Freezing, Regeneration and reusable ApplyEffect triggers: https://store.steampowered.com/news/?appgroupname=Mutilate-a-Doll+2&appids=665370&feed=steam_community_announcements&headlines=0
- MaD2 community Properties reference for reusable Poisoning, Freezing, Shocking, Healing, Hyping, Suspending/Stasis and resist properties: https://mutilate.fandom.com/wiki/Properties
- People Playground research was used only as contrast/evidence for effects players enjoy; its deeper liquid/physiology model is explicitly not adopted.

## 10. Priority rule

Status effects are a content multiplier, not a foundational simulation pillar.

Implementation priority remains:

```text
Next Fest foundations
    Scenes / multi-Buddy
    systemic construction/editing
    Blueprints
    Creator Studio Lite

then
    lightweight reusable effects + authored effect items
```

Do not destabilize or delay the core Next Fest differentiators merely to ship every status in this document.
