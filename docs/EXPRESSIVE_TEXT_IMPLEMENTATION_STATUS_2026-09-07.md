# Expressive Presentation Implementation Status

Date: 2026-09-07
Branch: `expressive-text-tutorial-buddy-plan`
Plan: `docs/EXPRESSIVE_TEXT_AND_TUTORIAL_BUDDY_PLAN_2026-09-06.md`

## Status

Implementation has started. The audited architecture remains authoritative; this file records what is actually present on the branch so later slices do not re-plan completed work.

## Completed kickoff slices

### 1. Semantic WordArt reward presentation

- `RewardPopup` remains the only queue/timing owner.
- Reward callers may provide a semantic `RewardPresentationKind`:
  - `ToolPurchase`
  - `WorkSessionMilestone`
  - `WorkLifetimeMilestone`
- Tool purchases and both Work milestone scopes now use that semantic path.
- The popup resolves the semantic kind to an original late-90s WordArt-inspired title preset.
- WordArt uses layered `RichTextLabel` text for outline/extrusion plus a short built-in wave/settle treatment.
- Reduced/disabled UI motion leaves the styled title static.
- Existing whole-popup breathing is suppressed while WordArt supplies the motion so the effects do not stack.
- The existing reward scenario now asserts queue ordering and semantic presentation-kind ordering.

### 2. Pure expressive-text vocabulary and timing

Added engine-independent presentation code for:

- semantic roles: `Input`, `Action`, `Money`, `Impact`, `Playful`;
- stable semantic tag names;
- punctuation-aware reveal timing;
- speech-chirp cadence that ignores whitespace/punctuation;
- Unicode `Rune` classification rather than C# `char` assumptions.

Unit coverage is present for the vocabulary, timing validation, punctuation classes and chirp cadence.

### 3. Safe semantic markup parser

English authored copy may use a deliberately tiny semantic language such as:

`Hold [input]right mouse button[/input] for a [impact]big swing[/impact].`

The pure parser produces plain player-readable text plus semantic runs. It does not understand arbitrary BBCode and does not encode the eventual visual treatment into the copy.

Safety rules already covered by tests:

- unknown tags stay literal;
- stray closing tags stay literal;
- missing closing tags stay literal;
- nested/general markup stays literal;
- malformed authoring cannot make tutorial instructions disappear or crash the parser.

This is the future-localization seam: a later translated line can put the same semantic markers around different/reordered words without changing the Godot renderer. No localization implementation is part of this feature now.

### 4. Tutorial text voice

`UiFeedbackAudioBootstrap` now has a partial-file extension for tutorial text voice:

- five closely related synthetic computer chirps;
- random-no-repeat selection with tiny pitch/volume variation;
- reuses the existing pooled UI voices and UI audio bus;
- therefore obeys Interface Sounds volume and existing audio lifecycle automatically;
- no new audio singleton/global manager.

### 5. Reusable `ExpressiveTextPresenter`

Added a single-`RichTextLabel` reusable presenter that:

- parses the semantic markup above;
- maps semantic roles to a small set of built-in BBCode treatments;
- uses `VisibleCharactersBehavior.CharsAfterShaping` so the full line is shaped/wrapped before reveal;
- reveals Unicode text elements/graphemes as units while translating them to Godot's codepoint-based `VisibleCharacters` count;
- applies punctuation timing from the pure model;
- requests chirps from the pooled UI audio path on the pure cadence;
- exposes `SpeakingChanged` for the later Buddy mouth animation;
- exposes `CompleteReveal()` for first-click skip-to-complete behavior;
- uses semantic line identity so presenting the same line again is idempotent;
- disables the reveal/wave motion through the existing `Win98MotionPolicy` while preserving readable static emphasis;
- explicitly matches the Win98 font size/text color rather than assuming `RichTextLabel` inherits the `Label` theme entries.

## Verification

CI run `1453` / workflow run `34064555514` is green on the presenter head commit:

- solution build: PASS
- domain unit tests: PASS
- Steam SDK binary guard: PASS
- authored SFX import-sidecar guard: PASS

The immediately preceding run was also green through the reward/cadence/parser slices.

## Next implementation slice

1. Integrate `ExpressiveTextPresenter` into `FirstSessionGuidanceController` for both the main tutorial window and Work helper window.
2. Fix the Drop Tool tutorial line to use the live configured binding while touching that controller.
3. Add semantic emphasis to a deliberately small first set of tutorial lines and verify first-click reveal completion cannot advance tutorial progression.
4. Then extract/migrate the shared offscreen Buddy preview surface before adding the live tutorial portrait, per the audited plan.

The live tutorial Buddy portrait, shared preview migration, blinking/talking/moods, and tutorial-wide authored emphasis have **not** been implemented yet.
