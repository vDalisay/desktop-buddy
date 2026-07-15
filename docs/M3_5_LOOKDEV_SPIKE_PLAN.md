# M3.5 Look-Dev Spike — "Miitopia-style" Toon Presentation (owner decision aid)

Status: **built; Variant C owner-accepted 2026-07-15**. This spike exists so the owner
can judge, from real in-engine renders, whether the 3D presentation can reach their
visual target before deciding at the paused Task 8 gate
(`docs/M3_5_3D_PRESENTATION_PLAN.md`) to either adopt `Mii3D` or stay with the 2D
presentation. The accepted result is productionized by
`docs/M3_5_MATERIALS_AND_LOOK_PLAN.md`; the spike remains development-only.

## Owner direction being tested (2026-07-14)

- Target look: **Miitopia-like soft toon** — gentle light falloff on rounded shapes,
  matte, friendly; *not* hard two-band cel shading and *not* the current flat
  Unshaded parity look.
- Facing: the owner corrected the earlier "always frontal (90°)" choice — characters
  should read at roughly a **60° three-quarter facing** (≈30° yaw off dead-frontal).
  The spike shows this statically; making it dynamic is M3.6 scope.
- Decision this serves: "go 3D or stay 2D." The spike must make the comparison honest:
  current look vs. achievable look, same proportions, same palette, over both a calm
  and a busy backdrop.

**Clean-room rule (hard):** "Mii-inspired" means proportions and simplicity only.
No Nintendo assets, face textures, hair, outfits, UI, or trade dress may be copied or
approximated from reference screenshots. Faces in this spike are generic dot-eyes plus
a simple mouth, built from primitives, in this project's own palette.

## What to build

One **development-only** scene: `scenes/spike_miitopia_look.tscn`.
Prefer a pure `.tscn` with built-in resources (no C# script → no build step). A small
`.gdshader` file beside it is acceptable if standard materials prove insufficient
(see Material ladder). Follow the export-exclusion convention of the existing
`scenes/spike_transparent_window.tscn`. Touch **no** shipping scene, script, profile,
or `.tres`. Dev-only Label3D captions are fine (the no-new-player-facing-text rule
governs shipped code, not spikes).

### Scene composition

- Orthographic `Camera3D`: `projection = 1`, `keep_aspect = 1` (Height),
  `size ≈ 340`, position `(0, 0, 500)` looking −Z, matching the M3.5 camera
  conventions (`src/Presentation3D/WorldPlaneMapping.cs` doc comment). Project window
  is 480×360; three buddies at x = −150 / 0 / +150 fit the ~453 px horizontal view.
- **No `WorldEnvironment`.** The shipped overlay forbids it (Task 1 outcome —
  transparent shell), so the spike must fake ambient the same way the real
  implementation would: a two-light rig.
  - Key `DirectionalLight3D`: pitch ≈ −35°, yaw ≈ −30°, energy ≈ 1.0, warm white
    `(1.0, 0.98, 0.94)`, shadows off initially (try one screenshot with contact
    shadows on; keep whichever reads softer).
  - Fill `DirectionalLight3D` along the camera axis: energy ≈ 0.3, slightly cool
    `(0.85, 0.9, 1.0)`.
- Backdrop (unshaded, behind the buddies at z ≈ −200): left two-thirds a light
  neutral panel; right third a deliberately busy panel (several overlapping
  saturated unshaded boxes) so outline/readability can be judged against a
  desktop-like mess.

### The three buddies (shared geometry)

Proportions come from the live rig (`data/buddy/lab_puppet_rig.tres`,
`scenes/buddy/puppet.tscn`); positions below are already Y-flipped into 3D (Y-up):

| Part | Mesh | Radius | Local position (3D) |
| --- | --- | --- | --- |
| Head | SphereMesh | 24 | (0, +50, lane) |
| Torso | CapsuleMesh | 28, height 70 (= 28 × 2.5 `CapsuleHeightScale`) | (0, 0, lane) |
| Hands L/R | SphereMesh | 15 | (∓38, +5, lane) |
| Feet L/R | SphereMesh | 17 | (∓22, −55, lane) |
| Connectors ×5 | CapsuleMesh | r 5 (head/hands), r 6 (feet) | torso→each, oriented along the offset |

Palette (from `data/buddy/lab_buddy_visual.tres`): head `(0.478, 0.78, 1)`, torso
`(0.27, 0.64, 0.88)`, hands `(0.56, 0.83, 1)`, feet `(0.38, 0.72, 0.94)`, connectors
`(0.27, 0.64, 0.88)`. Outline/face ink: `#183042`.

**Depth lanes, spike-local:** use *small* per-part Z offsets (head +30, hands +20,
torso 0, feet −20, connectors −10), not the shipped 96/48/0/−48 lanes. Reason: each
buddy sits under one yawable root, and under a 30° yaw a 96-unit lane displaces the
head ~48 px sideways on screen. This is a real design note for M3.6 (yaw must be
applied to sockets *before* camera-aligned lane offsets, or lanes shrunk); record it
in the outcome.

**Dot face (variants B/C, children of the head so they yaw with it):** two dark
spheres r ≈ 2.5 at approximately (±8, +4, +21) relative to head center (nudge onto
the sphere surface by screenshot iteration) and one horizontal dark capsule r ≈ 1.5,
length ≈ 8, at (0, −6, +22). Generic and friendly; nothing copied.

### Variant A — "current" (control)

Exactly today's `Mii3D` output: all Unshaded `StandardMaterial3D`, dead-frontal,
no dot face; a `Label3D` face `":|"` in `#183042`, font size 14, at
z = head radius + 0.1, with rotation Z = −π/2 (the sideways-ASCII quarter turn as the
presenter renders it — see `BuddyVisualPresenter.UpdateFace`).

### Variant B — soft toon, still frontal

Same pose as A, lit materials per the Material ladder, dot face instead of the label.
Isolates "what does shading alone buy."

### Variant C — the vision

Soft toon + dot face + the whole buddy root yawed **30° around Y** (the owner's ~60°
facing) + an **inverted-hull outline** on head/torso/hands/feet: duplicate
`MeshInstance3D` per part, same mesh, `StandardMaterial3D` with
`shading_mode = Unshaded`, `cull_mode = Front`, `grow = true`, `grow_amount ≈ 1.5`,
albedo `#183042`. If the outline fights the soft look, capture C both with and
without it — outline acceptance is explicitly part of the owner decision (the 2D
build has 2 px outlines).

### Material ladder (stop at the first rung that looks right)

1. `StandardMaterial3D` with `diffuse_mode = LambertWrap`, `specular_mode = Toon`
   with low specular, `roughness = 1`. Lambert-wrap's wraparound is the closest
   built-in to Miitopia's soft falloff and cannot go pitch-black without ambient.
2. `diffuse_mode = Toon` if wrap reads too flat — but expect it to be too harsh.
3. A custom spatial `.gdshader` (half-Lambert: `ndl * 0.5 + 0.5` pushed through a
   wide `smoothstep` band, shade floor ≈ 0.75 of albedo, optional ~0.15 rim):
   full control, no environment dependency. **Caveat to verify on this project's
   pinned Godot 4.6.1:** the renderer is `gl_compatibility` (`project.godot`);
   confirm the custom `light()` function path renders there, and record it — this
   caveat decides how the real materials task gets written.

## Verification / iteration loop

Per `AGENTS.md`, drive it through the Godot MCP server: run the project with this
scene (`resolve_godot.bat` conventions; `<godot> --path . res://scenes/spike_miitopia_look.tscn`
works directly since there is no C# in the spike; run a headless `--import` first if
the editor cache complains), screenshot, adjust light angles / band softness / grow
amount / eye placement, repeat. Live-tuning via MCP property/shader-param calls is
fine; persist final values back into the `.tscn`.

## Deliverable

1. Final screenshots (minimum: full A/B/C row over both backdrop halves at 480×360,
   plus one close crop of C) saved under `artifacts/m35_lookdev/` (untracked).
2. A short outcome section appended to this document: which material rung was used,
   whether `gl_compatibility` supported it, the lane-vs-yaw note, shadows on/off
   choice, and any color-parity drift observed between the lit 3D palette and the
   2D colors (lit materials will *intentionally* diverge — the exact-parity
   constraint dies the moment the owner picks a shaded look; say so explicitly).
3. Present the screenshots to the owner with the decision framing below. Do not
   amend `M3_5_3D_PRESENTATION_PLAN.md`, `M3_6_EXPRESSIVE_PRESENTATION_PLAN.md`, or
   `DECISIONS.md` — those amendments happen after the owner decides.

## Decision framing for the owner (include with the screenshots)

- **Adopt (looks right / close):** Task 8 stays paused; a new "materials & look"
  task gets written (toon materials + outlines + the lane/yaw fix), M3.6 adopts the
  ~60° facing target, and the exact-color-parity exit-gate wording is amended.
- **Reject (stay 2D):** record at the Task 8 gate that `LegacyCircles` remains the
  default; the presenter and seams stay (they cost nothing at runtime when hidden
  is the wrong claim — they cost a hidden per-frame mirror; if 2D is final, a
  follow-up should gate that off) and M3.6 is re-scoped or shelved.
- What this spike deliberately cannot show: hair/outfits/limbs (character-editor
  milestone, real meshes), the composed animated face and walk/turn life (M3.6).
  Judge shading, silhouette, facing, and outline only.

## Out of scope (hard)

- Any edit to shipping scenes, scripts, profiles, or resources.
- Perspective cameras (orthographic parity is an M3.5 invariant).
- Copying any Nintendo asset or likeness (clean-room rule above).
- Committing screenshots or `artifacts/` content.

## Outcome (2026-07-14)

The spike stopped at **material ladder rung 1**. Godot 4.6.1's built-in
`StandardMaterial3D` Lambert-wrap diffuse mode, toon specular mode with low specular,
and full roughness produced the requested smooth matte falloff under the two-light rig.
It rendered correctly on this project's `gl_compatibility` renderer (OpenGL 3.3,
NVIDIA RTX A2000 Laptop GPU), so no custom shader or custom `light()` path was needed
or exercised.

The retained key/fill setup uses energy `0.75`/`0.70`, warm/cool colors, and **shadows
off**. The required shadow-on comparison introduced noticeably harder self-shadow
boundaries and higher contrast around the head/torso overlap, moving away from the
friendly soft target. The unshaded inverted-hull outline at grow amount `1.5` remained
clean under Compatibility and materially improved Variant C's silhouette over the busy
panel without overwhelming the soft shading.

The spike confirms the lane/yaw design warning: even the reduced local head lane of
`+30` moves the head about 15 world/screen units sideways when the whole buddy root is
yawed 30 degrees. A production yaw implementation must apply yaw to the body sockets
before adding camera-aligned depth lanes, or shrink/remove those lanes; rotating today's
lane-bearing hierarchy directly is not acceptable.

Variant A retains the source palette exactly because it is unshaded. Variants B/C
intentionally do not: highlights become paler/cooler and shaded sides become deeper
blue while retaining the palette's hue family. If the owner adopts the shaded look,
exact per-pixel 2D/3D color parity must be replaced with an art-directed palette-range
criterion.

Interactive evidence was captured at 480x360 under `artifacts/m35_lookdev/` (untracked):
`abc_full_480x360.png` shows the complete A/B/C row across neutral and busy backdrops;
`c_vision_close_480x360.png` is the close Variant C silhouette/face/outline inspection.

**Owner decision (2026-07-15): ADOPT VARIANT C.** The production direction and its
scope boundaries are recorded in `docs/DECISIONS.md`. Task 8 stays paused until the
accepted materials, lighting, outline, and camera-space lane behavior are implemented
and pass the real-game gate.
