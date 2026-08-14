# SFX Manifest (branch `SFX`)

Drop files here: `assets/sfx/<id>.<ext>` — the `id` column below **is** the path under
`assets/sfx/`, so `ui/click` lives at `assets/sfx/ui/click.mp3`. Category = folder:
`assets/sfx/buddy/`, `tool/`, `bat/`, `gun/`, `grenade/`, `fire/`, `launch/`, `ball/`,
`care/`, `grab/`, `item/`, `drink/`, `repair/`, `world/`, `ui/`.

Format: `.mp3` or 16-bit PCM `.wav`, mono, trimmed to zero-crossings, peak ≈ -6 dBFS.
Loops must loop seamlessly (loop points come from the file, not from code).

**After adding a file, run `devtools\import_assets.bat`** (or open the editor once) so Godot
writes the `.import` sidecar — without it the clip works in a local run but is left out of an
exported build. Commit the `.import` next to the audio.

Every clip is played through an `AudioStreamRandomizer`: `RandomNoRepeats` across variations,
plus a small random pitch/volume per press so repeats aren't bit-identical. UI stays subtle
(pitch scale 1.04 ≈ ±4%); gameplay clips can go wider.

Variations: to get random pick-one, drop `name_1.wav`, `name_2.wav`, … instead of `name.wav`.
Nothing is required — any id with no file falls back to the existing synthesized clip (or silence
for the rows marked NEW).

Status column: **SYNTH** = already fires at the right moment today with a generated clip, dropping
a file just replaces it. **NEW** = the trigger exists in code but nothing plays yet.

---

## 0. How a control gets its sound

`UiFeedbackAudioBootstrap` is an autoload that hooks every `BaseButton`, `PopupMenu` and
`ItemList` as it enters the tree, anywhere in the game. Build a new menu and its controls are
already wired — there is nothing to remember to call.

What each control sounds like is decided in this order:

1. **A declared tag** — `UiFeedbackAudioBootstrap.Tag(button, UiSfx.Exit)` or
   `Tag(button, layer: UiSfx.Money)`. Stored as node metadata, read first.
2. **The label guess** — matching "close", "buy", "save" and friends in the button's text and
   node name.

**Prefer the tag for anything shared or translatable.** The label guess reads user-visible
text, so a translated build silently loses the sound: `PaintUiText` already routes labels
through `TranslationServer`, and "Close" becoming "Cerrar" matches nothing. The tag is also the
only correct answer when one button means two things — the shop's Buy/Equip button sets its own
layer on each refresh, because only the panel knows which it currently is.

Already tagged at the source, so every screen inherits them: the Win98 dialog close box, the
Win98 window frame's `×`, the Work Mode exit button, the shop and Buddy Studio buy/equip
buttons, and the Tools panel's equip button.

3. **The action itself** — for commitments, the code that performs the action calls
   `UiFeedbackAudioBootstrap.TryPlayLayer(this, UiSfx.Money)` and the button carries
   `UiSfx.NoLayer`. This is the right owner whenever an action has more than one route in: a
   Buddy Studio cosmetic can be bought by pressing Buy *or* by double-clicking its catalogue
   tile, which is not a button press at all. It also keeps a failed press — too expensive,
   nothing selected — honestly silent.

Same rule for whole modes: leaving Work Mode sounds from the coordinator, because the corner X
and a double-click on the companion are both exits, and the X is tagged `UiSfx.Silent` so it
does not sound twice.

**Timing:** the cue is read on `ButtonDown` and played on `Pressed`. The control's own handler
runs first — it was connected at construction, the audio hook only on tree entry — and it
routinely rewrites the label the sound is chosen from. Reading late made every purchase sound
like an equip, because buying flips "Buy" to "Equip" before the audio handler ever runs.

---

## 1. Buddy

| id | when it fires | trigger | kind | status |
|---|---|---|---|---|
| `buddy/hurt_light` | small impact accepted (low pain) | `InteractionDamageComponent.ImpactAccepted` | one-shot, 3 variations | SYNTH (pain chirp) |
| `buddy/hurt_heavy` | big impact accepted (high pain) | same, pain ≥ threshold | one-shot, 3 variations | SYNTH |
| `buddy/hurt_glove` | boxing-glove hit specifically | `ContentIds.ToolBoxingGlove` | one-shot, 4 var | LIVE |
| `buddy/enjoy` | impact the buddy *likes* | `ImpactMoodEffectKind.Enjoyment` | one-shot, 2 var | SYNTH (care chirp) |
| `buddy/pet` | pet reward tick | `CareAwarded(CareKind.Pet)` | one-shot, 3 var | SYNTH |
| `buddy/tickle_laugh` | tickle reward tick, friendly | `CareAwarded(CareKind.Tickle)` | one-shot, 3 var | SYNTH |
| `buddy/tickle_annoyed` | tickle past the friendly window | `TickleDisposition.Angry` | one-shot, 2 var | NEW |
| `buddy/knockout` | buddy goes down | `KnockoutStarted` | one-shot | NEW |
| `buddy/wake_up` | buddy gets back up | `KnockoutEnded` | one-shot | NEW |
| `buddy/burn_loop` | while on fire | `BurnEventApplied` / burning state | **loop** | NEW |
| `buddy/burn_yelp` | each burn tick lands | `BurnEventApplied` | one-shot, 3 var | NEW |
| `buddy/repaired` | repair kit consumed, buddy healed | repair-kit apply | one-shot | NEW |
| `buddy/eat_bite` | one bite of a meal/drink | `EatBiteCompleted` | one-shot, 3 var | NEW |
| `buddy/eat_finish` | consume succeeded | `ConsumeSucceeded` | one-shot | NEW |
| `buddy/refuse` | "no thanks" head shake | `ActivityId.Refuse` | one-shot, 2 var | NEW |
| `buddy/catch_delight` | clean catch of a thrown thing | `FunCatchDelighted` | one-shot | NEW |
| `buddy/jump` | jump anticipation → launch | `ActivityId.JumpAnticipation` | one-shot | NEW |
| `buddy/land` | body lands on the floor | ground contact | one-shot, 2 var | NEW |
| `buddy/footstep` | walk cycle step | `ActivityId.WalkCycle` (per cycle) | one-shot, 4 var | NEW |
| `buddy/wave` | greeting wave | `ActivityId.Wave` | one-shot | NEW |
| `buddy/idle_hum` | occasional idle noise, low frequency | `ActivityId.IdleBreathe` | one-shot, 4 var | NEW |
| `buddy/bored` | boredom threshold crossed | fun/interest boredom flip | one-shot | NEW |
| `buddy/grabbed` | picked up by cursor grab | `GrabTetherController` attach | one-shot | NEW |
| `buddy/dropped` | released from grab | `Released` | one-shot | NEW |

## 2. Tools

### Shared / cursor
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `tool/equip` | active tool changes | `ToolChanged` | one-shot | NEW |
| `tool/spawn_body` | a tool body appears | `BodySpawned` | one-shot | NEW |
| `tool/whoosh` | any swing release | `SwingReleased` | one-shot, 3 var | SYNTH |
| `tool/object_hit` | tool hits a loose object (not the buddy) | `LooseObjectSwingHit` | one-shot, 3 var | NEW |

### Baseball bat (`tool.baseball_bat`)
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `bat/charge_start` | grip charge begins | `ChargeStarted` | one-shot | SYNTH |
| `bat/charge_full` | charge tops out | `ChargeCompleted` | one-shot | SYNTH |
| `bat/swing` | swing released | `SwingReleased` | one-shot, 3 var | SYNTH |
| `bat/homerun_crack` | charged hit connects | `ImpactAccepted` w/ swing epoch | one-shot, 2 var | SYNTH |

### Guns (`tool.pistol`, `tool.nerf_blaster`, `tool.shotgun`)
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `gun/pistol_fire` | pistol shot | `ShotFired` (per `GunProfile`) | one-shot, 2 var | LIVE |
| `gun/nerf_fire` | nerf shot | `ShotFired` | one-shot, 2 var | NEW |
| `gun/shotgun_fire` | shotgun shot | `ShotFired` | one-shot, 2 var | NEW |
| `gun/dry_fire` | trigger pulled, empty | empty-magazine path | one-shot | NEW |
| `gun/mag_drop` | magazine hits the floor | `MagazineBody` ground contact | one-shot, 2 var | NEW |
| `gun/reload` | real pistol reload begins | `ReloadStarted` for `tool.pistol` | one-shot, pitch variation | LIVE |
| `gun/projectile_hit` | projectile hits anything | `ProjectileBody` contact | one-shot, 3 var | NEW |

### Grenade (`tool.grenade`)
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `grenade/pin_pull` | pin pulled | `PinPulled` | one-shot | NEW |
| `grenade/pin_drop` | pin lands | `PinBody` contact | one-shot | NEW |
| `grenade/fuse_loop` | fuse burning | between pin pull and detonation | **loop** | NEW |
| `grenade/thud` | grenade lands hard | `GroundContact` | one-shot, 2 var | SYNTH |
| `grenade/boom` | detonation | `Detonated` | one-shot, 2 var | SYNTH |

### Fire sprayer (`tool.fire_sprayer`)
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `fire/hiss_loop` | stream held | `SprayingChanged(true/false)` | **loop** | SYNTH |
| `fire/ignition` | something catches fire | `Ignited` | one-shot, 2 var | SYNTH |
| `fire/burn_ambient_loop` | anything is still burning | burning-set non-empty | **loop** | NEW |
| `fire/extinguish` | last fire goes out | burning-set empties | one-shot | NEW |

### Launcher / throwables (`tool.baseball`, `tool.soccer_ball`)
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `launch/pullback` | pullback drag begins | `PullbackLauncherComponent` charge | one-shot | NEW |
| `launch/release` | throw released | launcher release | one-shot, 2 var | NEW |
| `ball/bounce` | ball bounces off floor/wall | loose-object contact | one-shot, 3 var | NEW |
| `ball/kick` | soccer ball struck | impact | one-shot, 2 var | NEW |

### Care tools (`tool.pet`, `tool.tickle`, `tool.grab`, `tool.power_grab`, `tool.meal`, `tool.drink`, `tool.repair_kit`)
| id | when | trigger | kind | status |
|---|---|---|---|---|
| `care/stroke_loop` | pet stroke in contact & moving | `CareStrokeComponent` | **loop** (or fast one-shots) | NEW |
| `grab/attach` | tether grabs a body | `GrabTetherController` | one-shot | NEW |
| `grab/release` | tether lets go | `Released` | one-shot | NEW |
| `grab/power_strain_loop` | power grab holding something heavy | power-grab active | **loop** | NEW |
| `item/place` | meal / drink / repair kit placed in the room | tool use | one-shot | NEW |
| `drink/gulp` | drink consumed | consume | one-shot, 2 var | NEW |
| `repair/apply` | repair kit contacts the buddy | contact apply | one-shot | NEW |

## 3. World / environment

| id | when | trigger | kind | status |
|---|---|---|---|---|
| `world/object_impact_soft` | light loose-object collision | contact, low speed | one-shot, 3 var | NEW |
| `world/object_impact_hard` | heavy loose-object collision | contact, high speed | one-shot, 3 var | NEW |
| `world/wall_bump` | body part positively impacts the room boundary | six `BuddyPartWallImpactDetector`s on `ImpactAccepted` | temporary normal Buddy pair, pain-scaled | **TEMPORARY LIVE** |
| `world/object_spawn` | loose object spawned | `LooseObjectSpawnRequested` | one-shot | NEW |
| `world/object_clear` | room cleared of loose objects | `LooseObjectClearRequested` | one-shot | NEW |

## 4. UI / shell / meta

Two clips cover every button today — **LIVE**, sitting in `assets/sfx/ui/`, wired in
[`UiFeedbackAudioBootstrap`](../src/UI/UiFeedbackAudioBootstrap.cs). Buttons are hooked as they
enter the tree, so nothing needs per-screen wiring.

| id | when | routing rule | status |
|---|---|---|---|
| `ui/ClickWithinMenus.mp3` | every ordinary button, popup-menu item, and `ItemList` row selection (character library, paint layers) | default for anything not matching a rule below | **LIVE** |
| `ui/ExitClick_HorizontalMenuClick.mp3` | close boxes and the horizontal strip | button text is exactly `×`/`X`, or its name/label contains close / cancel / exit / dismiss, **or** it sits under the Win98 `CommandRow` (Shop, Tools, Settings, Paint ▸, Work) or `DesktopToolbarWindow` | **LIVE** |
| `ui/Zoom_in.mp3`, `ui/Zoom_out.mp3` | — | unwired at owner request 2026-08-13; the zoom buttons play ClickWithinMenus. Files kept for a later pass | PARKED |

**Layer clips** — these play *on top of* the click above, on a second player (`UiFeedbackLayerPlayer`),
never instead of it. One layer per press, first rule that matches wins. Matched on the visible
label, falling back to the node name only when the button has no text: node names like
`BuddyStudioUnsavedDiscard` contain "save" and must not trigger a confirmation.

| id | when | routing rule | status |
|---|---|---|---|
| `ui/Money_purchase.mp3` | anything costing credits | `UiSfx.Money` tag (shop rows, Buddy Studio, decorator Buy); label contains buy / purchase otherwise | **LIVE** |
| `ui/inventory_equip.mp3` | equipping an owned item | `UiSfx.Equip` tag (shop rows, Buddy Studio, Tools panel); label contains equip / select otherwise | **LIVE** |
| `ui/Confirmation_equip_Save.mp3` | Save and Confirm presses | label contains save / confirm | **LIVE** |

The shop and Buddy Studio share one button whose label changes with ownership — Buy → Equip →
Equipped — so the layer is re-read at press time, not at hook time. Pressing it while it says
"Buy" gives the money clip; while it says "Equip", the equip clip.

The synthesized cues below still exist as fallbacks and for non-button moments; buttons no
longer reach `Purchase`/`Confirm`/`Caution` (owner call: one click sound for all of them).

| id | when | trigger | kind | status |
|---|---|---|---|---|
| `ui/reward` | credits / unlock granted | `Reward` | one-shot | SYNTH |
| `ui/error` | rejected action | `Error` | one-shot | SYNTH |
| `ui/resize` | window resize handled | `Resize` | one-shot | SYNTH |
| `ui/purchase` | shop buy (not currently routed) | `Purchase` | one-shot | SYNTH |
| `ui/confirm` | work-mode exit | `Confirm` | one-shot | SYNTH |
| `ui/caution` | fallback when the exit clip is missing | `Caution` | one-shot | SYNTH |
| `ui/window_open` | a Win98 window opens | window show | one-shot | NEW |
| `ui/window_close` | window closes | `CloseRequested` | one-shot | NEW |
| `ui/window_minimize` | minimize | `MinimizeRequested` | one-shot | NEW |
| `ui/coin_tick` | money HUD counting up | `BalanceChanged` | one-shot, short | NEW |
| `ui/slider_tick` | settings slider crosses one step | shared `SettingsPanel.AddSlider` path | one-shot, 6 var | LIVE |
| `ui/work_enter` | work mode entered | `ActiveChanged(true)` | one-shot | NEW |
| `ui/work_exit` | work mode exited | `ActiveChanged(false)` | one-shot | SYNTH (confirm) |
| `ui/startup` | app boot finished | bootstrap ready | one-shot | NEW |

---

## Wiring plan (once files land)

One `SfxLibrary` autoload: scans `res://assets/sfx/` once, maps id → `AudioStream` (picking a
random variation when `_1/_2/…` exist), plus a small `AudioStreamPlayer` pool so overlapping
one-shots don't cut each other off. Each existing SYNTH component swaps its
`Player.Stream = _synthClip` line for `SfxLibrary.Stream(id) ?? _synthClip`; NEW rows get a
one-line handler on the listed event. No per-tool audio classes beyond the four that exist.

Priority if you're recording in passes: **buddy hurt/pet/KO → guns → grenade/fire → world
impacts → UI**. That order is roughly how often the player hears each one.

## Next iteration notes (not shipped)

- `buddy/hurt_glove`: the supplied clips read wetter than desired; dry the transient and reduce
  the wet/squelchy body while keeping the impact punch.
- `gun/pistol_fire`: the tail drops too abruptly; make the falloff more gradual, ideally with a
  longer echoing tail that eases toward silence.
