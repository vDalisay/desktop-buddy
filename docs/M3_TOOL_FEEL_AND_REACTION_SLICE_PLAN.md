# M3 Tool Feel and Reaction Slice

Status: owner requirements confirmed 2026-07-14. This slice follows the committed M3 Tasks 9–11 baseline (`6a485ba`) and does not implement the deferred persistent jump-personality/autonomy rewrite.

## Scope and architecture

1. Extend the Godot-free care model with distance-based Pet satisfaction, per-selection favorite weighting, and the confirmed Tickle Friendly/Angry/cooldown state machine. Keep confirmed constants in typed tuning data and cover normal, threshold, reset, and invalid-input paths.
2. Keep scene roots composition-only. Add focused workers for tool cursor presentation, semantic impact feedback/hit-stop, and tool-driven social reaction intent. Input flows from the existing pointer component; presentation never mutates authoritative damage state.
3. Keep the Boxing Glove a real `RigidBody2D`. Retune its tether/mass/force for lower lag and larger physical impulse. Speed influences pain only through measured solver impulse.
4. Add a target-side guarded-impact seam to the interaction pipeline. It applies only to Boxing Glove contacts on an actively guarding hand and scales accepted impulse by `0.5` before the shared curve. Bypassed hits use the unchanged pipeline.
5. Produce defensive/playful/angry movement as typed intent. The active drive alone applies bounded locomotion, jump, and hand-target forces; ordinary behavior never sets transforms or velocity.
6. Counter-rotate only the head emoticon. Draw original vector Pet/Tickle hand actors under the visible OS cursor with instant presentation following.
7. Halve the recovery assistance ramp to one second and recalibrate bounded self-right forces against a measured pre-change baseline; retain the two-second eligibility and ten-second hard-recovery thresholds.

## Verification

- Domain tests: Pet distance threshold, `1.2x` favorite weighting, three-second dual gate/reset; Tickle rewards at `3`/`6`, anger transition, negative cadence, eight-second cooldown, and no empty-space accumulation.
- Godot scenarios: upright rotating face; glove lag/impulse/speed scaling; guarded versus bypassed impact and displacement; hit-stop trigger/non-trigger/restore/no-stack; Pet favorite and facial feedback; friendly/angry Tickle hop/flee/reset; faster physical recovery.
- Journey: real-input Pet rubbing, Tickle escalation/cooldown, and glove strike/defense/bypass through `Input.ParseInputEvent`.
- Regression: solution build, full Domain suite, Godot import, existing M1/M3 quick validation, normal bootstrap, relevant soak/envelope checks, and windowed feel pass. MCP verification is required when the configured runtime tools are exposed; automated coverage remains mandatory regardless.

## Deferred next slice

At new-save creation, sample a buddy-specific ambient jump propensity and store it in versioned progress JSON. Reloads retain it; starting a new game samples a new value. The later M4 behavior arbiter will combine that trait with obstacle/situation evidence so ambient jumping is reduced and predictable. No persistence schema or obstacle-aware jumping is implemented here.
