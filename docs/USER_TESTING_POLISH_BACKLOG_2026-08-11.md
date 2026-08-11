# Desktop Buddy — User-Testing Bug Fix and Polish Backlog

Status: **AUTHORITATIVE NEXT IMPLEMENTATION GATE**  
Recorded: 2026-08-11  
Source: owner-provided notes from a user-testing session.

This document has precedence over earlier roadmap/polish wording when they conflict on the current Steam-demo UX. These are observed user-testing findings, not speculative backlog ideas. The concrete bug-fix and usability items in Sections 1–7 must be addressed before the Potion Shop feature slice begins, unless the owner explicitly changes or defers an item.

The original notes also contained separate demo-content and full-release ideas. Those are preserved in Sections 8–9 but **do not belong to the immediate bug-fix gate**.

## Owner follow-up — 2026-08-11

The next hands-on review reopened the gate with these binding corrections:

- reduce second-bend curve sensitivity;
- retain paint-tool names beside their icons and keep checked-checkbox hover text readable;
- render the Buddy brush footprint circular in the projected preview;
- mirror across the whole buddy, pairing opposite hands/feet rather than mirroring within one limb;
- keep Show limbs connectors attached and paintable;
- replace Float Palette with a blue-title-bar pin/detach interaction for both paint palettes, and give Room Decorator the same behavior;
- rename Catalogue to Inventory and Environment Decorator to Room Decorator;
- preserve the current game-window size when opening Buddy Studio or Paint Buddy;
- restore the neutral face in Studio and show Equipped only once; owned Studio tiles omit their former price/Free text;
- further restrain ordinary limb rotation;
- latch Work wheel-resize from the initial buddy press, use smaller increments, preserve the cursor anchor, and update composition continuously during native resizing;
- prevent repeated room-item clicks from transiently unpressing/reflowing the selected tile.

The owner explicitly retained SFX as owner-authored work; this follow-up makes no SFX changes.

## Owner follow-up 2 — 2026-08-11

The next hands-on review reopened the gate again with these binding corrections:

- use the shared pin/detach behavior for Paint Background, Inventory, and Room Decorator, with resizable detached windows and scrollable Room Decorator content;
- repair Paint Background **Save and Exit**;
- simplify Room Decorator actions: remove **Review Room**, separate **Buy** from owned-only **Place**, move **Edit Room** and **Delete item** to the lower left, move **Buy**/**Place** to the lower right, and rename **Reset All** to **Reset Room**;
- make Paint Buddy's detached palette use the same sizing/layout behavior as Paint Background's palette;
- make the visible arm/leg connectors paintable while **Show limbs** is enabled, reusing the paired hand/foot paint surfaces rather than adding persistence surfaces;
- make Buddy Studio single-click preview-only, clear that preview when changing tabs, show a faint blue equipped border, and use double-click to equip or buy-and-equip;
- treat cosmetic previews as transient rather than unsaved document changes so clean Cancel exits immediately and dirty Save/save-and-exit remains available;
- allow Work dragging to start on the CRT while preserving click-to-toggle, latch wheel resizing from any draggable Work surface, restyle the hover controls as Win98 buttons, and remove live-resize region churn that causes flicker/artifacts.

The earlier single-click-to-auto-equip Studio wording in Section 2 is superseded by this follow-up.

## Owner follow-up 3 — 2026-08-11

The next hands-on review added these binding corrections:

- paint footprint outlines must match the projected stamp shape: circles for circular stamps and ellipses for elliptical projected stamps;
- detached Win98 title bars keep the Move cursor on hover;
- Paint Background's whole tool window remains detachable, but its nested color palette no longer detaches separately;
- Paint Buddy's detached palette must preserve its complete contents without clipping; Undo, Redo, and Erase All show text beside their icons; and Erase All uses shared Win98 confirmation chrome;
- Room Decorator opens at its complete usable size whenever the display permits, using scrolling only as a small-window fallback; rename its modes to **Edit mode** and **Delete mode** and keep Buy/Place equally sized;
- expanded-limb connector strokes render only on the connector and end-part strokes only on the hand/foot, using disjoint regions of the existing paired surface rather than adding a seventh persistence surface;
- Paint Buddy opens the character currently active in the main game;
- Buddy Studio becomes a top-level horizontal command beside Decorate Room rather than a Paint submenu command;
- equipped Studio tiles always retain a title-blue border, including while selected/pressed;
- main game, Paint Buddy, and Buddy Studio reuse one window position so mode changes swap content without independent remembered positions;
- Work controls gain a title-blue hover backing, and native resize must not expose grey strips at the right or bottom edge.

## Owner follow-up 4 — 2026-08-11

The next hands-on review clarified these binding visual and interaction targets:

- project Paint Buddy's white brush outline from the same anisotropic surface stamp as the paint, so the outline visibly matches the ellipse at the hovered body location;
- keep Paint Background's palette inline without its own blue title bar or surrounding window frame;
- put Paint Buddy's Save, Use Character, Reset, and Exit actions in one equal-width bottom-right row;
- anchor Room Decorator's Edit mode, Delete mode, Reset Room, Buy, and Place actions to the bottom, remove Cancel, and close on an outside left-click only while the panel is pinned;
- use one thicker active-title-blue border for the Buddy Studio item currently shown in preview, never label a tile Preview, color purchasable prices green/red according to affordability, and render color suggestions as swatches rather than words;
- put Work mode's grey controls on a persistent Win98 active-title bar, and preserve the current native window region throughout live resize so no rectangular right/bottom strip is exposed.

## Owner follow-up 5 — 2026-08-11

- Paint Buddy must never fall back to a circular white brush outline; the visible fallback is an explicit horizontal ellipse.
- Revert the follow-up-4 native-region resize change while extending Work mode's blue title bar across the full window width.
- Keep the Room Decorator close box rightmost, with the pin immediately inside it.
- Buddy Studio color swatches fill their row, category arrows select adjacent tabs, and Save/Exit sit at the bottom of the inspector panel.

## Owner follow-up 6 — 2026-08-11

- Buddy paint's Brush cursor is the unrotated vertical ellipse shown by its stamp; Background paint's Brush cursor independently uses the background projection's horizontal ellipse.
- Add Pen directly below Brush in Paint Buddy.
- Keep Paint Buddy's four bottom actions at normal Win98 button height, and anchor Unsaved Background actions to the modal bottom.
- Add a thin grey Win98 outline around Work mode's full-width blue bar.

## Owner follow-up 7 — 2026-08-11

- Paint Buddy's Brush uses the same sideways ellipse for its cursor and painted pixels; this supersedes follow-up 6's vertical orientation.
- Paint Buddy's Pen uses a circular cursor and paints circular pixels.
- Anchor Paint Buddy's four normal-height actions to the lower-right.

## Owner follow-up 8 — 2026-08-11

- Compensate Paint Buddy Pen stamps per body-surface projection so their rendered output stays circular on torso bands, rounded caps, limbs, and visible connectors.
- Bottom-align the Paint Buddy action row inside the lower-right footer instead of vertically centering it.

## Owner follow-up 9 — 2026-08-11

- Rasterize Paint Buddy Pen dabs from a screen-space circle through the trusted hit mapper so capsule UV boundaries and half-atlas limbs cannot deform the output.
- New Character performs the current-character Save/Discard/Cancel flow before opening the naming prompt.
- The large left palette block always shows the active color; the color-picker icon button retains a neutral grey face.

---

## 1. Environment paint / Paint Background

User-testing findings:

- Curved Line feels weird/unclear with the two-point bending workflow.
- It is not clear when Curved Line is selected or when curving is finished.
- Show circular curve-control points while curving is active.
- Remove those control points when the curve is deselected, cancelled, completed, or the player clicks away from the curve.
- Change `Save` to **Save and Exit** where that is the actual behavior.
- Consider renaming `Fill` to **Bucket Fill**.
- The Eraser currently reads as an ellipse rather than a brush-shaped footprint; correct its visual/cursor feedback.
- Show the currently active paint tool by visibly indenting/pressing its button.
- If a shape is active, show the active shape name as well.

The previously accepted Spray/Curve/Undo behavior remains the functional baseline; this pass is specifically about clarity and interaction quality unless a bug is discovered while fixing it.

---

## 2. Buddy Studio

User-testing findings:

- Mouth options have too little visible difference. Add more distinct variations, e.g. `—`, `^`, and a rounded/`3`-like mouth family while preserving expression behavior.
- Hide **Accessories** from the demo for now if it remains weak/unfinished.
- Full-release Accessories should become more special/interactive with the buddy rather than merely static decoration. Example from testing notes: a phone the buddy holds/checks, potentially generating passive money.
- A single click on an owned item should auto-equip/select it.
- If an item is not bought, that unowned/preview state must be much clearer.
- The Buy button/current purchase state is unclear and needs redesign.
- Thumbnail quality should improve to **actual trusted renders of the item/appearance** rather than representative abstractions.

---

## 3. Paint Buddy

User-testing findings:

- Allow the color-palette panel/window to detach or float.
- Add a **Mirror** checkbox.
- Add an option to paint the backside at the same time as the front side.
- Add **Bucket Fill**.
- Consolidate color-picker behavior. Reuse one consistent picker component/interaction across paint systems rather than visibly different pickers where practical.
- The picker should show the active color block clearly and use an icon/tool-state affordance rather than relying only on a cursor.
- Move Turn and Zoom controls into the buddy preview frame, for example a compact lower-left cluster.
- After eyedropper/color-pick sampling, the previously selected palette swatch must not remain falsely selected if it no longer represents the active color.
- Add a **Show limbs** checkbox. When enabled, pose/stretch the buddy enough that limb surfaces can be intentionally painted.

---

## 4. General shell / catalogue

User-testing findings:

- Buddy balls/limbs rotate too much during normal animation/walking; reduce ordinary-animation rotation without removing intentional ragdoll response.
- Merge **Tools** and **Shop** into one player-facing catalogue.
  - Unowned item: `Buy`.
  - Owned usable item: `Equip`/select.
  - Remove the separate Tools command from the horizontal bar once the unified catalogue fully replaces it.
- Clicking outside an open room/top-bar popup should close open horizontal-bar windows/dropdowns consistently.
- Menus/panels intended to float should be draggable outside the play window where the shell can safely support that behavior.
- Show the currently active gameplay tool in the bottom/status-bar area.

---

## 5. Work Mode

User-testing findings:

- Add Work Mode resizing using **hold LMB + mouse-wheel scroll**.
- Automatically equip/select normal Grab when exiting Work Mode.

The later content-complete pass still owns broader Work Mode reward/onboarding/economy polish; these two observed interaction issues belong to the immediate user-testing gate.

---

## 6. Decorate Room

User-testing findings:

- Improve button UX/UI and make current modes/actions clearer.
- Show the money value with deliberate color treatment.
- Remove the **Snap to grid** option from the demo UX.
- Remove floor/wall placement restrictions. Retain only technical bounds needed to keep placements recoverable/on-screen; do not preserve artificial authored floor-vs-wall gating.
- Make Delete a dedicated **Delete mode**.
- Simplify the save flow. The current second save menu is confusing after placement confirmation.
- The room-level final prompt should communicate something closer to **“Satisfied with your room?”** and then save the whole room or cancel/revert to the previous room.
- Fix consistency of when the room save/commit UI appears.
- Wallpaper must follow the same dirty/commit expectations as other room changes; it should not silently bypass the save flow.

Permanent ownership/storage remains the existing economy rule unless separately changed by the owner.

---

## 7. Cross-cutting priority

**High-priority user-testing requirement: SFX for everything.**

The immediate pass should begin filling obvious missing feedback where it directly affects the user-tested interactions. The later Steam Demo content-complete phase performs the final full-demo audio coverage and consistency pass.

At minimum audit:

- Shop Buy/Equip feedback;
- paint tool selection/actions/errors/confirmation;
- Buddy Studio Buy/Equip/save states;
- room placement/edit/delete/save/cancel states;
- Work Mode resize/reward/exit feedback where sound is appropriate;
- ordinary gameplay tools/interactions where feedback is absent or inconsistent.

Avoid noisy/non-stop UI audio; the goal is clear, non-fatiguing action feedback.

---

## 8. Separate demo-content ideas — Potion Shop phase, not the bug-fix gate

The same testing notes proposed roughly three buyable/showcase demo items and the following candidate ideas:

- flashlight that shines a bright light;
- temporary tail;
- shiny/glossy buddy treatment;
- RGB/cycling-color treatment;
- glow-in-the-dark effect that can react to the flashlight;
- metallic/shiny effect with SFX and possible rigidity behavior;
- poison effect causing buddy damage/sickness presentation.

These are inputs to `docs/POTION_SHOP_CONCEPT.md`. Exact effects, prices, durations, currencies, stacking, gameplay modifiers and demo subset remain owner-design decisions for the **next feature phase after this bug-fix gate**.

---

## 9. Separate full-release ideas

The testing notes also proposed later full-release features:

### Original office-helper tutorial

- A tutorial/helper concept inspired only by the broad office-assistant idea, but using an original office item such as a pen.
- Must remain clean-room and not copy Clippy art, character, animation or dialogue.

### Player voice recordings

- Similar in broad function to playful voice-recording toys/apps, without copying their assets/UI/code.
- Optional Light ↔ Heavy goofy voice-filter intensity.
- Record voice reactions per authored buddy trigger such as damage, happiness, or occasional random noises.
- Support multiple recordings.

These remain full-release roadmap inputs and are not Steam-demo blockers.

---

## 10. Immediate closure rule

Before moving to Potion Shop:

1. walk every Section 1–7 item against the current build;
2. implement/fix it or record an explicit new owner decision;
3. add focused regression coverage where the behavior is automatable;
4. run the relevant Paint/Environment/Buddy Studio/Work/tool validators;
5. perform one owner/user-facing manual pass over the changed flows.

Do not postpone an observed user-testing defect into generic “later polish” merely because a broader polish phase also exists.
