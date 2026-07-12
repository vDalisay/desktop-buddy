using System;
using System.Numerics;

namespace DesktopBuddy.Domain.Physics;

/// <summary>Gait shape tuning (all lengths in world px; lean is a fraction).</summary>
public readonly record struct GaitTuning(
    float StepLength,   // forward/back reach of a foot within its cycle
    float StepLift,     // peak height the swing foot clears the floor
    float TorsoBob,     // vertical torso bob amplitude
    float TorsoLean);   // forward lean fraction applied in the walk direction

/// <summary>
/// One tick of gait, resolved to per-foot target offsets (relative to each foot's
/// rest point) plus a torso bob/lean bias. The two feet run 180 deg out of phase:
/// one swings (lifts and reaches forward) while the other is planted and pushes the
/// body forward. Pure math (RAGDOLL_AND_GAMEPLAY_SPEC.md 3.3 "limb-target forces"):
/// the Godot drive turns these targets into bounded spring forces; nothing here
/// touches a body. This is the reference's "oscillating leg impulses" walk
/// (REFERENCE_RESEARCH.md 2), not an articulated IK chain.
/// </summary>
public readonly record struct GaitSample(
    Vector2 LeftFootOffset,
    Vector2 RightFootOffset,
    bool LeftIsStance,
    bool RightIsStance,
    float TorsoBobOffset,
    float TorsoLeanOffset);

public static class GaitCycle
{
    /// <summary>
    /// Sample the gait at <paramref name="phase"/> in [0,1) for a walk in
    /// <paramref name="direction"/> (-1 left, +1 right, 0 idle -> zero offsets).
    /// The left foot swings over the first half of the cycle, the right over the
    /// second, so support alternates.
    /// </summary>
    public static GaitSample Sample(float phase, float direction, in GaitTuning tuning)
    {
        direction = Math.Clamp(direction, -1.0f, 1.0f);
        if (direction == 0.0f)
        {
            return default; // idle: no offsets, both feet nominally planted
        }

        phase -= MathF.Floor(phase); // wrap into [0,1)

        // Left leads; right is half a cycle behind.
        (Vector2 left, bool leftStance) = Foot(phase, direction, tuning);
        (Vector2 right, bool rightStance) = Foot(phase + 0.5f, direction, tuning);

        // Torso dips twice per cycle (once per footfall) and leans into travel.
        float bob = -MathF.Abs(MathF.Sin(phase * MathF.PI * 2.0f)) * tuning.TorsoBob;
        float lean = direction * tuning.TorsoLean;
        return new GaitSample(left, right, leftStance, rightStance, bob, lean);
    }

    private static (Vector2 Offset, bool Stance) Foot(float phase, float direction, in GaitTuning tuning)
    {
        phase -= MathF.Floor(phase); // [0,1): [0,0.5) swing, [0.5,1) stance
        float half = tuning.StepLength * 0.5f;
        if (phase < 0.5f)
        {
            // Swing: reach from back (-half) to front (+half), lifting in an arc.
            float f = phase / 0.5f;               // 0 -> 1
            float x = direction * (f - 0.5f) * tuning.StepLength;
            float y = -MathF.Sin(f * MathF.PI) * tuning.StepLift; // up is negative Y
            return (new Vector2(x, y), false);
        }

        // Stance: planted, driving from front (+half) back to (-half) to push the body forward.
        float s = (phase - 0.5f) / 0.5f;          // 0 -> 1
        float sx = direction * (0.5f - s) * tuning.StepLength;
        return (new Vector2(sx, 0.0f), true);
    }
}
