using System;

namespace DesktopBuddy.Domain.Presentation;

/// <summary>
/// Pure response curve used by velocity-aligned buddy parts. Ordinary desktop movement gets a
/// restrained fraction of the tracked angle, while high-speed throws/impacts smoothly recover the
/// full directional response. Keeping the curve engine-free makes the user-testing rule regression-testable.
/// </summary>
public static class VelocityRotationResponse
{
    public static float Scale(
        float speed,
        float deadband,
        float ordinaryScale,
        float fullResponseSpeed)
    {
        if (!float.IsFinite(speed) || speed < 0.0f)
            speed = 0.0f;
        if (!float.IsFinite(deadband) || deadband < 0.0f)
            throw new ArgumentOutOfRangeException(nameof(deadband));
        if (!float.IsFinite(ordinaryScale) || ordinaryScale < 0.0f || ordinaryScale > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(ordinaryScale));
        if (!float.IsFinite(fullResponseSpeed) || fullResponseSpeed <= deadband)
            throw new ArgumentOutOfRangeException(nameof(fullResponseSpeed));

        float response = Math.Clamp(
            (speed - deadband) / (fullResponseSpeed - deadband),
            0.0f,
            1.0f);
        response *= response;
        return ordinaryScale + ((1.0f - ordinaryScale) * response);
    }
}
