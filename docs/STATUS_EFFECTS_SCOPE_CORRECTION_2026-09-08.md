# Desktop Buddy — Status Effects Scope Correction

Status: **owner-directed scope correction / simplification**  
Recorded: 2026-09-08  
Branch: `plan/three-build-release-scope`

This document supersedes the heavier `Chemistry/Status` interpretation added to the three-build scope on 2026-09-08 wherever that wording implies a dedicated physiology/chemistry simulation as a Next Fest requirement.

The owner explicitly questioned whether that design was overengineered for Desktop Buddy and asked for comparison with Mutilate-a-Doll 2. The research supports simplifying the design.

## Research conclusion

Mutilate-a-Doll 2 does **not** appear to model a detailed physiology stack comparable to People Playground's more elaborate circulation/chemical experimentation. Instead, its sandbox primarily exposes reusable item/body **properties, effects and functionality parameters**.

Relevant examples from official Steam patch notes / developer material include:

- Poison Injector applying a poisonous effect to bullets;
- Neuro Gas applying damaging effects to living targets in an area;
- Blight as an alive-only damaging effect;
- Concussing and Hyped properties;
- Drain that damages and heals;
- generic effect application through reusable triggers such as `ApplyEffectInRadius` and `ApplyEffectOnSharp`;
- reusable effects such as shock, rust and other authored effects;
- Fleshcrafting values for damage multipliers and regeneration;
- isolated authored interactions such as Bleach + Ammonia rather than a requirement for a generalized chemical-simulation model.

MaD2's broader design philosophy is to make functionality generic and tweakable enough to reuse across many items, while keeping the individual systems comparatively direct.

References:

- https://steamcommunity.com/app/665370/discussions/0/1795152172925405479/
- https://steamcommunity.com/app/665370/allnews/
- https://steamdb.info/patchnotes/5225459/
- https://steamdb.info/patchnotes/7928143/

## Product decision

Desktop Buddy should follow the **simpler MaD2-like effect model** unless later playtesting proves a richer chemistry sandbox is worth the complexity.

### Do not make this a Next Fest requirement

Do **not** require:

- circulation simulation;
- oxygen simulation;
- muscle-system simulation;
- nervous-system simulation;
- vestibular simulation;
- liquid volumes moving through body parts;
- mixture concentration mathematics;
- pumps/tubing/containers;
- generic antidote chemistry;
- a chemistry UI;
- a large status-channel matrix.

These are not needed to deliver the core Desktop Buddy fantasy and would create additional implementation, persistence, balancing, UI and test surface.

## Recommended minimal architecture

If poison/status content is included, implement a small reusable **Effect capability** rather than a Chemistry subsystem.

Conceptually:

```text
EffectDefinition
    stable id
    duration
    stacking policy
    optional periodic interval
    trusted effect kind / bounded parameters
    presentation hooks
```

The runtime applies a small number of trusted authored effect kinds to a Buddy or supported entity.

The important abstraction is only:

```text
ApplyEffect(effectId, target)
RemoveEffect(effectId, target)
HasEffect(effectId, target)
```

Workshop/Creator content may reference only effect IDs explicitly marked safe for that build. It cannot author arbitrary C# behavior.

This is enough to support reusable item content without creating a parallel simulation architecture.

## Next Fest scope

Status effects are **optional content built on a minimal reusable capability**, not a release-gating standalone system.

If time/feel justify them, the preferred small showcase set is approximately:

- **Poisoned / Blighted** — gradual authored damage/weakness with visible sick reaction;
- **Concussed / Sedated** — pushes toward knockout / suppresses activity using existing knockout/autonomy seams;
- **Shocked / Stunned** — brief reaction / movement disruption using existing damage/reaction infrastructure;
- **Regeneration / Cure** — restores or clears the relevant authored effect.

Burning already exists and should not be rebuilt through a second effect engine merely for architectural purity.

The first useful delivery objects can be simple authored tools/items, for example:

- poison injector / poisoned projectile modifier;
- neuro-gas-style emitter or throwable if it reuses existing projectile/fire-area infrastructure cheaply;
- recovery serum / cure item.

Exact items are content choices, not architecture prerequisites.

### Not a separate milestone

Do not insert `Chemistry` as a required Next Fest milestone between systemic sandbox work and release.

Instead, effects may land opportunistically alongside:

- weapon/projectile capabilities;
- Creator Studio Lite effect choices;
- Buddy damage/reaction polish;
- Full Release Potion/effect work.

If they threaten the core Scene / multi-Buddy / Build / Blueprint / Creator Lite schedule, they are deferred without blocking Next Fest.

## Creator Studio Lite

Creator Studio Lite may expose a very small first-party effect dropdown only after the effect itself exists and is validated, e.g.:

```text
On Hit Effect:
    None
    Poisoned
    Concussive
    Burning
```

This remains a reference to trusted capability IDs, not user-defined effect code.

Sword and Explosive templates do not depend on status effects to ship.

## Full Release

Full Release should continue using the same simple effect capability by default.

Only deepen it if there is clear design/player value. Possible later additions can include:

- more effect definitions;
- gases or area effects;
- specific authored interactions between effects;
- a small number of combinations/antidotes;
- Potion Shop temporary effects using the same lifecycle infrastructure where sensible.

A full People Playground-style liquid/physiology simulation is **not currently approved or required**.

If later promoted, it requires a new source-alignment/design decision rather than emerging accidentally from this lightweight effect system.

## Implementation rule

Prefer:

```text
one reusable effect seam
+ a few fun authored effects
+ lots of item reuse
```

over:

```text
body simulation
+ chemistry simulation
+ concentration/mixing system
+ delivery plumbing
```

The success criterion is whether the effect creates a new sandbox verb or funny experiment at low implementation cost.

If an effect cannot justify itself that way, do not build the supporting system merely because competitor games have one.
