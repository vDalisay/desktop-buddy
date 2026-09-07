# Desktop Buddy — Three-Build Demo / Next Fest / Full Release Scope Alignment

Status: **owner-approved correction / superseding source alignment**  
Recorded: 2026-09-07  
Planning branch: `plan/full-release-systemic-sandbox`  
Audited base: `main` at `3966648b7f9fb8c6a70f4cf6f8e586b9998a2641`

This document corrects the first Next Fest vertical-slice draft. It is authoritative wherever `docs/NEXT_FEST_DEMO_SYSTEMIC_SANDBOX_VERTICAL_SLICE_2026-09-07.md` suggests removing, re-curating, hiding, or replacing content that already belongs to the normal Steam Demo.

## 1. Owner decision: three distinct builds

Desktop Buddy must support three intentionally different build surfaces from one codebase:

1. **Normal Demo**
2. **Next Fest Demo Update**
3. **Full Release**

They are cumulative in this order:

```text
Normal Demo
    ⊂ Next Fest Demo Update
        ⊂ Full Release
```

The important product invariant is:

> **The Next Fest Demo is additive over the Normal Demo. It must not remove anything that is already in `main` or already approved/planned for the Normal Demo.**

Likewise, Full Release contains the Normal Demo and Next Fest systems plus the remaining systemic-sandbox breadth/content unless a separate explicit product decision says otherwise.

The itch.io build remains a separate reduced distribution and is not one of these three Steam build surfaces.

---

## 2. Normal Demo baseline

The Normal Demo is the Steam Demo represented by the current `main` codebase plus the already-approved Normal Demo work that predates the new People Playground / Mutilate-a-Doll-inspired systemic-sandbox plan.

Examples of Normal Demo content that remains present include the existing/planned Demo surfaces such as:

- the existing normal gameplay tool catalogue allowed by the Normal Demo scope;
- Grab / care / damage / ragdoll gameplay already included there;
- Work Mode;
- Paint Buddy;
- Paint Background / Paint Room;
- Buddy Studio categories/content already authorized for the Normal Demo;
- the first-session tutorial;
- Gore Mode in Steam builds under the current policy;
- Steam Workshop v1 for the already-approved room-painting and Buddy-character package types;
- all other player-visible functionality already in `main` that is not explicitly Full-Release-only by the pre-existing Normal Demo scope.

This correction does **not** authorize moving pre-existing Full-Release-only content into the Normal Demo. It only protects the Normal Demo baseline from being reduced by the Next Fest vertical-slice planning.

The Normal Demo should remain buildable and shippable independently after Next Fest development begins.

---

## 3. Next Fest Demo Update = Normal Demo + new vertical slice

The Next Fest build contains **everything in the Normal Demo**, unchanged in availability unless a separate owner decision changes it, and then adds a curated vertical slice of the newly planned systemic-sandbox / Scene / multi-Buddy systems.

Conceptually:

```text
NEXT FEST DEMO
=
NORMAL DEMO
+
Multi-Buddy vertical slice
+
Scene-tab vertical slice
+
limited systemic construction
+
limited constraints
+
limited properties
+
limited devices/signals
+
limited materials/destruction
+
limited structural Buddy damage/repair
+
local Blueprint persistence
+
selected new room/system controls
+
optional Demo-compatible contraption Workshop support after local validation is stable
```

The Next Fest slice proves the new Full Release direction without pretending to contain its full content breadth.

### Non-regression rule

A Next Fest availability policy is never allowed to say:

```text
"hide Normal Demo tool X because another tool represents that category better"
```

if X was already part of the Normal Demo.

Vertical-slice curation applies only to **new systems/content being introduced by this new expansion plan**.

For example, selecting Wood Beam / Metal Plate / Wheel as Demo construction representatives is valid because construction is new. Removing Boxing Glove, Tickle, Nerf, Soccer Ball, Drink, or another already-included Normal Demo tool merely to make the new Demo catalogue smaller is **not valid**.

---

## 4. Full Release = complete direction

The Full Release contains:

- the complete Normal Demo feature/content baseline;
- the complete Next Fest vertical slice;
- broader/higher-cap versions of the Scene and multi-Buddy systems;
- the remaining planned systemic-sandbox construction pieces;
- broader constraint/joint vocabulary;
- broader property descriptors;
- broader device/sensor/logic catalogue;
- deeper materials, destruction, heat/fire/electricity/cutting as separately implemented;
- deeper Buddy structural systems, including physical detach/reattach only after the required rig refactor;
- broader Room Physics;
- functional furniture / scene tooling when implemented;
- larger Blueprint/Workshop ecosystem;
- additional Full Release cosmetics, room content, tools and expansion features from the existing Full Release roadmap.

Buddy-to-Buddy social AI, relationships, coordinated activities and intentional interaction remain outside the initial Scene/multi-Buddy implementation and are not automatically included merely because the Full Release can host several Buddies.

---

## 5. Build-feature model

Do not overload the existing `steam_demo` tag to mean both the Normal Demo and the widened Next Fest slice. Those are now distinct builds.

Recommended feature tags:

```text
Normal Demo:
    steam,steam_demo

Next Fest Demo Update:
    steam,steam_demo,next_fest_demo

Full Release:
    steam,full_release
```

The existing Normal Demo export preset may remain unchanged.

Add a separate Next Fest export preset when implementation begins rather than mutating `Windows Steam Demo` into the Next Fest build. This keeps reproducible access to both Demo variants.

Runtime policy should expose clear semantics such as:

```csharp
DemoScope.IsSteamDemo
DemoScope.IsNextFestDemo
DemoScope.IsFullRelease

DemoScope.IncludesNormalDemoFeatureX
DemoScope.IncludesNextFestSandboxSlice
DemoScope.IncludesFullSystemicSandbox
```

where appropriate.

Do not gate player-visible content on `steam` alone; `steam` identifies platform integration, not which of the three content builds is running.

### Expected truth table

| Capability | Normal Demo | Next Fest Demo | Full Release |
| --- | ---: | ---: | ---: |
| Existing Normal Demo gameplay/tools | Yes | Yes | Yes |
| Existing Normal Demo Work/Paint/Studio/tutorial | Yes | Yes | Yes |
| Existing Normal Demo Workshop v1 | Yes | Yes | Yes |
| New multi-Buddy vertical slice | No | Yes | Yes |
| New Scene tabs vertical slice | No | Yes | Yes |
| New construction vertical slice | No | Yes | Yes |
| New constraint/property/device slice | No | Yes | Yes |
| New Blueprint vertical slice | No | Yes | Yes |
| Full construction/device/property breadth | No | No | Yes |
| Higher Scene/Buddy/system caps | No | No | Yes |
| Full systemic-sandbox content roadmap | No | No | Yes |

---

## 6. Corrected Next Fest vertical-slice content principle

The systemic vertical slice remains intentionally small **inside the new systems**.

Good examples:

### Construction

Demo adds a small representative set such as:

- Wood Beam;
- Metal Block/Plate;
- Wheel.

Full Release adds the larger catalogue.

### Constraints

Demo adds:

- Rope / world anchor;
- Weld;
- Hinge.

Full Release adds Spring, Slider, motorized/breakable/advanced constraints later.

### Properties

Demo adds:

- Mass;
- Bounce;
- Gravity Scale;
- Frozen;
- a small safe weapon-property showcase such as Shotgun cadence/spread/knockback.

Full Release adds the larger bounded property vocabulary.

### Devices

Demo adds:

- Button;
- Timer;
- Piston;
- Weapon Trigger;
- optionally a Lamp output if useful for teaching.

Full Release adds sensors, logic, memory and specialist devices.

### Materials/destruction

Demo adds:

- Wood;
- Metal;
- one authored Wood break path.

Full Release adds the deeper material/destruction model.

This curation does **not** alter the pre-existing Normal Demo toybox.

---

## 7. Corrected implementation order

The development sequence should preserve all three distributable states.

### Stage A — preserve Normal Demo

Before the new systems widen scope:

- lock Normal Demo acceptance tests around current/pre-approved content;
- add explicit build-scope tests so `steam_demo` alone remains the Normal Demo;
- ensure no new default/fallback accidentally opts a Normal Demo build into Next Fest systems.

### Stage B — shared architecture

Implement the shared foundations needed by Next Fest and Full Release:

- account/Buddy state split;
- multi-Buddy production runtime;
- Scene document/runtime host;
- systemic sandbox foundations.

These code paths may exist in every build, but composition and UI remain gated by distribution scope.

### Stage C — Next Fest slice

Add `next_fest_demo` and expose only the selected new-system representatives/caps while retaining every Normal Demo feature.

Verify three build matrices on every relevant scope change:

```text
Normal Demo  -> baseline only
Next Fest    -> baseline + vertical slice
Full Release -> baseline + vertical slice + full breadth
```

### Stage D — Full Release breadth

Continue adding Full-Release-only definitions, devices, properties, materials, joints, room systems and content without widening either Demo build.

---

## 8. Save compatibility

The three builds should share compatible semantic formats wherever practical.

Normal Demo data must upgrade cleanly into Next Fest and Full Release.

Next Fest Scene/Buddy/Blueprint data must upgrade cleanly into Full Release.

A Full Release save may contain content unavailable to a Demo, so downgrading back to a Demo must fail safely or preserve unknown data without activating unavailable content. Never destructively strip Full-Release state merely because a Demo executable sees the same save path.

Steam Cloud/path policy must therefore be reviewed before all three builds are allowed to write mutually visible data.

---

## 9. Acceptance gates

### Normal Demo gate

Build the existing `steam,steam_demo` surface and prove that all previously approved Demo functionality remains available and no Next Fest-only systemic UI/content leaks in.

### Next Fest gate

Build `steam,steam_demo,next_fest_demo` and prove:

1. every Normal Demo acceptance journey still passes;
2. multiple Buddies / Scene tabs work at the Demo cap;
3. the selected construction/constraint/property/device/material slice works;
4. Blueprint persistence works;
5. Full-Release-only systemic definitions remain unavailable.

### Full Release gate

Build `steam,full_release` and prove:

1. all Normal Demo features still exist;
2. all Next Fest systems/content exist;
3. higher caps and Full-Release-only systemic content are available;
4. Demo-created compatible data opens without conversion loss.

---

## 10. Superseded wording

The following wording in the first Next Fest vertical-slice draft is superseded:

- any recommendation to **re-curate the existing Demo gameplay-tool roster by removing already included tools**;
- any statement that tools such as Tickle, Boxing Glove, Baseball, Nerf Blaster, Soccer Ball, Drink, Power Grab, or other current Normal Demo content become Full-Release-only merely because the Next Fest plan wants fewer representatives;
- any implication that the current `Windows Steam Demo` export preset should be transformed into the Next Fest build instead of preserving a Normal Demo build;
- any model with only two Steam content surfaces (`Steam Demo` vs `Full Release`) for this new roadmap.

The intended model is now unambiguous:

```text
Normal Demo
    current/pre-approved Demo content

Next Fest Demo
    Normal Demo
    + curated vertical slice of NEW systemic systems

Full Release
    Next Fest Demo
    + remaining systemic content/depth/caps
    + other approved Full Release expansion content
```
