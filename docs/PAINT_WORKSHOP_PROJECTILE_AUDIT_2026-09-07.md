# Paint, Workshop, and projectile audit — 2026-09-07

Branch: `codex/paint-workshop-projectile-audit`. Base: `3966648b`.
Scope: focused performance/correctness review of Buddy painting, the recent Workshop browser/query additions, and pistol/shotgun visuals. This is not an exhaustive review of every subsystem.

## Findings addressed

| Priority | Finding | Change |
| --- | --- | --- |
| P1 | The bullet and bright tracer cancelled the current body rotation, but Godot's rendering server subsequently interpolated the parent transform. The old inverse-rotation check could pass while the visible streak turned. | Both layers now draw in the existing top-level visual child under an identity transform, with interpolation disabled for that frame-updated drawing. Physical rotation, collision, velocity, impact settling, and damage are unchanged. Pool reuse and transformed-parent tests cover both guns. |
| P1 | A concurrent Workshop details request could inherit an unrelated request's IDs/results. Cancelling the owner left the query handle and lane pending. | Reject concurrent requests with a typed failure; release the owner's handle on cancellation/timeout, ignoring late callbacks. |
| P1 | A silent Workshop browse callback could leave the browser waiting indefinitely; transport shutdown did not complete pending browsing. | Bound the wait to 15 seconds of monotonic elapsed time, return a typed failure, release the handle once, and complete pending browsing as unavailable on shutdown. |
| P2 | Brush stamps repeatedly compared/wrote four individual RGBA bytes in the inner loop. | Compare/write one little-endian RGBA value with `BinaryPrimitives`, preserving geometry, wrapping, dirty revisions, and pixels. |
| P2 | Pen/eraser footprints and spray pulses allocated a fresh hit list repeatedly. | Reuse one canvas-owned list; the workspace consumes it synchronously. |
| P2 | Initial texture queueing uploaded six untouched transparent surfaces. | Leave revision-zero surfaces unbound until painted; the blank workspace now requires zero uploads. Existing texture updates and coalescing are retained. |
| P2 | Painting diagnostics performed file I/O from `_PhysicsProcess`, including outside development builds. | Run the existing sampling from `_Process` behind `BuildInfo.IsDebugBuild`. |
| P2 | Generated replacement hit testing fetched the same native global transform again for every candidate triangle hit. | Snapshot the transform once per raycast. |
| P2 | Workshop panel teardown left browse cancellation and cached previews alive; decoded images were not explicitly disposed. | Cancel browsing, invalidate pending preview generations, dispose cached texture wrappers on exit, and dispose decoded images after use. |
| P2 | Gun regression automation contained stale assumptions. | Nerf/Pistol waits for actual selection and uses measured simulation time for mood drift. The shotgun journey now tests the existing authored `InfiniteMagazine` behavior, including firing beyond nominal capacity without reload/dry fire. No gameplay tuning changed. |

Godot's [2D interpolation documentation](https://docs.godotengine.org/en/4.6/tutorials/physics/interpolation/2d_and_3d_physics_interpolation.html) explains the rendering-server/local-transform behavior underlying the projectile fix. The former test only multiplied a rotation by its inverse and could not detect interpolation inherited from its parent.

## Measurement

Local .NET 8 Release microbenchmark, seven timed runs after four warmups, 2,000 maximum-size brush stamps per run: diameter 128, vertical scale 0.5, U cycling from 0 to 0.99, V 0.5, changing red channel. Median CPU time changed from **46.21 ms to 31.68 ms**, about **31% less stamp time**. This measures CPU rasterization, not end-to-end editor FPS or GPU upload time.

Both implementations produced SHA-256 `B8CD92A76A3D66CEAF8F0509CB38206C330D01A16CF5381399053418135D2BF6`. The committed `PackedRgbaLargeBrushMatchesPreOptimizationPixelsAndSkipsEqualStamps` test locks that pre-change output and verifies equal stamps do not increment revision.

## Verification

- Debug solution build and complete managed/domain suite: 1,493 tests pass.
- Headless editor import passes.
- New adapter lifecycle scenario covers overlapping requests, cancellation, late/duplicate callbacks, a silent browse timeout, and shutdown.
- Installed GodotSteam native-addon smoke passes, including initialization, AppID identity, and subscription metadata loading. This does not replace two-account validation.
- New projectile scenario covers both gun profiles, freely spinning bodies, transformed parents, opposite flight directions, and reused pool slots.
- Existing pistol, shotgun, Nerf comparison, painting, persistence, upload, layer-order, memory, and Workshop room/buddy emulator scenarios pass; exact local verdicts/logs are under `.artifacts/audit/` and `.artifacts/audit-*.log`.
- Pistol, shotgun, and character-paint save/use/restart journeys pass.
- The generated-paint UV scenario could not pass its prerequisite: this checkout has no generated Tops fixture/catalogue (zero replacement surfaces). Generated replacement raycast coverage remains unverified locally; existing assets were not overwritten to manufacture fixtures.
- New focused gates are added only to the pull-request/manual build job. The push quick job is unchanged.

The Godot MCP interactive launch returned **“User declined run_project.”** No interactive verification was performed after that denial. Visible brush feel and the rendered bullet fix still need a playtest; headless transform assertions are not visual acceptance. This branch has not been pushed through the three required PR workflows, and live Steam/two-account validation remains an external release gate.

## Remaining audit findings

### Owner-reported painting follow-ups

The owner reported striped pen dots at sphere poles and a brush ellipse shrinking relative to its cursor at part centers. Canvas sampling now subdivides stretched primitive-surface sample gaps, and the brush reuses the screen-space dab path with the cursor's existing ellipse ratio. This corrects placement without changing the saved atlas or the direct-domain UV brush fallback. The extended `paint_limb_pose_mapping` regression covers both hands/feet at their upper poles, exact Undo, and brush outline coverage across both hands and the torso center band. The brush check failed with 20 missed interior probes before the change and passes with zero misses/overflow afterward. All 91 painting unit tests and four focused headless scenarios pass; visible playtesting remains outstanding. Existing saved gaps/undersized marks are unchanged and require repainting.

### Outstanding findings

The same bounded base grid now applies to Pen after the owner reported matching lag there. In the same 80-movement CPU test, Pen decreased from 320.20 ms to 149.15 ms; the maximum-size circular dab decreased from 4,091 to 1,107 samples. Circular coverage, upper-pole gap checks, and exact stroke Undo pass. These measurements exclude rendered frame time.

The subsequent maximum-brush lag report is addressed by snapshotting canvas/preview state once per dab, skipping generated-mesh hit testing when no replacements exist, limiting the brush's base sampling grid to 16 steps per radius with adaptive gap refinement retained, and using contiguous row comparison/fill for rasterization. The deterministic 80-movement, maximum-brush, zoom-4 CPU scenario measured 419.63 ms before and 124.32–135.08 ms afterward on this machine. This is an isolated CPU measurement, not GPU frame-time acceptance. Its maximum dab uses 1,385 samples and retains the ellipse coverage and exact Undo checks. The scalar-equivalence test covers seams, poles, narrow atlas lanes, and repeated erasing; the full suite passes 1,495 tests, alongside the four focused paint scenarios. Standalone visual/frame-time validation remains outstanding.

- **Workshop preview policy and memory:** the new community browser automatically decodes remote PNG/JPEG/WebP previews and keeps an unbounded URL cache during the panel's lifetime. Its 5 MiB HTTP-body limit does not bound decoded image memory. The normative M6 supplement still specifies external preview browsing and no automatic remote preview rendering. Resolve this existing implementation/documentation mismatch before expanding the browser; either restore the approved external-preview flow or explicitly authorize a bounded thumbnail format, decode policy, and cache budget. This patch only fixes teardown ownership.
- **Exit diagnostics:** the pistol punctuation and Nerf comparison scenarios pass their behavior checks but emit native resource/ObjectDB warnings on exit. The shared smoke-texture lifetime needs a separate teardown check; those warnings are not counted as clean engine shutdown. The new isolated projectile scenario's World2D/shape cleanup is explicit and its run is clean.
- **Build diagnostics:** the build reports 11 CA2255 warnings on existing scenario-registration module initializers. No new compiler warning category was introduced.
- **Performance acceptance:** the microbenchmark does not establish the full Windows/DPI/GPU performance matrix. Generated-asset brush feel, maximum-history memory, and frame-time spikes should be checked in the target Windows playtest before release.
