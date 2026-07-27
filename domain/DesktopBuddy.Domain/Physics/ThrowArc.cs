using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>One solved launch.</summary>
/// <param name="Velocity">
/// Launch velocity in px/s, screen axes (<c>+Y</c> is down). Meaningless when
/// <see cref="IsValid"/> is false.
/// </param>
/// <param name="Clamped">
/// The unclamped solution exceeded the speed cap and was scaled down. Direction and arc shape
/// are preserved, so the throw still goes the right way — it simply falls short, which is the
/// honest answer to "that target is further than the buddy can throw".
/// </param>
/// <param name="IsValid">False when the inputs cannot describe a flight at all.</param>
public readonly record struct ThrowArcResult(Vector2 Velocity, bool Clamped, bool IsValid)
{
    public static ThrowArcResult Invalid => new(Vector2.Zero, false, false);
}

/// <summary>
/// Solves the launch velocity that carries a thrown object from the hand to a chosen target
/// point in a fixed flight time (owner instruction 2026-07-27: "throw towards where the cursor
/// location is").
///
/// <para><b>Fixed flight time, not fixed speed.</b> Aiming a fixed launch speed at a target is
/// the classic ballistic problem, and it has zero, one, or two solutions depending on range —
/// so a target just out of reach produces no answer at all, and near targets produce either a
/// flat bullet or an absurd moon shot. Solving for a fixed <i>duration</i> instead always has
/// exactly one answer, scales speed naturally with distance, and guarantees a visible arc: the
/// upward component always carries the <c>½gT²</c> the fall will spend.</para>
///
/// <para><b>Damping is in the model.</b> Loose objects run real linear damping (Replace mode),
/// which over a half-second flight bleeds off a large fraction of the launch speed. A vacuum
/// solution therefore lands well short of the cursor, so the exponential decay is solved for
/// directly rather than ignored.</para>
///
/// <para>Pure and allocation-free (ARCHITECTURE §23); the runtime supplies the live gravity and
/// damping values so nothing here has to assume engine defaults.</para>
/// </summary>
public static class ThrowArc
{
    /// <summary>Below this damping coefficient the undamped limit is used instead.</summary>
    private const float DampingEpsilon = 0.0001f;

    /// <summary>
    /// Launch velocity that puts an object at <paramref name="displacement"/> from its release
    /// point after exactly <paramref name="flightSeconds"/>.
    /// </summary>
    /// <param name="displacement">Target minus release point, in px, screen axes.</param>
    /// <param name="gravity">Downward acceleration in px/s²; positive, screen axes.</param>
    /// <param name="linearDamping">
    /// The object's linear damping coefficient (velocity decays as <c>e^(-kt)</c>). Zero is a
    /// vacuum throw.
    /// </param>
    /// <param name="flightSeconds">How long the throw should stay in the air.</param>
    /// <param name="maximumSpeed">Speed cap; the solution is scaled down to fit, not rejected.</param>
    public static ThrowArcResult Solve(
        Vector2 displacement,
        float gravity,
        float linearDamping,
        float flightSeconds,
        float maximumSpeed)
    {
        if (!IsFinite(displacement) ||
            !float.IsFinite(gravity) || gravity < 0.0f ||
            !float.IsFinite(linearDamping) || linearDamping < 0.0f ||
            !float.IsFinite(flightSeconds) || flightSeconds <= 0.0f ||
            !float.IsFinite(maximumSpeed) || maximumSpeed <= 0.0f)
        {
            return ThrowArcResult.Invalid;
        }

        var acceleration = new Vector2(0.0f, gravity);
        Vector2 velocity;
        if (linearDamping < DampingEpsilon)
        {
            // Vacuum limit: d = v0·T + ½gT².
            velocity = (displacement / flightSeconds) - (acceleration * (0.5f * flightSeconds));
        }
        else
        {
            // With v' = -kv + g the displacement over T is (v0 - g/k)·A + (g/k)·T, where
            // A = (1 - e^(-kT))/k. Inverting that for v0 is the whole solver.
            float decay = MathF.Exp(-linearDamping * flightSeconds);
            float integral = (1.0f - decay) / linearDamping;
            Vector2 terminal = acceleration / linearDamping;
            velocity = ((displacement - (terminal * flightSeconds)) / integral) + terminal;
        }

        if (!IsFinite(velocity))
        {
            return ThrowArcResult.Invalid;
        }

        float speed = velocity.Length();
        if (speed > maximumSpeed)
        {
            return new ThrowArcResult(velocity * (maximumSpeed / speed), true, true);
        }

        return new ThrowArcResult(velocity, false, true);
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
