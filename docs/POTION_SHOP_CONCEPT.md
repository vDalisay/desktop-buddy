# Desktop Buddy — Potion Shop / Effect Consumables Concept

Status: **Approved Full Release roadmap addition; detailed design still pending**  
Recorded: 2026-08-11  
Target: **Full Release — explicitly excluded from Steam Demo scope**

## 1. Product goal

Add a dedicated Potion Shop that lets the player spend earned currency on temporary, highly visible effects for the buddy. The feature should create short, toy-like moments that are easy to understand and fun to combine with normal tools.

The owner moved this feature out of the Steam Demo so the demo can focus on polishing and releasing the systems already implemented. No Potion Shop code, economy, effects, UI, assets or marketing footage are required for the Steam Demo.

This document remains a concept boundary rather than a locked implementation spec. Exact effect durations, prices, stacking rules, economy tuning, UI layout and the initial Full Release effect selection are still owner-design decisions.

## 2. Initial Full Release target

Start with a **small polished effect set** rather than a large shallow catalogue. Roughly three showcase entries remains a useful first-slice target, but it is not a demo requirement and can be changed during the Full Release design pass.

Candidate effects from the current owner notes include:

- temporary tail;
- glossy/shiny buddy treatment;
- RGB/cycling-color effect;
- glow-in-the-dark treatment, potentially reacting to a flashlight item;
- metallic/shiny treatment with matching SFX and, only if explicitly approved, a temporary gameplay/rigidity modifier;
- poison/sickness effect with damage/reaction feedback;
- a flashlight as a separate buyable toy that can interact with light-sensitive potion effects.

The final initial subset is not locked yet.

## 3. Economy / Work Mode integration

Work Mode may contribute meaningfully to this loop, but the exact economy model is deliberately unresolved.

Design options to evaluate before implementation:

1. keep the existing credit economy and make Work Mode an additional way to afford potions;
2. award a limited Work/AFK bonus token that can be spent in the Potion Shop;
3. use Work milestones to grant discounts, free samples or occasional potion rewards without adding a second permanent currency.

Do **not** add a parallel currency ledger until the Full Release design pass explicitly chooses it and defines reset, persistence, earning, spending, UI and migration rules.

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

- initial effect entries;
- purchase model: single-use, timed activation, inventory quantity or immediate consume;
- effect duration and whether time advances in Work/hidden modes;
- whether more than one potion can be active;
- normal credits vs Work-specific reward integration;
- where the Potion Shop lives in the Win98 shell;
- whether the flashlight belongs to the Potion Shop or the normal unified Inventory;
- reset/restart behavior;
- HUD/status treatment for active effects;
- accessibility options for flashing/RGB/glow effects;
- SFX/VFX asset requirements.

## 6. Full Release implementation gate

The initial Potion Shop slice is complete when:

- the selected effects are visually distinct and production-presentable;
- purchase/use/expiry behavior is obvious without debug text;
- Work Mode integration follows the approved economy decision;
- effects cleanly restore the buddy's prior visual/gameplay state;
- effects do not corrupt Paint Buddy, Buddy Studio, room/environment, physics or save data;
- all shipped entries have production-quality assets and SFX;
- automated state/cleanup/economy tests and a local owner feel gate pass.

## 7. Sequencing

Do not implement this feature during Steam Demo polish, Steam platform foundation, Steam marketing production or Steam Demo RC. The current sequencing authority in `docs/ROADMAP.md` places Potion Shop at the start of the post-demo Full Release expansion program.
