# Expressive Text + Tutorial Buddy Plan

Date: 2026-09-06
Branch: `expressive-text-tutorial-buddy-plan`
Status: AUDITED — READY FOR IMPLEMENTATION, NO FEATURE CODE STARTED

## Goal

Add a reusable expressive-presentation layer that gives Desktop Buddy more authored personality while preserving the current gameplay, tutorial, reward, accessibility, audio and Buddy-rendering authorities.

First consumers:

1. **Tutorial dialogue** — English typewriter reveal, semantic emphasis, quiet speech-like computer chirps and a live rendered tutorial Buddy portrait.
2. **Tool purchase/unlock rewards** — short original late-90s Office/WordArt-inspired title treatment inside the existing reward popup.
3. **Work Mode milestones** — the same reward presentation architecture with milestone-specific copy/style.

Explicitly excluded from the first pass:

- Steam's native achievement notification: Desktop Buddy does not own that UI and must not attempt to animate or replace it.
- ordinary Paint Buddy / Buddy Studio Save, Use and Equip messages;
- Help hover text, Settings descriptions, Workshop progress text and normal tool descriptions;
- itch.io tutorial wiring. Existing `DemoScope` remains the feature gate and itch currently omits the tutorial/Work/Paint Room/Buddy Studio;
- adding or integrating any non-English language.

---

# Architecture audit verdict

The original direction is sound, with four architectural constraints locked before implementation.

## 1. English-only now, localization-ready by construction

This feature does **not** implement localization, PO files, language settings, locale switching or integration with the existing Russian localization branch.

However, expressive effects must never be attached by English character offsets, substring searches or phrase matching. That would make later localization unnecessarily expensive.

### Authoring contract

Use **English message templates + named semantic slots**.

Conceptual example:

- English template: `Hold {input_secondary} to charge a {impact}.`
- `input_secondary` value: `right mouse button`, semantic role `Input`
- `impact` value: `big swing`, semantic role `Impact`

The formatter produces an `ExpressiveTextDocument` containing ordinary runs and semantic runs. The renderer only sees the resolved document and semantic roles.

This gives us the desired authoring workflow now:

- choose the English sentence;
- mark exactly which words/phrases receive emphasis by assigning them to named slots;
- choose a semantic role rather than raw animation parameters.

And it leaves a clean future localization path:

- a translated template can reorder `{input_secondary}` and `{impact}`;
- the same semantic slots retain the correct effects;
- no renderer, tutorial controller or effect code changes are required.

No localization adapter is built in this feature. The message source simply returns the English template. A future `ITextTemplateSource`/localization adapter can replace that source without changing the document formatter or renderer.

### Important future-proofing rules

- no hard-coded start/end character indices in tutorial data;
- no `IndexOf("big swing")`-style effect lookup;
- no effect syntax mixed directly into player-facing prose;
- semantic slot names describe meaning (`Input`, `Money`, `Impact`, `Playful`) rather than visual implementation (`Wave`, `ShakeBlue`);
- plain-text projection is always available;
- do not force assumptions into the renderer that only work for one exact English sentence.

This is all the localization work required now.

## 2. Input prompts should use the actual configured binding

This is useful independently of localization.

`Drop Tool` is rebindable through `LocalSettingsInputBindings.DropTool(settings)`, while the current tutorial copy still says `D`.

Introduce `IInputPromptResolver` so expressive tutorial data can request a semantic input binding instead of hard-coding it.

Examples:

- `InputActions.DropTool` -> current configured chord;
- primary mouse -> `left mouse button`;
- secondary mouse -> `right mouse button`.

The resulting text is then supplied as an `Input` semantic slot and receives the corresponding emphasis automatically.

This also leaves room for future controller/input schemes without rewriting tutorial prose architecture.

## 3. The live portrait rendering stack already exists in another form

Work Mode already constructs the rendering pattern the tutorial portrait needs:

`SubViewportContainer` -> transparent `SubViewport` with its own `World3D` -> `StaticBuddyVisualTransformSource` -> `BuddyVisualRigView` -> appearance -> orthographic camera -> light.

Workshop/editor previews also use the same static visual source.

Do not create a second tutorial-only 3D rendering stack. Extract/reuse a small offscreen Buddy preview composition/facade from this proven pattern. The tutorial provides pose/crop/face data; it does not know how to build cameras, lights, rig nodes or appearance plumbing.

Also reuse the existing engine-free `BlinkModel` and `FaceRenderState`/face painter. Do not invent another blink algorithm or another face renderer.

## 4. Accessibility already has a strong presentation boundary

`EffectsSettings` is explicitly presentation-only and existing tests assert that changing it cannot change gameplay. `Win98MotionPolicy` separately combines the user's aesthetic `ModernUiMotion` preference with `ReducedMotion`.

New effects consume those existing policies rather than introducing ad-hoc settings.

Rules:

- WordArt entry/wobble/wave and animated text distortion require `Win98MotionPolicy.Allows`;
- photosensitivity/particle decisions use `EffectsSettings`;
- static color/weight emphasis remains visible when motion is removed;
- one centralized portrait-animation policy decides what blink/talk/idle motion survives Reduced Motion;
- tutorial chirps use the existing UI audio bus and `UiVolume`; no new sound toggle.

---

# Design patterns and boundaries

Use patterns where they protect an existing authority. Prefer composition over inheritance and avoid a new global manager/autoload.

## Ports and Adapters

Pure presentation models depend on small ports; Godot/runtime services implement them.

Initial ports:

- `IInputPromptResolver` — current human-readable binding text;
- `ITextVoiceSink` — plays/stops semantic text-voice cues;
- optional `IPresentationClock` / deterministic time source for pure timing tests.

Future-only seam:

- the English template provider is behind a small interface/data boundary so a localization-backed provider can replace it later. Do **not** build the localization adapter now.

Adapters:

- `LocalSettingsInputBindings` / `InputMap` -> `IInputPromptResolver`;
- `UiFeedbackAudioBootstrap` -> `ITextVoiceSink`.

## Immutable value objects / Parameter Objects

Avoid long primitive argument lists.

Core values:

- `ExpressiveMessageSpec`
- `ExpressiveSlotValue`
- `ExpressiveTextDocument`
- `TutorialGuideCue`
- `RewardPresentationRequest`
- `WordArtStylePreset`

These describe **what** should be presented; Godot views decide **how**.

## Composite / small text AST

An `ExpressiveTextDocument` is an ordered collection of literal and semantic runs.

Example resolved document:

- Plain: `Hold `
- Input: `right mouse button`
- Plain: ` to charge a `
- Impact: `big swing`
- Plain: `.`

The renderer never parses tutorial step IDs or searches prose for effect targets.

## Explicit state machine

Reveal behavior is a pure finite state machine:

`Idle -> Revealing -> PunctuationPause -> Revealing -> Complete`

Skip-to-complete is an explicit transition, not scattered timer checks.

## Strategy / resolver

Use resolvers for decisions that may vary without changing callers:

- reveal timing/cadence policy;
- semantic role -> visual treatment;
- portrait mood -> face pose;
- reward semantic kind -> WordArt/chrome preset.

Callers never choose raw shader values, wave amplitudes or gradient colors.

## Facades

Two facades keep orchestration out of existing controllers:

1. `TutorialGuideView` / `ITutorialGuidePresenter` owns expressive text + voice + portrait as one presentation unit.
2. `BuddyPreviewSurface` owns reusable offscreen Buddy rendering.

`FirstSessionGuidanceController` continues to own tutorial state, spotlights, input locks and gameplay completion checks. It does not become a glyph/mouth/audio controller.

## Observer/events

The expressive text presenter may emit lifecycle events such as:

- `RevealStarted`
- `SpeakingChanged`
- `RevealCompleted`

The containing guide facade uses those to coordinate portrait mouth state and audio. Tutorial gameplay progression never subscribes to per-glyph events.

---

# System A — expressive tutorial dialogue

## A1. Message model

Engine-independent presentation model:

- `ExpressiveMessageSpec`: stable message ID, English template, slot definitions;
- `ExpressiveSlotValue`: slot name, resolved value, semantic role;
- `ExpressiveTextDocument`: resolved ordered runs;
- `ExpressiveSemanticRole`: `Plain`, `Action`, `Input`, `Money`, `Impact`, `Playful`, `Warning`, etc.;
- `TextVoiceProfileId`.

Keep semantic roles meaningful. `Wave` and `Shake` are renderer choices, not content semantics.

## A2. English template expansion

Pipeline:

1. owning presenter requests an English `ExpressiveMessageSpec`;
2. dynamic slots are resolved, including current input bindings;
3. formatter validates that all required named slots exist;
4. formatter produces `ExpressiveTextDocument` preserving semantic run boundaries;
5. renderer lays out the complete document before reveal begins.

Template language supports named placeholders only. No arbitrary code, conditions or general scripting.

Conditional tutorial variants remain separate message/variant IDs rather than prose-level conditions.

Future localization can replace step 1 with a translated template provider while retaining steps 2–5 unchanged.

## A3. Reveal model

Use text elements/grapheme clusters rather than raw C# `char` where practical. This is cheap correctness now and avoids having to replace the reveal model later.

The view lays out the completed text first, then reveals it without changing line wrapping.

Behavior:

- configurable base reveal rate;
- slightly longer pauses for comma/sentence punctuation;
- no chirp on whitespace/punctuation;
- voice cadence every few visible text elements, not every letter;
- first confirm/click while revealing completes the current line only;
- reveal completion never advances a gameplay tutorial step by itself;
- replacing a message ID/variant resets reveal once;
- re-presenting the same cue is idempotent.

Line identity is semantic (`step + variant + message id`), not `_lastRenderedText` string equality.

## A4. Godot renderer

Use one `RichTextLabel`-backed presenter as shaping/wrapping authority. Do not create one `Label`/`Control` per glyph.

A small custom `RichTextEffect` layer may provide per-glyph offsets/transforms for selected semantic runs.

Requirements:

- effects never alter line measurement;
- static styling remains readable when motion is disabled;
- whole-pixel snapping where appropriate for the Win98 shell;
- no per-frame allocations proportional to full text length;
- settled/hidden effects stop processing;
- hidden tutorial views do not keep animation processing alive.

Initial semantic visual mapping:

- `Input` / required control -> strong Win98-blue emphasis;
- `Money` -> existing money green;
- `Impact` -> brief low-amplitude force/wave treatment;
- `Playful` -> gentle wave;
- `Warning` -> color/weight only;
- ordinary prose -> no effect.

## A5. Text voice

`ExpressiveTextPresenter` does not own `AudioStreamPlayer`s.

A pure cadence model decides when a chirp should occur. `ITextVoiceSink` adapts those requests to `UiFeedbackAudioBootstrap`.

Tutorial guide voice direction:

- short synthetic 90s-computer chirps;
- 3–5 closely related variants/pitches;
- subtle pitch variation;
- quiet enough for the full tutorial;
- stops immediately on reveal skip/completion;
- obeys the existing UI audio bus/volume.

---

# System B — WordArt-style reward presentation

## B1. Preserve the existing queue

`RewardPopup` remains the only notification queue/timing owner. Do not add a `WordArtManager` or second queue.

WordArt is a view component composed inside the existing popup.

## B2. Semantic reward request

Reward callers describe meaning, not style.

Initial kinds:

- `ToolPurchase`
- `WorkSessionMilestone`
- `WorkLifetimeMilestone`

A request carries semantic data such as title text/content ID, threshold/counter/scope, amount and icon ID. It never carries raw visual parameters.

`RewardPresentationStyleResolver` maps semantic reward kind/importance to an original WordArt preset.

English display text remains the source for now. Keep the request semantic enough that future localization can replace title construction without changing `RewardPopup` or the WordArt renderer.

## B3. WordArt renderer

Original late-90s Office/WordArt visual vocabulary, without copying Microsoft assets/fonts:

- outline;
- chunky drop/extruded shadow;
- simple gradient/fill;
- shallow arch/wave baseline;
- skew/perspective-like transform;
- entry squash/overshoot;
- short settle wobble.

Presets are immutable data/resources. Callers never tune raw parameters.

Fit rules:

- measure title before rendering;
- bounded scale-down with a minimum legibility floor;
- controlled two-line/static fallback for long titles;
- if a transform harms readability, use a flatter preset rather than clipping;
- do not rely on an all-uppercase-only layout.

## B4. Existing reward chrome

Do not stack every current glow/breath effect plus every WordArt effect by default. Style resolution may choose simpler surrounding chrome when WordArt supplies the visual punch.

Reduced Motion renders the final WordArt pose statically. Photosensitivity Safe forbids new brightness pulsing. Reduced Particles applies if glints/particles are added later.

---

# System C — live tutorial Buddy portrait

## C1. Reusable offscreen Buddy preview surface

Extract/reuse the proven Work/Workshop/editor preview composition:

- transparent `SubViewport` with own `World3D`;
- `BuddyVisualRigView`;
- physics-free visual transform source;
- same appearance/material/surface-underlay pipeline;
- orthographic camera;
- shared look/lighting setup where practical.

`BuddyPreviewSurface` is a rendering facade. It accepts visual profile, appearance, pose/face state and framing options. It contains no tutorial logic.

Avoid a broad Work Mode rewrite merely for abstraction purity. Extract the smallest common helper and migrate Work only if parity is proven.

## C2. Portrait model

A presentation-only `TutorialPortraitModel` owns:

- semantic tutorial mood;
- deterministic idle/look offsets;
- existing `BlinkModel` state;
- speaking/rest mouth phase;
- pupil/look intent.

Output is existing render data (`BuddyVisualPoseFrame` / `FaceRenderState`). It never owns `BuddyRoot`, `RigidBody2D`, damage, gameplay mood, economy, autonomy or gameplay RNG.

## C3. Reuse existing blink and face systems

Use the existing engine-free `BlinkModel` and existing face painter through `FaceRenderState`.

Do not mutate the live Buddy's semantic reaction state to make the tutorial guide smile/talk.

Talking is a presentation overlay over the tutorial mood. Do not reuse chewing as speech. If the current mouth vocabulary lacks a suitable speaking pose, extend the presentation pose vocabulary deliberately and add renderer coverage.

## C4. Tutorial moods

Presentation-only semantic moods:

- `Neutral`
- `Friendly`
- `Curious`
- `Pleased`
- `Proud`
- `Concerned`

A catalog/resolver maps moods to face feature poses. Step metadata chooses the mood; `FirstSessionGuidanceController` contains no face-string switches.

Compliment/farewell lines settle into a visible smile after speaking finishes.

## C5. Portrait pose and framing

- shoulders/chest upward;
- small deterministic idle/head movement;
- natural blinking;
- mouth moves while text is actively revealing;
- mouth rests during punctuation pauses and closes immediately when reveal is skipped/completed;
- one authored tutorial Buddy appearance, data-driven and stable throughout the tutorial;
- no screenshot capture;
- SubViewport updates only while portrait is visible/animated.

Hidden portrait rendering must be suspended so the desktop application does not pay permanent GPU cost after the tutorial is gone.

---

# Tutorial integration

Replace the narrow legacy `ITutorialCharacterPresenter` with a guide-level facade such as:

`ITutorialGuidePresenter.Present(TutorialGuideCue cue)` / `Dismiss()`.

`TutorialGuideCue` contains:

- semantic step ID + variant ID;
- `ExpressiveMessageSpec`;
- portrait mood;
- voice profile ID;
- dynamic semantic slot values required by the line.

### `FirstSessionGuidanceController` owns

- tutorial progress/persistence;
- real gameplay completion checks;
- spotlight/input locks;
- which step/variant is active;
- Continue/Skip/Goodbye semantics.

It does **not** own glyph timing, chirp cadence, portrait mouth state, blink state or WordArt rendering.

### `TutorialGuideView` owns

- expressive text presentation;
- reveal/skip-to-complete interaction;
- guide voice;
- live portrait coordination.

The main tutorial window and separate Work helper window host the same guide model/presenter. The Work host may use a compact layout, but it must not maintain a second plain-Label dialogue implementation.

---

# Suggested first-pass tutorial emphasis

Most words remain ordinary. Emphasis is authored only where it improves clarity or personality.

Examples:

- `left mouse button`, `right mouse button`, current Drop Tool binding, `Save`, `Buy`, `X` -> `Input`/`Action`;
- `Credits` -> `Money`;
- `big swing` -> `Impact`;
- `Paint away!` -> `Playful`;
- `Beautiful!` -> `Playful` + pleased portrait;
- `Spray tool!` -> `Playful`;
- `Button nose` -> `Playful`;
- `Now that is what I call a nose.` -> stronger playful/comedic treatment + smile;
- `best of buds` -> gentle positive/playful treatment.

Instruction-heavy lines must remain easy to scan.

---

# Performance constraints

Desktop Buddy is a long-running desktop application, so temporary visual effects must really be temporary.

- no one-node-per-glyph architecture;
- no per-frame allocation proportional to full text length;
- RichText custom effects stop processing once inactive/hidden;
- tutorial SubViewport rendering disables when the guide is hidden;
- WordArt nodes/effects live only for the reward popup lifetime;
- avoid pooling until profiling proves allocation churn is material;
- no additional permanent autoload/global manager for expressive presentation.

---

# Implementation sequence

## Phase 1 — pure expressive-text architecture

1. Add `ExpressiveMessageSpec`, named semantic slots, document/run model and role enum.
2. Add the English template formatter/validator.
3. Add `IInputPromptResolver` and resolve Drop Tool from the real current binding.
4. Add the reveal state machine and punctuation timing policy.
5. Add voice cadence policy.
6. Add deterministic tests for slot expansion, reveal, skip, idempotence and input binding resolution.

No localization implementation in this phase.

## Phase 2 — Godot dialogue presenter

1. `RichTextLabel`-based shaping/layout.
2. Semantic style/effect resolver.
3. Stable wrapping during reveal.
4. Motion/accessibility policy integration.
5. `ITextVoiceSink` adapter through existing UI audio.
6. Rendering scenario at UI scales 100–200%.

## Phase 3 — reusable Buddy preview + tutorial portrait

1. Extract reusable offscreen Buddy preview facade from the proven preview pattern.
2. Build presentation-only portrait model using existing `BlinkModel` and `FaceRenderState`.
3. Implement shoulders-up camera/framing and authored guide appearance.
4. Add talking mouth + mood resolver.
5. Prove no gameplay nodes/authority exist in portrait tree.
6. Prove hidden portrait stops updating.

## Phase 4 — tutorial integration

1. Introduce `TutorialGuideCue` + guide facade.
2. Replace main tutorial plain body label.
3. Replace Work helper plain dialogue path with the same guide architecture.
4. Convert selected English lines to named semantic slots.
5. Preserve all existing tutorial gates, replay paths and conditional variants.
6. First click completes reveal without advancing tutorial state.
7. Verify current Drop Tool binding appears in its tutorial prompt.

## Phase 5 — reward/WordArt integration

1. Introduce semantic reward-presentation request.
2. Build original WordArt renderer/preset resolver.
3. Compose it inside existing `RewardPopup` queue.
4. Apply to tool purchases.
5. Apply to Work Mode milestones.
6. Tune/suppress redundant existing reward motion where necessary.
7. Keep reward inputs semantic enough that localized title construction can be introduced later without touching the renderer.

## Phase 6 — release/regression gate

- fresh Steam/demo tutorial from Grab Buddy through farewell;
- replay tutorial;
- owned-bat route;
- full-character-slot route;
- Buddy-already-wearing-nose route;
- Work separate-window route;
- rebound Drop Tool key reflected in tutorial copy;
- UI scale 100/125/150/175/200%;
- Reduced Motion / Modern UI Motion off;
- Photosensitivity Safe;
- UI volume zero;
- rapid purchase reward queue;
- multiple Work milestones in one drain;
- portrait hidden/update suspension;
- full existing domain/scenario/CI suites.

Itch.io remains outside this feature's manual release gate until its shipped feature scope changes.

---

# Future localization contract — deliberately not implemented now

When localization is eventually added to this system, it should require only a different template provider/catalog:

- English: `Hold {input_secondary} to charge a {impact}.`
- another language may reorder those named slots freely;
- the formatter still produces the same semantic `Input` and `Impact` runs;
- the renderer/effect resolver remains unchanged.

At that time we can add locale-specific template validation, pluralization and international layout testing. None of that is part of the current feature.

This future contract is the reason named semantic slots exist now; it is **not** a request to build localization infrastructure early.

---

# Non-goals

- adding or integrating another language in this feature;
- changing tutorial progression/economy/domain rules;
- replacing Steam native achievement UI;
- gameplay physics/autonomy in the portrait;
- human voice acting or phoneme analysis;
- a general-purpose rich-text scripting language;
- a second reward queue;
- permanent animated Help/Settings/reference text;
- copying Microsoft WordArt assets or fonts.

---

# Acceptance criteria

The architecture is accepted when:

- English expressive text uses named semantic slots/runs rather than hard-coded character offsets;
- changing which English phrase receives an effect is an authoring/data change, not renderer code;
- the formatter boundary can later accept a translated/reordered template without changing the renderer;
- tutorial key prompts reflect current input bindings;
- reveal timing does not reflow lines while typing;
- text chirps route through the existing UI audio system and UI volume;
- motion/photosensitivity behavior is decided through existing presentation policy seams;
- the tutorial Buddy is a real live 3D Buddy render using the shared physics-free preview architecture;
- existing `BlinkModel` / face rendering are reused rather than duplicated;
- portrait speaking/smiling never touches live gameplay Buddy state;
- reward callers describe semantic reward events while `RewardPopup` remains the queue owner and a resolver chooses WordArt style;
- Steam's own achievement popup remains unaffected;
- hidden/settled expressive systems stop expensive processing;
- all existing tutorial/reward/domain regressions remain green.
