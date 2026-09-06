# Expressive Presentation Implementation Status

Date: 2026-09-07
Branch: `expressive-text-tutorial-buddy-plan`
Plan: `docs/EXPRESSIVE_TEXT_AND_TUTORIAL_BUDDY_PLAN_2026-09-06.md`

## Status

The audited implementation plan is now substantially implemented on this branch. The remaining work is validation/polish rather than another architecture pass.

## Implemented

### 1. Semantic WordArt rewards

- `RewardPopup` remains the single queue/timing owner.
- Semantic `RewardPresentationKind` values cover tool purchases, Work session milestones, and Work lifetime milestones.
- Those kinds resolve inside the popup to original late-90s WordArt-inspired title treatments.
- Reduced/disabled UI motion keeps the styled title static.
- Reward queue ordering and semantic presentation-kind ordering are covered by the existing reward scenario.

### 2. Expressive-text domain layer

Engine-independent presentation code now provides:

- semantic roles: `Input`, `Action`, `Money`, `Impact`, `Playful`;
- stable semantic tag names;
- safe semantic markup parsing;
- punctuation-aware reveal timing;
- Unicode-aware speech-chirp cadence.

Malformed, unknown or nested/general markup remains literal and readable rather than disappearing or being executed as arbitrary BBCode. Domain unit tests cover these policies.

### 3. Tutorial text voice and presenter

`UiFeedbackAudioBootstrap` owns five closely related synthetic tutorial chirps through the existing pooled UI audio path and UI bus.

`ExpressiveTextPresenter` now provides:

- one `RichTextLabel` surface;
- post-shaping typewriter reveal;
- grapheme/text-element reveal timing;
- semantic emphasis mapped to a small set of built-in RichText treatments;
- punctuation pauses;
- pooled speech chirps;
- `SpeakingChanged` for portrait mouth animation;
- first-click `CompleteReveal()` behavior;
- reduced-motion/static fallback;
- semantic identity plus source idempotence.

### 4. Tutorial integration

Both tutorial dialogue surfaces now use `ExpressiveTextPresenter`:

- main first-session tutorial window;
- separate Work Mode tutorial helper window.

Help/reference labels remain immediate ordinary Labels.

Interaction rules:

- gameplay actions remain possible while a line is typing;
- clicking the tutorial dialogue/window while a line is revealing completes only that reveal;
- Continue/Goodbye remains disabled until the current reveal has completed, preventing one click from both revealing and advancing;
- Work Mode uses the same expressive presenter behavior as the main tutorial.

The Drop Tool tutorial line reads the live configured binding through `LocalSettingsInputBindings.DropTool(...)` instead of teaching a hard-coded `D`.

English expressive copy now authors semantic tags directly rather than searching/replacing visible phrases. Conditional prompts that were not explicitly reauthored fall back to the existing authoritative `TextFor` copy.

The expressive presentation identity is semantic:

- normal prompts: `surface + step + default`;
- Create Buddy: `can-create` vs `select-existing`;
- Exit Buddy Studio: `nothing-to-save` vs `saved-item`.

The legacy controller still compares conditional source text to notice that live state changed, but rendered expressive identity no longer depends on that text equality.

### 5. Shared physics-free Buddy preview surface

Added `BuddyPreviewSurface : SubViewport` as the one shared offscreen Buddy-preview facade. It owns:

- isolated `World3D`;
- `StaticBuddyVisualTransformSource`;
- `BuddyVisualRigView`;
- orthographic camera;
- preview light.

It does not create gameplay bodies, solvers, autonomy or reaction authority.

Render lifecycle:

- continuous consumers use `UpdateMode.Always` only while their owning container is visible;
- hidden continuous previews switch to `UpdateMode.Disabled`;
- capture-only previews start disabled and request `UpdateMode.Once` explicitly;
- presentation/camera refresh cannot strand a continuous preview in `Once`.

All three pre-existing duplicated preview stacks are migrated before the tutorial portrait was added:

- Work Mode;
- Character Editor;
- Workshop preview capture.

Character Editor keeps the existing `CharacterPreview` container and exact preview-rig instance so editor/session references do not change. Workshop capture is now one-shot rather than permanently rendered.

### 6. Live tutorial Buddy portrait

The previous optional `tutorial_guide.png` / `DemoTutorialCharacterPresenter` placeholder path has been retired.

The tutorial guide is now a live, physics-free 3D Buddy rendered through the shared preview surface. It:

- copies the current live Buddy appearance and painted underlays;
- uses a shoulders-up portrait camera;
- uses the existing `BlinkModel` with the game's blink tuning;
- uses `FaceComposer` / `FaceRenderState` and `BuddyVisualRigView.SetPreviewFaceState`;
- opens/closes its mouth from the expressive presenter's real `SpeakingChanged` signal;
- owns only three audited moods: `Neutral`, `Friendly`, `Pleased`;
- stops portrait animation work while hidden;
- shares no gameplay physics/reaction/autonomy state with the live Buddy.

### 7. Regression coverage

Added `expressive_presentation` scenario coverage for:

- rebound Drop Tool copy;
- first-click reveal completion;
- visible continuous preview = `Always`;
- hidden preview = `Disabled`;
- reappearing preview resumes;
- refresh does not strand continuous surfaces in `Once`;
- capture-only preview starts disabled and requests one frame;
- preview world contains no physics/reaction/autonomy authority.

Existing Work, Workshop, Character Editor and tutorial closure scenarios remain the broader integration regression paths.

## Verification

Known green checkpoints during implementation:

- presenter/domain/reward foundation: CI run `1453` / workflow `34064555514` — PASS;
- Workshop/shared preview migration: run `1466` — PASS;
- Work preview migration: run `1467` — PASS;
- Character Editor preview migration: run `1468` — PASS;
- combined portrait + preview lifecycle + regression scenario head before semantic-identity cleanup: run `1477` / workflow `34065957561` — PASS for solution build, domain tests, Steam binary guard and SFX sidecar guard.

Run `1478` validates the final semantic-identity cleanup. A draft PR/full Godot scenario sweep should be used as the release gate before merge.

## Remaining validation/polish

1. Let final push CI finish green.
2. Run the repository's pull-request `build-test` suite, especially Workshop preview, Character paint/restart, Work window lifecycle and `tutorial_closure`.
3. Windowed visual review of:
   - typewriter speed and emphasis density;
   - chirp volume/pitch;
   - WordArt readability;
   - tutorial Buddy portrait framing, blinking and mouth cadence;
   - long tutorial lines at supported UI scales.
4. Only tune presentation constants/copy after that review; do not introduce another text, audio or preview architecture.
