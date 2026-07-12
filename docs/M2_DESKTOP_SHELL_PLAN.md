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

### Task 0 — Renderer decision spike (owner-manual gate, in progress)
`ARCHITECTURE.md` §20: validate per-pixel transparency + `msaa_2d` + V-sync
together on the `gl_compatibility` renderer, Win10/11. Record the decision in
`docs/DECISIONS.md` before HUD work. Status per `CHECKLIST.md`: 100% scale pass
confirmed 2026-07-12; **remaining** = 150% DPI pass, corner-readout pointer
checks, keep/delete of the spike scene, and the recorded decision. Blocks HUD
features only, not the shell engineering below.

### Task 1 — Window placement policy (Domain, headless-testable) — DONE
Pure geometry in `DesktopBuddy.Domain`, no engine:
- first-launch anchor 16 px inside the lower-right of the usable work area;
- clamp a stored position/size back into a usable monitor rect (off-screen
  recovery after monitor removal / topology change);
- enforce default `480x360`, minimum `360x270`, and monitor-usable maximum;
- carry DPI context.
Reuses `RoomLayoutPolicy` for zoom/room-floor clamping. xUnit coverage for
first-launch, persisted-valid, fully-off-screen, partially-off-screen, monitor
smaller than the window, and DPI-context cases.

### Task 2 — Input-mode state machine (Domain, headless-testable) — DONE
Pure Work/Play transition rules (`DECISIONS.md` "Overlay and Interface"):
enter Play on buddy/menu/tool-select/global-toggle; return to Work on
outside-click/`Escape`/global-toggle/tray; selected tool never changes across a
transition; input mode never changes from inactivity alone; transitions never
synthesize primary input. xUnit coverage per trigger and per invariant.

### Task 3 — Desktop window-service seam + emulated adapter (headless-testable) — DONE
`IDesktopWindowService` / `WindowSettings` (`ARCHITECTURE.md` §5), a
`GodotDesktopWindowService` using Godot Window APIs first (transparent,
borderless, topmost, size, position, usable-monitor rect) with a transparency
availability probe and opaque bordered fallback, and an `EmulatedDesktopWindow
Service` for headless/editor/CI. `IWindowsDesktopAdapter` abstracts the native
work; the emulated adapter is the CI default. Startup validation asserts the
window baseline. **Native adapter itself is Task 4.**

### Task 4 — Native Windows adapter (owner-manual verified)
`WindowsDesktopAdapter : IWindowsDesktopAdapter` (`ARCHITECTURE.md` §9, §24):
native handle, safe WndProc subclass/restore with a kept-alive delegate,
Work-Mode hit testing (`HTTRANSPARENT` over transparent pixels; normal over
buddy/menu/border/resize handles; whole-box in Play Mode), DPI screen/client
conversion, tray icon + menu, global hotkey register/conflict-report, launch-at-
login, and the §24 lifecycle messages (`WM_ENTERSIZEMOVE`/`EXITSIZEMOVE`,
`WM_DPICHANGED`, `WM_DISPLAYCHANGE`, work-area `WM_SETTINGCHANGE`,
`WM_POWERBROADCAST`, session lock/unlock). Restore the original procedure on
shutdown and on window recreation; never `SetWindowRgn`. Failure falls back to an
opaque/full-capture window with tray recovery. Verified on real Windows, then the
findings promoted into the §5 matrix checklist.

### Task 5 — Shell composition + resize→boundary integration
Compose the window service, placement policy, and mode machine into the sandbox
boot (a `DesktopShellController` under the app root per `ARCHITECTURE.md` §3).
Resize/zoom enqueue a boundary rebuild on the next physics boundary through the
existing `BoundaryController` path; forced corrections call
`ResetPhysicsInterpolation()`. Recovery paths (`Escape`, global toggle, tray)
always restore Work-Mode control. Headless-cover the seam-level wiring.

### Task 6 — Headless mode-transition journeys (Tier 2)
`AGENT_VERIFICATION_AND_E2E.md` §7 M2 row: windowed/headless mode-transition
journeys where in-process input suffices (buddy/menu interaction → Play;
outside-click / `Escape` → Work; tool persists across transitions). Document the
agent-assisted native matrix workflow.

### Task 7 — Standalone Windows matrix (owner-manual gate)
Execute `TEST_PLAN.md` §5 outside the editor across the Win10/11 × scale ×
monitor × size matrix, including transparency-forced-unavailable fallback and
recovery from every focus state. This is the milestone exit gate.

## This session

Landed Tasks 1–3 (headless-testable foundation) with build + domain tests green.
Tasks 0, 4, 7 are owner-manual gates; Tasks 5–6 are the next agent slices.
</content>
</invoke>
