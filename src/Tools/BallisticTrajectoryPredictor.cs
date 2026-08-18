using System;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Deterministic sampled 2D ballistic prediction used by every pullback launchable, including
/// Baseball and Grenade. It intentionally mirrors the project's fixed-tick integration order:
/// gravity, then linear damping, then position. The predictor has no collision promises; it is
/// only the same authored time horizon shown by the launch guide.
/// </summary>
public static class BallisticTrajectoryPredictor
{
    public readonly record struct Input(
        Vector2 Start,
        Vector2 InitialVelocity,
        float Gravity,
        float LinearDamp,
        float FixedStepSeconds)
    {
        public bool IsValid =>
            Start.IsFinite() && InitialVelocity.IsFinite() &&
            float.IsFinite(Gravity) && float.IsFinite(LinearDamp) && LinearDamp >= 0.0f &&
            float.IsFinite(FixedStepSeconds) && FixedStepSeconds > 0.0f;
    }

    public static Vector2 Predict(in Input input, float seconds)
    {
        if (!input.IsValid || !float.IsFinite(seconds) || seconds <= 0.0f)
            return input.Start;

        float remaining = seconds;
        Vector2 position = input.Start;
        Vector2 velocity = input.InitialVelocity;
        while (remaining > 0.0f)
        {
            float step = MathF.Min(input.FixedStepSeconds, remaining);
            velocity.Y += input.Gravity * step;
            velocity *= 1.0f / (1.0f + input.LinearDamp * step);
            position += velocity * step;
            remaining -= step;
        }
        return position;
    }

    /// <summary>
    /// Fills equally spaced samples from 1/N through the full horizon. Each point is evaluated
    /// from the same initial state rather than chaining rounded segment endpoints, so changing
    /// PredictionSegments changes guide density only, never the predicted landing point.
    /// </summary>
    public static void Sample(
        in Input input,
        float horizonSeconds,
        Span<Vector2> destination)
    {
        if (destination.Length == 0)
            return;

        for (int index = 0; index < destination.Length; index++)
        {
            float seconds = horizonSeconds * (index + 1) / destination.Length;
            destination[index] = Predict(input, seconds);
        }
    }
}
