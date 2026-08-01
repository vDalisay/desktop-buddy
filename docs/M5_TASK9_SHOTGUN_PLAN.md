# M5 Task 9 — Shotgun Plan

**Status: IMPLEMENTED 2026-07-31 (Tasks A–D), awaiting the Task E owner feel gate.**
Measured numbers are recorded in §2.3; the catalogue entry stays `Visible = false` until the
owner plays it. Original plan text below is unchanged except where marked MEASURED.

Refines the master plan's Task 9 stub to handoff fidelity. The gun platform was built to make this slice cheap —
"the Shotgun is a `.tres` plus a content ID, not new input code"
(`CursorGunComponent` doc comment) — and this plan holds it to that: the only real
engineering is the **shared-shot interaction id** (§2.2) and the mesh. Authoritative
contracts: RAGDOLL §9.1/§9.2 Shotgun row, §7.1–7.2 dedup, the gun plan
(`M5_TASK5_GUN_FEEL_AND_REAL_PISTOL_PLAN.md`) for every platform idiom.

**Prerequisites:** gun Tasks F/H (aim co-tuning + promotion) **should land first** —
the shotgun's profile copies the accepted aim constants, and authoring provisional
ones only to re-author them a week later is churn. If F/H stall, start anyway with
current values and note the copy is provisional (they are data either way).

---

## 1. What exists today (do not rediscover)

- **Identity minted (M5 Task 0):** `ToolId.Shotgun`, `tool.shotgun`, category Damage,
  `data/catalogue/tool_shotgun.tres` (100 credits provisional, order 13,
  `Visible = false`).
- **`GunMachine` already speaks shotgun:** `GunConstants.ProjectilesPerShot` exists,
  `GunResult.Projectiles` reports it, and the full cadence/reload/dry-fire/auto-reload
  rule set is profile-agnostic. **Zero domain changes.** The unit work is a profile
  *table* (capacity 5, interval 108, reload 240, pellets 6), not new machine states.
- **Spread is already implemented and deterministic:** `CursorGunComponent` fans
  `ProjectilesPerShot` across `SpreadHalfAngleDegrees` by index fraction
  (`forward.Rotated(fraction × halfAngle)`) — an even fan, no randomness.
  `GunProfile.Validate` already *requires* a positive spread when pellets > 1.
- **`ProjectileBody`** — pooled, CCD, layer-disciplined, `InteractionId` re-minted per
  launch. The re-mint is the one platform behavior this slice changes (§2.2).
- **Dedup (`ImpactRouter`)** — episode key `(SourceInteractionId, TargetPartId)`;
  first valid contact in an episode scores, continuations are suppressed.
- **Punctuation lanes (gun Task G):** `CameraKickComponent` (per-profile amplitude,
  non-stacking), muzzle flash, presentation recoil, pooled cosmetic
  `MagazineBody` drop — all authored per `GunProfile`, all reusable as data.
- **Mesh precedent:** `GunMeshBuilder` with `Visual3DKind` selecting the silhouette
  (Nerf, Pistol so far), envelope-bounds proof, positive-lighting-basis rule from the
  left-facing fix, exact-muzzle check. The shotgun adds a kind, not a builder.
- **Magazine persistence rule:** per-gun session magazine (put away mid-reload,
  resume on redraw) — inherited, worth one scenario check because reload is 2 s here.
- **Lab grant list** in `BuddyLab` (development catalogue) — add `ToolShotgun`.

## 2. Design

### 2.1 The profile — `data/tools/gun_shotgun.tres` (all data, validated)

| Field | Value | Note |
|---|---|---|
| `MagazineCapacity` | 5 | §9.2 |
| `ShotIntervalTicks` | 108 | 0.9 s |
| `ReloadTicks` | 240 | 2.0 s |
| `ProjectilesPerShot` | 6 | §9.2 |
| `SpreadHalfAngleDegrees` | 5.0 | provisional — tuned so the fan covers roughly a buddy torso at mid-room |
| `MuzzleSpeed` | 2200 | provisional; pistol is 2400 |
| `ProjectileRadius` / `Mass` | 1.6 / 0.12 | per pellet; pain target §2.3 |
| `ProjectileGravityScale` | 0.0 | hitscan-feel like the pistol |
| `ProjectileLifetimeTicks` / `MaxTravelPx` | 120 / 3000 | pistol values |
| `PoolCapacity` | 24 | worst case in flight: 2 bursts × 6 ≪ 24 |
| Aim constants block | copy accepted gun-Task-F values | data |
| `FireShakeAmplitudePx` / decay | 3.0 / 10 | between pistol (1.5/8) and grenade (4/14) |
| `MuzzleFlashTicks` / recoil | 3 / bigger `RecoilKickPx = 5` | reads "shotgun" |
| `DropsMagazineOnReload` | true | reused as an ejected shell, §3 default 3 |
| `Visual3DKind` | next free kind | §2.4 |
| Colors | gunmetal + walnut-brown accent | authored |

Selection key: assign at implementation from the free map (`G/B/K/J/F/T/N` + suggested
`H` for the sprayer are taken; suggest `L`). Lab boot grant added alongside.

### 2.2 Shared-shot interaction id — the one platform change

Today each `ProjectileBody` launch mints its own `InteractionId`, so six simultaneous
pellets on one part would open six episodes and score six times. The master plan says
the opposite is intended: *"dedup rules mean simultaneous pellets on one part form one
contact episode."* So: **one trigger pull = one interaction id**, stamped by
`CursorGunComponent` onto every pellet of that shot (the launch API takes an optional
shared id; the single-projectile path — pistol, nerf — re-mints per launch exactly as
today, byte-identical behavior).

Consequences, stated so nobody is surprised at the lab bench:

- Six pellets into **one** part = **one** accepted impact (the first arrival opens the
  episode; the rest are continuations). Point-blank single-part damage is *one pellet's
  worth*, not six.
- The shotgun's damage therefore lives in **coverage**: pellets spread across N parts
  open N episodes and score N times. Mid-range against a spread-eagled buddy out-damages
  point-blank against a fingertip — which is a defensible, even good, shotgun feel, and
  it is what the spec's dedup rules + the master plan's warning add up to.
- The scenario **asserts the actual accepted-event count** per the master plan — a
  point-blank one-part check pinning 1, and a mid-range multi-part check pinning the
  measured per-part count — never assuming 6.

Two neighbour proofs ride along: pellet impulse tuning (§2.3) must keep a single
pellet's solid hit **below** a pistol bullet's (it is 1/6th of a burst), and
`pistol_fire`/`nerf_versus_pistol` must stay green to prove the single-projectile
id path didn't move.

### 2.3 Pain target (empirical, measured at Task B and recorded here)

**MEASURED 2026-07-31 (implementation).** Final authored values: `MuzzleSpeed 2200`,
`ProjectileMass 0.20`, `ProjectileRadius 1.6`, `ContactSettleTicks 4`, `PoolCapacity 36`.
Through the shared curve only, seeds 1/7/13 of `shotgun_spread`:

| Measurement | Target | Measured |
|---|---|---|
| One solid pellet | 6–9 pain | **7.2 / 9.1 / 7.2** (impulse 483–510) |
| Pistol solid bullet, same run | 12.8–14.4 | **13.8 / 13.8 / 13.9** |
| Best multi-part burst | 30–50 over 4–6 parts | **9.0 / 25.7 / 26.0** over **2** parts |
| Point-blank CCD | never tunnels | green, all 6 pellets, all seeds |

Two deviations from the plan's guesses, both forced by measurement rather than taste:

- **`ContactSettleTicks` 2 → 4.** At the pistol's `2`, a pellet that had connected was taken
  out of the world before the solver resolved the real impulse, so a burst the player watched
  land delivered *zero*. Point-blank shots happened to survive it and everything past arm's
  length did not. This is a correctness value now; lowering it needs the coverage leg
  re-measured.
- **`ProjectileMass` 0.12 → 0.20.** At `0.12` a solid pellet scored `0.3–1.6` pain — under, or
  barely over, the curve's `350` impulse floor — which made the whole coverage model
  unobservable. The prior "muzzle speed, not mass, is the pain lever" finding holds for a
  *bullet-sized* projectile; at a pellet's size mass moves the reported impulse substantially.

**Coverage is 2 parts, not 4–6.** A `5°` half-angle fan at the range the room allows
(~150–180 px of flight) opens to roughly `±15 px`, which straddles two parts — typically the
near hand plus the head or torso — rather than a whole spread-eagled buddy. The rule the plan
cares about is pinned (`N` covered parts → exactly `N` accepted impacts, never two on one
part, checked on every landed burst); the *count* is reported, not assumed. If the owner wants
the plan's 30–50 burst, the dial is `SpreadHalfAngleDegrees`, and that is a feel-gate decision
rather than one the implementation should take.


Through the shared curve only — no per-tool anything. Tune pellet mass/speed so:

- one solid pellet ≈ **6–9** pain (pistol solid bullet: 12.8–14.4);
- a mid-range burst catching 4–6 parts ≈ **30–50** total — a strong hit, no KO;
- two fast consecutive full-coverage bursts can cross the 100-pain window — the KO
  path exists but demands both barrels' worth of commitment (§3 default 2).

CCD proof at point blank (never tunnels), same as the pistol's.

### 2.4 Presentation

- **Mesh:** new `Visual3DKind` in `GunMeshBuilder` — double-barrel-over-under or
  pump silhouette (implementation's pick), longer than the pistol
  (`VisualLengthPx ≈ 72`), walnut accent color block, every vertex in the envelope,
  positive lighting basis on both facings (the permanent scenario check covers this),
  exact muzzle at `MuzzleTipFraction`. Legacy 2D draws the same silhouette flat.
- **Six tracer streaks** fall out of six pooled projectiles with authored
  colors — no new tracer code.
- **Kick/flash/recoil:** authored per §2.1, all on existing lanes, non-stacking by
  construction.
- **Shell eject:** `DropsMagazineOnReload = true` reuses the pooled cosmetic
  `MagazineBody` (layer 0, `RoomBounds` mask, never a loose object, fades and
  re-pools). It draws as a small red shell rather than a magazine — a color/size
  authoring on the existing body if the visual supports it cheaply; if it demands a
  new mesh, ship the magazine visual and flag it (§3 default 3).
- **Audio:** the gun platform's existing shot/dry/reload cues at shotgun pitch —
  follow whatever the pistol authored; synthesized clean-room PCM idiom.

## 3. Owner gate — **ACCEPTED in full (owner, 2026-07-31)**, pre-implementation

All three defaults below are owner decisions — including the coverage-based damage
model of default 2, which is now the recorded dedup interpretation. Record them in
`DECISIONS.md` at Task E's bookkeeping.

1. **The pellet fan is even and deterministic**, not random scatter. Replayed seeds
   reproduce shots exactly, and the platform's fan already works this way.
   (Alternative: seeded per-shot jitter from the simulation's random source.)
2. **One shot into one part scores once** (§2.2). Damage comes from coverage;
   a KO needs two committed bursts. (Alternative: per-pellet ids — six-fold
   point-blank damage and a one-shot KO weapon.)
3. **Reload ejects a cosmetic shell** on the magazine-drop lane (visual reuse,
   possibly magazine-shaped at first). (Alternative: author it off.)

## 4. Implementation tasks (in order, each gated)

**Task A — DONE. Profile + unit table.** §2.1 `.tres`, selection key, lab grant, and the
`GunMachine` profile table: full cadence/reload/auto-reload/dry-fire state walk at
capacity 5 / 108 / 240 / 6 in ticks.
*Accept:* unit rows green; domain baseline moves by exactly the new rows; the gun
selection cycles Pistol → Nerf → Shotgun without disturbing per-gun magazines
(session-persistence check).

**Task B — DONE. Shared shot id + pellet tuning.** §2.2 seam + §2.3 measurement; numbers
recorded **here**.
*Accept:* `six_pellets_leave_on_one_press` (pool probe, fan angles match the index
formula), `point_blank_one_part_scores_exactly_once`,
`mid_range_burst_scores_per_covered_part` (measured count pinned),
`single_pellet_pain_sits_below_a_pistol_bullet` (band recorded),
`point_blank_pellets_never_tunnel` (CCD), `pistol_and_nerf_id_path_unmoved`
(`pistol_fire`, `nerf_versus_pistol` green). Seeds 1/7/13.

**Task C — DONE. Presentation.** §2.4 complete, both modes.
*Accept:* mesh envelope + positive-basis + exact-muzzle checks for the new kind;
`shotgun_kick_reads_bigger_than_pistol_and_never_stacks`;
`shell_ejects_on_reload_and_cannot_touch_the_buddy` (mask + registry probes,
magazine-lane rules verbatim); existing presentation regressions green.

**Task D — DONE. Scenario + journey + registration.** `shotgun_spread` (master-plan floor:
6 pellets per press, cadence/reload honored, multi-part per-part attribution with the
actual count asserted, point-blank no tunneling) + `m5_shotgun` journey (catalogue leg
per current visibility — grenade precedent — select by key, real pointer aim, fire,
`R` reload, dry-fire auto-reload leg as the secondary path). Register in
`ScenarioCatalog`, `TEST_PLAN.md`, quick suite (+2 steps).
*Accept:* journey green seeds 1/7, both presentations.

**Task E — OPEN (owner). Feel gate + bookkeeping.** Owner plays it. DECISIONS entry (the three §3
defaults, the shared-shot-id rule as the recorded dedup interpretation),
`CHECKLIST.md`, TEST_PLAN docs, full sweep recorded here. `Visible = true` only on
the owner's word.

## 5. Validation commands

The standard three: build + domain suite, quick scenario suite, targeted runs
(`shotgun_spread`, `m5_shotgun`, plus `pistol_fire`, `nerf_versus_pistol`,
`gun_visuals`, `pistol_punctuation`, `impact_dedup` as neighbours) across seeds
1/7/13 and both presentation modes. Any baseline movement stated in the commit
message, never silently absorbed.
