using Godot;

namespace DesktopBuddy.Grab;

/// <summary>
/// Snapshot of the current grab, consumed by fear-resistance behavior and lab
/// telemetry. <c>default</c> (Active = false, Target = null) means "no grab".
/// </summary>
public readonly record struct GrabState(
    bool Active,
    RigidBody2D? Target,
    Vector2 CursorAnchor,
    Vector2 GrabPoint);

/// <summary>
/// Development telemetry for the grab tether (RAGDOLL_AND_GAMEPLAY_SPEC.md
/// Section 11.1): current stretch, applied force, clamp state, and the most
/// recent capped release speed.
/// </summary>
public readonly record struct GrabTelemetry(
    bool Active,
    float Extension,
    Vector2 Force,
    bool ForceClamped,
    float LastReleaseSpeed);
