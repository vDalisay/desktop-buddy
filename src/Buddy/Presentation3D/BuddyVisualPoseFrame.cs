using System;
using DesktopBuddy.Buddy.Physics;
using DesktopBuddy.Domain.Presentation;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// One resolved visual part pose. The rendered 2D sample remains available so connector
/// geometry can be derived by the rig view without reading gameplay or physics state.
/// </summary>
public readonly record struct BuddyVisualPartPose(
    BuddyVisualTransform Rendered,
    Vector3 GlobalPosition,
    Vector3 GlobalRotation);

/// <summary>
/// Immutable frame passed from a gameplay/preview presenter into <see cref="BuddyVisualRigView"/>.
/// It contains resolved visual values only and owns no mutable gameplay authority.
/// </summary>
public readonly record struct BuddyVisualPoseFrame(
    BuddyVisualPartPose Head,
    BuddyVisualPartPose Torso,
    BuddyVisualPartPose LeftHand,
    BuddyVisualPartPose RightHand,
    BuddyVisualPartPose LeftFoot,
    BuddyVisualPartPose RightFoot,
    float BodyYawRadians,
    FaceRenderState? FaceState,
    string FallbackFace,
    float FallbackFaceRotation)
{
    public BuddyVisualPartPose Part(BuddyPartId partId) => partId switch
    {
        BuddyPartId.Head => Head,
        BuddyPartId.Torso => Torso,
        BuddyPartId.LeftHand => LeftHand,
        BuddyPartId.RightHand => RightHand,
        BuddyPartId.LeftFoot => LeftFoot,
        BuddyPartId.RightFoot => RightFoot,
        _ => throw new ArgumentOutOfRangeException(nameof(partId), partId, "Unknown buddy part."),
    };
}
