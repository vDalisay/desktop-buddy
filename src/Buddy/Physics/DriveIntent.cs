using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// One resolved bounded actuation request. Behavior components choose intent;
/// <see cref="ActiveDriveComponent"/> alone applies locomotion, jump, resistance,
/// and defensive hand-target forces.
/// </summary>
public readonly record struct DriveIntent(
    float WalkDirection,
    float LocomotionScale,
    bool JumpRequested,
    float JumpDirection,
    float JumpScale,
    float JumpHorizontalRatio,
    float ResistanceDirection,
    float ResistanceStrength,
    bool GuardActive,
    Vector2 LeftGuardTarget,
    Vector2 RightGuardTarget,
    float GuardStiffness,
    float GuardDamping,
    float GuardMaximumForce,
    float GuardAbsorption);
