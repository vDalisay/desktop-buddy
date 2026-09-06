# Expressive Text + Tutorial Buddy Plan

Date: 2026-09-06
Branch: `expressive-text-tutorial-buddy-plan`
Status: AUDITED TWICE — READY FOR IMPLEMENTATION AFTER ONE OPEN OWNER DECISION, NO FEATURE CODE STARTED

> **Read the second audit before implementing.** A code-verified audit on 2026-09-07 confirmed every
> existing-code claim below but found that Godot 4.6 already ships most of System A's reveal and effect
> machinery natively, and that one authoring decision is still open. See
> [Implementation audit — 2026-09-07](#implementation-audit--2026-09-07) at the end of this document; where
> the two disagree, the audit wins.

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

---

# Implementation audit — 2026-09-07

A second audit, run against the actual code and against the actual engine binary rather than against
the plan's own reasoning.

**Verdict: the diagnosis is sound and the prescription is roughly three times the machinery the
problem needs.** Every existing-code claim above was checked and holds. Three of them are real
defects worth fixing regardless of whether this feature ships. But System A specifies a reveal
engine, a per-glyph effect layer and a template/document pipeline that Godot 4.6 substantially
provides already — a gap that went unnoticed because this project has never used `RichTextLabel`
(zero occurrences in `src/`), so its capabilities were never on the table.

## Claims verified against the code

Every load-bearing claim above is true. Recording the evidence so the next reader does not re-derive it:

| Claim in this plan | Verified at |
| --- | --- |
| `Drop Tool` is rebindable but tutorial copy hard-codes `D` | `domain/DesktopBuddy.Domain/Persistence/LocalSettingsInputBindings.cs:17` vs `src/Onboarding/FirstSessionGuidanceController.cs:1266` |
| Line identity is `_lastRenderedText` string equality | `src/Onboarding/FirstSessionGuidanceController.cs:297` |
| The Work helper keeps a second plain-`Label` dialogue path | `_workGuideBody`, `src/Onboarding/FirstSessionGuidanceController.cs:192`, built at `:1150`, written at `:1218` |
| The offscreen preview stack already exists | `src/Work/WorkCompanionView.cs:527`, `src/CharacterEditor/CharacterEditorHost.cs:557`, `src/Sharing/WorkshopPreviewCapture.cs:73` |
| `ITutorialCharacterPresenter` is narrow | `Present(stepId, text)` / `Dismiss()`, `src/Onboarding/FirstSessionGuidanceController.cs:29`; one implementation |
| `Win98MotionPolicy.Allows` combines `ModernUiMotion` and `ReducedMotion` | `src/UI/Win98/Win98MotionPolicy.cs:11` |
| `EffectsSettings` is presentation-only with `PhotosensitivitySafe` / `ReducedParticles` | `domain/DesktopBuddy.Domain/Presentation/EffectsSettings.cs:19` |
| `RewardPopup` owns a single queue | `_queue`, `src/UI/RewardPopup.cs:43` |
| `BlinkModel`, `FaceRenderState`, `StaticBuddyVisualTransformSource`, `BuddyVisualRigView`, `UiFeedbackAudioBootstrap` all exist as described | present across `src/` and `domain/` |
| No feature code started | `ExpressiveMessageSpec`, `IInputPromptResolver`, `ITextTemplateSource` return zero hits repo-wide |

Two sharper readings of those facts:

- **The preview stack is not duplicated once, it is triplicated.** Work, the character editor and
  Workshop capture each build their own `SubViewport` + `OwnWorld3D` + `StaticBuddyVisualTransformSource`
  + `BuddyVisualRigView` + orthographic `Camera3D` + `DirectionalLight3D`. That materially strengthens
  the case for C1 — and changes what a correct extraction looks like (see finding 4).
- **The `D` bug is shippable on its own.** It needs `LocalSettingsInputBindings.DropTool(settings)`
  interpolated into one string. It should not wait for six phases of architecture.

## Finding 1 — Godot 4.6 already implements most of System A (the big one)

This project uses `RichTextLabel` zero times, so A3/A4 were designed as if the reveal and the
per-glyph effects had to be built. They do not. Probed directly against
`Godot_v4.6.1-stable_mono_win64` (`ClassDB` property list plus a live `bbcode_enabled` parse):

```
PROP visible_characters = true      PROP visible_ratio = true
PROP visible_characters_behavior = true    PROP custom_effects = true
TAG wave -> parsed_text=abc         TAG shake -> parsed_text=abc
TAG tornado -> parsed_text=abc      TAG fade -> parsed_text=abc
TAG rainbow -> parsed_text=abc      TAG pulse -> parsed_text=abc
ENUM VisibleCharactersBehavior: [VC_CHARS_BEFORE_SHAPING, VC_CHARS_AFTER_SHAPING,
                                 VC_GLYPHS_AUTO, VC_GLYPHS_LTR, VC_GLYPHS_RTL]
METHODS: get_character_line=true get_parsed_text=true get_total_character_count=true
```

(The six animated tags are stripped from `get_parsed_text()`, which is how the parser reports that it
recognised them. `[color]` and `[font_size]` stay literal only because the probe passed them no
argument.)

What that means, requirement by requirement:

- **A3 "the view lays out the completed text first, then reveals it without changing line wrapping"** is
  exactly `visible_ratio` / `visible_characters` with `visible_characters_behavior` set to a
  post-shaping mode. The label shapes the whole string once and reveals glyphs out of that finished
  layout. There is no wrapping to protect, because nothing re-wraps. Do not hand-roll this.
- **A3 "use text elements/grapheme clusters rather than raw C# `char`"** is the `VC_GLYPHS_*` behaviors.
  The engine's `TextServer` already does the cluster work, correctly, for scripts we have not thought
  about. A hand-written grapheme walker is strictly worse.
- **A4 "a small custom `RichTextEffect` layer may provide per-glyph offsets"** is `[wave]`, `[shake]`,
  `[pulse]`, `[fade]`, `[tornado]`, `[rainbow]` — all six built in, all parameterised (`amp`, `freq`,
  `rate`, `level`), and all guaranteed by construction not to affect line measurement, which is the
  hard half of A4's requirement list. `custom_effects` remains available for the one case a built-in
  genuinely cannot express; it should not be the default plan.
- **A4 "semantic style/effect resolver"** collapses to a `Dictionary<ExpressiveSemanticRole, string>`
  mapping a role to a BBCode tag (plus a static-fallback tag for when motion is disallowed). That is a
  lookup table, not a strategy subsystem, and it preserves the plan's actual invariant: callers name
  meaning, the table owns the visuals.
- **Motion policy integration stays intact.** `Win98MotionPolicy.Allows(settings)` selects between the
  animated tag and the static one when building the BBCode string; `EffectsSettings.PhotosensitivitySafe`
  gates `[rainbow]`/`[pulse]`-class brightness modulation. Same seams, one branch each.

What is genuinely still ours to write, and worth writing:

- the **chirp cadence model** (A5) — pure, deterministic, testable, no engine equivalent;
- the **punctuation pause policy** (A3) — likewise pure; it drives how fast `visible_ratio` advances;
- **skip-to-complete and re-present idempotence** (A3) — but these are now two assignments
  (`visible_ratio = 1.0`, stop the chirp) rather than an FSM with a `PunctuationPause` state.

**Recommendation.** Rewrite A3/A4 as: one `RichTextLabel`, reveal driven by advancing `visible_ratio`
from a pure cadence model, semantic roles mapped to BBCode tags through a table, `custom_effects` only
on demonstrated need. Keep the explicit state machine only if implementation shows the two-assignment
version actually tangles; do not build it up front. This deletes the per-glyph effect layer, the glyph
walker and most of the reveal FSM from the estimate.

**Caveat that survives.** `RichTextLabel` is new to this codebase and `Win98ThemeFactory` has no
styling for it, so Phase 2 must include theme/font parity with the existing `Label` look — otherwise
the mismatch surfaces at the Phase 6 UI-scale gate, which is far too late. This is a new Phase 2 item;
see the revised sequence.

## Finding 2 — five interfaces, five single implementations

`IInputPromptResolver`, `ITextVoiceSink`, `IPresentationClock`, `ITextTemplateSource` and
`ITutorialGuidePresenter` would each have exactly one implementation, and two of them wrap statics that
already exist and are already easy to call: `LocalSettingsInputBindings.DropTool(settings)` and
`UiFeedbackAudioBootstrap.TryPlay(...)`.

The testability the ports are meant to buy comes from the **pure models**, not from the indirection.
A cadence model that takes a `string dropToolChord` and returns "chirp now" is fully testable without
a single interface; the view calls the static and passes the value in. That is the same guarantee at
five fewer types.

**Recommendation.** Keep pure models and their deterministic tests. Drop `IInputPromptResolver`,
`ITextVoiceSink` and `IPresentationClock` — pass values and a `double delta` in. Keep a guide-level
facade for the tutorial (finding 6 gives it a second consumer, which is what earns it). Introduce
`ITextTemplateSource` only when a second template source actually exists; until then it is a named
placeholder for a feature this plan explicitly refuses to build.

## Finding 3 — the slot/template pipeline is the largest cost, and its rationale is contestable (OPEN OWNER DECISION)

Section 1 forbids "effect syntax mixed directly into player-facing prose", and that single rule is what
requires `ExpressiveMessageSpec` + `ExpressiveSlotValue` + `ExpressiveTextDocument` + a formatter + a
slot validator. It is a defensible rule — translators should not be handed markup they can corrupt —
but it is the most expensive line in this document, and it is bought for a localization pass that
Section 1 also explicitly declines to build.

The alternative reaches the same stated goal for far less. Author the English line with inline semantic
tags:

```
Hold [input]right mouse button[/input] to charge a [impact]big swing[/impact].
```

That **is** a named-slot document. It satisfies every future-proofing rule in Section 1: no character
offsets, no `IndexOf`, semantic names rather than visual ones (`input`, not `wave`), a plain-text
projection available for free via `get_parsed_text()`. A translator may reorder the tagged spans
freely, which is the exact property the "Future localization contract" section asks for, and Godot's
own translation workflow carries BBCode through PO files routinely. The renderer's job becomes
substituting each semantic tag for its visual BBCode tag before assigning `Text` — a string replace
over a table.

The genuine trade, stated plainly so it can be decided rather than assumed:

- **Slots (as planned):** translators never see markup; costs ~5 types, a formatter and a validator;
  slot-name typos are caught by the validator.
- **Inline semantic tags:** near-zero new types; translators see `[input]…[/input]` and can break it;
  a malformed tag degrades to visible literal text rather than a thrown validation error.

**Recommendation: inline semantic tags**, on the grounds that no localization is planned, the failure
mode is cosmetic, and the ~5 types can be introduced later without touching the renderer if a real
translation pass ever arrives. **This is the one call left to the owner** — it is the difference between
a small Phase 1 and a large one, and the rest of this audit's sequencing assumes the recommendation is
taken. If slots are chosen instead, Phase 1 stands roughly as originally written.

## Finding 4 — C1 as written sanctions a fourth copy

"Avoid a broad Work Mode rewrite merely for abstraction purity. Extract the smallest common helper and
migrate Work only if parity is proven" permits an outcome where the extracted facade has exactly one
consumer — the tutorial portrait — while all three existing stacks stay as they are. Extraction with
one consumer is not extraction; it is a fourth copy with a better name.

**Recommendation.** Either extract *and* migrate all three call sites (`WorkCompanionView`,
`CharacterEditorHost`, `WorkshopPreviewCapture`), or copy the ~40 lines into the tutorial with a
comment naming the duplication and the trigger for consolidating. Both are honest; the middle option is
not. Finding 5 supplies the strongest argument for choosing the first.

## Finding 5 — gap: the suspension fix belongs to all four consumers, not just the tutorial

C5 and the performance section require the tutorial SubViewport to stop rendering when hidden. All
three existing stacks set `RenderTargetUpdateMode.Always` (`WorkCompanionView.cs:531`,
`CharacterEditorHost.cs:561`, `WorkshopPreviewCapture.cs:78`). So the plan asks the new portrait to be
frugal while leaving Work and the editor paying permanent GPU cost in a long-running desktop
application — the precise failure mode the performance section exists to prevent.

**Recommendation.** Make visibility-driven update mode a property of the extracted facade, and let the
migration in finding 4 carry the fix to all three existing consumers. This is the concrete payoff that
justifies extracting at all, and it should be stated as a Phase 3 deliverable rather than left implicit.

## Finding 6 — gap: `DemoTutorialCharacterPresenter`'s owner-art path is silently retired

`src/Onboarding/DemoTutorialCharacterPresenter.cs:9` documents a standing promise: dropping the owner's
final art at `res://assets/ui/tutorial/tutorial_guide.png` replaces the procedural placeholder without
code changes. A live 3D portrait ends that promise. The plan never says so, and an owner who later drops
that PNG in will find it silently ignored.

**Recommendation.** State explicitly in Phase 3 or 4 that the optional-art path is retired, and delete
`OptionalArtPath` and its loader with the replacement rather than leaving dead code that advertises a
behavior the build no longer has. This is also what gives the guide facade its second consumer, which
is why it survives finding 2.

## Finding 7 — speculative enum breadth

`ExpressiveSemanticRole` lists 7 roles; the plan's own "Suggested first-pass tutorial emphasis" section
uses 5 and never uses `Warning`. `TutorialPortraitModel` lists 6 moods for 38 tutorial strings
(`TextFor` in `FirstSessionGuidanceController.cs`), which is more mood vocabulary than the copy can
distinguish.

**Recommendation.** Ship the roles the copy actually uses (`Input`/`Action`, `Money`, `Impact`,
`Playful`) and three moods (`Neutral`, `Friendly`, `Pleased`). Adding a role is a table row and a mood
is a face pose; neither needs to exist before its first use.

## Finding 8 — sequencing: Phase 5 is independent and should go first

Phase 5 (WordArt rewards) depends on nothing in Phases 1–4. It touches one 326-line file whose queue
ownership is already correct, and it is the most visible personality-per-line-of-code in the document.
Running it first de-risks the schedule: if the expressive-text work is cut or deferred, the reward
polish has already shipped.

## Revised implementation sequence

Supersedes the sequence above. Stop at any point where it feels finished; each step is shippable alone.

0. **`D`-binding fix, standalone.** Interpolate `LocalSettingsInputBindings.DropTool(settings)` into the
   `UnequipTool` line. One string, no architecture, ship immediately.
1. **Phase 5 as written** (rewards/WordArt), promoted to first, per finding 8.
2. **Pure models + tests:** chirp cadence, punctuation pause policy, semantic-role-to-tag table.
   No ports, no clock interface. Slot pipeline only if the owner rejects finding 3's recommendation.
3. **Godot dialogue presenter:** one `RichTextLabel`; reveal via `visible_ratio` with a post-shaping
   `visible_characters_behavior`; roles resolved to built-in BBCode tags, static variants chosen when
   `Win98MotionPolicy.Allows` is false; `UiFeedbackAudioBootstrap` called directly.
   **Includes `Win98ThemeFactory` styling for `RichTextLabel` and font parity with the existing
   `Label`** (finding 1's surviving caveat) — verified at 100–200% UI scale in this phase, not at the
   Phase 6 gate.
4. **Tutorial integration** as in the original Phase 4, plus: replace `_lastRenderedText` equality with
   semantic step+variant identity (`FirstSessionGuidanceController.cs:297`), and route the Work helper
   window through the same presenter so `_workGuideBody` stops being a second implementation.
5. **Preview facade extraction *with* migration of all three existing call sites, carrying the
   visibility-driven update-mode fix** (findings 4 and 5).
6. **Portrait**, as in the original Phase 3 items 2–6, retiring `OptionalArtPath` (finding 6). This part
   of the original plan needs no simplification: reusing `BlinkModel` and `FaceRenderState` and never
   touching live Buddy state is exactly right and worth its cost.
7. **Phase 6 release gate** as written, minus the UI-scale items already covered in step 3.

## Amendments to the acceptance criteria

Add:

- built-in `RichTextLabel` reveal and BBCode effects are used unless a specific requirement is
  demonstrably unmet, and any `custom_effects` addition names the requirement it satisfies;
- `RichTextLabel` matches the existing Win98 `Label` styling at 100–200% UI scale;
- no new interface ships with a single implementation unless a second consumer exists in the same change;
- the extracted preview facade has all four consumers migrated, or the duplication is left in place and
  commented — not a fourth silent copy;
- hidden-viewport suspension applies to Work, the character editor and Workshop capture, not only the
  tutorial portrait;
- the `tutorial_guide.png` optional-art path is explicitly retired along with its loader.
