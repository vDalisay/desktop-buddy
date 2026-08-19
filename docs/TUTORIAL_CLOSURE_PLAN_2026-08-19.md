# Desktop Buddy Tutorial Closure Plan

Date: 2026-08-19
Branch: `tutorial-closure`
Status: IMPLEMENTING

## Goal

Replace the broad first-session hints with a short action-driven walkthrough. The tutorial proves the player can perform one meaningful action in each major Demo workspace, then gets out of the way. Detailed discovery belongs to the permanent contextual Help mode.

## Tutorial contract

The walkthrough is ordered and durable. A step completes only when its real gameplay action succeeds; merely opening a screen does not satisfy the action inside it.

1. **Grab Buddy** — grab and move Buddy once.
2. **Earn credits** — earn enough real credits to establish the reward loop.
3. **Inventory** — open the Shop/Inventory, buy the Baseball Bat, then equip the Baseball Bat.
4. **Paint Buddy** — open Paint Buddy, make one paint change, save the character, then choose **Use Character** to apply it and return to Play.
5. **Paint Background** — open Paint Background, make one paint change, then **Save and Exit**.
6. **Buddy Studio** — open Buddy Studio, buy/equip one non-default cosmetic, switch back to the free/default item in that same slot to demonstrate unequip, save, then exit.
7. **Work Mode (last)** — enter Work Mode, drag the companion, resize it, then exit Work Mode.

The tutorial does not enumerate every tool, paint function, cosmetic control, room-decorator operation, setting, or shortcut. The permanent Help system owns those explanations.

## Presentation

- Tutorial prompt uses the same Win98 window vocabulary as the rest of the game: raised frame, blue active title bar, white title text, gray body, Win98 buttons.
- Tutorial text stays concise: one immediate action per prompt.
- `Skip Tutorial` remains available.
- The tutorial-character presenter remains an asset seam. The current procedural helper is a placeholder until owner art is supplied; tutorial authority never depends on that image.
- While Work Mode hides the normal shell, the tutorial is presented in a separate small Win98 helper window beside the Work companion rather than disappearing.

## Permanent Help mode

A Help button remains available outside the tutorial and across supported workspaces.

When Help mode is active:

- hovering a documented region dims the rest of the workspace;
- the hovered region remains visibly highlighted;
- a small Win98 explanation popup names the region and explains what it does;
- Help exploration does not activate the underlying control;
- explicit help metadata wins; existing tooltips are the fallback so coverage scales without duplicating every string.

Initial explicit region coverage:

- Play shell: Shop/Inventory, Tools, Paint, Work, credits.
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

## Engineering gates

1. Domain test: exact ordered v2 sequence, persistence round trip, skip/idempotence, unknown-step filtering.
2. Runtime test: Baseball Bat purchase/equip cannot be satisfied by another purchase/tool selection.
3. Runtime test: Paint Buddy requires an actual paint mutation plus successful save/use.
4. Runtime test: Paint Background requires an actual mutation plus save-and-exit.
5. Runtime test: Buddy Studio requires buy/equip, return to default, save, exit.
6. Runtime test: Work requires entry, drag, resize, exit and remains the terminal tutorial stage.
7. Runtime/presentation test: Win98 tutorial chrome and Help activation/region resolution.
8. Full CI plus owner local walkthrough.

## Out of scope

- final tutorial copy polish from the owner;
- final tutorial-character art from the owner;
- adding Room Decorator to the public Demo without the existing owner decision;
- Workshop/UGC, Steamworks, or other explicitly deferred post-Demo work.
