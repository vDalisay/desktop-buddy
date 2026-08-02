using System;
using System.Threading;
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
public readonly struct BuddyVisualPoseFrame
{
    private static long _createdCount;

    public BuddyVisualPoseFrame(
        BuddyVisualPartPose head,
        BuddyVisualPartPose torso,
        BuddyVisualPartPose leftHand,
        BuddyVisualPartPose rightHand,
        BuddyVisualPartPose leftFoot,
        BuddyVisualPartPose rightFoot,
        float bodyYawRadians,
        FaceRenderState? faceState,
        string fallbackFace,
        float fallbackFaceRotation)
    {
        ArgumentNullException.ThrowIfNull(fallbackFace);
        Sequence = Interlocked.Increment(ref _createdCount);
        Head = head;
        Torso = torso;
        LeftHand = leftHand;
        RightHand = rightHand;
        LeftFoot = leftFoot;
        RightFoot = rightFoot;
        BodyYawRadians = bodyYawRadians;
        FaceState = faceState ?? ResolveFallbackState(fallbackFace);
        FallbackFace = fallbackFace;
        FallbackFaceRotation = fallbackFaceRotation;
    }

    /// <summary>Monotonic process-local frame identity used by scenarios and diagnostics.</summary>
    public long Sequence { get; }
    public BuddyVisualPartPose Head { get; }
    public BuddyVisualPartPose Torso { get; }
    public BuddyVisualPartPose LeftHand { get; }
    public BuddyVisualPartPose RightHand { get; }
    public BuddyVisualPartPose LeftFoot { get; }
    public BuddyVisualPartPose RightFoot { get; }
    public float BodyYawRadians { get; }
    public FaceRenderState? FaceState { get; }
    public string FallbackFace { get; }
    public float FallbackFaceRotation { get; }

    public static long CreatedCount => Interlocked.Read(ref _createdCount);

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

    private static FaceRenderState? ResolveFallbackState(string fallbackFace)
    {
        if (string.IsNullOrEmpty(fallbackFace) ||
            !FaceExpressionCatalog.TryResolve(fallbackFace, out FaceFeaturePose pose))
        {
            return null;
        }

        return FaceComposer.Compose(
            pose,
            blinkClosed: false,
            chewActive: false,
            chewFrame: 0,
            faceSuppressed: false,
            pupilX: 0.0f,
            pupilY: 0.0f);
    }
}
