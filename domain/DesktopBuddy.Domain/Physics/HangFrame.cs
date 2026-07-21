using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>
/// Rest-frame and current-world directions from the grabbed point toward the
/// puppet's center of mass. A valid result is the absolute rotation that maps
/// the rest direction onto the actual direction.
/// </summary>
public readonly record struct HangFrameInput(
    Vector2 RestDirection,
    Vector2 ActualDirection);

/// <summary>Allocation-free hang-frame evaluation result.</summary>
public readonly record struct HangFrameResult(float Angle, bool IsValid);

/// <summary>
/// Derives the passive puppet frame implied by a grabbed part and the current
/// center of mass. The one-pixel guard avoids unstable angles when either
/// direction is effectively degenerate.
/// </summary>
public static class HangFrame
{
    private const float MinimumDirectionLength = 1.0f;
    private const float MinimumDirectionLengthSquared =
        MinimumDirectionLength * MinimumDirectionLength;

    public static HangFrameResult Evaluate(in HangFrameInput input)
    {
        if (!IsFinite(input.RestDirection) || !IsFinite(input.ActualDirection) ||
            input.RestDirection.LengthSquared() < MinimumDirectionLengthSquared ||
            input.ActualDirection.LengthSquared() < MinimumDirectionLengthSquared)
        {
            return new HangFrameResult(0.0f, false);
        }

        float restAngle = MathF.Atan2(input.RestDirection.Y, input.RestDirection.X);
        float actualAngle = MathF.Atan2(input.ActualDirection.Y, input.ActualDirection.X);
        return new HangFrameResult(WrapAngle(actualAngle - restAngle), true);
    }

    public static float WrapAngle(float angle)
    {
        if (!float.IsFinite(angle))
        {
            return 0.0f;
        }

        while (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }

        while (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        return angle;
    }

    private static bool IsFinite(Vector2 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y);
}
