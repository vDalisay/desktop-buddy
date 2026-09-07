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

## Post-implementation audit fixes — 2026-09-07

A code-verified audit of the implemented branch found five issues; all are fixed here. Domain tests
1540/1540, and `expressive_presentation`, `tutorial_closure`, `reward_popup_demo` and
`shop_panel_purchase` all pass locally.

1. **Tutorial copy was duplicated and had already drifted.** `TutorialExpressiveCopy` and
   `FirstSessionGuidanceController.TextFor` each held their own copy of ~28 lines, and
   `UsePaintedBuddy` had diverged ("Click on **the** 'Use Character'" versus "Click on 'Use
   Character'"). The authored semantic copy is now the single source: `TextFor` returns the
   tag-stripped projection via `TutorialExpressiveCopy.PlainTextFor`, and only the three genuinely
   conditional lines (`CreateBuddy`, `SelectPaintColor`, `ExitBuddyStudio`) remain in the
   controller's own switch. This deletes ~135 lines and makes the drift structurally impossible.
   The one behavioural change is that `UsePaintedBuddy` now reads grammatically.

2. **Per-frame prose rebuilding.** `SyncExpressiveTutorialPresentation` formatted the full semantic
   line, the identity strings and the Drop Tool binding on every frame of the tutorial purely to
   discover that `Present` would no-op — against the plan's "no per-frame allocation proportional to
   full text length". A cheap `cueKey` comparison now gates the rebuild.

3. **The legacy `_lastRenderedText` guard was neutralized rather than removed.** It was kept alive
   and defeated each frame by writing the expected value into it ahead of the controller, which cost
   a second full `TextFor` build per frame for a branch that could never fire. The branch and the
   field are deleted; semantic step+variant identity is now the only refresh authority.

4. **WordArt had no fit rules.** `ApplyWordArtPreset` used the preset font size unconditionally, so
   generated milestone titles overflowed the plate (`"1,000,000 keystrokes all time"` measures 265px
   against a 260px plate at size 19) and, with `ClipContents = false`, spilled onto the money line.
   `FitWordArtFontSize` now measures and steps down to a legibility floor of 12, with the plate
   reserving height for a wrapped second line. Because fonts scale through `Px()` while dialog
   widths do not — the convention every `Win98Dialog` caller follows — measuring the scaled string
   against the unscaled plate also keeps the fit correct at 125–200% UI scale.

5. **Motion policy was latched at Present time.** Toggling Reduced Motion mid-line left `[wave]`
   running until the step changed, because `Present` early-returns on matching identity and source.
   Motion is now part of the cue key, so the line re-presents statically at once.

Also added: `expressive_authored_copy_projects_to_clean_prose`, which walks every authored line and
fails if the plain projection still contains brackets — the check that would have caught issue 1.

Not changed, and deliberately: `PanelSize` and the other `Win98Dialog` sizes stay unscaled while
fonts scale. That mismatch is pre-existing and project-wide; fixing it belongs in its own pass, and
the WordArt fit above compensates for it locally.

Still open from "Remaining validation/polish": the windowed visual review, and one cosmetic note —
`ApplyWin98TextStyle` sets `line_separation` to `Px(1)` while `Label` defaults to 3, so tutorial
text renders about 9px tighter over four lines than the rest of the shell. Left as an owner call.

## Windowed visual review fixes — 2026-09-07

The "Remaining validation/polish" windowed review was run by the owner. Five defects it found, all
fixed. Domain 1540/1540; `expressive_presentation`, `tutorial_closure`,
`character_paint_save_use_restart`, `reward_popup_demo`, `shop_panel_purchase`,
`work_mode_resilience` and `work_play_window_behavior` pass.

1. **Portrait framed the legs instead of the head.** `BuildPortrait` computed the right focus point
   and then passed the 2D (Y-down) Y straight into the preview camera, which is Y-up — mirroring the
   focus through the torso. Head rest is `(0,-50)`, torso `(0,0)`, feet `(±22,55)`, so the focus
   `(0,-34)` put the camera at 3D Y **-34** framing -93..+25: head above the frame, feet filling it.
   Routed through `WorldPlaneMapping.To3D` as every other boundary crossing in that file already is.

2. **Portrait was too wide.** Camera size is now `headRadius * 2.9` with the focus biased 10% toward
   the torso, derived from the rig's own head radius rather than a literal, so a rig change carries
   the crop with it. `PortraitHeadFillFactor` is the single dial.

3. **Cursor tracking was bespoke and did not match the live Buddy.** The portrait now drives the same
   `LookAtModel` gameplay uses for the cursor gaze — same cone, easing and quantized pupils — and
   applies it as `Vector3(pitch, yaw, roll)` on the head socket exactly like `BuddyVisualPresenter`.
   The lateral head lean, the cursor-driven roll and the input pre-smoothing are gone; the model owns
   the easing. Two deliberate differences: the guide is always engaged (gameplay engages only within
   `EngagementRange` of the head) and the sweep normalizes against the display, so the cone limit is
   reached at the screen edge. The pure model is reused, never `HeadLookAtComponent`, which samples
   damage, care, activities and reactions and would give the portrait gameplay authority.

4. **Paint → Save could not be completed.** The gate required the **torso** revision to advance, so a
   player who painted the head, a hand or a foot satisfied "Paint away!" but never the step, and with
   the save already applied nothing could re-trigger it. Now any surface counts
   (`HasPaintedAnySurface`), and a revision total *below* the recorded baseline rebaselines rather
   than deadlocking — switching characters mid-step swaps in fresh surfaces whose revisions restart.
   `character_paint_save_use_restart` never caught this because it paints the torso.

5. **The farewell portrait pulled an open-mouthed "oh" face.** `Farewell` mapped to the `Pleased`
   mood, whose mouth is `OpenSmile`. It now uses `Friendly`, the closed smile the guide wears
   throughout. Note the literal `:3` pose is `(HappyArc, None, CatSmile)` and has **no pupils**, so it
   would end the tutorial with the gaze tracking dead — the same reason the code already avoids `^_^`.

Two further owner requests in the same pass:

- **The Work drag lesson completed mid-drag.** It advanced on the first moved pixel; it now waits for
  the release, via a new `WorkCompanionView.IsDragging`, matching the let-go rule the grab and paint
  steps already follow. Polled rather than bound to `DragFinished`, because the companion is destroyed
  and rebuilt on every Work entry and a polled check cannot go stale across that.
- **The tutorial click lock blocked window resizing.** The Win98 corner grips answer through
  `GuiInput`, which runs after this node's `_Input`, so `SetInputAsHandled` ate the press. Resize input
  now bypasses the lock, above the Help-mode branch as well. `Win98BuddyShellController.IsResizingWindow`
  exists because a rect test alone would swallow the *release* once the pointer left the grip.

Known and deliberately not changed, pending an owner call:

- the title-bar window *move* is still blocked while a step spotlights something;
- `ResizeWorkCompanion` still completes on the first pixel of size change rather than on release;
- `[WARN] [Onboarding] Tutorial step 'demo.onboarding.paint_buddy' found no 'Win98PaintViewportFrame'`
  appears at runtime, so that step draws no spotlight ring. The node exists
  (`CharacterEditorHost.Win98PaintLayout.cs:238`), so this is a lookup/timing issue, not a rename.
