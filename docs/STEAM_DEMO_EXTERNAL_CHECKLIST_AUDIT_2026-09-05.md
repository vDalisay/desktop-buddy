# Steam Demo Checklist — external audit

Source: "Steam Demo Checklist" (Notion, marnixwyns), read 2026-09-05.
Audited against this worktree at `main` (`b44e4a8b`). Code-level audit only —
nothing here was play-tested.

Legend: **YES** met · **PARTIAL** partly met · **NO** not met · **N/A** the item
assumes a conventional windowed game and does not map onto a desktop pet.

---

## Game

| Item | Verdict | Evidence |
|---|---|---|
| No breaking bugs (a demo is not a playtest) | OWNER GATE | Cannot be settled by inspection. CI runs the quick + full journey suites and `DemoCleanSaveAcceptanceTests`; the remaining risk is the manual Windows 10/11 pass. |
| Pause menu (pauses the game, access to settings) | **NO** | `src/App/GameplayPauseCoordinator.cs` is the sole writer of `SceneTree.Paused` and only three non-player reasons exist: `HiddenToTray`, `Suspended`, `CharacterEditor`. There is no player-invoked pause and no Escape binding. Settings *are* always reachable from the Win98 command bar, so the "access to settings" half is covered; the "pauses the game" half is not. |
| Steam overlay (Shift+Tab) pauses the game | **NO** | `src/Platform/Steam/GodotSteamBridge.gd` connects only Workshop signals (`item_created`, `item_updated`, `ugc_query_completed`, download results). No `overlay_toggled` handler exists anywhere, so nothing calls `GameplayPauseCoordinator.Set`. Adding it is a ~5-line change: one signal, one new `GameplayPauseReason`. |
| Core loop shown in the first 3 minutes | **YES** | The app boots straight into the shell with the Buddy present; the first tutorial step is Grab Buddy. No wandering, no gate. |
| Clear ending + CTA | **NO** | There is no demo-end state at all, and the only CTA in the codebase (`src/App/ItchWishlistBootstrap.cs`) is gated behind `DemoScope.IsItchIo`, so the Steam build never shows it. Its `SteamStoreUrl` is also still `""`. |
| Do not hijack the Quit button | **YES** | Every quit path (`Bootstrap.cs:331`, `BuddyLab.cs:551`, `SandboxRoot.cs:576`) calls `GodotInteropShutdown.PrepareForQuit()` then `GetTree().Quit()`. The only `OS.ShellOpen` calls are the explicit wishlist button and the Save Folder settings row. |

## Onboarding

| Item | Verdict | Evidence |
|---|---|---|
| Starts with a main menu | **N/A** | Genre mismatch. Desktop Buddy is a transparent always-on-top desktop pet whose shell *is* the UI (`Win98BuddyShellController`). A main menu would add a screen between the player and the product. Recommend treating the Win98 command bar as the menu surface — and then honouring the items below against it. |
| Main menu is animated/dynamic | **N/A** | Follows from the above. The live Buddy is the dynamic element. |
| Links to socials/support from the main menu | **NO** | Grep finds no Discord, Steam Discussions, support or community link anywhere in `src/`. The Data settings group offers only Save Folder, Reset Progress and Show Tutorial Again. This one is genuinely missing and cheap: two `AddAction` rows or two command-bar entries. |
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
| SFX always random pitched | **PARTIAL** | Multi-take cues go through `AudioStreamRandomizer` with `RandomPitchSemitones` and `RandomNoRepeats` (`ReactionAudioPresenter.BuildVariations`); UI presses vary ±4%; the flamethrower varies ±4%. But `BuildVariations` returns the raw stream unchanged when only one take exists, so single-take cues play identically every time. `BuildRandomized` already exists — routing the single-take path through it closes this. |
| Starts with all volumes at 50% | **NO** | `ProgressSave.cs:347-349` — `MasterVolume`, `SfxVolume`, `UiVolume` all default to `1.0f`. |

## Settings

| Item | Verdict | Evidence |
|---|---|---|
| Display mode (Windowed / Borderless / Fullscreen) | **N/A** | The window is a borderless per-pixel-transparent always-on-top overlay by design (`StartupValidator` asserts it). The analogous control exists: Compact vs Fullscreen Overlay layout modes, plus an Always On Top toggle. |
| V-Sync toggle | **YES** | Display settings group. |
| Framerate limiter | **PARTIAL** | Present, plus a Background Frame Limit the checklist does not ask for. But the set is `V-Sync / 30 / 60 / 120` — 165, 240 and unlimited are missing. One-line fix in `FrameLimits` / `FrameLimitLabels`. |
| Resolution dropdown | **N/A** | Meaningless for a desktop overlay. UI Scale (100–200%), Buddy Size (75–200%) and a Monitor picker cover the same intent. |
| Sound sliders (Main, Music, SFX, Ambient) | **PARTIAL** | Master, Sound Effects and Interface Sounds exist and each owns exactly one bus. Music and Ambient are missing because those layers do not exist — this item resolves itself once the two Audio gaps above are closed. |
| Localization | **NO** | No `[locale]` / translation config in `project.godot`, no `.po`/`.csv` catalogues, no language setting. All player text is hardcoded English. The checklist marks this skippable. |

## Marketing

| Item | Verdict | Evidence |
|---|---|---|
| Demo release trailer uploaded | OWNER | Outside the repo. `STEAM_DEMO_POLISH_AND_MARKETING_PLAN.md` schedules it as phase DEMO-M3. |
| Demo on itch.io | OWNER | An itch export preset exists (`Web itch.io experimental`, features `itch_io,protected_build,web_experimental`) and `DemoScope` has a full itch-reduced surface. Whether it is uploaded is not knowable here. |
| Wishlist buttons in main menu + ending CTA (UTM or overlay) | **NO** | The wishlist command and the welcome dialog are itch-only (`DemoScope.IsItchIo`), so the Steam demo has no wishlist affordance at all, and `SteamStoreUrl` is still `""`. For the Steam build the CTA should point at the *full game's* store page — best through the Steam overlay (`activateGameOverlayToStore`) rather than a browser shell-open. |
| Content creator outreach 1–2 weeks ahead | OWNER | Process item. |

---

## Summary

Met: 6 · Partial: 6 · Not met: 8 · N/A by genre: 4 · Owner/process: 4

### Cheap and worth doing

1. **Steam overlay pause** — connect the overlay signal in `GodotSteamBridge.gd`, add a `GameplayPauseReason.SteamOverlay`. Small, and it is one of the two items the checklist calls out by name.
2. **Wishlist CTA in the Steam build** — ungate `ItchWishlistBootstrap` (or a Steam sibling), fill in the store URL, prefer the overlay store call.
3. **Socials/support links** — two rows in the command bar or the Settings Data group.
4. **Frame limits** — add 165 / 240 / Unlimited.
5. **Default volumes to 50%** — three field defaults in `ProgressSave`.
6. **Single-take SFX pitch** — route the `validCount == 1` branch of `BuildVariations` through `BuildRandomized`.

### Real work

7. **Music + ambient layer** — two new buses, two sliders, and the assets. This is the largest genuine gap; it also unblocks the Sound-sliders item.
8. **A player-facing pause** — needs a design decision first. "Pause" for an always-running desktop pet is not obviously the same thing as pause in a windowed game.
9. **A demo ending** — there is currently no moment that says the demo is over. Also a design decision, not just code.
10. **Hover/press motion and window tweens** — the helpers already exist; this is wiring plus a taste call against the Win98 aesthetic.

### Deliberate deviations, worth keeping

- No main menu, no display-mode or resolution settings: the product is a desktop overlay, not a windowed game.
- Silent hover SFX: documented decision, correct for software that lives on the desktop all day.
