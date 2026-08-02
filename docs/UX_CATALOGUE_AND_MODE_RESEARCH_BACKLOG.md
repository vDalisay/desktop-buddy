# Catalogue, Economy, and Work/Play UX Research Backlog

Status: **owner design/research backlog, recorded 2026-08-03.** This does not block Character Editor Phase B painting and does not authorize production implementation yet.

## Purpose

The current Work/Play and compact/full-screen behavior is functionally accepted, but the player-facing workflow still needs a more intuitive design. The next pass should treat menu structure, buying/equipping, economy pacing, and mode transitions as one UX problem rather than as isolated buttons.

## 1. Unified catalogue and loadout menu

Research and prototype replacing the separate Shop and Tools menus with one catalogue/inventory surface.

For each item, the same entry should expose the appropriate state and action:

- **Locked and unaffordable:** show price, current balance, and why purchase is unavailable.
- **Locked and affordable:** offer a clear Buy action.
- **Owned:** offer Equip from the same entry.
- **Equipped:** show an unambiguous equipped state and avoid a redundant action.

Design questions:

- Whether the catalogue should be grouped by interaction, object, weapon, care item, or progression tier.
- Whether buying should immediately equip, or leave equipping as a separate explicit action.
- How to expose tool descriptions, controls, harmful/care intent, and current ownership without making the menu dense.
- How keyboard/controller navigation, focus, escape/back, and tool switching behave.
- How the menu behaves in Work and Play without silently changing interaction mode.
- Whether Shop and Tools toolbar entries become one Catalogue entry, while Settings and Character Editor remain separate.

The purchase and equip commands must continue to use the existing authoritative catalogue, permanent-unlock, selection, and immediate-save boundaries. The UX must not create a second ownership or pricing model.

## 2. Economy and price rebalance

Revisit prices together with the current active and passive earning rates. Do not tune item prices in isolation from the systems that generate money.

Required analysis:

- Measure current active earnings by tool and common play style.
- Measure passive/background earnings over realistic sessions.
- Compare the current unlock schedule against the intended completionist, casual, and item-skipping profiles.
- Identify items whose price no longer matches usefulness, novelty, power, or time-to-afford.
- Re-run the existing economy simulation after every price or payout-rate proposal.
- Preserve unrestricted skipping: players must be able to save for a preferred expensive item rather than follow a mandatory sequence.

The balancing pass should produce explicit target times and acceptable ranges before changing authored prices or payout coefficients.

## 3. Smoother Work/Play and compact/full-screen transitions

Research a more discoverable, physical transition between passive desktop-companion use and active play.

### Corner cage/room concept

Prototype the owner's idea of a small cage, room, habitat, or docking area placed in a screen corner. Dragging the buddy into or out of it could communicate a transition more naturally than a mode button.

Questions that must be resolved before implementation:

- Does entering the room switch **Work/Play interaction**, **compact/full-screen layout**, or both?
- Is the transition triggered when the buddy crosses a boundary, when the player releases the buddy, or after a short dwell?
- How does the player cancel an accidental transition?
- Can autonomous movement trigger it, or only an intentional player drag?
- Where does the room live on multi-monitor setups, and can the player reposition it?
- What happens to an equipped or held tool during the transition?
- How is the current mode communicated without permanent instructional text?
- What recovery route remains available if the room is off-screen or obscured?

Layout mode and interaction mode should remain separate internally even if one gesture can request both. The prototype must not make it impossible to use Compact Play or Full-screen Work when those combinations are useful.

### Comparative research

Review other desktop-companion games, desktop pets, overlay utilities, and always-on-top assistants for clean-room interaction patterns such as:

- Corner habitats, docks, cages, houses, or resting zones.
- Drag-to-enter and drag-to-exit transitions.
- Edge tabs and hover-revealed controls.
- Explicit mode toggles, tray recovery, and global shortcuts.
- How click-through and active interaction are communicated.
- How accidental activation is prevented.
- How multiple monitors and DPI changes are handled.

Record interaction principles and observations only. Do not copy names, artwork, layouts, text, or distinctive presentation from another product.

## 4. Prototype and decision gate

Before production implementation:

1. Document at least three transition concepts, including the corner room/cage.
2. Produce low-cost interaction prototypes using the existing shell seams.
3. Test discovery, accidental activation, recovery, tool continuity, and multi-monitor behavior.
4. Select one owner-approved menu model and one owner-approved mode-transition model.
5. Record the accepted behavior in `docs/DECISIONS.md`, then write an implementation plan with automated and Windows acceptance gates.

## Scheduling

This backlog is intentionally non-blocking for Phase B painting. It may be researched alongside painting, but production UX changes should be scheduled as their own slice so they do not destabilize painting, character persistence, or the trusted visual/physics boundary.
