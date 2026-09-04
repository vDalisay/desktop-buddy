using DesktopBuddy.Buddy.Physics;
using Godot;

namespace DesktopBuddy.Buddy.Presentation3D;

public partial class BuddyVisualRigView
{
    /// <summary>
    /// Editor-preview counterpart of the runtime presenter's yaw/lane order. Body positions are
    /// yawed around the torso first and their artificial draw-order depth lane is added afterwards.
    /// Rotating a preview root that already contains lane Z offsets turns those offsets into screen
    /// X at 90 degrees and visually tears the buddy apart; this keeps the lane camera-only.
    /// </summary>
    internal Vector3 EditorLanePosition(
        BuddyPartId partId,
        Vector2 worldPosition,
        float yawRadians,
        Vector2 torsoPosition)
    {
        int index = CheckedPartIndex(partId);
        EnsureInitialized();
        return ResolveLanePosition(
            worldPosition,
            _partDefinitions[index].DepthOffset,
            yawRadians,
            torsoPosition);
    }
}
