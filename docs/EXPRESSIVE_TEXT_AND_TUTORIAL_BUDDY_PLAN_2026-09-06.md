# Expressive Text + Tutorial Buddy Plan

Date: 2026-09-06
Branch: `expressive-text-tutorial-buddy-plan`
Status: PLANNING

## Goal

Add a reusable presentation layer that gives Desktop Buddy more authored personality without turning normal UI into animated noise.

The first consumers are:

1. **Tutorial dialogue** — typewriter reveal, authored emphasis, small speech-like SFX and a live rendered tutorial Buddy portrait.
2. **Tool purchase/unlock moments** — short, bold, animated 90s-office/WordArt-inspired titles.
3. **Work Mode milestones** — the same celebratory title system, with milestone-specific emphasis.

The implementation must be reusable rather than tutorial-specific, must preserve the current tutorial progression/economy authority, and must obey the existing sound/motion/accessibility settings.

The itch.io build remains omitted from this work for now.

## Explicit scope decisions

### Steam achievements

Do **not** try to animate, recolor, wobble or replace Steam's native achievement notification. That popup is platform UI rather than Desktop Buddy UI.

Desktop Buddy may still trigger Steam achievements through the normal platform seam, but the native Steam notification remains untouched. This plan only owns in-game presentation that Desktop Buddy actually renders itself.

### Paint Buddy / Buddy Studio

Do not add celebratory expressive text to ordinary Paint Buddy or Buddy Studio saves in the first pass. Their normal Save/Use/Equip flows should stay direct and quiet.

The architecture must allow a future authored one-off event to use the same presentation system if we later identify a genuinely important moment, but no first-pass caller is added there.

### Visual direction

Celebratory titles should be strongly inspired by late-90s Microsoft WordArt/Office presentation language without copying Microsoft artwork or shipping WordArt assets. The target is the visual vocabulary: arched/warped lettering, chunky extruded shadow, outlines, simple gradients, perspective/skew and deliberately excessive 90s desktop typography.

Tutorial dialogue is different: it stays readable inside the Win98 tutorial window and only uses small emphasis effects on selected words.

---

## Current seams to preserve

- `FirstSessionGuidanceController` remains the sole tutorial progression authority. Expressive presentation must not decide when a tutorial step completes.
- `TextFor(stepId)` remains the source of the tutorial's semantic copy until localization moves it elsewhere.
- `DemoTutorialCharacterPresenter` is already a presentation seam and should be replaced/reworked rather than bypassed.
- `RewardPopup` remains the central reward queue and reward timing authority for purchases and Work milestones.
- `UiFeedbackAudioBootstrap` remains the UI-audio owner. The new system must route through the UI bus/settings rather than create an unrelated audio manager.
- `Win98MotionPolicy` / the existing effects settings remain authoritative for reduced motion / motion-disabled behavior.
- Buddy 3D presentation continues to consume a read-only pose source. Do not instantiate gameplay physics merely to draw the tutorial portrait.

The repo already has `IBuddyVisualTransformSource` and `StaticBuddyVisualTransformSource`, which is explicitly physics-free and intended for preview rendering. The portrait should build on that seam.

---

# System A — expressive dialogue

## A1. Semantic document model

Introduce a small engine-independent authored model, e.g.:

- `ExpressiveTextDocument`
- `ExpressiveTextRun`
- `ExpressiveTextEffect`
- `ExpressiveTextVoiceProfile`

A run carries plain text plus semantic presentation metadata rather than embedding Godot node behavior into copy.

Initial effect vocabulary:

- `None`
- `Action` — required button/mouse/key action; strong readable emphasis
- `Input` — keyboard/mouse input styling
- `Money` — credit/reward styling using the established money-green language
- `Wave` — low-amplitude vertical wave
- `Shake` — brief force/impact emphasis
- `Excited` — stronger scale/wave accent used sparingly
- `Warning` — color/weight only; no distracting movement

Effects are composable only where needed. Avoid a general-purpose scripting language.

## A2. Authoring format

Use a deliberately small tag syntax in authored copy so localization can preserve meaning without hard-coding character offsets. Example concept only:

`Hold [input]right mouse[/input] to charge a [wave]big swing[/wave].`

The parser must:

- produce plain fallback text if a tag is unknown;
- never execute arbitrary code;
- keep semantic tags stable for localization;
- expose a plain-text version for tests/accessibility;
- allow effect-free strings with zero special handling.

Do not couple the parser to tutorial step IDs.

## A3. Typewriter presenter

Create a reusable `ExpressiveTextPresenter` that owns reveal state but not business state.

Behavior:

- reveal at a configurable base character rate;
- slightly longer pauses at comma / sentence-ending punctuation;
- skip spaces/punctuation for speech-blip playback;
- emit `RevealStarted`, `VisibleGlyphAdvanced`, `RevealCompleted` events;
- first click/confirm while revealing completes the current text instantly;
- a later click continues to whatever owning UI already defines;
- re-rendering the same semantic document does not restart speech every frame;
- replacing the document cleanly resets reveal/effect state.

The tutorial window consumes these events but remains responsible for its existing Continue/Skip behavior.

## A4. Expressive glyph rendering

Use a custom Control/RichTextLabel-backed presentation layer rather than scattering one Label per character throughout callers.

Requirements:

- per-run color/weight styling;
- per-visible-glyph offset/rotation/scale for Wave/Shake/Excited effects;
- deterministic layout and wrapping;
- effects never change line-break measurement after reveal begins;
- whole-pixel snapping where possible so Win98 text does not shimmer;
- effects are low amplitude in tutorial copy;
- effect animation stops when motion is disabled.

A plain `RichTextLabel` may own text measurement/selection semantics, but any custom glyph animation must live behind this one presenter so callers do not know how it is drawn.

## A5. Tutorial voice SFX

Add a reusable `TextVoiceProfile` / `ExpressiveTextAudioPresenter` layered through `UiFeedbackAudioBootstrap`.

Tutorial guide voice direction:

- tiny synthetic 90s-computer chirps rather than human speech;
- 3–5 closely related variants/pitches;
- one blip every ~2–3 visible non-space glyphs, not every glyph;
- no blip on punctuation/whitespace;
- subtle enough for 32 tutorial prompts;
- stops immediately when reveal is skipped/completed;
- obeys Interface Sounds / UI bus volume.

The voice profile is data. A future narrator/popup can select another profile without cloning the reveal code.

---

# System B — WordArt-style celebration titles

## B1. Reusable presenter

Create a separate `WordArtCelebrationPresenter` rather than abusing the tutorial dialogue renderer. Dialogue optimizes for reading; reward WordArt optimizes for a short visual hit.

Inputs:

- title text;
- style preset;
- optional icon;
- optional numeric/subtitle line;
- duration/priority supplied by the owning reward queue.

Initial style primitives:

- outline;
- drop/extruded shadow;
- two-stop/simple vertical gradient;
- arch or shallow wave baseline;
- skew/perspective-like transform;
- entry squash/overshoot;
- short settle wobble.

Build a handful of original presets from those primitives rather than exposing arbitrary shader parameters to callers.

Example preset concepts:

- `OfficeClassic` — blue fill, light highlight, dark extrusion;
- `MoneyBurst` — green/yellow emphasis for purchases;
- `MilestoneArc` — arched title with strong shadow;
- `BigDeal` — larger short-lived overshoot for rare milestones.

Exact art tuning is an owner visual gate after implementation.

## B2. RewardPopup integration

Do not create a competing notification queue. Extend or compose inside the existing `RewardPopup` path.

First-pass callers:

- successful tool purchase / unlock;
- Work Mode session milestone;
- Work Mode lifetime milestone shown by Desktop Buddy itself.

Native Steam achievement notification stays separate and untouched.

The existing icon/glow/reward timing may remain as the surrounding frame; WordArt becomes the expressive title treatment. If the combined result is too busy, prefer WordArt + icon and reduce redundant glow/breathing rather than stacking every effect.

## B3. Accessibility

When motion is disabled:

- render the final WordArt pose immediately;
- no wobble, arch animation, overshoot or animated distortion;
- retain static styling, outline and color.

Photosensitivity-safe behavior must not add brightness pulsing beyond current policy.

---

# System C — live tutorial Buddy portrait

## C1. Replace the procedural 2D guide

Retire the current procedural `TutorialBuddyCard` as the normal tutorial presentation.

The tutorial guide area becomes a live render of a real Buddy from approximately shoulders/chest upward:

- actual 3D Buddy geometry/materials;
- face compositor and current Buddy character visual language;
- dedicated camera crop for head + shoulders;
- transparent or Win98-panel-compatible background;
- no screenshot capture;
- no dependency on the live gameplay Buddy's current ragdoll pose;
- no gameplay physics, damage, economy or autonomy simulation.

Use a dedicated `SubViewport` and a physics-free pose source derived from/reusing `StaticBuddyVisualTransformSource`.

## C2. Portrait pose source

`StaticBuddyVisualTransformSource` is already immutable and physics-free. Add a small portrait-specific presentation controller around it rather than altering it into a tutorial class.

The portrait controller owns presentation-only state such as:

- head yaw/pitch drift;
- tiny shoulder/body idle movement;
- blink state;
- tutorial expression override;
- mouth/talk phase;
- optional eye look direction.

If the existing `BuddyVisualPresenter` needs a mutable read-only source for these presentation values, introduce a reusable `PreviewBuddyVisualTransformSource`/`PortraitBuddyPoseSource` that still implements `IBuddyVisualTransformSource` and has no gameplay authority.

## C3. Blinking

Blinking is independent from typewriter text:

- natural randomized interval in a bounded deterministic range;
- short close/open duration;
- do not blink continuously while hidden;
- reduced motion may keep blinking because it is character animation rather than UI travel, but expose one policy decision in code so this can be changed centrally.

Prefer reusing the face compositor's existing blink render state rather than inventing separate eyelid geometry.

## C4. Talking mouth

The portrait listens to `ExpressiveTextPresenter` reveal events.

While glyphs are actively revealing:

- alternate between a small set of mouth-open states at a human-readable cadence;
- do not map one mouth flap to one character;
- punctuation pauses naturally close/rest the mouth;
- skipping the reveal immediately returns the mouth to rest;
- after reveal completion, the Buddy returns to the step's resting expression.

This is visual pseudo-speech only. No phoneme/voice synthesis system is needed.

Where possible, drive this through the existing face-composition model (`FaceRenderState` / mouth pose) so the tutorial Buddy and live Buddy still share one face renderer.

## C5. Tutorial expression cues

Add semantic portrait moods to tutorial presentation, separate from raw face strings:

- `Neutral`
- `Friendly`
- `Pleased`
- `Proud`
- `Curious`
- `Concerned`

Map tutorial step IDs to these moods in one presentation-only table.

Examples:

- first greeting: Friendly;
- charged bat explanation: Curious/encouraging, not fearful;
- `admire_painted_buddy`: Pleased/Proud;
- `admire_studio_buddy`: Pleased/Proud;
- farewell: Friendly/Pleased.

The two compliment steps should visibly smile. The mouth animation during typing temporarily animates from that underlying mood and settles back into the smile when the line finishes.

Do not mutate the live gameplay Buddy's mood/reaction state to achieve tutorial expressions.

## C6. Character appearance

First implementation should use one authored tutorial Buddy appearance rather than whatever character the player currently has selected. This keeps the guide recognizable and prevents the narrator from visually changing halfway through the tutorial.

The appearance definition should be data-driven so the owner can later change the tutorial Buddy's colors/cosmetics without touching controller code.

---

# Integration with the current tutorial

`FirstSessionGuidanceController` should change as little as possible:

1. `TextFor(stepId)` produces/looks up expressive authored copy rather than being directly assigned to a plain Label.
2. The existing tutorial window embeds `ExpressiveTextPresenter` in the left text region.
3. `DemoTutorialCharacterPresenter` becomes the host/coordinator for `TutorialBuddyPortrait` in the right region.
4. When a new step is shown, the controller passes:
   - step ID;
   - expressive document;
   - portrait mood;
   - voice profile.
5. Tutorial progression still advances only from the existing real gameplay action checks.
6. Re-rendering conditional text for a live-state tutorial step must update correctly without replaying already-finished text unless the semantic line actually changed.
7. `Skip Tutorial` immediately silences text voice and tears down/hides portrait activity.

The Work Mode helper window should use the same dialogue presenter and portrait host where layout allows, rather than falling back to plain text just because it is a separate Window.

---

# Suggested authored emphasis for the tutorial first pass

Do not animate every sentence. Most words remain ordinary.

High-value emphasis targets:

- `left mouse button`, `right mouse button`, `D`, `Save`, `Use Character`, `Buy`, `Equip`, `X` — input/action styling;
- `Credits` — money green;
- `big swing` — brief wave/force emphasis;
- `Paint away!` — playful wave;
- `Beautiful!` — pleased emphasis paired with portrait smile;
- `Spray tool!` — small excited emphasis;
- `Button nose` — playful emphasis;
- `Now that is what I call a nose.` — stronger comedic emphasis + smile;
- farewell `best of buds` — gentle wave/positive emphasis.

Instruction-heavy lines should never become harder to scan because of animation.

---

# Implementation sequence

## Phase 1 — engine-independent contracts + tests

1. Add expressive text document/run/effect model.
2. Add safe tag parser and plain-text projection.
3. Add reveal timing model with punctuation delays and skip behavior.
4. Add deterministic unit tests for parsing, reveal order, punctuation pauses and skip-to-complete.
5. Add voice cadence model tests so spaces/punctuation do not spam SFX.

No tutorial UI changes yet.

## Phase 2 — Godot expressive dialogue presenter

1. Build the reusable presenter.
2. Implement per-run styling and low-amplitude glyph effects.
3. Add motion-policy fallback.
4. Route text chirps through UI audio settings/bus.
5. Add a focused scenario that renders several effects, wraps text and captures the final/animated states.

## Phase 3 — live tutorial Buddy portrait

1. Build a `SubViewport` portrait scene/control.
2. Reuse the Buddy 3D visual rig + physics-free pose source.
3. Add portrait camera/lighting/framing.
4. Add blink controller.
5. Add mouth-talking controller driven by reveal events.
6. Add semantic portrait mood mapping and smile states.
7. Replace the 2D procedural guide in the tutorial window.
8. Add scenario coverage proving there is no `BuddyRoot`/`RigidBody2D` gameplay authority inside the portrait tree.

## Phase 4 — tutorial integration/copy markup

1. Replace tutorial body Label usage with `ExpressiveTextPresenter`.
2. Mark up selected tutorial words only.
3. Bind portrait expression per step.
4. Preserve all 32 existing tutorial semantic steps and completion gates.
5. Verify Continue/Skip/Goodbye and Work helper-window behavior.
6. Verify replay and conditional prompt variants do not restart unexpectedly.

## Phase 5 — WordArt celebration system

1. Build original WordArt-style title renderer/presets.
2. Add static reduced-motion rendering.
3. Integrate inside the existing reward queue.
4. Apply to tool purchases.
5. Apply to Work Mode milestones.
6. Tune current popup glow/breathing if the combination becomes visually redundant.

## Phase 6 — release and regression gates

1. Fresh Steam/demo tutorial walkthrough from Grab Buddy through farewell.
2. Tutorial replay from Settings.
3. Owned-bat replay path.
4. Full character-slot conditional path.
5. Buddy-already-wearing-nose path.
6. Work Mode separate-window tutorial path.
7. Motion disabled.
8. Interface sounds disabled.
9. Photosensitivity-safe defaults.
10. Tool purchase queue with rapid consecutive rewards.
11. Multiple Work milestones crossing on one drain.
12. Localization-safe parser/tag fallback tests.
13. Full existing CI/test suite.

Itch.io remains out of the manual/release gate for this feature until its feature scope changes.

---

# Non-goals

- replacing Steam's native achievement UI;
- adding voiced human dialogue;
- phoneme/lip-sync analysis;
- gameplay Buddy physics inside the tutorial portrait;
- changing tutorial completion logic;
- adding new tutorial steps merely to show off the effects;
- animating Help hover copy, Settings descriptions, Workshop progress text or normal tool descriptions;
- celebratory Paint Buddy/Buddy Studio save text in this first pass;
- copying Microsoft WordArt assets/fonts.

---

# Acceptance criteria

The work is complete when:

- tutorial dialogue visibly types with punctuation-aware timing and optional semantic emphasis;
- tutorial speech chirps feel like one recognizable guide voice and obey audio settings;
- clicking during reveal completes the current line immediately without accidentally advancing tutorial state;
- the right side of the tutorial window contains a real live-rendered Buddy head/shoulders portrait, not procedural 2D art or a screenshot;
- that Buddy blinks, talks while text reveals, and visibly smiles on compliment/farewell beats;
- portrait animation has no gameplay physics/economy/autonomy authority;
- tool purchases and Work milestones can use reusable original WordArt-style animated title presets;
- motion-disabled mode preserves readable static presentation;
- Steam's native achievement notification is unaffected;
- all existing tutorial semantic gates and reward queues still pass their regression tests.
