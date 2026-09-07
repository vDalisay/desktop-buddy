# Desktop Buddy — Creator Studio Implementation Work Packets

Status: **implementation planning; no packet implemented by this document**
Recorded: 2026-09-07
Research baseline: `c286b1d9d6bfd8274238bd0ac5bc30cf3a36b15f`
Engineering review: `c9ff1b80`
Primary design: [Creator Studio research and plan](IN_GAME_CREATOR_STUDIO_RESEARCH_AND_PLAN_2026-09-07.md), especially section 22.

## 1. How to assign and execute this plan

Assign one `CS-xx` packet at a time. A packet is a bounded implementation/review unit, not a promise of a particular number of hours. Every packet starts **Not started**. Dependencies mean verified functionality on the implementation branch, not merely a document describing it. Do not implement a missing systemic subsystem inside a Creator UI packet.

Before editing code, read the current `AGENTS.md` source-of-truth list in its stated order. Then read this plan, the assigned packet, and its referenced research sections. In particular preserve the M6 package boundary, painting contracts, three cumulative build surfaces, and physics contract. This plan does not independently authorize new package formats, product limits, extra paint tools, multiple test Buddies, or executable mods. Apply higher-priority owner decisions where they resolve historical deferrals; otherwise record the exact unresolved decision and stop only the dependent work.

At packet start:

1. Record HEAD, branch, clean/dirty status and predecessor commit/verdicts. Preserve unrelated changes.
2. Locate the named seams with `rg`; read implementations and callers. Paths below are navigation starting points at the review baseline, not instructions to recreate renamed classes.
3. Write a short checklist from the packet's deliverables and tests. Reuse existing types and helpers before adding new ones.
4. Implement the smallest complete vertical change. Add tests with behavior, including relevant failure paths. Avoid new frameworks, general service locators, reflection, and speculative graph/runtime code.
5. Run the verification lane in section 6 plus the packet checks. Player-visible changes require real-input Godot MCP verification and committed automation.
6. Deliver the handoff in section 7. Update the packet ledger only with evidence; “code written” is not “passed.”

The suggested new directories below are **proposed ownership locations**, not existing APIs or mandatory scaffolding. Prefer the equivalent directory created by an upstream MOD/SCENE packet. Create only files consumed by the current packet.

## 2. Existing seams and upstream prerequisites

### Existing code to inspect

| Concern | Existing navigation seam | Reuse boundary |
| --- | --- | --- |
| Editor lifecycle | `src/App/CharacterEditorModeCoordinator.cs`, `src/App/GameplayPauseCoordinator.cs` | Window snapshot/recovery and pause reasons; do not inherit Buddy document behavior. |
| Shell isolation | `src/Platform/DesktopShellController.EditorIsolation.cs` | Editor resize/input restoration; no second window-placement policy. |
| Working copy | `src/CharacterEditor/CharacterEditorSession.cs` | Learn baseline/dirty/unsaved-action semantics; do not inject its economy or Buddy selection dependencies into Creator Test. |
| Raster CPU data | `domain/DesktopBuddy.Domain/Painting/PaintSurface.cs`, `PaintTypes.cs`, `PaintWorkspace.cs`, `PaintAlgorithms.cs` | Reuse kernels/history where compatible; current dimensions and UV seam behavior are Buddy-specific. |
| Texture uploads | `src/CharacterEditor/PaintTextureBridge.cs` | Revision coalescing and reusable scratch pattern; its rig binding is not the item renderer. |
| PNG/persistence | `src/Persistence/Characters/PaintPngCodec.cs`, `CharacterPaintStore.cs` | Strict image checks and transaction patterns; preserve existing fixed Buddy whitelist. |
| Platform staging | `src/Persistence/Sharing/WorkshopStagingStore.cs`, `WorkshopProvenanceStore.cs` | Immutable staging and provenance; keep package policy separate. |
| Existing package schema | `domain/DesktopBuddy.Domain/Sharing/ShareModels.cs`, `ShareManifestPolicy.cs` | Regression boundary for Buddy/Room, not the new Content Pack DTO. |
| Dynamic objects | `src/Objects/LooseObjectRegistry.cs` | Understand existing capacity/ownership; new systemic placed-object quotas must use the upstream systemic owner. |
| Projectiles | `src/Tools/ProjectileBody.cs`, `FastProjectileGunProfile.cs` | Trace damage, CCD, lifetime, attribution and cleanup before exposing a gun template. |
| Tests | `tests/DesktopBuddy.Domain.Tests`, `src/Testing/CharacterEditorScenarioRegistration.cs`, `CharacterEditorModeScenarios.cs` | Existing managed assertions and scenario registration conventions. |

### Prerequisite contracts

These labels are local shorthand for evidence to collect, not new parallel implementations.

| Label | Required upstream behavior | Source |
| --- | --- | --- |
| U-ID | Provider-qualified identity, capability exposure metadata and engine-free definitions with a real first-party consumer | MOD-0 and MOD-1 in [moddability supplement](MODDABILITY_AND_WORKSHOP_SECURITY_SOURCE_ALIGNMENT_2026-09-07.md). CS-02 may complete the narrow missing creator seam. |
| U-SCENE | Active Scene ownership, semantic instance IDs, save snapshots, build policy, safe command boundary and context-specific services | [Scene supplement](FULL_RELEASE_MULTI_BUDDY_SCENES_SOURCE_ALIGNMENT_2026-09-07.md), SCENE-0 through relevant SCENE-3 functionality; current three-build corrections apply. |
| U-BUILD | Selection, trusted definition spawn, constraints/wires, descriptor-based properties and whole-Scene capacity admission | [systemic implementation plan](FULL_RELEASE_SYSTEMIC_SANDBOX_IMPLEMENTATION_PLAN.md), selection/properties/construction/devices and Blueprint sections, read through [Next Fest scope](NEXT_FEST_THREE_BUILD_SCOPE_SOURCE_ALIGNMENT_2026-09-07.md). |
| U-WEAPON | Trusted reusable melee/projectile capabilities, attribution and reward policy independent of exact content ID | MOD-1 and systemic properties/weapons work. Existing tool classes alone are not proof this seam exists. |
| U-SIGNAL | Typed ports and bounded fixed-tick signal delivery consumed by a real device | Systemic devices/signal work and MOD event/action contract. |
| U-WORKSHOP | Existing optional GodotSteam adapter, publish callback lane, immutable import and offline tests pass | [M6 supplement](M6_WORKSHOP_SOURCE_ALIGNMENT_2026-08-25.md). |

### Decision gates to resolve before dependent code

CS-01 creates a recorded decision table with source, owner/tuning authority, chosen value, evidence and status. Unresolved means **Blocked on that decision**, not permission to use a screenshot's sample number.

| Gate | Required decision/output | Blocks |
| --- | --- | --- |
| D-PHYS | Approved/tuned primitive dimensions, mass/material/speed/impulse combinations, marker units/axes and collision policy for each enabled template | CS-03 and template exposure in CS-16. |
| D-PAINT | Item dimensions, bounded layer count, permitted source tools/compositing, history policy, source/decoded/GPU caps and total application residency | CS-10/11; no change to locked Buddy budgets. |
| D-TEST | Evidence-backed isolated test-world activation, pause and lifecycle design at Godot 4.6.1 | CS-13, resolved by CS-04 spike. |
| D-SOURCE | Exact local source schema, schema version, storage root/whitelist, save/recovery, immutable local runtime snapshot format/retention and dependency lock representation | CS-09/17/18. Reuse upstream policy where already specified. |
| D-GRAPH | Initial allowed event/action vocabulary, scheduling/overflow rules and measured global/per-pack/instance limits | CS-20/21; no general expression language. |
| D-PACK | Separate runtime Content Pack schema/version, exact declared-path grammar, count/byte caps, tag/build policy and explicit M6-extension authority | CS-23/24. Local pack compiler does not authorize Workshop admission. |

## 3. Delivery order and milestones

| Research milestone | Work packets | Completion result |
| --- | --- | --- |
| CREATOR-0 | CS-01–04 | Requirements mapped, shared semantic seam usable, physics policy tested, isolated-test feasibility proven. |
| CREATOR-1 | CS-05–07 | Local Blueprint save/place/library journey. This is the required creator value for the systemic slice. |
| CREATOR-2 | CS-08–17 | Local painted Prop, then approved Melee/Gun templates; safe one-click test and local use. Optional for Next Fest. |
| CREATOR-3 | CS-18–19 | In-game multi-item pack authoring, deterministic compilation and resilient dependency versions. |
| CREATOR-4 | CS-20–22 | Bounded graph runtime and visual authoring. Off the Next Fest critical path. |
| CREATOR-5 | CS-23–25, then CS-26 | Separate hostile package pipeline and publish/import/update; CS-26 also closes earlier local-only slices independently. |
| CREATOR-6 | CS-27 | Individually gated expansions; no blanket implementation authorization. |

Recommended sequence is ascending packet number. Dependencies inside each packet are authoritative. Blueprint work does not depend on the optional item editor or the isolated-test spike succeeding. Do not turn the foundation into a graph framework just to reserve future extension points.

Run CS-26 whenever an advertised slice is ready, using only that slice's dependencies. Its packet number does not require completing optional graph or Content Pack Workshop work before accepting local features.

## 4. Detailed work packets

### CS-01 — Reconcile implementation baseline and record decisions

**Depends on:** none. **Lane:** documentation/read-only audit. **Read:** research sections 14–16 and 22; all prerequisite sources in section 2.

**Deliver:** a baseline/evidence table and D-* decision records under this plan's ledger (section 8). Resolve existing decisions from sources first. Mark every U-* contract Present, Partial or Missing with file/symbol/commit evidence. Inventory current schema versions and scenario runner commands. Record reference hardware and existing paint/physics metrics. Identify the exact active-build policy instead of assuming historical export flag names.

**Steps:** inspect current checkout and upstream merges; trace one existing spawn, save, paint upload and editor close; map packet owners to actual files; distinguish product decisions from measured tuning work. Resolve ambiguous policies through the owner only where sources do not already resolve them.

**Check/exit:** every later packet has a verified dependency or an explicit missing prerequisite; no invented production limits; known M6 formats are unchanged. **Exclude:** implementation, global renames and prerequisite subsystem construction.

### CS-02 — Compile a local definition through the shared semantic seam

**Depends on:** CS-01, U-ID. **Lane:** domain + headless. **Read:** research 3, 10–11, 18, 22.1/22.5.

**Touch:** upstream domain definition/registry directory; proposed `domain/DesktopBuddy.Domain/Creator` only for authoring-specific DTOs; matching domain tests.

**Deliver:** one local Prop draft compiled to the same immutable runtime definition as a first-party Prop; typed diagnostics include stable code, source field/definition ID and severity. PackId and definition ID stay separate from paths/Steam provenance. Capability metadata drives validation and later UI.

**Steps:** reuse IDs/exposure descriptors; validate finite/range/type/build constraints; reject unknown and CoreOnly capabilities for local authoring; compile references once; inject the local provider explicitly into the existing factory. No ID-specific switch for the sample prop.

**Checks:** valid prop parity; malformed ID/core impersonation; duplicate definition; NaN/infinity; unsupported capability/build; input mutation after compile cannot alter compiled output; forbidden Godot/gameplay-authority types in authoring DTOs. **Exit:** headless trusted factory accepts the local result. **Exclude:** file packages, graph DTOs and UI.

### CS-03 — Primitive collision and marker contract

**Depends on:** CS-02, D-PHYS. **Lane:** domain + physics scenario. **Read:** research 4.4–5 and 22.3.

**Touch:** upstream collision/marker descriptors and trusted factory; proposed Creator validation tests.

**Deliver:** typed primitive descriptor and marker transform shared by compiler, preview and spawn. Store measured envelopes in the existing typed tuning pattern. Begin with approved primitives; hull generation is CS-27 unless explicitly selected by D-PHYS.

**Steps:** specify origin, units, axis, rotation/mirror conversion; validate nonzero geometry and combined mass/speed/recoil envelopes; use trusted material IDs; keep visual size independent from body scale. Route collision construction through the upstream factory.

**Checks:** minimum/maximum and just-outside limits; zero/negative/nonfinite dimensions; mirrored muzzle alignment; extreme legal mass ratios; thin/fast projectile case; identical Buddy geometry and forces before/after appearance-only change. **Exit:** measured stability and descriptor preview parity. **Exclude:** new solver, Buddy shape editing, arbitrary polygons.

### CS-04 — Prove isolated Creator Test physics

**Depends on:** CS-01, U-SCENE; a trusted built-in prop suffices. **Lane:** bounded Godot spike + scenario. **Read:** research 22.2.

**Touch:** proposed `src/Creator/CreatorTestHost.cs`, existing pause/shell seams only where necessary, `src/Testing`.

**Deliver:** a minimal test fixture demonstrating a separate World2D advancing at 120 Hz while the production Scene stays paused. Test-owned targets/services have no live progress reference. Document activation/teardown sequence as D-TEST evidence.

**Steps:** inject test registry/clock/RNG/effect policy; verify actual physics advancement, not merely node callbacks; pause on suspend/overlay according to existing lifecycle; retire callbacks before freeing world. Use inert targets, no additional Buddy.

**Checks:** test prop falls; production body transforms/domain tick count do not advance; no reward/stat/save mutation; overlay/suspend prevents inappropriate stepping; reset/exit releases bodies and handlers. **Exit:** promote successful fixture into a scenario; remove throwaway alternatives. If engine activation conflicts with application pause, report the failing minimal case and block CS-13. **Exclude:** unpausing the entire tree as a workaround.

### CS-05 — Blueprint capture and document validation

**Depends on:** CS-02, U-SCENE, U-BUILD. **Lane:** domain. **Read:** research 8, 12 and 22.5/22.6; upstream Blueprint format.

**Touch:** upstream Blueprint DTO/compiler; proposed tests in the matching domain directory.

**Deliver:** selection snapshot to a versioned Blueprint using semantic instance/definition IDs, relative placement, approved overrides and internal constraints/wires. Reuse the upstream format; do not add a second Blueprint schema.

**Steps:** capture at the owning command boundary; remap selected instance IDs; diagnose references crossing the selection boundary using the approved policy; record resolved dependency hashes; validate counts and payload size before materialization. Preserve unsupported bounded references as inert data.

**Checks:** one entity, connected selection, duplicate, empty selection, dangling wire, missing definition, future version, excessive count/depth, stable roundtrip and no saved transient velocity unless upstream explicitly permits it. **Exit:** captured Blueprint validates independently of the live scene. **Exclude:** UI and automatic publish.

### CS-06 — Atomic Blueprint placement

**Depends on:** CS-05. **Lane:** domain + headless physics. **Read:** research 22.6.

**Touch:** upstream Scene command/capacity owner and factory.

**Deliver:** preflight/reserve/prepare/commit placement with fresh instance IDs and remapped links. Admission includes existing Scene occupants and aggregate dependency costs. Missing-provider placeholders remain inert.

**Steps:** resolve/build-check the full graph; reserve all required capacity; prepare instances without activation; commit bodies/constraints/wires at the safe boundary; release reservations/resources on any failure. Use one explicit transaction, not one mutation per UI row.

**Checks:** successful connected placement; one remaining slot for a two-object graph rejects wholly; injected failure midway leaves counts unchanged; repeated placement gets unique IDs; Full-only indirect dependency rejects; pending scene switch cancels stale placement. **Exit:** proposed `creator_blueprint_atomic_place` scenario passes. **Exclude:** eviction-rule changes to make a Blueprint fit.

### CS-07 — Local Blueprint library and real-input journey

**Depends on:** CS-06 and upstream Blueprint store (implement its narrow missing storage work here only if its format is already approved). **Lane:** domain/store + UI journey.

**Touch:** upstream library/store, Win98 selection commands; `src/Testing` registration.

**Deliver:** Save as Blueprint, paged library, rename/duplicate/delete, generated thumbnail and explicit placement. Use upstream storage paths and recovery rules. Saving library content never activates it.

**Checks:** failed save retains source; delete affects only selected local artifact; large library loads bounded visible metadata/thumbnails; duplicate remaps IDs; restart/offline roundtrip. Promote `creator_blueprint_save_place` real-input journey. **Exit:** CREATOR-1 is usable without Creator Item UI. **Exclude:** Workshop buttons until their actual pipeline exists.

### CS-08 — Item working-copy session

**Depends on:** CS-02. **Lane:** managed tests. **Read:** research 6, 9–10 and 22.4.

**Touch:** proposed `src/Creator/CreatorEditorSession.cs`; inspect existing CharacterEditorSession semantics.

**Deliver:** working copy, saved revision, dirty state, typed edit commands, generation token and Save/Discard/Continue Editing state transitions. Session owns no Buddy selection, physics node or EconomyService.

**Checks:** new project not persisted; discard restores baseline; save of revision A while editing B leaves B dirty; failed/cancelled save leaves working copy; pending close/duplicate/new preserves exactly the requested action; stale completion ignored. **Exit:** state machine tests cover transitions before UI wiring. **Exclude:** general event sourcing/command bus and graph commands.

### CS-09 — Transactional local creator source store

**Depends on:** CS-08, D-SOURCE. **Lane:** store tests. **Read:** research 9 and 22.4.

**Touch:** proposed `src/Persistence/Creator`; reuse file transaction patterns, not Buddy filenames/schema.

**Deliver:** exact whitelisted source document/assets, atomic multi-file project save, rolling recovery policy, typed unsupported/corrupt/missing results and bounded library metadata. Copy a complete immutable snapshot for a write.

**Checks:** first save; replace; interruption at each commit stage; disk-full/denied write; backup recovery; future schema preserved; traversal/reparse/undeclared paths rejected; sequential migration; platform-correct absolute test paths. **Exit:** restart reads either old or new complete revision, never mixed assets. **Exclude:** Steam upload and active Scene saves.

### CS-10 — Flat raster kernel and bounded history

**Depends on:** CS-08, D-PAINT. **Lane:** raster domain + existing paint regressions. **Read:** research 4 and 22.1/22.6.

**Touch:** existing Painting primitives only where shared extraction is justified; proposed Creator canvas state.

**Deliver:** approved item dimensions, clipped-edge brush/eraser, revision tracking and complete-command undo/redo within the chosen history budget. Preserve Buddy wrapping and fixed 512×512 policy through its adapter.

**Steps:** isolate dimension/edge behavior at the lowest shared raster operation; keep Buddy mapping outside flat canvas; reuse dirty-rectangle before-data; evict oldest complete commands under policy; no new paint tools by implication.

**Checks:** all four edges/corners; long stroke across canvas; undo/redo roundtrip; no-op unchanged revision; history cap boundary; existing Buddy seam hashes/stroke continuity; peak scratch allocation. **Exit:** no regressions in Buddy behavior or budgets. **Exclude:** layer UI, fill/selection/stamps unless D-PAINT explicitly allows them.

### CS-11 — Source layers and deterministic flattening

**Depends on:** CS-09, CS-10, D-PAINT. **Lane:** domain/store + memory check.

**Touch:** proposed Creator layer DTO/compositor and source store.

**Deliver:** only approved bounded layer operations, visible/hidden state and fixed compositing semantics. Flatten to one runtime RGBA8 image; source layer metadata never enters runtime behavior. If layers are not approved for this slice, record that gate and do not advertise them.

**Checks:** ordering/visibility/alpha examples; save/reopen equality; flatten reproducibility; over-limit layer rejection before allocation; simultaneous source/baseline/history/flatten peak; hidden layers omitted from runtime pixels but preserved locally. **Exit:** agreed D-PAINT residency proven. **Exclude:** arbitrary blend modes or runtime layer compositor.

### CS-12 — Trusted item visual and canonical preview

**Depends on:** CS-03, CS-10. **Lane:** render scenario + visual inspection. **Read:** research 5 and 17.5.

**Touch:** upstream presentation factory; proposed painted-item visual component. Inspect PaintTextureBridge upload lifecycle.

**Deliver:** trusted textured card/thin primitive selected by measured prototype, matching editor/runtime transform and canonical preview. No imported mesh or shader path. Main-thread texture creation and revision-coalesced uploads use bounded reusable buffers.

**Checks:** transparent edges, nearest/pixel presentation if selected, front/back orientation, marker alignment, item occlusion in the actual presentation, unchanged revision zero uploads, repeated disposal returns owned counts, fallback diagnostics in headless. **Exit:** canonical preview matches compiled item. **Exclude:** silhouette extrusion and GPU readback during strokes.

### CS-13 — Transactional Apply/Test integration

**Depends on:** CS-03, CS-04, CS-08, CS-12. **Lane:** managed + Godot scenario. **Read:** research 10 and 22.2/22.4.

**Touch:** Creator session/test host and shared factory.

**Deliver:** captured source revision -> validated compiled definition -> prepared assets/instance -> safe-boundary commit. Recreate on structural/behavior changes; retain prior valid test on failure. Own old/new memory during preparation within policy.

**Checks:** newer Apply wins; close/reset during compile; failed texture/spawn allocation; capacity rejection; no mixed definition/assets; no callbacks after teardown; safe generation handles; no live progression mutation from descendants. **Exit:** proposed `creator_apply_transaction` and `creator_test_isolation` scenarios pass. **Exclude:** continuous per-keystroke live physics mutation and in-place structural hot reload.

### CS-14 — Win98 shell, General/Physics/Function/Test controls

**Depends on:** CS-09, CS-13. **Lane:** real-input journey. **Read:** research 2–3, 6 and 17.

**Touch:** proposed `src/Creator` UI composition, existing Win98 command surface and shell lifecycle seam.

**Deliver:** New Prop, name/properties, validation list, save/close handling and Test Spawn/Reset/Drop/Validate. Build controls from approved capability descriptors with visible units/ranges. Show only implemented pages/actions. Reuse window/input ownership and restoration.

**Checks:** keyboard focus, readable error association, Escape dirty-close, mouse input does not fire gameplay behind UI, compact/fullscreen restoration, monitor removal, overlay/suspend and minimum usable area. **Exit:** real-input create/configure/test/save journey uses production commands. **Exclude:** placeholder Publish or Advanced Behavior buttons.

### CS-15 — Paint page and marker editing

**Depends on:** CS-11, CS-12, CS-14. **Lane:** UI journey + memory scenario.

**Touch:** Creator canvas control/session commands; existing raster widgets only when not Buddy-specific.

**Deliver:** approved brush/eraser/color/history controls, zoom/pan/reset, layer controls and marker placement. Pointer-to-canvas/marker conversion uses CS-03 transforms. PNG import is exposed only with bounded hostile-image validation in place.

**Checks:** draw at zoomed/panned edges; marker placement roundtrip; undo after tool switch; dirty-close restores source and preview; malformed/oversized PNG; rapid strokes do not trigger hashing/encoding every frame; peak memory/upload counters. **Exit:** paint -> Test -> return -> edit -> Test works without restart. **Exclude:** extra brushes or material sliders outside approved fields.

### CS-16 — Approved Melee and Gun templates

**Depends on:** CS-15, U-WEAPON, per-template D-PHYS. **Lane:** domain + physics + UI journey.

**Touch:** trusted template/capability descriptors, existing weapon runtime adapters and Creator Function page.

**Deliver:** Melee then Gun as separately reviewable substeps. Template compiles existing trusted behavior; markers define handle/muzzle through validated rules. Projectile choice, rate/spread/recoil use approved bounds and transitive build checks. Reward-safe attribution applies to every emitted descendant and contact.

**Checks:** firing cadence and cooldown at 120 Hz; projectile lifetime/CCD; muzzle obstruction/self-hit rules; maximum legal recoil and mass combination; sleeping/removed owner cleanup; contact deduplication; no reward amplification; indirect Full-only projectile rejects. **Exit:** research beginner painted-gun journey through edit -> Test Spawn -> pick up/fire -> edit -> Test Spawn again (steps 1–10). Persistent library use and restart acceptance belong to CS-17. **Exclude:** new reload/magazine/explosive semantics absent from upstream capability.

### CS-17 — Local item library and CREATOR-2 acceptance

**Depends on:** CS-15; CS-16 for Melee/Gun acceptance; U-SCENE/U-BUILD; D-SOURCE covering immutable local runtime snapshots and retained dependency hashes. **Lane:** library + end-to-end + Windows/performance.

**Touch:** Creator library provider and upstream spawn browser. Reuse source metadata paging.

**Deliver:** saved local items in eligible build library, explicit use, duplicate/variant with new PackId and internal remapping, rename/delete policy and bounded thumbnail cache. Appearance-only variant keeps trusted physics. Deletion cannot free resources still in use.

**Before enabling Scene use:** implement or verify the narrow single-item runtime snapshot store and resolver. Persist immutable compiled definitions/assets with exact-byte hashes; Scene/Blueprint references pin the resolved hashes. Retain versions referenced by saved documents as well as live instances. Saving edited source creates a candidate revision and cannot replace a pinned runtime artifact; switching an existing instance is explicit and transactional. Missing snapshots preserve inert references, never silently resolve to the latest source. Reuse the upstream provider/store and D-SOURCE policy; broader multi-pack resolution remains CS-19. If this contract is unavailable, keep library spawning disabled.

**Checks:** local/offline restart; unavailable provider placeholder; duplicate preserves external refs; source-save failure doesn't replace runtime artifact; direct spawn enforces build policy; repeated editor/test/use cycles; Normal Demo unchanged. Save a Scene and Blueprint with item revision A, edit/save revision B, exit and reopen both: they retain A's exact definition/assets. Cover failed snapshot writes, explicit replacement failure and deletion while only a saved document references A. Run research section 22 phase gates and the complete section 20 beginner journey, including local save, restart and explicit library use, for Gun acceptance; use the equivalent Prop journey when Gun is deferred. **Exit:** accept the exact enabled template set and resource envelope; missing Gun gate does not invalidate completed Prop work. **Exclude:** dependency auto-enable or Workshop UI.

### CS-18 — Deterministic local Content Pack compiler and authoring UI

**Depends on:** CS-17, D-SOURCE and approved local runtime schema from MOD-2. **Lane:** domain/store + real-input journey.

**Touch:** upstream MOD compiler; proposed source-to-runtime adapter only where absent; Creator General page and local library commands.

**Deliver:** extend CS-17's immutable artifacts and hash identity to multiple definitions grouped under PackId/version; canonical serialization; source/runtime schemas version independently. Preserve one-definition projects through sequential migration. Expose validation through the existing headless/CLI convention. Provide in-game Create Pack, add copies of existing local items, choose destination pack, edit author version and Save/Validate controls. Reuse duplicate/remapping rules for copied definitions and their internal references; preserve external references and original items so existing Scenes remain intact. Show identity/version conflicts as diagnostics. No external JSON editing is required.

**Checks:** repeated compile and source-layout-only edit produce identical runtime bytes; one-pixel/runtime-property change alters hash; sorted references; unsupported schema/capability; malformed asset; no source editor metadata in runtime; local compile/import parity. Real-input journey: create a pack, add two existing local items, set its version, save/validate, restart and reopen with both items and references intact; original items and saved Scene references remain unchanged. **Exit:** the two-item pack is authored entirely in game and compiles offline without engine Resources in the result. **Exclude:** Workshop admission and executable metadata.

### CS-19 — Dependency lock, update and enable transactions

**Depends on:** CS-18. **Lane:** domain/store + scene scenario. **Read:** research 11–13 and 22.5.

**Touch:** upstream provider resolver and Scene/Blueprint dependency records.

**Deliver:** extend CS-17's snapshot retention and pinned references to bounded transitive multi-pack resolution, exact hash locks, typed identity/version conflicts and last-known-good version retention. Enable/update is explicit and atomic; existing live instances and saved documents retain their resolved versions/assets until their references are safely retired. Missing versions preserve inert bounded payloads.

**Checks:** cycle/depth/count/byte limits; same PackId/version different hash; transitive incompatible build; offline resolution; interrupted update; deleting/disable while instances live; reinstall restores placeholder references; save/load does not erase unknown state. **Exit:** proposed `creator_pack_update_retention` passes. **Exclude:** automatic downloads or silent latest-version substitution.

### CS-20 — Typed behavior graph compiler

**Depends on:** CS-18, U-SIGNAL, D-GRAPH. **Lane:** domain. **Read:** research 7 and security supplement graph contract.

**Touch:** upstream MOD graph compiler; add no second interpreter model for the UI.

**Deliver:** DTOs now consumed by runtime work, registered typed nodes/ports, source-location diagnostics and prepared execution representation. Default unknown capability to rejection. Define stable node/event order and delayed-boundary validation.

**Checks:** wrong socket type; unknown action; forbidden expressions/object references; immediate cycle; zero/negative timer; legal next-tick cycle; excessive node/edge/depth/state limits; deterministic compile. **Exit:** minimal OnSignal -> approved action compiles without UI. **Exclude:** reflection, text scripting, arbitrary method/property dispatch.

### CS-21 — Bounded graph execution on the routed tick

**Depends on:** CS-20. **Lane:** domain + stress scenario.

**Touch:** upstream signal/action scheduler and global Scene budget owner.

**Deliver:** bounded ready/next-tick queues, integer tick timers, stable scheduling/RNG, per-instance/pack/Scene fuel and action-specific fan-out/query accounting. Retain spawn ancestry after parent destruction. Implement D-GRAPH overflow diagnostic/disable policy and unconditional cleanup.

**Checks:** timer storm; delayed recursive spawner; cross-pack ping-pong; contact flood; expensive query action; queue boundary and one-over; destroyed handles; no catch-up after pause; identical seed/order; settled allocation test; cleanup still runs after fuel exhaustion. **Exit:** overload stays bounded without lowering 120 Hz or starving teardown. **Exclude:** unbounded per-frame traversal and per-entity physics callbacks.

### CS-22 — Advanced Behavior and Ports UI

**Depends on:** CS-19, CS-21, CS-16 for gun journey. **Lane:** real-input journey.

**Touch:** Creator graph/ports UI consuming compiler descriptors and existing working-copy commands.

**Deliver:** trusted event/condition/action picker, typed connections, node-specific diagnostics, visible budget meter, source layout persistence and Apply/Test. Keep simple Function UI available for templates.

**Checks:** invalid connection can't activate; keyboard navigation and visible errors; unsaved layout/behavior edits; reload/test generation safety; research advanced journey Button -> custom Gun -> Blueprint -> save/load. **Exit:** CREATOR-4 usable without JSON editing. **Exclude:** arbitrary expression boxes and alternate runtime execution in UI callbacks.

### CS-23 — Separate hostile runtime package validator

**Depends on:** CS-19, D-PACK; CS-21 only if graph payload is admitted. **Lane:** hostile-input tests + emulator.

**Touch:** separate MOD/Creator package policy; existing staging infrastructure. Keep ShareManifestPolicy's Buddy/Room admission intact.

**Deliver:** schema-owned exact paths and declared assets, immutable incoming snapshot, bounded parse/decode/compile, transitive build/exposure checks, typed validation failures. Validate the same compiled runtime bytes locally before publish.

**Checks:** path traversal/absolute/device/case-collision/duplicate paths; links/reparse points; undeclared/executable file; hash mismatch; JSON duplicate/unknown fields per schema policy; excessive nesting/count/bytes; malformed PNG/decode expansion; future schema; mutable source cache after snapshot. **Exit:** valid pack emulator roundtrip plus unchanged Buddy/Room hostile suite. **Exclude:** accepting generic directories or calling ResourceLoader on hostile data.

### CS-24 — Publish/Update Content Pack

**Depends on:** CS-23, U-WORKSHOP. **Lane:** managed transport + UI/emulator.

**Touch:** existing publish orchestration/callback lane; Creator Publish page adapter.

**Deliver:** immutable saved revision -> compiled/validated pack -> canonical preview -> publish/update. Preserve semantic PackId and separate PublishedFileId provenance. Derive compatibility/dependency/budget display; gate UI by distribution and actual capability availability.

**Checks:** validation fail before submit; cancel before/after remote commit; duplicate/late callback; retained staging while Steam consumes it; legal-agreement result; offline typed unavailable; source edits while publishing cannot change submitted bytes; no concurrent callback-lane owner. **Exit:** directory transport publish/update journey. **Exclude:** new Steam adapter or changed AppID ownership.

### CS-25 — Import/enable and failed-update recovery UI

**Depends on:** CS-24. **Lane:** emulator + real-input journey.

**Touch:** existing subscription/import surface and CS-19 provider transactions.

**Deliver:** validated local copy with explicit enable/use, dependency/conflict diagnostics, update candidate and last-known-good retention. Subscription removal doesn't delete imported content. No silent source-project reconstruction promise: variants initialize from public runtime semantics with flattened art.

**Checks:** fresh import remains inactive; missing dependency; conflicting hash; invalid update leaves active version; unsubscribe/offline reuse; stale download/cancel; Scene reload keeps locked version; no automatic remote-preview display that bypasses current policy. **Exit:** research Workshop safety journey with emulator. **Exclude:** auto-activation and writes to another item's upload directory.

### CS-26 — Creator release verification and closeout

**Depends on:** the advertised feature set and its transitive prerequisites: CS-07 for local Blueprints; CS-17 for local items (CS-16 only for advertised Melee/Gun); CS-19 for multi-pack enable/update; CS-22 for Advanced Behavior; CS-25 only for Content Pack Workshop sharing. **Lane:** applicable full regression, performance, Windows and external Steam matrix.

**Deliver:** exact-commit acceptance report with scenario/journey IDs, build matrix, hardware, p95/p99 timings, peak memory, allocations, queue/contact counts and external gate status. Keep the Normal Demo intact; Next Fest optional item authoring must not block its required Blueprint/systemic slice.

**Checks:** existing painting/character/window/physics/Workshop suites; item limits combined with legal Scene load; repeated test/enable/update/delete; malformed pack sweep; addon absent and present/no-Steam; itch.io exclusion; all three content builds reject indirect unavailable capabilities. Standalone Windows 10/11 DPI 100/125/150/200%, monitor recovery, compact/fullscreen and input ownership. Live two-account publish/legal/subscribe/import/update requires configured Steam and remains explicitly external if unavailable.

Select new-feature checks from the advertised slice: omit Creator graph/Content Pack checks only when those features are absent, recording them as not applicable with the reason. Preserve existing Workshop v1 and other baseline regressions. A local-only release does not require CS-23–25 or live Content Pack Steam validation; required external checks for shipped features remain named gates. Record each slice's acceptance separately and rerun CS-26 when its advertised feature set expands.

**Exit:** no selectable unfinished features; exact head passes required PR gates; measured locked budgets preserved; only named external gates may remain. **Exclude:** slow steps added to push `CI / quick`, self-hosted runners, claimed passes without evidence.

### CS-27 — Expansion packet template (deferred)

**Depends on:** CS-26 plus explicit scope/tuning decision for each addition. **Lane:** selected per capability.

Do not implement this as a single “richer templates” task. Create a separately numbered child packet for Projectile, Explosive, Construction Part, Device, Material, custom audio, generated hull/extrusion or richer painting only when authorized. Each child must list the upstream trusted capability, exact authorable fields/assets, build/exposure policy, measured limits, decoder/physics/threading risks and one complete user journey. Follow compiler -> runtime -> source store -> UI -> hostile package -> verification order. Default new capabilities to CoreOnly until reviewed.

**Exit:** child has the same prerequisite/deliver/check/exclude structure as above and its own acceptance evidence. **Exclude:** executing the whole candidate list because it appears in research section 15.

## 5. Ownership and failure rules used by every packet

| Boundary | Owner/required behavior |
| --- | --- |
| Source edit | Session revision changes; saved baseline and runtime stay unchanged. |
| Validation/compile | Engine-free prepared data and typed errors; no source-driven Godot type/Resource loading. |
| Asset preparation | Bounded detached work outside physics tick; required texture operations on main thread. |
| Apply | Generation-checked safe-boundary commit; old valid result survives preparation failure. |
| Save | Atomic source transaction; only saved revision advances baseline; post-commit result reflects actual persistence. |
| Test reset/exit | Retire generation first, release callbacks/queues/effects/bodies/assets, then restore shell and pause ownership. |
| Import | Validate one immutable snapshot, commit local artifact, then explicit enable; preserve previous valid version. |
| Publish | Existing callback lane and remote commit points; never report remote persistence as undone by cancellation. |
| Capacity failure | Reject before activation or roll back the whole pending graph; keep bounded diagnostic state. |

## 6. Verification lanes and runnable command shapes

Use existing repository runners and their timeout/artifact conventions from [agent verification](AGENT_VERIFICATION_AND_E2E.md). CS-01 records the verified Godot executable/runner path and exact scenario registration entry points. Names beginning `creator_` in this plan are **proposed tests**, not commands that already exist; register them before claiming coverage.

For code packets, baseline and final managed checks from repository root:

```powershell
dotnet build DesktopBuddy.sln -c Debug
dotnet test tests/DesktopBuddy.Domain.Tests/DesktopBuddy.Domain.Tests.csproj -c Debug
```

After CS-01 has resolved the installed Godot 4.6.1 .NET console executable into `$creatorGodotBin`, use the existing runner's equivalent of:

```powershell
& $creatorGodotBin --headless --path . -- --scenario=creator_test_isolation --seed=1
& $creatorGodotBin --headless --path . -- --journey=creator_blueprint_save_place --seed=1
```

Repeat applicable seeded gameplay journeys with seed 7 under the repository's presentation policy. Headless success does not establish GPU pixels, native window recovery, live Steam, or frame-time performance. Run those lanes in the appropriate real environment. Fixed-fps headless execution is for correctness, not a substitute for wall-clock profiling.

- **Domain:** normal, boundary, nonfinite/hostile input, identity, build and state transition checks. Use the existing test project/framework; do not create a new harness.
- **Store:** temp directories with platform-correct paths, commit-stage fault injection and byte-exact recovery; no user-library mutation.
- **Godot:** real trusted runtime ownership, world pause, physics outcomes and teardown counts through registered scenarios.
- **UI:** configured Godot MCP real input first, then committed journey; include keyboard/focus/dirty-close and window recovery when touched.
- **Performance:** warm up, use the repository's settled allocation measurement convention, measure agreed worst-case legal content and peak staging overlap. Compare to CS-01 baseline and locked caps.
- **Platform:** existing Steam-binary guard and native smoke where applicable; optional-addon absence/no-Steam fallback. Never commit SDK/runtime binaries, generated PNG output or `.godot`.

Full scenario/journey/native work belongs in PR/manual workflows. Preserve the current push quick lane and Linux runners. No new benchmark suite is required for documentation-only packets.

## 7. Packet handoff template

```text
Packet: CS-xx — title
Status: Passed / Implemented but verification pending / Blocked
Commit(s) and tested HEAD:
Prerequisites: U-/CS-/D- IDs with evidence
Behavior delivered:
Files and public contracts changed:
Schema/migration and commit-point behavior:
Tests: exact commands, seeds, environment, verdict, artifact paths
Interactive evidence (if player-visible):
Performance: baseline vs result, peak memory, tested load
Known failures/external gates:
Next eligible packet:
```

Do not assign “Passed” when a required environment is missing. Name the missing gate and retain the implemented work. Distinguish an external release check from a code defect. Update source/schema documentation in the same implementation packet that changes it.

## 8. Execution ledger

Initial state: **CS-01 through CS-27 are Not started**. This document and the research review do not count as CS-01's implementation-branch baseline audit. The implementation agent records actual U-* and D-* evidence here before dependent work. No product limit is approved by the examples in this plan.

| Packet/decision | Status | Commit/evidence | Remaining gate |
| --- | --- | --- | --- |
| CS-01 | Not started | — | Audit current implementation branch and source decisions. |
