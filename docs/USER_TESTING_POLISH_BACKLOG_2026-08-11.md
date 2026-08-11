# Desktop Buddy — User-Testing Bug Fix and Polish Backlog

Status: **AUTHORITATIVE NEXT IMPLEMENTATION GATE**  
Recorded: 2026-08-11  
Source: owner-provided notes from a user-testing session.

This document has precedence over earlier roadmap/polish wording when they conflict on the current Steam-demo UX. These are observed user-testing findings, not speculative backlog ideas. The concrete bug-fix and usability items in Sections 1–7 must be addressed before the Potion Shop feature slice begins, unless the owner explicitly changes or defers an item.

The original notes also contained separate demo-content and full-release ideas. Those are preserved in Sections 8–9 but **do not belong to the immediate bug-fix gate**.

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