using System;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Immutable input for the bounded angular servo that holds an elongated
/// cursor-tethered tool square to its own swing (RAGDOLL §9.1). This is the
/// rotational counterpart of <see cref="GrabTether"/>: the linear tether closes
/// the gap to the cursor, this closes the gap to the intended angle.
/// </summary>
public readonly record struct AlignmentTorqueInput(
    float AngleError,      // targetAngle - currentAngle, in radians, any winding
    float AngularVelocity, // the body's current spin
    float Stiffness,
    float Damping,
    float MaximumTorque);

/// <summary>Allocation-free result for one alignment tick.</summary>
public readonly record struct AlignmentTorqueResult(
    float Torque,
    float WrappedError, // the shortest-way-around error the torque was built from
    bool IsValid,
    bool WasClamped);

/// <summary>
/// Pure PD angular servo with a hard torque bound. Unlike
/// <see cref="PendulumTorque"/> — which models gravity and deliberately permits
/// momentum and overshoot around its equilibrium — this drives an angle the
/// player is authoring through cursor motion, so it takes the shortest way
/// around and settles rather than swinging.
///
/// The error is wrapped before the proportional term, so a tool whose swing
/// direction crosses ±π turns the short way instead of unwinding a full circle.
/// A non-finite input yields zero torque rather than poisoning the body with
/// NaN; a zero stiffness disables alignment entirely, which is how a circular
/// tool authors "no alignment" without a branch at the call site.
/// </summary>
public static class AlignmentTorque
{
    public static AlignmentTorqueResult Evaluate(in AlignmentTorqueInput input)
    {
        if (!float.IsFinite(input.AngleError) ||
            !float.IsFinite(input.AngularVelocity) ||
            !IsFiniteNonNegative(input.Stiffness) ||
            !IsFiniteNonNegative(input.Damping) ||
            !IsFinitePositive(input.MaximumTorque))
        {
            return new AlignmentTorqueResult(0.0f, 0.0f, false, false);
        }

        float wrappedError = HangFrame.WrapAngle(input.AngleError);
        float rawTorque = (wrappedError * input.Stiffness) -
                          (input.AngularVelocity * input.Damping);
        if (!float.IsFinite(rawTorque))
        {
            return new AlignmentTorqueResult(0.0f, wrappedError, false, false);
        }

        float torque = Math.Clamp(rawTorque, -input.MaximumTorque, input.MaximumTorque);
        return new AlignmentTorqueResult(
            torque, wrappedError, true, torque != rawTorque);
    }

    /// <summary>
    /// The angle an elongated tool should hold to strike with its length while
    /// travelling along <paramref name="velocityX"/>/<paramref name="velocityY"/>:
    /// square to the direction of travel, so the swing presents the barrel and not
    /// the tip. Below <paramref name="minimumSpeed"/> the swing has no direction to
    /// speak of and the caller must hold its previous angle, which
    /// <c>hasTarget = false</c> reports.
    /// </summary>
    public static (float Angle, bool HasTarget) SwingAngleFor(
        float velocityX,
        float velocityY,
        float minimumSpeed)
    {
        if (!float.IsFinite(velocityX) || !float.IsFinite(velocityY) ||
            !IsFiniteNonNegative(minimumSpeed))
        {
            return (0.0f, false);
        }

        float speedSquared = (velocityX * velocityX) + (velocityY * velocityY);
        if (speedSquared <= minimumSpeed * minimumSpeed)
        {
            return (0.0f, false);
        }

        // Perpendicular to travel. The half-turn ambiguity is deliberate: a bat is
        // symmetric about its center, so the servo may settle either way round and
        // the caller wraps to the nearer of the two.
        return (MathF.Atan2(velocityY, velocityX) + (MathF.PI * 0.5f), true);
    }

    /// <summary>
    /// The angle a <b>pointed</b> tool should hold to lead with its tip while travelling
    /// along <paramref name="velocityX"/>/<paramref name="velocityY"/>: along the direction
    /// of travel, not square to it. The counterpart of <see cref="SwingAngleFor"/>, and the
    /// difference between the two is the difference between a bat and a sword — a bat
    /// presents its barrel, a sword presents its point.
    ///
    /// <para>Unlike the swing angle there is <b>no half-turn ambiguity to fold out</b>: a
    /// blade reversed is a blade held by the wrong end, so the caller must use the plain
    /// wrapped error and never <see cref="SymmetricError"/>, or the sword would happily
    /// settle hilt-first.</para>
    ///
    /// <para>Below <paramref name="minimumSpeed"/> there is no direction to speak of and
    /// the caller must hold its previous angle, which <c>hasTarget = false</c> reports.</para>
    /// </summary>
    public static (float Angle, bool HasTarget) ThrustAngleFor(
        float velocityX,
        float velocityY,
        float minimumSpeed)
    {
        if (!float.IsFinite(velocityX) || !float.IsFinite(velocityY) ||
            !IsFiniteNonNegative(minimumSpeed))
        {
            return (0.0f, false);
        }

        float speedSquared = (velocityX * velocityX) + (velocityY * velocityY);
        if (speedSquared <= minimumSpeed * minimumSpeed)
        {
            return (0.0f, false);
        }

        return (MathF.Atan2(velocityY, velocityX), true);
    }

    /// <summary>
    /// Folds the half-turn symmetry of a two-ended tool into the error, so a bat
    /// that is "upside down" is already aligned and never spins 180° to prove it.
    /// </summary>
    public static float SymmetricError(float targetAngle, float currentAngle)
    {
        float error = HangFrame.WrapAngle(targetAngle - currentAngle);
        if (error > MathF.PI * 0.5f)
        {
            return error - MathF.PI;
        }

        if (error < -MathF.PI * 0.5f)
        {
            return error + MathF.PI;
        }

        return error;
    }

    private static bool IsFinitePositive(float value) =>
        float.IsFinite(value) && value > 0.0f;

    private static bool IsFiniteNonNegative(float value) =>
        float.IsFinite(value) && value >= 0.0f;
}
