using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

/// <summary>
/// Read-only observability for the gameplay sampling layer. The references and resolved
/// values deliberately remain on BuddyVisualPresenter rather than leaking into the rig view.
/// </summary>
public readonly record struct BuddyVisualPresenterSamplingSnapshot(
    BuddyRoot Buddy,
    BuddyPosePipeline? PosePipeline,
    FacingController? Facing,
    ActivityAnimator? Activities,
    HeadLookAtComponent? HeadLookAt,
    ImpactVisualOffsetComponent? ImpactVisualOffset,
    float PerformanceWeight,
    float BodyYawRadians,
    float HeadLookYawRadians,
    float HeadLookPitchRadians,
    float ActivityHeadYawRadians,
    bool PresentationHeld,
    Vector2 RenderedHeadPosition,
    Vector2 RenderedTorsoPosition);

public partial class BuddyVisualPresenter
{
    public BuddyVisualPresenterSamplingSnapshot CaptureSamplingSnapshot()
    {
        if (!IsInitialized)
            throw new System.InvalidOperationException(
                "BuddyVisualPresenter used before initialization.");

        return new BuddyVisualPresenterSamplingSnapshot(
            Buddy,
            PosePipeline,
            Facing,
            Activities,
            HeadLookAt,
            ImpactVisualOffset,
            _performanceWeight,
            _yawRadians,
            _headLookYawRadians,
            _headLookPitchRadians,
            _activityHeadYawRadians,
            _presentationHeld,
            _rendered[(int)BuddyPartId.Head].Position,
            _rendered[(int)BuddyPartId.Torso].Position);
    }
}
