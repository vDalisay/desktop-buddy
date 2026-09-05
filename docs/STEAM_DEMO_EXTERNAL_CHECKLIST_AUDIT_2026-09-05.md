# Steam Demo Checklist — external audit

Source: "Steam Demo Checklist" (Notion, marnixwyns), read 2026-09-05.
Audited against this worktree at `main` (`b44e4a8b`). Code-level audit only —
nothing here was play-tested.

**Update 2026-09-05:** six items were fixed on this branch after the first pass;
their rows below carry a "Fixed" note. One row was also **corrected** — the
original "single-take SFX are not pitch randomized" finding overstated the gap.
See "Correction" at the end.

Legend: **YES** met · **PARTIAL** partly met · **NO** not met · **N/A** the item
assumes a conventional windowed game and does not map onto a desktop pet.

---

## Game

| Item | Verdict | Evidence |
|---|---|---|
| No breaking bugs (a demo is not a playtest) | OWNER GATE | Cannot be settled by inspection. CI runs the quick + full journey suites and `DemoCleanSaveAcceptanceTests`; the remaining risk is the manual Windows 10/11 pass. |
| Pause menu (pauses the game, access to settings) | **NO** | `src/App/GameplayPauseCoordinator.cs` is the sole writer of `SceneTree.Paused` and only three non-player reasons exist: `HiddenToTray`, `Suspended`, `CharacterEditor`. There is no player-invoked pause and no Escape binding. Settings *are* always reachable from the Win98 command bar, so the "access to settings" half is covered; the "pauses the game" half is not. |
| Steam overlay (Shift+Tab) pauses the game | **FIXED** | Was: the bridge connected only Workshop signals, so nothing ever called `GameplayPauseCoordinator.Set`. Now `GodotSteamBridge.gd` connects GodotSteam's optional `overlay_toggled` and re-emits it as `steam_overlay_toggled`; `WorkshopBootstrap.ConnectOverlayPause` maps it to the new `GameplayPauseReason.SteamOverlay`. The bridge is `PROCESS_MODE_ALWAYS` and keeps pumping `run_callbacks` while paused, so the un-toggle always arrives. No GodotSteam, no overlay, nothing to pause for. |
| Core loop shown in the first 3 minutes | **YES** | The app boots straight into the shell with the Buddy present; the first tutorial step is Grab Buddy. No wandering, no gate. |
| Clear ending + CTA | **NO** | There is no demo-end state at all, and the only CTA in the codebase (`src/App/ItchWishlistBootstrap.cs`) is gated behind `DemoScope.IsItchIo`, so the Steam build never shows it. Its `SteamStoreUrl` is also still `""`. |
| Do not hijack the Quit button | **YES** | Every quit path (`Bootstrap.cs:331`, `BuddyLab.cs:551`, `SandboxRoot.cs:576`) calls `GodotInteropShutdown.PrepareForQuit()` then `GetTree().Quit()`. The only `OS.ShellOpen` calls are the explicit wishlist button and the Save Folder settings row. |

## Onboarding

| Item | Verdict | Evidence |
|---|---|---|
| Starts with a main menu | **N/A** | Genre mismatch. Desktop Buddy is a transparent always-on-top desktop pet whose shell *is* the UI (`Win98BuddyShellController`). A main menu would add a screen between the player and the product. Recommend treating the Win98 command bar as the menu surface — and then honouring the items below against it. |
| Main menu is animated/dynamic | **N/A** | Follows from the above. The live Buddy is the dynamic element. |
| Links to socials/support from the main menu | **FIXED** | A `Community` command-bar entry opens a Win98 dialog with Discord, YouTube and X buttons (`src/App/CommunityLinksBootstrap.cs`, formerly `ItchWishlistBootstrap`). |
| No opening cutscenes / cinematics / lore dumps | **YES** | None exist. |
| Contains a tutorial | **YES** | `src/Onboarding/FirstSessionGuidanceController.cs` — a persisted, skippable, spotlighted step sequence with a dedicated tutorial character presenter, replayable from Settings ("Show Tutorial Again", gated on `DemoScope.IncludesTutorial`). Covered by `DemoCleanSaveAcceptanceTests`. |
| Controls explicitly displayed (prompts or input diagram) | **PARTIAL** | Strong per-element coverage: a Help mode (`ContextHelpButton`) plus ~35 authored hover explanations, and both hotkeys are shown in Settings. But there is no single controls screen or input diagram, and mouse verbs (double-click to re-equip a dropped tool, click-and-hold to drag in Work Mode) are only discoverable through Help mode or the tutorial. |
| Does not soft-lock non-QWERTY players | **YES** | Every keyboard binding in `project.godot` uses `physical_keycode` (R, D, Ctrl+Shift+B/H/Q), which is layout-independent, and both player-facing hotkeys are rebindable through `AddHotkey` with capture + Escape-cancel. |

## Game Feel

| Item | Verdict | Evidence |
|---|---|---|
| SFX on button hover + click | **PARTIAL** | Click: yes, and well built — `UiFeedbackAudioBootstrap` auto-hooks every button in the tree with cue + layer tags. Hover: deliberately silent, documented in the class summary as "hover/focus changes stay silent to avoid fatiguing desktop use". That reasoning is sound for an always-on-desktop app; it is a conscious deviation, not an oversight. |
| Scale on hover, squish on click | **NO** | `Win98Motion.Pulse` exists and does exactly this, but has exactly one caller — the Buddy Studio Buy button (`BuddyStudioWorkspace.CaptureStorePolish.cs:119`). No hover scale anywhere. |
| Windows are tweened, not instant | **PARTIAL** | `Win98Motion.Reveal` exists behind the "Modern UI Motion" setting but has no callers outside the motion helpers; only `RewardPopup` and the Work reward feedback consult `Win98MotionPolicy`. Win98 dialogs and panels appear instantly. Note that instant windows are also period-correct for the Win98 shell — this is a taste call, not a defect. |

## Audio

| Item | Verdict | Evidence |
|---|---|---|
| Has music | **NO** | No music assets (`assets/` holds only `sfx`, `ui`, `work`) and no Music bus in `default_bus_layout.tres` (Master / SFX / UI only). |
| Has an ambient sound layer | **NO** | Same — nothing ambient exists. For a desktop pet a quiet room-tone loop would be the natural fit and would also give the Ambient slider something to control. |
| SFX for all major interactions | **YES** | Broad authored coverage: ball, bat, buddy impacts, fire, grab, grenade, gun, item, paint, ui, work typing. `devtools/verify_sfx_imports.sh` runs in quick and full CI so a missing `.import` sidecar fails the build instead of silently vanishing from the export. |
| SFX always random pitched | **FIXED** (finding corrected — see below) | Essentially everything was already randomized: multi-take cues through `AudioStreamRandomizer`, single-take cues through explicit `BuildRandomized`/`SfxRandomizer.Pick` calls, UI presses ±4%, flamethrower ±4%, paint strokes and Work typing with their own wobble. The one genuine exception was `ReactionAudioPresenter._itemFalling` — the generic dropped-item fallback, and therefore the most-repeated cue of the lot — which was assigned raw. It now goes through `SfxRandomizer.Pick(1.5f, …)`. |
| Starts with all volumes at 50% | **FIXED** (owner chose 80%) | `MasterVolume`, `SfxVolume` and `UiVolume` now default to `0.8f` in `ProgressSave`. The checklist says 50%; the owner picked 80% for a desktop pet whose cues are short and quiet by design. |

## Settings

| Item | Verdict | Evidence |
|---|---|---|
| Display mode (Windowed / Borderless / Fullscreen) | **N/A** | The window is a borderless per-pixel-transparent always-on-top overlay by design (`StartupValidator` asserts it). The analogous control exists: Compact vs Fullscreen Overlay layout modes, plus an Always On Top toggle. |
| V-Sync toggle | **YES** | Display settings group. |
| Framerate limiter | **FIXED** | Now `30 / 60 / 120 / 165 / 240 / Unlimited`, plus the Background Frame Limit the checklist does not ask for. The old index-0 label "V-Sync" was also wrong for the value it carried: `MaxFps = 0` is Godot's *no engine limit*, so it is now labelled Unlimited and V-Sync keeps its own toggle. |
| Resolution dropdown | **N/A** | Meaningless for a desktop overlay. UI Scale (100–200%), Buddy Size (75–200%) and a Monitor picker cover the same intent. |
| Sound sliders (Main, Music, SFX, Ambient) | **DONE as scoped** | Master, Sound Effects and Interface Sounds exist and each owns exactly one bus. Owner decision 2026-09-05: there is no music and no ambient layer, so those two sliders are deliberately not shipped — an empty slider is worse than no slider. Revisit only if music is added. |
| Localization | **NO** | No `[locale]` / translation config in `project.godot`, no `.po`/`.csv` catalogues, no language setting. All player text is hardcoded English. The checklist marks this skippable. |

## Marketing

| Item | Verdict | Evidence |
|---|---|---|
| Demo release trailer uploaded | OWNER | Outside the repo. `STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md` schedules it as phase DEMO-M3. |
| Demo on itch.io | OWNER | An itch export preset exists (`Web itch.io experimental`, features `itch_io,protected_build,web_experimental`) and `DemoScope` has a full itch-reduced surface. Whether it is uploaded is not knowable here. |
| Wishlist buttons in main menu + ending CTA (UTM or overlay) | **PARTIAL** (menu half fixed) | The wishlist command is no longer itch-only — every non-full-release build now shows it, pointing at `store.steampowered.com/app/5114950/Desktop_Buddy` via `OS.ShellOpen`. Still open: there is no *ending* CTA because there is no demo ending (see below), and the link carries no UTM parameters. Switching the Steam build to `activateGameOverlayToStore` would keep the player in-client, but needs the bridge method plumbed through and is not required by the checklist. |
| Content creator outreach 1–2 weeks ahead | OWNER | Process item. |

---

## Summary

After the 2026-09-05 fixes: Met: 12 · Partial: 2 · Not met: 4 · N/A or owner-scoped: 10

### Fixed on this branch

1. **Steam overlay pause** — `GodotSteamBridge.gd` + `GameplayPauseReason.SteamOverlay` + `WorkshopBootstrap.ConnectOverlayPause`.
2. **Wishlist CTA** — no longer itch-only, real store URL.
3. **Socials/support** — Community command with Discord / YouTube / X.
4. **Frame limits** — 30 / 60 / 120 / 165 / 240 / Unlimited.
5. **Default volumes** — 80%.
6. **`_itemFalling` pitch wobble** — the one cue that was still playing back identical.

### Closed by decision, not by code

- **Music and Ambient sliders** — no music, no ambient layer, so no sliders (owner, 2026-09-05).

### Still open

7. **Music + ambient layer** — the largest remaining gap against the checklist, and a content decision rather than an engineering one.
8. **A player-facing pause** — needs a design decision first. "Pause" for an always-running desktop pet is not obviously the same thing as pause in a windowed game.
9. **A demo ending + ending CTA** — there is currently no moment that says the demo is over, which is also why only the menu half of the wishlist item is closed.
10. **Hover/press motion and window tweens** — the helpers already exist; this is wiring plus a taste call against the Win98 aesthetic.
11. **Localization** — none; the checklist marks it skippable.

### Deliberate deviations, worth keeping

- No main menu, no display-mode or resolution settings: the product is a desktop overlay, not a windowed game.
- Silent hover SFX: documented decision, correct for software that lives on the desktop all day.

---

## Correction

The first pass reported "single-take cues play identically every time" as a general
gap, reasoning from `BuildVariations` returning its input unchanged at one stream.
That was wrong as a general claim: every single-take cue that reaches that code
(`GloveCriticalHeadImpact`, `PistolReload`, `ToygunReload`, `GrabInitial`) is
already routed through `BuildRandomized` explicitly, and the rest of the codebase
uses the shared `SfxRandomizer.Pick` helper, which always wraps. The real defect
was a single assignment — `_itemFalling` — not a class of them.
