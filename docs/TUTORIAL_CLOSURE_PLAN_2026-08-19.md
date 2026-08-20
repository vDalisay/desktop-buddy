# Desktop Buddy Tutorial Closure Plan

Date: 2026-08-19
Branch: `tutorial-closure`
Status: IMPLEMENTING

## Goal

Replace the broad first-session hints with a short action-driven walkthrough. The tutorial proves the player can perform one meaningful action in each major Demo workspace, then gets out of the way. Detailed discovery belongs to the permanent contextual Help mode.

## Tutorial contract

The walkthrough is ordered and durable. A step completes only when its real gameplay action succeeds; merely opening a screen does not satisfy the action inside it.

1. **Grab Buddy** — grab Buddy and **let go**. Holding does not advance; releasing does, so the next prompt never lights up mid-drag.
2. **Inventory** — open Inventory, then buy the Baseball Bat. Buying auto-equips, so there is no separate equip step. On tutorial replay, ownership is preserved and this step remains visible, highlighting the same row's Equip action instead of silently skipping it. The reward loop is taught *here* rather than as a step of its own: the purchase prompt says where credits are counted and that rough play earns them, and the spotlight lights the credit counter alongside the Buy or Equip button.
3. **Charged bat swing** — equipping a swing tool grips it (owner feedback 2026-08-20): the bat stands upright in hand for as long as it is selected, with no left button to hold. Hold right mouse to charge, then release to swing. Any amount of right-button charge advances this lesson; full charge is stronger but is not required, and the swing does not need to hit Buddy.
4. **Unequip** — press **D** to drop the tool, teaching that tools come off as easily as they go on.
5. **Paint Buddy** — open Paint Buddy, pick the Brush, pick any colour, paint the **torso** and **let go**, save, then **Use Character**. Both paint steps follow the same let-go rule as Grab: the first dab already mutates the surface, so completing on the press lit the next prompt mid-stroke. Back in Play, the guide compliments the result before moving on.
6. **Paint Background** — open Paint Background, pick Spray, pick any colour, spray the background, drag the tool panel out into its own floating window, then **Save and Exit**.
7. **Buddy Studio** — one visit: Nose category, the Button nose, Buy, Equip, Save, Exit, then a second compliment on the finished Buddy. Unequipping is *explained* in the prompt, never demanded as a second visit.
8. **Work Mode** — enter Work Mode, drag the companion, resize it, switch the counter between session and lifetime, then exit Work Mode.
9. **Farewell** — the walkthrough signs off, points at the Help button, and only then retires.

The tutorial does not enumerate every tool, paint function, cosmetic control, room-decorator operation, setting, or shortcut. The permanent Help system owns those explanations.

## Presentation

- Tutorial prompt uses the same Win98 window vocabulary as the rest of the game: raised frame, blue active title bar, white title text, gray body, Win98 buttons.
- The prompt and the guide art are **one** window: text on the left, guide square on the right. It opens centred against the right edge of the screen and is draggable by its title bar.
- Tutorial text stays concise: one immediate action per prompt.
- Steps that point at a control dim the rest of the workspace and ring that control, reusing the Help spotlight. The dim was raised 20% on owner feedback (2026-08-20): tutorial spotlights 0.34 → 0.41 alpha, Help spotlights 0.58 → 0.70. Steps whose action lives in the world or in the Work window dim nothing.
- **Input is locked to the highlighted control.** While a prompt points somewhere, only that control, the tutorial window and the Help button accept clicks; everything else is swallowed. Steps whose action is out in the world (grab, swing, drop) highlight nothing and so lock nothing — except the top bar, which stays off limits so the player cannot wander into another workspace mid-lesson.
- A step may light a **second** region it only mentions, without making it clickable: the purchase prompt rings the Buy button and the credit counter together. The dim is painted band by band so any number of holes can be cut.
- There is **no Dismiss**: hiding a prompt would strand the player behind a lock with no visible cause. `Skip Tutorial` is always available (except on the farewell, whose only action is `Goodbye`), and the two prompts with nothing to observe — the two compliments and the farewell — carry a `Continue` button instead.
- The tutorial-character presenter remains an asset seam. The current procedural helper is a placeholder until owner art is supplied; tutorial authority never depends on that image.
- While Work Mode hides the normal shell, the tutorial is presented in a separate small Win98 helper window beside the Work companion rather than disappearing.

## Replay

Settings ▸ Data carries **Show Tutorial Again**. It clears the durable v2 record — writing an empty record rather than removing the key, so a replay is never mistaken for the "existing player, no v2 record" auto-skip — and the controller restarts at Grab Buddy. Credits, owned tools and characters are untouched.

## Pricing and the economy benchmark

The Baseball Bat is now the tutorial's first purchase: **1 credit**, ordered directly under Grab. The Button nose is likewise 1 credit so the Buddy Studio lesson is affordable.

This has a real consequence for the accepted M5 benchmark. The bat left `BenchmarkSchedule` entirely — a 1-credit item cannot hold a 7-minute progression target — following the precedent already set for Pet, Tickle and Boxing Glove. Every surviving target keeps its accepted minute value. But the ~19 credits the player no longer spends on the bat pull the early-to-mid schedule forward, and three legacy rows now sit outside the ±15% band:

| Item | Target | Median | Deviation |
| --- | --- | --- | --- |
| Meal | 13m | 5.30m | −59.2% |
| Nerf Blaster | 21m | 14.64m | −30.3% |
| Soccer Ball | 52m | 43.86m | −15.7% |

Pistol onward re-converges (−12.7% and tighter). These targets were **not** silently retuned: re-pricing the early curve is an owner pacing decision, and `economy_calibration` stays red until that call is made.

## Help placement

Help is a `?` command in the Win98 title bar, immediately left of Minimize — not a floating button over the workspace. When no shell frame exists (the isolated sandbox scenario), the overlay button remains as the fallback.

Leaving Help mode must never depend on finding that small icon again: **Escape** exits it, and while it is active an **Exit Help Mode** button sits in the bottom-right corner. Both routes stay clickable under the input lock.

Work Mode hides the entire shell, so Help gets a **second surface inside the companion's own window** — its own dim, popup and `?` toggle — driven by the same region metadata as the main one. Only one surface is live at a time, chosen by whether Work is active.

## Permanent Help mode

A Help button remains available outside the tutorial and across supported workspaces.

When Help mode is active:

- hovering a documented region dims the rest of the workspace;
- the hovered region remains visibly highlighted;
- a small Win98 explanation popup names the region and explains what it does;
- Help exploration does not activate the underlying control;
- explicit help metadata wins; existing tooltips are the fallback so coverage scales without duplicating every string.

Initial explicit region coverage:

- Play shell: Inventory, Tools, Paint, Work, credits, the Help button itself, and the bottom status bar — both its message segment and the equipped-tool segment.
- Paint Buddy: canvas/preview, paint tools, brush size/history, palette/color, character library, Save/Use/Close actions.
- Paint Background: canvas, tools, brush size, palette, save/reset/exit.
- Buddy Studio: categories, preview/transform, styles catalogue, color/ownership, Buy/Equip, Save/Exit.
- Work Mode: drag region, resize affordance, counter, exit affordance.
- Room Decorator: contextual Help if the feature is present in the public Demo; tutorial inclusion remains dependent on the existing owner include/hide decision.

## Persistence and migration

- Tutorial closure uses a v2 extension record so the expanded semantic sequence does not reinterpret partial v1 records.
- Existing loaded players without a v2 record remain auto-skipped, matching the current first-session policy.
- Fresh/reset progress starts the v2 tutorial.
- Skip/completion remains idempotent and cloud-eligible through the existing progress extension map.
- The first Buddy Studio save/exit persists separate semantic v2 steps while reusing the same proven runtime Save/Exit actions as the second visit; this avoids duplicate UI authority.

## Engineering gates

1. Domain test: exact ordered v2 sequence, persistence round trip, skip/idempotence, unknown-step filtering.
2. Runtime test: Baseball Bat purchase/equip cannot be satisfied by another purchase/tool selection.
3. Runtime test: any released bat charge advances the tutorial without requiring full charge or Buddy contact.
4. Runtime test: Paint Buddy requires an actual paint mutation plus successful save/use.
5. Runtime test: Paint Background requires an actual mutation plus save-and-exit.
6. Runtime test: Buddy Studio requires buy/equip + save/exit, then a second visit with return-to-default + save/exit.
7. Runtime test: Work requires entry, drag, resize, exit and remains the terminal tutorial stage.
8. Runtime/presentation test: Win98 tutorial chrome and Help activation/region resolution.
9. Full CI plus owner local walkthrough.

## Tool usage lines

Every selectable tool carries a one-sentence "how you actually drive it" line (owner feedback
2026-08-20), shown above the buy/equip text in both the Inventory and Tools rows. It lives in
`ContentDisplayName.Usage` beside the display names, for the same reason the names do: the
authored `DescriptionKey` points at a string table that does not exist until localisation (M7).
Move both at once.

## Backlog: making rewards feel meaningful (owner, 2026-08-20)

Deferred out of tutorial closure into the general Demo polish pass. Earning credits currently
reads as a number quietly changing in the corner; it should read as a payoff.

- **Work Mode earnings** — money generated while the companion works needs its own visible beat
  rather than a silently ticking counter.
- **Purchases** — buying a tool pops a small celebratory panel with a shine/sweep animation over
  the item, not just a status-bar line.
- **Any purchase** — spawn a short burst of money particles at the cursor on a successful buy.
- **Pain payouts** — when Buddy is hurt and earns credits, spawn a money-sign particle off the
  impact, scaled by the size of the payout, so a full-charge hit visibly pays more than a tap.

All four share one seam: an existing reward event already fires for each (Work tick, purchase,
pain conversion). This is presentation on top of those, not new economy rules.

## Out of scope

- final tutorial copy polish from the owner;
- final tutorial-character art from the owner;
- adding Room Decorator to the public Demo without the existing owner decision;
- Workshop/UGC, Steamworks, or other explicitly deferred post-Demo work.
