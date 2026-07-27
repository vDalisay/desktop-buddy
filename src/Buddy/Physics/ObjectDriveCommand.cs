using DesktopBuddy.Objects;
using Godot;

namespace DesktopBuddy.Buddy.Physics;

/// <summary>Runtime-only object command consumed by ActiveDriveComponent.</summary>
public enum ObjectDriveAction
{
    None,

    /// <summary>Extend the hands minimally toward an incoming object and wait for contact.</summary>
    Catch,

    /// <summary>Dip the body and lower the hands to scoop a resting object off the ground.</summary>
    Scoop,

    /// <summary>Keep the hands at the carry pose; the object is attached, not driven.</summary>
    Hold,

    /// <summary>Draw the hand back before the return throw releases.</summary>
    ThrowWindup,

    Toss,
    Discard,
    Drop,
}

/// <summary>
/// Bounded physical command for a tracked loose object. The domain lifecycle carries only
/// runtime IDs; this runtime payload resolves that ID to the live Godot body for the drive
/// worker.
///
/// <para>
/// There is deliberately no object spring here. A held object is <b>attached</b> — frozen
/// kinematic and placed at the hand socket — and an unheld one is never pulled toward the
/// buddy. Springing the object toward a hold centre is what made objects float to the buddy
/// instead of the buddy catching them (owner correction 2026-07-26).
/// </para>
/// </summary>
public readonly record struct ObjectDriveCommand(
    ObjectDriveAction Action,
    LooseObjectBody? Body,
    Vector2 LeftHandTarget,
    Vector2 RightHandTarget,
    /// <summary>
    /// Launch velocity in px/s, assigned directly rather than applied as an impulse. A release
    /// unfreezes the body on the same tick, and an impulse queued against a body that has just
    /// left its frozen state is discarded — which is why thrown objects simply dropped.
    /// </summary>
    Vector2 ReleaseVelocity,
    float HandStiffness,
    float HandDamping,
    float MaximumHandForce,
    /// <summary>Bounded downward force applied to torso and head during a scoop dip.</summary>
    float DipForce = 0.0f)
{
    public static ObjectDriveCommand None => default;
    public bool Active => Action != ObjectDriveAction.None && Body is not null;

    /// <summary>True while the hands should be driven toward their targets.</summary>
    public bool DrivesHands => Action is
        ObjectDriveAction.Catch or
        ObjectDriveAction.Scoop or
        ObjectDriveAction.Hold or
        ObjectDriveAction.ThrowWindup;

    /// <summary>True for the one-shot release actions that apply an impulse.</summary>
    public bool Releases => Action is
        ObjectDriveAction.Toss or ObjectDriveAction.Discard;
}
