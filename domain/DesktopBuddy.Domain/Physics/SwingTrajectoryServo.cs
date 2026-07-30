using System;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Immutable input for the servo that drives a scripted swing arc
/// (`docs/M5_TASK4_HOME_RUN_BAT_FEEL_PLAN.md` §4.6). Unlike
/// <see cref="AlignmentTorqueInput"/> this carries a *commanded* angular
/// velocity as well as an angle, because the arc is a trajectory being tracked
/// rather than a target being settled onto.
/// </summary>
public readonly record struct SwingTrajectoryServoInput(
    float AngleError,             // targetAngle - currentAngle, in radians, any winding
    float AngularVelocity,        // the body's current spin
    float TargetAngularVelocity,  // the feed-forward the arc commands right now
    float Stiffness,
    float Damping,
    float MaximumTorque);

/// <summary>Allocation-free result for one swing-servo tick.</summary>
public readonly record struct SwingTrajectoryServoResult(
    float Torque,
    float WrappedError,     // the shortest-way-around angle error the torque was built from
    float VelocityError,    // target spin minus actual spin
    bool IsValid,
    bool WasClamped);

/// <summary>
/// Pure PD servo for a moving angle target with a nonzero commanded velocity.
///
/// The one difference from <see cref="AlignmentTorque"/> is the whole point of
/// this type: the damping term opposes the *velocity error*, not the absolute
/// angular velocity. An ordinary alignment servo handed a sweeping target damps
/// against every rad/s the body has, so it fights the very swing it was told to
/// perform and the bat arrives slow — the commanded speed would silently never
/// be reached, and every measured tip-speed envelope built on top of it would be
/// measuring the servo's saturation instead of the charge. Here, a body already
/// travelling at the commanded rate is left alone and only deviation is
/// corrected.
///
/// The angle error is wrapped so an arc crossing ±π takes the short way, and a
/// non-finite input yields zero torque rather than poisoning the body with NaN.
/// </summary>
public static class SwingTrajectoryServo
{
    public static SwingTrajectoryServoResult Evaluate(in SwingTrajectoryServoInput input)
    {
        if (!float.IsFinite(input.AngleError) ||
            !float.IsFinite(input.AngularVelocity) ||
            !float.IsFinite(input.TargetAngularVelocity) ||
            !IsFiniteNonNegative(input.Stiffness) ||
            !IsFiniteNonNegative(input.Damping) ||
            !IsFinitePositive(input.MaximumTorque))
        {
            return new SwingTrajectoryServoResult(0.0f, 0.0f, 0.0f, false, false);
        }

        float wrappedError = HangFrame.WrapAngle(input.AngleError);
        float velocityError = input.TargetAngularVelocity - input.AngularVelocity;
        float rawTorque = (wrappedError * input.Stiffness) + (velocityError * input.Damping);
        if (!float.IsFinite(rawTorque))
        {
            return new SwingTrajectoryServoResult(0.0f, wrappedError, velocityError, false, false);
        }

        float torque = Math.Clamp(rawTorque, -input.MaximumTorque, input.MaximumTorque);
        return new SwingTrajectoryServoResult(
            torque, wrappedError, velocityError, true, torque != rawTorque);
    }

    /// <summary>
    /// The centripetal force a tether must supply to hold a body's pivot still
    /// while the body rotates about it at <paramref name="angularVelocity"/>:
    /// <c>m · ω² · r</c>, where <paramref name="pivotToCenterOfMass"/> is the
    /// distance from the held point to the centre of mass.
    ///
    /// This exists so the swing's force cap can be *checked* rather than
    /// guessed. A handle pivot is not merely a rotation constraint — at full
    /// charge the bat's own spin pulls on the grip roughly an order of magnitude
    /// harder than the ordinary follow tether ever does, and a cap sized for
    /// following lets the "pivot" be dragged bodily across the room while the
    /// tip never reaches its commanded speed (§7).
    /// </summary>
    public static float PivotHoldForce(float mass, float angularVelocity, float pivotToCenterOfMass)
    {
        if (!IsFinitePositive(mass) ||
            !float.IsFinite(angularVelocity) ||
            !IsFiniteNonNegative(pivotToCenterOfMass))
        {
            return 0.0f;
        }

        float force = mass * angularVelocity * angularVelocity * pivotToCenterOfMass;
        return float.IsFinite(force) ? force : 0.0f;
    }

    private static bool IsFinitePositive(float value) =>
        float.IsFinite(value) && value > 0.0f;

    private static bool IsFiniteNonNegative(float value) =>
        float.IsFinite(value) && value >= 0.0f;
}
