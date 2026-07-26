using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>
/// One resolved bounded actuation request. Behavior components choose intent;
/// <see cref="ActiveDriveComponent"/> alone applies locomotion, jump, resistance,
/// defensive hand-target forces, and behavior-backed physical reaches.
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
    float GuardAbsorption,
    bool StationaryActive,
    bool ActivityHandReachActive,
    float ActivityReachLift,
    Vector2 LeftActivityHandTarget,
    Vector2 RightActivityHandTarget,
    // Panic hands are deliberately their own seam rather than reusing GuardActive: the guard
    // flag also means "this hand contact absorbs glove damage" (see InteractionDamageComponent),
    // and a frightened buddy flailing at a tether must not silently change damage scoring.
    // Gated per hand: a grabbed hand belongs to the tether, so driving a spring at it would
    // have the buddy fighting its own held limb instead of reaching with the free one.
    bool PanicLeftHandActive,
    bool PanicRightHandActive,
    Vector2 LeftPanicHandTarget,
    Vector2 RightPanicHandTarget,
    ObjectDriveCommand ObjectCommand = default);
