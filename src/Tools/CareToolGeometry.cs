using DesktopBuddy.Domain.Tools;
using Godot;

namespace DesktopBuddy.Tools;

/// <summary>
/// Where the business end of each care tool sits relative to the pointer hotspot. The
/// simulation's contact test and both presentations read these same numbers, so the tickle
/// zone lands under the plume the player can see instead of under the cursor
/// (owner instruction 2026-08-19).
/// </summary>
public static class CareToolGeometry
{
    public const float StickLength = 34.0f;
    public const float FerruleLength = 5.0f;

    /// <summary>Length of the vane above the ferrule.</summary>
    public const float PlumeLength = 44.0f;

    /// <summary>Half-width of the vane at its widest.</summary>
    public const float PlumeHalfWidth = 11.0f;

    /// <summary>
    /// Where the feather points before the player has moved the pointer far enough to steer it:
    /// up and to the left, out of the way of the cursor arrow.
    /// </summary>
    public const float RestAngle = -2.836f;

    /// <summary>Pointer hotspot to the player's grip on the stick, in world pixels.</summary>
    public static readonly Vector2 GripOffset = new(2.0f, 2.0f);

    /// <summary>
    /// The feather steers with pointer travel like every other cursor tool. Heavier than the
    /// pistol's aim and slower to turn — it is a long stick held at one end, not a barrel
    /// (owner instruction 2026-08-19).
    /// </summary>
    public static readonly CursorAimConstants FeatherAim = new(
        SmoothingHalfLifeTicks: 16.0f,
        MinimumAimSpeed: 0.30f,
        MaxTurnDegreesPerTick: 5.0f,
        DegreesPerWheelStep: 5.0f,
        MaximumOffsetDegrees: 0.0f);

    /// <summary>Grip to the middle of the vane, along the stick.</summary>
    public static float PlumeCentreDistance =>
        StickLength + FerruleLength + (PlumeLength * 0.42f);

    /// <summary>
    /// Pointer to the centre of the vane for a stick pointing along <paramref name="angle"/>.
    /// The presentation's sway spring is deliberately excluded: contact must not depend on a
    /// rendering flourish, and the vane is wide enough that a few degrees of sway stay inside it.
    /// </summary>
    public static Vector2 TickleContactOffset(float angle) =>
        GripOffset + (Vector2.FromAngle(angle) * PlumeCentreDistance);
}
