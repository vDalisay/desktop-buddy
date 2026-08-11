# Desktop Buddy — Steam Demo Polish and Marketing Plan

Status: **Approved roadmap phase; implementation not started**  
Recorded: 2026-08-11  
Target: Steam demo content-complete -> marketing capture -> release candidate

This plan is the post-feature polish pass for the Steam demo. It begins after the Potion Shop/demo-effect slice and the minimum Steam platform foundation needed to build and distribute the demo.

The goal is not to add another large system. The goal is to make every already-shipped demo system understandable, attractive, consistent, rewarding, and reliable enough to show publicly.

---

## 1. Phase order

### DEMO-P0 — bug triage and regression baseline

Before UX changes, establish a reproducible demo validation baseline:

- build + domain tests + Godot import;
- focused validators for Paint, Environment, Buddy Studio, Work Mode, tools/economy and the future Potion Shop;
- known-crash and save/restart scenarios;
- Windows DPI/window-size matrix;
- clean new-save and migrated-development-save runs.

Every owner-reported bug/polish item gets one of:

- fixed in the demo pass;
- intentionally changed by a documented owner decision;
- explicitly deferred with a reason.

### DEMO-P1 — progression and reward pass

Re-evaluate the complete demo progression as one experience rather than isolated systems:

- tool unlock order and prices;
- item/cosmetic/environment unlock visibility;
- first-session pacing;
- Work Mode session/lifetime rewards;
- first-entry Work reward clarity;
- Potion Shop affordability and Work Mode contribution;
- passive vs active earning balance;
- duplicate/permanent ownership language;
- reset behavior and player-facing reset route;
- no dead/locked content that has no understandable path to obtain it.

Do not silently recalibrate the economy. Record target times and owner approval before changing established progression numbers.

### DEMO-P2 — system UX/UI polish

Apply the concrete polish backlog below, then perform a cross-system consistency pass for wording, control placement, button states, tooltips, status-bar feedback, keyboard focus and window behavior.

### DEMO-P3 — final item/VFX/SFX asset pass

Replace temporary or programmer-facing presentation with approved demo-quality assets:

- tool/item art;
- Buddy Studio thumbnails;
- Environment item thumbnails;
- Potion Shop VFX/icons;
- semantic toolbar icons where placeholders remain;
- cursor/tool-state visuals;
- final sound effects.

**SFX for everything** is a high-priority cross-cutting requirement. Every major player action should have appropriate, non-fatiguing feedback, including purchasing/equipping, tool use, paint actions, room editing, Studio actions, Work Mode rewards, potion activation/expiry, errors and confirmations where sound helps.

### DEMO-P4 — public demo content lock

After this point only bug fixes, accessibility/readability fixes, capture-blocking polish and release-system fixes should land. Do not add new mechanics while marketing capture is in progress.

---

## 2. Paint Background / Environment paint polish

Required demo polish:

- Curved Line currently feels ambiguous with its two bend points. Show visible circular control points while a curve is actively being edited.
- Remove curve control points when the curve is completed, cancelled, deselected, or the player clicks away.
- Make active Curved Line/shape state visually obvious.
- Change the primary completion action from `Save` to **Save and Exit** where that is the actual behavior.
- Rename `Fill` to **Bucket Fill** if that better matches the final icon/interaction vocabulary.
- Correct Eraser feedback so its footprint/cursor reads like the authored brush footprint rather than an unintended ellipse.
- Show the currently active paint tool by using a pressed/indented Win98 button state.
- When a shape tool is active, surface the active shape name in the toolbar/status area.

The polish pass must preserve the accepted Spray, Curve, Undo and wallpaper/paint layering behavior.

---

## 3. Buddy Studio polish

Required demo polish:

- Increase visible differentiation between Mouth variants. Current alternatives are too similar; author clearly different shapes such as a flat line, upward/angled shape and rounded/`3`-like shape while preserving expression behavior.
- **Hide Accessories from the demo Studio for now** if the category cannot offer a meaningful finished demo selection. Do not ship a visibly empty/weak category merely to keep twelve tabs visible.
- Restore/expand Accessories in the full release as a more special authored-interaction category; see `FULL_RELEASE_EXPANSION_ROADMAP.md`.
- Single-clicking an owned item should immediately equip/select it rather than requiring an unnecessarily separate Equip confirmation.
- Single-clicking an unowned item may preview it, but the UI must make the unowned state, price and purchase action unmistakable.
- Rework the current Buy button/state language so `Buy`, `Owned`, `Equipped`, `Preview` and insufficient-funds states cannot be confused.
- Replace abstract/procedural representative thumbnails with **actual trusted renders of the item/appearance** where feasible so the tile matches what the player will see on the buddy.

The demo polish must preserve permanent ownership and the existing safe preview/save boundaries.

---

## 4. Paint Buddy polish

Required demo additions/polish:

- allow the color-palette panel to be detached/floated from the main editor workspace;
- add a Mirror checkbox for symmetric painting where the mapping supports it;
- add an option to paint the corresponding backside at the same time as the front side;
- add Bucket Fill;
- consolidate color-picker UX: reuse one consistent picker component/interaction across paint surfaces rather than multiple visually different pickers;
- the picker should always make the active color block obvious and use an icon/tool state rather than relying only on the cursor;
- move Turn and Zoom controls into the buddy preview frame, preferably a compact lower-left control cluster, so view controls are spatially associated with the preview;
- when the eyedropper samples a color that is not the currently selected palette swatch, clear the stale selected-swatch state;
- add a `Show limbs` checkbox. When enabled, pose/stretch the buddy enough to expose the limb surfaces so the player can intentionally paint them.

These are demo polish features, so they require the same Undo/save/restart/physics-isolation discipline as the existing Paint Buddy tools.

---

## 5. General shell / catalogue polish

Required demo polish:

- reduce excessive buddy ball/limb rotation during ordinary animation/walking without weakening intentional ragdoll reactions;
- merge the separate **Tools** and **Shop** concepts into one player-facing catalogue flow;
- in that unified catalogue, unowned entries use **Buy**, owned usable entries use **Equip/Select**, and the top horizontal `Tools` command can be removed once its behavior is fully covered;
- clicking the room/background outside an open horizontal-bar popup/dropdown should dismiss that popup consistently;
- menus/editor panels that are intended to float should be draggable beyond the bounds of the play area/window where the shell architecture safely supports it;
- show the currently active gameplay tool in the Win98 status bar or another persistent bottom-status location;
- normalize `Buy`, `Equip`, `Save`, `Save and Exit`, `Done`, `Cancel`, `Discard`, `Reset` and confirmation language across all systems.

The unified Shop/Tools pass must preserve the existing tool progression order, ownership and selection rules unless the separate progression review explicitly changes them.

---

## 6. Work Mode polish and rewards

Required demo polish:

- add a resize interaction based on holding LMB while using the mouse wheel, with sensible bounds and clear affordance;
- automatically re-select/equip normal Grab when exiting Work Mode so returning to Play has a predictable default interaction;
- finish the Work Mode reward presentation so session/lifetime milestones, first-entry reward and payout summaries are understandable;
- verify the active character/cosmetic renderer in Work Mode, including earned/equipped glasses and the current Buddy Studio appearance;
- add the missing first-entry onboarding/privacy sentence and the `double-click your buddy to return` teach;
- perform the Work reward/economy review together with the Potion Shop decision rather than adding isolated rewards that distort the main economy.

The four-hour Work soak and Windows monitor/DPI matrix become Steam-demo release gates rather than optional future checks.

---

## 7. Decorate Room polish

Required demo polish:

- improve the catalogue/action button hierarchy and make placement/edit/delete modes visually obvious;
- show money/balance values with deliberate color treatment that remains readable/accessibility-safe;
- remove Snap to grid from the player-facing demo UX unless a later owner review restores it;
- remove authored floor/wall placement restrictions so room objects can be freely positioned, while still keeping objects inside safe room/window bounds;
- change Delete into a deliberate **Delete mode** rather than only a selected-item action;
- simplify the nested save flow. Item placement confirmation and final room commit should not feel like two identical Save dialogs;
- the final room-level confirmation should read more like `Satisfied with your room?` with explicit Save Room / Cancel-or-Revert semantics;
- make wallpaper application follow the same dirty/commit semantics as furniture so the save prompt does not appear inconsistently by item type;
- preserve permanent ownership/storage behavior while making the distinction between `owned`, `placed`, `stored` and `new staged purchase` understandable through interaction rather than technical terminology.

---

## 8. Cross-system demo bug-fix gate

The final bug bash should explicitly exercise combinations rather than only individual screens:

- Paint Buddy -> Buddy Studio -> save -> Work Mode -> return to Play;
- Paint Background -> wallpaper -> Decorate Room -> restart;
- purchase/equip -> reset progress -> reload;
- Work Mode earnings -> Shop/Potion Shop purchase;
- potion/effect -> tool damage/reactions -> mode switch -> expiry/cleanup;
- window resize/maximize/fullscreen -> open every editor -> return;
- rapid opening/closing of horizontal-bar menus and outside-click dismissal;
- DPI changes and minimum/default/maximized layouts;
- clean install and migrated save.

No known data-loss, purchase duplication, input-lock, off-screen-window, invisible-buddy, stuck-effect or unrecoverable-shell bug may remain in the demo candidate.

---

## 9. Steam demo marketing asset phase

Marketing capture starts only from a content-locked build. Do not create final store images from a branch with programmer art, debug overlays or UI scheduled for replacement.

### 9.1 Asset inventory

Prepare the Steam-demo store/campaign package:

- Steam capsule art in all currently required store/library formats;
- main gameplay trailer;
- curated gameplay screenshots;
- short gameplay GIFs/loops for store/community/social use;
- game logo/wordmark and transparent variants as needed by the store art set;
- short/long store copy and feature bullets where required by the Steam page;
- demo-specific callouts/badges only if they match Steam's current store guidance.

Exact dimensions and Steam submission requirements must be re-checked against the current official Steamworks documentation during the asset-production phase rather than copied from an old roadmap.

### 9.2 Trailer content targets

Create a short storyboard before capture. The trailer should communicate the core toy loop quickly and show a range of systems rather than becoming a menu tour.

Candidate beats:

1. buddy living on the desktop / immediate ragdoll interaction;
2. memorable tools and reactions;
3. Paint Buddy customization;
4. Buddy Studio cosmetic customization;
5. room/background decorating;
6. Work Mode earning while the player types;
7. Potion Shop/effect showcase;
8. fast final montage + demo call-to-action.

Capture final game audio/SFX and use only approved music/audio assets.

### 9.3 Screenshot set

Capture intentional compositions rather than arbitrary gameplay frames. Cover at minimum:

- normal desktop companion view;
- one high-impact tool/reaction moment;
- Paint Buddy;
- Buddy Studio;
- decorated room/environment;
- Work Mode;
- one Potion Shop/effect moment.

Screenshots must use final HUD/UI, final item art and representative player-created/customized content.

### 9.4 GIF/loop set

Create short readable loops for features that communicate better in motion:

- ragdoll/tool reaction;
- paint stroke or customization transformation;
- room decoration placement;
- Work Mode typing/counter reaction;
- potion shader/VFX change.

Keep loops short enough that the feature reads immediately without narration.

---

## 10. Steam demo release-candidate gate

After marketing capture, cut a demo RC and run:

- full automated regression;
- installed-depot test;
- direct non-Steam launch test if supported by the demo build;
- clean install/uninstall/reinstall;
- fresh-save full progression sample;
- migration rehearsal from supported development saves;
- save corruption/recovery paths;
- offline/online Steam transitions relevant to the demo;
- 100/125/150/200% DPI and multi-monitor checks;
- minimum/default/maximized/fullscreen modes;
- four-hour active soak;
- four-hour Work Mode soak;
- hidden/tray soak;
- performance and memory review;
- final accessibility/readability/audio-volume review;
- final clean-room/IP asset audit.

The demo is ready to ship only when no selectable item/system is represented by placeholder content or a nonfunctional control.
