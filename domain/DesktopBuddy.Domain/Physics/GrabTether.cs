using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Immutable input for the player grab tether: a damped elastic pull that
/// closes the gap between the cursor anchor and the acquired point on the target
/// body (RAGDOLL_AND_GAMEPLAY_SPEC.md Section 6). Positions/velocities are sampled
/// by the Godot adapter; the math is Godot-free so it is unit-tested directly.
/// </summary>
public readonly record struct GrabTetherInput(
    Vector2 AnchorError,       // cursorAnchor - acquiredWorldPoint (the gap to close)
    Vector2 RelativeVelocity,  // acquiredPointVelocity - cursorVelocity
    float Stiffness,
    float Damping,
    float MaximumForce);

/// <summary>Allocation-free result for one tether tick.</summary>
public readonly record struct GrabTetherResult(
    Vector2 Force,   // applied to the target body at the acquired point
    float Extension, // |AnchorError| — how far the tether is stretched
    bool ForceClamped);

/// <summary>
/// Pure PD tether solver plus the release-velocity cap. The tether is a
/// spring/damper toward the cursor with a bounded force; it deliberately has no
/// breaking limit, so resistance can stretch it but never detach it. On release
/// the target keeps its motion, capped to a safe maximum throw speed.
/// </summary>
public static class GrabTether
{
    private const float Epsilon = 0.00001f;

    public static GrabTetherResult Evaluate(in GrabTetherInput input)
    {
        // PD controller: pull toward the cursor, damped against relative motion.
        Vector2 force = (input.AnchorError * input.Stiffness) -
                        (input.RelativeVelocity * input.Damping);

        bool clamped = false;
        float forceLengthSquared = force.LengthSquared();
        float maximumForceSquared = input.MaximumForce * input.MaximumForce;
        if (forceLengthSquared > maximumForceSquared && forceLengthSquared > Epsilon)
        {
            force *= input.MaximumForce / MathF.Sqrt(forceLengthSquared);
            clamped = true;
        }

        float extension = input.AnchorError.Length();
        return new GrabTetherResult(force, extension, clamped);
    }

    /// <summary>
    /// Cap a release velocity's magnitude to the configured throw-speed cap while
    /// preserving its direction (FR-006.4). Sub-cap velocities pass through
    /// unchanged, so ordinary releases keep their exact motion.
    /// </summary>
    public static Vector2 CapReleaseVelocity(Vector2 velocity, float maximumSpeed)
    {
        if (!(maximumSpeed >= 0.0f))
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumSpeed), maximumSpeed, "Throw-speed cap must be non-negative and finite.");
        }

        float speedSquared = velocity.LengthSquared();
        float maximumSquared = maximumSpeed * maximumSpeed;
        if (speedSquared <= maximumSquared || speedSquared <= Epsilon)
        {
            return velocity;
        }

        return velocity * (maximumSpeed / MathF.Sqrt(speedSquared));
    }
}
