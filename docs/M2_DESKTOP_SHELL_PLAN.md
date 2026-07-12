# Desktop Buddy — Milestone 2 (Windows Desktop Shell) Plan

Status: active work plan for ROADMAP Milestone 2. Authoritative behavior is in
`docs/DECISIONS.md` (wins conflicts), `docs/ARCHITECTURE.md` (§9, §20, §24), and
`docs/TEST_PLAN.md` (§5 standalone matrix, §8 already met for M1). This file is
the ordered task breakdown and status snapshot; when it disagrees with a green
run, trust the run and update this file.

Created 2026-07-12 (branch `opus`) after the M1 physics-lab gate was functionally
met and the accepted tuning locked.

## Scope reminder (ROADMAP M2)

Deliver: renderer decision; transparent borderless movable/resizable box window;
Work/Play input modes with dynamic hit regions, outside-click focus transition,
global hotkey, tray recovery; multi-monitor/DPI placement with first-launch
lower-right anchor, off-screen recovery, always-on-top, AA, V-sync, zoom
settings; opaque fallback when transparency is unavailable.

Exit criteria: the standalone Windows matrix (`TEST_PLAN.md` §5) passes at
minimum/default/ultrawide sizes, and the user can always recover control without
terminating the process.

## Test-layer boundary for M2

Native overlay behavior (real pointer passthrough, focus stealing, tray, global
hotkey, DPI, monitor topology) **cannot** be exercised by in-process input
synthesis (`AGENT_VERIFICATION_AND_E2E.md` §6). So M2 splits cleanly:

- **Headless-gate-able (agent-actionable):** placement geometry, zoom/room
  clamping, the Work/Play transition state machine, the window-service seam with
  the emulated adapter, and the in-process mode-transition journeys.
- **Owner-manual gate:** the renderer visual matrix and the `TEST_PLAN.md` §5
  standalone Windows matrix. An agent can only build the code behind the seam and
  prep/prompt the matrix; sign-off is the owner on real Win10/11 hardware.

Every native code path lives behind `IWindowsDesktopAdapter`; headless/editor and
CI use the emulated adapter, so `dotnet build` + domain tests + headless journeys
stay green with no Windows-only dependency.

## Tasks

### Task 0 — Renderer decision spike (owner-manual gate, POSTPONED)
`ARCHITECTURE.md` §20: validate per-pixel transparency + `msaa_2d` + V-sync
together on the `gl_compatibility` renderer, Win10/11. Record the decision in
`docs/DECISIONS.md` before HUD work. Status per `CHECKLIST.md`: 100% scale pass
confirmed 2026-07-12; **postponed by the owner on 2026-07-12** = 150% DPI pass,
corner-readout pointer checks, keep/delete of the spike scene, and the recorded
decision. Postponement is not acceptance: the renderer decision remains open and
continues to block renderer-dependent HUD features, but not the shell engineering
below.

### Task 1 — Window placement policy (Domain, headless-testable) — DONE
Pure geometry in `DesktopBuddy.Domain`, no engine:
- first-launch anchor 16 px inside the lower-right of the usable work area;
- clamp a stored position/size back into a usable monitor rect (off-screen
  recovery after monitor removal / topology change);
- enforce default `480x360`, minimum `360x270`, and monitor-usable maximum.
DPI context is not part of this pure geometry; it is carried at the window-service
seam (Task 3, `IWindowsDesktopAdapter.GetDpiScale`). Reuses `RoomLayoutPolicy` for
zoom/room-floor clamping. xUnit coverage for first-launch, persisted-valid,
fully-off-screen, partially-off-screen, and monitor-smaller-than-window cases.

### Task 2 — Input-mode state machine (Domain, headless-testable) — DONE
Pure Work/Play transition rules (`DECISIONS.md` "Overlay and Interface"):
enter Play on buddy/menu/tool-select/global-toggle; return to Work on
outside-click/`Escape`/global-toggle/tray; selected tool never changes across a
transition; input mode never changes from inactivity alone; transitions never
synthesize primary input. xUnit coverage per trigger and per invariant.

### Task 3 — Desktop window-service seam + emulated adapter (headless-testable) — DONE
`IDesktopWindowService` / `WindowSettings` (`ARCHITECTURE.md` §5), implemented by
`DesktopWindowController` (a Godot `Node`) using Godot Window APIs first
(transparent, borderless, topmost, size, position, usable-monitor rect) with a
transparency availability probe and opaque fallback. Native work sits behind an
injected `IWindowsDesktopAdapter`; `EmulatedWindowsDesktopAdapter` is the
headless/editor/CI default and records hit-region/capture requests for the
journeys to assert. Startup validation asserts the window baseline. **The native
adapter itself is Task 4.**

### Task 4 — Native Windows adapter (SKELETON LANDED, owner-manual verification pending)
`WindowsDesktopAdapter : IWindowsDesktopAdapter` (`ARCHITECTURE.md` §9) is written
and builds, selected by `WindowsDesktopAdapterFactory` only on a Windows standalone
run with a live display server (headless/editor/non-Windows and any attach failure
fall back to the emulated adapter, so CI never touches native code). First cut
implements the current seam surface:
- real HWND via `DisplayServer.WindowGetNativeHandle`;
- safe WndProc subclass with a GC-rooted delegate, restored on `Shutdown`; never
  `SetWindowRgn`;
- Work-Mode `WM_NCHITTEST` → `HTTRANSPARENT` over non-region pixels, `HTCLIENT`
  over the interactive regions; Play Mode captures the whole box;
- `EnumDisplayMonitors` + `GetMonitorInfo` usable work-area rects; `GetDpiForMonitor`
  per-monitor DPI; `DwmIsCompositionEnabled` transparency probe.

**Known gaps / next cut** (all owner-testable on Windows):
- ~~Hit regions were sandbox-space but treated as client pixels.~~ **RESOLVED**: the
  shell now projects the box through `SandboxProjection` (Domain, xUnit-covered) so
  the adapter receives client-pixel rects at any zoom. **Remaining**: per-monitor DPI
  is not yet folded into the hit test (logical pixels only) — that lands with the
  InputCollector coordinate layer (`ARCHITECTURE.md` §10).
- Tray icon + menu, global hotkey register/conflict-report, launch-at-login, and the
  §24 lifecycle messages (`WM_ENTERSIZEMOVE`/`EXITSIZEMOVE`, `WM_DPICHANGED`,
  `WM_DISPLAYCHANGE`, work-area `WM_SETTINGCHANGE`, `WM_POWERBROADCAST`, session
  lock/unlock) extend the seam in a follow-up slice — not in the current interface.
- Window recreation re-subclass is not yet handled.

**How to verify** (real Windows, outside the editor): run the standalone build; the
log should show `[WinAdapter] Native adapter attached (hwnd=… monitors=N transparency=True)`
and `DesktopWindowController ready (native=True …)`. Then walk the §5 matrix: Work-Mode
passthrough over transparent pixels, box interaction entering Play, outside-click and
Escape returning to Work, multi-monitor/DPI placement, and clean shutdown restoring the
window procedure.

### Task 5 — Shell composition + resize→boundary integration — DONE
`SandboxRoot` gained its single gameplay `_PhysicsProcess` and now composes
`DesktopWindowController` + `DesktopShellController` + a real `BoundaryController`
box (`SandboxBorder` draws the visible frame). `sandbox.tscn` was rebuilt from the
empty M0 stub. The shell applies the launch placement and window flags on boot,
drives Work/Play from the mode hotkey / Escape / box clicks / focus loss, and
drains a queued window resize into a `BoundaryController.RequestLayout` applied on
the physics boundary (`PhysicsTick`). Recovery (`Escape`, global toggle,
`ReturnToWorkMode()` for tray) always restores Work Mode. `ResetPhysicsInterpolation`
after forced corrections lands with the buddy composition (no dynamic bodies in the
sandbox yet). Verified: `boot_smoke` scenario + journey compose the shell headless.

### Task 6 — Headless mode-transition journeys (Tier 2) — DONE
`tests/journeys/desktop_shell_modes.json` drives the shell through the input paths
in-process synthesis can reach — the mode hotkey action, Escape, and clicks
inside/outside the box — and asserts Work↔Play transitions and control recovery
(8 predicates, all green). Native passthrough/tray/resize stay in the owner-manual
§5 matrix per `AGENT_VERIFICATION_AND_E2E.md` §6; the resize→boundary path itself is
already covered by the `room_resize_zoom` scenario.

### Task 7 — Standalone Windows matrix (owner-manual gate)
Execute `TEST_PLAN.md` §5 outside the editor across the Win10/11 × scale ×
monitor × size matrix, including transparency-forced-unavailable fallback and
recovery from every focus state. This is the milestone exit gate.

## Progress

Tasks 1–3 (foundation), Tasks 5–6 (shell composition + mode-transition journey), and
the Task 4 native adapter **skeleton** are landed with the suite green (build 0/0, 92
domain tests, `boot_smoke` + `desktop_shell_modes` journeys exit 0, headless confirmed
`native=False`). Remaining is all owner-manual on real Windows: the postponed Task 0
renderer visual matrix (150% DPI unverified), Task 4 verification + its next cut
(coordinate mapping, tray/hotkey/launch-at-login, lifecycle messages), and Task 7 the
`TEST_PLAN.md` §5 standalone matrix that is the milestone exit gate.
