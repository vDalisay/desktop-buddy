# Expressive Text + Tutorial Buddy Plan

Date: 2026-09-06
Branch: `expressive-text-tutorial-buddy-plan`
Status: AUDITED — READY FOR IMPLEMENTATION, NO FEATURE CODE STARTED

## Goal

Add a reusable expressive-presentation layer that gives Desktop Buddy more authored personality while preserving the current gameplay, tutorial, reward, accessibility, localization and rendering authorities.

First consumers:

1. **Tutorial dialogue** — localized typewriter reveal, semantic emphasis, quiet speech-like computer chirps and a live rendered tutorial Buddy portrait.
2. **Tool purchase/unlock rewards** — short original late-90s Office/WordArt-inspired title treatment inside the existing reward popup.
3. **Work Mode milestones** — the same reward presentation architecture with milestone-specific copy/style.

Explicitly excluded from the first pass:

- Steam's native achievement notification: Desktop Buddy does not own that UI and must not attempt to animate or replace it.
- ordinary Paint Buddy / Buddy Studio Save, Use and Equip messages;
- Help hover text, Settings descriptions, Workshop progress text and normal tool descriptions;
- itch.io tutorial wiring. The shared architecture may compile there, but existing `DemoScope` remains the feature gate and itch currently omits the tutorial/Work/Paint Room/Buddy Studio.

---

# Architecture audit verdict

The original plan was directionally correct, but four parts need to change before implementation.

## 1. Localization must be a first-class input, not markup added after English copy

There is already localization work in `feature/localization-ru`: Godot PO resources, a persisted language setting, runtime `TranslationServer.SetLocale`, translated tutorial lines and translated tool names/descriptions. That branch is currently diverged from `main`, so it must **not** be merged wholesale into this branch, but the new architecture must be compatible with the contract it establishes.

The Russian copy demonstrates why English character offsets or phrase matching are invalid: translated sentences reorder, inflect and sometimes paraphrase the emphasized phrase. The new system therefore must not search the translated result for English words and must not ask translators to preserve character positions.

The first draft's `[wave]...[/wave]`-style tags inside translator prose are also rejected as the primary authoring model. They mix grammar with rendering syntax and are easy to break during translation.

### Revised localization contract

Use **stable localization keys + named semantic slots**.

Conceptual example:

- message key: `tutorial.charged_bat`
- English fallback template: `Hold {input_secondary} to charge a {impact}.`
- `input_secondary` value: localized/current display text for the secondary mouse input, semantic style `Input`
- `impact` value: localized phrase for the sentence, semantic style `Impact`

A translator may freely reorder the placeholders. Code styles the semantic slot after localization rather than styling fixed character ranges.

New expressive messages should use stable keys even while older UI still uses source-English gettext msgids. A localization adapter may temporarily support both during migration:

1. stable key lookup;
2. English fallback template if untranslated;
3. optional legacy source-msgid fallback only where an existing PO entry must be preserved during migration.

The expressive presenter itself must never call `TranslationServer` directly.

### Required localization safety

- Validate named placeholders in every translated expressive template.
- Unknown/missing/duplicated required placeholders never strand the tutorial: log the problem and fall back to the English template, with safe/static styling if necessary.
- Plain-text projection must always be available for tests and accessibility.
- Dynamic counts use plural-aware localization rather than English concatenation.
- Do not force uppercase: casing rules differ by language.
- Measure the final localized string before rendering WordArt or dialogue.
- Add long-string, Cyrillic, CJK and at least one RTL/pseudo-RTL layout fixture even before all of those locales ship.
- Translator notes must describe each placeholder's meaning, not its visual implementation.

## 2. The tutorial already has a rebindable input that the current copy hardcodes

`Drop Tool` is no longer always `D`; `LocalSettingsInputBindings.DropTool(settings)` is the actual machine-local binding and is applied through the existing input-map seam.

The expressive system should fix this existing mismatch rather than preserve it.

Introduce an input-prompt adapter that resolves semantic input actions to display text. Tutorial copy references an input action/slot, not the literal key.

Examples:

- `InputActions.DropTool` -> current configured chord;
- primary/secondary mouse -> localized display phrase;
- later controller bindings can use the same seam without rewriting tutorial copy.

## 3. The live portrait rendering stack already exists in another form

Work Mode already constructs exactly the rendering pattern the tutorial portrait needs:

`SubViewportContainer` -> transparent `SubViewport` with its own `World3D` -> `StaticBuddyVisualTransformSource` -> `BuddyVisualRigView` -> appearance -> orthographic camera -> light.

Workshop/editor previews also use the same static visual source.

Do not create a second tutorial-only 3D rendering stack. Extract/reuse a small offscreen Buddy preview composition/facade from this proven pattern. The tutorial provides pose/crop/face data; it does not know how to build cameras, lights, rig nodes or appearance plumbing.

Also reuse the existing engine-free `BlinkModel` and `FaceRenderState`/face painter. Do not invent another blink algorithm or another face renderer.

## 4. Accessibility already has a stronger presentation boundary than the draft assumed

`EffectsSettings` is explicitly presentation-only and existing tests assert that changing it cannot change gameplay. `Win98MotionPolicy` separately combines the user's aesthetic `ModernUiMotion` preference with `ReducedMotion`.

New effects must consume these existing policies rather than scatter one-off booleans through each presenter.

Rules:

- WordArt entry/wobble/wave and animated text distortion require `Win98MotionPolicy.Allows`.
- Photosensitivity and particle decisions use `EffectsSettings`.
- text remains visible and readable when motion is removed;
- portrait blink/talk/idle animation is character animation, not generic UI travel, so one centralized portrait-animation policy decides which parts survive Reduced Motion. Do not make that decision independently in three classes.
- tutorial chirps use the existing UI audio bus. `Interface Sounds` is currently a UI-volume slider, so `UiVolume == 0` naturally silences them; do not add a redundant sound toggle in this feature.

---

# Design patterns and boundaries

Use patterns only where they protect an existing authority. Prefer composition over inheritance and avoid a new global manager.

## Ports and Adapters

Pure presentation models depend on small ports; Godot/runtime services implement them.

Suggested ports:

- `ITextLocalizer` — resolves a stable key/template, locale/context/plural form;
- `IInputPromptResolver` — returns current human-readable input binding text;
- `ITextVoiceSink` — plays/stops semantic text-voice cues;
- optional `IPresentationClock` / deterministic time source for pure timing tests.

Adapters:

- Godot `TranslationServer` -> `ITextLocalizer`;
- `LocalSettingsInputBindings` / `InputMap` -> `IInputPromptResolver`;
- `UiFeedbackAudioBootstrap` -> `ITextVoiceSink`.

No presenter reaches sideways into a singleton to discover policy when the owning composition can supply the dependency.

## Immutable value objects / Parameter Objects

Do not pass long lists of primitive arguments.

Examples:

- `ExpressiveMessageSpec`
- `ExpressiveSlotValue`
- `TutorialGuideCue`
- `RewardPresentationRequest`
- `WordArtStylePreset`

These describe **what** should be presented; Godot views decide **how**.

## Composite / small AST

After localization/template expansion, an `ExpressiveTextDocument` is a sequence/tree of literal runs and semantic runs. The renderer receives this document and does not parse business/tutorial IDs.

## State pattern / explicit finite state machine

Reveal behavior is a pure explicit state machine, for example:

`Idle -> Revealing -> PunctuationPause -> Revealing -> Complete`

Skip-to-complete is an explicit transition, not a collection of timer hacks.

## Strategy / resolver

Use strategies for choices that may vary without changing callers:

- reveal timing/cadence policy;
- semantic text effect -> visual treatment;
- portrait mood -> face pose;
- reward semantic kind -> WordArt/chrome preset.

Callers should never choose raw shader parameters, wave amplitudes or gradient colors.

## Facade

Two facades keep orchestration out of existing controllers:

1. `TutorialGuideView` / `ITutorialGuidePresenter` owns expressive text + voice + portrait as one presentation unit.
2. `BuddyPreviewSurface` owns the reusable offscreen Buddy render composition.

`FirstSessionGuidanceController` continues to own tutorial state, spotlights, input locks and gameplay completion checks. It should not become the mouth-animation/audio controller.

## Observer/events

The expressive text view may emit lifecycle events (`RevealStarted`, `SpeakingChanged`, `RevealCompleted`) to its containing guide facade. The facade coordinates portrait mouth state and audio. Tutorial gameplay progression does **not** subscribe to per-glyph events.

---

# System A — localized expressive dialogue

## A1. Message model

Engine-independent model under the presentation/domain boundary:

- `ExpressiveMessageSpec`: localization key + fallback template + context + slot definitions;
- `ExpressiveSlotValue`: slot name, localized/dynamic value, semantic role;
- `ExpressiveTextDocument`: resolved runs;
- `ExpressiveSemanticRole`: `Plain`, `Action`, `Input`, `Money`, `Impact`, `Playful`, `Warning`, etc.;
- `TextVoiceProfileId`.

Keep semantic roles meaningful. `Wave` and `Shake` are renderer decisions/preset mappings, not grammar concepts embedded into translated text.

## A2. Template expansion

Pipeline:

1. owning presenter requests a message spec;
2. localizer resolves the locale-specific template;
3. dynamic slot values are resolved (including current input binding);
4. template validator verifies required slot names;
5. formatter builds an `ExpressiveTextDocument` preserving semantic run boundaries;
6. renderer lays out the completed localized document.

The template language supports only named placeholders. No arbitrary code, conditions or general scripting.

Conditional tutorial variants remain separate stable message keys/variant IDs rather than prose-level condition syntax.

## A3. Unicode-safe reveal model

Never use C# `char` as "one visible character". It is UTF-16 and can split surrogate pairs/combining sequences.

Reveal timing works in Unicode **grapheme clusters/text elements**. Punctuation classification is Unicode-aware.

The Godot view lays out/shapes the complete localized text first, then reveals it without changing wrapping as letters appear. This prevents the tutorial box from reflowing every few glyphs.

For future RTL languages, the renderer follows the locale/text direction rather than assuming left-to-right reveal order.

## A4. Godot renderer

Use one `RichTextLabel`-backed presenter as shaping/wrapping authority. Do not create one `Control`/`Label` per glyph.

A small custom `RichTextEffect` layer may supply per-glyph offsets/transform for selected semantic runs. Requirements:

- static color/weight styling remains readable with motion disabled;
- effects never alter line measurement;
- whole-pixel snapping where appropriate for the Win98 shell;
- no per-frame allocations proportional to text length;
- animated effects stop processing once a line/effect has settled;
- hidden tutorial views do not keep RichText effects ticking in the background.

Initial semantic visual mapping:

- `Input` / required control: Win98-title blue/strong weight;
- `Money`: existing money green;
- `Impact`: short low-amplitude force/wave treatment;
- `Playful`: gentle wave;
- `Warning`: color/weight only;
- ordinary prose: no effect.

## A5. Typewriter behavior

- configurable base rate;
- punctuation-aware pauses;
- no speech chirp on whitespace/punctuation;
- voice cadence every few visible text elements, not every code unit;
- first confirm/click while revealing completes the line only;
- completion never advances a gameplay tutorial step by itself;
- replacing a semantic line resets reveal exactly once;
- re-rendering the same line identity is idempotent.

The identity must be semantic (`step + variant + locale/version`), not `_lastRenderedText` string equality. This avoids brittle behavior when locale or dynamic binding text changes.

If the locale changes while a tutorial line is visible, re-resolve the current cue and show the newly localized line immediately complete rather than replaying the whole spoken animation a second time.

## A6. Text voice

`ExpressiveTextPresenter` does not own `AudioStreamPlayer`s.

A voice cadence model decides when a semantic chirp is requested. `ITextVoiceSink` adapts this to `UiFeedbackAudioBootstrap`, preserving:

- pooled UI voices;
- UI bus volume;
- existing audio lifecycle;
- future alternate guide voices without duplicating reveal logic.

Tutorial guide direction: 3–5 closely related short synthetic computer chirps with subtle pitch variation, quiet enough for a 32-step tutorial.

---

# System B — WordArt-style reward presentation

## B1. Preserve the existing queue

`RewardPopup` remains the only notification queue/timing owner. Do not add a `WordArtManager` or second queue.

WordArt is a view component inside the existing popup.

## B2. Semantic reward request

Replace the presenter's dependence on raw title/style decisions with a semantic request at the UI boundary, e.g.:

- `ToolPurchase`
- `WorkSessionMilestone`
- `WorkLifetimeMilestone`

The request carries semantic data needed to build localized copy (content ID, threshold/counter/scope, amount, icon ID). It does not carry `MoneyBurst`, `BigDeal`, wave amplitude or gradient colors.

A `RewardPresentationStyleResolver` maps semantic reward kind/importance to an original WordArt preset.

## B3. Localization debt to fix at integration points

`ContentDisplayName.For` currently derives English tool names and explicitly documents that it should move to the localization table. Do not make WordArt depend permanently on that English derivation.

`WorkCompanionCoordinator.DescribeMilestone` currently concatenates English `actions/clicks/keystrokes` and formats the threshold invariantly. New reward presentation should instead resolve a localized/plural-aware milestone message from semantic milestone data.

Keep economy/domain milestone logic untouched; this is a presentation adapter migration only.

Number-format policy should be decided deliberately during localization integration. Do not silently change the authoritative credit formatting in this feature.

## B4. WordArt renderer

Original late-90s Office/WordArt visual vocabulary, without copying Microsoft assets/fonts:

- outline;
- chunky drop/extruded shadow;
- simple gradient/fill;
- shallow arch/wave baseline;
- skew/perspective-like transform;
- entry squash/overshoot;
- short settle wobble.

Presets are immutable data/resources and callers never tune raw parameters.

Localized-title fit rules:

- measure the final translated title first;
- never force uppercase;
- support Cyrillic/CJK font fallback;
- bounded scale-down with a minimum legibility floor;
- allow a controlled two-line/static fallback for long translations;
- if a transform harms readability, resolver falls back to a flatter preset rather than clipping text.

## B5. Existing reward chrome

Do not stack every existing glow/breath effect plus every WordArt effect by default. Style resolution may select a simpler reward-chrome profile when WordArt supplies the visual punch.

Reduced motion renders the final WordArt pose statically. Photosensitivity-safe mode forbids new brightness pulsing. Reduced-particle policy applies if any glint/particle primitive is introduced later.

---

# System C — live tutorial Buddy portrait

## C1. Reusable offscreen Buddy preview surface

Extract/reuse the proven Work/Workshop/editor preview composition:

- transparent `SubViewport` / own `World3D`;
- `BuddyVisualRigView`;
- physics-free visual transform source;
- same appearance pipeline/materials/surface underlays;
- orthographic camera;
- shared look/lighting setup where practical.

`BuddyPreviewSurface` is a rendering facade. It accepts visual profile, appearance, pose/face state and framing options. It contains no tutorial logic.

Avoid a risky broad Work Mode rewrite merely for purity: extract the smallest common helper, add parity coverage, then migrate Work to it only if the helper can replace the proven setup without changing Work output/behavior.

## C2. Portrait model

A presentation-only `TutorialPortraitModel`/`PortraitFaceModel` owns:

- semantic tutorial mood;
- deterministic idle/look offsets;
- `BlinkModel` state;
- speaking/rest mouth phase;
- pupil/look intent.

Output is existing render data (`BuddyVisualPoseFrame` / `FaceRenderState`). It never owns `BuddyRoot`, `RigidBody2D`, damage, mood, economy, autonomy or gameplay RNG.

## C3. Reuse existing blink and face systems

Use the existing engine-free `BlinkModel`; do not duplicate its random interval/suppression rules.

Use the existing face painter via `FaceRenderState`. Do not mutate the live Buddy's semantic reaction state to make the tutorial guide smile/talk.

Talking is a presentation overlay over the tutorial mood. Do not abuse the existing chew animation as speech. If the current mouth enum lacks a visually suitable neutral speaking pose, extend the presentation pose vocabulary deliberately and add renderer coverage rather than pretending eating is talking.

## C4. Tutorial moods

Semantic, presentation-only moods such as:

- Neutral
- Friendly
- Curious
- Pleased
- Proud
- Concerned

A catalog/strategy maps these to face feature poses. Step metadata chooses the mood; `FirstSessionGuidanceController` does not contain face-string switches.

Compliment/farewell lines settle into a visible smile after speaking finishes.

## C5. Portrait pose and framing

- shoulders/chest upward;
- small deterministic idle/head movement;
- natural blink;
- mouth moves while text is actively revealing, rests over punctuation pauses and closes immediately when reveal is skipped/completed;
- one authored tutorial Buddy appearance, data-driven and stable throughout the tutorial;
- no screenshot capture.

SubViewport updates only while the portrait is visible/animated. Hidden portrait rendering must be suspended so a desktop-idler does not pay a permanent GPU cost for a tutorial that is no longer on screen.

---

# Tutorial integration

Replace the narrow legacy `ITutorialCharacterPresenter` with a guide-level facade such as:

`ITutorialGuidePresenter.Present(TutorialGuideCue cue)` / `Dismiss()`.

`TutorialGuideCue` contains:

- semantic step ID + variant ID;
- `ExpressiveMessageSpec`;
- portrait mood;
- voice profile ID;
- any semantic input-slot values required by the line.

Responsibilities remain strict:

### `FirstSessionGuidanceController`

Owns:

- tutorial progress/persistence;
- real gameplay completion checks;
- spotlight/input locks;
- which step/variant is active;
- Continue/Skip/Goodbye semantics.

Does **not** own:

- glyph timing;
- chirp cadence;
- portrait mouth state;
- blink state;
- WordArt rendering.

### `TutorialGuideView`

Owns:

- localized expressive text presentation;
- reveal/skip-to-complete interaction;
- guide voice;
- live portrait coordination.

The main tutorial window and the separate Work helper window host the same guide view/model. Do not maintain a second plain-Label dialogue implementation for Work Mode. The Work host may use a compact layout, but it must share the same text/reveal/portrait architecture.

---

# Performance constraints

Desktop Buddy is a long-running desktop application, so temporary visual effects must really be temporary.

- no one-node-per-glyph architecture;
- no per-frame allocation proportional to full text length;
- RichText custom effects stop processing once inactive/hidden;
- tutorial SubViewport rendering is disabled when the guide is hidden;
- WordArt nodes/effects live only for the reward popup lifetime;
- avoid object pools until profiling proves allocation churn is material;
- no additional permanent autoload/global manager for expressive presentation.

---

# Implementation sequence

## Phase 0 — localization integration boundary

Before animated UI:

1. Add `ITextLocalizer`, message specs, named-slot formatter and English fallback catalog.
2. Support migration compatibility with current/source-msgid gettext strings where needed, but make new expressive messages stable-key based.
3. Add `IInputPromptResolver` and wire Drop Tool to the actual configured binding.
4. Add placeholder-contract validation tests and PO/localization fixtures.
5. Keep changes additive/narrow because the existing `feature/localization-ru` branch is diverged and touches the same controller/settings files.

## Phase 1 — pure expressive-text models

1. immutable document/run/semantic-role model;
2. grapheme-aware reveal state machine;
3. Unicode punctuation timing policy;
4. voice cadence policy;
5. deterministic tests for skip/idempotence/replacement.

## Phase 2 — Godot dialogue presenter

1. `RichTextLabel`-based shaping/layout;
2. semantic style/effect resolver;
3. post-layout reveal with stable wrapping;
4. motion/accessibility policy integration;
5. `ITextVoiceSink` adapter through existing UI audio;
6. rendering scenario at UI scales 100–200%.

## Phase 3 — reusable Buddy preview + tutorial portrait

1. extract reusable offscreen Buddy preview facade from the proven preview pattern;
2. build presentation-only portrait model using existing `BlinkModel` and `FaceRenderState`;
3. implement shoulders-up camera/framing and authored guide appearance;
4. talking mouth + mood strategy;
5. prove no gameplay nodes/authority exist in portrait tree;
6. prove hidden portrait stops updating.

## Phase 4 — tutorial integration

1. introduce `TutorialGuideCue` + guide facade;
2. replace main tutorial plain body label;
3. replace Work helper plain dialogue path with same guide architecture;
4. migrate selected lines to stable localized templates/semantic slots;
5. preserve all existing tutorial semantic gates, replay paths and conditional variants;
6. locale change while visible re-renders current line safely;
7. first click completes reveal without advancing tutorial state.

## Phase 5 — reward/WordArt integration

1. introduce semantic reward-presentation request;
2. localize tool purchase title through content localization boundary;
3. localize/pluralize Work milestone copy from semantic data;
4. build original WordArt renderer/preset resolver;
5. compose it inside existing `RewardPopup` queue;
6. apply to tool purchases and Work milestones;
7. tune/suppress redundant existing reward motion where necessary.

## Phase 6 — release/regression gate

- fresh Steam/demo tutorial from Grab Buddy through farewell;
- replay tutorial;
- owned-bat route;
- full-character-slot route;
- Buddy-already-wearing-nose route;
- Work separate-window route;
- rebound Drop Tool key reflected in tutorial copy;
- Russian localized tutorial fixture / long translated copy;
- grapheme fixture with combining marks/emoji;
- CJK no-space wrapping fixture;
- RTL/pseudo-RTL direction fixture;
- locale switch while tutorial visible;
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

# Localization branch / merge strategy

`feature/localization-ru` is currently ahead of its old base but substantially behind current `main`, and overlaps likely integration files (`ProgressSave`, `FirstSessionGuidanceController`, Settings, `ContentDisplayName`, `project.godot`).

Therefore:

- do not merge the stale branch into this planning branch;
- design against its intended contract (PO translation resource, machine-local language selection, runtime locale application);
- keep this implementation mostly additive/new-file based until the localization work is rebased/landed;
- after localization lands, migrate the expressive catalogs/PO entries through the shared localization adapter instead of creating a competing path.

Expected conflict hotspots are documented up front so implementation can minimize churn in them.

---

# Non-goals

- changing tutorial progression/economy/domain rules;
- replacing Steam native achievement UI;
- gameplay physics/autonomy in the portrait;
- human voice acting or phoneme analysis;
- a general-purpose rich-text scripting language;
- a second reward queue;
- a second localization framework;
- permanent animated Help/Settings/reference text;
- copying Microsoft WordArt assets or fonts.

---

# Acceptance criteria

The architecture is accepted when:

- expressive text is localized before rendering and uses validated semantic slots rather than English offsets;
- translated phrases may reorder freely without losing their semantic emphasis;
- tutorial key prompts reflect current input bindings;
- reveal timing is Unicode/grapheme safe and does not reflow lines while typing;
- text chirps route through the existing UI audio system and UI volume;
- motion/photosensitivity behavior is decided through existing presentation policy seams;
- the tutorial Buddy is a real live 3D Buddy render using the shared physics-free preview architecture;
- existing `BlinkModel` / face rendering are reused rather than duplicated;
- portrait speaking/smiling never touches live gameplay Buddy state;
- reward callers describe semantic reward events while `RewardPopup` remains the queue owner and a resolver chooses WordArt style;
- tool/Work reward copy is localization-ready rather than permanently English-derived;
- Steam's own achievement popup remains unaffected;
- hidden/settled expressive systems stop expensive processing;
- all existing tutorial/reward/domain regressions remain green.
