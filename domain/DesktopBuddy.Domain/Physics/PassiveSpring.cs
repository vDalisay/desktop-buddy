using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Immutable input for one vector spring link. Positions and velocities are
/// sampled at the configured local anchors. The rest offset is already rotated
/// into world space by the Godot adapter.
/// </summary>
public readonly record struct PassiveSpringInput(
    Vector2 ActualOffset,
    Vector2 RelativeVelocity,
    Vector2 RestOffset,
    float Stiffness,
    float Damping,
    float MaximumDistance,
    float LimitStiffness,
    float MaximumForce);

/// <summary>Allocation-free result for one passive structural link.</summary>
public readonly record struct PassiveSpringResult(
    Vector2 ForceOnA,
    float Separation,
    float Strain,
    bool LimitActive,
    bool ForceClamped);

/// <summary>
/// Pure vector spring/damper/soft-limit calculation shared by runtime physics
/// and Godot-free unit tests. The caller applies <see cref="PassiveSpringResult.ForceOnA"/>
/// to endpoint A and its exact negative to endpoint B.
/// </summary>
public static class PassiveSpring
{
    private const float Epsilon = 0.00001f;

    public static PassiveSpringResult Evaluate(in PassiveSpringInput input)
    {
        Vector2 displacementError = input.ActualOffset - input.RestOffset;
        Vector2 force = (displacementError * input.Stiffness) +
                        (input.RelativeVelocity * input.Damping);

        float separation = input.ActualOffset.Length();
        bool limitActive = separation > input.MaximumDistance && separation > Epsilon;
        if (limitActive)
        {
            float excess = separation - input.MaximumDistance;
            force += (input.ActualOffset / separation) * (excess * input.LimitStiffness);
        }

        bool forceClamped = false;
        float forceLengthSquared = force.LengthSquared();
        float maximumForceSquared = input.MaximumForce * input.MaximumForce;
        if (forceLengthSquared > maximumForceSquared && forceLengthSquared > Epsilon)
        {
            force *= input.MaximumForce / MathF.Sqrt(forceLengthSquared);
            forceClamped = true;
        }

        float strain = input.MaximumDistance > Epsilon
            ? separation / input.MaximumDistance
            : 0.0f;

        return new PassiveSpringResult(force, separation, strain, limitActive, forceClamped);
    }
}
