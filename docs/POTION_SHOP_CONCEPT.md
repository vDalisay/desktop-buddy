# Desktop Buddy — Potion Shop / Effect Consumables Concept

Status: **Approved roadmap addition; detailed design still pending**  
Recorded: 2026-08-11  
Target: Steam demo before the final polish/content-complete pass

## 1. Product goal

Add a dedicated Potion Shop that lets the player spend earned currency on temporary, highly visible effects for the buddy. The feature should create short, toy-like moments that are easy to understand, fun to combine with normal tools, and visually useful for the Steam demo/trailer.

This is intentionally a concept boundary rather than a locked implementation spec. Exact effect durations, prices, stacking rules, economy tuning, UI layout, and the demo's final three-item selection are still owner-design decisions.

## 2. Demo target

Aim for roughly **three polished showcase effects/items** in the Steam demo rather than a large shallow catalogue. Every shipped entry must have final presentation, sound, clear purchase/use feedback, and a working interaction loop.

Candidate effects from the current owner notes include:

- temporary tail;
- glossy/shiny buddy treatment;
- RGB/cycling-color effect;
- glow-in-the-dark treatment, potentially reacting to a flashlight item;
- metallic/shiny treatment with matching SFX and, only if explicitly approved, a temporary gameplay/rigidity modifier;
- poison/sickness effect with damage/reaction feedback;
- a flashlight as a separate buyable toy that can interact with light-sensitive potion effects.

The final demo subset is not locked yet.

## 3. Economy / Work Mode integration

Work Mode should contribute meaningfully to this loop, but the exact economy model is deliberately unresolved.

Design options to evaluate before implementation:

1. keep the existing credit economy and make Work Mode an additional way to afford potions;
2. award a limited Work/AFK bonus token that can be spent in the Potion Shop;
3. use Work milestones to grant discounts, free samples, or occasional potion rewards without adding a second permanent currency.

Do **not** add a parallel currency ledger until the design pass explicitly chooses it and defines reset, persistence, earning, spending, UI, and migration rules.

## 4. Architecture boundaries

Potions/effect consumables are not normal permanent tools and are not Buddy Studio cosmetics.

Recommended separation:

- permanent tools/cosmetics remain permanently owned content;
- potion purchases represent consumable or temporary effect activations;
- visual effects run through a bounded trusted effect catalogue;
- any gameplay-changing potion must have an explicit authored effect policy rather than arbitrary scripting;
- temporary visual state must never mutate character paint/cosmetic documents;
- effect cleanup must be deterministic when the duration ends, the mode changes, the buddy is reset, or the game exits.

If effects can stack, combinations must be intentionally authored or resolved by a simple compatibility policy so shader/material state cannot become order-dependent.

## 5. Required design pass before code

Lock these decisions before implementation:

- final three-ish demo entries;
- purchase model: single-use, timed activation, inventory quantity, or immediate consume;
- effect duration and whether time advances in Work/hidden modes;
- whether more than one potion can be active;
- normal credits vs Work-specific reward integration;
- where the Potion Shop lives in the Win98 shell;
- whether the flashlight belongs to the Potion Shop or the normal unified Shop;
- reset/restart behavior;
- HUD/status treatment for active effects;
- accessibility options for flashing/RGB/glow effects;
- SFX/VFX asset requirements.

## 6. Demo exit gate

The Potion Shop is demo-complete when:

- the selected demo effects are visually distinct and production-presentable;
- purchase/use/expiry behavior is obvious without debug text;
- Work Mode integration follows the approved economy decision;
- effects cleanly restore the buddy's prior visual/gameplay state;
- effects do not corrupt Paint Buddy, Buddy Studio, room/environment, physics, or save data;
- all shipped entries have final or approved demo-quality assets and SFX;
- automated state/cleanup/economy tests and a local owner feel gate pass.
