# Desktop Buddy — Work Mode Typing Companion Plan

Status: **Owner accepted 2026-08-08 — WM0–WM5 implementation complete; release verification remains**
Planning baseline: `main`  
Depends on:

- accepted Win98 shell / mode ownership work;
- current character appearance compiler and active-character selection;
- `docs/BUDDY_STUDIO_CUSTOMIZATION_PLAN.md` for account-wide cosmetic ownership and the glasses reward;
- existing passive-income / reward-ledger / atomic progress-save infrastructure;
- Windows desktop placement, monitor clamp, transparency, always-on-top, and recovery behavior already established by the desktop shell.

Recommended schedule: **after Buddy Studio's ownership slice is available**, because first-time Work Mode grants a glasses cosmetic. The Work Mode architecture itself must not depend on Steam.

This document supersedes the older deferred-roadmap description of Work Mode as merely a buddy with glasses at a mini PC. The revised product target is a minimal transparent desktop companion inspired by the broad interaction idea of apps such as Bongo Cat while using completely original Desktop Buddy art, behavior, UI, code, sounds, and presentation.

---

## 1. Product goal

Work Mode turns Desktop Buddy from the normal Win98 application window into a very small, low-distraction transparent desktop companion.

The active custom buddy sits at a project-owned retro PC/keyboard/mouse setup. While the player works in other applications:

- physical keyboard presses animate the buddy typing;
- left, right, and middle mouse clicks animate a short mouse-hand reaction;
- the CRT displays an anonymous action counter;
- clicking the CRT toggles between the current Work session total and lifetime Work total;
- milestone rewards are accumulated from current-session and lifetime activity;
- double-clicking the buddy exits Work Mode, restores normal windowed Desktop Buddy, and settles any pending Work milestone rewards;
- the entire companion can be dragged if it is covering useful desktop content;
- an optional low-distraction toggle freezes the buddy's reactive animation while counters continue updating.

There is no application frame, background panel, title bar, task strip, room background, or opaque rectangle around the Work companion. Only the buddy, desk/keyboard/mouse/PC art, CRT contents, and minimal hover controls are visible. Everything else remains transparent.

The default position is the lower-right usable desktop area immediately above the Windows taskbar, comparable to a compact desktop mascot rather than a normal application window.

---

## 2. Locked owner decisions — 2026-08-07

### 2.1 What counts as a Work action

Only these global physical input transitions count:

- keyboard **key-down transitions**;
- left mouse-button down;
- right mouse-button down;
- middle mouse-button down.

Explicit exclusions:

- mouse movement;
- mouse-wheel scrolling;
- held-key OS repeat events;
- pointer hover;
- touchpad movement without an eligible click;
- game/render frames;
- elapsed time by itself.

Each accepted keyboard transition or accepted mouse click increments the combined action total by exactly one.

The implementation tracks at minimum:

```text
session.keyboardPresses
session.mouseClicks
session.totalActions
lifetime.keyboardPresses
lifetime.mouseClicks
lifetime.totalActions
```

with:

```text
totalActions = keyboardPresses + mouseClicks
```

### 2.2 Privacy contract

Work Mode is an **activity counter, not a key logger**.

This is a hard product and architecture invariant:

- never persist which key was pressed;
- never persist scan codes, virtual-key values, characters, text, application names, window titles, processes, clipboard data, pointer coordinates, or typed strings;
- never derive words or characters from keyboard events;
- never emit raw keys into normal logs, diagnostics, telemetry, crash breadcrumbs, or analytics;
- discard the key identity immediately after deciding whether the physical down-transition is countable;
- global-input capture exists only while Work Mode is active and is unregistered immediately on exit/shutdown;
- callbacks never suppress, rewrite, delay, or inject user input.

The only durable input-derived values are aggregate integer counters and claimed milestone identifiers.

### 2.3 CRT counter modes

The retro CRT displays one large green counter, keeping the visual as minimal as the supplied mockup.

Clicking directly on the CRT screen toggles:

1. **Session** — `session.totalActions`
2. **Lifetime** — `lifetime.totalActions`

The mode wraps Session -> Lifetime -> Session.

Use a tiny unobtrusive indicator such as `S` / `L`, or a brief `SESSION` / `LIFETIME` overlay after switching, so the player can tell what the number represents without adding permanent UI chrome.

The display-mode preference is a machine/UI preference and should persist between Work Mode entries. It does not affect rewards.

### 2.4 Reward model

There is no per-action linear payout and no soft cap.

Work rewards are **data-driven milestones** evaluated against aggregate counters.

Required milestone scopes:

- current-session total actions;
- current-session keyboard presses;
- current-session mouse clicks when authored;
- lifetime total actions;
- lifetime keyboard presses;
- lifetime mouse clicks when authored.

Owner examples that must be representable from the first implementation:

- `10,000` total actions in one Work session;
- `10,000` keyboard presses in one Work session;
- `1,000,000` lifetime Work actions.

Additional thresholds may be authored without changing counting code.

Reward credit amounts are **not locked by this plan**. They must be authored in milestone data and calibrated against the existing economy before release.

Normal existing passive-income behavior may continue while Work Mode is active. Work-specific bonus income comes from milestones rather than an uncapped `credits-per-keystroke` formula.

### 2.5 Reward settlement

When a threshold is crossed:

1. the milestone evaluator marks the milestone pending/earned exactly once;
2. the CRT or companion may give a subtle nonblocking visual acknowledgement;
3. the milestone cannot be earned repeatedly by oscillating modes or restarting;
4. pending milestone rewards are settled through the authoritative reward ledger when Work Mode ends normally;
5. double-clicking the buddy is the primary normal exit and payout gesture.

Lifetime milestone claim identity must be persisted so the same threshold can never pay twice.

Session milestone identity applies to one Work session only; the next new Work session may earn a repeatable session milestone again if the milestone definition explicitly allows `RepeatPerSession`.

The implementation must define milestone repeat policy explicitly:

```text
OnceLifetime
RepeatPerSession
```

Do not infer repeat behavior from milestone names or thresholds.

### 2.6 Position and dragging

Default placement:

- current monitor's usable work area;
- lower-right corner;
- immediately above the Windows taskbar / work-area bottom edge;
- safe inset from right and bottom edges;
- restored user position takes precedence after the player has manually moved Work Mode.

The Work companion is movable as one unit.

Dragging rules:

- no visible window frame is shown;
- press-and-drag on eligible visible companion areas moves the entire Work window;
- CRT screen clicks remain reserved for counter switching;
- the animation toggle remains reserved for its action;
- double-click on the buddy remains reserved for exiting;
- movement begins only after a small drag-distance threshold, preventing a normal click from shifting the companion;
- moving never changes the buddy/PC relative layout;
- final position persists as a machine preference;
- position clamps to a recoverable region on the current monitor;
- monitor removal, resolution change, taskbar movement, or DPI change triggers the existing desktop-shell recovery/clamp policy.

There is no automatic edge snapping at launch. A future context action may offer **Reset Position**.

### 2.7 Character appearance and Work pose

Work Mode always uses the currently active custom buddy appearance:

- saved paint;
- face/feature selections;
- colors;
- accessories;
- glasses;
- headwear;
- tops;
- shoes;
- all other Buddy Studio appearance state that is visually compatible with the seated Work presentation.

Work Mode never forces or changes the equipped glasses selection.

The Work presentation uses a dedicated seated visual pose rather than running the gameplay ragdoll simulation inside the tiny desktop companion.

### 2.8 First-entry glasses reward

The **first ever successful entry into Work Mode** grants one specific project-owned glasses cosmetic for free.

Rules:

- grant is account/save-wide;
- grant occurs only once;
- it uses the Buddy Studio cosmetic ownership service, not a separate Work-only inventory;
- it is not automatically equipped and does not edit the active character document;
- on every Work entry, including the first, Work Mode respects whatever glasses selection the player chose in Buddy Studio;
- changing/removing the reward glasses later never removes ownership;
- Work Mode never silently equips or re-equips them;
- if ownership was already present before first Work entry, do not create a duplicate ownership record; complete the first-entry flag without changing equipment.

The glasses definition needs a stable cosmetic content ID before implementation. The exact visual design/name is content authoring, not architecture.

### 2.9 Animation behavior

Default reactive presentation:

- accepted keyboard action -> short alternating left/right typing-hand movement;
- accepted mouse click -> brief right-hand mouse movement/click reaction;
- rapid input queues/coalesces animation intent rather than restarting an expensive animation for every event;
- no input for a short interval -> calm seated idle;
- CRT counter updates independently from animation playback.

The current buddy's semantic face system may show a restrained focused/neutral expression, but Work Mode must not add new mood/economy semantics merely for typing.

### 2.10 Low-distraction animation toggle

The player can disable reactive typing/click animation while leaving Work Mode active.

When disabled:

- buddy remains in the seated static Work pose;
- counters continue to increment normally;
- milestone evaluation continues normally;
- CRT updates normally;
- passive income continues according to existing rules;
- global activity capture remains active;
- the toggle does not end or restart the Work session.

UX direction:

- expose a tiny Win98-style pause/animation toggle adjacent to the companion composition;
- keep it visually unobtrusive;
- recommended behavior is to show the tiny control only while the pointer is over the companion region, while preserving keyboard/accessibility reachability where practical;
- persist the animation-enabled preference between Work sessions.

---

## 3. Mode lifecycle

### 3.1 Entering Work Mode

Entry begins from the existing top-level `Work` command while Desktop Buddy is in normal app mode.

Ordered transition:

1. reject or resolve any modal/editor state that cannot safely transition;
2. snapshot current normal-window geometry and presentation mode;
3. resolve and compile the active character appearance;
4. run the first-entry glasses ownership transaction if required;
5. suspend normal gameplay simulation/activity presentation;
6. create or activate the Work companion visual composition;
7. resize/reposition the existing app window to the compact Work bounds;
8. hide all Win98 app chrome, menus, status bars, room background, and gameplay viewport elements not used by Work Mode;
9. enable per-pixel transparency for all pixels outside the companion art;
10. restore the saved Work position or choose the default lower-right work-area position;
11. initialize a fresh current-session counter state;
12. install the Windows global activity source;
13. mark Work Mode active only after the activity source and visual composition are ready.

Do not create a second normal gameplay process or a second buddy simulation.

### 3.2 During Work Mode

The normal gameplay clock is not simulated at full fidelity merely because Work Mode is visible.

Work Mode should use a low-cost foreground companion loop:

- aggregate global input events;
- update counters;
- evaluate milestone thresholds;
- update CRT only when its visible value changes;
- animate the small Work presentation only when enabled;
- continue existing economy clocks through their normal elapsed-time service where appropriate;
- avoid 120 Hz ragdoll physics when no gameplay physics is required.

### 3.3 Exiting by double-click

Double-clicking a defined buddy hit region performs the normal Work exit.

Ordered transition:

1. prevent new Work actions from entering the session accumulator;
2. unregister global input capture;
3. evaluate final counters one last time;
4. settle all pending Work milestone rewards idempotently through the reward ledger;
5. persist lifetime counters, claimed milestones, session summary as required, and relevant preferences;
6. destroy/hide Work-only visuals;
7. restore normal shell geometry, monitor clamp, transparency policy, and Win98 chrome;
8. restore the existing normal windowed Play state;
9. resume the gameplay presentation/simulation at a safe boundary;
10. show a concise in-app reward/session summary only if one or more rewards were earned, avoiding a modal when nothing happened.

The exit double-click itself may be counted as mouse actions if it reaches the global activity source before step 1; exact ordering must be deterministic and covered by tests.

### 3.4 Other exits

Also handle:

- application quit while in Work Mode;
- tray hide/recovery;
- Windows shutdown/logoff;
- crash/restart recovery;
- input-source failure;
- monitor topology change.

A non-normal exit must not duplicate rewards. Lifetime counters and claimed milestone state need periodic durable checkpoints or an idempotent session journal so a crash cannot turn a threshold into repeatable money.

Recommended checkpoint policy:

- aggregate counters in memory on every event;
- flush durable Work progress on milestone earn;
- additionally checkpoint at a low-frequency interval such as 30–60 seconds;
- always flush on orderly Work exit;
- never perform disk I/O from the global-input callback.

The exact checkpoint interval is implementation tuning; reward correctness is the invariant.

---

## 4. Global activity-capture architecture

### 4.1 Interface boundary

Create an engine-independent event abstraction rather than placing Windows hooks inside a UI component.

Recommended contract:

```csharp
public interface IWorkActivitySource : IDisposable
{
    event Action<WorkActivityKind> Activity;
    bool IsRunning { get; }
    WorkActivitySourceResult Start();
    void Stop();
}

public enum WorkActivityKind
{
    KeyboardPress,
    MouseClick,
}
```

No key code exists in the domain-facing event.

### 4.2 Windows implementation

A Windows-specific adapter may use low-level keyboard/mouse hooks or another proven unfocused-input mechanism, provided it satisfies all privacy and behavior invariants.

If low-level hooks are used:

- install only while Work Mode is active;
- callback work must be minimal;
- maintain only enough transient key state to suppress held-key repeat;
- convert accepted input immediately to `KeyboardPress` / `MouseClick`;
- enqueue/increment using thread-safe primitives;
- always call the next hook;
- never block the Windows input thread on Godot, rendering, persistence, economy, logging, or milestone evaluation;
- teardown must be exception-safe and idempotent.

The Godot/main thread consumes aggregated deltas later.

### 4.3 Held-key detection

To enforce one count per physical down transition:

- maintain a transient pressed-key set in the Windows adapter;
- first key-down while not pressed -> one `KeyboardPress`;
- repeated key-down while still pressed -> zero;
- key-up -> remove from pressed set;
- clear transient state when capture starts/stops or focus/session boundaries require recovery.

The set exists only in memory and is never serialized or logged.

### 4.4 Failure behavior

If global capture cannot start:

- Work Mode must not pretend it is counting;
- either abort entry and remain in normal windowed mode, or enter an explicitly degraded visual-only mode only if the UI clearly says activity counting is unavailable;
- no fake increments;
- no reward milestones while the source is unavailable;
- log only a generic adapter/error category, not input details.

---

## 5. Work counter domain model

Keep counting and milestone evaluation Godot-free.

Recommended values:

```text
WorkCounterSnapshot
    KeyboardPresses : Int64
    MouseClicks : Int64
    TotalActions : derived Int64

WorkSessionState
    SessionId
    StartedAt
    Counters
    EarnedRepeatPerSessionMilestoneIds

WorkLifetimeState
    Counters
    ClaimedOnceLifetimeMilestoneIds
    FirstEntryGlassesGranted
```

Use checked/saturating arithmetic at the chosen persistence boundary. `Int64` is recommended so lifetime counts cannot realistically overflow.

Do not store a list of individual action timestamps.

---

## 6. Milestone catalogue

Milestones are trusted project-owned definitions.

Recommended engine-free definition:

```text
WorkMilestoneDefinition
    Id
    CounterKind
    Scope
    Threshold
    RewardMilliCredits
    RepeatPolicy
    Visible
```

Enums:

```text
CounterKind
    TotalActions
    KeyboardPresses
    MouseClicks

Scope
    CurrentSession
    Lifetime

RepeatPolicy
    OnceLifetime
    RepeatPerSession
```

Validation rules:

- stable nonempty ID;
- threshold > 0;
- reward >= 0 and valid under existing ledger denomination rules;
- `RepeatPerSession` requires `CurrentSession`;
- lifetime milestones are `OnceLifetime` in launch content;
- duplicate `(scope, counterKind, threshold)` entries require distinct intentional IDs and must not accidentally stack unless explicitly approved;
- definitions are sorted for efficient threshold crossing without scanning unrelated entries per action.

### 6.1 Initial milestone content direction

The first content pass must include at least:

- session total actions: `10,000`;
- session keyboard presses: `10,000`;
- lifetime total actions: `1,000,000`.

Recommended additional progression tiers for economy review, not locked until calibrated:

- session total: 100 / 1,000 / 10,000;
- session keyboard: 100 / 1,000 / 10,000;
- session clicks: 100 / 1,000 / 10,000;
- lifetime total: 10,000 / 100,000 / 1,000,000;
- lifetime keyboard: 10,000 / 100,000 / 1,000,000;
- lifetime clicks: 10,000 / 100,000 / 1,000,000.

Reward values should be calibrated after measuring realistic Work activity rather than guessed into the architecture.

---

## 7. Reward ledger integration

Work bonuses must use the existing authoritative economy/reward boundary.

Requirements:

- a milestone claim produces one stable reward transaction identity;
- applying the same claim twice is harmless/idempotent;
- claimed lifetime IDs persist before/with reward durability so crash ordering cannot duplicate money;
- repeat-per-session claims include the Work `SessionId` in their transaction identity;
- no Work code writes the displayed balance directly;
- reward settlement emits the normal progress/save signal used by other economy changes.

Recommended transaction key shape:

```text
work:<milestoneId>:lifetime
work:<sessionId>:<milestoneId>
```

The exact ledger API should follow the current repository's established transaction conventions rather than introducing a parallel balance service.

---

## 8. First-entry glasses transaction

This crosses Work Mode, cosmetic ownership, and persistence, so it must be explicit.

Recommended order:

1. check durable `FirstEntryGlassesGranted` flag;
2. if already true, do nothing;
3. ensure the glasses cosmetic ownership ID exists in the player's account/save ownership set;
4. set and persist `FirstEntryGlassesGranted = true` with cosmetic ownership;
5. continue Work Mode entry without changing the active character.

Failure requirements:

- never grant duplicate ownership;
- never read, write, or otherwise change the active character document;
- if progress save fails, keep the state dirty so the same ownership/flag transaction can be retried safely;
- no rollback may subtract a legitimately granted account-wide cosmetic.

---

## 9. Work presentation architecture

### 9.1 Dedicated visual composition

Create a presentation-only `WorkCompanionView` rather than shrinking the live ragdoll scene.

Composition:

```text
WorkCompanionView
├─ seated active buddy visual
├─ retro desk
├─ keyboard
├─ mouse
├─ retro CRT + PC case
├─ CRT counter renderer
└─ minimal hover controls
```

The buddy renderer should reuse the trusted appearance compilation/rendering seam so the Work buddy stays visually consistent with the selected character.

No Work presentation node may mutate character identity or physics data.

### 9.2 Work pose

The seated pose is deterministic and authored for readability at small desktop scale.

- torso/head remain readable;
- hands rest near keyboard positions;
- feet/body are arranged around/behind the desk according to final original art;
- paint and cosmetics remain visible where the pose exposes their parts;
- headwear/glasses should not clip through the CRT or desk under supported definitions;
- Work-specific pose offsets belong to trusted presentation metadata, never user character files.

### 9.3 Typing animation coalescing

Do not attempt to render one full animation per physical input when the user types rapidly.

Use an accumulator such as:

```text
pendingTypingImpulse += keyboardDelta
pendingMouseImpulse += mouseDelta
```

and consume at a bounded visual rate.

Requirements:

- counter accuracy is independent from visual animation rate;
- alternating typing hands still reads as responsive;
- long bursts cannot create an unbounded animation queue;
- animation-disabled mode discards visual impulses while retaining count totals.

### 9.4 CRT rendering

The CRT should resemble a green phosphor/digital display, not a Win98 UI control.

Requirements:

- large legible numeric value;
- no commas if they reduce readability at small sizes unless tested and accepted;
- support at least seven digits from launch and scale/abbreviate safely beyond that;
- clicking the visible screen changes display scope without beginning a drag;
- CRT display updates only when the visible value/mode changes;
- no expensive texture recreation every frame.

For very large lifetime values, prefer deterministic compact notation only if the full value no longer fits; otherwise retain the exact integer.

---

## 10. Transparent window and hit ownership

### 10.1 Window shape

Work Mode reuses the existing application window and temporarily configures it as a compact transparent companion window.

Do not create a second ordinary Desktop Buddy process/window.

The compact Work bounds should tightly contain:

- buddy;
- PC;
- desk/keyboard/mouse;
- a small safety inset for animation;
- the optional hover control.

All unused pixels remain transparent.

### 10.2 Pointer hit regions

Only meaningful visible regions consume clicks.

Semantic regions:

- `BuddyExitRegion` — receives double-click exit and eligible drag starts;
- `CrtToggleRegion` — single-click toggles Session/Lifetime;
- `AnimationToggleRegion` — toggles reactive animation;
- `ResizeRegion` — starts a native bottom-right resize while held;
- `DragRegion` — desk/PC/buddy visible regions that are safe to drag;
- transparent empty pixels — click through where supported by the existing shell's dynamic hit-region model.

Resolve precedence deterministically:

```text
Resize > AnimationToggle > CRT > BuddyDoubleClick > Drag > click-through
```

A drag crossing the double-click timing window must not accidentally exit.

### 10.3 Dragging behavior

- start only after pointer displacement exceeds the drag threshold;
- while dragging, capture pointer until release;
- clamp window bounds to recoverable monitor work area;
- persist the final position after release, not on every mouse move;
- do not count internal drag movement as Work actions;
- ordinary global mouse-button-down events may still increment the aggregate click counter according to the activity source; dragging itself adds no extra count.

The adjacent resize control uses the diagonal resize cursor and preserves the companion's visual
proportions. Work size is clamped to the usable monitor area and persisted independently from the
normal game window size.

---

## 11. Preferences versus progress

### Machine/UI preferences

Keep these outside progression reset where existing project policy does so:

- Work companion screen position;
- Work companion size;
- selected CRT display mode (Session/Lifetime);
- reactive animation enabled/disabled;

### Progress/save data

These affect rewards/ownership and belong to durable player progress:

- lifetime Work counters;
- claimed lifetime milestone IDs;
- first-entry glasses grant completion;
- glasses cosmetic ownership through the shared cosmetic ownership model;
- Work reward transactions through the economy ledger.

Current session counters are transient except for crash-safe checkpoints/session journal state.

Reset Progress follows the project's existing policy: progression counters/reward claims reset; machine positioning/animation preferences survive.

---

## 12. Persistence and crash safety

Introduce the Work data through the next legitimate save-schema migration rather than unversioned sidecar state when it belongs to core progress.

Migration defaults:

```text
lifetime keyboard = 0
lifetime mouse = 0
claimed milestones = empty
first-entry glasses granted = false
```

Existing players therefore receive the first-entry glasses reward on their first Work entry after the feature ships.

If a development save already contains an earlier experimental Work counter, migrate only if its semantics are proven equivalent; otherwise do not guess.

Session checkpoint data should include only:

- Work SessionId;
- aggregate session counters;
- repeat-per-session milestone IDs already earned/settled;
- enough settlement state to prevent duplicate reward claims.

No raw input history is persisted.

---

## 13. UI/UX details

### 13.1 Entry

The existing top-level **Work** command becomes the deliberate Work Mode toggle/entry.

Before transition, status/help copy should make the behavior understandable on first use, including the privacy claim in concise language such as:

`Work Mode counts key presses and mouse clicks only. It never stores what you type.`

Do not show this as a blocking confirmation on every entry. First-entry onboarding may show one short Win98-styled explanation.

### 13.2 Minimal companion

Persistent visible UI is intentionally near-zero:

- CRT number;
- buddy/desk/PC art;
- no title bar;
- no menu bar;
- no status bar;
- no border;
- no room background.

Hover-only controls may include:

- pause/resume reactive animation;
- optional future Reset Position via context menu.

### 13.3 Exit affordance

Primary: double-click the buddy.

First-entry onboarding should explicitly teach:

`Double-click your buddy to return to Desktop Buddy.`

A recovery path must still exist through the tray/global hotkey so Work Mode can never trap the player if double-click handling fails.

### 13.4 Payout feedback

When returning to normal windowed mode:

- if no milestone reward earned: return directly without unnecessary popup;
- if rewards earned: show a compact Win98-styled summary listing milestone(s) and total credits earned;
- do not list raw input history;
- lifetime/session totals may be summarized.

---

## 14. Performance requirements

Work Mode is intended to coexist with real work and should be cheaper than normal gameplay.

Targets:

- no active ragdoll physics unless a later feature explicitly requires it;
- no disk write per key/click;
- no texture recreation per input event;
- no allocation-heavy OS hook callback;
- aggregate many OS events into one main-thread update;
- CRT redraw/update only on value change;
- bounded animation frequency;
- animation-disabled mode should reduce Work presentation cost further;
- global input capture must not measurably delay user typing/clicking.

Measure CPU/GPU and memory during:

- idle Work Mode;
- sustained 120 WPM-like typing;
- burst clicking;
- animation enabled;
- animation disabled;
- four-hour Work soak.

---

## 15. Accessibility and usability

- counter must remain readable under supported Windows DPI scales;
- CRT color/brightness cannot be the only indicator of Session/Lifetime mode;
- animation toggle requires a tooltip/accessibility description;
- dragging must not require pixel-perfect selection;
- first-entry instructions explain drag, CRT toggle, animation toggle, and double-click exit;
- reactive animation toggle supports players who find motion distracting;
- Work Mode must remain recoverable through existing tray/global recovery controls.

---

## 16. Implementation slices

## WM0 — domain counters, milestones, and persistence contract

Deliver:

- `WorkCounterSnapshot`;
- `WorkSessionState`;
- `WorkLifetimeState` or integration into the established progress root;
- milestone definitions/evaluator;
- repeat-policy model;
- schema migration;
- idempotent reward-claim identities;
- unit tests.

Exit gate:

- exact threshold crossing;
- no double claims;
- repeat-per-session reset behavior;
- Int64/saturation boundary tests;
- migration from pre-Work saves.

## WM1 — Windows global activity source

Deliver:

- `IWorkActivitySource`;
- Windows unfocused keyboard/mouse adapter;
- held-key-repeat suppression;
- start/stop/failure handling;
- privacy-safe diagnostics;
- aggregated main-thread bridge.

Exit gate:

- one physical key press -> one keyboard increment;
- held key repeat -> one increment until released/pressed again;
- L/R/M button-down -> one click each;
- wheel/movement -> zero;
- callbacks never block or suppress input;
- no raw key identity crosses into domain/logs.

## WM2 — compact transparent Work presentation

Deliver:

- `WorkCompanionView`;
- original retro PC/CRT/keyboard/mouse/desk visuals;
- seated active-character rendering;
- compact transparent bounds;
- lower-right work-area placement;
- monitor/DPI clamp;
- stored Work position;
- semantic hit regions.

Exit gate:

- only intended art is visible;
- transparent pixels do not behave like a giant opaque window region;
- active custom character renders consistently;
- normal shell geometry restores exactly after exit.

## WM3 — reactive animation and CRT interactions

Deliver:

- coalesced alternating typing animation;
- mouse-click reaction;
- seated idle;
- animation enable/disable control;
- CRT Session/Lifetime toggle;
- display-mode preference;
- drag behavior;
- double-click exit gesture.

Exit gate:

- counters remain exact under rapid input even if animation cannot render every action;
- animation-off still counts/rewards;
- CRT click never initiates drag;
- drag never triggers accidental exit;
- double-click reliably restores normal mode.

## WM4 — rewards and first-entry glasses

Deliver:

- initial milestone catalogue;
- reward-ledger settlement;
- pending/claimed persistence;
- first-entry glasses ownership grant;
- no active-character equipment mutation;
- payout summary UI.

Exit gate:

- 10k session total, 10k session keyboard, and 1m lifetime total are representable/tested with accelerated counters;
- no milestone pays twice after restart/crash/re-entry;
- glasses grant happens once;
- later Work entries respect the player's chosen glasses state.

## WM5 — crash safety, low-cost mode, and integration

Deliver:

- periodic checkpoint/session journal;
- quit/shutdown/tray recovery;
- Work entry restrictions around modal/editor states;
- normal passive-income clock integration;
- performance tuning;
- four-hour Work soak;
- standalone Windows validation.

Exit gate:

- crash/restart does not duplicate settled rewards;
- no missing recovery path;
- normal gameplay does not run hidden at full cost;
- global capture always unregisters on exit;
- stable across supported monitor/DPI changes.

---

## 17. Automated verification

### 17.1 Domain tests

- keyboard + mouse aggregation;
- total derived exactly;
- milestone below/equal/above threshold;
- multiple thresholds crossed by one aggregated delta;
- OnceLifetime claim idempotency;
- RepeatPerSession claim identity;
- fresh SessionId permits repeatable session milestone again;
- lifetime counters persist across sessions;
- current-session counters reset on a new Work session;
- migration defaults;
- first-entry glasses flag transaction states;
- reset-progress semantics.

### 17.2 Windows adapter tests / harness

Where automation permits:

- synthetic adapter events rather than injecting real user input into unit tests;
- pressed-key transition state;
- repeat suppression;
- wheel rejection;
- left/right/middle acceptance;
- start/stop idempotency;
- event-after-stop rejection;
- callback-to-main-thread aggregation under high event volume.

Do not build tests that record real typed content.

### 17.3 Godot/headless scenarios

Recommended scenarios:

- `work_mode_counter_pipeline`
- `work_mode_milestone_claims`
- `work_mode_first_entry_glasses`
- `work_mode_display_toggle`
- `work_mode_animation_toggle`
- `work_mode_geometry_restore`
- `work_mode_position_recovery`

Headless tests should substitute a fake `IWorkActivitySource`.

### 17.4 Real-input journey

Recommended journey: `work_mode_typing_companion`

Flow:

1. start in normal windowed mode;
2. enter Work;
3. verify compact transparent composition;
4. first entry grants glasses ownership once without equipping it;
5. feed real keyboard and L/R/M input through the Windows test harness where safe;
6. verify wheel does not count;
7. verify CRT current count;
8. click CRT and verify lifetime mode;
9. drag the companion and verify persisted position;
10. disable reactive animation;
11. feed more actions and verify counters still increase;
12. cross accelerated milestone thresholds;
13. double-click buddy;
14. verify one reward settlement;
15. verify normal window geometry restoration;
16. enter Work again and verify no duplicate glasses grant / no duplicate lifetime claim.

---

## 18. Manual Windows test matrix

Test on Windows 10/11 as supported by the project.

Display scaling:

- 100%
- 125%
- 150%
- 200%

Window/desktop cases:

- bottom taskbar;
- left/right/top taskbar where available;
- primary monitor;
- secondary monitor;
- monitor disconnected while Work is active;
- resolution change while active;
- taskbar work-area change;
- always-on-top interactions with common applications;
- transparent-pixel click-through;
- dragging over text-heavy applications;
- restore after tray/global recovery.

Input cases:

- normal typing;
- held letter;
- held modifier;
- modifier shortcuts;
- fast typing;
- L/R/M clicks;
- mouse wheel;
- mouse movement;
- animation on/off;
- CRT click;
- buddy double-click;
- drag threshold.

Privacy inspection:

- examine game log during Work Mode and confirm no key codes/text/window titles/process names are present;
- inspect persisted Work data and confirm only aggregate counters/claim IDs exist.

---

## 19. Future nice-to-have scope

Not part of launch Work Mode:

- alternate retro PC/desk cosmetic sets;
- user-selectable Work poses;
- different CRT themes;
- separate keyboard/click counter pages beyond Session/Lifetime combined total;
- small milestone progress indicator;
- sounds synchronized with typing, default off or separately toggleable;
- configurable companion scale;
- taskbar-edge snapping;
- multiple Work buddy positions/profiles;
- richer idle animations;
- achievement/platform integration for Work milestones after M6;
- Steam stat mirroring of aggregate lifetime counters;
- optional per-application exclusion lists **only if they can be implemented without recording/persisting application activity history**.

No future feature may weaken the core privacy rule by recording typed content.

---

## 20. Definition of done

Revised Work Mode is complete when:

- the normal Win98 app can deliberately transition into a compact transparent Work companion and back;
- active buddy appearance, paint, clothing, glasses, and headwear render correctly in the seated Work presentation;
- keyboard physical down-transitions and L/R/M clicks are counted globally while Work is active;
- wheel/movement/held-repeat are not counted;
- no typed content/key identity/application information is persisted or logged;
- CRT toggles correctly between current-session and lifetime combined action totals;
- Work companion starts lower-right above the taskbar, can be freely dragged, persists position, and recovers across monitor/DPI changes;
- reactive typing/click animation works and can be disabled without disabling counting;
- double-clicking the buddy exits, restores normal window geometry, and settles pending milestone rewards exactly once;
- the first Work entry grants the free glasses once without equipping them, and every entry respects player customization;
- milestone architecture supports at minimum 10k session actions, 10k session keyboard presses, and 1m lifetime actions;
- reward amounts are economy-calibrated before release;
- crash/restart cannot duplicate claims;
- performance is appropriate for prolonged foreground desktop use;
- four-hour Work soak and Windows/DPI/input/recovery matrices pass;
- owner accepts the final companion size, placement, animation intensity, CRT readability, drag feel, and reward pacing.
