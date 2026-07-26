using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Runtime-only object command consumed by ActiveDriveComponent.</summary>
public enum ObjectDriveAction
{
    None,
    Catch,
    Hold,
    Toss,
    Discard,
    Drop,
}

/// <summary>
/// Bounded physical command for a tracked loose object. The domain lifecycle
/// carries only runtime IDs; this runtime payload resolves that ID to the live
/// Godot body for the drive worker.
/// </summary>
public readonly record struct ObjectDriveCommand(
    ObjectDriveAction Action,
    LooseObjectBody? Body,
    Vector2 LeftHandTarget,
    Vector2 RightHandTarget,
    Vector2 ObjectTarget,
    Vector2 ReleaseImpulse,
    float HandStiffness,
    float HandDamping,
    float MaximumHandForce,
    float ObjectStiffness,
    float ObjectDamping,
    float MaximumObjectForce)
{
    public static ObjectDriveCommand None => default;
    public bool Active => Action != ObjectDriveAction.None && Body is not null;
}
