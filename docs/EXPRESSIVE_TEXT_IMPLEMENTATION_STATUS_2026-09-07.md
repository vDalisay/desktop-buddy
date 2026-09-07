# Expressive Presentation Implementation Status

Date: 2026-09-07
Branch: `expressive-text-tutorial-buddy-plan`
Plan: `docs/EXPRESSIVE_TEXT_AND_TUTORIAL_BUDDY_PLAN_2026-09-06.md`

## Status

The audited implementation plan is implemented on this branch. The repository-level automated release gate was green at commit `e85c122c7e7f3784ed661dcc43c8e64ea71f8a49`; the only remaining gate after the mouse-tracking polish is to re-run those same checks on the final head and do a windowed presentation review.

## Implemented

### 1. Semantic WordArt rewards

- `RewardPopup` remains the single queue/timing owner.
- Semantic `RewardPresentationKind` values cover tool purchases, Work session milestones, and Work lifetime milestones.
- Those kinds resolve inside the popup to original late-90s WordArt-inspired title treatments.
- Reduced/disabled UI motion keeps the styled title static.
- Reward queue ordering and semantic presentation-kind ordering are covered by the existing reward scenario.

### 2. Expressive-text domain layer

Engine-independent presentation code provides:

- semantic roles: `Input`, `Action`, `Money`, `Impact`, `Playful`;
- stable semantic tag names;
- safe semantic markup parsing;
- punctuation-aware reveal timing;
- Unicode-aware speech-chirp cadence.

Malformed, unknown or nested/general markup remains literal and readable rather than disappearing or being executed as arbitrary BBCode. Domain unit tests cover these policies.

### 3. Tutorial text voice and presenter

`UiFeedbackAudioBootstrap` owns five closely related synthetic tutorial chirps through the existing pooled UI audio path and UI bus.

`ExpressiveTextPresenter` provides:

- one `RichTextLabel` surface;
- Godot post-shaping glyph reveal;
- punctuation-aware timing without reflow;
- semantic emphasis mapped to a small set of built-in RichText treatments;
- pooled speech chirps;
- `SpeakingChanged` for portrait mouth animation;
- first-click `CompleteReveal()` behavior;
- reduced-motion/static fallback;
- semantic identity plus source idempotence.

### 4. Tutorial integration

Both tutorial dialogue surfaces use the same guide architecture:

- main first-session tutorial window;
- separate Work Mode tutorial helper window.

Help/reference labels remain immediate ordinary Labels.

Interaction rules:

- gameplay actions remain possible while a line is typing;
- clicking the tutorial dialogue/window while a line is revealing completes only that reveal;
- Continue/Goodbye remains disabled until the current reveal has completed, preventing one click from both revealing and advancing;
- Work Mode uses the same expressive presenter and live guide behavior as the main tutorial.

The Drop Tool tutorial line reads the live configured binding through `LocalSettingsInputBindings.DropTool(...)` instead of teaching a hard-coded `D`.

English expressive copy authors semantic tags directly rather than searching/replacing visible phrases. Conditional prompts that were not explicitly reauthored fall back to the existing authoritative `TextFor` copy.

The expressive presentation identity is semantic:

- normal prompts: `surface + step + default`;
- Create Buddy: `can-create` vs `select-existing`;
- Exit Buddy Studio: `nothing-to-save` vs `saved-item`;
- Drop Tool: current configured chord is part of the semantic variant.

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

All three pre-existing duplicated preview stacks were migrated before the tutorial portrait was added:

- Work Mode;
- Character Editor;
- Workshop preview capture.

Character Editor keeps the existing `CharacterPreview` container and exact preview-rig instance so editor/session references do not change. Workshop capture is one-shot rather than permanently rendered.

### 6. Live tutorial Buddy portrait

The previous optional `tutorial_guide.png` / `DemoTutorialCharacterPresenter` placeholder path is retired.

The tutorial guide is a live, physics-free 3D Buddy rendered through the shared preview surface. It:

- uses one stable authored Buddy appearance for the whole tutorial rather than mirroring the player's active Buddy;
- uses a shoulders-up portrait camera;
- uses the existing `BlinkModel` with the game's blink tuning;
- uses `FaceComposer` / `FaceRenderState` and `BuddyVisualRigView.SetPreviewFaceState`;
- opens/closes its mouth from the expressive presenter's real `SpeakingChanged` signal;
- owns only three audited moods: `Neutral`, `Friendly`, `Pleased`;
- uses open-eyed pleased presentation so pointer tracking remains available in every mood except the instant of a natural blink;
- follows the desktop mouse with its pupils in both the main tutorial and Work helper;
- smoothly leans/tilts its head a small amount toward the pointer while motion is enabled;
- keeps pointer eye tracking under Reduced Motion but removes idle bob and pointer-driven head motion;
- stops portrait animation work while hidden;
- shares no gameplay physics/reaction/autonomy state with the live Buddy.

### 7. Regression coverage

The `expressive_presentation` scenario covers:

- normalized/clamped tutorial pointer-look mapping;
- rebound Drop Tool copy;
- first-click reveal completion;
- reduced-motion immediate/static dialogue behavior;
- visible continuous preview = `Always`;
- hidden preview = `Disabled`;
- reappearing preview resumes;
- refresh does not strand continuous surfaces in `Once`;
- capture-only preview starts disabled and requests one frame;
- preview world contains no physics/reaction/autonomy authority.

The PR `build-test` workflow runs `expressive_presentation` directly after `tutorial_closure`. Existing Work, Workshop, Character Editor and tutorial closure scenarios remain the broader integration regression paths.

## Verification

Important green checkpoints:

- presenter/domain/reward foundation: CI run `1453` / workflow `34064555514` — PASS;
- Workshop/shared preview migration: run `1466` — PASS;
- Work preview migration: run `1467` — PASS;
- Character Editor preview migration: run `1468` — PASS;
- combined portrait + preview lifecycle checkpoint: run `1477` / workflow `34065957561` — PASS;
- audited pre-mouse final head `e85c122c7e7f3784ed661dcc43c8e64ea71f8a49`: PR CI run `1555` / workflow `34111741891` — PASS for all 51 `build-test` steps, including `tutorial_closure` and `expressive_presentation`; GodotSteam Native Smoke and Asset Forge CI also passed.

The final mouse-tracking head must pass those same automated checks before merge.

## Remaining validation/polish

1. Final-head CI / PR build-test green.
2. Windowed visual review of:
   - typewriter speed and emphasis density;
   - chirp volume/pitch;
   - WordArt readability;
   - tutorial Buddy portrait framing, blinking, mouth cadence, mouse eye tracking and head tilt;
   - main tutorial and Work helper behavior;
   - long tutorial lines at supported UI scales.
3. Only tune presentation constants/copy after that review; do not introduce another text, audio or preview architecture.
