using System;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Physical inputs for the bounded restoring torque of a hanging puppet frame.
/// </summary>
public readonly record struct PendulumTorqueInput(
    float AngleError,
    float TotalMass,
    float ArmLength,
    float GravityGain,
    float AngularVelocity,
    float SwingDamping,
    float MaximumTorque);

/// <summary>Allocation-free pendulum torque evaluation result.</summary>
public readonly record struct PendulumTorqueResult(
    float Torque,
    bool IsValid,
    bool WasClamped);

/// <summary>
/// Evaluates an underdamped gravity-style restoring torque. Unlike a linear
/// position servo, the sine response permits momentum and overshoot around the
/// equilibrium frame while retaining a hard safety bound.
/// </summary>
public static class PendulumTorque
{
    public static PendulumTorqueResult Evaluate(in PendulumTorqueInput input)
    {
        if (!float.IsFinite(input.AngleError) ||
            !IsFinitePositive(input.TotalMass) ||
            !IsFinitePositive(input.ArmLength) ||
            !IsFiniteNonNegative(input.GravityGain) ||
            !float.IsFinite(input.AngularVelocity) ||
            !IsFiniteNonNegative(input.SwingDamping) ||
            !IsFinitePositive(input.MaximumTorque))
        {
            return new PendulumTorqueResult(0.0f, false, false);
        }

        float wrappedError = HangFrame.WrapAngle(input.AngleError);
        float rawTorque = (input.GravityGain * input.TotalMass * input.ArmLength *
                           MathF.Sin(wrappedError)) -
                          (input.AngularVelocity * input.SwingDamping);
        if (!float.IsFinite(rawTorque))
        {
            return new PendulumTorqueResult(0.0f, false, false);
        }

        float torque = Math.Clamp(rawTorque, -input.MaximumTorque, input.MaximumTorque);
        return new PendulumTorqueResult(torque, true, torque != rawTorque);
    }

    private static bool IsFinitePositive(float value) =>
        float.IsFinite(value) && value > 0.0f;

    private static bool IsFiniteNonNegative(float value) =>
        float.IsFinite(value) && value >= 0.0f;
}
